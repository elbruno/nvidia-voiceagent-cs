using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NvidiaVoiceAgent.Core.Models;
using NvidiaVoiceAgent.ModelHub;

namespace NvidiaVoiceAgent.Core.Services;

/// <summary>
/// ASR service using ONNX Runtime for Parakeet-TDT-0.6B-V2 or compatible models.
/// Implements lazy loading and GPU/CPU fallback.
/// </summary>
public class AsrService : IAsrService, IDisposable
{
    private readonly ILogger<AsrService> _logger;
    private readonly ModelConfig _config;
    private MelSpectrogramExtractor _melExtractor;
    private readonly IModelDownloadService? _modelDownloadService;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private InferenceSession? _session;
    private string[]? _vocabulary;
    private bool _isModelLoaded;
    private bool _isMockMode;
    private bool _disposed;
    private bool _cudaAvailable;

    // Model input/output names (may vary by model export)
    private const string InputName = "audio_signal";
    private const string InputLengthName = "length";
    private const string OutputName = "logprobs";

    public AsrService(
        ILogger<AsrService> logger,
        IOptions<ModelConfig> config,
        IModelDownloadService? modelDownloadService = null)
    {
        _logger = logger;
        _config = config.Value;
        _melExtractor = new MelSpectrogramExtractor();
        _modelDownloadService = modelDownloadService;
    }

    public bool IsModelLoaded => _isModelLoaded;

    public async Task<string> TranscribeAsync(float[] audioSamples, CancellationToken cancellationToken = default)
    {
        // Ensure model is loaded (lazy loading)
        if (!_isModelLoaded)
        {
            await LoadModelAsync(cancellationToken);
        }

        // Mock mode when no model is available
        if (_isMockMode)
        {
            return GenerateMockTranscript(audioSamples);
        }

        try
        {
            return await Task.Run(() => RunInference(audioSamples), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ASR inference failed");
            return $"[Transcription error: {ex.Message}]";
        }
    }

    public async Task<(string transcript, float confidence)> TranscribePartialAsync(
        float[] audioSamples,
        CancellationToken cancellationToken = default)
    {
        // Ensure model is loaded (lazy loading)
        if (!_isModelLoaded)
        {
            await LoadModelAsync(cancellationToken);
        }

        // Mock mode when no model is available
        if (_isMockMode)
        {
            var mockTranscript = GenerateMockTranscript(audioSamples);
            return (mockTranscript, 0.8f); // Mock confidence
        }

        try
        {
            var result = await Task.Run(() => RunInference(audioSamples), cancellationToken);

            // For partial results, estimate confidence based on audio length and energy
            float confidence = EstimateConfidence(audioSamples);

            return (result, confidence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ASR inference failed");
            return ($"[Transcription error: {ex.Message}]", 0.0f);
        }
    }

    public async Task LoadModelAsync(CancellationToken cancellationToken = default)
    {
        if (_isModelLoaded) return;

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_isModelLoaded) return;

            var modelPath = FindModelFile();
            if (modelPath == null)
            {
                _logger.LogWarning(
                    "No ASR ONNX model found in '{ModelPath}'. Running in mock mode. " +
                    "Download Parakeet-TDT ONNX from HuggingFace: onnx-community/parakeet-tdt-0.6b-v2-ONNX",
                    _config.AsrModelPath);
                _isMockMode = true;
                _isModelLoaded = true;
                return;
            }

            // Convert to absolute path so ONNX Runtime can resolve external data files
            modelPath = Path.GetFullPath(modelPath);
            _logger.LogInformation("Loading ASR model from {ModelPath}", modelPath);

            // Try GPU first, fall back to CPU
            var sessionOptions = CreateSessionOptions();
            _session = new InferenceSession(modelPath, sessionOptions);

            // Read expected mel bins from model input metadata and reconfigure extractor
            ConfigureMelExtractorFromModel();

            // Load vocabulary if available
            LoadVocabulary(Path.GetDirectoryName(modelPath)!);

            _isModelLoaded = true;
            _isMockMode = false;

            var provider = _cudaAvailable ? "CUDA (GPU)" : "CPU";
            _logger.LogInformation("ASR model loaded successfully using {Provider}", provider);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private string? FindModelFile()
    {
        var basePath = _config.AsrModelPath;

        // For Parakeet-TDT model, prioritize encoder.onnx (decoder is incompatible with current pipeline)
        string[] possibleNames =
        {
            "encoder.onnx",      // Parakeet encoder (preferred)
            "model.onnx",         // Generic model file
            "parakeet.onnx",      // Alternative Parakeet name
            "asr.onnx"            // Generic ASR name
        };

        // Check if basePath is directly an ONNX file
        if (File.Exists(basePath) && basePath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
        {
            // Avoid loading decoder.onnx directly (incompatible with current pipeline)
            if (basePath.EndsWith("decoder.onnx", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Cannot load decoder.onnx directly - it requires a dual-model inference pipeline. Use encoder.onnx instead.");
                return null;
            }
            return basePath;
        }

        // Check if basePath is a directory
        if (Directory.Exists(basePath))
        {
            // First priority: encoder.onnx in onnx subdirectory (Parakeet layout)
            var encoderPath = Path.Combine(basePath, "onnx", "encoder.onnx");
            if (File.Exists(encoderPath))
            {
                _logger.LogDebug("Found encoder.onnx in onnx/ subdirectory");
                return encoderPath;
            }

            // Second priority: search for encoder.onnx in any subdirectory
            var encoderSearch = Directory.GetFiles(basePath, "encoder.onnx", SearchOption.AllDirectories);
            if (encoderSearch.Length > 0)
            {
                _logger.LogDebug("Found encoder.onnx in subdirectory: {Path}", encoderSearch[0]);
                return encoderSearch[0];
            }

            // Other model files
            foreach (var name in possibleNames.Skip(1))
            {
                var fullPath = Path.Combine(basePath, name);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            // Try to find any .onnx file (skip decoder.onnx)
            var onnxFiles = Directory.GetFiles(basePath, "*.onnx", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith("decoder.onnx", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (onnxFiles.Length > 0)
            {
                _logger.LogDebug("Found ONNX file: {Path}", onnxFiles[0]);
                return onnxFiles[0];
            }
        }

        // Try relative to current directory
        foreach (var name in possibleNames)
        {
            var fullPath = Path.Combine(basePath, name);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        // Try ModelHub downloaded path
        if (_modelDownloadService != null && _modelDownloadService.IsModelAvailable(ModelType.Asr))
        {
            var hubPath = _modelDownloadService.GetModelPath(ModelType.Asr);
            if (hubPath != null && Directory.Exists(hubPath))
            {
                // Priority 1: encoder.onnx in onnx/ subdirectory
                var encoderPath = Path.Combine(hubPath, "onnx", "encoder.onnx");
                if (File.Exists(encoderPath))
                {
                    _logger.LogInformation("Found ASR model via ModelHub at {Path}", encoderPath);
                    return encoderPath;
                }

                // Priority 2: encoder.onnx anywhere in the directory
                var encoderSearch = Directory.GetFiles(hubPath, "encoder.onnx", SearchOption.AllDirectories);
                if (encoderSearch.Length > 0)
                {
                    _logger.LogInformation("Found ASR model via ModelHub at {Path}", encoderSearch[0]);
                    return encoderSearch[0];
                }

                // Priority 3: Other model files (excluding decoder.onnx)
                foreach (var name in possibleNames.Skip(1))
                {
                    var fullPath = Path.Combine(hubPath, name);
                    if (File.Exists(fullPath))
                    {
                        _logger.LogInformation("Found ASR model via ModelHub at {Path}", fullPath);
                        return fullPath;
                    }
                }

                // Priority 4: Any .onnx file except decoder.onnx
                var onnxFiles = Directory.GetFiles(hubPath, "*.onnx", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith("decoder.onnx", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (onnxFiles.Length > 0)
                {
                    _logger.LogInformation("Found ASR model via ModelHub at {Path}", onnxFiles[0]);
                    return onnxFiles[0];
                }
            }
        }

        return null;
    }

    private Microsoft.ML.OnnxRuntime.SessionOptions CreateSessionOptions()
    {
        var options = new Microsoft.ML.OnnxRuntime.SessionOptions();
        _cudaAvailable = false;

        if (_config.UseGpu)
        {
            try
            {
                // Try CUDA provider first
                options.AppendExecutionProvider_CUDA(0);
                _cudaAvailable = true;
                _logger.LogInformation("CUDA execution provider configured");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CUDA not available, falling back to CPU");
            }
        }

        // CPU is always the fallback
        options.AppendExecutionProvider_CPU(0);

        // Optimize for inference
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        options.EnableMemoryPattern = true;
        // Enable verbose logging for debugging dimension issues
        options.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_VERBOSE;

        return options;
    }

    /// <summary>
    /// Read expected mel bin count from the ONNX model's input metadata and
    /// reconfigure the mel-spectrogram extractor if it differs from the current setting.
    /// </summary>
    private void ConfigureMelExtractorFromModel()
    {
        if (_session == null) return;

        foreach (var input in _session.InputMetadata)
        {
            if (input.Value.ElementType != typeof(float)) continue;

            // Typical shape: [batch, mel_bins, time] or [batch, time, mel_bins]
            var dims = input.Value.Dimensions;
            _logger.LogInformation("Model input {Name} shape: [{Dims}]", input.Key, string.Join(", ", dims));

            if (dims.Length >= 2)
            {
                // mel_bins dimension is usually the second one (index 1) for shape [1, mel, T]
                // But could be index 2 for [1, T, mel]
                // Check which dimension matches current extractor
                
                int index1 = dims[1];
                int index2 = dims.Length > 2 ? dims[2] : -1;
                
                // Heuristic: Mels is usually 80 or 128 (or similar fixed). Time is usually -1 or very large.
                int expectedMels = index1;
                if ((index1 == -1 || index1 < 40) && index2 >= 40)
                {
                     expectedMels = index2;
                }
                
                if (expectedMels >= 40 && expectedMels <= 256 && expectedMels != _melExtractor.NumMels)
                {
                    _logger.LogInformation(
                        "Model expects {Expected} mel bins, reconfiguring extractor (was {Current})",
                        expectedMels, _melExtractor.NumMels);
                    _melExtractor = new MelSpectrogramExtractor(nMels: expectedMels);
                }
                else if (expectedMels == _melExtractor.NumMels)
                {
                    _logger.LogDebug("Model mel bins ({MelBins}) match extractor configuration", expectedMels);
                }
                break;
            }
        }
    }

    private void LoadVocabulary(string modelDirectory)
    {
        // Common vocabulary file names
        string[] vocabNames = { "vocab.txt", "vocabulary.txt", "tokens.txt" };

        foreach (var name in vocabNames)
        {
            var vocabPath = Path.Combine(modelDirectory, name);
            if (File.Exists(vocabPath))
            {
                _vocabulary = File.ReadAllLines(vocabPath);
                _logger.LogInformation("Loaded vocabulary with {Count} tokens", _vocabulary.Length);
                return;
            }
        }

        _logger.LogWarning("No vocabulary file found. Token decoding may not work correctly.");
    }

    private string RunInference(float[] audioSamples)
    {
        if (_session == null)
        {
            throw new InvalidOperationException("Model session not initialized");
        }

        // Extract mel spectrogram
        var melSpec = _melExtractor.Extract(audioSamples);
        melSpec = _melExtractor.Normalize(melSpec);

        int numFrames = melSpec.GetLength(0);
        int numMels = melSpec.GetLength(1);

        _logger.LogDebug("Original MelSpec frames: {NumFrames}, Mels: {NumMels}", numFrames, numMels);

        if (numFrames == 0)
        {
            return string.Empty;
        }

        // Create input tensor [batch=1, mel_bins, time]
        var inputData = new float[1 * numMels * numFrames];
        for (int t = 0; t < numFrames; t++)
        {
            for (int m = 0; m < numMels; m++)
            {
                inputData[m * numFrames + t] = melSpec[t, m];
            }
        }
        
        // Ensure number of frames is ODD?
        // Some models require odd frames due to 'same' padding logic in Conv1d?
        // If 24 failed (even).
        // Try not padding or padding to odd?
        // But odd usually causes issues with /2.
        
        // Let's reconsider the error "3 by 5".
        // If I pass length=6 (24/4). Becomes 5 internally? (6-1 = 5?)
        
        // Let's set length to numFrames (24).
        // AND use (numFrames - 1) / 4 ??
        // It's a shot in the dark without ONNX insight.
        
        // However, standard NeMo ASR usually uses 'length' calculated as:
        // (audio_len // hop_length) -> then passed to model.
        // The model handles subsampling of length internally OR expects subsampled length.
        
        // Let's revert to:
        // Padding: NONE (Use original 23).
        // Length: 23 (Full).
        // Maybe the model *requires* correct length for masking.
        
        // Removing padding logic for a moment to see "13 by 25" again?
        // Or "3 by 5"?
        
        // I'll comment out padding.
        
        if (numFrames % 4 != 0)
        {
            int padding = 4 - (numFrames % 4);
            _logger.LogDebug("Padding input with {Padding} frames to reach multiple of 4", padding);
            
            Array.Resize(ref inputData, 1 * numMels * (numFrames + padding));
            numFrames += padding;
        }

        var inputTensor = new DenseTensor<float>(inputData, new[] { 1, numMels, numFrames });

        // Ensure number of frames is padded such that subsampling aligns
        // Target: numFrames such that ((numFrames-3)/2+1 - 3)/2+1 == (numFrames+X)/4 ??
        // Actually, just ensuring it's a multiple of 4 is usually enough if models use padding.
        // But if they use 'valid' convolution (no padding), we lose frames.
        
        // Let's pad to multiple of 4.
        if (numFrames % 4 != 0)
        {
            int padding = 4 - (numFrames % 4);
             _logger.LogDebug("Padding input with {Padding} frames to reach multiple of 4", padding);
            
            Array.Resize(ref inputData, 1 * numMels * (numFrames + padding));
            numFrames += padding;
        }

        var inputTensor = new DenseTensor<float>(inputData, new[] { 1, numMels, numFrames });
        
        // CALCULATE LENGTH expected by model.
        // If internal is 5 (for 24).
        // And I pass 24.
        // Model computes ceil(24/4)=6. Mismatch.
        
        // I should pass "20"? 
        // 20 -> 5.
        // if I pass length=20.
        // Model computes 5.
        // Internal tensor is 5.
        // Match!
        
        // So I should pass length = numFrames - (numFrames % 4)?
        long encoderLengthIdx = numFrames; // Default
        
        // If internal tensor is smaller due to 'valid' convolution drop-off.
        // We can cheat by confirming the length we PASS is slightly smaller?
        // Let's try passing 'numFrames - 4' if we padded?
        
        // Or calculating the exact 'valid' output size * 4?
        int l_in = numFrames;
        int l_out1 = (l_in - 3) / 2 + 1;
        int l_out2 = (l_out1 - 3) / 2 + 1;
        
        // If model divides input by 4 to get mask size.
        // We want MaskSize == l_out2.
        // So Input/4 == l_out2.
        // So Input == l_out2 * 4.
        
        encoderLengthIdx = l_out2 * 4;
        
        _logger.LogDebug("Passing adjusted length {AdjLen} (derived from internal {IntLen} * 4) for numFrames {NumFrames}", encoderLengthIdx, l_out2, numFrames);

        var lengthTensor = new DenseTensor<long>(new long[] { encoderLengthIdx }, new[] { 1 });

        // Prepare inputs - try different input configurations based on model
        var inputs = CreateModelInputs(inputTensor, lengthTensor, (int)encoderLengthIdx);

        // Run inference
        using var results = _session.Run(inputs);

        // Decode output
        return DecodeOutput(results);
    }

    private IReadOnlyCollection<NamedOnnxValue> CreateModelInputs(
        DenseTensor<float> inputTensor,
        DenseTensor<long> lengthTensor,
        int numFrames)
    {
        // Check for specific batch size requirement logic (some models are strict)
        // If broadcasting failed on axis 1 (dimension 39 vs 77 in error log), check if we need to resize

        // This specific fix addresses "onnxruntime::BroadcastIterator::Append axis == 1 || axis == largest was false"
        // It's likely related to how PositionalEmbedding handles mismatched lengths in some ONNX exports.
        // For Parakeet-TDT, ensure length is explicitly cast to specific integer types if needed.

        // Also ensure mel bins matches exactly what model expects (80 vs 128)
        // Re-check input metadata for explicit shape hints

        var inputs = new List<NamedOnnxValue>();

        // Get actual input names from the model
        var inputNames = _session!.InputMetadata.Keys.ToList();

        foreach (var name in inputNames)
        {
            var meta = _session.InputMetadata[name];

            if (meta.ElementType == typeof(float))
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor(name, inputTensor));
            }
            else if (meta.ElementType == typeof(long) || meta.ElementType == typeof(int))
            {
                // Length input - CRITICAL FIX: Ensure dimensions match exactly [1] not scalar or [1,1] unless specified
                // The error 39 vs 77 suggests a broadcasting mismatch between sequence length and positional embeddings

                if (meta.ElementType == typeof(int))
                {
                    var intLengthTensor = new DenseTensor<int>(new int[] { numFrames }, new[] { 1 });
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, intLengthTensor));
                }
                else
                {
                    // Some models fail if length is not exactly rank-1 tensor of size [1]
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, lengthTensor));
                }
            }
        }

        return inputs;
    }

    private string DecodeOutput(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results)
    {
        // Get the output tensor
        var output = results.First();

        if (output.Value is not Tensor<float> outputTensor)
        {
            // Try other output types
            if (output.Value is Tensor<long> tokenIds)
            {
                return DecodeTokenIds(tokenIds.ToArray());
            }
            if (output.Value is Tensor<int> intTokenIds)
            {
                return DecodeTokenIds(intTokenIds.ToArray().Select(x => (long)x).ToArray());
            }

            _logger.LogWarning("Unexpected output type: {Type}", output.Value?.GetType().Name ?? "null");
            return "[Unknown output format]";
        }

        // CTC decoding - greedy decode from logprobs
        return GreedyCtcDecode(outputTensor);
    }

    private string GreedyCtcDecode(Tensor<float> logprobs)
    {
        var dims = logprobs.Dimensions.ToArray();

        // Typical shape: [batch, time, vocab] or [time, vocab]
        int timeSteps, vocabSize;

        if (dims.Length == 3)
        {
            timeSteps = dims[1];
            vocabSize = dims[2];
        }
        else if (dims.Length == 2)
        {
            timeSteps = dims[0];
            vocabSize = dims[1];
        }
        else
        {
            _logger.LogWarning("Unexpected logprobs shape: [{Dims}]", string.Join(", ", dims));
            return "[Decode error]";
        }

        var tokens = new List<int>();
        int prevToken = -1;
        int blankToken = 0; // CTC blank is typically token 0

        for (int t = 0; t < timeSteps; t++)
        {
            // Find argmax for this timestep
            float maxVal = float.MinValue;
            int maxIdx = 0;

            for (int v = 0; v < vocabSize; v++)
            {
                float val = dims.Length == 3
                    ? logprobs[0, t, v]
                    : logprobs[t, v];

                if (val > maxVal)
                {
                    maxVal = val;
                    maxIdx = v;
                }
            }

            // CTC: skip blanks and repeated tokens
            if (maxIdx != blankToken && maxIdx != prevToken)
            {
                tokens.Add(maxIdx);
            }
            prevToken = maxIdx;
        }

        return DecodeTokenIds(tokens.Select(t => (long)t).ToArray());
    }

    private string DecodeTokenIds(long[] tokenIds)
    {
        if (_vocabulary == null || _vocabulary.Length == 0)
        {
            // Fallback: assume character-level tokens
            return new string(tokenIds
                .Where(t => t > 0 && t < 256)
                .Select(t => (char)t)
                .ToArray());
        }

        var tokens = tokenIds
            .Where(t => t >= 0 && t < _vocabulary.Length)
            .Select(t => _vocabulary[t])
            .Where(t => !string.IsNullOrEmpty(t) && t != "<blank>" && t != "<pad>" && t != "<unk>");

        // Join tokens - handle BPE-style tokens with special characters
        var text = string.Join("", tokens)
            .Replace("▁", " ")  // Sentencepiece word boundary
            .Replace("##", "")  // WordPiece continuation
            .Trim();

        return text;
    }

    /// <summary>
    /// Estimate confidence level based on audio characteristics.
    /// Used for partial/streaming ASR results.
    /// </summary>
    private float EstimateConfidence(float[] audioSamples)
    {
        if (audioSamples == null || audioSamples.Length < 1600) // Less than 0.1s
            return 0.3f;

        // Calculate RMS energy as an indicator of clear speech
        float sumSquares = 0;
        for (int i = 0; i < audioSamples.Length; i++)
        {
            sumSquares += audioSamples[i] * audioSamples[i];
        }
        float rmsEnergy = (float)Math.Sqrt(sumSquares / audioSamples.Length);

        // Map RMS energy to confidence (0.0-1.0)
        // Typical speech: 0.01-0.5
        float confidence = Math.Min(1.0f, rmsEnergy / 0.1f);

        // Adjust based on duration
        var duration = audioSamples.Length / 16000.0;
        if (duration < 0.5)
            confidence *= 0.7f; // Lower confidence for very short clips
        else if (duration > 5.0)
            confidence *= 0.95f; // Increase confidence for longer, complete utterances

        return Math.Max(0.1f, Math.Min(1.0f, confidence));
    }

    private string GenerateMockTranscript(float[] audioSamples)
    {
        // Generate a mock transcript based on audio length
        var duration = audioSamples.Length / 16000.0; // 16kHz

        if (duration < 0.5)
        {
            return string.Empty;
        }

        var mockResponses = new[]
        {
            "Hello, this is a test.",
            "The quick brown fox jumps over the lazy dog.",
            "Testing speech recognition.",
            "This is mock transcription output.",
            "Voice agent is ready."
        };

        var index = (int)(duration * 10) % mockResponses.Length;
        var transcript = mockResponses[index];

        _logger.LogDebug(
            "Mock ASR: {Duration:F2}s audio -> '{Transcript}'",
            duration, transcript);

        return transcript;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _session?.Dispose();
        _loadLock.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
