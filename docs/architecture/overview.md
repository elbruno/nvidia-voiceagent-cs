# Architecture Overview

This document describes the solution architecture, project layers, and dependency graph for the NVIDIA Voice Agent C# application.

## Solution Structure

The solution consists of three class libraries and three test projects:

```
NvidiaVoiceAgent.slnx
├── NvidiaVoiceAgent              (ASP.NET Core Web App)
├── NvidiaVoiceAgent.Core         (ML/Audio class library)
├── NvidiaVoiceAgent.ModelHub     (Model download class library)
├── tests/NvidiaVoiceAgent.Tests
├── tests/NvidiaVoiceAgent.Core.Tests
└── tests/NvidiaVoiceAgent.ModelHub.Tests
```

## Project Dependency Graph

```
┌──────────────────────────────┐
│      NvidiaVoiceAgent        │   ASP.NET Core Web App
│   (WebSockets, UI, Endpoints)│   SDK: Microsoft.NET.Sdk.Web
│                              │
│   Packages:                  │
│   - Swashbuckle.AspNetCore   │
│   - NAudio                   │
│   - Serilog.AspNetCore       │
└──────┬───────────┬───────────┘
       │           │
       ▼           ▼
┌──────────────┐  ┌────────────────────────┐
│   Core       │  │   ModelHub              │  Class Libraries
│  (ML/Audio)  │  │ (HuggingFace Download)  │  SDK: Microsoft.NET.Sdk
│              │  │                          │
│ Packages:    │  │ Packages:               │
│ - OnnxRuntime│──► - HuggingfaceHub         │
│ - M.E.Log   │  │ - M.E.Log.Abstractions  │
│ - M.E.Options│  │ - M.E.Options           │
│ - M.E.DI    │  │ - M.E.DI.Abstractions   │
└──────────────┘  └────────────────────────┘
```

Arrows represent `ProjectReference` dependencies. The web app references both Core and ModelHub. Core also references ModelHub (for `IModelDownloadService` and `ModelType`).

## Layer Responsibilities

### NvidiaVoiceAgent (Web App)

The ASP.NET Core host that wires everything together. It owns:

| Component | Location | Purpose |
|-----------|----------|---------|
| `Program.cs` | Root | DI registration, middleware, endpoint mapping |
| `VoiceWebSocketHandler` | `Hubs/` | Orchestrates the ASR → LLM → TTS pipeline over WebSocket |
| `LogsWebSocketHandler` | `Hubs/` | Streams real-time logs to browser clients |
| `LogBroadcaster` | `Services/` | Manages multiple WebSocket log clients |
| `WebProgressReporter` | `Services/` | Bridges model download progress to WebSocket broadcast |
| DTOs | `Models/` | WebSocket message types (`VoiceResponse`, `HealthStatus`, etc.) |
| `wwwroot/index.html` | Static files | Browser UI with models panel and voice interface |

This layer has **no ML or audio processing logic** — it delegates to Core.

### NvidiaVoiceAgent.Core (ML/Audio Library)

Framework-agnostic class library containing all ML inference and audio processing:

| Component | Location | Purpose |
|-----------|----------|---------|
| `AsrService` | `Services/` | ONNX Runtime speech-to-text with lazy loading & mock mode |
| `AudioProcessor` | `Services/` | WAV decoding/encoding, PCM resampling |
| `MelSpectrogramExtractor` | `Services/` | FFT, mel filterbank, spectrogram normalization |
| `ModelConfig` | `Models/` | Configuration POCO for model paths and GPU settings |
| `IAsrService`, `IAudioProcessor`, `ILlmService`, `ITtsService` | `Services/` | Service interfaces |
| `ServiceCollectionExtensions` | Root | `AddVoiceAgentCore()` DI helper |

**Design constraints:**

- No ASP.NET dependencies (uses only `Microsoft.Extensions.*` abstractions)
- Can be consumed by any .NET host (console app, MAUI, tests, etc.)
- ONNX Runtime packages live here, not in the web app

### NvidiaVoiceAgent.ModelHub (Model Download Library)

Manages ONNX model discovery and download from HuggingFace:

| Component | Location | Purpose |
|-----------|----------|---------|
| `ModelDownloadService` | Root | Downloads models from HuggingFace, checks cache |
| `ModelRegistry` | Root | Defines available models (repo IDs, filenames, sizes) |
| `IProgressReporter` | Root | Abstraction for reporting download progress |
| `ConsoleProgressReporter` | Root | Default console-based progress output |
| `ModelHubOptions` | Root | Configuration for auto-download, cache path, tokens |

**Design constraints:**

- No ASP.NET or ONNX Runtime dependencies
- Can be tested entirely offline with mocked HTTP
- `IProgressReporter` allows the web app to substitute `WebProgressReporter`

## High-Level Request Flow

```
Browser                    Web App                    Core                ModelHub
  │                          │                          │                    │
  │── WAV audio (binary) ──► │                          │                    │
  │                          │── DecodeWav() ──────────►│                    │
  │                          │◄── float[] samples ──────│                    │
  │                          │── TranscribeAsync() ────►│                    │
  │                          │                          │── Extract mel ──►  │
  │                          │                          │── ONNX inference ►│
  │                          │                          │── CTC decode ────►│
  │                          │◄── transcript ───────────│                    │
  │◄── TranscriptResponse ──│                          │                    │
  │                          │── LLM (future) ────────►│                    │
  │                          │── TTS (future) ────────►│                    │
  │◄── VoiceResponse ───────│                          │                    │
```

## Dependency Injection Registration

Services are registered in `Program.cs` using extension methods from each library:

```csharp
// Core ML/Audio services (ASR, AudioProcessor)
builder.Services.AddVoiceAgentCore();

// ModelHub (download service, registry, progress reporting)
builder.Services.AddModelHub(options => { ... });

// Web-specific services (log broadcast, WebSocket handlers)
builder.Services.AddSingleton<ILogBroadcaster, LogBroadcaster>();
builder.Services.AddSingleton<IProgressReporter, WebProgressReporter>();
builder.Services.AddSingleton<VoiceWebSocketHandler>();
builder.Services.AddSingleton<LogsWebSocketHandler>();
```

## Key Design Patterns

### Lazy Model Loading

`AsrService` uses a semaphore-guarded double-check pattern to load the ONNX model on first use:

```
First TranscribeAsync() call
    → _loadLock.WaitAsync()
    → FindModelFile() (checks config path, ModelHub cache, recursive search)
    → new InferenceSession(path, sessionOptions)  // GPU → CPU fallback
    → _isModelLoaded = true
    → _loadLock.Release()
Subsequent calls skip loading
```

### Mock Mode / Graceful Degradation

Every service checks model availability at init time. If no model is found:

- Sets `_isMockMode = true`
- Returns simulated responses (e.g., mock transcripts based on audio duration)
- Allows full UI and WebSocket development without models

### Model Availability Guard

Models in the registry have an `IsAvailableForDownload` flag. Placeholder models (TTS, Vocoder, LLM) are marked `IsAvailableForDownload = false` because their HuggingFace repos don't exist yet. The UI shows "Coming Soon" badges for these models instead of download buttons, and the `POST /api/models/{name}/download` endpoint returns `400 Bad Request` if the model isn't available for download.

### Progress Reporting Bridge

ModelHub defines `IProgressReporter`. The web app overrides it with `WebProgressReporter` which:

1. Receives download progress callbacks from `ModelDownloadService`
2. Formats them as log messages
3. Broadcasts via `ILogBroadcaster` to all connected WebSocket `/ws/logs` clients
4. The browser UI parses these messages to update progress bars in the Models panel

## Technology Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Framework | .NET | 10.0 (preview) |
| Web | ASP.NET Core | 10.0 |
| ML Inference | ONNX Runtime (CPU + CUDA GPU) | 1.20.1 |
| Audio | NAudio | 2.2.1 |
| Logging | Serilog | 8.0.3 |
| API Docs | Swashbuckle (Swagger) | 7.2.0 |
| Model Download | HuggingfaceHub | 0.1.3 |
| Testing | xUnit + FluentAssertions | 2.9.3 / 8.8.0 |
| Solution Format | .slnx (XML) | — |
