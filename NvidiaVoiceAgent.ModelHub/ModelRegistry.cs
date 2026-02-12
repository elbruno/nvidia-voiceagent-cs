using Microsoft.Extensions.Options;

namespace NvidiaVoiceAgent.ModelHub;

/// <summary>
/// Default registry of model definitions for NVIDIA Voice Agent.
/// </summary>
public class ModelRegistry : IModelRegistry
{
    private readonly List<ModelInfo> _models;

    public ModelRegistry(IOptions<ModelHubOptions> options)
    {
        var opts = options.Value;

        _models = new List<ModelInfo>
        {
            new ModelInfo
            {
                Type = ModelType.Asr,
                Name = "Parakeet-TDT-0.6B-V2",
                RepoId = "onnx-community/parakeet-tdt-0.6b-v2-ONNX",
                // Note: The ONNX file is split into encoder.onnx (671 KB) and encoder.onnx_data (2.48 GB)
                Filename = "onnx/encoder.onnx",
                ExpectedSizeBytes = 2_665_185_280L, // ~2.48 GB for encoder.onnx_data + encoder.onnx
                LocalDirectory = "parakeet-tdt-0.6b",
                IsRequired = true,
                AdditionalFiles = ["onnx/encoder.onnx_data", "onnx/decoder.onnx"]
            }
        };
    }

    /// <inheritdoc />
    public ModelInfo? GetModel(ModelType modelType)
    {
        return _models.FirstOrDefault(m => m.Type == modelType);
    }

    /// <inheritdoc />
    public IReadOnlyList<ModelInfo> GetAllModels()
    {
        return _models.AsReadOnly();
    }

    /// <inheritdoc />
    public IReadOnlyList<ModelInfo> GetRequiredModels()
    {
        return _models.Where(m => m.IsRequired).ToList().AsReadOnly();
    }
}
