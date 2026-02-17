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
            return "[Transcription error]";
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

        // Try various common ONNX model filenames
        string[] possibleNames =
        {
            "encoder.onnx",
            "model.onnx",
            "parakeet.onnx",
            "asr.onnx"
        };

        // Check if basePath is directly an ONNX file
        if (File.Exists(basePath) && basePath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
        {
            return basePath;
        }

        // Check if basePath is a directory
        if (Directory.Exists(basePath))
        {
            foreach (var name in possibleNames)
            {
                var fullPath = Path.Combine(basePath, name);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            // Try to find any .onnx file
            var onnxFiles = Directory.GetFiles(basePath, "*.onnx", SearchOption.AllDirectories);
            if (onnxFiles.Length > 0)
            {
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
            if (hubPath != null)
            {
                // GetModelPath returns a directory, so find the actual .onnx file
                if (Directory.Exists(hubPath))
                {
                    foreach (var name in possibleNames)
                    {
                        var fullPath = Path.Combine(hubPath, name);
                        if (File.Exists(fullPath))
                        {
                            _logger.LogInformation("Found ASR model via ModelHub at {Path}", fullPath);
                            return fullPath;
                        }
                    }

                    // Try to find any .onnx file in the directory
                    var onnxFiles = Directory.GetFiles(hubPath, "*.onnx", SearchOption.AllDirectories);
                    if (onnxFiles.Length > 0)
                    {
                        _logger.LogInformation("Found ASR model via ModelHub at {Path}", onnxFiles[0]);
                        return onnxFiles[0];
                    }
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
            if (dims.Length >= 2)
            {
                // mel_bins dimension is usually the second one (index 1) for shape [1, mel, T]
                int expectedMels = dims[1];

                // Sanity check: mel bins should be between 40 and 256
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

        var inputTensor = new DenseTensor<float>(inputData, new[] { 1, numMels, numFrames });

        // The encoder's convolutional frontend subsamples the mel spectrogram
        // (stride 2). The ONNX export does not track the length reduction
        // internally, so we compute the post-subsampling length ourselves.
        // Formula: ceil(numFrames / 2) = (numFrames + 1) / 2
        int encoderLength = (numFrames + 1) / 2;
        var lengthTensor = new DenseTensor<long>(new long[] { encoderLength }, new[] { 1 });

        // Prepare inputs - try different input configurations based on model
        var inputs = CreateModelInputs(inputTensor, lengthTensor, encoderLength);

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
                // Length input
                if (meta.ElementType == typeof(int))
                {
                    var intLengthTensor = new DenseTensor<int>(new int[] { numFrames }, new[] { 1 });
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, intLengthTensor));
                }
                else
                {
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
