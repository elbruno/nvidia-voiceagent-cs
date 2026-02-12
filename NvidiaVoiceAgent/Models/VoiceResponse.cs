using System.Text.Json.Serialization;

namespace NvidiaVoiceAgent.Models;

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
