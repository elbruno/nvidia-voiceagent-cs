namespace NvidiaVoiceAgent.Core.Services;

/// <summary>
/// Represents a contiguous chunk of audio with metadata for tracking overlap regions.
/// </summary>
/// <param name="Samples">Float32 audio samples in range [-1.0, 1.0]</param>
/// <param name="StartFrame">Frame index in original audio where this chunk starts (includes pre-overlap)</param>
/// <param name="EndFrame">Frame index in original audio where this chunk ends (includes post-overlap)</param>
/// <param name="OverlapStartFrame">Frame index where the overlap region begins (end of previous chunk)</param>
public record AudioChunk(float[] Samples, int StartFrame, int EndFrame, int OverlapStartFrame);

/// <summary>
/// Strategy for splitting audio into chunks for processing by models with input length constraints.
/// Implementations handle overlap management and edge cases.
/// </summary>
public interface IAudioChunkingStrategy
{
    /// <summary>
    /// Recommended chunk duration in seconds.
    /// </summary>
    float ChunkSizeSeconds { get; }

    /// <summary>
    /// Overlap duration in seconds between consecutive chunks.
    /// Used to minimize word-split artifacts at chunk boundaries.
    /// </summary>
    float OverlapSeconds { get; }

    /// <summary>
    /// Split audio into chunks with overlap.
    /// </summary>
    /// <param name="samples">Float32 audio samples at 16kHz mono</param>
    /// <param name="sampleRate">Sample rate in Hz (default 16000)</param>
    /// <returns>Array of chunks including overlap metadata</returns>
    /// <remarks>
    /// For audio shorter than chunk size, returns single chunk with no overlap.
    /// Overlap regions help detect and remove duplicate content during transcript merging.
    /// </remarks>
    AudioChunk[] ChunkAudio(float[] samples, int sampleRate = 16000);

    /// <summary>
    /// Merge transcripts from multiple chunks into single output.
    /// </summary>
    /// <param name="transcripts">Array of transcripts, one per chunk</param>
    /// <param name="chunks">Array of chunks (for overlap tracking)</param>
    /// <returns>Single merged transcript with duplicates removed</returns>
    /// <remarks>
    /// Implementation should:
    /// 1. Detect overlap regions where same text appears multiple times
    /// 2. Remove duplicates from overlap boundaries
    /// 3. Preserve sentence boundaries and punctuation
    /// 4. Handle cases where ASR output differs due to context changes
    /// </remarks>
    string MergeTranscripts(string[] transcripts, AudioChunk[] chunks);
}
