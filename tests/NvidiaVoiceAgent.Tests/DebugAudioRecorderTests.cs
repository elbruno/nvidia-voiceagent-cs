using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NvidiaVoiceAgent.Models;
using NvidiaVoiceAgent.Services;

namespace NvidiaVoiceAgent.Tests;

/// <summary>
/// Tests for IDebugAudioRecorder service.
/// </summary>
public class DebugAudioRecorderTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly ILogger<DebugAudioRecorder> _logger;

    public DebugAudioRecorderTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "debug-audio-tests-" + Guid.NewGuid().ToString("N"));
        _logger = new LoggerFactory().CreateLogger<DebugAudioRecorder>();
    }

    public void Dispose()
    {
        // Cleanup test directory
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public void IsEnabled_ReturnsFalse_WhenDebugModeDisabled()
    {
        // Arrange
        var config = CreateConfig(enabled: false);
        var recorder = new DebugAudioRecorder(_logger, config);

        // Act & Assert
        recorder.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_ReturnsTrue_WhenDebugModeEnabled()
    {
        // Arrange
        var config = CreateConfig(enabled: true);
        var recorder = new DebugAudioRecorder(_logger, config);

        // Act & Assert
        recorder.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task StartSessionAsync_CreatesSessionDirectory()
    {
        // Arrange
        var config = CreateConfig(enabled: true);
        var recorder = new DebugAudioRecorder(_logger, config);
        var sessionId = "test-session-1";

        // Act
        var sessionDir = await recorder.StartSessionAsync(sessionId, smartMode: true, "phi3", realtimeMode: false);

        // Assert
        sessionDir.Should().NotBeNullOrEmpty();
        Directory.Exists(sessionDir).Should().BeTrue();
        sessionDir.Should().Contain(sessionId);
    }

    [Fact]
    public async Task StartSessionAsync_DoesNotCreateDirectory_WhenDisabled()
    {
        // Arrange
        var config = CreateConfig(enabled: false);
        var recorder = new DebugAudioRecorder(_logger, config);
        var sessionId = "test-session-2";

        // Act
        var sessionDir = await recorder.StartSessionAsync(sessionId, smartMode: false, null, realtimeMode: false);

        // Assert
        sessionDir.Should().BeEmpty();
        Directory.Exists(Path.Combine(_testDirectory, sessionId)).Should().BeFalse();
    }

    [Fact]
    public async Task SaveIncomingAudioAsync_SavesWavFile()
    {
        // Arrange
        var config = CreateConfig(enabled: true);
        var recorder = new DebugAudioRecorder(_logger, config);
        var sessionId = "test-session-3";
        await recorder.StartSessionAsync(sessionId, smartMode: false, null, realtimeMode: false);

        var audioData = CreateMockWavData();

        // Act
        var filePath = await recorder.SaveIncomingAudioAsync(sessionId, turnNumber: 1, audioData);

        // Assert
        filePath.Should().NotBeNullOrEmpty();
        File.Exists(filePath).Should().BeTrue();
        filePath.Should().Contain("turn_001_user.wav");

        var savedData = await File.ReadAllBytesAsync(filePath);
        savedData.Should().Equal(audioData);
    }

    [Fact]
    public async Task SaveOutgoingAudioAsync_SavesWavFile()
    {
        // Arrange
        var config = CreateConfig(enabled: true);
        var recorder = new DebugAudioRecorder(_logger, config);
        var sessionId = "test-session-4";
        await recorder.StartSessionAsync(sessionId, smartMode: false, null, realtimeMode: false);

        var audioData = CreateMockWavData();

        // Act
        var filePath = await recorder.SaveOutgoingAudioAsync(sessionId, turnNumber: 1, audioData);

        // Assert
        filePath.Should().NotBeNullOrEmpty();
        File.Exists(filePath).Should().BeTrue();
        filePath.Should().Contain("turn_001_assistant.wav");

        var savedData = await File.ReadAllBytesAsync(filePath);
        savedData.Should().Equal(audioData);
    }

    [Fact]
    public async Task RecordTurnAsync_SavesMetadata()
    {
        // Arrange
        var config = CreateConfig(enabled: true);
        var recorder = new DebugAudioRecorder(_logger, config);
        var sessionId = "test-session-5";
        await recorder.StartSessionAsync(sessionId, smartMode: true, "phi3", realtimeMode: false);

        // Act
        await recorder.RecordTurnAsync(
            sessionId,
            turnNumber: 1,
            userTranscript: "Hello, how are you?",
            assistantResponse: "I'm doing well, thank you!",
            userAudioFile: null,
            assistantAudioFile: null);

        // Assert
        var metadataFile = Path.Combine(_testDirectory, sessionId, "session_metadata.json");
        File.Exists(metadataFile).Should().BeTrue();

        var json = await File.ReadAllTextAsync(metadataFile);
        var metadata = JsonSerializer.Deserialize<SessionMetadata>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        metadata.Should().NotBeNull();
        metadata!.SessionId.Should().Be(sessionId);
        metadata.Turns.Should().HaveCount(1);
        metadata.Turns[0].TurnNumber.Should().Be(1);
        metadata.Turns[0].UserTranscript.Should().Be("Hello, how are you?");
        metadata.Turns[0].AssistantResponse.Should().Be("I'm doing well, thank you!");
    }

    [Fact]
    public async Task RecordTurnAsync_AccumulatesMultipleTurns()
    {
        // Arrange
        var config = CreateConfig(enabled: true);
        var recorder = new DebugAudioRecorder(_logger, config);
        var sessionId = "test-session-6";
        await recorder.StartSessionAsync(sessionId, smartMode: true, "phi3", realtimeMode: false);

        // Act
        await recorder.RecordTurnAsync(sessionId, 1, "Turn 1 user", "Turn 1 assistant");
        await recorder.RecordTurnAsync(sessionId, 2, "Turn 2 user", "Turn 2 assistant");
        await recorder.RecordTurnAsync(sessionId, 3, "Turn 3 user", "Turn 3 assistant");

        // Assert
        var metadataFile = Path.Combine(_testDirectory, sessionId, "session_metadata.json");
        var json = await File.ReadAllTextAsync(metadataFile);
        var metadata = JsonSerializer.Deserialize<SessionMetadata>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        metadata.Should().NotBeNull();
        metadata!.Turns.Should().HaveCount(3);
        metadata.Turns[0].TurnNumber.Should().Be(1);
        metadata.Turns[1].TurnNumber.Should().Be(2);
        metadata.Turns[2].TurnNumber.Should().Be(3);
    }

    [Fact]
    public async Task EndSessionAsync_SetsEndTime()
    {
        // Arrange
        var config = CreateConfig(enabled: true);
        var recorder = new DebugAudioRecorder(_logger, config);
        var sessionId = "test-session-7";
        await recorder.StartSessionAsync(sessionId, smartMode: false, null, realtimeMode: false);
        await recorder.RecordTurnAsync(sessionId, 1, "User text", "Assistant text");

        // Act
        await recorder.EndSessionAsync(sessionId);

        // Assert
        var metadataFile = Path.Combine(_testDirectory, sessionId, "session_metadata.json");
        var json = await File.ReadAllTextAsync(metadataFile);
        var metadata = JsonSerializer.Deserialize<SessionMetadata>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        metadata.Should().NotBeNull();
        metadata!.EndTime.Should().NotBeNull();
        metadata.EndTime.Should().BeAfter(metadata.StartTime);
    }

    [Fact]
    public async Task CleanupOldFilesAsync_DeletesOldSessions()
    {
        // Arrange
        var config = CreateConfig(enabled: true, maxAgeInDays: 1); // 1 day threshold
        var recorder = new DebugAudioRecorder(_logger, config);

        var oldSessionId = "old-session";
        var sessionDir = Path.Combine(_testDirectory, oldSessionId);
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "test.txt"), "test");

        // Try to make the directory appear old (2 days ago)
        try
        {
            Directory.SetCreationTimeUtc(sessionDir, DateTime.UtcNow.AddDays(-2));
            Directory.SetLastWriteTimeUtc(sessionDir, DateTime.UtcNow.AddDays(-2));
        }
        catch
        {
            // Skip test if we can't set directory times (some file systems don't support this)
            return;
        }

        // Verify the time was actually set
        var dirInfo = new DirectoryInfo(sessionDir);
        if (dirInfo.CreationTimeUtc >= DateTime.UtcNow.AddDays(-1))
        {
            // Skip test if setting the time didn't work
            return;
        }

        // Act
        await recorder.CleanupOldFilesAsync();

        // Assert
        Directory.Exists(sessionDir).Should().BeFalse();
    }

    [Fact]
    public async Task CleanupOldFilesAsync_PreservesRecentSessions()
    {
        // Arrange
        var config = CreateConfig(enabled: true, maxAgeInDays: 7);
        var recorder = new DebugAudioRecorder(_logger, config);

        var recentSessionId = "recent-session";
        var sessionDir = Path.Combine(_testDirectory, recentSessionId);
        Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "test.txt"), "test");

        // Act
        await recorder.CleanupOldFilesAsync();

        // Assert
        Directory.Exists(sessionDir).Should().BeTrue();
    }

    [Fact]
    public async Task SaveIncomingAudioAsync_DoesNotSave_WhenSaveIncomingAudioDisabled()
    {
        // Arrange
        var config = CreateConfig(enabled: true, saveIncomingAudio: false);
        var recorder = new DebugAudioRecorder(_logger, config);
        var sessionId = "test-session-8";
        await recorder.StartSessionAsync(sessionId, smartMode: false, null, realtimeMode: false);

        var audioData = CreateMockWavData();

        // Act
        var filePath = await recorder.SaveIncomingAudioAsync(sessionId, turnNumber: 1, audioData);

        // Assert
        filePath.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveOutgoingAudioAsync_DoesNotSave_WhenSaveOutgoingAudioDisabled()
    {
        // Arrange
        var config = CreateConfig(enabled: true, saveOutgoingAudio: false);
        var recorder = new DebugAudioRecorder(_logger, config);
        var sessionId = "test-session-9";
        await recorder.StartSessionAsync(sessionId, smartMode: false, null, realtimeMode: false);

        var audioData = CreateMockWavData();

        // Act
        var filePath = await recorder.SaveOutgoingAudioAsync(sessionId, turnNumber: 1, audioData);

        // Assert
        filePath.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordTurnAsync_DoesNotSave_WhenSaveMetadataDisabled()
    {
        // Arrange
        var config = CreateConfig(enabled: true, saveMetadata: false);
        var recorder = new DebugAudioRecorder(_logger, config);
        var sessionId = "test-session-10";
        await recorder.StartSessionAsync(sessionId, smartMode: false, null, realtimeMode: false);

        // Act
        await recorder.RecordTurnAsync(sessionId, 1, "User", "Assistant");

        // Assert
        var metadataFile = Path.Combine(_testDirectory, sessionId, "session_metadata.json");
        File.Exists(metadataFile).Should().BeFalse();
    }

    private IOptions<DebugModeConfig> CreateConfig(
        bool enabled = true,
        bool saveIncomingAudio = true,
        bool saveOutgoingAudio = true,
        bool saveMetadata = true,
        int maxAgeInDays = 7)
    {
        var config = new DebugModeConfig
        {
            Enabled = enabled,
            AudioLogPath = _testDirectory,
            SaveIncomingAudio = saveIncomingAudio,
            SaveOutgoingAudio = saveOutgoingAudio,
            SaveMetadata = saveMetadata,
            MaxAgeInDays = maxAgeInDays
        };

        return Options.Create(config);
    }

    private static byte[] CreateMockWavData()
    {
        // Create a minimal valid WAV file with 0.1 seconds of silence at 16000Hz
        const int sampleRate = 16000;
        const int durationMs = 100;
        const int numSamples = sampleRate * durationMs / 1000;
        const int bytesPerSample = 2; // 16-bit audio
        const int dataSize = numSamples * bytesPerSample;

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // RIFF header
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize); // File size - 8
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // Chunk size
        writer.Write((short)1); // Audio format (PCM)
        writer.Write((short)1); // Number of channels
        writer.Write(sampleRate); // Sample rate
        writer.Write(sampleRate * bytesPerSample); // Byte rate
        writer.Write((short)bytesPerSample); // Block align
        writer.Write((short)16); // Bits per sample

        // data chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        // Write silence
        for (int i = 0; i < numSamples; i++)
        {
            writer.Write((short)0);
        }

        return ms.ToArray();
    }
}
