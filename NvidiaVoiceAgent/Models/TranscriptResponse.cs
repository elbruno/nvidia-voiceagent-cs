using System.Text.Json.Serialization;

namespace NvidiaVoiceAgent.Models;

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
