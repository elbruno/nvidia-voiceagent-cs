# Architecture Overview - NVIDIA Voice Agent C#

## Solution Structure

The NVIDIA Voice Agent solution is organized into multiple projects following clean architecture principles:

```
nvidia-voiceagent-cs/
├── NvidiaVoiceAgent/              # Main ASP.NET Core web application
│   ├── Controllers/               # API controllers (NEW)
│   ├── Hubs/                      # WebSocket handlers
│   ├── Models/                    # DTOs and view models
│   ├── Services/                  # Web-specific services
│   └── wwwroot/                   # Static web assets (HTML UI)
│
├── NvidiaVoiceAgent.Core/         # Core AI services library
│   ├── Models/                    # Core domain models
│   └── Services/                  # ASR, LLM, Audio processing
│
├── NvidiaVoiceAgent.ModelHub/     # Model download & management library
│   └── *.cs                       # HuggingFace model downloading
│
└── tests/                         # Test projects
    ├── NvidiaVoiceAgent.Tests/
    ├── NvidiaVoiceAgent.Core.Tests/
    └── NvidiaVoiceAgent.ModelHub.Tests/
```

---

## Project Responsibilities

### 1. NvidiaVoiceAgent (Web Layer)

**Purpose**: ASP.NET Core web application that provides HTTP/WebSocket APIs and serves the browser UI.

**Key Components**:

- **Controllers/** (NEW)
  - `HealthController`: System health and status endpoints
  - `ModelsController`: Model management (list, download, delete)
  - Uses attribute routing: `[Route("api/[controller]")]`

- **Hubs/** - WebSocket Handlers
  - `VoiceWebSocketHandler`: Real-time voice processing pipeline
  - `LogsWebSocketHandler`: Real-time log streaming

- **Models/** - DTOs
  - `ModelStatusResponse`: Model metadata for API responses
  - `VoiceResponse`: Voice processing results
  - `HealthStatus`: System health information

- **Services/** - Web-specific
  - `LogBroadcaster`: Multi-client log distribution
  - `WebProgressReporter`: Download progress for WebSocket clients

- **wwwroot/** - Static Assets
  - `index.html`: Single-page browser UI with model management

**Dependencies**:
- `NvidiaVoiceAgent.Core` (AI services)
- `NvidiaVoiceAgent.ModelHub` (model management)
- ASP.NET Core 10, NAudio, Serilog

---

### 2. NvidiaVoiceAgent.Core (AI Services Layer)

**Purpose**: Core ML/AI service implementations. Framework-agnostic class library.

**Key Services**:

- **AsrService**: Speech-to-Text using ONNX Runtime
  - Parakeet-TDT-0.6B-V2 model
  - Lazy loading with thread-safe initialization
  - GPU/CPU auto-detection
  - Mock mode for development

- **PersonaPlexService**: Full-duplex speech-to-speech LLM
  - Implements both `IPersonaPlexService` and `ILlmService`
  - 18 voice personas
  - Requires HuggingFace authentication
  - Currently in mock mode (TorchSharp integration planned)

- **AudioProcessor**: Audio encoding/decoding
- **MelSpectrogramExtractor**: Audio feature extraction

**Design Patterns**:
- Lazy loading with `SemaphoreSlim` for thread safety
- Graceful degradation to mock mode
- Disposal pattern for ONNX sessions
- Dependency injection via `ServiceCollectionExtensions`

---

### 3. NvidiaVoiceAgent.ModelHub (Model Management Layer)

**Purpose**: Downloads and manages AI models from HuggingFace.

**Key Components**:

- **ModelDownloadService**: Downloads models via HuggingfaceHub NuGet
- **ModelRegistry**: Centralized model catalog
- **Progress Reporting**: `IProgressReporter` interface

---

## API Endpoints

### Health & Status

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Legacy health check |
| `/api/health` | GET | System health with model status |

### Model Management

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/models` | GET | List all registered models |
| `/api/models/{name}/download` | POST | Download specific model |
| `/api/models/{name}` | DELETE | Delete model files |

### WebSocket Endpoints

| Endpoint | Protocol | Description |
|----------|----------|-------------|
| `/ws/voice` | WebSocket | Voice processing pipeline |
| `/ws/logs` | WebSocket | Real-time log streaming |

---

## Key Architectural Improvements

### Controller-Based API (NEW)

**Before**: Inline endpoints in `Program.cs`
**After**: Dedicated controller classes

**Benefits**:
- Better testability
- Clearer separation of concerns
- Improved Swagger documentation
- Easier to add new endpoints

### Authentication Support for Gated Models

- `RequiresAuthentication` field in `ModelStatusResponse`
- UI shows warning for gated models (PersonaPlex)
- Comprehensive HF token setup guide

---

## Security

### HuggingFace Token Management

- Environment variables (recommended)
- Azure Key Vault (production)
- Never commit tokens to Git

See: [HuggingFace Token Setup Guide](../guides/huggingface-token-setup.md)

---

**Last Updated**: February 2026  
**Version**: 2.0
