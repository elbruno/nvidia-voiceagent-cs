using System.Text.Json.Serialization;

namespace NvidiaVoiceAgent.Models;

/// <summary>
/// Health check response.
/// </summary>
public class HealthStatus
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "healthy";

    [JsonPropertyName("asr_loaded")]
    public bool AsrLoaded { get; set; }

    [JsonPropertyName("asr_downloaded")]
    public bool AsrDownloaded { get; set; }

    [JsonPropertyName("tts_loaded")]
    public bool TtsLoaded { get; set; }

    [JsonPropertyName("llm_loaded")]
    public bool LlmLoaded { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
