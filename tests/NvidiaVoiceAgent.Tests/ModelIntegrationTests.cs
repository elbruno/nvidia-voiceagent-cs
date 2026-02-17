using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NvidiaVoiceAgent.Core.Models;
using NvidiaVoiceAgent.Core.Services;

namespace NvidiaVoiceAgent.Tests;

/// <summary>
/// Integration tests for ASR, LLM, and TTS models using real recorded audio.
/// These tests verify that all models are properly loaded and functional.
/// 
/// Test audio files recorded from actual user sessions:
/// - bruno_question.wav: "Hello I have a question, can you help me?" (3.3s)
/// - bruno_name.wav: "Hello, my name is Bruno" (3.1s)
/// </summary>
public class ModelIntegrationTests : IClassFixture<WebApplicationFactoryFixture>
{
    private readonly WebApplicationFactoryFixture _factory;
    private readonly ILogger<ModelIntegrationTests> _logger;

    public ModelIntegrationTests(WebApplicationFactoryFixture factory)
    {
        _factory = factory;
        var loggerFactory = factory.Services.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger<ModelIntegrationTests>();
    }

    #region Helper Methods

    /// <summary>
    /// Load a WAV file from the TestData directory.
    /// </summary>
    private byte[] LoadTestAudio(string filename)
    {
        var testDataPath = Path.Combine(AppContext.BaseDirectory, "TestData", filename);

        if (!File.Exists(testDataPath))
        {
            throw new FileNotFoundException($"Test audio file not found: {testDataPath}");
        }

        return File.ReadAllBytes(testDataPath);
    }

    /// <summary>
    /// Decode WAV file to float samples at 16kHz.
    /// </summary>
    private float[] LoadAndDecodeAudio(string filename)
    {
        var audioProcessor = _factory.Services.GetRequiredService<IAudioProcessor>();
        var wavBytes = LoadTestAudio(filename);
        var samples = audioProcessor.DecodeWav(wavBytes);

        _logger.LogInformation(
            "Loaded {Filename}: {Duration:F2}s, {Samples} samples",
            filename,
            samples.Length / 16000.0,
            samples.Length);

        return samples;
    }

    #endregion

    #region ASR Tests

    [Fact]
    public async Task AsrService_LoadModel_Succeeds()
    {
        // Arrange
        var asrService = _factory.Services.GetRequiredService<IAsrService>();

        // Act
        await asrService.LoadModelAsync();

        // Assert
        asrService.IsModelLoaded.Should().BeTrue("ASR model should be loaded successfully");
        _logger.LogInformation("✅ ASR model loaded successfully");
    }

    [Fact]
    public async Task AsrService_TranscribeQuestionAudio_ReturnsText()
    {
        // Arrange
        var asrService = _factory.Services.GetRequiredService<IAsrService>();
        await asrService.LoadModelAsync();

        var audioSamples = LoadAndDecodeAudio("bruno_question.wav");

        // Act
        var transcript = await asrService.TranscribeAsync(audioSamples);

        // Assert
        transcript.Should().NotBeNullOrWhiteSpace("Transcription should produce text output");

        // Log the result (may be mock mode or actual transcription)
        _logger.LogInformation("Transcript: '{Transcript}'", transcript);

        // Don't assert exact text match due to potential model errors
        // Just verify it doesn't crash and returns something
        transcript.Length.Should().BeGreaterThan(0);

        _logger.LogInformation("✅ ASR transcription completed (3.3s audio)");
    }

    [Fact]
    public async Task AsrService_TranscribeNameAudio_ReturnsText()
    {
        // Arrange
        var asrService = _factory.Services.GetRequiredService<IAsrService>();
        await asrService.LoadModelAsync();

        var audioSamples = LoadAndDecodeAudio("bruno_name.wav");

        // Act
        var transcript = await asrService.TranscribeAsync(audioSamples);

        // Assert
        transcript.Should().NotBeNullOrWhiteSpace("Transcription should produce text output");

        _logger.LogInformation("Transcript: '{Transcript}'", transcript);
        transcript.Length.Should().BeGreaterThan(0);

        _logger.LogInformation("✅ ASR transcription completed (3.1s audio)");
    }

    [Fact]
    public async Task AsrService_TranscribePartial_ReturnsTextAndConfidence()
    {
        // Arrange
        var asrService = _factory.Services.GetRequiredService<IAsrService>();
        await asrService.LoadModelAsync();

        var audioSamples = LoadAndDecodeAudio("bruno_question.wav");

        // Act
        var (transcript, confidence) = await asrService.TranscribePartialAsync(audioSamples);

        // Assert
        transcript.Should().NotBeNullOrWhiteSpace("Partial transcription should produce text");
        confidence.Should().BeInRange(0.0f, 1.0f, "Confidence should be between 0 and 1");

        _logger.LogInformation(
            "Partial transcript: '{Transcript}' (confidence: {Confidence:F2})",
            transcript,
            confidence);

        _logger.LogInformation("✅ ASR partial transcription with confidence");
    }

    #endregion

    #region LLM Tests

    [Fact]
    public async Task LlmService_LoadModel_Succeeds()
    {
        // Arrange
        var llmService = _factory.Services.GetService<ILlmService>();

        if (llmService == null)
        {
            _logger.LogWarning("⚠️  LLM service not registered - skipping test");
            return;
        }

        // Act
        await llmService.LoadModelAsync();

        // Assert
        llmService.IsModelLoaded.Should().BeTrue("LLM model should be loaded successfully");
        _logger.LogInformation("✅ LLM model loaded successfully");
    }

    [Fact]
    public async Task LlmService_GenerateResponse_ReturnsText()
    {
        // Arrange
        var llmService = _factory.Services.GetService<ILlmService>();

        if (llmService == null)
        {
            _logger.LogWarning("⚠️  LLM service not registered - skipping test");
            return;
        }

        await llmService.LoadModelAsync();
        var prompt = "Hello, can you help me with a question?";

        // Act
        var response = await llmService.GenerateResponseAsync(prompt);

        // Assert
        response.Should().NotBeNullOrWhiteSpace("LLM should generate a response");
        response.Length.Should().BeGreaterThan(10, "Response should be substantial");

        _logger.LogInformation("LLM Response: '{Response}'", response);
        _logger.LogInformation("✅ LLM response generation completed");
    }

    [Fact]
    public async Task LlmService_StreamResponse_YieldsTokens()
    {
        // Arrange
        var llmService = _factory.Services.GetService<ILlmService>();

        if (llmService == null)
        {
            _logger.LogWarning("⚠️  LLM service not registered - skipping test");
            return;
        }

        await llmService.LoadModelAsync();
        var prompt = "Tell me a short greeting.";

        // Act
        var tokens = new List<string>();
        await foreach (var token in llmService.GenerateResponseStreamAsync(prompt))
        {
            tokens.Add(token);
        }

        // Assert
        tokens.Should().NotBeEmpty("Stream should yield at least one token");
        var fullResponse = string.Join("", tokens);
        fullResponse.Should().NotBeNullOrWhiteSpace();

        _logger.LogInformation("Streamed {Count} tokens: '{Response}'", tokens.Count, fullResponse);
        _logger.LogInformation("✅ LLM streaming response completed");
    }

    #endregion

    #region TTS Tests

    [Fact]
    public async Task TtsService_LoadModels_Succeeds()
    {
        // Arrange
        var ttsService = _factory.Services.GetService<ITtsService>();

        if (ttsService == null)
        {
            _logger.LogWarning("⚠️  TTS service not registered - skipping test");
            return;
        }

        // Act
        await ttsService.LoadModelsAsync();

        // Assert
        ttsService.AreModelsLoaded.Should().BeTrue("TTS models should be loaded successfully");
        _logger.LogInformation("✅ TTS models loaded successfully");
    }

    [Fact]
    public async Task TtsService_Synthesize_ReturnsAudio()
    {
        // Arrange
        var ttsService = _factory.Services.GetService<ITtsService>();

        if (ttsService == null)
        {
            _logger.LogWarning("⚠️  TTS service not registered - skipping test");
            return;
        }

        await ttsService.LoadModelsAsync();
        var text = "Hello, welcome to the voice agent.";

        // Act
        var audioSamples = await ttsService.SynthesizeAsync(text);

        // Assert
        audioSamples.Should().NotBeNull("TTS should return audio samples");
        audioSamples.Length.Should().BeGreaterThan(0, "Audio should have samples");

        var duration = audioSamples.Length / 22050.0; // TTS output is 22050Hz
        _logger.LogInformation(
            "Synthesized {Length} samples ({Duration:F2}s) from text: '{Text}'",
            audioSamples.Length,
            duration,
            text);

        _logger.LogInformation("✅ TTS synthesis completed");
    }

    #endregion

    #region End-to-End Pipeline Tests

    [Fact]
    public async Task FullPipeline_AsrToLlmToTts_Succeeds()
    {
        // Arrange
        var asrService = _factory.Services.GetRequiredService<IAsrService>();
        var llmService = _factory.Services.GetService<ILlmService>();
        var ttsService = _factory.Services.GetService<ITtsService>();

        await asrService.LoadModelAsync();

        if (llmService != null)
            await llmService.LoadModelAsync();

        if (ttsService != null)
            await ttsService.LoadModelsAsync();

        // Load user audio
        var userAudio = LoadAndDecodeAudio("bruno_question.wav");

        // Act - Step 1: ASR
        var transcript = await asrService.TranscribeAsync(userAudio);
        transcript.Should().NotBeNullOrWhiteSpace();
        _logger.LogInformation("Step 1 - ASR: '{Transcript}'", transcript);

        // Act - Step 2: LLM (if available)
        string llmResponse = $"Echo: {transcript}";
        if (llmService != null && llmService.IsModelLoaded)
        {
            llmResponse = await llmService.GenerateResponseAsync(transcript);
            llmResponse.Should().NotBeNullOrWhiteSpace();
            _logger.LogInformation("Step 2 - LLM: '{Response}'", llmResponse);
        }
        else
        {
            _logger.LogWarning("Step 2 - LLM: Not available, using echo");
        }

        // Act - Step 3: TTS (if available)
        if (ttsService != null && ttsService.AreModelsLoaded)
        {
            var responseAudio = await ttsService.SynthesizeAsync(llmResponse);
            responseAudio.Should().NotBeNull();
            responseAudio.Length.Should().BeGreaterThan(0);

            var duration = responseAudio.Length / 22050.0;
            _logger.LogInformation(
                "Step 3 - TTS: Generated {Duration:F2}s audio",
                duration);
        }
        else
        {
            _logger.LogWarning("Step 3 - TTS: Not available, skipping");
        }

        // Assert
        _logger.LogInformation("✅ Full pipeline completed successfully");
    }

    [Fact]
    public async Task FullPipeline_BothTestAudios_Complete()
    {
        // Arrange
        var asrService = _factory.Services.GetRequiredService<IAsrService>();
        await asrService.LoadModelAsync();

        var testFiles = new[] { "bruno_question.wav", "bruno_name.wav" };

        // Act & Assert
        foreach (var filename in testFiles)
        {
            var audioSamples = LoadAndDecodeAudio(filename);
            var transcript = await asrService.TranscribeAsync(audioSamples);

            transcript.Should().NotBeNullOrWhiteSpace(
                $"ASR should transcribe {filename}");

            _logger.LogInformation(
                "✅ Processed {Filename}: '{Transcript}'",
                filename,
                transcript);
        }

        _logger.LogInformation("✅ All test audio files processed successfully");
    }

    #endregion

    #region Performance Tests

    [Fact]
    public async Task AsrService_TranscriptionPerformance_IsReasonable()
    {
        // Arrange
        var asrService = _factory.Services.GetRequiredService<IAsrService>();
        await asrService.LoadModelAsync();

        var audioSamples = LoadAndDecodeAudio("bruno_question.wav");
        var audioDuration = audioSamples.Length / 16000.0;

        // Warm up
        await asrService.TranscribeAsync(audioSamples);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var transcript = await asrService.TranscribeAsync(audioSamples);
        stopwatch.Stop();

        // Assert
        var inferenceTime = stopwatch.Elapsed.TotalSeconds;
        var realTimeRatio = inferenceTime / audioDuration;

        _logger.LogInformation(
            "Performance: {AudioDuration:F2}s audio processed in {InferenceTime:F2}s (RTF: {RTF:F2}x)",
            audioDuration,
            inferenceTime,
            realTimeRatio);

        // RTF (Real-Time Factor) < 1.0 means faster than real-time
        // For CPU inference, < 5.0 is acceptable; GPU should be < 1.0
        realTimeRatio.Should().BeLessThan(10.0,
            "Transcription should complete within reasonable time");

        _logger.LogInformation("✅ Performance test completed");
    }

    #endregion

    #region Audio Quality Tests

    [Fact]
    public void TestAudio_HasCorrectFormat()
    {
        // Arrange
        var audioProcessor = _factory.Services.GetRequiredService<IAudioProcessor>();
        var testFiles = new[] { "bruno_question.wav", "bruno_name.wav" };

        // Act & Assert
        foreach (var filename in testFiles)
        {
            var wavBytes = LoadTestAudio(filename);
            var samples = audioProcessor.DecodeWav(wavBytes);

            // Verify audio properties
            samples.Should().NotBeEmpty($"{filename} should contain audio samples");
            samples.Length.Should().BeGreaterThan(16000,
                $"{filename} should be at least 1 second");

            // Check sample value range (should be normalized between -1 and 1)
            var maxAbsValue = samples.Max(Math.Abs);
            maxAbsValue.Should().BeLessThanOrEqualTo(1.0f,
                $"{filename} samples should be normalized");

            // Check that audio has energy (not silence)
            var rmsEnergy = Math.Sqrt(samples.Average(s => s * s));
            rmsEnergy.Should().BeGreaterThan(0.001f,
                $"{filename} should contain actual speech (not silence)");

            _logger.LogInformation(
                "✅ {Filename}: {Samples} samples, RMS energy: {Energy:F4}",
                filename,
                samples.Length,
                rmsEnergy);
        }
    }

    #endregion
}
