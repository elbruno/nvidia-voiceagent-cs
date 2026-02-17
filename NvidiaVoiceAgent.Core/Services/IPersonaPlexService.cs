namespace NvidiaVoiceAgent.Core.Services;

/// <summary>
/// PersonaPlex service interface for full-duplex speech-to-speech AI.
/// PersonaPlex is an NVIDIA model based on Moshi architecture that handles
/// real-time conversational AI with voice and persona control.
/// </summary>
public interface IPersonaPlexService
{
    /// <summary>
    /// Generate a conversational response using PersonaPlex.
    /// </summary>
    /// <param name="prompt">User's query or conversation text</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generated response text</returns>
    Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a conversational response directly from audio using PersonaPlex's
    /// speech-to-speech capabilities.
    /// </summary>
    /// <param name="audioSamples">Input audio samples (float32, 24kHz mono recommended)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generated response audio samples (float32, 24kHz mono)</returns>
    Task<float[]> GenerateSpeechResponseAsync(float[] audioSamples, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load the PersonaPlex model into memory.
    /// This includes loading the main model, tokenizer, and voice embeddings.
    /// </summary>
    Task LoadModelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the PersonaPlex model is loaded and ready.
    /// </summary>
    bool IsModelLoaded { get; }

    /// <summary>
    /// Set the voice persona for PersonaPlex responses.
    /// PersonaPlex includes 18 pre-packaged voices (voice_0 through voice_17).
    /// </summary>
    /// <param name="voiceId">Voice identifier (e.g., "voice_0", "voice_1", etc.)</param>
    void SetVoice(string voiceId);

    /// <summary>
    /// Get the currently selected voice ID.
    /// </summary>
    string CurrentVoice { get; }
}
