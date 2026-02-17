namespace NvidiaVoiceAgent.Services;

/// <summary>
/// Service interface for recording audio conversations in debug mode.
/// </summary>
public interface IDebugAudioRecorder
{
    /// <summary>
    /// Whether debug mode is enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Start a new debug session.
    /// </summary>
    /// <param name="sessionId">Unique session identifier</param>
    /// <param name="smartMode">Whether smart mode is enabled</param>
    /// <param name="smartModel">LLM model name (if applicable)</param>
    /// <param name="realtimeMode">Whether realtime mode is enabled</param>
    /// <returns>Session directory path</returns>
    Task<string> StartSessionAsync(string sessionId, bool smartMode, string? smartModel, bool realtimeMode);

    /// <summary>
    /// Save incoming audio (user voice) for the current turn.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="turnNumber">Turn number (1-indexed)</param>
    /// <param name="audioData">WAV audio data</param>
    /// <returns>File path where audio was saved</returns>
    Task<string> SaveIncomingAudioAsync(string sessionId, int turnNumber, byte[] audioData);

    /// <summary>
    /// Save outgoing audio (TTS response) for the current turn.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="turnNumber">Turn number (1-indexed)</param>
    /// <param name="audioData">WAV audio data</param>
    /// <returns>File path where audio was saved</returns>
    Task<string> SaveOutgoingAudioAsync(string sessionId, int turnNumber, byte[] audioData);

    /// <summary>
    /// Record a conversation turn with transcript and audio references.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="turnNumber">Turn number (1-indexed)</param>
    /// <param name="userTranscript">User's transcribed speech</param>
    /// <param name="assistantResponse">Assistant's response text</param>
    /// <param name="userAudioFile">Path to user audio file</param>
    /// <param name="assistantAudioFile">Path to assistant audio file</param>
    Task RecordTurnAsync(
        string sessionId,
        int turnNumber,
        string userTranscript,
        string assistantResponse,
        string? userAudioFile = null,
        string? assistantAudioFile = null);

    /// <summary>
    /// End the debug session and finalize metadata.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    Task EndSessionAsync(string sessionId);

    /// <summary>
    /// Clean up old debug files based on MaxAgeInDays configuration.
    /// </summary>
    Task CleanupOldFilesAsync();
}
