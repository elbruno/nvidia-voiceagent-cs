Param(
    [string]$ModelCachePath = "model-cache",
    [switch]$SkipVerify
)

$ErrorActionPreference = "Stop"

function Write-Step($message) {
    Write-Host "`n==> $message" -ForegroundColor Cyan
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$requirements = Join-Path $repoRoot "scripts\onnx\requirements.txt"

$cachePath = Join-Path $repoRoot $ModelCachePath
$onnxDir = Join-Path $cachePath "parakeet-tdt-0.6b\onnx"
$modelDir = Join-Path $cachePath "parakeet-tdt-0.6b"

Write-Step "Preparing Parakeet-TDT ASR model"

if (-not (Test-Path $onnxDir)) {
    Write-Host "ERROR: $onnxDir not found." -ForegroundColor Red
    Write-Host "Download the model first via ModelHub or place it under $cachePath." -ForegroundColor Yellow
    exit 1
}

Write-Step "Installing Python dependencies"
python -m pip install -r $requirements

Write-Step "Patching encoder.onnx"
python (Join-Path $repoRoot "scripts\onnx\patch_encoder.py") --model-dir $onnxDir

Write-Step "Patching decoder.onnx"
python (Join-Path $repoRoot "scripts\onnx\patch_decoder.py") --model-dir $onnxDir

Write-Step "Generating vocab.txt"
python (Join-Path $repoRoot "scripts\onnx\extract_vocab.py") --model-dir $modelDir

if (-not $SkipVerify) {
    Write-Step "Verifying encoder"
    python (Join-Path $repoRoot "scripts\onnx\verify_encoder.py") --model-path (Join-Path $onnxDir "encoder.onnx")
}

Write-Host "`nModel preparation complete." -ForegroundColor Green
