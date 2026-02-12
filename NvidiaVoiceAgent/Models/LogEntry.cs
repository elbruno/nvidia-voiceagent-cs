using System.Text.Json.Serialization;

namespace NvidiaVoiceAgent.Models;

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
