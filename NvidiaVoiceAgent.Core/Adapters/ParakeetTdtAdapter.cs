using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NvidiaVoiceAgent.Core.Models;
using NvidiaVoiceAgent.Core.Services;

namespace NvidiaVoiceAgent.Core.Adapters;

/// <summary>
/// Model adapter for NVIDIA Parakeet-TDT ASR models.
/// Handles Parakeet-specific preprocessing, padding, and TDT greedy decoding
/// using both encoder and decoder ONNX models.
/// </summary>
public class ParakeetTdtAdapter : IAsrModelAdapter
{
    private readonly ILogger<ParakeetTdtAdapter> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private AsrModelSpecification? _specification;
    private InferenceSession? _encoderSession;
    private InferenceSession? _decoderSession;
    private MelSpectrogramExtractor? _melExtractor;
    private string[]? _vocabulary;
    private IAudioChunkingStrategy? _chunker;
    private IAudioMerger? _merger;
    private bool _isLoaded;
    private bool _disposed;
    private bool _cudaAvailable;
    private bool _chunkingEnabled;

    public string ModelName => _specification?.ModelName ?? "unknown";
    public bool IsLoaded => _isLoaded;

    public ParakeetTdtAdapter(ILogger<ParakeetTdtAdapter> logger)
    {
        _logger = logger;
    }

    public async Task LoadAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        if (_isLoaded) return;

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_isLoaded) return; // Double-check pattern

            _logger.LogInformation("Loading Parakeet-TDT model from {Path}", modelPath);

            // Load model specification
            var specPath = Path.Combine(modelPath, "model_spec.json");
            if (!File.Exists(specPath))
            {
                throw new FileNotFoundException($"Model specification not found: {specPath}");
            }

            var specJson = await File.ReadAllTextAsync(specPath, cancellationToken);
            _specification = JsonSerializer.Deserialize<AsrModelSpecification>(specJson)
                ?? throw new InvalidOperationException("Failed to deserialize model specification");

            _logger.LogInformation("Model specification loaded: {ModelName} ({Type})",
                _specification.ModelName, _specification.ModelType);

            // Configure mel-spectrogram extractor from spec
            var audioConfig = _specification.AudioPreprocessing;
            _melExtractor = new MelSpectrogramExtractor(
                nMels: audioConfig.MelBins,
                nFft: audioConfig.FftSize,
                winLength: audioConfig.WinLength,
                hopLength: audioConfig.HopLength,
                sampleRate: audioConfig.SampleRate,
                fMin: audioConfig.Fmin,
                fMax: audioConfig.Fmax
            );

            _logger.LogDebug("Mel-spectrogram extractor configured: {Mels} mels, {Fft} FFT",
                audioConfig.MelBins, audioConfig.FftSize);

            // Load ONNX model
            var encoderPath = Path.Combine(modelPath,
                _specification.Files.AdditionalFiles?["encoder"]?.ToString() ?? "onnx/encoder.onnx");

            if (!File.Exists(encoderPath))
            {
                throw new FileNotFoundException($"Encoder model not found: {encoderPath}");
            }

            var sessionOptions = CreateSessionOptions();
            _encoderSession = new InferenceSession(encoderPath, sessionOptions);

            _logger.LogInformation("Encoder ONNX session created using {Provider}",
                _cudaAvailable ? "CUDA (GPU)" : "CPU");

            // Load decoder ONNX model (for TDT decoding)
            var decoderFile = _specification.Files.AdditionalFiles?.ContainsKey("decoder") == true
                ? _specification.Files.AdditionalFiles["decoder"]?.ToString()
                : null;
            if (!string.IsNullOrEmpty(decoderFile))
            {
                var decoderPath = Path.Combine(modelPath, decoderFile);
                if (File.Exists(decoderPath))
                {
                    var decoderOptions = CreateSessionOptions();
                    _decoderSession = new InferenceSession(decoderPath, decoderOptions);
                    _logger.LogInformation("Decoder ONNX session created: {Path}", decoderPath);
                }
                else
                {
                    _logger.LogWarning("Decoder model not found: {Path}. TDT decoding unavailable.", decoderPath);
                }
            }

            // Load vocabulary
            var vocabFile = _specification.Decoding.VocabularyFile;
            if (!string.IsNullOrEmpty(vocabFile))
            {
                var vocabPath = Path.Combine(modelPath, vocabFile);
                if (File.Exists(vocabPath))
                {
                    _vocabulary = await File.ReadAllLinesAsync(vocabPath, cancellationToken);
                    _logger.LogInformation("Vocabulary loaded: {Count} tokens", _vocabulary.Length);
                }
                else
                {
                    _logger.LogWarning("Vocabulary file not found: {Path}", vocabPath);
                }
            }

            // Initialize chunking if enabled
            if (_specification.Chunking?.Enabled == true)
            {
                var chunkingConfig = _specification.Chunking;
                _chunker = new OverlappingAudioChunker(
                    chunkingConfig.ChunkSizeSeconds,
                    chunkingConfig.OverlapSeconds,
                    null);
                _merger = new TranscriptMerger(null);
                _chunkingEnabled = true;

                _logger.LogInformation(
                    "Audio chunking enabled: chunk_size={ChunkSize}s, overlap={Overlap}s",
                    chunkingConfig.ChunkSizeSeconds, chunkingConfig.OverlapSeconds);
            }
            else
            {
                _chunkingEnabled = false;
                _logger.LogInformation("Audio chunking disabled");
            }

            _isLoaded = true;
            _logger.LogInformation("Parakeet-TDT adapter loaded successfully");
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public float[,] PrepareInput(object rawData)
    {
        if (_melExtractor == null || _specification == null)
        {
            throw new InvalidOperationException("Model not loaded. Call LoadAsync first.");
        }

        var audioSamples = (float[])rawData;

        // Extract mel-spectrogram
        var melSpec = _melExtractor.Extract(audioSamples);
        melSpec = _melExtractor.Normalize(melSpec);

        return melSpec;
    }

    public async Task<string> InferAsync(float[,] melSpectrogram, CancellationToken cancellationToken = default)
    {
        if (_encoderSession == null || _specification == null)
        {
            throw new InvalidOperationException("Model not loaded. Call LoadAsync first.");
        }

        return await Task.Run(() => RunInference(melSpectrogram), cancellationToken);
    }

    public async Task<string> TranscribeAsync(float[] audioSamples, CancellationToken cancellationToken = default)
    {
        if (!_isLoaded)
        {
            throw new InvalidOperationException("Model not loaded. Call LoadAsync first.");
        }

        // Check if audio is long enough to require chunking
        const int maxFramesBeforeChunking = 6000;  // ~60 seconds at 16kHz
        if (_chunkingEnabled && _chunker != null && _merger != null &&
            audioSamples.Length > maxFramesBeforeChunking * 160)  // Convert frames to samples
        {
            return await TranscribeWithChunkingAsync(audioSamples, cancellationToken);
        }

        // Standard path: process as single chunk
        var melSpec = PrepareInput(audioSamples);
        return await Task.Run(() => RunInference(melSpec), cancellationToken);
    }

    /// <summary>
    /// Transcribe audio using chunking for long-form inputs.
    /// </summary>
    private async Task<string> TranscribeWithChunkingAsync(float[] audioSamples, CancellationToken cancellationToken)
    {
        if (_chunker == null || _merger == null || _specification == null)
        {
            throw new InvalidOperationException("Chunking not properly initialized");
        }

        const int sampleRate = 16000;

        // Split audio into chunks
        var chunks = _chunker.ChunkAudio(audioSamples, sampleRate);

        if (chunks.Length == 0)
        {
            return "[No audio chunks created]";
        }

        if (chunks.Length == 1)
        {
            // Single chunk: process normally (should not happen in this code path)
            var melSpec = PrepareInput(chunks[0].Samples);
            return await Task.Run(() => RunInference(melSpec), cancellationToken);
        }

        // Process each chunk
        _logger.LogInformation("Processing {ChunkCount} chunks with chunking strategy", chunks.Length);
        var transcripts = new string[chunks.Length];

        for (int i = 0; i < chunks.Length; i++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunk = chunks[i];
                _logger.LogDebug("Processing chunk {Index}/{Total}: {SampleCount} samples",
                    i + 1, chunks.Length, chunk.Samples.Length);

                var melSpec = PrepareInput(chunk.Samples);
                transcripts[i] = await Task.Run(() => RunInference(melSpec), cancellationToken);

                _logger.LogDebug("Chunk {Index} transcript: '{Transcript}' ({Length} chars)",
                    i + 1, transcripts[i], transcripts[i]?.Length ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process chunk {Index}", i + 1);
                transcripts[i] = "[Error processing chunk]";
            }
        }

        // Merge transcripts using overlap detection
        string merged = _chunker.MergeTranscripts(transcripts, chunks);

        // Apply deduplication via merger
        float overlapFraction = _specification.Chunking?.OverlapSeconds ?? 2f / (_specification.Chunking?.ChunkSizeSeconds ?? 50f);
        merged = _merger.MergeTranscripts(transcripts, overlapFraction);

        _logger.LogInformation("Chunked transcription complete: {Length} chars", merged.Length);

        return merged;
    }

    public async Task<(string transcript, float confidence)> TranscribeWithConfidenceAsync(
        float[] audioSamples,
        CancellationToken cancellationToken = default)
    {
        var transcript = await TranscribeAsync(audioSamples, cancellationToken);

        // Simple confidence estimation based on audio energy
        float confidence = EstimateConfidence(audioSamples);

        return (transcript, confidence);
    }

    public ModelSpecification GetSpecification()
    {
        return _specification ?? throw new InvalidOperationException("Model not loaded");
    }

    private string RunInference(float[,] melSpectrogram)
    {
        int numFrames = melSpectrogram.GetLength(0);
        int numMels = melSpectrogram.GetLength(1);

        if (numFrames == 0)
        {
            _logger.LogWarning("Empty mel-spectrogram: no audio frames extracted");
            return string.Empty;
        }

        // Validate audio length
        const int minFrames = 5;      // ~50ms minimum
        const int maxFrames = 6000;   // ~60 second maximum
        if (numFrames < minFrames)
        {
            _logger.LogWarning("Audio too short: {Frames} frames (minimum {Min}). Duration: {Duration:F2}ms",
                numFrames, minFrames, numFrames * 10);
            return "[Audio too short for transcription]";
        }
        if (numFrames > maxFrames)
        {
            string chunkingMsg = _chunkingEnabled ? " Enable chunking in model configuration or reduce audio length." : "";
            _logger.LogWarning("Audio too long: {Frames} frames (maximum {Max}). Duration: {Duration:F2}s.{ChunkingMsg}",
                numFrames, maxFrames, numFrames * 0.01, chunkingMsg);
            return $"[Audio too long for transcription (max {maxFrames} frames).{chunkingMsg}]";
        }

        // Apply padding according to specification
        var paddingConfig = _specification!.InputRequirements.Padding;
        int paddedFrames = numFrames;

        if (paddingConfig?.Enabled == true && paddingConfig.Strategy == "multiple_of")
        {
            int multiple = paddingConfig.Value;
            paddedFrames = ((numFrames + multiple - 1) / multiple) * multiple;
            if (paddedFrames != numFrames)
            {
                _logger.LogDebug("Padding frames from {Original} to {Padded} (multiple of {Multiple})",
                    numFrames, paddedFrames, multiple);
            }
        }

        // Create input tensor [batch=1, mel_bins, time] with padding
        var inputData = new float[1 * numMels * paddedFrames];
        for (int t = 0; t < paddedFrames; t++)
        {
            for (int m = 0; m < numMels; m++)
            {
                if (t < numFrames)
                {
                    inputData[m * paddedFrames + t] = melSpectrogram[t, m];
                }
                else
                {
                    inputData[m * paddedFrames + t] = paddingConfig?.PadValue ?? 0f;
                }
            }
        }

        var inputTensor = new DenseTensor<float>(inputData, new[] { 1, numMels, paddedFrames });
        long lengthValue = paddedFrames;

        _logger.LogInformation(
            "Running ASR inference: input_shape=[1, {MelBins}, {TimeFrames}], length_param={Length}",
            numMels, paddedFrames, lengthValue);

        // Create encoder inputs
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("audio_signal", inputTensor),
            NamedOnnxValue.CreateFromTensor(_specification.InputRequirements.LengthParameter?.Name ?? "length",
                new DenseTensor<long>(new long[] { lengthValue }, new[] { 1 }))
        };

        // Run encoder
        using var encoderResults = _encoderSession!.Run(inputs);

        // Get encoder output tensor
        var encoderOutput = encoderResults.First();
        if (encoderOutput.Value is not Tensor<float> encHidden)
        {
            _logger.LogWarning("Unexpected encoder output type: {Type}", encoderOutput.Value?.GetType().Name ?? "null");
            return "[Unknown encoder output format]";
        }

        // Get encoded lengths
        var encodedLengthsOutput = encoderResults.ElementAt(1);
        int encodedTimeSteps;
        if (encodedLengthsOutput.Value is Tensor<long> encLengths)
        {
            encodedTimeSteps = (int)encLengths[0];
        }
        else
        {
            // Fall back to shape-based calculation
            encodedTimeSteps = encHidden.Dimensions[2];
        }

        _logger.LogDebug("Encoder output: [{Dims}], encoded_time={EncTime}",
            string.Join(", ", encHidden.Dimensions.ToArray()), encodedTimeSteps);

        // Route to appropriate decoder
        if (_decoderSession != null && _specification.Decoding.Type == "tdt")
        {
            return GreedyTdtDecode(encHidden, encodedTimeSteps);
        }

        _logger.LogWarning("No TDT decoder available, falling back to raw output decode");
        return "[Decoder not available for TDT model]";
    }

    /// <summary>
    /// Greedy TDT (Token-and-Duration Transducer) decoding using the decoder ONNX model.
    /// </summary>
    private string GreedyTdtDecode(Tensor<float> encoderOutputs, int encodedTimeSteps)
    {
        var decoding = _specification!.Decoding;
        int blankId = decoding.BlankTokenId;  // 1024
        int[] tdtDurations = decoding.TdtDurations ?? [0, 1, 2, 3, 4];
        int numLayers = decoding.PrednetNumLayers;  // 2
        int hiddenSize = decoding.PrednetHiddenSize;  // 640
        int totalClasses = blankId + 1 + tdtDurations.Length;  // 1030

        // Create encoder output tensor for decoder input (reuse the encoder output as-is)
        var encDims = encoderOutputs.Dimensions.ToArray();
        var encData = new float[encDims[0] * encDims[1] * encDims[2]];
        for (int bi = 0; bi < encDims[0]; bi++)
            for (int di = 0; di < encDims[1]; di++)
                for (int ti = 0; ti < encDims[2]; ti++)
                    encData[bi * encDims[1] * encDims[2] + di * encDims[2] + ti] = encoderOutputs[bi, di, ti];
        var encTensor = new DenseTensor<float>(encData, encDims);

        // Initialize LSTM states to zeros
        var lstmH = new DenseTensor<float>(new float[numLayers * 1 * hiddenSize], new[] { numLayers, 1, hiddenSize });
        var lstmC = new DenseTensor<float>(new float[numLayers * 1 * hiddenSize], new[] { numLayers, 1, hiddenSize });

        var decodedTokens = new List<int>();
        int t = 0;
        int lastLabel = blankId;
        int maxIterations = encodedTimeSteps * 10;  // Safety limit
        int iterations = 0;

        while (t < encodedTimeSteps && iterations < maxIterations)
        {
            iterations++;

            // Create targets tensor [batch=1, seq_len=1]
            var targets = new DenseTensor<int>(new int[] { lastLabel }, new[] { 1, 1 });

            // Run decoder
            var decoderInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("encoder_outputs", encTensor),
                NamedOnnxValue.CreateFromTensor("targets", targets),
                NamedOnnxValue.CreateFromTensor("input_states_1", lstmH),
                NamedOnnxValue.CreateFromTensor("input_states_2", lstmC)
            };

            using var decoderResults = _decoderSession!.Run(decoderInputs);

            // Get joint output: [batch, enc_time, target_time, num_classes]
            var jointOutput = decoderResults.First();
            if (jointOutput.Value is not Tensor<float> jointLogits)
            {
                _logger.LogWarning("Unexpected decoder output type");
                break;
            }

            // Update LSTM states for next iteration
            // Outputs: [0]=outputs, [1]=output_states_1, [2]=output_states_2
            var newH = decoderResults.ElementAt(1);
            var newC = decoderResults.ElementAt(2);
            if (newH.Value is Tensor<float> newHStates)
            {
                lstmH = CopyTensor(newHStates);
            }
            if (newC.Value is Tensor<float> newCStates)
            {
                lstmC = CopyTensor(newCStates);
            }

            // Get logits at current encoder time step: jointLogits[0, t, 0, :]
            // Token/blank logits are at indices [0..blankId]
            float maxTokenVal = float.MinValue;
            int maxTokenIdx = blankId;  // Default to blank

            for (int k = 0; k <= blankId; k++)
            {
                float val = jointLogits[0, t, 0, k];
                if (val > maxTokenVal)
                {
                    maxTokenVal = val;
                    maxTokenIdx = k;
                }
            }

            if (maxTokenIdx == blankId)
            {
                // Blank: advance time by 1
                t++;
            }
            else
            {
                // Token emitted
                decodedTokens.Add(maxTokenIdx);
                lastLabel = maxTokenIdx;

                // Get duration from duration logits [blankId+1 .. totalClasses-1]
                int durOffset = blankId + 1;
                float maxDurVal = float.MinValue;
                int maxDurIdx = 0;

                for (int d = 0; d < tdtDurations.Length; d++)
                {
                    float val = jointLogits[0, t, 0, durOffset + d];
                    if (val > maxDurVal)
                    {
                        maxDurVal = val;
                        maxDurIdx = d;
                    }
                }

                int duration = tdtDurations[maxDurIdx];
                t += Math.Max(1, duration);
            }
        }

        if (iterations >= maxIterations)
        {
            _logger.LogWarning("TDT decoding hit safety limit ({MaxIter} iterations)", maxIterations);
        }

        _logger.LogDebug("TDT decoded {TokenCount} tokens in {Iterations} iterations",
            decodedTokens.Count, iterations);

        return DecodeTokenIds(decodedTokens.Select(id => (long)id).ToArray());
    }

    private static DenseTensor<float> CopyTensor(Tensor<float> source)
    {
        var dims = source.Dimensions.ToArray();
        var totalSize = 1;
        foreach (var d in dims) totalSize *= d;
        var data = new float[totalSize];

        int idx = 0;
        foreach (var val in source)
        {
            data[idx++] = val;
        }

        return new DenseTensor<float>(data, dims);
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

        // Join tokens - handle BPE-style tokens
        var text = string.Join("", tokens)
            .Replace("▁", " ")  // Sentencepiece word boundary
            .Replace("##", "")  // WordPiece continuation
            .Trim();

        return text;
    }

    private float EstimateConfidence(float[] audioSamples)
    {
        if (audioSamples == null || audioSamples.Length < 1600) // Less than 0.1s
            return 0.3f;

        // Calculate RMS energy
        float sumSquares = 0;
        for (int i = 0; i < audioSamples.Length; i++)
        {
            sumSquares += audioSamples[i] * audioSamples[i];
        }
        float rmsEnergy = MathF.Sqrt(sumSquares / audioSamples.Length);

        // Map RMS energy to confidence
        float confidence = Math.Min(1.0f, rmsEnergy / 0.1f);

        // Adjust based on duration
        var duration = audioSamples.Length / 16000.0;
        if (duration < 0.5)
            confidence *= 0.7f;
        else if (duration > 5.0)
            confidence *= 0.95f;

        return Math.Max(0.1f, Math.Min(1.0f, confidence));
    }

    private SessionOptions CreateSessionOptions()
    {
        var options = new SessionOptions();
        _cudaAvailable = false;

        try
        {
            options.AppendExecutionProvider_CUDA(0);
            _cudaAvailable = true;
            _logger.LogInformation("CUDA execution provider configured");
        }
        catch
        {
            _logger.LogWarning("CUDA not available, using CPU");
        }

        options.AppendExecutionProvider_CPU(0);
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        // Disable memory pattern to avoid buffer reuse issues with patched model shapes
        options.EnableMemoryPattern = false;
        options.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING;

        return options;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _encoderSession?.Dispose();
        _decoderSession?.Dispose();
        _loadLock?.Dispose();
        (_chunker as IDisposable)?.Dispose();
        (_merger as IDisposable)?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
