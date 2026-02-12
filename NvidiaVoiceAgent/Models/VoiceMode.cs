namespace NvidiaVoiceAgent.Models;

/// <summary>
/// Voice agent operating mode.
/// </summary>
public enum VoiceMode
{
    /// <summary>
    /// Echo mode: ASR → TTS (parrot back).
    /// </summary>
    Echo,

    /// <summary>
    /// Smart mode: ASR → LLM → TTS (AI response).
    /// </summary>
    Smart
}
