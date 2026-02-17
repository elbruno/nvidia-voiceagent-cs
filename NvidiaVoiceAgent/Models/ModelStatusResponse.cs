namespace NvidiaVoiceAgent.Models;

/// <summary>
/// Response model for model status API endpoint.
/// </summary>
public class ModelStatusResponse
{
    /// <summary>
    /// Model name (e.g., "Parakeet-TDT-0.6B-V2").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Model type (Asr, Tts, Vocoder, Llm).
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Download status ("downloaded", "not_downloaded").
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// HuggingFace repository ID.
    /// </summary>
    public required string RepoId { get; set; }

    /// <summary>
    /// Local path to the model files (null if not downloaded).
    /// </summary>
    public string? LocalPath { get; set; }

    /// <summary>
    /// Expected size in megabytes.
    /// </summary>
    public double ExpectedSizeMb { get; set; }

    /// <summary>
    /// Whether this model is required for the application to function.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Whether this model is available for download.
    /// </summary>
    public bool IsAvailableForDownload { get; set; }
}
