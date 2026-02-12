using System.Text;
using FluentAssertions;
using NvidiaVoiceAgent.Core.Services;

namespace NvidiaVoiceAgent.Core.Tests;

/// <summary>
/// Tests for IAudioProcessor implementation.
/// These tests use inline test implementations since AudioProcessor isn't implemented yet.
/// When AudioProcessor is implemented, update these to use the real implementation.
/// </summary>
public class AudioProcessorTests
{
    [Fact]
    public void DecodeWav_WithValidWav_ExtractsSamples()
    {
        // Arrange
        var processor = new TestAudioProcessor();
        var wavData = CreateWavFile(new float[] { 0.5f, -0.5f, 0.25f, -0.25f }, 16000);

        // Act
        var samples = processor.DecodeWav(wavData);

        // Assert
        samples.Should().NotBeEmpty();
        samples.Length.Should().Be(4);
    }

    [Fact]
    public void DecodeWav_WithInvalidHeader_ThrowsException()
    {
        // Arrange
        var processor = new TestAudioProcessor();
        var invalidData = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        // Act & Assert
        var act = () => processor.DecodeWav(invalidData);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void DecodeWav_WithEmptyData_ThrowsException()
    {
        // Arrange
        var processor = new TestAudioProcessor();

        // Act & Assert
        var act = () => processor.DecodeWav(Array.Empty<byte>());
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void EncodeWav_ProducesValidWavHeader()
    {
        // Arrange
        var processor = new TestAudioProcessor();
        var samples = new float[] { 0.5f, -0.5f, 0.25f };

        // Act
        var wavData = processor.EncodeWav(samples, 16000);

        // Assert
        wavData.Length.Should().BeGreaterThan(44); // Minimum WAV header size
        Encoding.ASCII.GetString(wavData, 0, 4).Should().Be("RIFF");
        Encoding.ASCII.GetString(wavData, 8, 4).Should().Be("WAVE");
        Encoding.ASCII.GetString(wavData, 12, 4).Should().Be("fmt ");
    }

    [Fact]
    public void EncodeWav_RoundTripsCorrectly()
    {
        // Arrange
        var processor = new TestAudioProcessor();
        var originalSamples = new float[] { 0.5f, -0.5f, 0.25f, -0.25f };

        // Act
        var wavData = processor.EncodeWav(originalSamples, 16000);
        var decodedSamples = processor.DecodeWav(wavData);

        // Assert - samples should be close (16-bit quantization may introduce small errors)
        decodedSamples.Length.Should().Be(originalSamples.Length);
        for (int i = 0; i < originalSamples.Length; i++)
        {
            decodedSamples[i].Should().BeApproximately(originalSamples[i], 0.001f);
        }
    }

    [Fact]
    public void Resample_SameRate_ReturnsOriginal()
    {
        // Arrange
        var processor = new TestAudioProcessor();
        var samples = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };

        // Act
        var resampled = processor.Resample(samples, 16000, 16000);

        // Assert
        resampled.Should().BeEquivalentTo(samples);
    }

    [Fact]
    public void Resample_DownsampleHalf_ReducesLength()
    {
        // Arrange
        var processor = new TestAudioProcessor();
        var samples = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f };

        // Act
        var resampled = processor.Resample(samples, 32000, 16000);

        // Assert
        resampled.Length.Should().Be(4);
    }

    [Fact]
    public void Resample_UpsampleDouble_IncreasesLength()
    {
        // Arrange
        var processor = new TestAudioProcessor();
        var samples = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };

        // Act
        var resampled = processor.Resample(samples, 8000, 16000);

        // Assert
        resampled.Length.Should().Be(8);
    }

    [Fact]
    public void Resample_44100To16000_ProducesCorrectLength()
    {
        // Arrange
        var processor = new TestAudioProcessor();
        // 1 second at 44100 Hz
        var samples = new float[44100];

        // Act
        var resampled = processor.Resample(samples, 44100, 16000);

        // Assert - should be approximately 1 second at 16000 Hz
        resampled.Length.Should().BeCloseTo(16000, 10);
    }

    private static byte[] CreateWavFile(float[] samples, int sampleRate)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        short bitsPerSample = 16;
        short channels = 1;
        int dataLength = samples.Length * 2;

        // RIFF header
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);

        // data chunk
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);

        foreach (var sample in samples)
        {
            var intSample = (short)(sample * 32767);
            writer.Write(intSample);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Test implementation of IAudioProcessor for unit testing.
    /// Replace with actual AudioProcessor once implemented.
    /// </summary>
    private class TestAudioProcessor : IAudioProcessor
    {
        public float[] DecodeWav(byte[] wavData)
        {
            if (wavData == null || wavData.Length < 44)
                throw new InvalidDataException("Invalid WAV data: too short");

            var riffHeader = Encoding.ASCII.GetString(wavData, 0, 4);
            if (riffHeader != "RIFF")
                throw new InvalidDataException("Invalid WAV data: missing RIFF header");

            using var ms = new MemoryStream(wavData);
            using var reader = new BinaryReader(ms);

            // Skip RIFF header
            reader.ReadBytes(12);

            // Read fmt chunk
            reader.ReadBytes(4); // "fmt "
            var fmtSize = reader.ReadInt32();
            reader.ReadInt16(); // format
            var channels = reader.ReadInt16();
            var sampleRate = reader.ReadInt32();
            reader.ReadInt32(); // byte rate
            reader.ReadInt16(); // block align
            var bitsPerSample = reader.ReadInt16();

            // Skip any extra fmt bytes
            if (fmtSize > 16)
                reader.ReadBytes(fmtSize - 16);

            // Find data chunk
            while (ms.Position < ms.Length - 8)
            {
                var chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                var chunkSize = reader.ReadInt32();

                if (chunkId == "data")
                {
                    var numSamples = chunkSize / (bitsPerSample / 8) / channels;
                    var samples = new float[numSamples];

                    for (int i = 0; i < numSamples; i++)
                    {
                        var intSample = reader.ReadInt16();
                        samples[i] = intSample / 32767f;
                    }

                    return samples;
                }
                else
                {
                    reader.ReadBytes(chunkSize);
                }
            }

            throw new InvalidDataException("Invalid WAV data: no data chunk found");
        }

        public byte[] EncodeWav(float[] samples, int sampleRate)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            short bitsPerSample = 16;
            short channels = 1;
            int dataLength = samples.Length * 2;

            // RIFF header
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            // fmt chunk
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * bitsPerSample / 8);
            writer.Write((short)(channels * bitsPerSample / 8));
            writer.Write(bitsPerSample);

            // data chunk
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);

            foreach (var sample in samples)
            {
                var clampedSample = Math.Max(-1f, Math.Min(1f, sample));
                var intSample = (short)(clampedSample * 32767);
                writer.Write(intSample);
            }

            return ms.ToArray();
        }

        public float[] Resample(float[] samples, int sourceSampleRate, int targetSampleRate = 16000)
        {
            if (sourceSampleRate == targetSampleRate)
                return samples;

            double ratio = (double)targetSampleRate / sourceSampleRate;
            int newLength = (int)(samples.Length * ratio);
            var result = new float[newLength];

            for (int i = 0; i < newLength; i++)
            {
                double sourceIndex = i / ratio;
                int index0 = (int)sourceIndex;
                int index1 = Math.Min(index0 + 1, samples.Length - 1);
                double frac = sourceIndex - index0;

                result[i] = (float)(samples[index0] * (1 - frac) + samples[index1] * frac);
            }

            return result;
        }
    }
}
