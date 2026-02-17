namespace NvidiaVoiceAgent.Core.Models;

/// <summary>
/// Configuration for AI model paths and settings.
/// </summary>
public class ModelConfig
{
    /// <summary>
    /// Path to the ASR (Automatic Speech Recognition) model directory.
    /// </summary>
    public string AsrModelPath { get; set; } = "models/parakeet-tdt-0.6b";

    /// <summary>
    /// Path to the FastPitch TTS model directory.
    /// </summary>
    public string FastPitchModelPath { get; set; } = "models/fastpitch";

    /// <summary>
    /// Path to the HiFi-GAN vocoder model directory.
    /// </summary>
    public string HifiGanModelPath { get; set; } = "models/hifigan";

    /// <summary>
    /// Path to the LLM (Large Language Model) directory.
    /// </summary>
    public string LlmModelPath { get; set; } = "models/tinyllama";

    /// <summary>
    /// Path to the PersonaPlex model directory.
    /// </summary>
    public string PersonaPlexModelPath { get; set; } = "model-cache/personaplex-7b";

    /// <summary>
    /// Selected voice embedding for PersonaPlex (e.g., "voice_0", "voice_1", etc.).
    /// PersonaPlex includes 18 pre-packaged voices.
    /// </summary>
    public string PersonaPlexVoice { get; set; } = "voice_0";

    /// <summary>
    /// Enable GPU acceleration for ONNX Runtime.
    /// </summary>
    public bool UseGpu { get; set; } = true;

    /// <summary>
    /// Use 4-bit quantization for LLM models (reduces memory usage).
    /// </summary>
    public bool Use4BitQuantization { get; set; } = true;
}
