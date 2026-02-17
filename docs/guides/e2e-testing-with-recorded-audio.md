# Using Recorded Audio for End-to-End Testing

## Purpose

This guide shows how to leverage debug mode recordings to build automated end-to-end tests for the NVIDIA Voice Agent voice processing pipeline.

## Workflow

### Step 1: Record Test Sessions

1. Enable debug mode in configuration
2. Use the web app to have conversations covering test scenarios
3. Recorded audio and metadata are saved to `logs/audio-debug/`

### Step 2: Create Test Project

Add a new test project for E2E tests (if not already exists):

```bash
dotnet new xunit -n NvidiaVoiceAgent.E2ETests -o tests/NvidiaVoiceAgent.E2ETests
cd tests/NvidiaVoiceAgent.E2ETests
dotnet add reference ../../NvidiaVoiceAgent/NvidiaVoiceAgent.csproj
```

### Step 3: Copy Test Data

Organize recorded sessions into test data:

```
tests/
└── E2ETestData/
    ├── simple-greeting/
    │   ├── turn_001_user.wav
    │   ├── turn_001_assistant.wav
    │   └── session_metadata.json
    └── complex-conversation/
        ├── turn_001_user.wav
        ├── turn_002_user.wav
        └── session_metadata.json
```

## Example Test Cases

### Test 1: Validate ASR Transcription

Goal: Ensure ASR correctly transcribes the recorded audio

```csharp
[Fact]
public async Task AsrService_TranscribesRecordedAudio_MatchesExpected()
{
    // Load recorded audio
    var audioFile = "tests/E2ETestData/simple-greeting/turn_001_user.wav";
    var audioBytes = await File.ReadAllBytesAsync(audioFile);
    
    // Load expected transcript from metadata
    var metadataJson = await File.ReadAllTextAsync(
        "tests/E2ETestData/simple-greeting/session_metadata.json");
    var metadata = JsonSerializer.Deserialize<SessionMetadata>(metadataJson);
    var expectedTranscript = metadata.Turns[0].UserTranscript;
    
    // Transcribe audio
    var actualTranscript = await TranscribeAudioAsync(audioBytes);
    
    // Compare (allowing minor variations)
    Assert.True(
        TranscriptsAreSimilar(expectedTranscript, actualTranscript),
        $"Expected: '{expectedTranscript}', Got: '{actualTranscript}'");
}
```

### Test 2: Full Pipeline Processing Time

Goal: Measure end-to-end latency

```csharp
[Fact]
public async Task FullPipeline_ProcessesAudio_WithinTimeLimit()
{
    var audioFile = "tests/E2ETestData/simple-greeting/turn_001_user.wav";
    var audioBytes = await File.ReadAllBytesAsync(audioFile);
    
    var sw = Stopwatch.StartNew();
    
    // Process through full pipeline
    var transcript = await _asrService.TranscribeAsync(audioBytes);
    var response = await _llmService.GenerateResponseAsync(transcript);
    var ttsAudio = await _ttsService.SynthesizeAsync(response);
    
    sw.Stop();
    
    // Should complete within reasonable time
    Assert.True(sw.ElapsedMilliseconds < 5000, 
        $"Pipeline took {sw.ElapsedMilliseconds}ms, expected < 5000ms");
}
```

### Test 3: Multi-Turn Conversation

Goal: Validate conversation context is maintained

```csharp
[Fact]
public async Task MultiTurnConversation_MaintainsContext()
{
    var sessionPath = "tests/E2ETestData/complex-conversation";
    var metadata = LoadSessionMetadata(sessionPath);
    
    var chatHistory = new List<ChatMessage>();
    
    foreach (var turn in metadata.Turns)
    {
        // Process user audio
        var audioBytes = await File.ReadAllBytesAsync(turn.UserAudioFile);
        var transcript = await TranscribeAudioAsync(audioBytes);
        
        // Generate response with context
        chatHistory.Add(new ChatMessage { Role = "user", Content = transcript });
        var response = await _llmService.GenerateResponseAsync(
            transcript, 
            chatHistory);
        chatHistory.Add(new ChatMessage { Role = "assistant", Content = response });
        
        // Verify response is relevant (not exact match due to LLM variance)
        Assert.False(string.IsNullOrWhiteSpace(response));
    }
    
    // Verify we processed all turns
    Assert.Equal(metadata.Turns.Count * 2, chatHistory.Count);
}
```

## Utility Functions

### Loading Metadata

```csharp
private SessionMetadata LoadSessionMetadata(string sessionPath)
{
    var metadataPath = Path.Combine(sessionPath, "session_metadata.json");
    var json = File.ReadAllText(metadataPath);
    var options = new JsonSerializerOptions 
    { 
        PropertyNameCaseInsensitive = true 
    };
    return JsonSerializer.Deserialize<SessionMetadata>(json, options);
}
```

### Transcript Comparison

```csharp
private bool TranscriptsAreSimilar(string expected, string actual)
{
    // Normalize text
    expected = expected.ToLower().Trim();
    actual = actual.ToLower().Trim();
    
    // Exact match
    if (expected == actual) return true;
    
    // Check if key words match (simple heuristic)
    var expectedWords = expected.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var actualWords = actual.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    
    var matchingWords = expectedWords.Intersect(actualWords).Count();
    var matchRate = (double)matchingWords / expectedWords.Length;
    
    return matchRate >= 0.8; // 80% of words should match
}
```

## Running Tests

### Local Development

```bash
# Run all E2E tests
dotnet test --filter "Category=E2E"

# Run specific test
dotnet test --filter "FullyQualifiedName~AsrService_TranscribesRecordedAudio"
```

### CI/CD Pipeline

Add E2E tests to your CI workflow:

```yaml
# .github/workflows/tests.yml
- name: Run E2E Tests
  run: dotnet test --filter "Category=E2E" --logger "trx"
  
- name: Upload Test Results
  uses: actions/upload-artifact@v3
  with:
    name: test-results
    path: TestResults/
```

## Best Practices

1. **Curate Test Audio**: Select diverse voices, accents, and audio quality levels
2. **Document Expectations**: Clearly specify what each test validates
3. **Keep Tests Fast**: Use shorter audio clips for unit tests
4. **Version Test Data**: Track test audio with Git LFS or external storage
5. **Handle Non-Determinism**: LLM responses may vary; test for quality not exact matches

## Common Issues

### Audio Format Mismatch

Ensure test audio matches expected format:
- ASR input: 16kHz, mono, 16-bit PCM WAV
- Check with: `ffprobe audio.wav`
- Convert if needed: `ffmpeg -i input.wav -ar 16000 -ac 1 output.wav`

### Flaky Tests

If tests are inconsistent:
- Use deterministic models (disable sampling/temperature)
- Increase similarity thresholds
- Mock external dependencies
- Run on consistent hardware

### Missing Test Data

Verify test data location:
```csharp
[Fact]
public void TestData_Exists()
{
    Assert.True(Directory.Exists("tests/E2ETestData"), 
        "E2E test data directory should exist");
}
```

## Next Steps

- Review [Debug Mode Guide](debug-mode.md) for recording more test data
- See [Testing Strategy](../../tests/README.md) for the full test pyramid
- Explore [Model Evaluation](model-evaluation.md) for quality metrics
