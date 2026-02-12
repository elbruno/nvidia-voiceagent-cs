namespace NvidiaVoiceAgent.Services;

/// <summary>
/// Audio processing service for format conversion.
/// Handles WAV decoding and resampling to 16kHz.
/// </summary>
public interface IAudioProcessor
{
    /// <summary>
    /// Decode WAV bytes to float samples.
    /// </summary>
    /// <param name="wavData">Raw WAV file bytes</param>
    /// <returns>Decoded audio samples</returns>
    float[] DecodeWav(byte[] wavData);

    /// <summary>
    /// Resample audio to target sample rate.
    /// </summary>
    /// <param name="samples">Input audio samples</param>
    /// <param name="sourceSampleRate">Source sample rate</param>
    /// <param name="targetSampleRate">Target sample rate (default 16000)</param>
    /// <returns>Resampled audio</returns>
    float[] Resample(float[] samples, int sourceSampleRate, int targetSampleRate = 16000);

    /// <summary>
    /// Encode float samples to WAV bytes.
    /// </summary>
    /// <param name="samples">Audio samples</param>
    /// <param name="sampleRate">Sample rate</param>
    /// <returns>WAV file bytes</returns>
    byte[] EncodeWav(float[] samples, int sampleRate);
}
