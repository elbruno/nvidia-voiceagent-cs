using NvidiaVoiceAgent.ModelHub;

namespace NvidiaVoiceAgent.Services;

/// <summary>
/// Progress reporter that broadcasts download progress to connected WebSocket clients
/// via the LogBroadcaster, as well as logging to the console.
/// </summary>
public class WebProgressReporter : IProgressReporter
{
    private readonly ILogger<WebProgressReporter> _logger;
    private readonly ILogBroadcaster _logBroadcaster;

    public WebProgressReporter(ILogger<WebProgressReporter> logger, ILogBroadcaster logBroadcaster)
    {
        _logger = logger;
        _logBroadcaster = logBroadcaster;
    }

    /// <inheritdoc />
    public void Report(DownloadProgress progress)
    {
        string message;
        if (progress.TotalBytes > 0)
        {
            var downloadedMb = progress.BytesDownloaded / (1024.0 * 1024.0);
            var totalMb = progress.TotalBytes / (1024.0 * 1024.0);
            message = $"Downloading {progress.ModelName}: {progress.ProgressPercent:F1}% ({downloadedMb:F1}/{totalMb:F1} MB)";
        }
        else
        {
            var downloadedMb = progress.BytesDownloaded / (1024.0 * 1024.0);
            message = $"Downloading {progress.ModelName}: {downloadedMb:F1} MB downloaded";
        }

        _logger.LogInformation(message);
        _ = _logBroadcaster.BroadcastLogAsync(message, "INFO");
    }

    /// <inheritdoc />
    public void OnDownloadStarted(string modelName, long totalBytes)
    {
        string message;
        if (totalBytes > 0)
        {
            var totalMb = totalBytes / (1024.0 * 1024.0);
            message = $"📥 Download started: {modelName} ({totalMb:F1} MB)";
        }
        else
        {
            message = $"📥 Download started: {modelName}";
        }

        _logger.LogInformation(message);
        _ = _logBroadcaster.BroadcastLogAsync(message, "INFO");
    }

    /// <inheritdoc />
    public void OnDownloadCompleted(string modelName, bool success)
    {
        string message;
        string level;

        if (success)
        {
            message = $"✅ Download complete: {modelName}";
            level = "INFO";
            _logger.LogInformation(message);
        }
        else
        {
            message = $"❌ Download failed: {modelName}";
            level = "WARN";
            _logger.LogWarning(message);
        }

        _ = _logBroadcaster.BroadcastLogAsync(message, level);
    }

    /// <inheritdoc />
    public void OnModelCached(string modelName, string modelPath)
    {
        var message = $"✅ Model already downloaded: {modelName}";
        _logger.LogInformation(message);
        _ = _logBroadcaster.BroadcastLogAsync(message, "INFO");
    }
}
