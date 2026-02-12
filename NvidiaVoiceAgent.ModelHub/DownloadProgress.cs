namespace NvidiaVoiceAgent.ModelHub;

/// <summary>
/// Progress information for a model download.
/// </summary>
public class DownloadProgress
{
    /// <summary>
    /// Name of the model being downloaded.
    /// </summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary>
    /// Name of the file being downloaded.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// Bytes downloaded so far.
    /// </summary>
    public long BytesDownloaded { get; init; }

    /// <summary>
    /// Total bytes to download (-1 if unknown).
    /// </summary>
    public long TotalBytes { get; init; } = -1;

    /// <summary>
    /// Download progress as a percentage (0-100).
    /// </summary>
    public double ProgressPercent => TotalBytes > 0
        ? (double)BytesDownloaded / TotalBytes * 100.0
        : -1;
}
