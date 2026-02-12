# GitHub Copilot Instructions - NVIDIA Voice Agent C#

## Project Overview

This is a production-ready, real-time voice agent built with **ASP.NET Core 10** that performs Speech-to-Text (ASR), LLM processing, and Text-to-Speech (TTS) using NVIDIA NIM models via ONNX Runtime. The application uses WebSocket for bi-directional audio streaming and supports both GPU and CPU inference.

## Repository Documentation Guidelines

### Root-Level Files

Only the following files should exist at the repository root:

- `README.md` - Main project documentation
- `LICENSE` - Project license file
- Code files and solution files (`.slnx`, `.csproj`, `.cs`, etc.)
- Configuration files (`.gitignore`, `.gitattributes`, `.editorconfig`)

### Documentation Folder Structure

All additional documentation must be stored in the `docs/` folder:

```
docs/
├── plans/           # Project plans and proposals
├── architecture/    # Architecture decision records (ADRs)
├── api/            # API documentation
└── guides/         # Developer guides and tutorials
```

### Plan Files Naming Convention

All plan files must be saved in `docs/plans/` with the following naming format:

```
plan_YYMMDD_HHMM.md
```

Where:

- `YYMMDD` - Date the plan was created (e.g., `260212` for Feb 12, 2026)
- `HHMM` - Time the plan was created in 24-hour format (e.g., `0930` for 9:30 AM)

**Example:** `plan_260212_0930.md` for a plan created on February 12, 2026 at 9:30 AM

## Technology Stack

- **.NET 10.0** - Latest .NET framework
- **ASP.NET Core 10** - Web framework with WebSocket support
- **ONNX Runtime** - ML inference engine (GPU/CPU)
- **NAudio** - Audio processing library
- **Serilog** - Structured logging
- **xUnit** - Testing framework
- **FluentAssertions** - Test assertions

## Architecture Patterns

### 1. Service-Based Architecture

All business logic is encapsulated in services:

- **AsrService** - Speech-to-text using ONNX models
- **AudioProcessor** - WAV encoding/decoding and resampling
- **MelSpectrogramExtractor** - Audio feature extraction
- **LogBroadcaster** - Multi-client log distribution

Use dependency injection for all services:

```csharp
builder.Services.AddSingleton<IAsrService, AsrService>();
builder.Services.AddSingleton<IAudioProcessor, AudioProcessor>();
```

### 2. WebSocket Handlers

WebSocket communication is handled by specialized handlers in the `Hubs/` directory:

- **VoiceWebSocketHandler** - Voice processing pipeline (ASR → LLM → TTS)
- **LogsWebSocketHandler** - Real-time log streaming

Handlers should:

- Handle both binary and text messages
- Manage session state per connection
- Properly dispose resources on disconnect
- Use cancellation tokens for graceful shutdown

### 3. Lazy Loading with Thread Safety

Models are loaded lazily using semaphore-protected initialization:

```csharp
private readonly SemaphoreSlim _loadLock = new(1, 1);

public async Task LoadModelAsync(CancellationToken cancellationToken = default)
{
    if (_isModelLoaded) return;

    await _loadLock.WaitAsync(cancellationToken);
    try
    {
        if (_isModelLoaded) return;  // Double-check pattern
        // Load model...
        _isModelLoaded = true;
    }
    finally
    {
        _loadLock.Release();
    }
}
```

### 4. Graceful Degradation (Mock Mode)

Services should gracefully degrade when models are unavailable:

- Check for model files during initialization
- Set `_isMockMode = true` if models not found
- Provide simulated responses for development/testing
- Log warnings when running in mock mode

Example:

```csharp
if (modelPath == null)
{
    _logger.LogWarning("No ASR ONNX model found. Running in mock mode.");
    _isMockMode = true;
    return;
}
```

## Coding Conventions

### One Class Per File Rule

> **CRITICAL:** Each C# class, interface, enum, or record MUST be in its own file.

**File Naming:**

- File name must match the type name exactly
- `MyClass.cs` contains only `class MyClass`
- `IMyService.cs` contains only `interface IMyService`
- `MyEnum.cs` contains only `enum MyEnum`

**Exception:** Nested private classes may remain in parent class file.

**Rationale:**

- Improves code navigation and discoverability
- Enables better source control tracking
- Follows .NET community best practices
- Makes refactoring easier

**Example:**

```
Models/
├── ModelConfig.cs        # Contains only ModelConfig class
├── VoiceResponse.cs      # Contains only VoiceResponse class
├── VoiceMode.cs          # Contains only VoiceMode enum
└── IAsrService.cs        # Contains only IAsrService interface
```

### Naming Standards

- **Interfaces**: `I{Service}Service` (e.g., `IAsrService`, `ITtsService`)
- **Implementations**: `{Service}Service` (e.g., `AsrService`, `TtsService`)
- **WebSocket Handlers**: `{Name}WebSocketHandler` (e.g., `VoiceWebSocketHandler`)
- **Models/DTOs**: Descriptive names (e.g., `VoiceResponse`, `ConfigMessage`, `HealthStatus`)
- **Private Fields**: `_camelCase` with underscore prefix
- **Public Properties**: `PascalCase`
- **Methods**: `PascalCase` with async suffix for async methods (e.g., `RunAsrAsync`)

### File Organization

```
nvidia-voiceagent-cs/
├── NvidiaVoiceAgent/              # ASP.NET Core Web Application
│   ├── Hubs/                      # WebSocket handlers only
│   ├── Services/                  # Business logic, interfaces first
│   ├── Models/                    # DTOs, configuration models (one class per file)
│   ├── wwwroot/                   # Static files (HTML, CSS, JS)
│   ├── Program.cs                 # DI configuration and routing
│   └── appsettings.json
│
├── NvidiaVoiceAgent.ModelHub/     # Model download class library
│   ├── IModelDownloadService.cs   # Interfaces
│   ├── ModelDownloadService.cs    # Implementations
│   └── ...                        # One class per file
│
├── NvidiaVoiceAgent.Core/         # (Future) Core ML services library
│
├── tests/
│   ├── NvidiaVoiceAgent.Tests/
│   └── NvidiaVoiceAgent.ModelHub.Tests/
│
└── docs/
    └── plans/                     # Project plans (plan_YYMMDD_HHMM.md)
```

### Class Library Guidelines

When creating new class libraries:

1. **Minimal Dependencies** - Only include necessary packages
2. **No ASP.NET Dependencies** - Keep libraries framework-agnostic
3. **Interface-First** - Define interfaces before implementations
4. **DI Extensions** - Provide `ServiceCollectionExtensions.cs` for easy registration

### Code Style

1. **Nullable Reference Types**: Always enabled (`#nullable enable`)

   ```csharp
   public string? OptionalValue { get; set; }  // Explicitly nullable
   public string RequiredValue { get; set; } = string.Empty;  // Non-null
   ```

2. **Async/Await**: Use `async Task` for I/O-bound operations
   - Always pass `CancellationToken` parameters
   - Use `ConfigureAwait(false)` in libraries (not needed in ASP.NET Core)
   - Avoid `async void` except for event handlers

3. **Error Handling**:

   ```csharp
   try
   {
       return await Task.Run(() => RunInference(audioSamples), cancellationToken);
   }
   catch (Exception ex)
   {
       _logger.LogError(ex, "ASR inference failed");
       return "[Transcription error]";  // Graceful fallback
   }
   ```

4. **Disposal Pattern**:

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

5. **Logging**: Use structured logging with `ILogger<T>`
   ```csharp
   _logger.LogInformation("ASR model loaded successfully using {Provider}", provider);
   _logger.LogDebug("Processing {SampleCount} audio samples", samples.Length);
   _logger.LogWarning("No ONNX model found at {Path}", modelPath);
   _logger.LogError(ex, "Inference failed for {Duration}ms audio", duration);
   ```

### Documentation

1. **Public APIs**: Use XML documentation comments

   ```csharp
   /// <summary>
   /// Transcribes audio samples to text using ONNX-based ASR model.
   /// </summary>
   /// <param name="audioSamples">Float32 audio samples at 16kHz mono</param>
   /// <param name="cancellationToken">Cancellation token</param>
   /// <returns>Transcribed text or error message</returns>
   public async Task<string> TranscribeAsync(float[] audioSamples, CancellationToken cancellationToken = default)
   ```

2. **Complex Logic**: Add inline comments for algorithms
   - FFT implementation details
   - CTC decoding logic
   - Audio resampling calculations

3. **TODOs**: Mark incomplete features clearly
   ```csharp
   // TODO: Implement TTS service when FastPitch model is available
   // builder.Services.AddSingleton<ITtsService, TtsService>();
   ```

## Testing Guidelines

### Test Structure

- **Unit Tests**: Services in isolation with mocked dependencies
- **Integration Tests**: WebSocket handlers with TestServer
- **End-to-End**: Full pipeline from audio input to response

### Test Naming

```csharp
[Fact]
public async Task TranscribeAsync_WithValidAudio_ReturnsTranscript()
{
    // Arrange
    var service = CreateAsrService();
    var audioSamples = GenerateTestAudio(duration: 1.0f);

    // Act
    var result = await service.TranscribeAsync(audioSamples);

    // Assert
    result.Should().NotBeNullOrEmpty();
}
```

### Use FluentAssertions

```csharp
result.Should().NotBeNull();
result.Should().BeOfType<VoiceResponse>();
response.Transcript.Should().NotBeNullOrEmpty();
audioData.Should().HaveCountGreaterThan(0);
```

### Test Fixtures

Use `WebApplicationFactory<Program>` for integration tests:

```csharp
public class VoiceWebSocketTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public VoiceWebSocketTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }
}
```

## ONNX Model Integration

### Model Loading Pattern

1. Check configured path in `appsettings.json`
2. Try multiple common filenames:
   - `encoder.onnx` (Parakeet encoder)
   - `model.onnx` (generic)
   - `{model-name}.onnx`
3. Recursively search for `.onnx` files
4. Fall back to mock mode if not found

### GPU Acceleration

```csharp
SessionOptions sessionOptions;
try
{
    sessionOptions = new SessionOptions();
    sessionOptions.AppendExecutionProvider_CUDA(0);  // Device 0
    _logger.LogInformation("CUDA provider available");
}
catch
{
    _logger.LogWarning("CUDA not available, using CPU");
    sessionOptions = new SessionOptions();
}
```

### Memory Management

- Dispose `InferenceSession` on service disposal
- Use `GC.Collect()` after model loading (optional, for large models)
- Consider 4-bit quantization for LLM models

## WebSocket Communication

### Message Types

**Binary Messages**: Audio data (WAV format)

```csharp
if (result.MessageType == WebSocketMessageType.Binary)
{
    var audioData = messageBuffer.ToArray();
    await HandleBinaryMessageAsync(audioData, webSocket, cancellationToken);
}
```

**Text Messages**: Configuration, commands

```csharp
if (result.MessageType == WebSocketMessageType.Text)
{
    var messageText = Encoding.UTF8.GetString(messageBuffer.ToArray());
    await HandleTextMessageAsync(messageText, webSocket, sessionState, cancellationToken);
}
```

### Response Format

Use JSON serialization with `System.Text.Json`:

```csharp
var response = new VoiceResponse
{
    Type = "voice",
    Transcript = transcript,
    Response = llmResponse,
    AudioData = Convert.ToBase64String(audioWav)
};

var json = JsonSerializer.Serialize(response);
var bytes = Encoding.UTF8.GetBytes(json);
await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
```

## Audio Processing

### WAV Format

- **ASR Input**: 16kHz, mono, 16-bit PCM
- **TTS Output**: 22050Hz, mono, 16-bit PCM
- **Browser Compatibility**: Include RIFF/WAVE headers

### Resampling

Linear interpolation for sample rate conversion:

```csharp
public float[] Resample(float[] input, int inputRate, int outputRate)
{
    double ratio = (double)inputRate / outputRate;
    int outputLength = (int)(input.Length / ratio);
    var output = new float[outputLength];

    for (int i = 0; i < outputLength; i++)
    {
        double srcPos = i * ratio;
        int srcIndex = (int)srcPos;
        double fraction = srcPos - srcIndex;

        if (srcIndex + 1 < input.Length)
            output[i] = (float)(input[srcIndex] * (1 - fraction) + input[srcIndex + 1] * fraction);
        else
            output[i] = input[srcIndex];
    }

    return output;
}
```

### Mel-Spectrogram Extraction

Parakeet-TDT specifications:

- **80 mel bins**, 512-point FFT
- **25ms window**, 10ms hop
- **Hann window** function
- **Normalization**: Mean=-4.0, StdDev=4.0

## Performance Optimization

### Concurrency

- Use `SemaphoreSlim` for model loading (single load, multiple callers wait)
- `ConcurrentDictionary` for multi-client state (LogBroadcaster)
- Avoid locking in hot paths (inference loops)

### Memory

```csharp
// Reuse buffers where possible
private readonly MemoryStream _messageBuffer = new();

// Clear instead of recreate
_messageBuffer.SetLength(0);
```

### Async Best Practices

- Don't block async code with `.Result` or `.Wait()`
- Use `await` instead of `Task.Run` for I/O
- Pass `CancellationToken` through the call stack

## Common Pitfalls to Avoid

1. **Don't dispose WebSockets prematurely** - Let handlers manage lifecycle
2. **Don't mix sync and async** - Use async throughout the pipeline
3. **Don't ignore CancellationToken** - Check for cancellation in loops
4. **Don't forget to normalize audio** - Mel-spectrogram requires normalization
5. **Don't assume model availability** - Always check `IsModelLoaded` or use mock mode
6. **Don't log sensitive data** - Avoid logging user audio or transcripts in production

## Package Management

### NuGet Packages

When adding packages:

1. Verify .NET 10 compatibility
2. Use latest stable versions
3. Avoid redundant packages (e.g., System.Text.Json is built-in)

Current packages:

```xml
<PackageReference Include="Swashbuckle.AspNetCore" Version="7.2.0" />
<PackageReference Include="TorchSharp" Version="0.102.6" />
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.20.1" />
<PackageReference Include="Microsoft.ML.OnnxRuntime.Managed" Version="1.20.1" />
<PackageReference Include="Microsoft.ML.OnnxRuntime.Gpu" Version="1.20.1" />
<PackageReference Include="NAudio" Version="2.2.1" />
<PackageReference Include="Serilog.AspNetCore" Version="8.0.3" />
```

## AI Assistance Tips

When working with GitHub Copilot:

1. **Context**: Provide method signatures and XML docs for accurate completions
2. **Patterns**: Reference existing services (e.g., AsrService) when creating new ones
3. **Tests**: Write test names first, let Copilot generate test bodies
4. **Comments**: Write clear intent comments before complex code blocks
5. **Refactoring**: Select code blocks and ask Copilot Chat for optimization suggestions

## Resources

- [ASP.NET Core Docs](https://learn.microsoft.com/en-us/aspnet/core/)
- [ONNX Runtime C# API](https://onnxruntime.ai/docs/api/csharp/api/)
- [NAudio Documentation](https://github.com/naudio/NAudio/blob/master/Docs/README.md)
- [WebSocket Protocol RFC](https://datatracker.ietf.org/doc/html/rfc6455)
- [NVIDIA NIM Catalog](https://catalog.ngc.nvidia.com/)

---

**Remember:** This is a real-time audio processing application. Prioritize performance, memory efficiency, and graceful error handling. When in doubt, follow the patterns established in existing services like `AsrService` and `AudioProcessor`.
