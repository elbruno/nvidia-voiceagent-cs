namespace NvidiaVoiceAgent.Models;

/// <summary>
/// Response containing a partial transcript with confidence score.
/// Used in realtime conversation mode.
/// </summary>
public class PartialTranscriptResponse
{
    /// <summary>
    /// Type identifier for the message.
    /// </summary>
    public string Type { get; init; } = "partial_transcript";

    /// <summary>
    /// Partial or complete transcribed text.
    /// </summary>
    public required string Transcript { get; init; }

    /// <summary>
    /// Confidence score (0.0 - 1.0).
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// Whether this is a partial result (true) or final result (false).
    /// </summary>
    public bool IsPartial { get; init; }
}
