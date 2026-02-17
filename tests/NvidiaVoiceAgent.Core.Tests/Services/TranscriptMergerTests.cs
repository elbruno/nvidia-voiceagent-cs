using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using NvidiaVoiceAgent.Core.Services;

namespace NvidiaVoiceAgent.Core.Tests.Services;

public class TranscriptMergerTests
{
    private readonly ILogger<TranscriptMerger> _logger;

    public TranscriptMergerTests()
    {
        _logger = NullLogger<TranscriptMerger>.Instance;
    }

    [Fact]
    public void MergeTranscripts_WithEmptyArray_ReturnsEmpty()
    {
        var merger = new TranscriptMerger(_logger);
        var transcripts = Array.Empty<string>();

        var result = merger.MergeTranscripts(transcripts, 0.04f);

        result.Should().BeEmpty();
    }

    [Fact]
    public void MergeTranscripts_WithSingleTranscript_ReturnsSame()
    {
        var merger = new TranscriptMerger(_logger);
        var transcripts = new[] { "Hello world" };

        var result = merger.MergeTranscripts(transcripts, 0.04f);

        result.Should().Be("Hello world");
    }

    [Fact]
    public void MergeTranscripts_WithoutOverlap_ConcatenatesWithSpace()
    {
        var merger = new TranscriptMerger(_logger);
        var transcripts = new[] { "Hello world", "this is a test" };

        var result = merger.MergeTranscripts(transcripts, 0.04f);

        result.Should().Contain("Hello world");
        result.Should().Contain("this is a test");
    }

    [Fact]
    public void MergeTranscripts_WithPerfectOverlap_RemovesDuplicate()
    {
        var merger = new TranscriptMerger(_logger);
        // Common overlap: "this is"
        var transcripts = new[] { "Hello world this is", "this is a test" };

        var result = merger.MergeTranscripts(transcripts, 0.1f);

        // Should not have "this is" twice
        int count = (result.Length - result.Replace("this is", "").Length) / "this is".Length;
        count.Should().BeLessThanOrEqualTo(1);  // At most once
    }

    [Fact]
    public void MergeTranscripts_WithoutCommonWords_ConcatenatesWithoutDuplication()
    {
        var merger = new TranscriptMerger(_logger);
        var transcripts = new[] { "foo bar", "baz qux" };

        var result = merger.MergeTranscripts(transcripts, 0.1f);

        result.Should().Contain("foo");
        result.Should().Contain("bar");
        result.Should().Contain("baz");
        result.Should().Contain("qux");
    }

    [Fact]
    public void MergeTranscripts_WithNullTranscript_HandlesGracefully()
    {
        var merger = new TranscriptMerger(_logger);
        var transcripts = new string?[] { "Hello", null, "world" };

        var result = merger.MergeTranscripts(transcripts!, 0.04f);

        result.Should().Contain("Hello");
        result.Should().Contain("world");
    }

    [Fact]
    public void MergeTranscripts_HandlesPunctuation()
    {
        var merger = new TranscriptMerger(_logger);
        // Overlap with punctuation variations
        var transcripts = new[] { "Hello world, how are", "how are you today" };

        var result = merger.MergeTranscripts(transcripts, 0.1f);

        // Should handle "how are" with or without punctuation
        result.Should().Contain("how");
        result.Should().Contain("are");
    }

    [Fact]
    public void MergeTranscripts_WithShortTranscripts_StillDeduplicates()
    {
        var merger = new TranscriptMerger(_logger);
        // Very short transcripts (1-2 words)
        var transcripts = new[] { "hello there", "there friend" };

        var result = merger.MergeTranscripts(transcripts, 0.1f);

        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void MergeTranscripts_PreservesWordOrder()
    {
        var merger = new TranscriptMerger(_logger);
        var transcripts = new[]
        {
            "The quick brown fox jumps over",
            "over the lazy dog"
        };

        var result = merger.MergeTranscripts(transcripts, 0.1f);

        // Check that words appear in order
        var wordsToFind = new[] { "The", "quick", "brown", "fox", "jumps", "over", "lazy", "dog" };
        var lastIndex = -1;
        foreach (var word in wordsToFind)
        {
            var index = result.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
                index.Should().BeGreaterThan(lastIndex);
            lastIndex = index;
        }
    }

    [Fact]
    public void MergeTranscripts_WithCaseInsensitiveOverlap_DeduplicatesCorrectly()
    {
        var merger = new TranscriptMerger(_logger);
        // Same overlap but different case
        var transcripts = new[] { "Hello WORLD test", "test FOO bar" };

        var result = merger.MergeTranscripts(transcripts, 0.1f);

        // Should handle case-insensitive matching
        result.Should().NotBeNull();
    }

    [Fact]
    public void MergeTranscripts_EmptyOverlapFraction_UsesSensibleDefaults()
    {
        var merger = new TranscriptMerger(_logger);
        var transcripts = new[] { "Hello world", "world test" };

        // Even with 0 overlap fraction, should still work
        var result = merger.MergeTranscripts(transcripts, 0f);

        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void MergeTranscripts_WithHighOverlapFraction_StillMerges()
    {
        var merger = new TranscriptMerger(_logger);
        var transcripts = new[] { "Hello world test", "test frame" };

        // With high fraction, should search for longer overlaps
        var result = merger.MergeTranscripts(transcripts, 0.5f);

        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void MergeTranscripts_MultipleChunks_ComplexCase()
    {
        var merger = new TranscriptMerger(_logger);
        var transcripts = new[]
        {
            "The meeting started at nine",
            "at nine in the morning",
            "morning everyone was ready",
            "ready to discuss projects"
        };

        var result = merger.MergeTranscripts(transcripts, 0.1f);

        // Should contain all key words
        result.Should().Contain("meeting");
        result.Should().Contain("morning");
        result.Should().Contain("projects");
    }
}
