using NvidiaVoiceAgent.Core.Models;

namespace NvidiaVoiceAgent.Core.Adapters;

/// <summary>
/// Base interface for model adapters.
/// Adapters encapsulate model-specific preprocessing, inference, and postprocessing logic.
/// </summary>
/// <typeparam name="TInput">Prepared input type for the model</typeparam>
/// <typeparam name="TOutput">Output type after postprocessing</typeparam>
public interface IModelAdapter<TInput, TOutput> : IDisposable
{
    /// <summary>
    /// Model name from specification.
    /// </summary>
    string ModelName { get; }

    /// <summary>
    /// Whether the model has been successfully loaded.
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Load the model from the specified path.
    /// </summary>
    /// <param name="modelPath">Directory containing the model and spec file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task LoadAsync(string modelPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepare raw input data according to model requirements.
    /// </summary>
    /// <param name="rawData">Raw input (e.g., audio samples, text)</param>
    /// <returns>Prepared input tensor/data</returns>
    TInput PrepareInput(object rawData);

    /// <summary>
    /// Run inference with prepared input.
    /// </summary>
    /// <param name="input">Prepared input from PrepareInput</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processed output</returns>
    Task<TOutput> InferAsync(TInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the model specification.
    /// </summary>
    ModelSpecification GetSpecification();
}

/// <summary>
/// Convenience interface for ASR models (audio → text).
/// </summary>
public interface IAsrModelAdapter : IModelAdapter<float[,], string>
{
    /// <summary>
    /// Transcribe audio samples directly (combines PrepareInput + InferAsync).
    /// </summary>
    /// <param name="audioSamples">Audio samples (mono, sample rate from spec)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<string> TranscribeAsync(float[] audioSamples, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribe with confidence score.
    /// </summary>
    Task<(string transcript, float confidence)> TranscribeWithConfidenceAsync(
        float[] audioSamples,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Convenience interface for TTS acoustic models (phonemes → mel-spectrogram).
/// </summary>
public interface ITtsAcousticModelAdapter : IModelAdapter<int[], float[,]>
{
    /// <summary>
    /// Generate mel-spectrogram from phoneme sequence.
    /// </summary>
    Task<float[,]> GenerateMelAsync(int[] phonemes, CancellationToken cancellationToken = default);
}

/// <summary>
/// Convenience interface for vocoder models (mel-spectrogram → waveform).
/// </summary>
public interface IVocoderModelAdapter : IModelAdapter<float[,], float[]>
{
    /// <summary>
    /// Generate waveform from mel-spectrogram.
    /// </summary>
    Task<float[]> GenerateWaveformAsync(float[,] melSpectrogram, CancellationToken cancellationToken = default);
}

/// <summary>
/// Convenience interface for LLM models (text → text).
/// </summary>
public interface ILlmModelAdapter : IModelAdapter<string, string>
{
    /// <summary>
    /// Generate text completion.
    /// </summary>
    /// <param name="prompt">Input prompt</param>
    /// <param name="maxTokens">Maximum tokens to generate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<string> GenerateAsync(
        string prompt,
        int maxTokens = 512,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream generated tokens.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        string prompt,
        int maxTokens = 512,
        CancellationToken cancellationToken = default);
}
