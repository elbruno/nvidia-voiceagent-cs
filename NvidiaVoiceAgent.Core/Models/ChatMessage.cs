namespace NvidiaVoiceAgent.Core.Models;

/// <summary>
/// Represents a chat message in a conversation.
/// </summary>
public class ChatMessage
{
    /// <summary>
    /// Role of the message sender (e.g., "user", "assistant", "system").
    /// </summary>
    public required string Role { get; set; }

    /// <summary>
    /// Content of the message.
    /// </summary>
    public required string Content { get; set; }
}
