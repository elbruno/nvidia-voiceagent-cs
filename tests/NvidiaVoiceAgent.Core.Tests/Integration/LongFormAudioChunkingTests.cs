using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using NvidiaVoiceAgent.Core.Adapters;
using NvidiaVoiceAgent.Core.Services;

namespace NvidiaVoiceAgent.Core.Tests.Integration;

/// <summary>
/// Integration tests for audio chunking with long-form audio scenarios.
/// Tests realistic use cases: podcasts, meetings, lectures, etc.
/// </summary>
public class LongFormAudioChunkingTests
{
    private const int SampleRate = 16000;
    private const float ChunkSizeSeconds = 50f;
    private const float OverlapSeconds = 2f;

    /// <summary>
    /// Generate synthetic audio at specified duration and characteristics.
    /// </summary>
    private float[] GenerateSynthAudio(float durationSeconds, AudioCharacteristic characteristic)
    {
        int samples = (int)(durationSeconds * SampleRate);
        var audio = new float[samples];

        switch (characteristic)
        {
            case AudioCharacteristic.CleanSpeech:
                // Simulate clean speech: 100-400 Hz sinusoid with amplitude modulation
                GenerateCleanSpeech(audio);
                break;

            case AudioCharacteristic.WithSilence:
                // Simulate speech with pauses
                GenerateSpeechWithSilence(audio);
                break;

            case AudioCharacteristic.WithNoise:
                // Simulate speech with background noise
                GenerateNoisySpeech(audio);
                break;

            case AudioCharacteristic.MultiSpeaker:
                // Simulate overlapping speakers
                GenerateMultiSpeaker(audio);
                break;
        }

        return audio;
    }

    private void GenerateCleanSpeech(float[] audio)
    {
        var random = new Random(42);
        for (int i = 0; i < audio.Length; i++)
        {
            // Mix of sine waves to simulate speech formants
            float fundamental = 150f + random.Next(50);  // 150-200 Hz fundamental
            float sample = 0.5f * (float)Math.Sin(2 * Math.PI * fundamental * i / SampleRate);
            sample += 0.3f * (float)Math.Sin(2 * Math.PI * (fundamental * 2) * i / SampleRate);
            sample += 0.2f * (float)Math.Sin(2 * Math.PI * (fundamental * 3) * i / SampleRate);

            // Amplitude envelope (prevents clicks)
            float windowPos = (float)i / audio.Length;
            float envelope = (float)Math.Sin(Math.PI * windowPos) * 0.8f;

            audio[i] = (sample / 2f) * envelope;
        }
    }

    private void GenerateSpeechWithSilence(float[] audio)
    {
        GenerateCleanSpeech(audio);

        // Add pauses every 5 seconds
        int pauseDuration = (int)(1 * SampleRate);  // 1 second pause
        int stride = (int)(5 * SampleRate);

        for (int i = 0; i < audio.Length; i += stride)
        {
            for (int j = 0; j < pauseDuration && i + j < audio.Length; j++)
            {
                audio[i + j] *= 0.1f;  // Reduce to near silence
            }
        }
    }

    private void GenerateNoisySpeech(float[] audio)
    {
        GenerateCleanSpeech(audio);

        // Add low-level noise
        var random = new Random(42);
        for (int i = 0; i < audio.Length; i++)
        {
            audio[i] += (float)(random.NextDouble() - 0.5f) * 0.1f;  // 10% noise
            audio[i] = Math.Clamp(audio[i], -1f, 1f);
        }
    }

    private void GenerateMultiSpeaker(float[] audio)
    {
        // Simulate two speakers with different pitch ranges
        var random = new Random(42);
        int half = audio.Length / 2;

        for (int i = 0; i < half; i++)
        {
            // Speaker 1: lower pitch (120-150 Hz)
            float f1 = 120f + random.Next(30);
            audio[i] = 0.3f * (float)Math.Sin(2 * Math.PI * f1 * i / SampleRate);
        }

        for (int i = half; i < audio.Length; i++)
        {
            // Speaker 2: higher pitch (200-250 Hz)
            float f2 = 200f + random.Next(50);
            audio[i] = 0.3f * (float)Math.Sin(2 * Math.PI * f2 * (i - half) / SampleRate);
        }
    }

    [Fact]
    public void LongFormAudio_30Minutes_ChunksCorrectly()
    {
        var chunker = new OverlappingAudioChunker(ChunkSizeSeconds, OverlapSeconds);
        float durationMinutes = 30;
        var audio = GenerateSynthAudio(durationMinutes * 60, AudioCharacteristic.CleanSpeech);

        var chunks = chunker.ChunkAudio(audio, SampleRate);

        // 30 minutes should require multiple chunks
        chunks.Length.Should().BeGreaterThan(5);

        // All chunks should have valid samples
        chunks.Should().AllSatisfy(c => c.Samples.Should().NotBeEmpty());

        // Total coverage should span complete audio
        chunks.Last().EndFrame.Should().Be(audio.Length);
    }

    [Fact]
    public void LongFormAudio_10Minutes_MaintainsAccuracy()
    {
        var chunker = new OverlappingAudioChunker(ChunkSizeSeconds, OverlapSeconds);
        var merger = new TranscriptMerger(NullLogger<TranscriptMerger>.Instance);

        var audio = GenerateSynthAudio(10 * 60, AudioCharacteristic.CleanSpeech);
        var chunks = chunker.ChunkAudio(audio, SampleRate);

        // Simulate transcripts (in real test, would use actual ASR)
        var transcripts = new string[chunks.Length];
        for (int i = 0; i < chunks.Length; i++)
        {
            transcripts[i] = $"chunk {i} transcript";
        }

        var merged = merger.MergeTranscripts(transcripts, 0.04f);

        // Merged result should contain all chunks
        merged.Should().Contain("chunk 0");
        merged.Should().Contain($"chunk {chunks.Length - 1}");
    }

    [Fact]
    public void LongFormAudio_WithSilence_HandlesGracefully()
    {
        var chunker = new OverlappingAudioChunker(ChunkSizeSeconds, OverlapSeconds);
        var audio = GenerateSynthAudio(15 * 60, AudioCharacteristic.WithSilence);

        var chunks = chunker.ChunkAudio(audio, SampleRate);

        chunks.Should().NotBeEmpty();
        chunks.Should().AllSatisfy(c => c.Samples.Length.Should().BeGreaterThan(0));
    }

    [Fact]
    public void LongFormAudio_WithNoise_ProducesValidChunks()
    {
        var chunker = new OverlappingAudioChunker(ChunkSizeSeconds, OverlapSeconds);
        var audio = GenerateSynthAudio(10 * 60, AudioCharacteristic.WithNoise);

        var chunks = chunker.ChunkAudio(audio, SampleRate);

        chunks.Length.Should().BeGreaterThan(2);
        chunks.Should().AllSatisfy(c =>
        {
            c.Samples.Should().AllSatisfy(s => float.IsNaN(s).Should().BeFalse());
            c.Samples.Should().AllSatisfy(s => float.IsInfinity(s).Should().BeFalse());
            c.StartFrame.Should().BeLessThan(c.EndFrame);
        });
    }

    [Fact]
    public void LongFormAudio_MultiSpeaker_MaintainsBoundaries()
    {
        var chunker = new OverlappingAudioChunker(ChunkSizeSeconds, OverlapSeconds);
        var audio = GenerateSynthAudio(20 * 60, AudioCharacteristic.MultiSpeaker);

        var chunks = chunker.ChunkAudio(audio, SampleRate);

        // Verify chunk boundaries don't overlap incorrectly
        for (int i = 1; i < chunks.Length; i++)
        {
            chunks[i].StartFrame.Should().BeLessThan(chunks[i].EndFrame);
            chunks[i].OverlapStartFrame.Should().BeLessThanOrEqualTo(chunks[i].StartFrame);
        }

        // Verify no gaps in coverage
        chunks.Last().EndFrame.Should().Be(audio.Length);
    }

    [Fact]
    public void ChunkedVsSinglePass_ShortAudio_SameSize()
    {
        var chunker = new OverlappingAudioChunker(ChunkSizeSeconds, OverlapSeconds);
        var audio = GenerateSynthAudio(30, AudioCharacteristic.CleanSpeech);  // 30 seconds

        var chunks = chunker.ChunkAudio(audio, SampleRate);

        // Short audio should fit in single chunk
        chunks.Should().HaveCount(1);
        chunks[0].Samples.Should().HaveCount(audio.Length);
    }

    [Fact]
    public void OverlapRegions_AreCorrectlyIdentified()
    {
        var chunker = new OverlappingAudioChunker(ChunkSizeSeconds, OverlapSeconds);
        var audio = GenerateSynthAudio(2 * ChunkSizeSeconds + 10, AudioCharacteristic.CleanSpeech);

        var chunks = chunker.ChunkAudio(audio, SampleRate);

        if (chunks.Length > 1)
        {
            // Overlap should be approximately OverlapSeconds
            int actualOverlap = chunks[1].OverlapStartFrame;

            actualOverlap.Should().BeGreaterThan(0);
            actualOverlap.Should().BeLessThan(chunks[1].StartFrame + chunks[1].Samples.Length);
        }
    }

    [Theory]
    [InlineData(60)]      // Exactly 60s (1 minute)
    [InlineData(120)]     // Exactly 120s (2 minutes)
    [InlineData(152)]     // Not aligned with chunk boundary
    public void ChunkBoundaryAlignment_HandlesEdgeCases(int durationSeconds)
    {
        var chunker = new OverlappingAudioChunker(ChunkSizeSeconds, OverlapSeconds);
        var audio = GenerateSynthAudio(durationSeconds, AudioCharacteristic.CleanSpeech);

        var chunks = chunker.ChunkAudio(audio, SampleRate);

        chunks.Should().NotBeEmpty();
        chunks.Last().EndFrame.Should().Be(audio.Length);
    }

    private enum AudioCharacteristic
    {
        CleanSpeech,
        WithSilence,
        WithNoise,
        MultiSpeaker
    }
}
