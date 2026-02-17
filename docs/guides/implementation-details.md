# Implementation Details

This document covers the internal workings of the voice processing pipeline, audio processing, ONNX inference, and model management.

## Voice Processing Pipeline

The full pipeline for a single voice request flows through these stages:

```
Browser WAV Audio (Binary WebSocket message)
    │
    ▼
1. AudioProcessor.DecodeWav()          — Parse RIFF/WAV, extract PCM samples
    │
    ▼
2. AudioProcessor.Resample()           — Convert to 16kHz mono if needed
    │
    ▼
3. MelSpectrogramExtractor.Extract()   — 128-mel filterbank spectrogram
    │
    ▼
4. MelSpectrogramExtractor.Normalize() — Per-feature normalization
    │
    ▼
5. ParakeetTdtAdapter.RunInference()   — ONNX Runtime inference (encoder + decoder)
    │
    ▼
6. ParakeetTdtAdapter.GreedyTdtDecode() — TDT decoding → text
    │
    ▼
7. [Optional] LLM → TTS → WAV response
```

All stages run in `NvidiaVoiceAgent.Core`. The web app's `VoiceWebSocketHandler` orchestrates the pipeline and sends responses back over WebSocket.

> **Important:** The handler calls `AsrService.TranscribeAsync()` directly without checking `IsModelLoaded` first. `TranscribeAsync` handles lazy loading internally — loading the ONNX session on the first call. This avoids bypassing the lazy-load mechanism with a premature `IsModelLoaded` gate.

## Audio Processing (AudioProcessor)

### WAV Decoding

`DecodeWav(byte[] wavData)` parses standard RIFF/WAV files:

1. Validates RIFF and WAVE headers
2. Reads the `fmt` chunk for format metadata (sample rate, bit depth, channels)
3. Locates the `data` chunk
4. Converts PCM bytes to `float[]` samples normalized to [-1.0, 1.0]

Supported formats:

- 8-bit, 16-bit, 24-bit, and 32-bit PCM
- Mono and stereo (stereo is averaged to mono)

### Resampling

`Resample(float[] samples, int sourceSampleRate, int targetSampleRate)` uses linear interpolation:

```
For each output sample i:
    srcPos = i * (sourceRate / targetRate)
    output[i] = lerp(input[floor(srcPos)], input[ceil(srcPos)], frac(srcPos))
```

This handles common conversions like 44100 Hz → 16000 Hz (browser default → ASR required).

### WAV Encoding

`EncodeWav(float[] samples, int sampleRate)` produces a standard 16-bit PCM WAV file with RIFF header, suitable for browser `<audio>` playback.

## Mel-Spectrogram Extraction

`MelSpectrogramExtractor` converts raw audio samples into the mel-spectrogram features required by the Parakeet-TDT model. The extractor is configurable via constructor parameters and defaults to values matching the Parakeet-TDT model spec.

### Parameters (Parakeet-TDT spec)

| Parameter | Value |
|-----------|-------|
| Mel bins | **128** (auto-detected from model) |
| FFT size | 512 |
| Window size | 400 samples (25ms at 16kHz) |
| Hop size | 160 samples (10ms at 16kHz) |
| Sample rate | 16000 Hz |
| Window function | Hann |
| Normalization | Per-feature mean/std (per mel bin) |

> **Note:** The mel bin count is auto-detected from the ONNX model’s input metadata at load time. After loading the ONNX session, `AsrService.ConfigureMelExtractorFromModel()` reads `_session.InputMetadata` to find the expected mel dimension. If the model expects a different count than the extractor’s current setting, a new `MelSpectrogramExtractor` is created with the correct value. This ensures the extractor always matches the model.

### Processing Steps

1. **Windowing** — Apply Hann window to overlapping frames
2. **FFT** — Radix-2 Cooley-Tukey FFT (power of 2 zero-padded)
3. **Power Spectrum** — Compute magnitude squared of FFT bins
4. **Mel Filterbank** — Apply 128 triangular mel-scale filters
5. **Log Transform** — `log(max(value, 1e-10))` to convert to log-mel scale
6. **Normalization** — Per-feature normalization (zero mean, unit variance per mel bin)

The FFT is implemented from scratch (no external DSP library) for zero-dependency operation.

## ASR Service (ONNX Inference)

### Model Loading Strategy

`AsrService.LoadModelAsync()` uses a multi-step search:

1. Check if `ModelConfig.AsrModelPath` is a direct `.onnx` file path
2. If it's a directory, try common filenames: `encoder.onnx`, `model.onnx`, `parakeet.onnx`, `asr.onnx`
3. Recursively search for any `.onnx` file in the directory
4. Query `IModelDownloadService.GetModelPath(ModelType.Asr)` for ModelHub-cached files
5. If nothing found → enter Mock Mode

Once found, the path is converted to an absolute path via `Path.GetFullPath()` before passing to `InferenceSession`. This ensures ONNX Runtime can resolve external data files (e.g., `encoder.onnx_data`) relative to the model file's directory.

### GPU/CPU Fallback

```csharp
// Try CUDA first
try {
    options.AppendExecutionProvider_CUDA(0);
} catch {
    // CUDA not available — CPU only
}
options.AppendExecutionProvider_CPU(0);
```

The service logs which execution provider was used. Check logs for `"CUDA execution provider configured"` or `"CUDA not available, falling back to CPU"`.

### Inference Pipeline

1. **Input preparation**: Mel spectrogram → tensor `[1, 128, T]` (batch, mels, time frames)
2. **Length tensor**: `[T]` as int64
3. **Dynamic input binding**: Reads `_session.InputMetadata` to match actual model input names and types
4. **Run**: `_session.Run(inputs)` → output tensor
5. **Output decoding**: Greedy TDT decode using the decoder ONNX model

### TDT Decoding

Greedy Token-and-Duration Transducer decoding (high-level):

```
Run encoder on mel-spectrogram
Initialize decoder state, timestep t = 0
Loop:
    decoder outputs logits over (vocab + blank + durations)
    if blank → advance time (t += 1)
    if token → emit token and advance by predicted duration
Stop when time exceeds encoded length or safety limit reached
```

Token IDs are mapped to text using `vocab.txt`. SentencePiece word boundaries (`▁`) are converted to spaces.

### Mock Mode

When no model is found, `AsrService` returns simulated transcripts based on audio duration:

```csharp
var mockResponses = new[] {
    "Hello, this is a test.",
    "The quick brown fox jumps over the lazy dog.",
    "Testing speech recognition.",
    ...
};
var index = (int)(duration * 10) % mockResponses.Length;
```

This enables full UI and WebSocket testing without downloading models.

## Model Management (ModelHub)

### Model Registry

`ModelRegistry` defines available models with HuggingFace metadata:

```csharp
new ModelInfo {
    Name = "Parakeet-TDT-0.6B-V2",
    Type = ModelType.Asr,
    RepoId = "onnx-community/parakeet-tdt-0.6b-v2-ONNX",
    PrimaryFile = "onnx/encoder.onnx",
    AdditionalFiles = ["onnx/encoder.onnx_data", "onnx/decoder.onnx"],
    IsRequired = true,
    IsAvailableForDownload = true,  // false for placeholder models
    ExpectedSizeBytes = 1_310_720_000
}
```

The registry includes four model entries:

| Model | Type | Required | Available for Download |
|-------|------|----------|------------------------|
| Parakeet-TDT-0.6B-V2 | ASR | Yes | Yes |
| FastPitch | TTS | No | No (coming soon) |
| HiFiGAN | Vocoder | No | No (coming soon) |
| TinyLlama | LLM | No | No (coming soon) |

Placeholder models have `IsAvailableForDownload = false`. The UI shows "Coming Soon" badges for these, and the download endpoint returns `400 Bad Request`.

### Download Flow

```
EnsureModelsAsync()
    │
    ├── For each required model:
    │   ├── Check cache: primary file + all additional files exist?
    │   │   ├── All present → OnModelCached() callback → skip
    │   │   └── Any missing → Download from HuggingFace
    │   │       ├── Delete existing files first (Windows workaround)
    │   │       ├── Download PrimaryFile
    │   │       ├── Download each AdditionalFile
    │   │       └── Report progress via IProgressReporter
    │   └── Done
    │
    └── Log summary: "Model check complete: N/N models ready"
```

> **Note:** `IsModelAvailable()` verifies that **all** files (primary + additional) exist, not just the main `.onnx` file. This prevents partial downloads from being treated as complete.

> **Windows workaround:** Before downloading, existing files are deleted first using a `DeleteExistingFile()` helper. This avoids `IOException` errors from HuggingFace Hub’s `ChmodAndReplace` operation, which can fail on Windows when trying to overwrite locked or in-use files.

### Progress Reporting

The `IProgressReporter` interface bridges ModelHub with UI:

```
ModelDownloadService → IProgressReporter.OnProgress(...)
                                │
                    ┌───────────┼───────────────┐
                    ▼                            ▼
        ConsoleProgressReporter          WebProgressReporter
        (Console.WriteLine)              (ILogBroadcaster → WebSocket → Browser)
```

The browser parses log messages for download patterns like `"Downloading encoder.onnx: 45%"` and updates progress bars in the Models panel.

## WebSocket Session Management

### VoiceWebSocketHandler

Each connection gets its own `VoiceSessionState`:

```csharp
public class VoiceSessionState {
    public bool SmartMode { get; set; }
    public string SmartModel { get; set; }
    public List<ChatMessage> ChatHistory { get; set; }
}
```

The handler:

1. Reads from WebSocket in a loop (binary or text messages)
2. Accumulates message fragments until `EndOfMessage`
3. Routes to `HandleTextMessageAsync` (config) or `HandleBinaryMessageAsync` (audio)
4. Sends JSON responses back and broadcasts logs

### LogsWebSocketHandler

Simpler — each connection registers with `LogBroadcaster` and receives all log entries:

```
Client connects → RegisterClient(connectionId)
Log event fires → BroadcastLogAsync() → Send to all registered clients
Client disconnects → UnregisterClient(connectionId)
```

## Thread Safety

| Resource | Protection | Pattern |
|----------|-----------|---------|
| Model loading | `SemaphoreSlim(1,1)` | Double-check locking |
| Log client registry | `ConcurrentDictionary` | Thread-safe collection |
| WebSocket send | Sequential per connection | Single handler loop |
| Model download | Sequential per model | Awaited in startup |

## Configuration Flow

```
appsettings.json
    │
    ├── "ModelConfig" section → IOptions<ModelConfig> → AsrService, future TTS/LLM
    │       AsrModelPath, UseGpu, Use4BitQuantization, etc.
    │
    ├── "ModelHub" section → ModelHubOptions → ModelDownloadService
    │       AutoDownload, ModelCachePath, HuggingFaceToken, etc.
    │
    └── "Logging" section → Serilog → structured logging
```

`ModelConfig` lives in `NvidiaVoiceAgent.Core.Models`. `ModelHubOptions` lives in `NvidiaVoiceAgent.ModelHub`.
