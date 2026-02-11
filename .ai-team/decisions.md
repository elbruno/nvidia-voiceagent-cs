# Decisions

Team decisions that affect everyone. Read before starting work.

<!-- Scribe merges new decisions from .ai-team/decisions/inbox/ -->

---

# Architecture Decision: C# Project Structure

**Date:** 2026-02-11  
**Author:** Dallas (Lead)  
**Status:** Accepted

## Context

We're porting the Python NVIDIA Voice Agent (scenario5/app.py) to C# / ASP.NET Core 8. The Python implementation uses FastAPI with WebSocket endpoints for real-time voice processing.

## Decision

### Solution Structure

```
NvidiaVoiceAgent.sln
└── src/NvidiaVoiceAgent/
    ├── Program.cs          (entry point, middleware, endpoints)
    ├── Services/           (AI model abstractions)
    │   ├── IAsrService.cs
    │   ├── ITtsService.cs
    │   ├── ILlmService.cs
    │   ├── IAudioProcessor.cs
    │   └── ILogBroadcaster.cs
    ├── Hubs/               (WebSocket handlers)
    │   ├── VoiceWebSocketHandler.cs
    │   └── LogsWebSocketHandler.cs
    ├── Models/             (DTOs and config)
    │   └── VoiceModels.cs
    └── wwwroot/            (static UI files)
        └── index.html
```

### Key Decisions

1. **Native WebSockets over SignalR** — The Python frontend expects raw WebSocket protocol with binary audio frames. SignalR would require frontend changes. We use `System.Net.WebSockets` directly for compatibility.

2. **Interface-first services** — All AI services (ASR, TTS, LLM) are defined as interfaces. This allows:
   - Swapping implementations (TorchSharp vs ONNX vs REST API)
   - Unit testing with mocks
   - Parallel development by team members

3. **TorchSharp + ONNX Runtime** — Support both inference paths. ONNX is faster and more portable; TorchSharp gives flexibility for models not yet exported to ONNX.

4. **NAudio for audio processing** — The de facto .NET audio library. Handles WAV decode/encode and resampling that the Python version does with NumPy/SciPy.

5. **Serilog for logging** — Structured logging with easy broadcast to the log WebSocket endpoint.

6. **.NET 8 LTS** — Stable, supported through 2026. No reason to use preview versions.

### WebSocket Protocol (unchanged from Python)

| Endpoint | Type | Description |
|----------|------|-------------|
| `/ws/voice` | Binary | Audio in → transcription/response → audio out |
| `/ws/logs` | Text/JSON | Real-time log stream |
| `/health` | HTTP GET | Health check with model status |

### NuGet Packages

- `TorchSharp` — PyTorch bindings for .NET
- `Microsoft.ML.OnnxRuntime` — ONNX model inference
- `NAudio` — Audio processing
- `Serilog.AspNetCore` — Structured logging

## Consequences

- Frontend HTML/JS can be copied directly from Python version
- Model weights may need conversion (PyTorch → ONNX)
- GPU support depends on CUDA availability and TorchSharp/ORT configuration
- Team can work in parallel: Ripley on ASR/TTS, Parker on audio, Lambert on tests

---

# NVIDIA Voice Model Integration for C#/.NET

**Author:** Parker (Integration Dev)  
**Date:** 2026-02-11  
**Status:** Approved

## Executive Summary

This document evaluates options for integrating NVIDIA's voice models (Parakeet ASR, FastPitch TTS, HiFiGAN vocoder) and small LLMs (TinyLlama, Phi-3) into a C#/.NET application. **ONNX Runtime is the recommended approach** for all models due to mature .NET support, CUDA acceleration, and available pre-exported ONNX models.

---

## Model Analysis

### 1. ASR: Parakeet-TDT-0.6B-V2 (600M params)

| Approach | Feasibility | Notes |
|----------|-------------|-------|
| **ONNX Runtime** | ✅ Recommended | Community ONNX exports available on HuggingFace |
| TorchSharp | ⚠️ Difficult | Requires Python preprocessing, architecture mismatch issues |
| NVIDIA Riva gRPC | ✅ Alternative | Requires Riva server deployment |

**Recommended:** ONNX Runtime with pre-exported model from `istupakov/parakeet-tdt-0.6b-v2-onnx` or `onnx-community/parakeet-tdt-0.6b-v2-ONNX`

**Key Considerations:**
- Model exports to multiple ONNX files (encoder.onnx + external data files ~1.2GB)
- Audio preprocessing (mel-spectrogram extraction) must be implemented in C#
- 16kHz mono audio input required
- ~2-4GB VRAM for GPU inference, ~4GB system RAM for CPU

**Preprocessing Pipeline:**
1. Resample audio to 16kHz mono
2. Extract log-mel spectrogram (80 mel bins, 25ms window, 10ms hop)
3. Normalize features
4. Run ONNX inference
5. Decode tokens with vocabulary file

---

### 2. TTS: FastPitch (45M params) + HiFiGAN (14M params)

| Approach | Feasibility | Notes |
|----------|-------------|-------|
| **ONNX Runtime** | ✅ Recommended | Export via NeMo toolkit, combined ~250MB |
| TorchSharp | ❌ Not recommended | Complex custom layers, tokenization issues |
| NVIDIA Riva gRPC | ✅ Alternative | Production-ready, requires server |

**Recommended:** ONNX Runtime with NeMo-exported models

**Key Considerations:**
- **Two-stage pipeline:** FastPitch (text→mel) → HiFiGAN (mel→wav)
- **Text preprocessing challenge:** G2P (grapheme-to-phoneme) and tokenization must be ported to C#
  - Use CMU Pronouncing Dictionary
  - Handle heteronyms (words with multiple pronunciations)
- Output: 22050Hz audio
- ~1GB VRAM combined for GPU, ~2GB system RAM for CPU

**Tokenization Options:**
1. Port NeMo's Python tokenizer to C# (complex)
2. Use simple character-level tokenization (quality tradeoff)
3. Pre-compute phoneme mappings for common words

---

### 3. LLM: Phi-3-mini-4k-instruct (3.8B params)

| Approach | Feasibility | Notes |
|----------|-------------|-------|
| **ONNX Runtime GenAI** | ✅ Recommended | Microsoft's official ONNX models with int4 quantization |
| TorchSharp | ❌ Not feasible | Too large, no quantization support |
| llama.cpp via P/Invoke | ⚠️ Alternative | GGUF format, requires native interop |

**Recommended:** ONNX Runtime GenAI with `Microsoft.ML.OnnxRuntimeGenAI` package

**Model Variants:**
- `cpu-int4-rtn-block-32` — CPU inference, 4-bit quantized (~2GB)
- `cuda-int4-rtn-block-32` — CUDA GPU inference, 4-bit quantized (~2GB)
- `directml-int4-awq-block-128` — DirectML for AMD/Intel GPUs

**Memory Requirements:**
- CPU int4: ~4-6GB RAM
- CUDA int4: ~4GB VRAM + ~2GB RAM
- Full precision (not recommended): ~15GB

**Note:** TinyLlama-1.1B can also use ONNX Runtime GenAI if smaller footprint needed.

---

## NuGet Packages Required

### Core Packages
```xml
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.20.0" />
<PackageReference Include="Microsoft.ML.OnnxRuntime.Gpu" Version="1.20.0" />
<PackageReference Include="Microsoft.ML.OnnxRuntimeGenAI" Version="0.5.0" />
<PackageReference Include="Microsoft.ML.OnnxRuntimeGenAI.Cuda" Version="0.5.0" />
```

### Audio Processing
```xml
<PackageReference Include="NAudio" Version="2.2.1" />
<PackageReference Include="NWaves" Version="0.9.6" />
```

### Optional (for Riva alternative)
```xml
<PackageReference Include="Grpc.Net.Client" Version="2.60.0" />
<PackageReference Include="Google.Protobuf" Version="3.25.2" />
```

---

## CUDA/System Requirements

### For GPU Acceleration (ONNX Runtime 1.20.x)
- **CUDA Toolkit:** 12.x
- **cuDNN:** 9.x
- **Visual C++ Redistributable:** 2019 or newer
- **NVIDIA Driver:** 525.60.13+ (Linux) / 528.33+ (Windows)

### Minimum VRAM
- ASR only: 2-4GB
- TTS only: 1GB
- LLM (int4): 4GB
- Full pipeline: 6-8GB recommended

### CPU Fallback
All models support CPU inference with degraded performance:
- ASR: ~3-5x slower
- TTS: ~2-3x slower
- LLM: ~10-20x slower (not recommended for real-time)

---

## Alternative: NVIDIA Riva API

If local ONNX inference proves too complex, NVIDIA Riva provides production-ready gRPC APIs:

**Pros:**
- Production-optimized models
- Streaming support built-in
- No model management overhead

**Cons:**
- Requires Docker/Kubernetes deployment
- NVIDIA AI Enterprise license for production
- Network latency for API calls

**C# Integration:**
1. Generate C# client from Riva `.proto` files
2. Use `Grpc.Net.Client` for streaming audio
3. Handle reconnection/failover logic

---

## Recommended Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    C# Voice Agent                           │
├─────────────────────────────────────────────────────────────┤
│  IAsrService          │  ITtsService        │  ILlmService  │
│  ───────────────      │  ───────────        │  ──────────   │
│  ParakeetOnnxAsr      │  FastPitchOnnxTts   │  Phi3OnnxLlm  │
│       ↓               │       ↓             │       ↓       │
│  ONNX Runtime         │  ONNX Runtime       │  ONNX GenAI   │
│  (encoder.onnx)       │  (fastpitch.onnx +  │  (Phi-3 int4) │
│                       │   hifigan.onnx)     │               │
└─────────────────────────────────────────────────────────────┘
```

---

## Known Limitations & Workarounds

### 1. Audio Preprocessing Gap
**Problem:** ONNX models don't include audio feature extraction.  
**Workaround:** Implement mel-spectrogram extraction using NWaves library.

### 2. TTS Tokenization
**Problem:** G2P conversion is complex to port to C#.  
**Workaround:** Start with character-level tokenization, iterate to phoneme-based.

### 3. Model Size
**Problem:** Large ONNX files (~1-2GB each).  
**Workaround:** Lazy loading, download on first use, cache in AppData.

### 4. Streaming Inference
**Problem:** ONNX Runtime doesn't natively support streaming.  
**Workaround:** Chunk audio for ASR, implement buffered TTS output.

---

## Implementation Priority

1. **Phase 1:** LLM with Phi-3 ONNX (best .NET support, well-documented)
2. **Phase 2:** ASR with Parakeet ONNX (community exports available)
3. **Phase 3:** TTS with FastPitch+HiFiGAN (most complex, tokenization challenge)

---

## Decision

**Approved Approach:** ONNX Runtime for all models with CUDA acceleration when available, CPU fallback for portability.

**Rationale:**
- Single runtime (ONNX) for all models
- Official Microsoft support for Phi-3
- Community-maintained ONNX exports for NVIDIA models
- No external service dependencies
- Cross-platform (.NET 8 on Windows/Linux)
