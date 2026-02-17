using NvidiaVoiceAgent.Core.Models;

namespace NvidiaVoiceAgent.Models;

/// <summary>
/// Session state for a voice WebSocket connection.
/// </summary>
public class VoiceSessionState
{
    public bool SmartMode { get; set; } = false;
    public string SmartModel { get; set; } = "phi3";
    public List<ChatMessage> ChatHistory { get; set; } = new();

    // Real-time conversation mode fields
    /// <summary>
    /// Enable continuous audio streaming and pause-based processing.
    /// When true, audio is continuously buffered and processed on pause detection.
    /// </summary>
    public bool RealtimeMode { get; set; } = false;

    /// <summary>
    /// Pause detection threshold in milliseconds (default: 800ms).
    /// When audio silence exceeds this duration, processing is triggered.
    /// </summary>
    public int PauseThresholdMs { get; set; } = 800;

    /// <summary>
    /// Timestamp when audio was last received.
    /// </summary>
    public DateTime LastAudioReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Current count of pending audio samples in buffer.
    /// </summary>
    public int PendingAudioSampleCount { get; set; } = 0;

    /// <summary>
    /// Current conversation turn number (1-indexed).
    /// Used for debug mode audio recording.
    /// </summary>
    public int CurrentTurnNumber { get; set; } = 0;
}
