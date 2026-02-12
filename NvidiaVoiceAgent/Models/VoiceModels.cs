using System.Text.Json.Serialization;

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

/// <summary>
/// Session state for a voice WebSocket connection.
/// </summary>
public class VoiceSessionState
{
    public bool SmartMode { get; set; } = false;
    public string SmartModel { get; set; } = "phi3";
    public List<ChatMessage> ChatHistory { get; set; } = new();
}

/// <summary>
/// A message in the chat history.
/// </summary>
public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Incoming config message from client.
/// </summary>
public class ConfigMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("smart_mode")]
    public bool? SmartMode { get; set; }

    [JsonPropertyName("smart_model")]
    public string? SmartModel { get; set; }
}

/// <summary>
/// Transcript response sent to client.
/// </summary>
public class TranscriptResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "transcript";

    [JsonPropertyName("transcript")]
    public string Transcript { get; set; } = string.Empty;
}

/// <summary>
/// Thinking indicator sent to client.
/// </summary>
public class ThinkingResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "thinking";
}

/// <summary>
/// Full response with audio sent to client.
/// </summary>
public class VoiceResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "response";

    [JsonPropertyName("transcript")]
    public string Transcript { get; set; } = string.Empty;

    [JsonPropertyName("response")]
    public string Response { get; set; } = string.Empty;

    [JsonPropertyName("audio")]
    public string Audio { get; set; } = string.Empty; // Base64 encoded WAV
}

/// <summary>
/// Voice agent operating mode.
/// </summary>
public enum VoiceMode
{
    /// <summary>
    /// Echo mode: ASR → TTS (parrot back).
    /// </summary>
    Echo,

    /// <summary>
    /// Smart mode: ASR → LLM → TTS (AI response).
    /// </summary>
    Smart
}

/// <summary>
/// WebSocket message for voice commands.
/// </summary>
public class VoiceMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("audio")]
    public byte[]? AudioData { get; set; }
}

/// <summary>
/// Log entry for broadcast to clients.
/// </summary>
public class LogEntry
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("level")]
    public string Level { get; set; } = "info";

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Health check response.
/// </summary>
public class HealthStatus
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "healthy";

    [JsonPropertyName("asr_loaded")]
    public bool AsrLoaded { get; set; }

    [JsonPropertyName("tts_loaded")]
    public bool TtsLoaded { get; set; }

    [JsonPropertyName("llm_loaded")]
    public bool LlmLoaded { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
