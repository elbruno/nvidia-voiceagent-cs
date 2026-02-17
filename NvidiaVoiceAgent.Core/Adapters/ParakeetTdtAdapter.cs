using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NvidiaVoiceAgent.Core.Models;
using NvidiaVoiceAgent.Core.Services;

namespace NvidiaVoiceAgent.Core.Adapters;

/// <summary>
/// Model adapter for NVIDIA Parakeet-TDT ASR models.
/// Handles Parakeet-specific preprocessing, padding, CTC decoding, and optional audio chunking.
/// </summary>
public class ParakeetTdtAdapter : IAsrModelAdapter
{
    private readonly ILogger<ParakeetTdtAdapter> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private AsrModelSpecification? _specification;
    private InferenceSession? _session;
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
            _session = new InferenceSession(encoderPath, sessionOptions);

            _logger.LogInformation("ONNX session created using {Provider}",
                _cudaAvailable ? "CUDA (GPU)" : "CPU");

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
                    _logger);
                _merger = new TranscriptMerger(_logger);
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
        if (_session == null || _specification == null)
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
        return await InferAsync(melSpec, cancellationToken);
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
            return await InferAsync(melSpec, cancellationToken);
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
                transcripts[i] = await InferAsync(melSpec, cancellationToken);

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
                numFrames, minFrames, numFrames * 10);  // 10ms per frame
            return "[Audio too short for transcription]";
        }
        if (numFrames > maxFrames)
        {
            string chunkingMsg = _chunkingEnabled ? " Enable chunking in model configuration or reduce audio length." : "";
            _logger.LogWarning("Audio too long: {Frames} frames (maximum {Max}). Duration: {Duration:F2}s.{ChunkingMsg}",
                numFrames, maxFrames, numFrames * 0.01, chunkingMsg);  // 10ms per frame
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

        // IMPORTANT: Use paddedFrames as length, not numFrames
        // The model expects length parameter to match the actual tensor time dimension
        long lengthValue = _specification.InputRequirements.LengthParameter?.Value switch
        {
            "padded_frame_count" => paddedFrames,
            "frame_count" => paddedFrames,  // Use padded frames (tensor actually contains padded data)
            _ => paddedFrames
        };

        _logger.LogInformation(
            "Running ASR inference: input_shape=[1, {MelBins}, {TimeFrames}], length_param={Length}, sample_count={Samples}",
            numMels, paddedFrames, lengthValue, (int)(numFrames * 160));

        // Create inputs
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("audio_signal", inputTensor),
            NamedOnnxValue.CreateFromTensor(_specification.InputRequirements.LengthParameter?.Name ?? "length",
                new DenseTensor<long>(new long[] { lengthValue }, new[] { 1 }))
        };

        // Run inference
        using var results = _session!.Run(inputs);

        // Decode output
        return DecodeOutput(results);
    }

    private string DecodeOutput(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results)
    {
        var output = results.First();

        if (output.Value is not Tensor<float> logprobs)
        {
            _logger.LogWarning("Unexpected output type: {Type}", output.Value?.GetType().Name ?? "null");
            return "[Unknown output format]";
        }

        return GreedyCtcDecode(logprobs);
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
        int blankToken = _specification!.Decoding.BlankTokenId;

        for (int t = 0; t < timeSteps; t++)
        {
            // Find argmax for this timestep
            float maxVal = float.MinValue;
            int maxIdx = 0;

            for (int v = 0; v < vocabSize; v++)
            {
                float val = dims.Length == 3 ? logprobs[0, t, v] : logprobs[t, v];

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
        options.EnableMemoryPattern = true;
        options.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING;

        return options;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _session?.Dispose();
        _loadLock?.Dispose();
        (_chunker as IDisposable)?.Dispose();
        (_merger as IDisposable)?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
