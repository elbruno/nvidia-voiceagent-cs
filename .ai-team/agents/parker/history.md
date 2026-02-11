# Project Context

- **Owner:** Bruno Capuano (bcapuano@gmail.com)
- **Project:** NVIDIA Voice Agent — C# port of Python real-time voice assistant
- **Stack:** C#, ASP.NET Core 8, WebSockets, TorchSharp/ONNX for NVIDIA models
- **Created:** 2026-02-11

## Source Project Models

From Python scenario5:
- **Parakeet-TDT-0.6B-V2** — 600M param ASR model (Token-and-Duration Transducer)
- **FastPitch** — 45M param spectrogram generator
- **HiFiGAN** — 14M param neural vocoder
- **TinyLlama-1.1B-Chat** — 1.1B param LLM with 4-bit quantization
- **Phi-3-mini-4k-instruct** — 3.8B param alternative LLM

C# options for model inference:
- **TorchSharp** — native PyTorch bindings for .NET
- **ONNX Runtime** — NVIDIA models often export to ONNX
- **Microsoft.ML.OnnxRuntime.Gpu** — CUDA-accelerated ONNX

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-02-11: ASR Service Implementation

**Implemented:** `AsrService.cs` with ONNX Runtime inference + `MelSpectrogramExtractor.cs` for audio preprocessing.

**Key decisions:**
- Used pure C# FFT implementation to avoid external signal processing dependencies
- `SessionOptions` conflicts with ASP.NET Core — must use fully qualified `Microsoft.ML.OnnxRuntime.SessionOptions`
- Mel spectrogram config: 80 bins, 512 FFT, 400 window, 160 hop (matches Parakeet)
- Greedy CTC decoding without beam search (simpler, works for most cases)

**Model paths:** Default is `models/parakeet-tdt-0.6b/`. Service auto-discovers `encoder.onnx`, `model.onnx`, or any `.onnx` file.

**Mock mode:** When no model found, returns mock transcripts and logs warning. Lets app run during development without large model files.

**GPU fallback:** CUDA provider tried first, catches exception and falls back to CPU if unavailable. Added `Microsoft.ML.OnnxRuntime.Gpu` package for CUDA support.

**ONNX input flexibility:** Auto-detects input tensor names from model metadata rather than hardcoding — different Parakeet exports may have different names.
