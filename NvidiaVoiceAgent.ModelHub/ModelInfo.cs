namespace NvidiaVoiceAgent.ModelHub;

/// <summary>
/// Metadata about a downloadable model.
/// </summary>
public class ModelInfo
{
    /// <summary>
    /// Type of model (ASR, TTS, Vocoder, LLM).
    /// </summary>
    public ModelType Type { get; init; }

    /// <summary>
    /// Human-readable name of the model.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// HuggingFace repository ID (e.g., "onnx-community/parakeet-tdt-0.6b-v2-ONNX").
    /// </summary>
    public string RepoId { get; init; } = string.Empty;

    /// <summary>
    /// Specific filename to download from the repository.
    /// </summary>
    public string Filename { get; init; } = string.Empty;

    /// <summary>
    /// Expected file size in bytes (approximate, for progress reporting).
    /// </summary>
    public long ExpectedSizeBytes { get; init; }

    /// <summary>
    /// Local subdirectory name for storing the model.
    /// </summary>
    public string LocalDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Whether this model is required (vs optional).
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// Additional files to download alongside the main model file.
    /// </summary>
    public string[] AdditionalFiles { get; init; } = [];

    /// <summary>
    /// Alternate filenames to try if the primary filename is unavailable.
    /// </summary>
    public string[] AlternateFilenames { get; init; } = [];

    /// <summary>
    /// Optional list of file suffixes or exact filenames to include when
    /// scanning a repository for downloadable files.
    /// </summary>
    public string[] RepoFileIncludes { get; init; } = [];

    /// <summary>
    /// Whether to scan the repository file list when primary files are missing.
    /// </summary>
    public bool AllowRepoScan { get; init; }

    /// <summary>
    /// Whether the model's HuggingFace repo and files are available for download.
    /// Set to false for placeholder/future models whose ONNX exports don't exist yet.
    /// </summary>
    public bool IsAvailableForDownload { get; init; } = true;
}
