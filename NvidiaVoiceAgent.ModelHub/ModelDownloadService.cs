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
                _progressReporter.OnModelCached(model.Name, cachedPath!);
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

            // Delete existing files to avoid IOException from HuggingfaceHub's
            // ChmodAndReplace, which cannot overwrite on Windows
            DeleteExistingFile(localDir, model.Filename);
            foreach (var additionalFile in model.AdditionalFiles)
            {
                DeleteExistingFile(localDir, additionalFile);
            }

            // Create a progress reporter that forwards to our IProgressReporter
            var lastReportedProgress = -1;
            var progress = new Progress<int>(progressPercent =>
            {
                // Clamp progressPercent to 0-100 range to handle edge cases or HFDownloader anomalies
                int clampedPercent = Math.Max(0, Math.Min(100, progressPercent));

                // Only report every 5% to avoid spamming logs
                if (clampedPercent >= lastReportedProgress + 5 || clampedPercent == 100)
                {
                    lastReportedProgress = clampedPercent;
                    var bytesDownloaded = (long)(model.ExpectedSizeBytes * clampedPercent / 100.0);
                    // Ensure bytesDownloaded doesn't exceed TotalBytes
                    bytesDownloaded = Math.Min(bytesDownloaded, model.ExpectedSizeBytes);

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
            var failedAdditionalFiles = new List<string>();
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
                    failedAdditionalFiles.Add(additionalFile);
                }
            }

            if (failedAdditionalFiles.Count > 0)
            {
                var failureMessage = $"Missing required files for {model.Name}: {string.Join(", ", failedAdditionalFiles)}";
                _logger.LogWarning(failureMessage);
                _progressReporter.OnDownloadCompleted(model.Name, success: false);

                return new DownloadResult
                {
                    Success = false,
                    ModelType = modelType,
                    ErrorMessage = failureMessage
                };
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

    /// <summary>
    /// Delete an existing file in the local directory to avoid IOException
    /// from HuggingfaceHub's ChmodAndReplace on Windows.
    /// </summary>
    private void DeleteExistingFile(string localDir, string relativeFilename)
    {
        var filePath = Path.Combine(localDir, relativeFilename);
        if (File.Exists(filePath))
        {
            _logger.LogDebug("Removing existing file before re-download: {File}", filePath);
            File.Delete(filePath);
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
            // Return the directory path, not the file path
            // This allows services to locate related files
            return localDir;
        }

        // Search for common model file extensions in the local directory
        if (Directory.Exists(localDir))
        {
            // Search for ONNX models
            var onnxFiles = Directory.GetFiles(localDir, "*.onnx", SearchOption.AllDirectories);
            if (onnxFiles.Length > 0)
            {
                return Path.GetDirectoryName(onnxFiles[0]);
            }

            // Search for SafeTensors models (used by PersonaPlex)
            var safetensorsFiles = Directory.GetFiles(localDir, "*.safetensors", SearchOption.AllDirectories);
            if (safetensorsFiles.Length > 0)
            {
                return Path.GetDirectoryName(safetensorsFiles[0]);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public bool IsModelAvailable(ModelType modelType)
    {
        var model = _registry.GetModel(modelType);
        if (model == null) return false;

        // Primary file must exist
        if (GetModelPath(modelType) == null) return false;

        // All additional files must also exist
        var localDir = Path.Combine(_options.ModelCachePath, model.LocalDirectory);
        foreach (var additionalFile in model.AdditionalFiles)
        {
            var filePath = Path.Combine(localDir, additionalFile);
            if (!File.Exists(filePath))
            {
                _logger.LogWarning(
                    "Model {ModelName} is missing required file: {File}",
                    model.Name, additionalFile);
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public bool DeleteModel(ModelType modelType)
    {
        var model = _registry.GetModel(modelType);
        if (model == null)
        {
            _logger.LogWarning("Cannot delete: no model registered for type {ModelType}", modelType);
            return false;
        }

        var localDir = Path.Combine(_options.ModelCachePath, model.LocalDirectory);
        if (!Directory.Exists(localDir))
        {
            _logger.LogInformation("Model {ModelName} directory does not exist, nothing to delete", model.Name);
            return false;
        }

        var deletedAny = false;

        // Delete primary file
        DeleteFileIfExists(localDir, model.Filename, ref deletedAny);

        // Delete additional files
        foreach (var additionalFile in model.AdditionalFiles)
        {
            DeleteFileIfExists(localDir, additionalFile, ref deletedAny);
        }

        // Clean up empty directories (walk up from subdirectories)
        CleanupEmptyDirectories(localDir);

        if (deletedAny)
        {
            _logger.LogInformation("Model {ModelName} deleted from {Path}", model.Name, localDir);
        }
        else
        {
            _logger.LogInformation("No files found to delete for model {ModelName}", model.Name);
        }

        return deletedAny;
    }

    /// <summary>
    /// Delete a specific file and track whether anything was deleted.
    /// </summary>
    private void DeleteFileIfExists(string localDir, string relativeFilename, ref bool deletedAny)
    {
        var filePath = Path.Combine(localDir, relativeFilename);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogDebug("Deleted file: {File}", filePath);
            deletedAny = true;
        }
    }

    /// <summary>
    /// Remove empty directories starting from the given path and walking up.
    /// Stops at the ModelCachePath root.
    /// </summary>
    private void CleanupEmptyDirectories(string directory)
    {
        try
        {
            var cacheRoot = Path.GetFullPath(_options.ModelCachePath);

            // Walk subdirectories depth-first and remove empty ones
            if (Directory.Exists(directory))
            {
                foreach (var subDir in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length))
                {
                    if (Directory.Exists(subDir) && !Directory.EnumerateFileSystemEntries(subDir).Any())
                    {
                        Directory.Delete(subDir);
                        _logger.LogDebug("Removed empty directory: {Dir}", subDir);
                    }
                }

                // Remove the model directory itself if empty
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                    _logger.LogDebug("Removed empty directory: {Dir}", directory);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up empty directories under {Dir}", directory);
        }
    }
}
