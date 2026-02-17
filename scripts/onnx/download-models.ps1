Param(
    [string]$BaseUrl = "http://localhost:5003",
    [switch]$IncludeOptional,
    [switch]$IncludePersonaPlex,
    [string[]]$Models
)

$ErrorActionPreference = "Stop"

function Write-Step($message) {
    Write-Host "`n==> $message" -ForegroundColor Cyan
}

$requiredModels = @(
    "Parakeet-TDT-0.6B-V2"
)

$optionalModels = @(
    "FastPitch-HiFiGAN-EN",
    "HiFiGAN-EN",
    "TinyLlama-1.1B-ONNX"
)

$personaPlexModel = "PersonaPlex-7B-v1"

if ($Models -and $Models.Count -gt 0) {
    $targetModels = $Models
}
else {
    $targetModels = @()
    $targetModels += $requiredModels

    if ($IncludeOptional) {
        $targetModels += $optionalModels
    }

    if ($IncludePersonaPlex) {
        $targetModels += $personaPlexModel
    }
}

Write-Step "Requesting model downloads from $BaseUrl"

foreach ($model in $targetModels) {
    try {
        Write-Host "Downloading: $model" -ForegroundColor Yellow
        $url = "$BaseUrl/api/models/$model/download"
        Invoke-RestMethod -Method Post -Uri $url | Out-Null
        Write-Host "Queued: $model" -ForegroundColor Green
    }
    catch {
        Write-Host "Failed: $model" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor DarkRed
    }
}

Write-Host "`nDone. Check the Models UI or logs for progress." -ForegroundColor Green
