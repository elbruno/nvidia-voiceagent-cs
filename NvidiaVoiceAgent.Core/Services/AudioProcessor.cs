using System.Buffers.Binary;
using Microsoft.Extensions.Logging;

namespace NvidiaVoiceAgent.Core.Services;

/// <summary>
/// Audio processing service for WAV format handling.
/// Decodes WAV to float samples and encodes float samples to WAV.
/// </summary>
public class AudioProcessor : IAudioProcessor
{
    private readonly ILogger<AudioProcessor> _logger;

    public AudioProcessor(ILogger<AudioProcessor> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public float[] DecodeWav(byte[] wavData)
    {
        if (wavData == null || wavData.Length < 44)
        {
            _logger.LogWarning("Invalid WAV data: too short");
            return Array.Empty<float>();
        }

        try
        {
            // Parse WAV header
            var riff = System.Text.Encoding.ASCII.GetString(wavData, 0, 4);
            if (riff != "RIFF")
            {
                _logger.LogWarning("Invalid WAV data: missing RIFF header");
                return Array.Empty<float>();
            }

            var wave = System.Text.Encoding.ASCII.GetString(wavData, 8, 4);
            if (wave != "WAVE")
            {
                _logger.LogWarning("Invalid WAV data: missing WAVE format");
                return Array.Empty<float>();
            }

            // Find fmt chunk
            int offset = 12;
            int audioFormat = 1;
            int numChannels = 1;
            int sampleRate = 16000;
            int bitsPerSample = 16;

            while (offset < wavData.Length - 8)
            {
                var chunkId = System.Text.Encoding.ASCII.GetString(wavData, offset, 4);
                var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(wavData.AsSpan(offset + 4, 4));

                if (chunkId == "fmt ")
                {
                    audioFormat = BinaryPrimitives.ReadInt16LittleEndian(wavData.AsSpan(offset + 8, 2));
                    numChannels = BinaryPrimitives.ReadInt16LittleEndian(wavData.AsSpan(offset + 10, 2));
                    sampleRate = BinaryPrimitives.ReadInt32LittleEndian(wavData.AsSpan(offset + 12, 4));
                    bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(wavData.AsSpan(offset + 22, 2));

                    // Validate audio format
                    if (audioFormat != 1)
                    {
                        _logger.LogWarning("Unsupported audio format: {Format} (only PCM supported)", audioFormat);
                    }
                    if (numChannels < 1 || numChannels > 2)
                    {
                        _logger.LogWarning("Unsupported channel count: {Channels} (mono/stereo supported only)", numChannels);
                    }
                }
                else if (chunkId == "data")
                {
                    // Extract audio data
                    var dataOffset = offset + 8;
                    var dataLength = Math.Min(chunkSize, wavData.Length - dataOffset);
                    var samples = DecodePcmData(wavData, dataOffset, dataLength, bitsPerSample, numChannels);

                    // Log decoded audio info
                    _logger.LogInformation(\"Decoded WAV: {Samples} samples, {SampleRate}Hz, {Channels} channel(s), {BitsPerSample}-bit\",
                        samples.Length, sampleRate, numChannels, bitsPerSample);

                    // Auto-resample to 16kHz if needed
                    if (sampleRate != 16000)
                    {
                        _logger.LogInformation(\"Resampling from {SourceRate}Hz to 16000Hz for ASR\", sampleRate);
                        samples = Resample(samples, sampleRate, 16000);
                    }

                    return samples;
                }

                offset += 8 + chunkSize;
                // Align to 2-byte boundary
                if (chunkSize % 2 != 0) offset++;
            }

            _logger.LogWarning("Invalid WAV data: no data chunk found");
            return Array.Empty<float>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decoding WAV data");
            return Array.Empty<float>();
        }
    }

    private float[] DecodePcmData(byte[] data, int offset, int length, int bitsPerSample, int numChannels)
    {
        int bytesPerSample = bitsPerSample / 8;
        int numSamples = length / (bytesPerSample * numChannels);
        var samples = new float[numSamples];

        for (int i = 0; i < numSamples; i++)
        {
            int sampleOffset = offset + i * bytesPerSample * numChannels;

            // Read first channel (mono or left channel)
            float sample = bitsPerSample switch
            {
                8 => (data[sampleOffset] - 128) / 128.0f,
                16 => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(sampleOffset, 2)) / 32768.0f,
                24 => Read24BitSample(data, sampleOffset) / 8388608.0f,
                32 => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(sampleOffset, 4)) / 2147483648.0f,
                _ => 0
            };

            // If stereo, average both channels
            if (numChannels == 2)
            {
                int rightOffset = sampleOffset + bytesPerSample;
                float rightSample = bitsPerSample switch
                {
                    8 => (data[rightOffset] - 128) / 128.0f,
                    16 => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(rightOffset, 2)) / 32768.0f,
                    24 => Read24BitSample(data, rightOffset) / 8388608.0f,
                    32 => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(rightOffset, 4)) / 2147483648.0f,
                    _ => 0
                };
                sample = (sample + rightSample) / 2.0f;
            }

            samples[i] = sample;
        }

        _logger.LogDebug("Decoded {Samples} PCM samples ({BitsPerSample}-bit, {Channels} channel)",
            numSamples, bitsPerSample, numChannels);
        return samples;
    }

    private static int Read24BitSample(byte[] data, int offset)
    {
        int value = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
        // Sign extend
        if ((value & 0x800000) != 0)
            value |= unchecked((int)0xFF000000);
        return value;
    }

    /// <inheritdoc />
    public float[] Resample(float[] samples, int sourceSampleRate, int targetSampleRate = 16000)
    {
        if (sourceSampleRate == targetSampleRate)
            return samples;

        // Linear interpolation resampling
        double ratio = (double)sourceSampleRate / targetSampleRate;
        int outputLength = (int)(samples.Length / ratio);
        var output = new float[outputLength];

        for (int i = 0; i < outputLength; i++)
        {
            double srcIndex = i * ratio;
            int srcIndexFloor = (int)srcIndex;
            double frac = srcIndex - srcIndexFloor;

            if (srcIndexFloor + 1 < samples.Length)
            {
                // Linear interpolation between two samples
                output[i] = (float)(samples[srcIndexFloor] * (1 - frac) + samples[srcIndexFloor + 1] * frac);
            }
            else if (srcIndexFloor < samples.Length)
            {
                output[i] = samples[srcIndexFloor];
            }
        }

        _logger.LogDebug("Resampled from {SourceRate}Hz to {TargetRate}Hz ({InputSamples} -> {OutputSamples} samples)",
            sourceSampleRate, targetSampleRate, samples.Length, outputLength);
        return output;
    }

    /// <inheritdoc />
    public byte[] EncodeWav(float[] samples, int sampleRate)
    {
        const int bitsPerSample = 16;
        const int numChannels = 1;
        int bytesPerSample = bitsPerSample / 8;
        int dataSize = samples.Length * bytesPerSample;

        using var ms = new MemoryStream(44 + dataSize);
        using var writer = new BinaryWriter(ms);

        // RIFF header
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize); // File size - 8
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // Chunk size
        writer.Write((short)1); // Audio format (PCM)
        writer.Write((short)numChannels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * numChannels * bytesPerSample); // Byte rate
        writer.Write((short)(numChannels * bytesPerSample)); // Block align
        writer.Write((short)bitsPerSample);

        // data chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        // Write samples
        foreach (var sample in samples)
        {
            // Clamp and convert to 16-bit
            var clamped = Math.Clamp(sample, -1.0f, 1.0f);
            var int16Value = (short)(clamped * 32767);
            writer.Write(int16Value);
        }

        _logger.LogDebug("Encoded {Samples} samples to WAV ({SampleRate}Hz, {DataSize} bytes)",
            samples.Length, sampleRate, dataSize);
        return ms.ToArray();
    }
}
