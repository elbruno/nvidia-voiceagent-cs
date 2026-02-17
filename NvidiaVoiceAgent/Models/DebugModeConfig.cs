namespace NvidiaVoiceAgent.Models;

/// <summary>
/// Configuration options for debug mode audio recording.
/// </summary>
public class DebugModeConfig
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "DebugMode";

    /// <summary>
    /// Enable debug mode to save conversation audio to disk.
    /// Default: false
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Directory path where audio debug files will be saved.
    /// Default: "logs/audio-debug"
    /// </summary>
    public string AudioLogPath { get; set; } = "logs/audio-debug";

    /// <summary>
    /// Save incoming audio (user voice) to WAV files.
    /// Default: true
    /// </summary>
    public bool SaveIncomingAudio { get; set; } = true;

    /// <summary>
    /// Save outgoing audio (TTS responses) to WAV files.
    /// Default: true
    /// </summary>
    public bool SaveOutgoingAudio { get; set; } = true;

    /// <summary>
    /// Save session metadata (transcript, timestamps, etc.) to JSON files.
    /// Default: true
    /// </summary>
    public bool SaveMetadata { get; set; } = true;

    /// <summary>
    /// Maximum age in days for audio debug files before they are deleted.
    /// Set to 0 to keep files indefinitely.
    /// Default: 7 days
    /// </summary>
    public int MaxAgeInDays { get; set; } = 7;
}
