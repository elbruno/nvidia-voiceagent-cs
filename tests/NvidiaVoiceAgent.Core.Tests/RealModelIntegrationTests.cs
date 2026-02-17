using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NvidiaVoiceAgent.Core.Models;
using NvidiaVoiceAgent.Core.Services;

namespace NvidiaVoiceAgent.Core.Tests;

/// <summary>
/// Real-world integration tests using actual downloaded models.
/// These tests verify the full audio processing pipeline with Parakeet-TDT and PersonaPlex models.
/// Models must be downloaded in the same location as configured for the main application.
/// </summary>
public class RealModelIntegrationTests
{
    private readonly TestConfiguration _config;
    private readonly ILogger<AsrService> _asrLogger;
    private readonly ILogger<PersonaPlexService> _llmLogger;
    private readonly ModelConfig _modelConfig;

    public RealModelIntegrationTests()
    {
        _config = TestConfiguration.Instance;
        _asrLogger = _config.AsrLogger;
        _llmLogger = _config.LlmLogger;
        _modelConfig = _config.ModelConfig;
    }

    #region ASR (Parakeet-TDT) Real Model Tests

    [Fact]
    public async Task ParakeetTDT_LoadEncoder_SucceedsWithRealModel()
    {
        // Skip if model not downloaded
        if (!_config.AsrModelExists())
        {
            // This is expected in CI or fresh environments
            return;
        }

        // Arrange
        var service = new AsrService(_asrLogger, Options.Create(_modelConfig), null);

        // Act
        await service.LoadModelAsync();

        // Assert
        service.IsModelLoaded.Should().BeTrue("Parakeet-TDT encoder should load from configured path");
    }

    [Fact]
    public async Task ParakeetTDT_TranscribeSpeechLikeAudio_ProducesOutput()
    {
        if (!_config.AsrModelExists())
        {
            return;
        }

        // Arrange
        var service = new AsrService(_asrLogger, Options.Create(_modelConfig), null);
        await service.LoadModelAsync();

        // Generate speech-like audio (formant synthesis)
        var audioSamples = GenerateSpeechLikeAudio(duration: 2.0f);

        // Act
        var transcript = await service.TranscribeAsync(audioSamples);

        // Assert
        transcript.Should().NotBeNull();
        transcript.Should().NotBeEmpty("Real model should produce output for speech-like audio");
        transcript.Should().NotContain("RuntimeException", "Should not have ONNX errors");
        transcript.Should().NotContain("BroadcastIterator", "Should not have dimension errors");
        transcript.Should().NotContain("[Transcription error", "Should transcribe without errors");
    }

    [Fact]
    public async Task ParakeetTDT_ProcessVaryingAudioDurations_AllSucceed()
    {
        if (!_config.AsrModelExists())
        {
            return;
        }

        // Arrange
        var service = new AsrService(_asrLogger, Options.Create(_modelConfig), null);
        await service.LoadModelAsync();

        var durations = new[] { 0.5f, 1.0f, 2.0f, 3.5f, 5.0f };

        foreach (var duration in durations)
        {
            // Act
            var audioSamples = GenerateSpeechLikeAudio(duration);
            var transcript = await service.TranscribeAsync(audioSamples);

            // Assert
            transcript.Should().NotBeNull($"{duration}s audio should process");
            transcript.Should().NotContain("RuntimeException", $"{duration}s should not error");
            transcript.Should().NotContain("BroadcastIterator", $"{duration}s should not have dimension mismatch");
        }
    }

    [Fact]
    public async Task ParakeetTDT_HandleRealWorldAudioSampleCounts_NoDimensionErrors()
    {
        if (!_config.AsrModelExists())
        {
            return;
        }

        // Arrange
        var service = new AsrService(_asrLogger, Options.Create(_modelConfig), null);
        await service.LoadModelAsync();

        // Real-world sample counts from actual usage logs
        var realWorldSamples = new[]
        {
            81964,   // From logs: voice-session_20260217_111305
            98348,   // From logs: voice-session_20260217_112212
            16000,   // Exactly 1 second
            48000,   // 3 seconds
            80000    // 5 seconds
        };

        foreach (var sampleCount in realWorldSamples)
        {
            // Act
            var audioSamples = GenerateSpeechLikeAudio(sampleCount);
            var transcript = await service.TranscribeAsync(audioSamples);

            // Assert
            transcript.Should().NotBeNull($"{sampleCount} samples should process");
            transcript.Should().NotContain("BroadcastIterator",
                $"{sampleCount} samples caused dimension errors in the past - should be fixed");
            transcript.Should().NotContain("RuntimeException",
                $"{sampleCount} samples should not crash");
        }
    }

    [Fact]
    public async Task ParakeetTDT_MultipleConsecutiveCalls_RemainsStable()
    {
        if (!_config.AsrModelExists())
        {
            return;
        }

        // Arrange
        var service = new AsrService(_asrLogger, Options.Create(_modelConfig), null);
        await service.LoadModelAsync();

        // Act & Assert - Simulate multiple user requests
        for (int i = 0; i < 10; i++)
        {
            var audioSamples = GenerateSpeechLikeAudio(duration: 1.5f);
            var transcript = await service.TranscribeAsync(audioSamples);

            transcript.Should().NotBeNull($"Call {i + 1} should succeed");
            transcript.Should().NotContain("RuntimeException", $"Call {i + 1} should not error");
        }
    }

    [Fact]
    public async Task ParakeetTDT_PartialTranscription_ReturnsConfidence()
    {
        if (!_config.AsrModelExists())
        {
            return;
        }

        // Arrange
        var service = new AsrService(_asrLogger, Options.Create(_modelConfig), null);
        await service.LoadModelAsync();
        var audioSamples = GenerateSpeechLikeAudio(duration: 2.5f);

        // Act
        var (transcript, confidence) = await service.TranscribePartialAsync(audioSamples);

        // Assert
        transcript.Should().NotBeNull();
        transcript.Should().NotContain("RuntimeException");
        confidence.Should().BeInRange(0.0f, 1.0f, "Confidence should be a valid probability");
    }

    #endregion

    #region PersonaPlex LLM Real Model Tests

    [Fact]
    public async Task PersonaPlex_LoadModel_SucceedsWithRealModel()
    {
        if (!_config.PersonaPlexModelExists())
        {
            // Skip if PersonaPlex not downloaded
            return;
        }

        // Arrange
        var service = new PersonaPlexService(_llmLogger, Options.Create(_modelConfig));

        // Act
        await service.LoadModelAsync();

        // Assert
        service.IsModelLoaded.Should().BeTrue("PersonaPlex model should load from configured path");
    }

    [Fact]
    public async Task PersonaPlex_GenerateResponse_ProducesValidOutput()
    {
        if (!_config.PersonaPlexModelExists())
        {
            return;
        }

        // Arrange
        var service = new PersonaPlexService(_llmLogger, Options.Create(_modelConfig));
        await service.LoadModelAsync();

        var prompt = "Hello, how are you?";

        // Act
        var response = await service.GenerateResponseAsync(prompt);

        // Assert
        response.Should().NotBeNullOrWhiteSpace("LLM should generate response");
        response.Length.Should().BeGreaterThan(5, "Response should have meaningful length");
    }

    #endregion

    #region Full Pipeline Integration Tests

    [Fact]
    public async Task FullPipeline_ASRtoLLM_WorksEndToEnd()
    {
        if (!_config.AsrModelExists() || !_config.PersonaPlexModelExists())
        {
            // Skip if models not available
            return;
        }

        // Arrange - Create ASR and LLM services
        var asrService = new AsrService(_asrLogger, Options.Create(_modelConfig), null);
        var llmService = new PersonaPlexService(_llmLogger, Options.Create(_modelConfig));

        await asrService.LoadModelAsync();
        await llmService.LoadModelAsync();

        var audioSamples = GenerateSpeechLikeAudio(duration: 2.0f);

        // Act - Process through full pipeline
        var transcript = await asrService.TranscribeAsync(audioSamples);
        var llmResponse = await llmService.GenerateResponseAsync(transcript);

        // Assert
        transcript.Should().NotBeNull("ASR should produce transcript");
        transcript.Should().NotContain("RuntimeException");

        llmResponse.Should().NotBeNullOrWhiteSpace("LLM should generate response");
        llmResponse.Length.Should().BeGreaterThan(0);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Generate speech-like audio with formants and natural pitch variation.
    /// More realistic than simple sine waves for testing ASR.
    /// </summary>
    private float[] GenerateSpeechLikeAudio(float duration)
    {
        const int sampleRate = 16000;
        int sampleCount = (int)(duration * sampleRate);
        return GenerateSpeechLikeAudio(sampleCount);
    }

    /// <summary>
    /// Generate speech-like audio with specific sample count.
    /// Uses formant synthesis to create more realistic speech patterns.
    /// </summary>
    private float[] GenerateSpeechLikeAudio(int sampleCount)
    {
        var samples = new float[sampleCount];
        var random = new Random(42); // Fixed seed for reproducibility

        // Simulate speech with varying pitch and formants
        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / 16000.0f;

            // Varying fundamental frequency (F0) - simulates pitch variation
            float f0 = 120 + 30 * MathF.Sin(2 * MathF.PI * 2 * t); // 120-150 Hz (male voice)

            // Formants (resonant frequencies of vocal tract)
            float f1 = 700;  // First formant
            float f2 = 1220; // Second formant  
            float f3 = 2600; // Third formant

            // Formant amplitudes vary to simulate vowel changes
            float a1 = 0.4f * (1 + 0.3f * MathF.Sin(2 * MathF.PI * 3 * t));
            float a2 = 0.25f * (1 + 0.3f * MathF.Sin(2 * MathF.PI * 4 * t));
            float a3 = 0.15f * (1 + 0.3f * MathF.Sin(2 * MathF.PI * 5 * t));

            // Glottal pulse (excitation)
            float glottal = MathF.Sin(2 * MathF.PI * f0 * t);

            // Apply formant filters (simplified resonances)
            float signal = a1 * MathF.Sin(2 * MathF.PI * f1 * t) * glottal
                         + a2 * MathF.Sin(2 * MathF.PI * f2 * t) * glottal
                         + a3 * MathF.Sin(2 * MathF.PI * f3 * t) * glottal;

            // Add aspiration noise
            signal += 0.05f * (float)(random.NextDouble() - 0.5);

            // Amplitude modulation to simulate syllables
            float envelope = 0.5f + 0.5f * MathF.Sin(2 * MathF.PI * 4 * t);
            signal *= envelope;

            samples[i] = Math.Clamp(signal, -1.0f, 1.0f);
        }

        return samples;
    }

    #endregion
}
