namespace NvidiaVoiceAgent.Core.Services;

/// <summary>
/// Text-to-Speech service interface.
/// Uses NVIDIA FastPitch + HiFiGAN models.
/// </summary>
public interface ITtsService
{
    /// <summary>
    /// Convert text to speech audio.
    /// </summary>
    /// <param name="text">Text to synthesize</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Audio samples at 22050Hz</returns>
    Task<float[]> SynthesizeAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load the TTS models into memory.
    /// </summary>
    Task LoadModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the models are loaded and ready.
    /// </summary>
    bool AreModelsLoaded { get; }
}
