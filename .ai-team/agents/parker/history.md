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
