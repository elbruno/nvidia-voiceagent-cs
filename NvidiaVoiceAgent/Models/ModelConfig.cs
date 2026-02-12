namespace NvidiaVoiceAgent.Models;

/// <summary>
/// Configuration for AI models (ASR, TTS, LLM).
/// </summary>
public class ModelConfig
{
    /// <summary>
    /// Path to the ASR model (NVIDIA Parakeet-TDT-0.6B-V2).
    /// </summary>
    public string AsrModelPath { get; set; } = "models/parakeet-tdt-0.6b";

    /// <summary>
    /// Path to the FastPitch TTS model.
    /// </summary>
    public string FastPitchModelPath { get; set; } = "models/fastpitch";

    /// <summary>
    /// Path to the HiFiGAN vocoder model.
    /// </summary>
    public string HifiGanModelPath { get; set; } = "models/hifigan";

    /// <summary>
    /// Path to the LLM model (TinyLlama or Phi-3).
    /// </summary>
    public string LlmModelPath { get; set; } = "models/tinyllama";

    /// <summary>
    /// Use GPU acceleration if available.
    /// </summary>
    public bool UseGpu { get; set; } = true;

    /// <summary>
    /// Use 4-bit quantization for LLM (reduces memory).
    /// </summary>
    public bool Use4BitQuantization { get; set; } = true;
}
