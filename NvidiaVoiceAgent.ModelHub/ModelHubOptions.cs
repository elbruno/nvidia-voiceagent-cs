namespace NvidiaVoiceAgent.ModelHub;

/// <summary>
/// Configuration options for the ModelHub service.
/// </summary>
public class ModelHubOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "ModelHub";

    /// <summary>
    /// Whether to automatically download missing models on startup.
    /// </summary>
    public bool AutoDownload { get; set; } = true;

    /// <summary>
    /// Whether to prefer INT8 quantized models (smaller/faster).
    /// </summary>
    public bool UseInt8Quantization { get; set; } = true;

    /// <summary>
    /// Local path for caching downloaded models.
    /// </summary>
    public string ModelCachePath { get; set; } = "models";

    /// <summary>
    /// HuggingFace API token for accessing gated models (optional).
    /// </summary>
    public string? HuggingFaceToken { get; set; }
}
