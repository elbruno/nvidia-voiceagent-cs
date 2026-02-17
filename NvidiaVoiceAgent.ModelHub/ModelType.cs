namespace NvidiaVoiceAgent.ModelHub;

/// <summary>
/// Types of AI models supported by the voice agent.
/// </summary>
public enum ModelType
{
    /// <summary>
    /// Automatic Speech Recognition (Speech-to-Text).
    /// </summary>
    Asr,

    /// <summary>
    /// Text-to-Speech mel spectrogram generator.
    /// </summary>
    Tts,

    /// <summary>
    /// Text-to-Speech vocoder.
    /// </summary>
    Vocoder,

    /// <summary>
    /// Large Language Model for smart mode.
    /// </summary>
    Llm,

    /// <summary>
    /// PersonaPlex full-duplex speech-to-speech model.
    /// </summary>
    PersonaPlex
}
