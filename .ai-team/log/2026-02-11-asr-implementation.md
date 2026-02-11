# Session Log: ASR Implementation

**Date:** 2026-02-11  
**Requested by:** Bruno Capuano  
**Who worked:** Parker  
**Duration:** Ongoing

## What They Did

Implemented `AsrService.cs` with ONNX Runtime for Parakeet-TDT ASR model inference, including:

1. **AsrService.cs** — Main ASR service with:
   - Lazy model loading (loads on first transcription request)
   - GPU/CPU fallback (tries CUDA first, falls back to CPU if unavailable)
   - Mock mode for development (returns mock transcripts when no ONNX model found)
   - Automatic model discovery from configured path

2. **MelSpectrogramExtractor.cs** — Audio preprocessing pipeline:
   - 80 mel bins
   - 25ms window (400 samples at 16kHz)
   - 10ms hop (160 samples at 16kHz)
   - Hann windowing
   - Log-mel normalization

3. **Project updates:**
   - Added `Microsoft.ML.OnnxRuntime.Gpu` NuGet package
   - Registered `AsrService` in DI container (`Program.cs`)
   - Updated health check endpoint

## Key Outcomes

✅ **ASR service ready for development**
- Lazy loading prevents startup delays when model is missing
- GPU/CPU fallback ensures portability across machines
- Mock mode allows frontend development without ONNX models
- Preprocessing pipeline matches Parakeet-TDT requirements

## Files Modified

- `src/NvidiaVoiceAgent/Services/AsrService.cs` (new)
- `src/NvidiaVoiceAgent/Services/MelSpectrogramExtractor.cs` (new)
- `src/NvidiaVoiceAgent/NvidiaVoiceAgent.csproj` (added ONNX Runtime)
- `src/NvidiaVoiceAgent/Program.cs` (registered service)

## Model Requirements

- Downloads Parakeet-TDT-0.6B v2 from HuggingFace
- Models placed in `models/parakeet-tdt-0.6b/`
- ~1.2GB on disk, 2-4GB VRAM (GPU), 4GB RAM (CPU)

## Next Steps

- Implement TtsService for FastPitch + HiFiGAN
- Add streaming support if needed
- Integrate LLM service layer
- End-to-end testing with mock audio

## Notes

See `.ai-team/decisions.md` for ASR/TTS/LLM architecture decisions and NVIDIA model integration strategy.
