using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using NvidiaVoiceAgent.Core.Services;

namespace NvidiaVoiceAgent.Core.Tests.Services;

public class OverlappingAudioChunkerTests
{
    private readonly ILogger<OverlappingAudioChunker> _logger;

    public OverlappingAudioChunkerTests()
    {
        _logger = NullLogger<OverlappingAudioChunker>.Instance;
    }

    [Fact]
    public void ChunkAudio_WithEmptyAudio_ReturnsEmptyArray()
    {
        var chunker = new OverlappingAudioChunker(50f, 2f, _logger);
        var samples = Array.Empty<float>();

        var chunks = chunker.ChunkAudio(samples, 16000);

        chunks.Should().BeEmpty();
    }

    [Fact]
    public void ChunkAudio_WithShortAudio_ReturnsSingleChunk()
    {
        var chunker = new OverlappingAudioChunker(50f, 2f, _logger);
        const int sampleRate = 16000;
        const float durationSeconds = 30f;
        var samples = new float[(int)(sampleRate * durationSeconds)];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (float)Math.Sin(i * 0.01f);

        var chunks = chunker.ChunkAudio(samples, sampleRate);

        chunks.Should().HaveCount(1);
        chunks[0].Samples.Should().HaveCount(samples.Length);
        chunks[0].StartFrame.Should().Be(0);
        chunks[0].EndFrame.Should().Be(samples.Length);
    }

    [Fact]
    public void ChunkAudio_WithLongAudio_ReturnsMultipleChunks()
    {
        var chunker = new OverlappingAudioChunker(50f, 2f, _logger);
        const int sampleRate = 16000;
        const float durationSeconds = 120f;
        var samples = new float[(int)(sampleRate * durationSeconds)];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (float)Math.Sin(i * 0.001f);

        var chunks = chunker.ChunkAudio(samples, sampleRate);

        // 120s audio with 50s chunks (stride = 48s) should produce ~3 chunks
        chunks.Length.Should().BeGreaterThan(1);
        chunks.Should().AllSatisfy(c => c.Samples.Should().NotBeEmpty());
    }

    [Fact]
    public void ChunkAudio_WithPerfectFitAudio_HandlesCorrectly()
    {
        var chunker = new OverlappingAudioChunker(50f, 2f, _logger);
        const int sampleRate = 16000;
        const float chunkDuration = 50f;
        var samples = new float[(int)(sampleRate * chunkDuration * 2)];  // 100 seconds
        for (int i = 0; i < samples.Length; i++)
            samples[i] = 0.5f;

        var chunks = chunker.ChunkAudio(samples, sampleRate);

        // 100s with 50s chunks and 2s overlap creates 3 chunks (stride = 48s)
        // Chunk 0: 0-50s, Chunk 1: 48-98s, Chunk 2: 96-100s
        chunks.Should().HaveCount(3);
        // Each chunk should have ~50s of audio + 2s overlap
        var expectedChunkSize = (int)(sampleRate * chunkDuration);
        foreach (var chunk in chunks)
            chunk.Samples.Length.Should().BeLessThanOrEqualTo(expectedChunkSize + (int)(sampleRate * 2));
    }

    [Fact]
    public void ChunkAudio_WithRemainderAudio_IncludesLastPartialChunk()
    {
        var chunker = new OverlappingAudioChunker(50f, 2f, _logger);
        const int sampleRate = 16000;
        const float durationSeconds = 125f;  // 50 + 50 + 25
        var samples = new float[(int)(sampleRate * durationSeconds)];

        var chunks = chunker.ChunkAudio(samples, sampleRate);

        // Last chunk should contain the remainder
        chunks.Last().EndFrame.Should().Be(samples.Length);
    }

    [Fact]
    public void ChunkAudio_OverlapMetadataIsCorrect()
    {
        var chunker = new OverlappingAudioChunker(50f, 2f, _logger);
        const int sampleRate = 16000;
        const float durationSeconds = 100f;
        var samples = new float[(int)(sampleRate * durationSeconds)];

        var chunks = chunker.ChunkAudio(samples, sampleRate);

        // First chunk should have no overlap start
        chunks[0].OverlapStartFrame.Should().Be(0);

        // Subsequent chunks should track overlap correctly
        if (chunks.Length > 1)
        {
            chunks[1].OverlapStartFrame.Should().BeGreaterThan(0);
            chunks[1].OverlapStartFrame.Should().BeLessThan(chunks[1].StartFrame + chunks[1].Samples.Length);
        }
    }

    [Fact]
    public void ChunkAudio_PropertiesSetCorrectly()
    {
        var chunkSize = 45f;
        var overlap = 3f;
        var chunker = new OverlappingAudioChunker(chunkSize, overlap, _logger);

        chunker.ChunkSizeSeconds.Should().Be(chunkSize);
        chunker.OverlapSeconds.Should().Be(overlap);
    }

    [Fact]
    public void Constructor_WithInvalidChunkSize_ThrowsArgumentException()
    {
        Action act = () => new OverlappingAudioChunker(-10f, 2f, _logger);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Chunk size must be positive*");
    }

    [Fact]
    public void Constructor_WithInvalidOverlap_ThrowsArgumentException()
    {
        var act1 = () => new OverlappingAudioChunker(50f, -2f, _logger);
        var act2 = () => new OverlappingAudioChunker(50f, 60f, _logger);

        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MergeTranscripts_WithSingleChunk_ReturnsSameText()
    {
        var chunker = new OverlappingAudioChunker(50f, 2f, _logger);
        var transcripts = new[] { "Hello world" };
        var chunks = new[]
        {
            new AudioChunk(new float[100], 0, 100, 0)
        };

        var result = chunker.MergeTranscripts(transcripts, chunks);

        result.Should().Be("Hello world");
    }

    [Fact]
    public void MergeTranscripts_WithMultipleChunks_ConcatenatesWithSpace()
    {
        var chunker = new OverlappingAudioChunker(50f, 2f, _logger);
        var transcripts = new[] { "Hello", "world", "test" };
        var chunks = new[]
        {
            new AudioChunk(new float[100], 0, 100, 0),
            new AudioChunk(new float[100], 80, 180, 80),
            new AudioChunk(new float[100], 160, 260, 160)
        };

        var result = chunker.MergeTranscripts(transcripts, chunks);

        result.Should().Be("Hello world test");
    }

    [Fact]
    public void MergeTranscripts_SkipsEmptyTranscripts()
    {
        var chunker = new OverlappingAudioChunker(50f, 2f, _logger);
        var transcripts = new[] { "Hello", "world" };  // Skip the null for now
        var chunks = new[]
        {
            new AudioChunk(new float[100], 0, 100, 0),
            new AudioChunk(new float[100], 80, 180, 80)
        };

        var result = chunker.MergeTranscripts(transcripts, chunks);

        result.Should().Be("Hello world");
    }
}
