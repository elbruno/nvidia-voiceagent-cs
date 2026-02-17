# Model Preparation Guide (All Models)

This guide standardizes the **one-time model setup** required to run the NVIDIA Voice Agent on any machine. It covers:

- where to store model files
- how to download all models consistently
- how to patch the Parakeet-TDT ONNX graphs
- how to generate `vocab.txt`
- how to verify everything before running the app

> **Note:** Today, ASR (Parakeet-TDT) is fully wired. PersonaPlex, FastPitch, and HiFiGAN downloads are supported, but inference for those models is still in mock mode until the respective services are fully implemented.

---

## First-time setup (step-by-step)

1. **Set your model cache path**

  Decide where models should live (recommended: `model-cache/`) and update:

- `ModelHub:ModelCachePath`
- `ModelConfig:*Path` values

1. **Download models**

  Start the app or use the Models UI/API to download:

- **Required**: Parakeet-TDT (ASR)
- **Optional**: PersonaPlex, FastPitch, HiFiGAN, TinyLlama

  If you prefer a script, run:

  ```powershell
  .\scripts\onnx\download-models.ps1 -BaseUrl http://localhost:5003 -IncludeOptional
  ```

  To include PersonaPlex (requires HF token + license acceptance):

  ```powershell
  .\scripts\onnx\download-models.ps1 -BaseUrl http://localhost:5003 -IncludeOptional -IncludePersonaPlex
  ```

1. **Install Python helpers (one time)**

  ```bash
  pip install -r scripts/onnx/requirements.txt
  ```

1. **Patch Parakeet-TDT encoder**

  ```bash
  python scripts/onnx/patch_encoder.py --model-dir model-cache/parakeet-tdt-0.6b/onnx
  ```

1. **Patch Parakeet-TDT decoder**

  ```bash
  python scripts/onnx/patch_decoder.py --model-dir model-cache/parakeet-tdt-0.6b/onnx
  ```

1. **Generate vocab.txt**

  ```bash
  python scripts/onnx/extract_vocab.py --model-dir model-cache/parakeet-tdt-0.6b
  ```

1. **Verify encoder sanity**

  ```bash
  python scripts/onnx/verify_encoder.py --model-path model-cache/parakeet-tdt-0.6b/onnx/encoder.onnx
  ```

1. **Run the app**

  ```bash
  cd NvidiaVoiceAgent
  dotnet run
  ```

> **Windows shortcut:** You can run the full ASR preparation flow with:
>
> ```powershell
> .\scripts\onnx\prepare-models.ps1 -ModelCachePath model-cache
> ```

---

## 1) Choose a model storage layout

You can store models in **one place** and point the app to that location.

Recommended layout (ModelHub cache):

```
model-cache/
├── parakeet-tdt-0.6b/
├── fastpitch-en/
├── hifigan-en/
├── tinyllama-1.1b/
└── personaplex-7b/
```

### Configure paths

Update these settings in `appsettings.json` (or via environment variables):

- `ModelHub:ModelCachePath` → where downloads go
- `ModelConfig:*Path` → where runtime loads models from

A consistent setup is to point ModelConfig paths **into the cache**:

| Setting | Recommended value |
| --- | --- |
| `ModelConfig:AsrModelPath` | `model-cache/parakeet-tdt-0.6b` |
| `ModelConfig:FastPitchModelPath` | `model-cache/fastpitch-en` |
| `ModelConfig:HifiGanModelPath` | `model-cache/hifigan-en` |
| `ModelConfig:LlmModelPath` | `model-cache/tinyllama-1.1b` |
| `ModelConfig:PersonaPlexModelPath` | `model-cache/personaplex-7b` |

---

## 2) Download all models

### Option A — Auto-download on startup (recommended)

Set the following in `appsettings.json`:

```json
"ModelHub": {
  "AutoDownload": true,
  "ModelCachePath": "model-cache",
  "HuggingFaceToken": "hf_..."
}
```

Then start the app. Required models are downloaded on startup. Optional models can be downloaded from the **Models** UI or via API.

### Option B — Manual download

Use the **Models** UI or API endpoints to download each model on demand. PersonaPlex requires a HuggingFace token and license acceptance. See: `docs/guides/huggingface-token-setup.md`.

---

## 3) Prepare the Parakeet-TDT ASR model (required)

The Parakeet-TDT ONNX export requires **three one-time fixes** before it works correctly:

1. Patch the encoder `Slice_3` axis bug
2. Remove the decoder’s stale `prednet_lengths` output
3. Generate `vocab.txt` from SentencePiece tokenizer

### Install Python dependencies

```bash
pip install -r scripts/onnx/requirements.txt
```

### Patch the encoder

```bash
python scripts/onnx/patch_encoder.py --model-dir model-cache/parakeet-tdt-0.6b/onnx
```

### Patch the decoder

```bash
python scripts/onnx/patch_decoder.py --model-dir model-cache/parakeet-tdt-0.6b/onnx
```

### Generate vocab.txt

```bash
python scripts/onnx/extract_vocab.py --model-dir model-cache/parakeet-tdt-0.6b
```

### Verify encoder inference

```bash
python scripts/onnx/verify_encoder.py --model-path model-cache/parakeet-tdt-0.6b/onnx/encoder.onnx
```

**Required files after preparation:**

```
parakeet-tdt-0.6b/
├── model_spec.json
├── vocab.txt
└── onnx/
    ├── encoder.onnx
    ├── encoder.onnx_data
    └── decoder.onnx
```

---

## 4) Prepare PersonaPlex (LLM)

PersonaPlex is downloaded via ModelHub but still runs in **mock mode** until TorchSharp integration is complete.

To prepare:

1. Accept NVIDIA’s license on HuggingFace
2. Configure `ModelHub:HuggingFaceToken`
3. Download via UI or API

Expected files:

```
personaplex-7b/
├── model.safetensors
├── tokenizer-e351c8d8-checkpoint125.safetensors
├── tokenizer_spm_32k_3.model
└── voices.tgz
```

---

## 5) Prepare FastPitch + HiFiGAN (TTS)

These models are **downloadable** via ModelHub but **not yet wired** to runtime inference.

Recommended preparation:

- Download `fastpitch-en` and `hifigan-en` via ModelHub
- Keep them under the cache path shown above

Expected layout:

```
fastpitch-en/
└── model.onnx

hifigan-en/
└── model.onnx
```

---

## 6) Prepare TinyLlama (LLM)

TinyLlama is available for download via ModelHub, but inference integration is pending. You can still download and keep it ready:

```
tinyllama-1.1b/
├── model.onnx
├── model.onnx_data
├── tokenizer.json
├── tokenizer.model
└── config.json
```

---

## 7) Final verification checklist

✅ `ModelHub:ModelCachePath` points to the model cache directory

✅ `ModelConfig:*Path` values align with the cache layout

✅ Parakeet-TDT encoder and decoder are patched

✅ `vocab.txt` exists in the ASR model directory

✅ PersonaPlex/FastPitch/HiFiGAN/TinyLlama directories exist (if you want all models ready)

Once these are done, the app will load ASR properly on any machine and all other models will be pre-staged for future activation.
