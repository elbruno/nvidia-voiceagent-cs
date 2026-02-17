using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NvidiaVoiceAgent.Models;

namespace NvidiaVoiceAgent.Services;

/// <summary>
/// Service for recording audio conversations in debug mode.
/// Saves audio files and metadata for end-to-end testing.
/// </summary>
public class DebugAudioRecorder : IDebugAudioRecorder
{
    private readonly ILogger<DebugAudioRecorder> _logger;
    private readonly DebugModeConfig _config;
    private readonly ConcurrentDictionary<string, SessionMetadata> _sessions = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DebugAudioRecorder(ILogger<DebugAudioRecorder> logger, IOptions<DebugModeConfig> config)
    {
        _logger = logger;
        _config = config.Value;

        if (_config.Enabled)
        {
            _logger.LogInformation("Debug mode enabled. Audio will be saved to: {Path}", _config.AudioLogPath);
        }
    }

    public bool IsEnabled => _config.Enabled;

    public async Task<string> StartSessionAsync(string sessionId, bool smartMode, string? smartModel, bool realtimeMode)
    {
        if (!_config.Enabled)
            return string.Empty;

        var sessionDir = Path.Combine(_config.AudioLogPath, sessionId);
        Directory.CreateDirectory(sessionDir);

        var metadata = new SessionMetadata
        {
            SessionId = sessionId,
            StartTime = DateTime.UtcNow,
            Configuration = new SessionConfiguration
            {
                SmartMode = smartMode,
                SmartModel = smartModel,
                RealtimeMode = realtimeMode
            }
        };

        _sessions[sessionId] = metadata;

        _logger.LogInformation("Debug session started: {SessionId} at {Path}", sessionId, sessionDir);
        return sessionDir;
    }

    public async Task<string> SaveIncomingAudioAsync(string sessionId, int turnNumber, byte[] audioData)
    {
        if (!_config.Enabled || !_config.SaveIncomingAudio)
            return string.Empty;

        var fileName = $"turn_{turnNumber:D3}_user.wav";
        var sessionDir = Path.Combine(_config.AudioLogPath, sessionId);
        var filePath = Path.Combine(sessionDir, fileName);

        await _fileLock.WaitAsync();
        try
        {
            await File.WriteAllBytesAsync(filePath, audioData);
            _logger.LogDebug("Saved incoming audio: {Path} ({Size} bytes)", filePath, audioData.Length);
        }
        finally
        {
            _fileLock.Release();
        }

        return filePath;
    }

    public async Task<string> SaveOutgoingAudioAsync(string sessionId, int turnNumber, byte[] audioData)
    {
        if (!_config.Enabled || !_config.SaveOutgoingAudio)
            return string.Empty;

        var fileName = $"turn_{turnNumber:D3}_assistant.wav";
        var sessionDir = Path.Combine(_config.AudioLogPath, sessionId);
        var filePath = Path.Combine(sessionDir, fileName);

        await _fileLock.WaitAsync();
        try
        {
            await File.WriteAllBytesAsync(filePath, audioData);
            _logger.LogDebug("Saved outgoing audio: {Path} ({Size} bytes)", filePath, audioData.Length);
        }
        finally
        {
            _fileLock.Release();
        }

        return filePath;
    }

    public async Task RecordTurnAsync(
        string sessionId,
        int turnNumber,
        string userTranscript,
        string assistantResponse,
        string? userAudioFile = null,
        string? assistantAudioFile = null)
    {
        if (!_config.Enabled || !_config.SaveMetadata)
            return;

        if (!_sessions.TryGetValue(sessionId, out var metadata))
        {
            _logger.LogWarning("Session not found: {SessionId}", sessionId);
            return;
        }

        var turn = new ConversationTurn
        {
            TurnNumber = turnNumber,
            Timestamp = DateTime.UtcNow,
            UserTranscript = userTranscript,
            AssistantResponse = assistantResponse,
            UserAudioFile = userAudioFile,
            AssistantAudioFile = assistantAudioFile
        };

        // Calculate audio durations if files exist
        if (userAudioFile != null && File.Exists(userAudioFile))
        {
            turn.UserAudioDuration = await GetAudioDurationAsync(userAudioFile);
        }

        if (assistantAudioFile != null && File.Exists(assistantAudioFile))
        {
            turn.AssistantAudioDuration = await GetAudioDurationAsync(assistantAudioFile);
        }

        metadata.Turns.Add(turn);

        // Save metadata incrementally after each turn
        await SaveMetadataAsync(sessionId, metadata);

        _logger.LogDebug("Recorded turn {Turn} for session {SessionId}", turnNumber, sessionId);
    }

    public async Task EndSessionAsync(string sessionId)
    {
        if (!_config.Enabled)
            return;

        if (_sessions.TryRemove(sessionId, out var metadata))
        {
            metadata.EndTime = DateTime.UtcNow;
            await SaveMetadataAsync(sessionId, metadata);
            _logger.LogInformation("Debug session ended: {SessionId}", sessionId);
        }
    }

    public async Task CleanupOldFilesAsync()
    {
        if (!_config.Enabled || _config.MaxAgeInDays <= 0)
            return;

        var cutoffDate = DateTime.UtcNow.AddDays(-_config.MaxAgeInDays);
        var rootDir = _config.AudioLogPath;

        if (!Directory.Exists(rootDir))
            return;

        await _fileLock.WaitAsync();
        try
        {
            var sessionDirs = Directory.GetDirectories(rootDir);
            var deletedCount = 0;

            foreach (var sessionDir in sessionDirs)
            {
                var dirInfo = new DirectoryInfo(sessionDir);
                if (dirInfo.CreationTimeUtc < cutoffDate)
                {
                    Directory.Delete(sessionDir, recursive: true);
                    deletedCount++;
                    _logger.LogInformation("Deleted old debug session: {Path}", sessionDir);
                }
            }

            if (deletedCount > 0)
            {
                _logger.LogInformation("Cleaned up {Count} old debug sessions", deletedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up old debug files");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task SaveMetadataAsync(string sessionId, SessionMetadata metadata)
    {
        var sessionDir = Path.Combine(_config.AudioLogPath, sessionId);
        var metadataFile = Path.Combine(sessionDir, "session_metadata.json");

        await _fileLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(metadata, JsonOptions);
            await File.WriteAllTextAsync(metadataFile, json);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<double> GetAudioDurationAsync(string filePath)
    {
        try
        {
            // Read WAV header to get duration
            // WAV format: 44-byte header, then data
            // Sample rate at offset 24 (4 bytes)
            // Data size at offset 40 (4 bytes)
            await using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);

            // Skip to sample rate (offset 24)
            stream.Seek(24, SeekOrigin.Begin);
            var sampleRate = reader.ReadInt32();

            // Skip to data chunk size (offset 40)
            stream.Seek(40, SeekOrigin.Begin);
            var dataSize = reader.ReadInt32();

            // Duration = data size / (sample rate * channels * bytes per sample)
            // Assuming 16-bit mono: bytes per sample = 2, channels = 1
            var duration = dataSize / (double)(sampleRate * 2);

            return duration;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read audio duration from {Path}", filePath);
            return 0;
        }
    }
}
