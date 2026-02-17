using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NvidiaVoiceAgent.Core.Models;
using NvidiaVoiceAgent.Core.Services;

namespace NvidiaVoiceAgent.Core.Adapters;

/// <summary>
/// Model adapter for NVIDIA Parakeet-TDT ASR models.
/// Handles Parakeet-specific preprocessing, padding, and CTC decoding.
/// </summary>
public class ParakeetTdtAdapter : IAsrModelAdapter
{
    private readonly ILogger<ParakeetTdtAdapter> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private AsrModelSpecification? _specification;
    private InferenceSession? _session;
    private MelSpectrogramExtractor? _melExtractor;
    private string[]? _vocabulary;
    private bool _isLoaded;
    private bool _disposed;
    private bool _cudaAvailable;

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

        var melSpec = PrepareInput(audioSamples);
        return await InferAsync(melSpec, cancellationToken);
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
            return string.Empty;
        }

        // Apply padding according to specification
        var paddingConfig = _specification!.InputRequirements.Padding;
        int paddedFrames = numFrames;

        if (paddingConfig?.Enabled == true && paddingConfig.Strategy == "multiple_of")
        {
            int multiple = paddingConfig.Value;
            paddedFrames = ((numFrames + multiple - 1) / multiple) * multiple;
            _logger.LogDebug("Padding frames from {Original} to {Padded} (multiple of {Multiple})",
                numFrames, paddedFrames, multiple);
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

        // Determine length parameter value based on specification
        long lengthValue = _specification.InputRequirements.LengthParameter?.Value switch
        {
            "padded_frame_count" => paddedFrames,
            "frame_count" => numFrames,
            _ => paddedFrames
        };

        _logger.LogDebug("Running inference: input=[1, {Mels}, {Frames}], length={Length}",
            numMels, paddedFrames, lengthValue);

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
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
