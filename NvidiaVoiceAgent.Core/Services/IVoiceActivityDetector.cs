namespace NvidiaVoiceAgent.Core.Services;

/// <summary>
/// Voice Activity Detector (VAD) interface for detecting speech vs silence in audio streams.
/// </summary>
public interface IVoiceActivityDetector
{
    /// <summary>
    /// Analyze an audio chunk and return speech confidence.
    /// </summary>
    /// <param name="samples">Audio samples (16kHz, mono, float32)</param>
    /// <returns>Confidence value 0.0 (silence) to 1.0 (definite speech)</returns>
    float AnalyzeAudio(float[] samples);

    /// <summary>
    /// Get or set the energy-based silence threshold (0.0-1.0 scale).
    /// Above this threshold, audio is considered speech.
    /// </summary>
    float SilenceThreshold { get; set; }

    /// <summary>
    /// Reset any internal state (e.g., for new recording session).
    /// </summary>
    void Reset();
}
