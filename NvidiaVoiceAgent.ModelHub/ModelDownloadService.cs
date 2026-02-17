using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
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
    private static readonly HttpClient HttpClient = new();
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

        if (!_options.AutoDownload)
        {
            _logger.LogWarning(
                "Model {ModelName} download requested but auto-download is disabled. " +
                "Download manually from HuggingFace: {RepoId}",
                model.Name, model.RepoId);
            return new DownloadResult
            {
                Success = false,
                ModelType = modelType,
                ErrorMessage = "Auto-download is disabled. Enable AutoDownload in ModelHubOptions or download manually."
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

            // Download primary model file(s) with progress reporting
            var (modelPath, _) = await DownloadPrimaryFilesAsync(model, localDir, progress, cancellationToken);

            // Ensure additional files are present (if any)
            var failedAdditionalFiles = await DownloadAdditionalFilesAsync(model, localDir, cancellationToken);

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

    private async Task<(string modelPath, bool usedRepoScan)> DownloadPrimaryFilesAsync(
        ModelInfo model,
        string localDir,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        var candidates = GetPrimaryCandidates(model);
        Exception? lastError = null;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var modelPath = await HFDownloader.DownloadFileAsync(
                    model.RepoId,
                    candidate,
                    localDir: localDir,
                    token: _options.HuggingFaceToken,
                    progress: progress);

                _logger.LogInformation("Downloaded primary model file: {File}", candidate);
                return (modelPath, false);
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Failed to download primary file {File}.", candidate);
            }
        }

        if (model.AllowRepoScan && model.RepoFileIncludes.Length > 0)
        {
            var filesDownloaded = await DownloadRepoFilesAsync(model, localDir, cancellationToken);
            if (filesDownloaded > 0)
            {
                return (localDir, true);
            }
        }

        if (lastError != null)
        {
            throw lastError;
        }

        throw new InvalidOperationException($"No downloadable files found for {model.Name}.");
    }

    private async Task<List<string>> DownloadAdditionalFilesAsync(
        ModelInfo model,
        string localDir,
        CancellationToken cancellationToken)
    {
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

        return failedAdditionalFiles;
    }

    private static IReadOnlyList<string> GetPrimaryCandidates(ModelInfo model)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(model.Filename))
        {
            candidates.Add(model.Filename);
        }

        if (model.AlternateFilenames.Length > 0)
        {
            candidates.AddRange(model.AlternateFilenames);
        }

        return candidates;
    }

    private async Task<int> DownloadRepoFilesAsync(ModelInfo model, string localDir, CancellationToken cancellationToken)
    {
        var repoFiles = await GetRepoFileListAsync(model.RepoId, cancellationToken);
        var includes = model.RepoFileIncludes;

        var filesToDownload = repoFiles
            .Where(file => MatchesInclude(file, includes))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (filesToDownload.Count == 0)
        {
            _logger.LogWarning("No files matched repo include patterns for {ModelName}.", model.Name);
            return 0;
        }

        _logger.LogInformation(
            "Repo scan for {ModelName} matched {Count} file(s).",
            model.Name,
            filesToDownload.Count);

        var downloadedCount = 0;
        foreach (var file in filesToDownload)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await HFDownloader.DownloadFileAsync(
                    model.RepoId,
                    file,
                    localDir: localDir,
                    token: _options.HuggingFaceToken);

                downloadedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download repo file {File}.", file);
            }
        }

        return downloadedCount;
    }

    private async Task<IReadOnlyList<string>> GetRepoFileListAsync(string repoId, CancellationToken cancellationToken)
    {
        var url = $"https://huggingface.co/api/models/{repoId}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (!string.IsNullOrWhiteSpace(_options.HuggingFaceToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.HuggingFaceToken);
        }

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("siblings", out var siblings) ||
            siblings.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var files = new List<string>();
        foreach (var sibling in siblings.EnumerateArray())
        {
            if (sibling.TryGetProperty("rfilename", out var filenameElement))
            {
                var filename = filenameElement.GetString();
                if (!string.IsNullOrWhiteSpace(filename))
                {
                    files.Add(filename);
                }
            }
        }

        return files;
    }

    private static bool MatchesInclude(string filename, string[] includes)
    {
        if (includes.Length == 0)
        {
            return false;
        }

        foreach (var include in includes)
        {
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            if (include.StartsWith(".", StringComparison.Ordinal))
            {
                if (filename.EndsWith(include, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (filename.EndsWith(include, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
        if (!string.IsNullOrWhiteSpace(model.Filename) && File.Exists(mainFilePath))
        {
            // Return the directory path, not the file path
            // This allows services to locate related files
            return localDir;
        }

        foreach (var alternate in model.AlternateFilenames)
        {
            var alternatePath = Path.Combine(localDir, alternate);
            if (File.Exists(alternatePath))
            {
                return localDir;
            }
        }

        // Search for common model file extensions in the local directory
        if (Directory.Exists(localDir))
        {
            foreach (var include in model.RepoFileIncludes)
            {
                var match = FindMatchingFile(localDir, include);
                if (match != null)
                {
                    return Path.GetDirectoryName(match);
                }
            }

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

    private static string? FindMatchingFile(string localDir, string include)
    {
        if (string.IsNullOrWhiteSpace(include))
        {
            return null;
        }

        if (include.StartsWith(".", StringComparison.Ordinal))
        {
            var matches = Directory.GetFiles(localDir, "*" + include, SearchOption.AllDirectories);
            return matches.FirstOrDefault();
        }

        var explicitMatches = Directory.GetFiles(localDir, "*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(include, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return explicitMatches.FirstOrDefault();
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
