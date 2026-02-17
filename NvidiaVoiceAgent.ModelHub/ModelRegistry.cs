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
            },
            new ModelInfo
            {
                Type = ModelType.Tts,
                Name = "FastPitch-HiFiGAN-EN",
                RepoId = "nvidia/tts_en_fastpitch",
                Filename = "model.onnx",
                ExpectedSizeBytes = 85_000_000L, // ~80 MB (placeholder)
                LocalDirectory = "fastpitch-en",
                IsRequired = false,
                AdditionalFiles = [],
                IsAvailableForDownload = true
            },
            new ModelInfo
            {
                Type = ModelType.Vocoder,
                Name = "HiFiGAN-EN",
                RepoId = "nvidia/tts_hifigan",
                Filename = "model.onnx",
                ExpectedSizeBytes = 55_000_000L, // ~52 MB (placeholder)
                LocalDirectory = "hifigan-en",
                IsRequired = false,
                AdditionalFiles = [],
                IsAvailableForDownload = true
            },
            new ModelInfo
            {
                Type = ModelType.Llm,
                Name = "TinyLlama-1.1B-ONNX",
                RepoId = "TinyLlama/TinyLlama-1.1B-Chat-v1.0",
                Filename = "model.onnx",
                ExpectedSizeBytes = 2_200_000_000L, // ~2.05 GB (placeholder)
                LocalDirectory = "tinyllama-1.1b",
                IsRequired = false,
                AdditionalFiles = [],
                IsAvailableForDownload = true
            },
            new ModelInfo
            {
                Type = ModelType.PersonaPlex,
                Name = "PersonaPlex-7B-v1",
                RepoId = "nvidia/personaplex-7b-v1",
                Filename = "model.safetensors",
                ExpectedSizeBytes = 17_900_000_000L, // ~16.7 GB for model.safetensors
                LocalDirectory = "personaplex-7b",
                IsRequired = false,
                AdditionalFiles = [
                    "tokenizer-e351c8d8-checkpoint125.safetensors",  // ~385 MB
                    "tokenizer_spm_32k_3.model",                     // ~553 KB
                    "voices.tgz"                                     // ~6.1 MB
                ],
                IsAvailableForDownload = true
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
