using FluentAssertions;
using Microsoft.Extensions.Logging;
using NvidiaVoiceAgent.Core.Adapters;

namespace NvidiaVoiceAgent.Core.Tests;

/// <summary>
/// Tests for the Parakeet-TDT adapter to verify it correctly handles
/// model specifications and resolves dimension mismatch errors.
/// </summary>
public class ParakeetAdapterTests
{
    private readonly TestConfiguration _config;
    private readonly ILogger<ParakeetTdtAdapter> _logger;

    public ParakeetAdapterTests()
    {
        _config = TestConfiguration.Instance;
        _logger = _config.CreateLogger<ParakeetTdtAdapter>();
    }

    [Fact]
    public async Task LoadAsync_WithValidModelPath_LoadsSuccessfully()
    {
        // Arrange
        if (!_config.AsrModelExists())
        {
            // Skip if model not available
            return;
        }

        var adapter = new ParakeetTdtAdapter(_logger);
        var modelPath = _config.ModelConfig.AsrModelPath;

        // Act
        await adapter.LoadAsync(modelPath);

        // Assert
        adapter.IsLoaded.Should().BeTrue();
        adapter.ModelName.Should().Be("parakeet-tdt-0.6b");

        var spec = adapter.GetSpecification();
        spec.Should().NotBeNull();
        spec.ModelType.Should().Be("asr_encoder_ctc");
    }

    [Fact]
    public async Task LoadAsync_WithMissingSpec_ThrowsException()
    {
        // Arrange
        var adapter = new ParakeetTdtAdapter(_logger);
        var invalidPath = "nonexistent/path";

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => adapter.LoadAsync(invalidPath));
    }

    [Fact]
    public async Task TranscribeAsync_WithValidAudio_ReturnsTranscript()
    {
        // Arrange
        if (!_config.AsrModelExists())
        {
            return;
        }

        var adapter = new ParakeetTdtAdapter(_logger);
        await adapter.LoadAsync(_config.ModelConfig.AsrModelPath);

        var audioSamples = GenerateTestAudio(duration: 1.0f);

        // Act
        var transcript = await adapter.TranscribeAsync(audioSamples);

        // Assert
        transcript.Should().NotBeNull();
        transcript.Should().NotContain("RuntimeException", "Should not have ONNX runtime errors");
        transcript.Should().NotContain("BroadcastIterator", "Should not have dimension mismatch errors");
        transcript.Should().NotContain("[Transcription error", "Should not have transcription errors");
    }

    [Fact]
    public async Task TranscribeAsync_WithVariableLengths_HandlesAllLengths()
    {
        // Arrange
        if (!_config.AsrModelExists())
        {
            return;
        }

        var adapter = new ParakeetTdtAdapter(_logger);
        await adapter.LoadAsync(_config.ModelConfig.AsrModelPath);

        var testDurations = new[] { 0.5f, 1.0f, 2.0f, 3.0f };

        foreach (var duration in testDurations)
        {
            // Act
            var audioSamples = GenerateTestAudio(duration);
            var transcript = await adapter.TranscribeAsync(audioSamples);

            // Assert
            transcript.Should().NotBeNull($"Duration {duration}s should process");
            transcript.Should().NotContain("RuntimeException",
                $"Duration {duration}s should not have runtime errors");
            transcript.Should().NotContain("BroadcastIterator",
                $"Duration {duration}s should not have dimension errors");
        }
    }

    [Fact]
    public async Task TranscribeWithConfidenceAsync_ReturnsValidConfidence()
    {
        // Arrange
        if (!_config.AsrModelExists())
        {
            return;
        }

        var adapter = new ParakeetTdtAdapter(_logger);
        await adapter.LoadAsync(_config.ModelConfig.AsrModelPath);

        var audioSamples = GenerateTestAudio(duration: 2.0f);

        // Act
        var (transcript, confidence) = await adapter.TranscribeWithConfidenceAsync(audioSamples);

        // Assert
        transcript.Should().NotBeNull();
        confidence.Should().BeInRange(0.0f, 1.0f);
        transcript.Should().NotContain("RuntimeException");
    }

    [Fact]
    public async Task PrepareInput_CreatesCorrectMelSpectrogram()
    {
        // Arrange
        if (!_config.AsrModelExists())
        {
            return;
        }

        var adapter = new ParakeetTdtAdapter(_logger);
        await adapter.LoadAsync(_config.ModelConfig.AsrModelPath);

        var audioSamples = GenerateTestAudio(duration: 1.0f);

        // Act
        var melSpec = adapter.PrepareInput(audioSamples);

        // Assert
        melSpec.Should().NotBeNull();
        melSpec.GetLength(1).Should().Be(128, "Parakeet spec requires 128 mel bins");
        melSpec.GetLength(0).Should().BeGreaterThan(0, "Should have time frames");
    }

    [Fact]
    public async Task MultipleInferences_RemainStable()
    {
        // Arrange
        if (!_config.AsrModelExists())
        {
            return;
        }

        var adapter = new ParakeetTdtAdapter(_logger);
        await adapter.LoadAsync(_config.ModelConfig.AsrModelPath);

        var audioSamples = GenerateTestAudio(duration: 1.0f);

        // Act & Assert - Multiple calls should all succeed
        for (int i = 0; i < 5; i++)
        {
            var transcript = await adapter.TranscribeAsync(audioSamples);
            transcript.Should().NotBeNull($"Iteration {i} should succeed");
            transcript.Should().NotContain("RuntimeException", $"Iteration {i} should not crash");
        }
    }

    /// <summary>
    /// Generate test audio samples (mono, 16kHz).
    /// </summary>
    private float[] GenerateTestAudio(float duration)
    {
        const int sampleRate = 16000;
        int sampleCount = (int)(duration * sampleRate);
        var samples = new float[sampleCount];
        var random = new Random(42); // Fixed seed

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float signal = 0.3f * MathF.Sin(2 * MathF.PI * 200 * t)   // F1
                         + 0.2f * MathF.Sin(2 * MathF.PI * 800 * t)   // F2
                         + 0.1f * MathF.Sin(2 * MathF.PI * 2400 * t)  // F3
                         + 0.05f * (float)(random.NextDouble() - 0.5); // Noise

            samples[i] = Math.Clamp(signal, -1.0f, 1.0f);
        }

        return samples;
    }
}
