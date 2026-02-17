using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NvidiaVoiceAgent.Core.Services;

/// <summary>
/// Merges transcripts from overlapping audio chunks by detecting and removing duplicate text
/// that appears in the overlap regions between consecutive chunks.
/// </summary>
public class TranscriptMerger : IAudioMerger
{
    private readonly ILogger<TranscriptMerger> _logger;

    public TranscriptMerger(ILogger<TranscriptMerger>? logger = null)
    {
        _logger = logger ?? NullLogger<TranscriptMerger>.Instance;
    }

    public string MergeTranscripts(string[] transcripts, float overlapFraction)
    {
        if (transcripts == null || transcripts.Length == 0)
            return string.Empty;

        if (transcripts.Length == 1)
            return transcripts[0] ?? string.Empty;

        var result = new List<string>();
        string previousFull = string.Empty;

        for (int i = 0; i < transcripts.Length; i++)
        {
            string current = transcripts[i] ?? string.Empty;

            if (i == 0)
            {
                // First chunk: add all text
                result.Add(current);
                previousFull = current;
            }
            else
            {
                // Subsequent chunks: try to detect and remove overlap
                string merged = MergeAdjacentTranscripts(previousFull, current, overlapFraction);
                result.Add(merged);
                previousFull = merged;
            }
        }

        string finalResult = string.Join(" ", result).TrimEnd();

        _logger.LogInformation(
            "Merged {ChunkCount} transcripts using overlap fraction {Fraction}. Final length: {Length} chars",
            transcripts.Length, overlapFraction, finalResult.Length);

        return finalResult;
    }

    /// <summary>
    /// Merge two adjacent transcripts, removing detected duplicate from overlap region.
    /// </summary>
    private string MergeAdjacentTranscripts(string previous, string current, float overlapFraction)
    {
        if (string.IsNullOrEmpty(previous))
            return current;

        if (string.IsNullOrEmpty(current))
            return previous;

        // Estimate number of tokens in overlap region
        // Rough heuristic: ~4 tokens per second, overlap is typically 2s = ~8 tokens
        // But we'll be more conservative and search for reasonable overlap sizes
        int maxOverlapTokens = Math.Max(5, (int)(previous.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length * overlapFraction * 2));
        int minOverlapTokens = 1;

        // Try to find matching text at end of previous and start of current
        var overlapMatch = FindOverlap(previous, current, minOverlapTokens, maxOverlapTokens);

        if (!string.IsNullOrEmpty(overlapMatch))
        {
            // Found overlap: remove it from the start of current
            int overlapIndex = current.IndexOf(overlapMatch, StringComparison.OrdinalIgnoreCase);
            if (overlapIndex >= 0)
            {
                int afterOverlap = overlapIndex + overlapMatch.Length;

                // Trim to next word boundary
                while (afterOverlap < current.Length && char.IsWhiteSpace(current[afterOverlap]))
                    afterOverlap++;

                string withoutOverlap = current.Substring(afterOverlap).Trim();

                _logger.LogDebug(
                    "Detected overlap: '{Overlap}' ({Length} chars). Removing from current transcript.",
                    overlapMatch, overlapMatch.Length);

                return withoutOverlap;
            }
        }

        // No overlap detected: just concatenate
        return current;
    }

    /// <summary>
    /// Find text that appears at the end of previous and start of current transcript.
    /// </summary>
    private string? FindOverlap(string previous, string current, int minTokens, int maxTokens)
    {
        var previousTokens = TokenizeText(previous);
        var currentTokens = TokenizeText(current);

        if (previousTokens.Length == 0 || currentTokens.Length == 0)
            return null;

        // Try overlap lengths from longest to shortest
        for (int overlapLen = Math.Min(maxTokens, Math.Min(previousTokens.Length, currentTokens.Length)); overlapLen >= minTokens; overlapLen--)
        {
            // Get last N tokens from previous
            var previousEnd = new string[overlapLen];
            Array.Copy(previousTokens, previousTokens.Length - overlapLen, previousEnd, 0, overlapLen);

            // Get first N tokens from current
            var currentStart = new string[overlapLen];
            Array.Copy(currentTokens, 0, currentStart, 0, overlapLen);

            // Compare (case-insensitive, ignoring punctuation)
            if (TokensMatch(previousEnd, currentStart))
            {
                return string.Join(" ", currentStart);
            }
        }

        return null;
    }

    /// <summary>
    /// Check if two token arrays match semantically (case-insensitive, punctuation-tolerant).
    /// </summary>
    private bool TokensMatch(string[] tokens1, string[] tokens2)
    {
        if (tokens1.Length != tokens2.Length)
            return false;

        for (int i = 0; i < tokens1.Length; i++)
        {
            // Normalize: remove punctuation, lowercase
            string norm1 = NormalizeToken(tokens1[i]);
            string norm2 = NormalizeToken(tokens2[i]);

            // Exact match or highly similar (Levenshtein distance <= 1)
            if (!string.Equals(norm1, norm2, StringComparison.OrdinalIgnoreCase))
            {
                if (LevenshteinDistance(norm1, norm2) > 1)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Normalize a token by removing punctuation and converting to lowercase.
    /// </summary>
    private string NormalizeToken(string token)
    {
        // Remove common punctuation
        return Regex.Replace(token.ToLowerInvariant(), @"[.,!?;:\""()—-]+", "");
    }

    /// <summary>
    /// Compute Levenshtein distance between two strings (for fuzzy matching).
    /// </summary>
    private int LevenshteinDistance(string s1, string s2)
    {
        if (s1.Length == 0) return s2.Length;
        if (s2.Length == 0) return s1.Length;

        int[,] d = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= s2.Length; j++) d[0, j] = j;

        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;

                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[s1.Length, s2.Length];
    }

    /// <summary>
    /// Split text into tokens (words), preserving punctuation-attached words.
    /// </summary>
    private string[] TokenizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
