using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NvidiaVoiceAgent.Core.Models;
using NvidiaVoiceAgent.Core.Services;
using NvidiaVoiceAgent.ModelHub;

namespace NvidiaVoiceAgent.Core.Tests;

/// <summary>
/// Unit tests for ASR service to prevent dimension mismatch errors.
/// These tests validate that the ASR service correctly processes audio inputs.
/// Tests are configured via appsettings.Test.json to use the same model paths as the main app.
/// </summary>
public class AsrServiceTests
{
    private readonly TestConfiguration _config;
    private readonly ILogger<AsrService> _logger;
    private readonly ModelConfig _modelConfig;

    public AsrServiceTests()
    {
        _config = TestConfiguration.Instance;
        _logger = _config.AsrLogger;
        _modelConfig = _config.ModelConfig;
    }

    #region Mock Mode Tests

    [Fact]
    public async Task TranscribeAsync_WithNoModel_UsesMockMode()
    {
        // Arrange
        var config = new ModelConfig { AsrModelPath = "nonexistent/path" };
        var service = new AsrService(_logger, Options.Create(config), null);
        var audioSamples = GenerateTestAudio(duration: 1.0f);

        // Act
        var result = await service.TranscribeAsync(audioSamples);

        // Assert
        result.Should().NotBeNullOrEmpty("Mock mode should return a mock transcript");
        service.IsModelLoaded.Should().BeTrue("Service should be 'loaded' in mock mode");
    }

    [Fact]
    public async Task TranscribeAsync_WithShortAudio_ReturnsEmpty()
    {
        // Arrange
        var config = new ModelConfig { AsrModelPath = "nonexistent/path" };
        var service = new AsrService(_logger, Options.Create(config), null);
        var audioSamples = GenerateTestAudio(duration: 0.1f); // Too short

        // Act
        var result = await service.TranscribeAsync(audioSamples);

        // Assert
        result.Should().BeEmpty("Very short audio should return empty string");
    }

    [Fact]
    public async Task TranscribePartialAsync_WithMockMode_ReturnsConfidence()
    {
        // Arrange
        var config = new ModelConfig { AsrModelPath = "nonexistent/path" };
        var service = new AsrService(_logger, Options.Create(config), null);
        var audioSamples = GenerateTestAudio(duration: 1.0f);

        // Act
        var (transcript, confidence) = await service.TranscribePartialAsync(audioSamples);

        // Assert
        transcript.Should().NotBeNullOrEmpty();
        confidence.Should().BeInRange(0.0f, 1.0f);
    }

    #endregion

    #region Real Model Tests (Integration)

    [Fact]
    public async Task LoadModelAsync_WithRealParakeetModel_LoadsSuccessfully()
    {
        // Arrange
        if (!_config.AsrModelExists())
        {
            // Skip if model not available  
            return;
        }

        var service = new AsrService(_logger, Options.Create(_modelConfig), null);

        // Act
        await service.LoadModelAsync();

        // Assert
        service.IsModelLoaded.Should().BeTrue("Real Parakeet-TDT model should load");
    }

    [Fact]
    public async Task TranscribeAsync_WithRealModel_ProducesValidTranscript()
    {
        // Arrange
        if (!_config.AsrModelExists())
        {
            return;
        }

        var service = new AsrService(_logger, Options.Create(_modelConfig), null);
        await service.LoadModelAsync();
        var audioSamples = GenerateTestAudio(duration: 1.0f);

        // Act
        var result = await service.TranscribeAsync(audioSamples);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotContain("RuntimeException", "Should not have ONNX runtime errors");
        result.Should().NotContain("BroadcastIterator", "Should not have dimension mismatch errors");
        result.Should().NotContain("[Transcription error", "Should not have transcription errors");

        // With real model, we expect actual output (not mock or error)
        result.Length.Should().BeGreaterThan(0, "Real model should produce output");
    }

    [Fact]
    public async Task TranscribeAsync_WithRealModel_HandlesDifferentLengths()
    {
        // Arrange
        if (!_config.AsrModelExists())
        {
            return;
        }

        var service = new AsrService(_logger, Options.Create(_modelConfig), null);
        await service.LoadModelAsync();

        var testDurations = new[] { 0.5f, 1.0f, 2.0f, 3.0f, 5.0f };

        foreach (var duration in testDurations)
        {
            // Act
            var audioSamples = GenerateTestAudio(duration);
            var result = await service.TranscribeAsync(audioSamples);

            // Assert
            result.Should().NotBeNull($"Duration {duration}s should process");
            result.Should().NotContain("RuntimeException", $"Duration {duration}s should not have runtime errors");
            result.Should().NotContain("BroadcastIterator", $"Duration {duration}s should not have dimension errors");
        }
    }

    [Fact]
    public async Task TranscribeAsync_WithRealModel_RemainsStableAcrossMultipleCalls()
    {
        // Arrange
        if (!_config.AsrModelExists())
        {
            return;
        }

        var service = new AsrService(_logger, Options.Create(_modelConfig), null);
        await service.LoadModelAsync();
        var audioSamples = GenerateTestAudio(duration: 1.0f);

        // Act & Assert - Call multiple times to ensure stability
        for (int i = 0; i < 5; i++)
        {
            var result = await service.TranscribeAsync(audioSamples);
            result.Should().NotBeNull($"Iteration {i} should succeed");
            result.Should().NotContain("RuntimeException", $"Iteration {i} should not crash");
            result.Should().NotContain("[Transcription error", $"Iteration {i} should not error");
        }
    }

    [Fact]
    public async Task TranscribeAsync_WithRealModel_HandlesEdgeCaseLengths()
    {
        // Arrange
        if (!_config.AsrModelExists())
        {
            return;
        }

        var service = new AsrService(_logger, Options.Create(_modelConfig), null);
        await service.LoadModelAsync();

        // Test edge cases: very short, exact multiples of hop length, etc.
        var edgeCaseSamples = new[]
        {
            160,    // Exactly 1 hop length (10ms)
            320,    // 2 hop lengths
            800,    // Exactly 50ms
            1600,   // Exactly 100ms
            16000,  // Exactly 1 second
            16160,  // 1 second + 1 hop
            25000,  // Odd length
            32768,  // Power of 2
            98348   // Real world example from logs
        };

        foreach (var sampleCount in edgeCaseSamples)
        {
            // Act
            var audioSamples = GenerateTestAudio(sampleCount);
            var result = await service.TranscribeAsync(audioSamples);

            // Assert
            result.Should().NotBeNull($"{sampleCount} samples should process");
            result.Should().NotContain("BroadcastIterator",
                $"{sampleCount} samples should not have dimension errors");
            result.Should().NotContain("RuntimeException",
                $"{sampleCount} samples should not crash");
        }
    }

    [Fact]
    public async Task TranscribePartialAsync_WithRealModel_ReturnsValidConfidence()
    {
        // Arrange
        if (!_config.AsrModelExists())
        {
            return;
        }

        var service = new AsrService(_logger, Options.Create(_modelConfig), null);
        await service.LoadModelAsync();
        var audioSamples = GenerateTestAudio(duration: 2.0f);

        // Act
        var (transcript, confidence) = await service.TranscribePartialAsync(audioSamples);

        // Assert
        transcript.Should().NotBeNull();
        confidence.Should().BeInRange(0.0f, 1.0f);
        transcript.Should().NotContain("RuntimeException");
        transcript.Should().NotContain("[Transcription error");
    }

    #endregion

    #region Mel-Spectrogram Configuration Tests

    [Fact]
    public void MelExtractor_DefaultConfiguration_Uses128Bins()
    {
        // Arrange & Act
        var extractor = new MelSpectrogramExtractor();

        // Assert
        extractor.NumMels.Should().Be(128, "Default should match Parakeet-TDT-V2 expectations");
    }

    [Fact]
    public void MelExtractor_WithCustomBins_ConfiguresCorrectly()
    {
        // Arrange & Act
        var extractor = new MelSpectrogramExtractor(nMels: 80);

        // Assert
        extractor.NumMels.Should().Be(80);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Generate test audio samples (mono, 16kHz).
    /// </summary>
    private float[] GenerateTestAudio(float duration)
    {
        const int sampleRate = 16000;
        int sampleCount = (int)(duration * sampleRate);
        return GenerateTestAudio(sampleCount);
    }

    /// <summary>
    /// Generate test audio with specific sample count.
    /// Creates a simple sine wave with some noise.
    /// </summary>
    private float[] GenerateTestAudio(int sampleCount)
    {
        var samples = new float[sampleCount];
        var random = new Random(42); // Fixed seed for reproducibility

        for (int i = 0; i < sampleCount; i++)
        {
            // Mix of sine waves at different frequencies (simulate speech formants)
            float t = i / 16000.0f;
            float signal = 0.3f * MathF.Sin(2 * MathF.PI * 200 * t)   // F1
                         + 0.2f * MathF.Sin(2 * MathF.PI * 800 * t)   // F2
                         + 0.1f * MathF.Sin(2 * MathF.PI * 2400 * t)  // F3
                         + 0.05f * (float)(random.NextDouble() - 0.5); // Noise

            samples[i] = Math.Clamp(signal, -1.0f, 1.0f);
        }

        return samples;
    }

    #endregion
}
