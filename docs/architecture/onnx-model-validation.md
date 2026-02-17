# ONNX Model Validation Workflow

This document describes the approach used to validate and fix ONNX models before they are consumed by the C# application.

## Problem Statement

The C# application uses ONNX Runtime as a black-box inference engine — it loads the model and runs it, but has no ability to inspect or modify the internal graph structure. When an ONNX model export contains a bug (incorrect constants, wrong axis values, missing operations), the C# application will fail at runtime with opaque errors like dimension mismatches.

We need a way to:

- Inspect the ONNX model's internal structure (nodes, constants, shape formulas)
- Identify and fix graph-level bugs
- Verify the fix works before deploying to the C# application
- Apply any necessary configuration changes (e.g., session options) to the C# code

## Approach: Python for Model Analysis, C# for Runtime

```
┌──────────────────────────────────────────────────┐
│  Python (offline tooling)                        │
│                                                  │
│  • onnx library: read/write ONNX protobuf graphs │
│  • onnxruntime: test inference end-to-end        │
│  • numpy: manipulate tensor values               │
│                                                  │
│  Scripts in: scripts/onnx/                       │
└────────────────┬─────────────────────────────────┘
                 │
                 │  Produces:
                 │  • Patched encoder.onnx
                 │  • Configuration values for C#
                 │
                 ▼
┌──────────────────────────────────────────────────┐
│  C# (runtime application)                        │
│                                                  │
│  • Loads the patched ONNX model                  │
│  • Configures SessionOptions per Python findings │
│  • Passes correct input shapes and parameters    │
│                                                  │
│  Code in: NvidiaVoiceAgent.Core/Adapters/        │
└──────────────────────────────────────────────────┘
```

## Why Python?

| Capability | Python (`onnx` library) | C# (ONNX Runtime) |
|---|---|---|
| Read graph nodes and edges | Yes | No |
| Read/modify initializer constants | Yes | No |
| Inspect shape annotations | Yes | No |
| Run inference | Yes | Yes |
| Patch and re-save models | Yes | No |
| Production inference | Possible but not used | Yes (primary) |

The `onnx` Python package provides full access to the Protocol Buffers representation of the model graph, which is essential for diagnosing and fixing export bugs.

## Workflow Steps

### 1. Inspect the Model

Run `scripts/onnx/inspect_model.py` to extract:

- Input/output tensor names, shapes, and data types
- Key constants (axis values, padding, reshape dimensions)
- Length-computation formulas (how the `length` parameter is transformed internally)
- Self-attention structure (to identify the rel_shift pattern)

### 2. Identify the Bug

Compare the ONNX graph's behavior against the expected PyTorch/NeMo implementation. In the Parakeet-TDT case, the `Slice_3` node in `rel_shift` was using axis=3 instead of axis=2, leaving the positional encoding dimension un-trimmed.

### 3. Patch the Model

Run `scripts/onnx/patch_encoder.py` to:

- Modify the incorrect constant value in the graph
- Clear stale shape metadata so ONNX Runtime doesn't pre-allocate wrong buffer sizes
- Save the patched model (backing up the original)

### 4. Verify the Patch

Run `scripts/onnx/verify_encoder.py` to confirm inference succeeds with:

- Synthetic inputs of varying frame lengths
- Real recorded audio (WAV file)

### 5. Apply to C# Code

Update the C# adapter with any configuration changes discovered during analysis:

- **Session options**: e.g., `EnableMemoryPattern = false` when shape metadata was cleared
- **Input parameters**: e.g., length parameter value (padded frame count vs. sample count)
- **model_spec.json**: update `length_parameter.value` to match the correct interpretation

## Case Study: Parakeet-TDT 0.6B rel_shift Bug

### Symptom

```
RuntimeException: Attempting to broadcast an axis by a dimension other than 1. 39 by 77
  at /layers.0/self_attn/Add_2
```

### Root Cause

The ONNX export of Parakeet-TDT 0.6B had a bug in the relative positional encoding shift. The `Slice_3` operation in each of the 24 Conformer self-attention layers was configured to slice on axis 3 instead of axis 2.

### Investigation with Python

1. **inspect_model.py** revealed `Constant_104 = [3]` (should be `[2]`)
2. **Shape tracing** showed the rel_shift output was `[B, 8, 2T-1, T]` instead of `[B, 8, T, T]`
3. **verify_encoder.py** confirmed the original model fails for ALL inputs (not a data issue)

### Fix

1. `patch_encoder.py` changed `Constant_104` from `[3]` to `[2]`
2. C# adapter: set `EnableMemoryPattern = false`
3. C# adapter: length parameter = `paddedFrames` (confirmed correct via Python testing)

### Result

- Python: all synthetic frame sizes and real audio pass
- C#: all 221 unit tests pass
- Live app: inference succeeds without dimension errors

## Scripts Location

All Python scripts are in [`scripts/onnx/`](../../scripts/onnx/):

| Script | Purpose |
|---|---|
| `inspect_model.py` | Read model structure and constants |
| `patch_encoder.py` | Fix known ONNX graph bugs |
| `verify_encoder.py` | Test inference with patched model |
| `requirements.txt` | Python dependencies |
| `README.md` | Detailed usage instructions |
