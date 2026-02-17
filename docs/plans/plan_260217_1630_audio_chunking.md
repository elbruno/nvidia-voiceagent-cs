# Audio Chunking Implementation Plan

**Author:** AI Assistant  
**Date:** 2026-02-17 16:30  
**Status:** In Progress  
**Priority:** Medium  

## Executive Summary

Implement intelligent audio chunking for Parakeet-TDT ASR to handle audio longer than 60 seconds. Uses overlapping chunks with deduplication to preserve transcription quality while enabling long-form audio support.

**Key Decisions:**

- ✅ Overlapping chunks with 2-second overlap (reduces edge artifacts)
- ✅ Adapter abstraction (Parakeet-specific, doesn't affect other models)
- ✅ Configurable via model specification JSON
- ✅ Optional feature (can disable for compatibility)

---

## Problem Statement

**Current Limitation:**

- Parakeet-TDT max input: 6000 frames (~60 seconds)
- Longer audio: Returns `"[Audio too long for transcription]"` error
- User impact: Cannot transcribe meetings, lectures, podcasts

**Root Cause:**

- ONNX model input constraints (model architecture, GPU memory)
- No chunking strategy implemented

**Desired Outcome:**

- Support arbitrary audio length (30+ minutes)
- Maintain transcription quality
- Minimal code duplication
- Optional feature (don't break existing workflow)

---

## Solution Architecture

### 1. New Abstractions

**Interface: `IAudioChunkingStrategy`**

```csharp
public interface IAudioChunkingStrategy
{
    /// Get recommended chunk size in seconds
    float ChunkSizeSeconds { get; }
    
    /// Split audio into chunks
    AudioChunk[] ChunkAudio(float[] samples, int sampleRate);
    
    /// Merge transcripts from chunks
    string MergeTranscripts(string[] transcripts, AudioChunk[] chunks);
}

public record AudioChunk(float[] Samples, int StartFrame, int EndFrame, int OverlapStartFrame);
```

**Interface: `IAudioMerger`**

```csharp
public interface IAudioMerger
{
    /// Remove duplicate text from overlap regions
    string MergeTranscripts(string[] transcripts, float overlapFraction);
}
```

### 2. Implementation Classes

**Class: `OverlappingAudioChunker : IAudioChunkingStrategy`**

- Splits audio into fixed-size chunks
- Adds 2-second overlap between chunks
- Configurable chunk size via constructor

**Class: `TranscriptMerger : IAudioMerger`**

- Detects and removes duplicate text in overlap
- Preserves sentence boundaries
- Handles edge cases (single-word overlaps, punctuation)

### 3. Integration Points

**ParakeetTdtAdapter Modifications:**

- Add optional `_chunker` field (nullable)
- Modify `InferAsync()` to detect long audio and use chunker
- Modify `TranscribeAsync()` to merge results
- Add configuration via model spec (optional `chunking` section)

**Model Specification Extension:**

```json
{
  "chunking": {
    "enabled": true,
    "maxAudioFrames": 6000,
    "chunkSizeSeconds": 50,
    "overlapSeconds": 2,
    "strategy": "overlapping"
  }
}
```

---

## Implementation Phases

### Phase 1: Core Abstractions (2 hours)

**Files Created:**

- `NvidiaVoiceAgent.Core/Services/IAudioChunkingStrategy.cs` (interface + models)
- `NvidiaVoiceAgent.Core/Services/IAudioMerger.cs` (interface)

**Deliverables:**

- ✅ Define all interfaces
- ✅ Document expected behavior
- ✅ No implementation yet

**Tests:**

- None (interfaces only)

---

### Phase 2: Audio Chunker Implementation (3 hours)

**Files Created:**

- `NvidiaVoiceAgent.Core/Services/OverlappingAudioChunker.cs`
- `NvidiaVoiceAgent.Core/Services/AudioChunkerTests.cs`

**Key Logic:**

```
Input: 100 seconds of audio, chunk=50s, overlap=2s
└─ Chunk 0: 0-50s
├─ Chunk 1: 48-98s (2s overlap with chunk 0)
└─ Chunk 2: 96-100s (available data only)
```

**Tests:**

- ✅ Single chunk (audio < chunk size)
- ✅ Multiple chunks with perfect fit
- ✅ Multiple chunks with remainder
- ✅ Edge case: very short audio
- ✅ Edge case: chunk size larger than audio

**Acceptance Criteria:**

- All tests pass
- No off-by-one errors in frame calculations
- Handles edge cases gracefully

---

### Phase 3: Transcript Merger Implementation (3 hours)

**Files Created:**

- `NvidiaVoiceAgent.Core/Services/TranscriptMerger.cs`
- `NvidiaVoiceAgent.Core/Services/TranscriptMergerTests.cs`

**Key Logic (Greedy String Matching):**

```
Chunk 0: "Hello world this is"
Chunk 1: "this is a test" (2s overlap ≈ 20 tokens)

Overlap region: "this is"
├─ In Chunk 0 end: "... this is"
├─ In Chunk 1 start: "this is a ..."
├─ Match found: Remove "this is" from Chunk 1 start
└─ Result: "Hello world this is a test"
```

**Tests:**

- ✅ Perfect overlap (exact text match)
- ✅ Partial overlap (missing words)
- ✅ No clear overlap (random/unintelligible)
- ✅ Single word chunks
- ✅ Punctuation handling

**Acceptance Criteria:**

- Detects 80%+ of real overlaps
- Never creates duplicate content
- Handles edge cases (empty transcripts, single words)

---

### Phase 4: Adapter Integration (2 hours)

**Files Modified:**

- `NvidiaVoiceAgent.Core/Adapters/ParakeetTdtAdapter.cs`
- `NvidiaVoiceAgent.Core.Models/AsrModelSpecification.cs`

**Changes:**

1. Add optional `ChunkingConfig` to `AsrModelSpecification`
2. Add `_chunker` and `_merger` fields to `ParakeetTdtAdapter`
3. Load chunking config in `LoadAsync()`
4. Modify `RunInference()` to detect long audio and use chunker
5. Add `TranscribeWithChunkingAsync()` method
6. Update health status to report chunking capability

**Algorithm in `RunInference()`:**

```csharp
if (numFrames > maxFrames && chunking.enabled)
{
    // Use chunking
    var chunks = _chunker.ChunkAudio(samples, sampleRate);
    var transcripts = new string[chunks.Length];
    for (int i = 0; i < chunks.Length; i++)
    {
        var melSpec = PrepareInput(chunks[i].Samples);
        transcripts[i] = await InferAsync(melSpec);
    }
    return _merger.MergeTranscripts(transcripts, chunks);
}
else if (numFrames > maxFrames)
{
    // Old behavior: return error
    return "[Audio too long for transcription]";
}
```

**Tests:**

- ✅ Chunking disabled (backward compatibility)
- ✅ Short audio (no chunking needed)
- ✅ Long audio (triggers chunking)
- ✅ End-to-end long audio transcription

**Acceptance Criteria:**

- 100% backward compatible (no breaking changes)
- Chunking optional via config
- Health check reports chunking status
- Error handling for edge cases

---

### Phase 5: Configuration & Testing (2 hours)

**Files Modified:**

- `appsettings.json`
- `appsettings.Development.json.example`
- `NvidiaVoiceAgent.Core.Tests/Adapters/ParakeetTdtAdapterTests.cs`

**Configuration Example:**

```json
{
  "ModelConfig": {
    "AsrModelPath": "models/parakeet-tdt-0.6b",
    "AudioChunking": {
      "Enabled": true,
      "MaxChunkSizeSeconds": 50,
      "OverlapSeconds": 2
    }
  }
}
```

**Tests Added:**

- ✅ Unit tests: OverlappingAudioChunker (10 tests)
- ✅ Unit tests: TranscriptMerger (8 tests)
- ✅ Integration tests: ParakeetTdtAdapter with chunking (5 tests)
- ✅ Performance test: 10-minute audio (benchmark)

**Acceptance Criteria:**

- All 23 new tests pass
- Performance: < 150ms per chunk inference
- Configuration documented in README
- No regressions in existing tests

---

### Phase 6: Validation & Deployment (3 hours)

**Objective:** Validate implementation with real-world scenarios, measure performance, and prepare for production deployment.

**Files Modified:**

- `docs/guides/developer-guide.md` (update with chunking guidance)
- `appsettings.Development.json` (enable chunking)
- `README.md` (document long-form audio support)

**Tasks:**

#### 6.1 Integration Testing with Long-Form Audio (1 hour)

- **Create test suite:** Generate or record 30-minute+ continuous audio samples
  - Silent portions (silence robustness)
  - Music + speech (channel separation)
  - Multiple speakers (context switching)
  - Different accents & speech rates

- **Test scenarios:**
  - 30-minute continuous podcast
  - 10-minute meeting with overlapping speech
  - Technical lecture with pauses
  - Edge case: exactly 60s, 120s, 150s (chunk boundary aligned)

- **Validation:**
  - ✅ Transcription accuracy comparable to single-pass (within 5% error rate)
  - ✅ No text duplicates in overlap regions
  - ✅ Proper sentence boundaries preserved
  - ✅ No dropped words at chunk transitions

**Acceptance Criteria:**

- All real-world test cases pass
- Chunked transcript matches single-pass (baseline) ±5%
- Zero data loss or duplication artifacts

#### 6.2 Performance Profiling (1 hour)

- **Benchmark scenarios:**
  - 10-minute audio (typical use case)
  - 60-minute audio (stress test)
  - Memory usage tracking per chunk

- **Metrics to measure:**
  - Total inference time vs. audio duration (should be ~linear)
  - Per-chunk overhead (ideally < 10ms)
  - Peak memory usage (GPU + CPU)
  - Chunk processing time variance

- **Profiling tools:**
  - Stopwatch around chunk inference loop
  - GC.GetTotalMemory() before/after each chunk
  - ONNX Runtime profiling (optional)

- **Performance targets:**
  - 10-min audio: < 5 seconds total (realistic: 15-20s depending on CPU/GPU)
  - Memory overhead: < 100MB per chunk (typically 50-80MB)
  - Linear scaling: doubling audio ≈ doubles inference time

**Acceptance Criteria:**

- Performance benchmarks completed and logged
- No memory leaks (memory stable across many chunks)
- Performance acceptable for intended use case

#### 6.3 Enable in Model Specification (30 minutes)

- **Update Parakeet model spec** (`models/parakeet-tdt-0.6b/model_spec.json`):

  ```json
  {
    "chunking": {
      "enabled": true,
      "chunk_size_seconds": 50,
      "overlap_seconds": 2,
      "strategy": "overlapping"
    }
  }
  ```

- **Verify configuration loading:**
  - ✅ Model spec deserializes correctly
  - ✅ Chunking config is recognized
  - ✅ Adapter initializes with chunker + merger
  - ✅ Health endpoint reports chunking capability

**Acceptance Criteria:**

- Model spec valid JSON
- Chunking enabled in production model config
- Health check confirms chunking is active

#### 6.4 Documentation & UI Updates (30 minutes)

- **Update developer guide:**
  - Explain when chunking is triggered
  - Document configuration options
  - Show chunking behavior in logs
  - Troubleshooting: chunking disabled, performance tips

- **Update UI (if applicable):**
  - Show progress across chunks (e.g., "2/3 chunks processed")
  - Display chunk boundaries in transcript viewer
  - Optional: show overlap detection highlights

- **Update README:**
  - Add "Long-Form Audio Support" section
  - Link to new documentation
  - Performance expectations

**Acceptance Criteria:**

- Documentation is clear and complete
- Examples provided for common scenarios
- Users understand chunking trade-offs

---

## Success Metrics

| Metric | Target | How to Measure |
|--------|--------|----------------|
| Long-form support | 30+ min audio | Test with 30-min sample |
| Quality preservation | ≥80% match to single-chunk baseline | Compare outputs |
| Performance | <1 sec overhead | Profile chunking logic |
| Backward compat | 100% | All existing tests pass |
| Code coverage | ≥85% | New code only |

---

## Risk Assessment

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|-----------|
| Duplicate text in output | **Medium** | Word repetition | Robust overlap detection + test suite |
| Word split at boundaries | **High** | Garbled words | 2-second overlap window |
| Performance regression | **Low** | Slower inference | Benchmark & profile in Phase 5 |
| Config complexity | **Low** | User confusion | Defaults work out-of-box |
| GPU memory issues | **Medium** | OOM errors | Chunk size configurable |

---

## Dependencies

**Internal:**

- IAudioProcessor (existing)
- MelSpectrogramExtractor (existing)
- ParakeetTdtAdapter (to modify)
- AsrModelSpecification (to extend)

**External:**

- System.Collections.Generic
- Microsoft.Extensions.Logging (existing)
- xUnit (for tests)

**No new NuGet packages required** ✅

---

## File Structure

```
NvidiaVoiceAgent.Core/
├── Services/
│   ├── IAudioChunkingStrategy.cs (NEW)
│   ├── IAudioMerger.cs (NEW)
│   ├── OverlappingAudioChunker.cs (NEW)
│   ├── TranscriptMerger.cs (NEW)
│   └── AudioProcessor.cs (unchanged)
├── Adapters/
│   └── ParakeetTdtAdapter.cs (MODIFIED)
└── Models/
    └── AsrModelSpecification.cs (MODIFIED)

NvidiaVoiceAgent.Core.Tests/
├── Services/
│   ├── OverlappingAudioChunkerTests.cs (NEW)
│   └── TranscriptMergerTests.cs (NEW)
└── Adapters/
    └── ParakeetTdtAdapterChunkingTests.cs (NEW)
```

---

## Timeline

| Phase | Duration | Start | End |
|-------|----------|-------|-----|
| Phase 1: Abstractions | 2h | Day 1 | Day 1 |
| Phase 2: Chunker | 3h | Day 1 | Day 2 |
| Phase 3: Merger | 3h | Day 2 | Day 2 |
| Phase 4: Integration | 2h | Day 2 | Day 3 |
| Phase 5: Testing & Config | 2h | Day 3 | Day 3 |
| Phase 6: Validation & Deployment | 3h | Day 3 | Day 4 |
| **Total** | **14h** | | |

---

## Decision Log

**Decision 1: Overlapping vs. Context-Window Approach**

- ✅ **Chosen:** Overlapping chunks
- **Reasoning:** Parakeet encoder is stateless; overlap is simpler than maintaining hidden state
- **Alternative:** Store hidden states between chunks (complex, not needed for CTC)

**Decision 2: Fixed-size vs. Dynamic Chunks**

- ✅ **Chosen:** Fixed-size chunks
- **Reasoning:** Predictable memory, simpler to implement, configurable
- **Alternative:** Dynamic sizing based on silence (complex, premature optimization)

**Decision 3: Where to Implement Chunking**

- ✅ **Chosen:** In `ParakeetTdtAdapter` (model-specific)
- **Reasoning:** Different models may need different strategies; isolates complexity
- **Alternative:** In `AudioProcessor` (too generic, affects all models)

**Decision 4: Merge Strategy**

- ✅ **Chosen:** String-based greedy matching on overlap region
- **Reasoning:** Simple, no dependencies, works with any ASR output
- **Alternative:** Token-level merging (requires access to internal model state, fragile)

---

## Future Enhancements

- **Post-Phase 5:**
  - Language-aware punctuation detection
  - Confidence scores per chunk
  - Adaptive chunk sizing based on silence
  - Batch processing multiple chunks in parallel
  - Stream-based chunking (socket receives chunks live)

---

## References

- [Plan: Model Adapter Pattern](plan_260217_1300_model_adapter_pattern.md)
- [Audio Processing Analysis](../guides/implementation-details.md)
- [ParakeetTdtAdapter Current Implementation](../../NvidiaVoiceAgent.Core/Adapters/ParakeetTdtAdapter.cs)
