# Troubleshooting

Common issues and their solutions when running the NVIDIA Voice Agent.

## Build & Setup Issues

### .NET 10 SDK not found

**Symptom:** `The framework 'Microsoft.NETCore.App', version '10.0.0' was not found`

**Solution:**

1. Install [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (preview)
2. Verify: `dotnet --list-sdks` — should show a `10.0.x` entry
3. If using global.json, ensure it allows preview SDKs:

   ```json
   { "sdk": { "allowPrerelease": true } }
   ```

### Package restore fails

**Symptom:** NuGet restore errors or version conflicts

**Solution:**

```bash
dotnet clean
dotnet nuget locals all --clear
dotnet restore
dotnet build
```

### Solution won't open in Visual Studio

**Symptom:** `.slnx` not recognized

**Solution:** The project uses the new XML solution format (`.slnx`). Requires Visual Studio 2022 17.10+ or VS Code with C# Dev Kit. Alternatively, open individual `.csproj` files directly.

---

## Model Download Issues

### Model download fails or hangs

**Symptom:** App hangs on startup or shows download errors in logs

**Causes & Solutions:**

1. **Network issue** — Check internet connectivity. The app downloads from `huggingface.co`.
2. **Firewall** — Ensure outbound HTTPS to `huggingface.co` and `cdn-lfs.huggingface.co` is allowed.
3. **Disk space** — The ASR model is ~1.2 GB. Check free space on the drive containing `model-cache/`.
4. **Disable auto-download** — Set `"AutoDownload": false` in `appsettings.json` and manually place model files.

### "No ASR ONNX model found" / Mock Mode

**Symptom:** Logs show `Running in mock mode` and transcripts are simulated

**Causes:**

- Model files not downloaded yet (first run, or download failed)
- `ModelConfig.AsrModelPath` points to wrong directory
- Model files exist but have unexpected filename

**Solutions:**

1. Check model cache: look for `model-cache/parakeet-tdt-0.6b/onnx/encoder.onnx`
2. Open browser UI → click the model status indicator → check the Models panel for status and paths
3. Use the `/api/models` endpoint to see model status programmatically
4. Verify `appsettings.json` paths match actual file locations
5. If auto-download is disabled, manually download from HuggingFace:

   ```bash
   # Using HuggingFace CLI
   pip install huggingface-hub
   huggingface-cli download onnx-community/parakeet-tdt-0.6b-v2-ONNX \
     --include "onnx/*" \
     --local-dir model-cache/parakeet-tdt-0.6b
   ```

### Model shows as "downloaded" but "not loaded"

**Symptom:** `/health` returns `asr_downloaded: true` but `asr_loaded: false`

**Explanation:** This is normal. The ASR model uses lazy loading — the ONNX session is only created on the first transcription request. Send audio through the voice WebSocket to trigger model loading.

### ASR always returns mock transcript

**Symptom:** Every transcription returns `"Hello, this is a test transcription."` even though the model is downloaded.

**Cause:** If `VoiceWebSocketHandler.RunAsrAsync()` checks `_asrService.IsModelLoaded` before calling `TranscribeAsync()`, the check will always be `false` on the first call because `AsrService` uses lazy loading (the ONNX session is created inside `TranscribeAsync()`). This causes the handler to skip the real service and return the hardcoded mock.

**Fix:** The handler should call `TranscribeAsync()` directly when `_asrService` is registered, without gating on `IsModelLoaded`. `TranscribeAsync` handles lazy loading internally and falls back to mock mode only if no model files exist on disk.

### "file_size: The system cannot find the file specified: encoder.onnx_data"

**Symptom:** ONNX Runtime throws `RuntimeException` during model initialization, complaining about a missing `encoder.onnx_data` file.

**Cause:** The `encoder.onnx` model uses an external data file (`encoder.onnx_data`, ~2.48 GB) for its weights. If the initial download was interrupted or only the primary `.onnx` file was cached, the external data file will be missing. The availability check now verifies all required files (primary + additional), so a re-download will be triggered automatically.

**Solutions:**

1. Restart the app — it will detect the missing file and re-download all model files automatically. Existing files are deleted before re-downloading to avoid Windows file-locking issues.

2. If auto-download still fails, delete the incomplete cache and restart:

   ```bash
   # Windows
   Remove-Item -Recurse -Force NvidiaVoiceAgent/model-cache/parakeet-tdt-0.6b
   cd NvidiaVoiceAgent && dotnet run
   ```

3. Manually download the missing file:

   ```bash
   pip install huggingface-hub
   huggingface-cli download onnx-community/parakeet-tdt-0.6b-v2-ONNX \
     --include "onnx/encoder.onnx_data" \
     --local-dir NvidiaVoiceAgent/model-cache/parakeet-tdt-0.6b
   ```

---

## Model Availability Issues

### Download button shows "Coming Soon"

**Symptom:** The Models panel shows a "Coming Soon" badge instead of a download button for TTS, Vocoder, or LLM models.

**Explanation:** These models are registered as placeholders in the model registry with `IsAvailableForDownload = false`. Their HuggingFace repositories don't exist yet. The download endpoint returns `400 Bad Request` if you try to download them via the API.

**Status:** These models will be made available in future releases. Only the ASR model (Parakeet-TDT) is currently downloadable.

### Mel bin mismatch error

**Symptom:** ONNX Runtime throws a shape mismatch error during inference (e.g., expected dimension 128 but got 80).

**Cause:** The mel-spectrogram extractor was configured with a different number of mel bins than the ONNX model expects. The Parakeet-TDT model uses 128 mel bins.

**Solution:** This is handled automatically. `AsrService.ConfigureMelExtractorFromModel()` reads the model's `InputMetadata` after loading and reconfigures the `MelSpectrogramExtractor` if the expected mel dimension differs from the current setting. If you see this error, ensure you're using the latest code — the auto-detection was added to prevent this mismatch.

---

## GPU / CUDA Issues

### "CUDA not available, falling back to CPU"

**Symptom:** Logs show CUDA warning, inference runs on CPU (slower)

**Solutions:**

1. Verify NVIDIA driver: `nvidia-smi` should show driver version and GPU info
2. Check CUDA version: needs CUDA 11.8+ (check with `nvcc --version`)
3. Verify the GPU package is referenced:

   ```bash
   dotnet list NvidiaVoiceAgent.Core package | findstr OnnxRuntime.Gpu
   ```

   Should show `Microsoft.ML.OnnxRuntime.Gpu 1.20.1`
4. On Linux, ensure CUDA libraries are in `LD_LIBRARY_PATH`

### CUDA out of memory

**Symptom:** `CUDA error: out of memory` during model loading or inference

**Solutions:**

1. Close other GPU-intensive applications
2. Check GPU memory: `nvidia-smi` — look at memory usage
3. Use CPU fallback:

   ```json
   { "ModelConfig": { "UseGpu": false } }
   ```

4. When LLM is active, enable 4-bit quantization:

   ```json
   { "ModelConfig": { "Use4BitQuantization": true } }
   ```

---

## WebSocket Issues

### Connection fails (400 Bad Request)

**Symptom:** Browser shows 400 when connecting to `/ws/voice` or `/ws/logs`

**Causes:**

- Making an HTTP request instead of a WebSocket upgrade
- Proxy/load balancer stripping WebSocket headers

**Solutions:**

1. Ensure the URL uses `ws://` (not `http://`)
2. If behind a reverse proxy (nginx, etc.), enable WebSocket proxying:

   ```nginx
   location /ws/ {
       proxy_pass http://localhost:5003;
       proxy_http_version 1.1;
       proxy_set_header Upgrade $http_upgrade;
       proxy_set_header Connection "upgrade";
   }
   ```

### WebSocket disconnects unexpectedly

**Symptom:** Connection drops during audio processing

**Causes:**

- Audio message too large (> 256 KB buffer)
- Server-side exception
- Network timeout

**Solutions:**

1. Check server logs (`/ws/logs` or console) for error details
2. Keep audio chunks under 256 KB (about 8 seconds at 16kHz 16-bit)
3. Enable debug logging to see detailed WebSocket events:

   ```json
   { "Logging": { "LogLevel": { "NvidiaVoiceAgent.Hubs": "Debug" } } }
   ```

---

## Audio Issues

### No audio captured in browser

**Symptom:** Record button works but no audio is sent

**Solutions:**

1. Grant microphone permission when the browser prompts
2. Check browser console for `NotAllowedError` or `NotFoundError`
3. Try a different browser (Chrome recommended)
4. Verify your microphone works in OS settings

### Transcription is always empty

**Symptom:** ASR returns empty string

**Causes:**

- Audio is too short (< 0.5 seconds)
- Audio is silent (all zeros)
- Wrong sample rate

**Solutions:**

1. Record at least 1-2 seconds of speech
2. Check logs for `"Decoded N audio samples"` — N should be > 8000 for 0.5s
3. Enable debug logging to see sample counts and mel spectrogram dimensions

### Garbled or incorrect transcription

**Symptom:** ASR returns nonsense text

**Causes:**

- Audio at wrong sample rate (not 16kHz)
- Stereo audio not being mixed to mono correctly
- WAV header mismatch

**Solutions:**

1. Enable debug logging to check incoming audio parameters
2. Test with a known-good WAV file at 16kHz mono
3. Check browser's `MediaRecorder` settings — ensure PCM or WAV output

---

## UI Issues

### Models panel shows "not downloaded" but model exists

**Symptom:** Model files are on disk but the UI shows red status

**Solutions:**

1. Click the model status indicator to refresh
2. Check `/api/models` — the `local_path` field shows where the app is looking
3. Verify the file path matches the ModelHub cache path in `appsettings.json`

### Log panel not updating

**Symptom:** No log messages appear in the browser

**Solutions:**

1. Check browser console for WebSocket errors
2. Verify `/ws/logs` is accessible: open `ws://localhost:5003/ws/logs` in a WebSocket test tool
3. The log connection auto-reconnects — wait a few seconds

---

## Test Issues

### Tests fail with "Model already downloaded"

This is informational output, not an error. The tests use `WebApplicationFactory` which starts the real app, triggering model checks. Tests should still pass.

### Tests timeout

**Symptom:** Integration tests take > 30 seconds

**Causes:**

- Model download happening during tests (if auto-download is on and models aren't cached)
- Slow disk I/O

**Solutions:**

1. Run the app once first to cache models: `cd NvidiaVoiceAgent && dotnet run` (then Ctrl+C)
2. Set `"AutoDownload": false` in test-specific configuration

---

## Diagnostic Commands

```bash
# Check .NET SDK version
dotnet --version

# List installed SDKs
dotnet --list-sdks

# Verify solution builds
dotnet build

# Run all tests with output
dotnet test --logger "console;verbosity=detailed"

# Check GPU
nvidia-smi

# Check listening ports (Windows)
netstat -an | findstr 5003

# Check listening ports (Linux/Mac)
lsof -i :5003

# View model cache contents (Windows)
Get-ChildItem -Recurse NvidiaVoiceAgent/model-cache -Include *.onnx

# View model cache contents (Linux/Mac)
find NvidiaVoiceAgent/model-cache -name "*.onnx"

# Check app health
curl http://localhost:5003/health

# Check model status
curl http://localhost:5003/api/models
```

## Getting Help

- **GitHub Issues:** [github.com/elbruno/nvidia-voiceagent-cs/issues](https://github.com/elbruno/nvidia-voiceagent-cs/issues)
- **Original Project:** [nvidia-transcribe](https://github.com/elbruno/nvidia-transcribe)
- **ONNX Runtime Docs:** [onnxruntime.ai/docs](https://onnxruntime.ai/docs/)
