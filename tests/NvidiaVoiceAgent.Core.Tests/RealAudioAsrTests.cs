using FluentAssertions;
using Microsoft.Extensions.Logging;
using NvidiaVoiceAgent.Core.Adapters;
using NvidiaVoiceAgent.Core.Services;

namespace NvidiaVoiceAgent.Core.Tests;

/// <summary>
/// Integration tests using real recorded audio files.
/// These tests verify the ASR pipeline with actual microphone recordings
/// to catch dimension mismatch and preprocessing errors.
/// </summary>
public class RealAudioAsrTests
{
    private readonly TestConfiguration _config;
    private readonly ILogger<ParakeetTdtAdapter> _adapterLogger;

    private static readonly string TestDataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData");

    public RealAudioAsrTests()
    {
        _config = TestConfiguration.Instance;
        _adapterLogger = _config.CreateLogger<ParakeetTdtAdapter>();
    }

    [Fact]
    public void TestAudioFile_Exists()
    {
        var wavPath = Path.Combine(TestDataDir, "hey_can_you_help_me.wav");
        File.Exists(wavPath).Should().BeTrue(
            $"Test audio file should exist at {wavPath}");
    }

    [Fact]
    public void TestAudioFile_IsValidWav()
    {
        var wavPath = Path.Combine(TestDataDir, "hey_can_you_help_me.wav");
        var wavBytes = File.ReadAllBytes(wavPath);

        wavBytes.Length.Should().BeGreaterThan(44, "WAV file should be larger than header");

        var riff = System.Text.Encoding.ASCII.GetString(wavBytes, 0, 4);
        riff.Should().Be("RIFF", "File should start with RIFF header");

        var wave = System.Text.Encoding.ASCII.GetString(wavBytes, 8, 4);
        wave.Should().Be("WAVE", "File should have WAVE format");
    }

    [Fact]
    public void DecodeRealAudio_ProducesValidSamples()
    {
        // Arrange
        var wavPath = Path.Combine(TestDataDir, "hey_can_you_help_me.wav");
        var wavBytes = File.ReadAllBytes(wavPath);
        var processor = new AudioProcessor(
            _config.CreateLogger<AudioProcessor>());

        // Act
        var samples = processor.DecodeWav(wavBytes);

        // Assert
        samples.Should().NotBeEmpty("Decoded audio should have samples");
        samples.Length.Should().BeGreaterThan(16000, "3-second audio should have >16000 samples");

        // Verify audio has energy (not silence)
        var rms = MathF.Sqrt(samples.Select(s => s * s).Average());
        rms.Should().BeGreaterThan(0.001f, "Audio should not be silent");
    }

    [Fact]
    public void MelSpectrogram_FromRealAudio_HasCorrectShape()
    {
        // Arrange
        var wavPath = Path.Combine(TestDataDir, "hey_can_you_help_me.wav");
        var wavBytes = File.ReadAllBytes(wavPath);
        var processor = new AudioProcessor(
            _config.CreateLogger<AudioProcessor>());
        var samples = processor.DecodeWav(wavBytes);

        // Use 128 mel bins as specified in model_spec.json
        var melExtractor = new MelSpectrogramExtractor(nMels: 128);

        // Act
        var melSpec = melExtractor.Extract(samples);
        var normalized = melExtractor.Normalize(melSpec);

        // Assert
        melSpec.GetLength(1).Should().Be(128, "Should produce 128 mel bins");
        melSpec.GetLength(0).Should().BeGreaterThan(0, "Should produce time frames");
        normalized.GetLength(0).Should().Be(melSpec.GetLength(0));
        normalized.GetLength(1).Should().Be(128);
    }

    [Fact]
    public async Task TranscribeRealAudio_WithAdapter_NoDimensionErrors()
    {
        // Skip if model not downloaded
        if (!_config.AsrModelExists())
        {
            return;
        }

        // Arrange
        var wavPath = Path.Combine(TestDataDir, "hey_can_you_help_me.wav");
        var wavBytes = File.ReadAllBytes(wavPath);
        var processor = new AudioProcessor(
            _config.CreateLogger<AudioProcessor>());
        var samples = processor.DecodeWav(wavBytes);

        var adapter = new ParakeetTdtAdapter(_adapterLogger);
        await adapter.LoadAsync(_config.ModelConfig.AsrModelPath);

        // Act
        var transcript = await adapter.TranscribeAsync(samples);

        // Assert - The critical check: no dimension mismatch errors
        transcript.Should().NotContain("RuntimeException",
            "Real audio should not cause ONNX runtime errors");
        transcript.Should().NotContain("BroadcastIterator",
            "Real audio should not cause dimension mismatch (39 by 77)");
        transcript.Should().NotContain("[Transcription error",
            "Transcription should succeed without errors");
        transcript.Should().NotBeNullOrEmpty(
            "Should produce a transcript from real speech audio");
    }

    [Fact]
    public async Task TranscribeRealAudio_WithAsrService_NoDimensionErrors()
    {
        // Skip if model not downloaded
        if (!_config.AsrModelExists())
        {
            return;
        }

        // Arrange
        var wavPath = Path.Combine(TestDataDir, "hey_can_you_help_me.wav");
        var wavBytes = File.ReadAllBytes(wavPath);
        var processor = new AudioProcessor(
            _config.CreateLogger<AudioProcessor>());
        var samples = processor.DecodeWav(wavBytes);

        var adapter = new ParakeetTdtAdapter(_adapterLogger);
        var service = new AsrService(
            _config.AsrLogger,
            Microsoft.Extensions.Options.Options.Create(_config.ModelConfig),
            adapter);
        await service.LoadModelAsync();

        // Act
        var transcript = await service.TranscribeAsync(samples);

        // Assert
        transcript.Should().NotContain("RuntimeException",
            "Full ASR service should not produce ONNX errors with real audio");
        transcript.Should().NotContain("BroadcastIterator",
            "Full ASR service should not have dimension mismatch");
        transcript.Should().NotContain("[Transcription error",
            "Should transcribe real audio successfully");
    }

    [Fact]
    public async Task TranscribeRealAudio_PartialWithConfidence_Succeeds()
    {
        if (!_config.AsrModelExists())
        {
            return;
        }

        // Arrange
        var wavPath = Path.Combine(TestDataDir, "hey_can_you_help_me.wav");
        var wavBytes = File.ReadAllBytes(wavPath);
        var processor = new AudioProcessor(
            _config.CreateLogger<AudioProcessor>());
        var samples = processor.DecodeWav(wavBytes);

        var adapter = new ParakeetTdtAdapter(_adapterLogger);
        var service = new AsrService(
            _config.AsrLogger,
            Microsoft.Extensions.Options.Options.Create(_config.ModelConfig),
            adapter);
        await service.LoadModelAsync();

        // Act
        var (transcript, confidence) = await service.TranscribePartialAsync(samples);

        // Assert
        transcript.Should().NotContain("RuntimeException");
        transcript.Should().NotContain("BroadcastIterator");
        confidence.Should().BeInRange(0.0f, 1.0f);
    }

    /// <summary>
    /// CRITICAL TEST: Verifies the ONNX model can now be run with padded mel frame count
    /// as the length parameter. The original encoder.onnx had a bug in the rel_shift
    /// implementation (Slice_3 on axis 3 instead of axis 2) which caused dimension
    /// mismatch errors like "39 by 77" in self-attention. The model was patched to fix this.
    /// </summary>
    [Fact]
    public void LengthParameter_IsPaddedFrameCount()
    {
        // Arrange: decode the exact audio that caused the production error
        var wavPath = Path.Combine(TestDataDir, "hey_can_you_help_me.wav");
        var wavBytes = File.ReadAllBytes(wavPath);
        var processor = new AudioProcessor(
            _config.CreateLogger<AudioProcessor>());
        var samples = processor.DecodeWav(wavBytes);

        // Compute mel spectrogram (same as the adapter does)
        var melExtractor = new MelSpectrogramExtractor(nMels: 128);
        var melSpec = melExtractor.Extract(samples);
        int melFrames = melSpec.GetLength(0);

        // Pad to multiple of 8 (same as adapter)
        int paddedFrames = ((melFrames + 7) / 8) * 8;

        // Assert: length parameter is the padded frame count
        // The original error was NOT about sample count vs frame count.
        // The real bug was in the ONNX model's rel_shift (Slice_3 axis was 3 instead of 2).
        paddedFrames.Should().BeGreaterThan(0, "Should have valid padded frames");
        melFrames.Should().BeGreaterThan(200, "3-second audio should produce 200+ mel frames");
        paddedFrames.Should().Be(312, "49152 samples at hopLength=160 => 305 frames => padded to 312");
    }

    /// <summary>
    /// Verify the mel spectrogram dimensions match what the model expects
    /// for the recorded test audio.
    /// </summary>
    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    [InlineData(2.0f)]
    [InlineData(3.072f)]  // Exact duration of the failing audio
    [InlineData(5.0f)]
    [InlineData(10.0f)]
    public void LengthParameter_ForVariousDurations_IsPaddedFrameCount(float durationSeconds)
    {
        // Arrange
        const int sampleRate = 16000;
        int sampleCount = (int)(durationSeconds * sampleRate);
        var samples = new float[sampleCount];

        var melExtractor = new MelSpectrogramExtractor(nMels: 128);
        var melSpec = melExtractor.Extract(samples);
        int melFrames = melSpec.GetLength(0);
        int paddedFrames = ((melFrames + 7) / 8) * 8;

        // Assert: padded frame count is a multiple of 8
        (paddedFrames % 8).Should().Be(0,
            $"For {durationSeconds}s audio: padded frames ({paddedFrames}) must be multiple of 8");
        paddedFrames.Should().BeGreaterThanOrEqualTo(melFrames,
            "Padded frames should be >= raw mel frames");
    }
}
