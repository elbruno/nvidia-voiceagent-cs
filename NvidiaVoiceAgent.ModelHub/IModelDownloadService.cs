namespace NvidiaVoiceAgent.ModelHub;

/// <summary>
/// Service for downloading and managing AI models from HuggingFace.
/// </summary>
public interface IModelDownloadService
{
    /// <summary>
    /// Ensure all required models are available locally.
    /// Downloads missing models if auto-download is enabled.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of download results for each model.</returns>
    Task<IReadOnlyList<DownloadResult>> EnsureModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Download a specific model by type.
    /// </summary>
    /// <param name="modelType">Type of model to download.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Download result.</returns>
    Task<DownloadResult> DownloadModelAsync(ModelType modelType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the local file path for a model, or null if not available.
    /// </summary>
    /// <param name="modelType">Type of model to locate.</param>
    /// <returns>Local file path, or null if model is not downloaded.</returns>
    string? GetModelPath(ModelType modelType);

    /// <summary>
    /// Check if a specific model is available locally.
    /// </summary>
    /// <param name="modelType">Type of model to check.</param>
    /// <returns>True if the model is available locally.</returns>
    bool IsModelAvailable(ModelType modelType);

    /// <summary>
    /// Delete a model and all its related files from the local cache.
    /// </summary>
    /// <param name="modelType">Type of model to delete.</param>
    /// <returns>True if files were deleted, false if model was not found on disk.</returns>
    bool DeleteModel(ModelType modelType);
}
