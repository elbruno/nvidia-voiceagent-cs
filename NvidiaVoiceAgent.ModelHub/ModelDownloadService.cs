using HuggingfaceHub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NvidiaVoiceAgent.ModelHub;

/// <summary>
/// Service that downloads and manages AI models from HuggingFace.
/// Uses the HuggingfaceHub NuGet package for downloads.
/// </summary>
public class ModelDownloadService : IModelDownloadService
{
    private readonly ILogger<ModelDownloadService> _logger;
    private readonly IModelRegistry _registry;
    private readonly IProgressReporter _progressReporter;
    private readonly ModelHubOptions _options;

    public ModelDownloadService(
        ILogger<ModelDownloadService> logger,
        IModelRegistry registry,
        IProgressReporter progressReporter,
        IOptions<ModelHubOptions> options)
    {
        _logger = logger;
        _registry = registry;
        _progressReporter = progressReporter;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DownloadResult>> EnsureModelsAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<DownloadResult>();
        var requiredModels = _registry.GetRequiredModels();

        _logger.LogInformation("Checking {Count} required model(s)...", requiredModels.Count);

        foreach (var model in requiredModels)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsModelAvailable(model.Type))
            {
                var cachedPath = GetModelPath(model.Type);
                _logger.LogInformation("Model {ModelName} already available at {Path}", model.Name, cachedPath);
                results.Add(new DownloadResult
                {
                    Success = true,
                    ModelPath = cachedPath,
                    ModelType = model.Type,
                    WasCached = true
                });
                continue;
            }

            if (!_options.AutoDownload)
            {
                _logger.LogWarning(
                    "Model {ModelName} not found and auto-download is disabled. " +
                    "Download from HuggingFace: {RepoId}",
                    model.Name, model.RepoId);
                results.Add(new DownloadResult
                {
                    Success = false,
                    ModelType = model.Type,
                    ErrorMessage = "Model not found and auto-download is disabled."
                });
                continue;
            }

            var result = await DownloadModelAsync(model.Type, cancellationToken);
            results.Add(result);
        }

        var successCount = results.Count(r => r.Success);
        _logger.LogInformation(
            "Model check complete: {SuccessCount}/{TotalCount} models ready",
            successCount, results.Count);

        return results.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<DownloadResult> DownloadModelAsync(ModelType modelType, CancellationToken cancellationToken = default)
    {
        var model = _registry.GetModel(modelType);
        if (model == null)
        {
            return new DownloadResult
            {
                Success = false,
                ModelType = modelType,
                ErrorMessage = $"No model registered for type {modelType}."
            };
        }

        var localDir = Path.Combine(_options.ModelCachePath, model.LocalDirectory);

        try
        {
            _progressReporter.OnDownloadStarted(model.Name, model.ExpectedSizeBytes);
            _logger.LogInformation(
                "Downloading model {ModelName} from {RepoId}/{Filename}",
                model.Name, model.RepoId, model.Filename);

            // Create a progress reporter that forwards to our IProgressReporter
            var lastReportedProgress = -1;
            var progress = new Progress<int>(progressPercent =>
            {
                // Only report every 5% to avoid spamming logs
                if (progressPercent >= lastReportedProgress + 5 || progressPercent == 100)
                {
                    lastReportedProgress = progressPercent;
                    var bytesDownloaded = (long)(model.ExpectedSizeBytes * progressPercent / 100.0);
                    _progressReporter.Report(new DownloadProgress
                    {
                        ModelName = model.Name,
                        BytesDownloaded = bytesDownloaded,
                        TotalBytes = model.ExpectedSizeBytes
                    });
                }
            });

            // Download main model file with progress reporting
            var modelPath = await HFDownloader.DownloadFileAsync(
                model.RepoId,
                model.Filename,
                localDir: localDir,
                token: _options.HuggingFaceToken,
                progress: progress);

            // Download additional files
            foreach (var additionalFile in model.AdditionalFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await HFDownloader.DownloadFileAsync(
                        model.RepoId,
                        additionalFile,
                        localDir: localDir,
                        token: _options.HuggingFaceToken);

                    _logger.LogDebug("Downloaded additional file: {File}", additionalFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download additional file {File}. Continuing...", additionalFile);
                }
            }

            _progressReporter.OnDownloadCompleted(model.Name, success: true);

            return new DownloadResult
            {
                Success = true,
                ModelPath = modelPath,
                ModelType = modelType,
                WasCached = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download model {ModelName}", model.Name);
            _progressReporter.OnDownloadCompleted(model.Name, success: false);

            return new DownloadResult
            {
                Success = false,
                ModelType = modelType,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public string? GetModelPath(ModelType modelType)
    {
        var model = _registry.GetModel(modelType);
        if (model == null) return null;

        var localDir = Path.Combine(_options.ModelCachePath, model.LocalDirectory);

        // Check if the specific model file exists
        var mainFilePath = Path.Combine(localDir, model.Filename);
        if (File.Exists(mainFilePath))
        {
            return mainFilePath;
        }

        // Search for any .onnx file in the local directory
        if (Directory.Exists(localDir))
        {
            var onnxFiles = Directory.GetFiles(localDir, "*.onnx", SearchOption.AllDirectories);
            if (onnxFiles.Length > 0)
            {
                return onnxFiles[0];
            }
        }

        return null;
    }

    /// <inheritdoc />
    public bool IsModelAvailable(ModelType modelType)
    {
        return GetModelPath(modelType) != null;
    }
}
