# Test Configuration Guide

This guide explains how the tests are configured to use real downloaded models.

## Configuration Structure

Tests use `appsettings.Test.json` which mirrors the main application's configuration:

```json
{
  "ModelConfig": {
    "AsrModelPath": "Models/parakeet-tdt-0.6b",
    "PersonaPlexModelPath": "model-cache/personaplex-7b",
    "UseGpu": false  // Tests use CPU for consistency
  }
}
```

## Test Organization

### 1. Mock Mode Tests (AsrServiceTests)

- Test graceful degradation when models are not available
- Use synthetic generated audio
- Always run (don't require real models)

### 2. Real Model Tests (AsrServiceTests)

- Load actual Parakeet-TDT ONNX model
- Test with various audio lengths and edge cases
- Skip gracefully if model not downloaded

### 3. Integration Tests (RealModelIntegrationTests)

- Test with real Parakeet-TDT and PersonaPlex models
- Full pipeline testing (ASR → LLM)
- Use speech-like generated audio (formant synthesis)
- Skip if models not available

### 4. Web Integration Tests (ModelIntegrationTests)

- Use WebApplicationFactory (full app context)
- Test with real recorded WAV files
- Validate end-to-end scenarios

## Running Tests

### Run All Tests

```bash
dotnet test
```

### Run Only Tests That Don't Require Models

```bash
dotnet test --filter "FullyQualifiedName~MockMode"
```

### Run Real Model Tests (requires models)

```bash
dotnet test --filter "FullyQualifiedName~RealModel"
```

### Check Test Results

- **Skipped tests**: Model not found (expected in CI or fresh environments)
- **Passed tests**: Model loaded and tested successfully
- **Failed tests**: Actual error that needs investigation

## Model Setup for Tests

### Option 1: Copy from Main App

If you've already downloaded models for the main application:

```bash
# Models should be in these locations relative to solution root:
Models/parakeet-tdt-0.6b/onnx/encoder.onnx
model-cache/personaplex-7b/model.safetensors
```

Tests will automatically find them.

### Option 2: Download via ModelHub

Run the main application once with `AutoDownload: true`:

```bash
cd NvidiaVoiceAgent
dotnet run
```

Models will be downloaded to locations specified in `appsettings.Development.json`.

### Option 3: Manual Download

Download models from HuggingFace:

**Parakeet-TDT:**

```bash
# Download from: https://huggingface.co/onnx-community/parakeet-tdt-0.6b-v2-ONNX
# Place in: Models/parakeet-tdt-0.6b/onnx/encoder.onnx
```

**PersonaPlex:**

```bash
# Download from: https://huggingface.co/andthattoo/PersonaPlex-7B
# Place in: model-cache/personaplex-7b/
```

## Updating Model Paths

If you store models in a different location, update `appsettings.Test.json`:

```json
{
  "ModelConfig": {
    "AsrModelPath": "C:/path/to/your/models/parakeet",
    "PersonaPlexModelPath": "C:/path/to/your/models/personaplex"
  }
}
```

## CI/CD Considerations

### GitHub Actions / Azure DevOps

Models are skipped by default (tests gracefully skip when models not found).

To enable model testing in CI:

1. Cache downloaded models between runs
2. Set `AutoDownload: true` in test configuration
3. Provide HuggingFace token via environment variable

### Local Development

Models are tested automatically if available. No special configuration needed.

## Test Coverage

### ASR (Parakeet-TDT) Tests

- ✅ Model loading
- ✅ Audio lengths: 0.5s - 5.0s
- ✅ Edge case sample counts (160 - 98348 samples)
- ✅ Multiple consecutive calls (stability)
- ✅ Partial transcription with confidence
- ✅ Real-world audio sample counts from logs
- ✅ Dimension mismatch prevention (the critical fix!)

### LLM (PersonaPlex) Tests

- ✅ Model loading
- ✅ Response generation
- ✅ Streaming responses

### Full Pipeline Tests

- ✅ ASR → LLM integration
- ✅ End-to-end voice processing

## Troubleshooting

### "Model not found" - Tests Skipped

This is **expected** if models aren't downloaded. Tests will gracefully skip.

To fix:

1. Download models (see "Model Setup" above)
2. Verify paths in `appsettings.Test.json`
3. Run tests again

### "ONNX Runtime Error"

Real error that needs investigation. Check:

1. Model files are complete (not corrupted)
2. ONNX Runtime version matches
3. CUDA/GPU drivers if using GPU

### "Dimension Mismatch Error" (BroadcastIterator)

This was the original bug we fixed. If you see this:

1. Verify you're using the latest code
2. Check that audioLength uses `audioSamples.Length` not `numFrames`
3. Run the specific failing test with verbose logging

## Key Files

- `appsettings.Test.json` - Test configuration
- `TestConfiguration.cs` - Configuration loader
- `AsrServiceTests.cs` - ASR unit tests (mock + real)
- `RealModelIntegrationTests.cs` - Full model integration tests
- `ModelIntegrationTests.cs` - Web app integration tests

## Philosophy

Tests are designed to:

1. **Pass without models** - Mock mode tests always work
2. **Test with real models** - If available, validate actual behavior
3. **Be self-documenting** - Clear test names and assertions
4. **Catch regressions** - Especially dimension mismatch errors
5. **Run in CI** - Work in automated environments

---

**Remember:** If tests skip due to missing models, that's OK! The mock tests still validate core logic. Real model tests add confidence when models are available.
