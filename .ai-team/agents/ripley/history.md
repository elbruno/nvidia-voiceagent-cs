# Project Context

- **Owner:** Bruno Capuano (bcapuano@gmail.com)
- **Project:** NVIDIA Voice Agent — C# port of Python real-time voice assistant
- **Stack:** C#, ASP.NET Core 8, WebSockets, TorchSharp/ONNX for NVIDIA models
- **Created:** 2026-02-11

## Source Project

Porting from: https://github.com/elbruno/nvidia-transcribe/tree/main/scenario5

Python server uses:
- FastAPI with WebSocket endpoints
- Binary audio frames (WAV) over WebSocket
- JSON messages for config/response
- Static file serving for UI

C# equivalent patterns:
- ASP.NET Core with `UseWebSockets()` middleware
- `WebSocket.ReceiveAsync()` for binary frames
- `System.Text.Json` for JSON messages
- `UseStaticFiles()` for UI

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-02-11: WebSocket Message Protocol
- `/ws/voice` handles both binary (WAV audio) and text (JSON config) frames
- Text messages: `{"type": "config", "smart_mode": true, "smart_model": "phi3"}` and `{"type": "clear_history"}`
- Audio processing pipeline: WAV decode → ASR → (optional LLM) → TTS → WAV encode
- Responses: `transcript`, `thinking`, `response` JSON messages
- Session state maintains smart_mode, smart_model, and chat_history per connection

### 2026-02-11: LogBroadcaster Pattern
- Uses `ConcurrentDictionary<string, WebSocket>` for thread-safe client tracking
- Handles disconnections gracefully during broadcast with cleanup list
- LogsWebSocketHandler registers/unregisters with broadcaster on connect/disconnect
