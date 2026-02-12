using System.Text.Json.Serialization;

namespace NvidiaVoiceAgent.Models;

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
