using Microsoft.Extensions.Logging;

namespace NvidiaVoiceAgent.Core.Services;

/// <summary>
/// Energy-based Voice Activity Detector (VAD) for MVP implementation.
/// Analyzes audio energy and spectral features to detect speech.
/// 
/// This is a lightweight implementation suitable for real-time use.
/// For higher accuracy, consider integrating Silero VAD (ONNX model) in Phase 2.
/// </summary>
public class EnergyBasedVad : IVoiceActivityDetector
{
    private readonly ILogger<EnergyBasedVad> _logger;
    private float[] _previousFrameEnergy = Array.Empty<float>();
    private const int FrameSize = 512; // At 16kHz: 32ms frames

    /// <summary>
    /// Energy threshold for speech detection. Default: 0.02
    /// Adjust based on microphone and environment:
    /// - Quiet environment: 0.01-0.02
    /// - Noisy environment: 0.05-0.10
    /// </summary>
    public float SilenceThreshold { get; set; } = 0.02f;

    public EnergyBasedVad(ILogger<EnergyBasedVad> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyze audio chunk and return speech confidence (0.0-1.0).
    /// </summary>
    public float AnalyzeAudio(float[] samples)
    {
        if (samples == null || samples.Length == 0)
            return 0.0f;

        // Calculate RMS energy
        float rmsEnergy = CalculateRmsEnergy(samples);

        // Calculate spectral centroid (higher for speech, lower for silence)
        float spectralCentroid = CalculateSpectralCentroid(samples);

        // Calculate zero-crossing rate (speech typically has higher ZCR)
        float zcr = CalculateZeroCrossingRate(samples);

        // Combine features with weighted scoring
        float confidence = CombineFeatures(rmsEnergy, spectralCentroid, zcr);

        _logger.LogDebug(
            "VAD analysis - RMS: {Rms:F4}, Spectral: {Spectral:F2}, ZCR: {Zcr:F3}, Confidence: {Conf:F2}",
            rmsEnergy, spectralCentroid, zcr, confidence);

        return confidence;
    }

    /// <summary>
    /// Reset internal state for new recording session.
    /// </summary>
    public void Reset()
    {
        _previousFrameEnergy = Array.Empty<float>();
        _logger.LogDebug("VAD reset for new session");
    }

    /// <summary>
    /// Calculate RMS (Root Mean Square) energy of audio samples.
    /// </summary>
    private float CalculateRmsEnergy(float[] samples)
    {
        if (samples.Length == 0)
            return 0.0f;

        float sumSquares = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            sumSquares += samples[i] * samples[i];
        }

        float rms = (float)Math.Sqrt(sumSquares / samples.Length);

        // Normalize to 0-1 range
        // Typical speech energy: 0.01-0.5
        // Peak digital audio: 1.0
        return Math.Min(1.0f, rms / 0.1f);
    }

    /// <summary>
    /// Simple spectral centroid estimation using energy distribution.
    /// Speech typically concentrates energy in mid-frequencies.
    /// </summary>
    private float CalculateSpectralCentroid(float[] samples)
    {
        if (samples.Length < FrameSize)
            return 0.0f;

        // Frame-based analysis
        float totalCentroid = 0;
        int frameCount = 0;

        for (int i = 0; i + FrameSize <= samples.Length; i += FrameSize / 2)
        {
            var frame = samples.Skip(i).Take(FrameSize).ToArray();
            float frameEnergy = CalculateRmsEnergy(frame);
            totalCentroid += frameEnergy;
            frameCount++;
        }

        return frameCount > 0 ? totalCentroid / frameCount : 0.0f;
    }

    /// <summary>
    /// Calculate zero-crossing rate (ZCR).
    /// Speech typically has higher ZCR than silent periods.
    /// </summary>
    private float CalculateZeroCrossingRate(float[] samples)
    {
        if (samples.Length < 2)
            return 0.0f;

        int zeroCrossings = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            // Count sign changes
            if ((samples[i - 1] >= 0 && samples[i] < 0) ||
                (samples[i - 1] < 0 && samples[i] >= 0))
            {
                zeroCrossings++;
            }
        }

        // Normalize ZCR to 0-1 range
        // Max possible ZCR is when signal alternates every sample
        float maxZcr = samples.Length / 2.0f;
        return Math.Min(1.0f, zeroCrossings / maxZcr);
    }

    /// <summary>
    /// Combine VAD features with weighted scoring to produce final confidence.
    /// </summary>
    private float CombineFeatures(float rmsEnergy, float spectralCentroid, float zcr)
    {
        // Weight the features (can be tuned based on environment)
        float weightedScore =
            (rmsEnergy * 0.5f) +              // Energy is primary indicator
            (spectralCentroid * 0.3f) +       // Spectral characteristics
            (zcr * 0.2f);                     // ZCR as secondary indicator

        // Apply threshold
        if (weightedScore < SilenceThreshold)
            return 0.0f;

        // Return confidence, clamped to 0-1
        return Math.Min(1.0f, weightedScore);
    }
}
