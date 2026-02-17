using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using NvidiaVoiceAgent.Core.Services;

namespace NvidiaVoiceAgent.Core.Tests.Performance;

/// <summary>
/// Performance benchmarking tests for audio chunking.
/// Measures throughput, latency, and resource utilization.
/// </summary>
public class AudioChunkingPerformanceTests
{
    private const int SampleRate = 16000;
    private const float ChunkSizeSeconds = 50f;
    private const float OverlapSeconds = 2f;

    private readonly ITestOutputHelper _output;

    public AudioChunkingPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Generate synthetic audio for benchmarking.
    /// </summary>
    private float[] GenerateSynthAudio(float durationSeconds)
    {
        int samples = (int)(durationSeconds * SampleRate);
        var audio = new float[samples];
        var random = new Random(42);

        for (int i = 0; i < samples; i++)
        {
            // Mix of frequencies to simulate speech
            float fundamental = 150f + random.Next(100);
            audio[i] = 0.3f * (float)Math.Sin(2 * Math.PI * fundamental * i / SampleRate);
            audio[i] += 0.2f * (float)Math.Sin(2 * Math.PI * (fundamental * 2) * i / SampleRate);
            audio[i] += 0.1f * (float)(random.NextDouble() - 0.5f);  // Noise
            audio[i] = Math.Clamp(audio[i], -1f, 1f);
        }

        return audio;
    }

    [Fact]
    public void ChunkAudio_10Minutes_MeasureThroughput()
    {
        var chunker = new OverlappingAudioChunker(ChunkSizeSeconds, OverlapSeconds);
        var audio = GenerateSynthAudio(10 * 60);  // 10 minutes

        var sw = Stopwatch.StartNew();
        var chunks = chunker.ChunkAudio(audio, SampleRate);
        sw.Stop();

        _output.WriteLine($"10-minute audio chunking:");
        _output.WriteLine($"  Chunks created: {chunks.Length}");
        _output.WriteLine($"  Time elapsed: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Throughput: {(10 * 60) / (sw.ElapsedMilliseconds / 1000f):F2} seconds/second");

        // Chunking should be fast (< 100ms for CPU work)
        sw.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    [Fact]
    public void ChunkAudio_60Minutes_MeasureScalability()
    {
        var chunker = new OverlappingAudioChunker(ChunkSizeSeconds, OverlapSeconds);
        var audio = GenerateSynthAudio(60 * 60);  // 60 minutes (stress test)

        var sw = Stopwatch.StartNew();
        var chunks = chunker.ChunkAudio(audio, SampleRate);
        sw.Stop();

        _output.WriteLine($"60-minute audio chunking (stress test):");
        _output.WriteLine($"  Chunks created: {chunks.Length}");
        _output.WriteLine($"  Time elapsed: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Throughput: {(60 * 60) / (sw.ElapsedMilliseconds / 1000f):F2} seconds/second");

        // Should scale linearly
        sw.ElapsedMilliseconds.Should().BeLessThan(1000);  // < 1 second for CPU chunking
    }

    [Fact]
    public void MergeTranscripts_ManyChunks_MeasureLatency()
    {
        var merger = new TranscriptMerger(NullLogger<TranscriptMerger>.Instance);

        // Create many transcripts to merge (simulating 60+ minute audio)
        int chunkCount = 70;
        var transcripts = new string[chunkCount];
        for (int i = 0; i < chunkCount; i++)
        {
            transcripts[i] = $"this is chunk {i} transcript with some content";
        }

        var sw = Stopwatch.StartNew();
        var merged = merger.MergeTranscripts(transcripts, 0.04f);
        sw.Stop();

        _output.WriteLine($"Merging {chunkCount} transcripts:");
        _output.WriteLine($"  Time elapsed: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Result length: {merged.Length} characters");

        // Merging should be fast (< 50ms even for many chunks)
        sw.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(60)]
    public void ChunkAudio_VariousDurations_MeasureLinearScaling(int durationMinutes)
    {
        var chunker = new OverlappingAudioChunker(ChunkSizeSeconds, OverlapSeconds);
        var audio = GenerateSynthAudio(durationMinutes * 60);

        var sw = Stopwatch.StartNew();
        var chunks = chunker.ChunkAudio(audio, SampleRate);
        sw.Stop();

        double timePerMinute = sw.ElapsedMilliseconds / (double)durationMinutes;

        _output.WriteLine($"{durationMinutes}-minute audio: {sw.ElapsedMilliseconds}ms " +
            $"({timePerMinute:F2}ms per minute, {chunks.Length} chunks)");

        // All should be very fast (< 100ms even for 60 minutes)
        sw.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    [Fact]
    public void MemoryUsage_DuringChunking_RemainsStable()
    {
        var chunker = new OverlappingAudioChunker(ChunkSizeSeconds, OverlapSeconds);

        // Measure baseline
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long baselineMemory = GC.GetTotalMemory(false);

        // Generate and chunk audio
        var audio = GenerateSynthAudio(10 * 60);
        var chunks = chunker.ChunkAudio(audio, SampleRate);

        // Measure peak
        long peakMemory = GC.GetTotalMemory(false);
        long memoryIncrease = peakMemory - baselineMemory;

        _output.WriteLine($"Memory usage during 10-minute chunking:");
        _output.WriteLine($"  Baseline: {baselineMemory / (1024 * 1024)}MB");
        _output.WriteLine($"  Peak: {peakMemory / (1024 * 1024)}MB");
        _output.WriteLine($"  Increase: {memoryIncrease / (1024 * 1024)}MB");

        // Memory increase should be reasonable (< 500MB for temp buffers)
        memoryIncrease.Should().BeLessThan(500 * 1024 * 1024);
    }

    [Fact]
    public void ChunkAudio_MultipleRuns_ConsistentPerformance()
    {
        var chunker = new OverlappingAudioChunker(ChunkSizeSeconds, OverlapSeconds);
        var audio = GenerateSynthAudio(5 * 60);  // 5 minutes

        var times = new List<long>();

        for (int run = 0; run < 5; run++)
        {
            var sw = Stopwatch.StartNew();
            _ = chunker.ChunkAudio(audio, SampleRate);
            sw.Stop();
            times.Add(sw.ElapsedMilliseconds);
        }

        _output.WriteLine($"5-minute chunking over 5 runs:");
        _output.WriteLine($"  Times: {string.Join(", ", times)}ms");
        _output.WriteLine($"  Average: {times.Average():F2}ms");
        _output.WriteLine($"  StdDev: {Math.Sqrt(times.Average(t => Math.Pow(t - times.Average(), 2))):F2}ms");

        // Performance should be consistent (low variance)
        double stdDev = Math.Sqrt(times.Average(t => Math.Pow(t - times.Average(), 2)));
        stdDev.Should().BeLessThan(5);  // Variance should be low
    }

    [Fact]
    public void OverlapDetection_LargeTranscripts_MeasurePerformance()
    {
        var merger = new TranscriptMerger(NullLogger<TranscriptMerger>.Instance);

        // Create large transcripts with realistic overlap
        var transcripts = new[]
        {
            "the quick brown fox jumps over the lazy dog this is a test of the emergency broadcast system",
            "over the lazy dog this is a test of the emergency broadcast system and it is working well",
            "broadcast system and it is working well and everyone should be prepared for any eventuality"
        };

        var sw = Stopwatch.StartNew();
        var merged = merger.MergeTranscripts(transcripts, 0.1f);
        sw.Stop();

        _output.WriteLine($"Large transcript overlap detection:");
        _output.WriteLine($"  Input: {string.Join(" | ", transcripts.Select(t => $"{t.Length}c"))}");
        _output.WriteLine($"  Output: {merged.Length} characters");
        _output.WriteLine($"  Time: {sw.ElapsedMilliseconds}ms");

        sw.ElapsedMilliseconds.Should().BeLessThan(10);  // Very fast for overlap detection
    }
}
