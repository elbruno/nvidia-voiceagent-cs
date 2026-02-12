namespace NvidiaVoiceAgent.Services;

/// <summary>
/// Language Model service interface for Smart Mode.
/// Uses TinyLlama-1.1B or Phi-3 Mini with 4-bit quantization.
/// </summary>
public interface ILlmService
{
    /// <summary>
    /// Generate a response to a user query.
    /// </summary>
    /// <param name="prompt">User's transcribed speech</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generated response text</returns>
    Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load the LLM model into memory.
    /// </summary>
    Task LoadModelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the model is loaded and ready.
    /// </summary>
    bool IsModelLoaded { get; }
}
