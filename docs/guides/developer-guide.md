# Developer Guide

Coding conventions, project structure rules, and patterns for contributing to the NVIDIA Voice Agent.

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An IDE: Visual Studio 2022 17.10+, VS Code with C# Dev Kit, or JetBrains Rider
- Git

## Building and Running

```bash
# Build everything
dotnet build

# Run the web app
cd NvidiaVoiceAgent
dotnet run
# → http://localhost:5003

# Run all tests
dotnet test
```

## Model Preparation

For a standardized, repeatable model setup (including ONNX patches and vocab generation),
see the guide: `docs/guides/model-preparation.md`.

## Coding Conventions

### One Class Per File

Each C# class, interface, enum, or record **must** be in its own file. The filename must match the type name exactly.

```csharp
Services/
├── AsrService.cs          → class AsrService
├── IAsrService.cs         → interface IAsrService
├── AudioProcessor.cs      → class AudioProcessor
└── IAudioProcessor.cs     → interface IAudioProcessor
```

**Exception:** Private nested classes may remain in their parent class file.

### Naming Standards

| Element | Convention | Example |
| --- | --- | --- |
| Interfaces | `I{Name}` | `IAsrService`, `IAudioProcessor` |
| Implementations | `{Name}` | `AsrService`, `AudioProcessor` |
| WebSocket handlers | `{Name}WebSocketHandler` | `VoiceWebSocketHandler` |
| DTOs / Models | Descriptive | `VoiceResponse`, `HealthStatus` |
| Private fields | `_camelCase` | `_logger`, `_isModelLoaded` |
| Public properties | `PascalCase` | `IsModelLoaded`, `Status` |
| Async methods | `{Name}Async` | `TranscribeAsync`, `LoadModelAsync` |
| Test methods | `{Method}_{Scenario}_{Expected}` | `DecodeWav_WithValidWav_ExtractsSamples` |

### Nullable Reference Types

Always enabled. Be explicit:

```csharp
public string RequiredValue { get; set; } = string.Empty; // non-null
public string? OptionalValue { get; set; }                 // nullable
```

### Async / Cancellation

- Use `async Task` for I/O-bound methods
- Always accept and forward `CancellationToken`
- Never use `async void` (except event handlers)
- Avoid `.Result` or `.Wait()` — use `await`

```csharp
public async Task<string> TranscribeAsync(
    float[] audioSamples,
    CancellationToken cancellationToken = default)
```

### Logging

Use structured logging with `ILogger<T>`:

```csharp
_logger.LogInformation("ASR model loaded using {Provider}", provider);
_logger.LogDebug("Processing {SampleCount} samples", samples.Length);
_logger.LogWarning("No model found at {Path}", path);
_logger.LogError(ex, "Inference failed for {Duration}ms audio", duration);
```

Never log sensitive user data (audio content, transcripts in production).

### XML Documentation

Required for all public APIs:

```csharp
/// <summary>
/// Transcribes audio samples to text using the ONNX ASR model.
/// </summary>
/// <param name="audioSamples">Float32 audio at 16kHz mono</param>
/// <param name="cancellationToken">Cancellation token</param>
/// <returns>Transcribed text or error message</returns>
public async Task<string> TranscribeAsync(float[] audioSamples, CancellationToken cancellationToken = default)
```

### Disposal

Implement `IDisposable` for classes holding native resources:

```csharp
public void Dispose()
{
    if (_disposed) return;
    _session?.Dispose();
    _loadLock?.Dispose();
    _disposed = true;
    GC.SuppressFinalize(this);
}
```

## Project Structure Rules

### Where code lives

| Type of code | Project | Namespace |
| --- | --- | --- |
| ML inference services | `NvidiaVoiceAgent.Core` | `NvidiaVoiceAgent.Core.Services` |
| Audio processing | `NvidiaVoiceAgent.Core` | `NvidiaVoiceAgent.Core.Services` |
| Model configuration | `NvidiaVoiceAgent.Core` | `NvidiaVoiceAgent.Core.Models` |
| Service interfaces (ML) | `NvidiaVoiceAgent.Core` | `NvidiaVoiceAgent.Core.Services` |
| Model downloading | `NvidiaVoiceAgent.ModelHub` | `NvidiaVoiceAgent.ModelHub` |
| WebSocket handlers | `NvidiaVoiceAgent` (web) | `NvidiaVoiceAgent.Hubs` |
| Web-specific services | `NvidiaVoiceAgent` (web) | `NvidiaVoiceAgent.Services` |
| WebSocket DTOs | `NvidiaVoiceAgent` (web) | `NvidiaVoiceAgent.Models` |
| DI wiring / endpoints | `NvidiaVoiceAgent` (web) | `Program.cs` (top-level) |

### Adding a new service

1. **Define the interface** in the appropriate project:
   - ML/Audio → `NvidiaVoiceAgent.Core/Services/IMyService.cs`
   - Web-specific → `NvidiaVoiceAgent/Services/IMyService.cs`

2. **Implement the class** in its own file:
   - `NvidiaVoiceAgent.Core/Services/MyService.cs`

3. **Register in DI:**
   - Core services → add to `Core/ServiceCollectionExtensions.cs` `AddVoiceAgentCore()`
   - Web services → add to `Program.cs`

4. **Write tests** in the corresponding test project:
   - Core → `tests/NvidiaVoiceAgent.Core.Tests/`
   - Web → `tests/NvidiaVoiceAgent.Tests/`

### Adding a new class library

Follow the existing pattern:

1. Create project: `dotnet new classlib -n NvidiaVoiceAgent.NewLib -f net10.0`
2. Enable nullable: `<Nullable>enable</Nullable>` in csproj
3. Create `ServiceCollectionExtensions.cs` for DI registration
4. Add to solution: edit `NvidiaVoiceAgent.slnx`
5. Create test project: `tests/NvidiaVoiceAgent.NewLib.Tests/`
6. Keep dependencies minimal — no ASP.NET packages in class libraries

## Audio Chunking for Long-Form ASR

The Parakeet-TDT ASR model has a maximum input length of ~60 seconds. For longer audio, the system automatically uses an **overlapping chunk** strategy.

### How It Works

When audio exceeds the model's limit:

1. **Chunking** - Audio is split into 50-second chunks with 2-second overlap
2. **Inference** - Each chunk is transcribed independently
3. **Merging** - Transcripts are combined with overlap detection to remove duplicates

```plaintext
Input audio (100s)
     ↓
  Chunks: [0-50s] → "hello world"
          [48-98s] → "world this is a test"  (2s overlap)
          [96-100s] → "a test"               (remainder)
     ↓
Transcripts merged: "hello world this is a test"
```

### Configuration

Chunking is configured per model in `model_spec.json`:

```json
{
  "chunking": {
    "enabled": true,
    "chunk_size_seconds": 50,
    "overlap_seconds": 2,
    "strategy": "overlapping"
  }
}
```

### Using It

No code changes required — chunking is **automatic**:

```csharp
// Short audio (< 60s) → single pass
var transcript = await asrService.TranscribeAsync(shortAudio);

// Long audio (> 60s) → automatically chunks and merges
var transcript = await asrService.TranscribeAsync(longAudio);  
// Returns merged transcript across multiple chunks
```

### Performance Tips

- **Per-chunk time**: ~150ms (CPU) to seconds (GPU) depending on hardware
- **30 minutes audio**: ~100-300 seconds total (including overlap overhead)
- **Memory**: ~80-100MB per chunk (typically stable across many chunks)

### Troubleshooting

| Issue | Cause | Solution |
| --- | --- | --- |
| Chunking disabled | Model spec has `"enabled": false` | Update model_spec.json |
| Duplicate words | Overlap detection failed | Verify overlap > 2s, check logs |
| Audio too long error | Chunking disabled, audio > 60s | Enable chunking in model spec |

### Testing Long-Form Audio

See `tests/NvidiaVoiceAgent.Core.Tests/Integration/LongFormAudioChunkingTests.cs` for:

- 30-minute audio scenarios
- Synthetic audio generation (speech, silence, noise, multi-speaker)
- Edge case handling

Run tests:

```bash
dotnet test --filter "LongFormAudioChunkingTests"
```

### Performance Benchmarking

See `tests/NvidiaVoiceAgent.Core.Tests/Performance/AudioChunkingPerformanceTests.cs` for:

- Throughput measurements (seconds/second)
- Memory usage tracking
- Consistency testing across multiple runs

Run benchmarks:

```bash
dotnet test --filter "AudioChunkingPerformanceTests" -- --logger "console;verbosity=detailed"
```

## Testing Patterns

### Test Framework

- **xUnit** for test runner
- **FluentAssertions** for readable assertions
- **WebApplicationFactory** for integration tests

### Test Structure

```csharp
[Fact]
public async Task TranscribeAsync_WithValidAudio_ReturnsTranscript()
{
    // Arrange
    var service = CreateService();
    var samples = GenerateTestAudio(duration: 1.0f);

    // Act
    var result = await service.TranscribeAsync(samples);

    // Assert
    result.Should().NotBeNullOrEmpty();
}
```

### Unit Tests

Test services in isolation. Use test doubles (inline fakes or mocks) for dependencies:

```csharp
private class TestAudioProcessor : IAudioProcessor
{
    public float[] DecodeWav(byte[] wavData) { /* test impl */ }
    public byte[] EncodeWav(float[] samples, int sampleRate) { /* test impl */ }
    public float[] Resample(float[] samples, int source, int target) { /* test impl */ }
}
```

### Integration Tests

Use `WebApplicationFactory<Program>` via the shared `WebApplicationFactoryFixture`:

```csharp
public class MyTests : IClassFixture<WebApplicationFactoryFixture>
{
    private readonly WebApplicationFactoryFixture _factory;

    public MyTests(WebApplicationFactoryFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Endpoint_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

### Running Specific Tests

```bash
# By class name
dotnet test --filter FullyQualifiedName~AudioProcessorTests

# By project
dotnet test tests/NvidiaVoiceAgent.Core.Tests

# By test name
dotnet test --filter "DecodeWav_WithValidWav_ExtractsSamples"
```

### Test Configuration (Real Models)

Core tests use configuration-based model loading that mirrors the main app. Settings live in `tests/NvidiaVoiceAgent.Core.Tests/appsettings.Test.json` and are copied to the test output directory.

Key points:

- **Real-model tests are optional** — they skip when models are not downloaded.
- **Paths are relative to the solution root** (same as the app).
- **CPU default** for tests (`UseGpu: false`) to keep runs stable.

For details, see `tests/NvidiaVoiceAgent.Core.Tests/README.md`.

## Mock Mode Development

You can develop the full UI and WebSocket pipeline without downloading ONNX models. The app automatically enters Mock Mode when models aren't found:

- **ASR** returns simulated transcripts based on audio duration
- **TTS** returns silent WAV files
- **LLM** echoes back the input with a wrapper response
- WebSocket connections, audio encoding/decoding, and UI all work normally

This is the default experience on first run if you set `"AutoDownload": false`.

## Documentation

### File locations

```plaintext
docs/
├── plans/           # Design proposals (plan_YYMMDD_HHMM.md)
├── architecture/    # Architecture overview and diagrams
├── api/             # API endpoint reference
└── guides/          # Developer guides, implementation details, troubleshooting
```

### Plan file naming

Plans use the format `plan_YYMMDD_HHMM.md` (e.g., `plan_260212_0930.md` for Feb 12, 2026 at 9:30 AM).

## Environment Configuration

### Development (default)

```bash
cd NvidiaVoiceAgent
dotnet run
# Uses appsettings.Development.json overrides
# Swagger UI available at /swagger
```

### User Secrets (Development)

Use User Secrets for per-developer values like `ModelHub:HuggingFaceToken` and `ModelHub:ModelCachePath`:

```bash
cd NvidiaVoiceAgent
dotnet user-secrets init
dotnet user-secrets set "ModelHub:HuggingFaceToken" "hf_your_actual_token_here"
dotnet user-secrets set "ModelHub:ModelCachePath" "models-cache"
dotnet user-secrets list
```

### Production

```bash
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS="http://0.0.0.0:8080"
dotnet run --configuration Release
```

### Environment Variable Overrides

Configuration can be overridden with environment variables using `__` as separator:

```bash
# Override model path
export ModelConfig__AsrModelPath="/opt/models/parakeet"

# Override ModelHub settings
export ModelHub__AutoDownload=false
export ModelHub__ModelCachePath="/opt/model-cache"
export ModelHub__HuggingFaceToken="hf_your_token_here"

# Override logging
export Logging__LogLevel__Default=Debug
```

**Windows (PowerShell):**

```powershell
$env:ModelHub__ModelCachePath="E:\\models-cache"
$env:ModelHub__HuggingFaceToken="hf_your_token_here"
```
