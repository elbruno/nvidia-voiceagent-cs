# NVIDIA Voice Agent (C#)

A real-time voice agent built with **ASP.NET Core 10** that performs Speech-to-Text (ASR), LLM processing, and Text-to-Speech (TTS) using NVIDIA NIM models via ONNX Runtime.

**Ported from:** [nvidia-transcribe/scenario5](https://github.com/elbruno/nvidia-transcribe/tree/main/scenario5)

[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Build](https://img.shields.io/badge/build-passing-brightgreen.svg)](https://github.com/elbruno/nvidia-voiceagent-cs)
[![Tests](https://img.shields.io/badge/tests-69%20passed-brightgreen.svg)](https://github.com/elbruno/nvidia-voiceagent-cs)

## Features

- **Real-time Speech Recognition** — NVIDIA Parakeet-TDT-0.6B-V2 via ONNX Runtime
- **LLM Integration** — Phi-3-mini-4k / TinyLlama with 4-bit quantization
- **Text-to-Speech** — FastPitch + HiFiGAN voice synthesis
- **WebSocket Streaming** — Bi-directional audio via `/ws/voice` and `/ws/logs`
- **Browser UI** — Models panel with download status, progress bars, and disk paths
- **Auto Model Download** — Fetches ONNX models from HuggingFace on first run
- **Mock Mode** — Full development workflow without downloading models
- **GPU Acceleration** — CUDA with automatic CPU fallback

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- (Optional) NVIDIA GPU with CUDA 11.8+ for GPU acceleration

## Quick Start

```bash
# Clone and build
git clone https://github.com/elbruno/nvidia-voiceagent-cs.git
cd nvidia-voiceagent-cs
dotnet build

# Run the app
cd NvidiaVoiceAgent
dotnet run
```

Open **<http://localhost:5003>** in your browser. The app auto-downloads the ASR model on first run (~1.2 GB).

> **No GPU?** The app works on CPU and falls back to Mock Mode if models are unavailable.

## Run Tests

```bash
dotnet test
```

69 tests across 3 test projects (Web, Core, ModelHub) — all passing.

## Configuration

Edit `NvidiaVoiceAgent/appsettings.json`:

```jsonc
{
  "ModelHub": {
    "AutoDownload": true,          // Download models on startup
    "UseInt8Quantization": true,   // Prefer quantized models
    "ModelCachePath": "model-cache" // Local cache directory
  },
  "ModelConfig": {
    "UseGpu": true,                // CUDA acceleration
    "Use4BitQuantization": true    // LLM quantization
  }
}
```

## Project Structure

```
nvidia-voiceagent-cs/
├── NvidiaVoiceAgent/              # ASP.NET Core Web App (UI, WebSockets, endpoints)
├── NvidiaVoiceAgent.Core/         # ML/Audio class library (ASR, AudioProcessor, MelSpectrogram)
├── NvidiaVoiceAgent.ModelHub/     # Model download library (HuggingFace integration)
├── tests/                         # xUnit test projects
└── docs/                          # Detailed documentation
```

## Documentation

| Document | Description |
|----------|-------------|
| [Architecture](docs/architecture/overview.md) | Solution structure, project layers, dependency graph |
| [Implementation Details](docs/guides/implementation-details.md) | Voice pipeline, audio processing, ONNX inference, model loading |
| [API Reference](docs/api/endpoints.md) | HTTP and WebSocket endpoints with message formats |
| [Developer Guide](docs/guides/developer-guide.md) | Coding conventions, adding services, testing patterns |
| [Troubleshooting](docs/guides/troubleshooting.md) | Common issues and solutions |

## Contributing

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Commit changes and open a Pull Request

See the [Developer Guide](docs/guides/developer-guide.md) for coding standards.

## License

MIT License — see [LICENSE](LICENSE) for details.

## Credits

- **Original**: [nvidia-transcribe](https://github.com/elbruno/nvidia-transcribe) (Python)
- **Models**: NVIDIA Parakeet, FastPitch, HiFiGAN
- **Runtime**: [ONNX Runtime](https://onnxruntime.ai/), [ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/)
