namespace NvidiaVoiceAgent.Core.Services;

/// <summary>
/// Automatic Speech Recognition service interface.
/// Uses NVIDIA Parakeet-TDT-0.6B-V2 model.
/// </summary>
public interface IAsrService
{
    /// <summary>
    /// Transcribe audio samples to text.
    /// </summary>
    /// <param name="audioSamples">16kHz mono float audio samples</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Transcribed text</returns>
    Task<string> TranscribeAsync(float[] audioSamples, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribe audio samples with partial result capability for streaming.
    /// Returns both transcript and confidence level.
    /// </summary>
    /// <param name="audioSamples">16kHz mono float audio samples</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple of (transcript, confidence)</returns>
    Task<(string transcript, float confidence)> TranscribePartialAsync(
        float[] audioSamples,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Load the ASR model into memory.
    /// </summary>
    Task LoadModelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the model is loaded and ready.
    /// </summary>
    bool IsModelLoaded { get; }
}
