# NVIDIA Voice Agent (C#)

A real-time voice agent built with ASP.NET Core 8 that performs Speech-to-Text (ASR), LLM processing, and Text-to-Speech (TTS) using NVIDIA NIM models via ONNX Runtime.

**Ported from:** [nvidia-transcribe/scenario5](https://github.com/elbruno/nvidia-transcribe/tree/main/scenario5)

## Features

- 🎤 **Real-time Speech Recognition** - Parakeet-TDT-0.6B-V2 ASR model
- 🤖 **LLM Integration** - Phi-3-mini-4k for conversational responses
- 🔊 **Text-to-Speech** - FastPitch + HiFiGAN for natural voice synthesis
- 🌐 **WebSocket API** - Real-time bi-directional audio streaming
- 📱 **Browser UI** - Ready-to-use web interface

## Architecture

```
┌─────────────┐     WebSocket      ┌──────────────────────────────────────┐
│  Browser    │◄──────────────────►│  ASP.NET Core 8 Server               │
│  (UI)       │   /ws/voice        │                                      │
└─────────────┘                    │  ┌─────┐   ┌─────┐   ┌─────┐        │
                                   │  │ ASR │──►│ LLM │──►│ TTS │        │
                                   │  └─────┘   └─────┘   └─────┘        │
                                   │  ONNX Runtime (GPU/CPU)              │
                                   └──────────────────────────────────────┘
```

## Quick Start

### Prerequisites

- .NET 8.0 SDK
- (Optional) NVIDIA GPU with CUDA 11.8+ for GPU acceleration

### Run

```bash
cd src/NvidiaVoiceAgent
dotnet run
```

Open http://localhost:5000 in your browser.

### Run Tests

```bash
dotnet test
```

## Configuration

Edit `appsettings.json`:

```json
{
  "ModelConfig": {
    "AsrModelPath": "models/parakeet-tdt-0.6b-v2.onnx",
    "TtsModelPath": "models/fastpitch.onnx",
    "VocoderModelPath": "models/hifigan.onnx",
    "LlmModelPath": "models/phi-3-mini-4k-instruct.onnx"
  }
}
```

## API Endpoints

| Endpoint | Protocol | Description |
|----------|----------|-------------|
| `/ws/voice` | WebSocket | Voice processing pipeline |
| `/ws/logs` | WebSocket | Real-time log streaming |
| `/health` | HTTP GET | Health check |

## WebSocket Protocol

### Voice Endpoint (`/ws/voice`)

**Send:** Binary WAV audio (16kHz, mono, 16-bit PCM)

**Receive:**
```json
{ "type": "transcription", "text": "Hello world" }
{ "type": "response", "text": "Hi there!" }
{ "type": "audio", "data": "<base64 WAV>" }
```

## Project Structure

```
├── src/NvidiaVoiceAgent/
│   ├── Hubs/                  # WebSocket handlers
│   │   ├── VoiceWebSocketHandler.cs
│   │   └── LogsWebSocketHandler.cs
│   ├── Services/              # Core services
│   │   ├── AsrService.cs      # Speech recognition
│   │   ├── TtsService.cs      # Text-to-speech
│   │   ├── LlmService.cs      # Language model
│   │   └── AudioProcessor.cs  # Audio utilities
│   ├── Models/                # DTOs
│   └── wwwroot/               # Browser UI
└── tests/NvidiaVoiceAgent.Tests/
```

## Model Requirements

| Model | Size | VRAM |
|-------|------|------|
| Parakeet-TDT-0.6B-V2 | ~1.2GB | 2-4GB |
| FastPitch + HiFiGAN | ~250MB | ~1GB |
| Phi-3-mini-4k (int4) | ~2GB | ~4GB |

**Total recommended:** 6-8GB VRAM for full pipeline

## Development

The project runs in **mock mode** when model files are not present, allowing development without downloading large models.

### Adding Models

1. Download ONNX models from NVIDIA NIM or Hugging Face
2. Place in `models/` directory (or configure paths in appsettings.json)
3. Restart the server

## License

MIT

## Credits

- Original Python implementation: [nvidia-transcribe](https://github.com/elbruno/nvidia-transcribe)
- NVIDIA NIM models
- ONNX Runtime
