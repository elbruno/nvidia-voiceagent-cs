# NVIDIA Voice Agent (C#)

A real-time voice agent built with **ASP.NET Core 10** that performs Speech-to-Text (ASR), LLM processing, and Text-to-Speech (TTS) using NVIDIA NIM models via ONNX Runtime and TorchSharp.

**Ported from:** [nvidia-transcribe/scenario5](https://github.com/elbruno/nvidia-transcribe/tree/main/scenario5)

[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Build](https://img.shields.io/badge/build-passing-brightgreen.svg)](https://github.com/elbruno/nvidia-voiceagent-cs)
[![Tests](https://img.shields.io/badge/tests-112%20passed-brightgreen.svg)](https://github.com/elbruno/nvidia-voiceagent-cs)

## Features

- **Real-time Speech Recognition** — NVIDIA Parakeet-TDT-0.6B-V2 via ONNX Runtime
- **PersonaPlex LLM** — NVIDIA PersonaPlex-7B-v1 full-duplex speech-to-speech AI (NEW ✨)
- **Alternative LLMs** — Phi-3-mini-4k / TinyLlama with 4-bit quantization
- **Text-to-Speech** — FastPitch + HiFiGAN voice synthesis
- **WebSocket Streaming** — Bi-directional audio via `/ws/voice` and `/ws/logs`
- **Browser UI** — Models panel with download status, progress bars, and disk paths
- **Auto Model Download** — Fetches models from HuggingFace on first run
- **Mock Mode** — Full development workflow without downloading models
- **GPU Acceleration** — CUDA with automatic CPU fallback
- **Voice Personas** — 18 pre-packaged voices with PersonaPlex
- **Debug Mode** — Record conversations to disk for testing and analysis (NEW ✨)
- **Long-Form Audio Support** — Automatic chunking for audio longer than 60 seconds (NEW ✨)

## Long-Form Audio Support

The ASR model now supports arbitrarily long audio through intelligent **overlapping chunk processing**:

- ✅ **Automatic chunking** for audio > 60 seconds
- ✅ **Intelligent overlap** (2s) minimizes word-split artifacts  
- ✅ **Transparent merging** preserves sentence boundaries & punctuation
- ✅ **Zero code changes** — works transparently

**Example:** Transcribe a 30-minute podcast in one call:

```csharp
// Just pass any audio length — chunking happens automatically
var transcript = await asrService.TranscribeAsync(thirtyMinuteAudio);
// Result: Full transcription across multiple chunks, no duplicates
```

**Performance:** ~100-300 seconds for 30-minute real-world audio (varies by CPU/GPU).

📖 See [Audio Chunking Guide](docs/guides/developer-guide.md#audio-chunking-for-long-form-asr) for configuration, troubleshooting, and performance tips.

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- (Optional) NVIDIA GPU with CUDA 11.8+ for GPU acceleration

## Quick Start

```bash
# Clone and build
git clone https://github.com/elbruno/nvidia-voiceagent-cs.git
cd nvidia-voiceagent-cs
dotnet build

# (First-time) install Python helpers for model preparation
pip install -r scripts/onnx/requirements.txt

# (First-time) prepare Parakeet-TDT ASR model
python scripts/onnx/patch_encoder.py --model-dir model-cache/parakeet-tdt-0.6b/onnx
python scripts/onnx/patch_decoder.py --model-dir model-cache/parakeet-tdt-0.6b/onnx
python scripts/onnx/extract_vocab.py --model-dir model-cache/parakeet-tdt-0.6b

# (Windows) run all ASR prep steps at once
pwsh -File scripts/onnx/prepare-models.ps1 -ModelCachePath model-cache

# Run the app
cd NvidiaVoiceAgent
dotnet run
```

Open **<http://localhost:5003>** in your browser. The app auto-downloads models into `ModelHub:ModelCachePath` on first run.

> **First-time setup:** See the full step-by-step guide at `docs/guides/model-preparation.md`.
> **No GPU?** The app works on CPU and falls back to Mock Mode if models are unavailable.

## Run Tests

```bash
dotnet test
```

Tests are configuration-driven. Core tests read `tests/NvidiaVoiceAgent.Core.Tests/appsettings.Test.json` (mirrors the main app) and **skip real-model tests** when models are not available.

### Useful filters

```bash
dotnet test --filter "FullyQualifiedName~MockMode"
dotnet test --filter "FullyQualifiedName~RealModel"
dotnet test --filter "FullyQualifiedName~RealModelIntegrationTests"
```

See `tests/NvidiaVoiceAgent.Core.Tests/README.md` for the full test configuration guide.

## Supported Models

| Model | Type | Size | Status | Notes |
| --- | --- | --- | --- | --- |
| **Parakeet-TDT-0.6B-V2** | ASR | ~2.5 GB | ✅ Auto-download | ONNX format, GPU/CPU |
| **PersonaPlex-7B-v1** | LLM | ~16.7 GB | ✅ Available | Full-duplex speech AI, 18 voices, requires HF token |
| **TinyLlama-1.1B** | LLM | ~2.0 GB | 🔜 Coming soon | Fallback LLM option |
| **FastPitch** | TTS | ~80 MB | 🔜 Coming soon | Mel-spectrogram generator |
| **HiFiGAN** | Vocoder | ~55 MB | 🔜 Coming soon | Neural vocoder |

### PersonaPlex Model

PersonaPlex-7B-v1 is NVIDIA's state-of-the-art full-duplex speech-to-speech conversational AI:

- **7 billion parameters** based on Moshi architecture
- **Ultra-low latency**: ~170ms time-to-first-token
- **Voice control**: 18 pre-packaged voice personas + custom voice cloning
- **Persona prompting**: Text prompts define conversation role/style
- **Gated access**: Requires accepting NVIDIA's license on HuggingFace

To use PersonaPlex:

1. **Accept the license** at <https://huggingface.co/nvidia/personaplex-7b-v1>
2. **Generate a HuggingFace token** with read access
3. **Add token** to `appsettings.json`:

   ```json
   {
     "ModelHub": {
       "HuggingFaceToken": "hf_your_token_here"
     }
   }
   ```

4. **Download** via the UI or API: `POST /api/models/PersonaPlex-7B-v1/download`

📖 **Detailed Setup Guide**: See [HuggingFace Token Setup Guide](docs/guides/huggingface-token-setup.md) for complete instructions, troubleshooting, and security best practices.

**Note**: PersonaPlex currently runs in mock mode. TorchSharp integration for actual inference is planned.

## Configuration

Edit `NvidiaVoiceAgent/appsettings.json`:

```jsonc
{
  "ModelHub": {
    "AutoDownload": true,           // Download models on startup
    "UseInt8Quantization": true,    // Prefer quantized models
    "ModelCachePath": "model-cache", // Local cache directory
    "HuggingFaceToken": null        // Required for gated models (PersonaPlex)
  },
  "ModelConfig": {
    "UseGpu": true,                 // CUDA acceleration
    "Use4BitQuantization": true,    // LLM quantization
    "PersonaPlexVoice": "voice_0"   // Default PersonaPlex voice (0-17)
  },
  "DebugMode": {
    "Enabled": false,               // Record conversations for testing
    "AudioLogPath": "logs/audio-debug",
    "SaveIncomingAudio": true,      // Save user voice
    "SaveOutgoingAudio": true,      // Save TTS responses
    "SaveMetadata": true,           // Save conversation metadata
    "MaxAgeInDays": 7               // Auto-delete old recordings
  }
}
```

## Project Structure

```plaintext
nvidia-voiceagent-cs/
├── NvidiaVoiceAgent/              # ASP.NET Core Web App (UI, WebSockets, endpoints)
├── NvidiaVoiceAgent.Core/         # ML/Audio class library (ASR, AudioProcessor, MelSpectrogram)
├── NvidiaVoiceAgent.ModelHub/     # Model download library (HuggingFace integration)
├── tests/                         # xUnit test projects
└── docs/                          # Detailed documentation
```

## Documentation

| Document | Description |
| --- | --- |
| [Architecture](docs/architecture/overview.md) | Solution structure, project layers, dependency graph |
| [Implementation Details](docs/guides/implementation-details.md) | Voice pipeline, audio processing, ONNX inference, model loading |
| [PersonaPlex Integration Plan](docs/plans/plan_260217_0020.md) | Detailed plan for PersonaPlex-7B-v1 implementation |
| [Debug Mode Guide](docs/guides/debug-mode.md) | Record conversations for testing and analysis |
| [E2E Testing with Recorded Audio](docs/guides/e2e-testing-with-recorded-audio.md) | Build automated tests from recorded conversations |
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
- **Models**: NVIDIA Parakeet, PersonaPlex, FastPitch, HiFiGAN
- **Runtime**: [ONNX Runtime](https://onnxruntime.ai/), [TorchSharp](https://github.com/dotnet/TorchSharp), [ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/)

## Related Links

- [PersonaPlex Research Page](https://research.nvidia.com/labs/adlr/personaplex/)
- [PersonaPlex on HuggingFace](https://huggingface.co/nvidia/personaplex-7b-v1)
- [PersonaPlex GitHub](https://github.com/NVIDIA/personaplex)
