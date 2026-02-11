# Project Context

- **Owner:** Bruno Capuano (bcapuano@gmail.com)
- **Project:** NVIDIA Voice Agent — C# port of Python real-time voice assistant
- **Stack:** C#, ASP.NET Core 8, WebSockets, TorchSharp/ONNX for NVIDIA models
- **Created:** 2026-02-11

## Source Project

Porting from: https://github.com/elbruno/nvidia-transcribe/tree/main/scenario5

Key components:
- **ASR:** NVIDIA Parakeet-TDT-0.6B-V2 (speech-to-text)
- **TTS:** NVIDIA FastPitch + HiFiGAN (text-to-speech)
- **LLM:** TinyLlama-1.1B / Phi-3 Mini (smart mode responses)
- **Server:** FastAPI → ASP.NET Core
- **Transport:** WebSocket binary audio (16kHz WAV)
- **UI:** Browser-based, unchanged from Python version

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->
