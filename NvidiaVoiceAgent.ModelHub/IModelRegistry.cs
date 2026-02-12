namespace NvidiaVoiceAgent.ModelHub;

/// <summary>
/// Registry of available model definitions and their download metadata.
/// </summary>
public interface IModelRegistry
{
    /// <summary>
    /// Get model information by type.
    /// </summary>
    /// <param name="modelType">Type of model to look up.</param>
    /// <returns>Model information, or null if not registered.</returns>
    ModelInfo? GetModel(ModelType modelType);

    /// <summary>
    /// Get all registered models.
    /// </summary>
    /// <returns>Collection of all registered model definitions.</returns>
    IReadOnlyList<ModelInfo> GetAllModels();

    /// <summary>
    /// Get all required models.
    /// </summary>
    /// <returns>Collection of required model definitions.</returns>
    IReadOnlyList<ModelInfo> GetRequiredModels();
}
