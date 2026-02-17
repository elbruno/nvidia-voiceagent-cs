namespace NvidiaVoiceAgent.Models;

/// <summary>
/// Metadata for a debug mode conversation session.
/// </summary>
public class SessionMetadata
{
    /// <summary>
    /// Unique session identifier.
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// Session start timestamp.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Session end timestamp (if completed).
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// List of conversation turns in this session.
    /// </summary>
    public List<ConversationTurn> Turns { get; set; } = new();

    /// <summary>
    /// Session configuration (smart mode, model, etc.).
    /// </summary>
    public SessionConfiguration? Configuration { get; set; }
}

/// <summary>
/// Represents a single conversation turn (user input + assistant response).
/// </summary>
public class ConversationTurn
{
    /// <summary>
    /// Turn number (1-indexed).
    /// </summary>
    public int TurnNumber { get; set; }

    /// <summary>
    /// Timestamp when the turn started.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// User's transcribed speech.
    /// </summary>
    public required string UserTranscript { get; set; }

    /// <summary>
    /// Assistant's response text.
    /// </summary>
    public required string AssistantResponse { get; set; }

    /// <summary>
    /// File path to the user's audio recording (incoming).
    /// </summary>
    public string? UserAudioFile { get; set; }

    /// <summary>
    /// File path to the assistant's audio response (outgoing).
    /// </summary>
    public string? AssistantAudioFile { get; set; }

    /// <summary>
    /// Duration of user audio in seconds.
    /// </summary>
    public double? UserAudioDuration { get; set; }

    /// <summary>
    /// Duration of assistant audio in seconds.
    /// </summary>
    public double? AssistantAudioDuration { get; set; }
}

/// <summary>
/// Session configuration snapshot.
/// </summary>
public class SessionConfiguration
{
    /// <summary>
    /// Whether smart mode (LLM) was enabled.
    /// </summary>
    public bool SmartMode { get; set; }

    /// <summary>
    /// LLM model name (if smart mode enabled).
    /// </summary>
    public string? SmartModel { get; set; }

    /// <summary>
    /// Whether realtime conversation mode was enabled.
    /// </summary>
    public bool RealtimeMode { get; set; }
}
