using System.Text.Json.Serialization;

namespace NvidiaVoiceAgent.Models;

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
