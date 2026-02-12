# API Reference

This document covers all HTTP and WebSocket endpoints exposed by the NVIDIA Voice Agent.

## HTTP Endpoints

### GET /health

Returns the health status of the application and model availability.

**Response** (`200 OK`):

```json
{
  "status": "healthy",
  "asr_loaded": false,
  "asr_downloaded": true,
  "tts_loaded": false,
  "llm_loaded": false,
  "timestamp": "2026-02-12T12:00:00Z"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `status` | string | Always `"healthy"` if the server is responding |
| `asr_loaded` | boolean | Whether the ASR ONNX session is loaded in memory |
| `asr_downloaded` | boolean | Whether the ASR model files exist on disk |
| `tts_loaded` | boolean | Whether TTS models are loaded (future) |
| `llm_loaded` | boolean | Whether the LLM model is loaded (future) |
| `timestamp` | string | ISO 8601 UTC timestamp |

> **Note:** `asr_downloaded: true` with `asr_loaded: false` means the model files are cached but the ONNX session hasn't been initialized yet (lazy loading — loads on first inference request).

---

### GET /api/models

Returns detailed status for every registered model.

**Response** (`200 OK`):

```json
[
  {
    "name": "Parakeet-TDT-0.6B-V2",
    "type": "Asr",
    "status": "downloaded",
    "repo_id": "onnx-community/parakeet-tdt-0.6b-v2-ONNX",
    "local_path": "D:\\labs\\nvidia-voiceagent-cs\\NvidiaVoiceAgent\\model-cache\\parakeet-tdt-0.6b\\onnx\\encoder.onnx",
    "expected_size_mb": 1250.0,
    "is_required": true,
    "is_available_for_download": true
  }
]
```

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Human-readable model name |
| `type` | string | Model type: `"Asr"`, `"Tts"`, `"Vocoder"`, or `"Llm"` |
| `status` | string | `"downloaded"` or `"not_downloaded"` |
| `repo_id` | string | HuggingFace repository ID |
| `local_path` | string? | Absolute path on disk (null if not downloaded) |
| `expected_size_mb` | number | Expected download size in MB |
| `is_required` | boolean | Whether the model is required for the app to function |
| `is_available_for_download` | boolean | Whether the model can be downloaded (false for placeholder/coming-soon models) |

The registry includes four models: **Parakeet-TDT (ASR)**, **FastPitch (TTS)**, **HiFiGAN (Vocoder)**, and **TinyLlama (LLM)**. Only the ASR model is required; the others are optional and show download buttons in the UI.

---

### POST /api/models/{name}/download

Trigger download of a specific model by name.

**Request:** No body required. The `{name}` path parameter is the model name (e.g., `Parakeet-TDT-0.6B-V2`).

**Response** (`200 OK`):

```json
{
  "message": "Model 'Parakeet-TDT-0.6B-V2' downloaded successfully.",
  "path": "model-cache/parakeet-tdt-0.6b/onnx/encoder.onnx"
}
```

**Error Responses:**

- `400 Bad Request` — Model is not available for download (placeholder/coming-soon model)
- `404 Not Found` — Model name not recognized
- `500 Internal Server Error` — Download failed (network, disk, etc.)

---

### GET / (Static Files)

Serves the browser UI from `wwwroot/index.html`. The UI includes:

- Voice recording and playback controls
- Smart/Echo mode toggle
- Real-time log viewer
- Models panel showing download status and disk paths

---

### GET /swagger (Development only)

Swagger UI for exploring API endpoints. Available when `ASPNETCORE_ENVIRONMENT=Development`.

---

## WebSocket Endpoints

### /ws/voice — Voice Processing

Bi-directional WebSocket for the voice pipeline. Accepts both binary (audio) and text (configuration) messages.

**Connection:** `ws://localhost:5003/ws/voice`

#### Client → Server Messages

**1. Binary Audio Data**

Send WAV-encoded audio for transcription.

- Format: 16-bit PCM, mono, 16kHz (standard WAV with RIFF header)
- The server decodes, resamples if needed, and runs ASR

**2. Configuration Message** (Text/JSON)

```json
{
  "type": "config",
  "smartMode": true,
  "smartModel": "phi-3"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `type` | string | Must be `"config"` |
| `smartMode` | boolean | Enable AI-powered responses (vs echo mode) |
| `smartModel` | string | LLM model identifier to use |

**3. Clear History** (Text/JSON)

```json
{
  "type": "clear_history"
}
```

Clears the session's chat history for LLM context.

#### Server → Client Messages

All server responses are JSON text messages.

**1. Transcript Response**

Sent immediately after ASR completes:

```json
{
  "type": "transcript",
  "text": "Hello, how can I help you?"
}
```

**2. Thinking Indicator** (Smart Mode only)

Sent before LLM processing begins:

```json
{
  "type": "thinking"
}
```

**3. Voice Response**

Final response with transcript, LLM response, and synthesized audio:

```json
{
  "type": "voice",
  "transcript": "Hello, how can I help you?",
  "response": "I'm here to assist you!",
  "audio": "<base64-encoded-wav>"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `transcript` | string | ASR transcription of input audio |
| `response` | string | LLM response text (or echo of transcript) |
| `audio` | string | Base64-encoded WAV audio (22050Hz, 16-bit, mono) |

#### Session State

Each WebSocket connection maintains its own session:

- `SmartMode` — whether LLM is enabled (default: false)
- `SmartModel` — which LLM to use
- `ChatHistory` — conversation history for LLM context

---

### /ws/logs — Real-time Log Streaming

Read-only WebSocket that streams application logs to connected clients.

**Connection:** `ws://localhost:5003/ws/logs`

#### Server → Client Messages

**Welcome Message** (sent on connection):

```json
{
  "timestamp": "2026-02-12T12:00:00Z",
  "level": "info",
  "message": "Connected to log stream"
}
```

**Log Entry:**

```json
{
  "timestamp": "2026-02-12T12:00:01Z",
  "level": "info",
  "message": "ASR model loaded successfully using CPU"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `timestamp` | string | ISO 8601 UTC timestamp |
| `level` | string | `"info"`, `"warning"`, `"error"`, or `"debug"` |
| `message` | string | Log message text |

Log messages include:

- Model download progress (e.g., `"Downloading encoder.onnx: 45%"`)
- Model load/cached status (e.g., `"✅ Model already downloaded: Parakeet-TDT-0.6B-V2"`)
- ASR transcription results
- WebSocket connection events
- Error details

---

## Error Handling

All endpoints return standard HTTP status codes:

| Code | Meaning |
|------|---------|
| `200` | Success |
| `400` | Bad request (e.g., non-WebSocket request to WebSocket endpoint) |
| `500` | Internal server error |

WebSocket connections close with standard close codes:

- `1000` — Normal closure
- `1001` — Going away (server shutdown)
- `1011` — Internal error
