using System.Text.Json.Serialization;

namespace NvidiaVoiceAgent.Models;

/// <summary>
/// Thinking indicator sent to client.
/// </summary>
public class ThinkingResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "thinking";
}
