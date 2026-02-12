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
                Filename = opts.UseInt8Quantization ? "onnx/encoder_model_int8.onnx" : "onnx/encoder_model.onnx",
                ExpectedSizeBytes = opts.UseInt8Quantization ? 683_671_552L : 1_267_343_360L,
                LocalDirectory = "parakeet-tdt-0.6b",
                IsRequired = true,
                AdditionalFiles = ["config.json"]
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
