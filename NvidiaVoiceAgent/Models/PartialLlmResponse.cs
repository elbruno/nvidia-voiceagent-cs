namespace NvidiaVoiceAgent.Models;

/// <summary>
/// Response containing partial LLM-generated text.
/// Used in realtime conversation mode for streaming responses.
/// </summary>
public class PartialLlmResponse
{
    /// <summary>
    /// Type identifier for the message.
    /// </summary>
    public string Type { get; init; } = "partial_llm";

    /// <summary>
    /// Partial or complete LLM response text.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Whether this is the complete response (true) or more chunks are coming (false).
    /// </summary>
    public bool IsComplete { get; init; }
}
