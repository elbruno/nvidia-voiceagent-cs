namespace NvidiaVoiceAgent.ModelHub;

/// <summary>
/// Interface for reporting download progress.
/// </summary>
public interface IProgressReporter
{
    /// <summary>
    /// Report download progress.
    /// </summary>
    /// <param name="progress">Current download progress information.</param>
    void Report(DownloadProgress progress);

    /// <summary>
    /// Report that a download has started.
    /// </summary>
    /// <param name="modelName">Name of the model being downloaded.</param>
    /// <param name="totalBytes">Total bytes to download (-1 if unknown).</param>
    void OnDownloadStarted(string modelName, long totalBytes);

    /// <summary>
    /// Report that a download has completed.
    /// </summary>
    /// <param name="modelName">Name of the model that was downloaded.</param>
    /// <param name="success">Whether the download succeeded.</param>
    void OnDownloadCompleted(string modelName, bool success);
}
