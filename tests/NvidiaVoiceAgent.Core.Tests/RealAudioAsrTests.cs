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
    /// CRITICAL TEST: Proves the length parameter is the raw audio sample count,
    /// not the mel frame count. This test does NOT require the ONNX model.
    /// It mathematically reproduces the exact error scenario from session
    /// ad22728e35f94a23970bbd84141feebb where passing mel frames (312) caused
    /// dimension mismatch "39 by 77" in self-attention.
    /// </summary>
    [Fact]
    public void LengthParameter_WithRealAudio_IsSampleCountNotFrameCount()
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

        var adapter = new ParakeetTdtAdapter(_adapterLogger);

        // Act: calculate length parameter the way the fixed code does
        long lengthValue = adapter.CalculateLengthParameter(
            audioSampleCount: samples.Length, melFrameCount: melFrames);

        // Assert: length MUST be the raw sample count, NOT mel frames
        lengthValue.Should().Be(samples.Length,
            "Length parameter must be raw audio sample count");
        lengthValue.Should().BeGreaterThan(paddedFrames * 10,
            "Length in samples should be ~100x larger than padded mel frames");

        // The old buggy value was paddedFrames (312) — verify we're NOT using that
        lengthValue.Should().NotBe(paddedFrames,
            $"Must NOT pass padded mel frames ({paddedFrames}) — this caused '39 by 77' error");
        lengthValue.Should().NotBe(melFrames,
            $"Must NOT pass raw mel frames ({melFrames}) — this would also cause dimension errors");
    }

    /// <summary>
    /// Verify the fallback path (when audioSampleCount is null) still produces
    /// a value much larger than mel frames — estimated from frames * hopLength.
    /// </summary>
    [Fact]
    public void LengthParameter_FallbackEstimate_IsReasonable()
    {
        // Arrange
        var wavPath = Path.Combine(TestDataDir, "hey_can_you_help_me.wav");
        var wavBytes = File.ReadAllBytes(wavPath);
        var processor = new AudioProcessor(
            _config.CreateLogger<AudioProcessor>());
        var samples = processor.DecodeWav(wavBytes);

        var melExtractor = new MelSpectrogramExtractor(nMels: 128);
        var melSpec = melExtractor.Extract(samples);
        int melFrames = melSpec.GetLength(0);
        int paddedFrames = ((melFrames + 7) / 8) * 8;

        var adapter = new ParakeetTdtAdapter(_adapterLogger);

        // Act: fallback when audioSampleCount is null
        long fallbackLength = adapter.CalculateLengthParameter(
            audioSampleCount: null, melFrameCount: melFrames);

        // Assert: fallback estimates samples from melFrames * hopLength (160)
        fallbackLength.Should().Be((long)melFrames * 160,
            "Fallback should estimate sample count as melFrames * hopLength");
        fallbackLength.Should().BeGreaterThan(paddedFrames * 10,
            "Fallback should still be much larger than padded mel frames");
        fallbackLength.Should().BeCloseTo(samples.Length, (uint)(samples.Length * 0.05),
            "Fallback estimate should be within ~5% of actual sample count");
    }

    /// <summary>
    /// Verify that for various audio durations, the length parameter
    /// is always the sample count, never the mel frame count.
    /// </summary>
    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    [InlineData(2.0f)]
    [InlineData(3.072f)]  // Exact duration of the failing audio
    [InlineData(5.0f)]
    [InlineData(10.0f)]
    public void LengthParameter_ForVariousDurations_AlwaysSampleCount(float durationSeconds)
    {
        // Arrange
        const int sampleRate = 16000;
        int sampleCount = (int)(durationSeconds * sampleRate);
        var samples = new float[sampleCount]; // Content doesn't matter for this test

        var melExtractor = new MelSpectrogramExtractor(nMels: 128);
        var melSpec = melExtractor.Extract(samples);
        int melFrames = melSpec.GetLength(0);
        int paddedFrames = ((melFrames + 7) / 8) * 8;

        var adapter = new ParakeetTdtAdapter(_adapterLogger);

        // Act
        long lengthValue = adapter.CalculateLengthParameter(sampleCount, melFrames);

        // Assert
        lengthValue.Should().Be(sampleCount,
            $"For {durationSeconds}s audio: must pass sample count ({sampleCount}), not mel frames ({melFrames}) or padded ({paddedFrames})");
    }
}
