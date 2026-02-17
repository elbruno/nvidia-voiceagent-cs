using System.IO;
using System.Text;

namespace NvidiaVoiceAgent.Core.Tests;

public static class TestAudioGenerator
{
    public static byte[] CreateSilenceWav(int durationSeconds = 1, int sampleRate = 16000)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        short bitsPerSample = 16;
        short channels = 1;
        int dataLength = sampleRate * durationSeconds * channels * (bitsPerSample / 8);

        // RIFF header
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // chunk size
        writer.Write((short)1); // PCM format
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8); // byte rate
        writer.Write((short)(channels * bitsPerSample / 8)); // block align
        writer.Write(bitsPerSample);

        // data chunk
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);

        // Write silence (all zeros)
        writer.Write(new byte[dataLength]);

        return ms.ToArray();
    }

    public static float[] CreateSilenceFloats(int durationSeconds = 1, int sampleRate = 16000)
    {
        return new float[sampleRate * durationSeconds];
    }
}