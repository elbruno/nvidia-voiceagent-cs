namespace NvidiaVoiceAgent.Models;

/// <summary>
/// Audio stream chunk for realtime TTS output.
/// </summary>
public class AudioStreamChunk
{
    /// <summary>
    /// Type identifier for the message.
    /// </summary>
    public string Type { get; init; } = "audio_chunk";

    /// <summary>
    /// Base64-encoded audio data (WAV format).
    /// </summary>
    public required string AudioBase64 { get; init; }

    /// <summary>
    /// Whether this is the final audio chunk (true) or more chunks are coming (false).
    /// </summary>
    public bool IsFinal { get; init; }
}
