using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NvidiaVoiceAgent.Core.Services;

/// <summary>
/// Splits audio into overlapping chunks to handle model input length constraints
/// while preserving transcription quality at chunk boundaries.
/// </summary>
public class OverlappingAudioChunker : IAudioChunkingStrategy
{
    private readonly ILogger<OverlappingAudioChunker> _logger;
    private readonly float _chunkSizeSeconds;
    private readonly float _overlapSeconds;

    /// <summary>
    /// Create a chunker with specified chunk and overlap sizes.
    /// </summary>
    /// <param name="chunkSizeSeconds">Target duration per chunk in seconds (default 50s)</param>
    /// <param name="overlapSeconds">Overlap duration between chunks in seconds (default 2s)</param>
    /// <param name="logger">Optional logger</param>
    public OverlappingAudioChunker(
        float chunkSizeSeconds = 50f,
        float overlapSeconds = 2f,
        ILogger<OverlappingAudioChunker>? logger = null)
    {
        if (chunkSizeSeconds <= 0)
            throw new ArgumentException("Chunk size must be positive", nameof(chunkSizeSeconds));
        if (overlapSeconds < 0 || overlapSeconds >= chunkSizeSeconds)
            throw new ArgumentException("Overlap must be between 0 and chunk size", nameof(overlapSeconds));

        _chunkSizeSeconds = chunkSizeSeconds;
        _overlapSeconds = overlapSeconds;
        _logger = logger ?? NullLogger<OverlappingAudioChunker>.Instance;
    }

    public float ChunkSizeSeconds => _chunkSizeSeconds;
    public float OverlapSeconds => _overlapSeconds;

    public AudioChunk[] ChunkAudio(float[] samples, int sampleRate = 16000)
    {
        if (samples == null || samples.Length == 0)
        {
            _logger.LogWarning("Empty audio samples provided to chunker");
            return Array.Empty<AudioChunk>();
        }

        // Calculate frame counts
        int chunkFrames = (int)(sampleRate * _chunkSizeSeconds);
        int overlapFrames = (int)(sampleRate * _overlapSeconds);
        int totalFrames = samples.Length;

        // If audio fits in single chunk, return as-is
        if (totalFrames <= chunkFrames)
        {
            _logger.LogInformation(
                "Audio duration {Duration:F2}s fits in single chunk ({ChunkSize:F2}s). No chunking needed.",
                totalFrames / (float)sampleRate, _chunkSizeSeconds);

            return new[]
            {
                new AudioChunk(samples, 0, totalFrames, 0)
            };
        }

        // Calculate number of chunks needed
        int stride = chunkFrames - overlapFrames;
        int numChunks = (totalFrames - chunkFrames + stride - 1) / stride + 1;

        _logger.LogInformation(
            "Splitting {Duration:F2}s audio into {ChunkCount} chunks ({ChunkSize:F2}s each, {Overlap:F2}s overlap)",
            totalFrames / (float)sampleRate, numChunks, _chunkSizeSeconds, _overlapSeconds);

        var chunks = new AudioChunk[numChunks];

        for (int i = 0; i < numChunks; i++)
        {
            int chunkStartFrame = i * stride;
            int chunkEndFrame = Math.Min(chunkStartFrame + chunkFrames, totalFrames);
            int overlapStartFrame = i > 0 ? chunkStartFrame : 0;

            // Extract chunk samples
            int chunkLength = chunkEndFrame - chunkStartFrame;
            var chunkSamples = new float[chunkLength];
            Array.Copy(samples, chunkStartFrame, chunkSamples, 0, chunkLength);

            // Log chunk info
            _logger.LogDebug(
                "Chunk {Index}: frames [{Start}-{End}), duration {Duration:F2}s, overlap_start={OverlapStart}",
                i, chunkStartFrame, chunkEndFrame, chunkLength / (float)sampleRate, overlapStartFrame);

            chunks[i] = new AudioChunk(chunkSamples, chunkStartFrame, chunkEndFrame, overlapStartFrame);
        }

        return chunks;
    }

    public string MergeTranscripts(string[] transcripts, AudioChunk[] chunks)
    {
        if (transcripts == null || transcripts.Length == 0)
            return string.Empty;

        if (transcripts.Length == 1)
            return transcripts[0] ?? string.Empty;

        if (chunks == null || chunks.Length != transcripts.Length)
        {
            _logger.LogWarning(
                "Transcript count ({TransCount}) doesn't match chunk count ({ChunkCount}). Concatenating without merge.",
                transcripts.Length, chunks?.Length ?? 0);
            return string.Join(" ", transcripts.Where(t => !string.IsNullOrEmpty(t)));
        }

        // For now, simple concatenation with space
        // Actual deduplication happens in IAudioMerger implementation
        var result = string.Join(" ", transcripts.Where(t => !string.IsNullOrEmpty(t)));

        _logger.LogInformation("Merged {ChunkCount} transcripts into single output", transcripts.Length);

        return result;
    }
}
