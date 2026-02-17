namespace NvidiaVoiceAgent.Core.Services;

/// <summary>
/// Strategy for merging transcripts from overlapping audio chunks while removing duplicate content.
/// </summary>
public interface IAudioMerger
{
    /// <summary>
    /// Merge multiple transcripts from overlapping chunks, removing detected duplicates.
    /// </summary>
    /// <param name="transcripts">Array of transcripts from consecutive chunks</param>
    /// <param name="overlapFraction">Fraction of chunk length dedicated to overlap (e.g., 0.04 for 2s overlap in 50s chunk)</param>
    /// <returns>Single merged transcript with duplicates removed</returns>
    /// <remarks>
    /// Algorithm:
    /// 1. For each pair of consecutive transcripts:
    ///    - Estimate how many tokens should be in overlap region
    ///    - Search for matching text at end of first transcript and start of second
    ///    - If found with >80% confidence, remove from second transcript start
    /// 2. Concatenate all unique segments
    /// 3. Clean up excess whitespace
    ///
    /// Handles edge cases:
    /// - Very short transcripts (single words should still deduplicate)
    /// - Punctuation differences ("hello," vs "hello")
    /// - Partial matches (some words missing from ASR)
    /// </remarks>
    string MergeTranscripts(string[] transcripts, float overlapFraction);
}
