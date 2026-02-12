namespace NvidiaVoiceAgent.ModelHub;

/// <summary>
/// Result of a model download operation.
/// </summary>
public class DownloadResult
{
    /// <summary>
    /// Whether the download was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Local file path of the downloaded model.
    /// </summary>
    public string? ModelPath { get; init; }

    /// <summary>
    /// Type of model that was downloaded.
    /// </summary>
    public ModelType ModelType { get; init; }

    /// <summary>
    /// Error message if download failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Whether the model was already cached locally.
    /// </summary>
    public bool WasCached { get; init; }
}
