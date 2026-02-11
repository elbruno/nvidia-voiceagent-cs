# Project Context

- **Owner:** Bruno Capuano (bcapuano@gmail.com)
- **Project:** NVIDIA Voice Agent — C# port of Python real-time voice assistant
- **Stack:** C#, ASP.NET Core 8, WebSockets, TorchSharp/ONNX for NVIDIA models
- **Created:** 2026-02-11

## Test Strategy

From Python scenario5, key behaviors to test:
- WebSocket connect/disconnect lifecycle
- Binary audio receive → WAV decode
- Config message handling (smart_mode, smart_model)
- Transcript message response
- Audio playback response (base64 WAV)
- Error handling (invalid audio, model failures)

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->
