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
    /// Enable GPU acceleration for ONNX Runtime.
    /// </summary>
    public bool UseGpu { get; set; } = true;

    /// <summary>
    /// Use 4-bit quantization for LLM models (reduces memory usage).
    /// </summary>
    public bool Use4BitQuantization { get; set; } = true;
}
