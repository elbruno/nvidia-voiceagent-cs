using Microsoft.Extensions.Logging;

namespace NvidiaVoiceAgent.ModelHub;

/// <summary>
/// Console-based progress reporter with formatted output.
/// </summary>
public class ConsoleProgressReporter : IProgressReporter
{
    private readonly ILogger<ConsoleProgressReporter> _logger;

    public ConsoleProgressReporter(ILogger<ConsoleProgressReporter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void Report(DownloadProgress progress)
    {
        if (progress.TotalBytes > 0)
        {
            var downloadedMb = progress.BytesDownloaded / (1024.0 * 1024.0);
            var totalMb = progress.TotalBytes / (1024.0 * 1024.0);
            _logger.LogInformation(
                "Downloading {ModelName}: {Progress:F1}% ({DownloadedMB:F1}/{TotalMB:F1} MB)",
                progress.ModelName,
                progress.ProgressPercent,
                downloadedMb,
                totalMb);
        }
        else
        {
            var downloadedMb = progress.BytesDownloaded / (1024.0 * 1024.0);
            _logger.LogInformation(
                "Downloading {ModelName}: {DownloadedMB:F1} MB downloaded",
                progress.ModelName,
                downloadedMb);
        }
    }

    /// <inheritdoc />
    public void OnDownloadStarted(string modelName, long totalBytes)
    {
        if (totalBytes > 0)
        {
            var totalMb = totalBytes / (1024.0 * 1024.0);
            _logger.LogInformation("Download started: {ModelName} ({TotalMB:F1} MB)", modelName, totalMb);
        }
        else
        {
            _logger.LogInformation("Download started: {ModelName}", modelName);
        }
    }

    /// <inheritdoc />
    public void OnDownloadCompleted(string modelName, bool success)
    {
        if (success)
        {
            _logger.LogInformation("Download complete: {ModelName}", modelName);
        }
        else
        {
            _logger.LogWarning("Download failed: {ModelName}", modelName);
        }
    }
}
