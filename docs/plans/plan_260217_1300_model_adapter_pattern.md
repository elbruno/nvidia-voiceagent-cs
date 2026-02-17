# Implementation Plan: Model Adapter Pattern for Multi-Model Support

**Date:** February 17, 2026, 13:00  
**Status:** In Progress  
**Priority:** High (Blocks ASR functionality)

## Problem Statement

The current codebase assumes all models of the same type (ASR, TTS, LLM) work identically:

- Hardcoded mel-spectrogram parameters (128 mels, 512 FFT)
- Generic tensor preparation
- Runtime ONNX introspection (unreliable)
- **Current blocker:** Parakeet-TDT dimension mismatches causing runtime errors

**Models to support:**

1. `parakeet-tdt-0.6b` - ASR (CTC-based)
2. `fastpitch-en` - TTS Acoustic Model
3. `hifigan-en` - TTS Vocoder
4. `personaplex-7b` - Full-duplex LLM
5. `tinyllama-1.1b` - Causal LLM

## Solution: Model Adapter Pattern with Metadata

### Architecture Overview

```
Model Specification (JSON) → Adapter Factory → Specific Adapter → Service
                                    ↓
                            (ParakeetAdapter, WhisperAdapter, etc.)
```

**Key Components:**

1. **Model Specification Files** (`model_spec.json`) - Declarative config per model
2. **Adapter Interface** - Contract for model integration
3. **Concrete Adapters** - Model-specific implementations
4. **Adapter Factory** - Runtime adapter selection
5. **Updated Services** - Use adapters instead of hardcoded logic

---

## Phase 1: Foundation & Fix Current Issues (Week 1)

**Goal:** Fix Parakeet-TDT errors and establish adapter infrastructure

### 1.1 Create Model Specification Schema

**File:** `NvidiaVoiceAgent.Core/Models/ModelSpecification.cs`

```csharp
- ModelSpecification (base class)
- AsrModelSpecification
- TtsModelSpecification  
- LlmModelSpecification
- JSONSchema validation attributes
```

### 1.2 Create Parakeet-TDT Specification

**File:** `NvidiaVoiceAgent/Models/parakeet-tdt-0.6b/model_spec.json`

```json
{
  "model_name": "parakeet-tdt-0.6b",
  "model_type": "asr_encoder_ctc",
  "framework": "onnx",
  "audio_preprocessing": {
    "sample_rate": 16000,
    "mel_bins": 128,
    "fft_size": 512,
    "hop_length": 160,
    "win_length": 400,
    "fmin": 0,
    "fmax": 8000
  },
  "input_requirements": {
    "format": "[batch, mel_bins, time_frames]",
    "padding": {
      "enabled": true,
      "strategy": "multiple_of",
      "value": 8,
      "pad_value": 0.0
    },
    "length_parameter": {
      "name": "length",
      "type": "int64",
      "value": "padded_frame_count"
    }
  },
  "model_files": {
    "encoder": "onnx/encoder.onnx",
    "vocabulary": "vocab.txt"
  },
  "decoding": {
    "type": "ctc",
    "blank_token_id": 0
  }
}
```

### 1.3 Implement Base Adapter Interface

**File:** `NvidiaVoiceAgent.Core/Adapters/IModelAdapter.cs`

```csharp
public interface IModelAdapter<TInput, TOutput> : IDisposable
{
    string ModelName { get; }
    bool IsLoaded { get; }
    
    Task LoadAsync(string modelPath, CancellationToken ct);
    TInput PrepareInput(object rawData);
    Task<TOutput> InferAsync(TInput input, CancellationToken ct);
    
    // Metadata
    ModelSpecification GetSpecification();
}
```

### 1.4 Create Parakeet Adapter

**File:** `NvidiaVoiceAgent.Core/Adapters/ParakeetTdtAdapter.cs`

- Load `model_spec.json`
- Configure `MelSpectrogramExtractor` from spec
- Implement padding logic from spec
- Handle ONNX inference with correct tensors
- CTC decoding

### 1.5 Update AsrService to Use Adapter

- Inject `IModelAdapter<float[], string>` instead of direct ONNX
- Remove hardcoded mel-spectrogram creation
- Delegate to adapter for all model operations

### 1.6 Testing

- Update `AsrServiceTests` to use adapter pattern
- Test with real Parakeet model
- Verify dimension errors are resolved

**Deliverables:**

- ✅ Model specification schema
- ✅ Parakeet spec JSON file
- ✅ Base adapter interfaces
- ✅ ParakeetTdtAdapter implementation
- ✅ Updated AsrService
- ✅ All tests passing

---

## Phase 2: Extend to Other Models (Week 2)

### 2.1 TTS Model Specifications

**FastPitch:**

```json
{
  "model_type": "tts_acoustic",
  "input_type": "phoneme_sequence",
  "output_type": "mel_spectrogram",
  "vocoder_required": true
}
```

**HiFi-GAN:**

```json
{
  "model_type": "vocoder",
  "input_type": "mel_spectrogram",
  "output_type": "waveform",
  "sample_rate": 22050
}
```

### 2.2 TTS Adapters

- `FastPitchAdapter` - Phoneme → Mel
- `HiFiGanAdapter` - Mel → Waveform
- Update `TtsService` to use adapter pipeline

### 2.3 LLM Specifications

**TinyLlama:**

```json
{
  "model_type": "llm_causal",
  "context_length": 2048,
  "quantization": "4bit",
  "tokenizer": "tokenizer.json"
}
```

**PersonaPlex:**

```json
{
  "model_type": "llm_full_duplex",
  "context_length": 8192,
  "voice_embeddings": "voices.tgz",
  "num_voices": 18
}
```

### 2.4 LLM Adapters

- `TinyLlamaAdapter`
- `PersonaPlexAdapter`
- Update LLM services

**Deliverables:**

- ✅ All model spec files created
- ✅ TTS adapters implemented
- ✅ LLM adapters implemented
- ✅ All services refactored
- ✅ Integration tests passing

---

## Phase 3: Advanced Features (Week 3)

### 3.1 Adapter Factory & Registry

**File:** `NvidiaVoiceAgent.Core/Adapters/ModelAdapterFactory.cs`

```csharp
public class ModelAdapterFactory
{
    private static readonly Dictionary<string, Type> _adapterRegistry;
    
    public static IModelAdapter CreateAdapter(string modelPath)
    {
        var spec = LoadModelSpec(modelPath);
        var adapterType = _adapterRegistry[spec.ModelType];
        return (IModelAdapter)Activator.CreateInstance(adapterType, spec);
    }
    
    public static void RegisterAdapter(string modelType, Type adapterType);
}
```

### 3.2 Model Discovery & Validation

- Auto-discover models in `Models/` directory
- Validate `model_spec.json` against schema
- Health check endpoint to verify model compatibility

### 3.3 Multiple Model Support

- Allow multiple ASR models loaded simultaneously
- Model selection via configuration or API
- Benchmark different models

### 3.4 Community Adapters

- Adapter plugin system
- External adapter DLLs
- Adapter versioning

**Deliverables:**

- ✅ Adapter factory
- ✅ Model discovery service
- ✅ Validation framework
- ✅ Multi-model support
- ✅ Documentation

---

## Phase 4: Testing & Documentation (Week 4)

### 4.1 Comprehensive Testing

- Unit tests for each adapter
- Integration tests for service → adapter interaction
- End-to-end tests with real models
- Performance benchmarks

### 4.2 Documentation

**Files to create:**

- `docs/architecture/model_adapter_pattern.md` - Architecture decision record
- `docs/guides/adding_new_models.md` - How to add custom models
- `docs/guides/model_specifications.md` - Spec file reference
- `README.md` updates

### 4.3 Migration Guide

- Document breaking changes
- Provide migration path from old code
- Script to auto-generate spec files from existing models

### 4.4 Examples

- Example custom adapter
- Example model spec for popular models (Whisper, Wav2Vec2)
- Sample integration with external model hubs

**Deliverables:**

- ✅ 100% test coverage for adapters
- ✅ Complete documentation
- ✅ Migration guide
- ✅ Example implementations

---

## Implementation Order

### Immediate (Today)

1. ✅ Create this plan document
2. ⬜ Create model specification classes
3. ⬜ Create Parakeet `model_spec.json`
4. ⬜ Implement `IModelAdapter` interface
5. ⬜ Implement `ParakeetTdtAdapter`

### This Week

1. ⬜ Refactor `AsrService` to use adapter
2. ⬜ Fix all ASR tests
3. ⬜ Verify Parakeet works end-to-end

### Next Week

1. ⬜ TTS adapters
2. ⬜ LLM adapters
3. ⬜ Factory pattern

### Week 3-4

1. ⬜ Advanced features
2. ⬜ Testing & documentation

---

## Success Criteria

- [ ] Parakeet-TDT ASR works without dimension errors
- [ ] All 5 models have specification files
- [ ] Each model type has at least one working adapter
- [ ] All existing tests pass
- [ ] New adapter tests have >90% coverage
- [ ] Documentation complete
- [ ] Zero hardcoded model assumptions in services

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Model specs incomplete/incorrect | High | Create validation tests, use ONNX metadata as source of truth |
| Breaking existing functionality | High | Comprehensive regression tests, feature flags |
| Performance overhead | Medium | Benchmark, optimize factory pattern, consider caching |
| Over-engineering | Medium | Start simple (Phase 1), iterate based on needs |
| Spec schema changes | Low | Version spec files, support multiple schema versions |

---

## Dependencies

- `System.Text.Json` - JSON parsing (already in project)
- `Microsoft.ML.OnnxRuntime` - Model inference (already in project)
- No new external dependencies required

---

## Notes

- Keep backward compatibility during migration
- Use feature flags to enable/disable adapter pattern during testing
- Consider lazy loading of adapters for performance
- Plan for future: Model marketplace, cloud-hosted spec repository

---

## Checkpoints

- **Checkpoint 1** (End of Week 1): Parakeet adapter working, ASR fixed
- **Checkpoint 2** (End of Week 2): All models have adapters
- **Checkpoint 3** (End of Week 3): Advanced features complete
- **Checkpoint 4** (End of Week 4): Documentation & testing complete

---

**Next Steps:** Start implementing model specification classes and Parakeet spec file.
