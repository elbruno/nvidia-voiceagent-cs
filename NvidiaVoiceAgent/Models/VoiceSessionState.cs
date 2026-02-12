namespace NvidiaVoiceAgent.Models;

/// <summary>
/// Session state for a voice WebSocket connection.
/// </summary>
public class VoiceSessionState
{
    public bool SmartMode { get; set; } = false;
    public string SmartModel { get; set; } = "phi3";
    public List<ChatMessage> ChatHistory { get; set; } = new();
}
