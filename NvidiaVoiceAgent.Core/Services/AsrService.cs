using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NvidiaVoiceAgent.Core.Adapters;
using NvidiaVoiceAgent.Core.Models;
using NvidiaVoiceAgent.ModelHub;

namespace NvidiaVoiceAgent.Core.Services;

/// <summary>
/// ASR service using model adapter pattern for flexible model support.
/// Delegates actual inference to IAsrModelAdapter implementations.
/// Implements lazy loading and graceful mock mode fallback.
/// </summary>
public class AsrService : IAsrService, IDisposable
{
    private readonly ILogger<AsrService> _logger;
    private readonly ModelConfig _config;
    private readonly IAsrModelAdapter _adapter;
    private readonly IModelDownloadService? _modelDownloadService;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private bool _isModelLoaded;
    private bool _isMockMode;
    private bool _disposed;

    public AsrService(
        ILogger<AsrService> logger,
        IOptions<ModelConfig> config,
        IAsrModelAdapter adapter,
        IModelDownloadService? modelDownloadService = null)
    {
        _logger = logger;
        _config = config.Value;
        _adapter = adapter;
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
            return await _adapter.TranscribeAsync(audioSamples, cancellationToken);
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
            return await _adapter.TranscribeWithConfidenceAsync(audioSamples, cancellationToken);
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
            if (_isModelLoaded) return; // Double-check pattern

            var modelPath = FindModelDirectory();
            if (modelPath == null)
            {
                _logger.LogWarning(
                    "No ASR model found in '{ModelPath}'. Running in mock mode. " +
                    "Download Parakeet-TDT ONNX from HuggingFace: onnx-community/parakeet-tdt-0.6b-v2-ONNX",
                    _config.AsrModelPath);
                _isMockMode = true;
                _isModelLoaded = true;
                return;
            }

            _logger.LogInformation("Loading ASR model from {ModelPath}", modelPath);

            try
            {
                // Delegate model loading to the adapter
                await _adapter.LoadAsync(modelPath, cancellationToken);

                _isModelLoaded = true;
                _isMockMode = false;

                _logger.LogInformation("ASR model loaded successfully via adapter: {ModelName}",
                    _adapter.ModelName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load ASR model via adapter. Falling back to mock mode.");
                _isMockMode = true;
                _isModelLoaded = true;
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private string? FindModelDirectory()
    {
        var basePath = _config.AsrModelPath;

        // Check if basePath directly contains model_spec.json
        if (Directory.Exists(basePath))
        {
            var specPath = Path.Combine(basePath, "model_spec.json");
            if (File.Exists(specPath))
            {
                _logger.LogDebug("Found model specification at {Path}", specPath);
                return basePath;
            }

            // Search subdirectories for model_spec.json
            var specFiles = Directory.GetFiles(basePath, "model_spec.json", SearchOption.AllDirectories);
            if (specFiles.Length > 0)
            {
                var modelDir = Path.GetDirectoryName(specFiles[0])!;
                _logger.LogDebug("Found model specification in subdirectory: {Path}", modelDir);
                return modelDir;
            }
        }

        // Try ModelHub downloaded path
        if (_modelDownloadService != null && _modelDownloadService.IsModelAvailable(ModelType.Asr))
        {
            var hubPath = _modelDownloadService.GetModelPath(ModelType.Asr);
            if (hubPath != null && Directory.Exists(hubPath))
            {
                var specPath = Path.Combine(hubPath, "model_spec.json");
                if (File.Exists(specPath))
                {
                    _logger.LogInformation("Found ASR model via ModelHub at {Path}", hubPath);
                    return hubPath;
                }

                // Search subdirectories  
                var specFiles = Directory.GetFiles(hubPath, "model_spec.json", SearchOption.AllDirectories);
                if (specFiles.Length > 0)
                {
                    var modelDir = Path.GetDirectoryName(specFiles[0])!;
                    _logger.LogInformation("Found ASR model via ModelHub at {Path}", modelDir);
                    return modelDir;
                }
            }
        }

        return null;
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

        if (_adapter is IDisposable disposableAdapter)
        {
            disposableAdapter.Dispose();
        }

        _loadLock.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
