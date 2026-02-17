# Debug Mode for Voice Conversation Recording

## Overview

The NVIDIA Voice Agent includes a **debug mode** feature that records voice conversations to disk for later analysis and end-to-end testing. When enabled, the system automatically saves:

- Incoming audio files (user voice recordings)
- Outgoing audio files (TTS responses)
- Conversation metadata (transcripts, timestamps, configuration)

This feature is particularly useful for:
- Creating test datasets for model evaluation
- Debugging voice processing issues
- Analyzing conversation flows
- Building end-to-end test scenarios

## Configuration

Debug mode is configured in `appsettings.json` or `appsettings.Development.json`:

```json
{
  "DebugMode": {
    "Enabled": false,
    "AudioLogPath": "logs/audio-debug",
    "SaveIncomingAudio": true,
    "SaveOutgoingAudio": true,
    "SaveMetadata": true,
    "MaxAgeInDays": 7
  }
}
```

### Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Enabled` | boolean | `false` | Master switch to enable/disable debug mode |
| `AudioLogPath` | string | `"logs/audio-debug"` | Directory where recordings will be saved |
| `SaveIncomingAudio` | boolean | `true` | Save user voice recordings as WAV files |
| `SaveOutgoingAudio` | boolean | `true` | Save TTS response audio as WAV files |
| `SaveMetadata` | boolean | `true` | Save conversation metadata as JSON |
| `MaxAgeInDays` | integer | `7` | Auto-delete recordings older than N days (0 = keep forever) |

## File Structure

When debug mode is enabled, each conversation session creates a dedicated directory:

```
logs/audio-debug/
├── {session-id-1}/
│   ├── turn_001_user.wav         # User audio from turn 1
│   ├── turn_001_assistant.wav    # Assistant audio from turn 1
│   ├── turn_002_user.wav         # User audio from turn 2
│   ├── turn_002_assistant.wav    # Assistant audio from turn 2
│   └── session_metadata.json     # Conversation metadata
├── {session-id-2}/
│   ├── turn_001_user.wav
│   ├── turn_001_assistant.wav
│   └── session_metadata.json
└── ...
```

### Audio File Naming Convention

- **User audio**: `turn_{NNN}_user.wav` where NNN is the zero-padded turn number (001, 002, etc.)
- **Assistant audio**: `turn_{NNN}_assistant.wav`

All audio files are in WAV format:
- **Incoming audio**: 16kHz, mono, 16-bit PCM (ASR input format)
- **Outgoing audio**: 22050Hz, mono, 16-bit PCM (TTS output format)

## Session Metadata

Each session directory contains a `session_metadata.json` file with the following structure:

```json
{
  "sessionId": "a1b2c3d4e5f6g7h8",
  "startTime": "2024-02-17T14:30:00.000Z",
  "endTime": "2024-02-17T14:35:00.000Z",
  "configuration": {
    "smartMode": true,
    "smartModel": "phi3",
    "realtimeMode": false
  },
  "turns": [
    {
      "turnNumber": 1,
      "timestamp": "2024-02-17T14:30:15.000Z",
      "userTranscript": "Hello, how are you?",
      "assistantResponse": "I'm doing well, thank you! How can I help you today?",
      "userAudioFile": "/path/to/logs/audio-debug/a1b2c3d4e5f6g7h8/turn_001_user.wav",
      "assistantAudioFile": "/path/to/logs/audio-debug/a1b2c3d4e5f6g7h8/turn_001_assistant.wav",
      "userAudioDuration": 2.5,
      "assistantAudioDuration": 3.2
    },
    {
      "turnNumber": 2,
      "timestamp": "2024-02-17T14:30:30.000Z",
      "userTranscript": "What's the weather like today?",
      "assistantResponse": "I don't have access to real-time weather data...",
      "userAudioFile": "/path/to/logs/audio-debug/a1b2c3d4e5f6g7h8/turn_002_user.wav",
      "assistantAudioFile": "/path/to/logs/audio-debug/a1b2c3d4e5f6g7h8/turn_002_assistant.wav",
      "userAudioDuration": 1.8,
      "assistantAudioDuration": 4.1
    }
  ]
}
```

### Metadata Fields

**Session Level:**
- `sessionId`: Unique identifier for the session
- `startTime`: ISO 8601 timestamp when session started
- `endTime`: ISO 8601 timestamp when session ended (null if still active)
- `configuration`: Snapshot of session settings

**Turn Level:**
- `turnNumber`: Sequential turn number (1-indexed)
- `timestamp`: ISO 8601 timestamp for this turn
- `userTranscript`: ASR-transcribed text from user
- `assistantResponse`: Text response from assistant (LLM or echo)
- `userAudioFile`: Absolute path to user audio file
- `assistantAudioFile`: Absolute path to assistant audio file
- `userAudioDuration`: Duration in seconds (extracted from WAV header)
- `assistantAudioDuration`: Duration in seconds (extracted from WAV header)

## Usage Examples

### Enable Debug Mode for Development

1. Create or edit `appsettings.Development.json`:

```json
{
  "DebugMode": {
    "Enabled": true,
    "AudioLogPath": "logs/audio-debug",
    "MaxAgeInDays": 1
  }
}
```

2. Run the application:

```bash
cd NvidiaVoiceAgent
dotnet run
```

3. Use the web interface to have voice conversations

4. Check the `logs/audio-debug/` directory for recorded sessions

### Enable Debug Mode via Environment Variables

For containerized deployments or CI/CD:

```bash
export DebugMode__Enabled=true
export DebugMode__AudioLogPath=/var/log/voice-agent/audio-debug
export DebugMode__MaxAgeInDays=3
dotnet run
```

### Selective Recording

To save only incoming audio (user voice) without TTS responses:

```json
{
  "DebugMode": {
    "Enabled": true,
    "SaveIncomingAudio": true,
    "SaveOutgoingAudio": false,
    "SaveMetadata": true
  }
}
```

## Automatic Cleanup

The debug audio recorder automatically cleans up old recordings based on the `MaxAgeInDays` setting:

- Runs during session cleanup
- Deletes entire session directories older than the threshold
- Uses directory creation time to determine age
- Set to `0` to disable automatic cleanup

To manually trigger cleanup or implement a scheduled cleanup job:

```csharp
// Get the service from DI
var debugRecorder = serviceProvider.GetRequiredService<IDebugAudioRecorder>();

// Run cleanup
await debugRecorder.CleanupOldFilesAsync();
```

## Security and Privacy Considerations

⚠️ **Important**: Debug mode records user voice conversations, which may contain sensitive or personal information.

**Best Practices:**

1. **Disable in Production**: Only enable debug mode in development/testing environments
2. **Data Retention**: Use appropriate `MaxAgeInDays` values to avoid accumulating data
3. **Access Control**: Restrict file system permissions on the `AudioLogPath` directory
4. **GDPR Compliance**: If recording user data, ensure compliance with privacy regulations
5. **Informed Consent**: Inform users when their conversations are being recorded

**Recommended Configuration for Production:**

```json
{
  "DebugMode": {
    "Enabled": false  // Always disabled in production
  }
}
```

## Troubleshooting

### No Audio Files Created

Check the following:
1. Verify `DebugMode.Enabled` is `true` in your appsettings
2. Ensure the `AudioLogPath` directory is writable
3. Check logs for errors related to DebugAudioRecorder
4. Verify `SaveIncomingAudio` and `SaveOutgoingAudio` are `true`

### Disk Space Issues

If debug recordings are consuming too much space:
1. Reduce `MaxAgeInDays` to delete old recordings sooner
2. Manually run cleanup: `await debugRecorder.CleanupOldFilesAsync()`
3. Disable `SaveOutgoingAudio` if only user audio is needed
4. Monitor the `AudioLogPath` directory size

### Missing Metadata

If `session_metadata.json` is not being created:
1. Verify `SaveMetadata` is `true`
2. Check that the session completes normally (not interrupted)
3. Look for exceptions in the logs

## Related Documentation

- [End-to-End Testing Guide](e2e-testing-with-recorded-audio.md) - Using recorded audio for automated tests
- [Architecture Decision Records](../architecture/) - Design decisions for debug mode
- [API Documentation](../api/) - IDebugAudioRecorder interface reference
