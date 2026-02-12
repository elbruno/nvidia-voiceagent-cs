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

# Run all tests (69 tests across 3 projects)
dotnet test
```

## Coding Conventions

### One Class Per File

Each C# class, interface, enum, or record **must** be in its own file. The filename must match the type name exactly.

```
Services/
├── AsrService.cs          → class AsrService
├── IAsrService.cs         → interface IAsrService
├── AudioProcessor.cs      → class AudioProcessor
└── IAudioProcessor.cs     → interface IAudioProcessor
```

**Exception:** Private nested classes may remain in their parent class file.

### Naming Standards

| Element | Convention | Example |
|---------|-----------|---------|
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
|-------------|---------|-----------|
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

## Mock Mode Development

You can develop the full UI and WebSocket pipeline without downloading ONNX models. The app automatically enters Mock Mode when models aren't found:

- **ASR** returns simulated transcripts based on audio duration
- **TTS** returns silent WAV files
- **LLM** echoes back the input with a wrapper response
- WebSocket connections, audio encoding/decoding, and UI all work normally

This is the default experience on first run if you set `"AutoDownload": false`.

## Documentation

### File locations

```
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

# Override logging
export Logging__LogLevel__Default=Debug
```
