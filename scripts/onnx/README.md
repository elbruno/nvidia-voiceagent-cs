# ONNX Model Scripts

Python utilities for inspecting, patching, and verifying the Parakeet-TDT ONNX encoder model used by this project.

## Why Python?

The C# application uses ONNX Runtime to run inference, but ONNX Runtime treats the model as a black box — it executes the graph but does not expose the internal structure. When the ONNX graph itself contains a bug (as happened with the Parakeet-TDT export), we need to:

1. **Read the graph** to understand nodes, constants, and shape formulas.
2. **Identify the bug** by tracing tensor shapes through the computation.
3. **Patch the graph** by modifying initializer constants or clearing stale metadata.
4. **Verify the patch** by running inference with ONNX Runtime in Python.
5. **Apply learnings to C#** — update session options, length parameters, etc.

The `onnx` Python library provides full read/write access to the ONNX protobuf graph, making it the right tool for this job. The values and structure discovered here are then hard-coded or configured in the C# adapter (`ParakeetTdtAdapter.cs`).

## Setup

```bash
# From the repository root
pip install -r scripts/onnx/requirements.txt
```

Requires Python 3.10+.

## Scripts

### 1. `inspect_model.py` — Understand the model

Prints the model's metadata, I/O shapes, length-computation formula, self-attention structure, positional encoding info, and — critically — the `Slice_3` axis values used in the `rel_shift` operation.

```bash
python scripts/onnx/inspect_model.py

# Or point to a specific model
python scripts/onnx/inspect_model.py --model-path E:/models-cache/parakeet-tdt-0.6b/onnx/encoder.onnx
```

**Key output to look for:**

```
SLICE_3 AXIS VALUES (rel_shift diagnostic)
  /layers.0/self_attn/Constant_104_output_0 = [2]  [OK]
```

If the value is `[3]` instead of `[2]`, the model has the rel_shift bug.

### 2. `patch_encoder.py` — Fix the model

Patches the `Slice_3` axis from `3` to `2` across all 24 Conformer layers, clears stale shape annotations, backs up the original, and saves the fixed model.

```bash
# Dry run (diagnose only, no files changed)
python scripts/onnx/patch_encoder.py --dry-run

# Apply the patch (backs up original as encoder_original_buggy.onnx)
python scripts/onnx/patch_encoder.py

# Patch a model in a different directory
python scripts/onnx/patch_encoder.py --model-dir E:/models-cache/parakeet-tdt-0.6b/onnx/
```

### 3. `patch_decoder.py` — Remove stale decoder output

The decoder export contains a `prednet_lengths` output with no producing node.
ONNX Runtime rejects the model unless that output is removed.

```bash
# Dry run (diagnose only)
python scripts/onnx/patch_decoder.py --dry-run

# Apply patch
python scripts/onnx/patch_decoder.py

# Patch a model in a different directory
python scripts/onnx/patch_decoder.py --model-dir E:/models-cache/parakeet-tdt-0.6b/onnx/
```

### 4. `extract_vocab.py` — Generate vocab.txt

Extracts a SentencePiece vocabulary from a tokenizer model (e.g. `tokenizer.model`).

```bash
# Default model location
python scripts/onnx/extract_vocab.py

# Different model directory
python scripts/onnx/extract_vocab.py --model-dir E:/models-cache/parakeet-tdt-0.6b
```

### 5. `verify_encoder.py` — Confirm the fix

Runs the encoder with synthetic inputs of varying sizes and (optionally) with a real recorded WAV file to confirm no dimension-mismatch errors.

```bash
python scripts/onnx/verify_encoder.py

# With a specific WAV file
python scripts/onnx/verify_encoder.py --wav path/to/recording.wav
```

**Expected output:**

```
SYNTHETIC INPUT TESTS
  frames=   64  ->  output=(1, 1024, 8)   enc_len=[8]  OK
  frames=  128  ->  output=(1, 1024, 16)  enc_len=[16] OK
  frames=  280  ->  output=(1, 1024, 35)  enc_len=[35] OK
  ...
ALL TESTS PASSED
```

## The Bug

The Parakeet-TDT 0.6B ONNX export has a bug in the **relative positional encoding** (`rel_shift`) of the Conformer self-attention layers.

### What happens in `rel_shift`

In each of the 24 Conformer layers, the self-attention computes both content scores and positional scores. The positional scores need a "relative shift" to align positions correctly:

```
MatMul output (positional):  [B, 8, T, 2T-1]
Pad:                         [B, 8, T, 2T]       # add 1 column
Reshape:                     [B, 8, 2T, T]       # swap dims
Slice_1 (axis=2, start=1):  [B, 8, 2T-1, T]     # remove first row
Slice_3 (axis=?, end=T):    [B, 8, T, T]         # trim to T rows  ← THE BUG
```

### The bug

`Slice_3` should slice on **axis 2** (the `2T-1` dimension) to produce `[B, 8, T, T]`. But the buggy export sets axis to **3**, which slices the last dimension (already `T`) and leaves the `2T-1` dimension intact → `[B, 8, 2T-1, T]`.

When `Add_2` tries to add:

- Content scores: `[B, 8, T, T]`
- Positional scores: `[B, 8, 2T-1, T]`  ← wrong shape

→ **Broadcast error**: `"Attempting to broadcast an axis by a dimension other than 1. T by 2T-1"`

### The fix

Change the shared axis constant (`Constant_104`) from `[3]` to `[2]`. This single constant is used by all 24 layers' `Slice_3` nodes.

### C# side effects

After patching the ONNX model:

1. **Length parameter** — should be `paddedFrames` (the time dimension of the mel-spectrogram input tensor). No sample-count conversion needed.
2. **Memory pattern** — `SessionOptions.EnableMemoryPattern` must be `false` because ONNX Runtime's pre-allocated buffers are sized for the old (buggy) shapes. This is set in `ParakeetTdtAdapter.CreateSessionOptions()`.

## Workflow Summary

```
┌─────────────────────────────────────────────────────────┐
│  Python (offline, one-time)                             │
│                                                         │
│  1. inspect_model.py  → discover bug (axis=3)           │
│  2. patch_encoder.py  → fix ONNX graph (axis→2)        │
│  3. verify_encoder.py → confirm inference works         │
└──────────────────┬──────────────────────────────────────┘
                   │  values & settings
                   ▼
┌─────────────────────────────────────────────────────────┐
│  C# (runtime)                                           │
│                                                         │
│  ParakeetTdtAdapter.cs                                  │
│    • length param = paddedFrames                         │
│    • EnableMemoryPattern = false                         │
│    • loads the patched encoder.onnx                      │
└─────────────────────────────────────────────────────────┘
```
