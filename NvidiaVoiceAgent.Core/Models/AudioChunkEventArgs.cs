namespace NvidiaVoiceAgent.Core.Models;

/// <summary>
/// Event arguments for audio chunk events.
/// </summary>
public class AudioChunkEventArgs : EventArgs
{
    /// <summary>
    /// Audio samples in the chunk.
    /// </summary>
    public required float[] Samples { get; init; }

    /// <summary>
    /// Sample rate of the audio (e.g., 16000 Hz).
    /// </summary>
    public int SampleRate { get; init; }

    /// <summary>
    /// Timestamp when the chunk was received.
    /// </summary>
    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;
}
