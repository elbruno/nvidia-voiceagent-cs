# HuggingFace Token Setup Guide for PersonaPlex

## Overview

NVIDIA's PersonaPlex-7B-v1 is a gated model on HuggingFace, which means you need to:

1. Accept NVIDIA's license agreement
2. Authenticate with a HuggingFace token

This guide walks you through the complete setup process.

---

## Why is a Token Required?

PersonaPlex-7B-v1 is a **gated model** on HuggingFace for several reasons:

- **License Agreement**: You must accept NVIDIA's terms of use
- **Access Control**: Ensures users acknowledge the model's capabilities and limitations
- **Usage Tracking**: Helps NVIDIA understand model adoption and usage

Without a valid HuggingFace token, attempts to download PersonaPlex will fail with authentication errors.

---

## Step-by-Step Setup

### 1. Create a HuggingFace Account

If you don't have one already:

1. Visit [https://huggingface.co/join](https://huggingface.co/join)
2. Sign up with your email
3. Verify your email address

### 2. Accept the PersonaPlex License

1. Navigate to the PersonaPlex model page:  
   **[https://huggingface.co/nvidia/personaplex-7b-v1](https://huggingface.co/nvidia/personaplex-7b-v1)**

2. Read NVIDIA's license agreement carefully

3. Click **"Agree and access repository"** button

   > ⚠️ **Important**: You must accept the license before generating a token. Downloads will fail even with a valid token if you haven't accepted the license.

### 3. Generate an Access Token

1. Go to your HuggingFace settings:  
   **[https://huggingface.co/settings/tokens](https://huggingface.co/settings/tokens)**

2. Click **"New token"** button

3. Configure your token:
   - **Name**: `nvidia-voiceagent-personaplex` (or any descriptive name)
   - **Type**: Select **"Read"** (write access is not needed)
   - **Scope**: Leave default (all repositories)

4. Click **"Generate token"**

5. **Copy the token immediately** — it will only be shown once!  
   Format: `hf_XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX`

### 4. Configure Your Token

You have three options for configuring your HuggingFace token, listed from most secure to least secure:

#### Option 1: User Secrets (Recommended for Development)

The most secure way for local development is to use .NET User Secrets:

1. Navigate to the project directory:

   ```bash
   cd NvidiaVoiceAgent
   ```

2. Initialize user secrets (if not already done):

   ```bash
   dotnet user-secrets init
   ```

3. Set your HuggingFace token:

   ```bash
   dotnet user-secrets set "ModelHub:HuggingFaceToken" "hf_your_actual_token_here"
   ```

4. (Optional) Set a model cache path (recommended for large models):

   ```bash
   dotnet user-secrets set "ModelHub:ModelCachePath" "E:\\models-cache"
   ```

5. Verify it was set correctly:

   ```bash
   dotnet user-secrets list
   ```

**Benefits:**

- Token is stored outside the project directory
- Never accidentally committed to Git
- Specific to your user account on your machine
- Automatically loaded in Development environment

    "ModelCachePath": "E:\\models-cache",
    "HuggingFaceToken": "hf_your_actual_token_here"
- **Windows**: `%APPDATA%\Microsoft\UserSecrets\nvidia-voiceagent-cs-secrets\secrets.json`
- **Linux/macOS**: `~/.microsoft/usersecrets/nvidia-voiceagent-cs-secrets/secrets.json`

#### Option 2: Environment Variables (Recommended for Production)

For production deployments, use environment variables:

**Linux/macOS:**

```bash
export ModelHub__HuggingFaceToken="hf_your_token_here"
export ModelHub__ModelCachePath="/opt/model-cache"
dotnet run
```

**Windows (PowerShell):**

```powershell
$env:ModelHub__HuggingFaceToken="hf_your_token_here"
$env:ModelHub__ModelCachePath="E:\\models-cache"
dotnet run
```

**Docker:**

```yaml
environment:
  - ModelHub__HuggingFaceToken=hf_your_token_here
```

#### Option 3: Configuration File (Least Secure)

> ⚠️ **Warning**: Only use this for testing. Never commit tokens to source control!

Create `appsettings.Development.json` from the example:

```bash
cp appsettings.Development.json.example appsettings.Development.json
```

Then edit and replace `"hf_your_actual_token_here"` with your actual token:

> ✅ **Note**: User Secrets and Environment Variables override appsettings values in Development.

```json
{
  "ModelHub": {
    "HuggingFaceToken": "hf_your_actual_token_here"
  }
}
```

**Note:** This file is gitignored and will not be committed.

---

## Security Best Practices

### ✅ Recommended: User Secrets (Development)

For local development, **always use User Secrets** as described in Option 1 above. This ensures:

- Tokens are never in your project directory
- No risk of accidental commits
- Easy to manage per-developer configuration

### ⚠️ Configuration Files are Now Gitignored

As of the latest version, the following files are excluded from Git:

- `appsettings.json`
- `appsettings.*.json` (except the example file)

This prevents accidental token commits. Use User Secrets or Environment Variables instead.

### 🔒 Use Azure Key Vault (Enterprise)

For enterprise deployments:

```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri("https://your-keyvault.vault.azure.net/"),
    new DefaultAzureCredential());
```

Store token as a secret named `ModelHub--HuggingFaceToken`.

---

## Downloading PersonaPlex

### Via Web UI

1. Start the application:

   ```bash
   cd NvidiaVoiceAgent
   dotnet run
   ```

2. Open browser: `http://localhost:5003`

3. Navigate to **Models** panel

4. Find **PersonaPlex-7B-v1**

5. Click **Download** button

6. Monitor progress (download is ~17 GB, may take 10-30 minutes depending on connection)

### Via API

Using curl:

```bash
curl -X POST http://localhost:5003/api/models/PersonaPlex-7B-v1/download
```

Using PowerShell:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5003/api/models/PersonaPlex-7B-v1/download"
```

### Via Code

```csharp
var modelDownloadService = app.Services.GetRequiredService<IModelDownloadService>();
var result = await modelDownloadService.DownloadModelAsync(ModelType.PersonaPlex);

if (result.Success)
{
    Console.WriteLine($"Downloaded to: {result.ModelPath}");
}
else
{
    Console.WriteLine($"Error: {result.ErrorMessage}");
}
```

---

## Troubleshooting

### ❌ "401 Unauthorized" Error

**Cause**: Invalid or missing token

**Solution**:

1. Verify token is correctly copied to `appsettings.json`
2. Ensure token starts with `hf_`
3. Check token hasn't been revoked in HuggingFace settings
4. Regenerate token if needed

### ❌ "403 Forbidden" Error

**Cause**: License not accepted

**Solution**:

1. Visit [https://huggingface.co/nvidia/personaplex-7b-v1](https://huggingface.co/nvidia/personaplex-7b-v1)
2. Click "Agree and access repository"
3. Try download again

### ❌ Download Stops at 0%

**Cause**: Network issues or server-side rate limiting

**Solution**:

1. Check internet connection
2. Wait a few minutes and retry
3. If persistent, try downloading during off-peak hours

### ❌ "Model not found" After Download

**Cause**: Files downloaded to wrong location

**Solution**:

1. Check `ModelHub:ModelCachePath` in `appsettings.json`
2. Verify files exist in `model-cache/personaplex-7b/`
3. Expected files:
   - `model.safetensors` (~16.7 GB)
   - `tokenizer-e351c8d8-checkpoint125.safetensors` (~385 MB)
   - `tokenizer_spm_32k_3.model` (~553 KB)
   - `voices.tgz` (~6.1 MB)

### ❌ "There is not enough space on the disk"

**Cause**: The download drive does not have enough free space.

**Solution**:

1. Free at least 20–25 GB on the target drive
2. Or set `ModelHub:ModelCachePath` to a drive with more space

---

## Verifying Installation

### Check Model Status via API

```bash
curl http://localhost:5003/api/models
```

Look for PersonaPlex entry:

```json
{
  "name": "PersonaPlex-7B-v1",
  "type": "PersonaPlex",
  "status": "downloaded",
  "localPath": "/path/to/model-cache/personaplex-7b",
  "expectedSizeMb": 17015.63,
  "isRequired": false,
  "isAvailableForDownload": true,
  "requiresAuthentication": true
}
```

### Check Health Endpoint

```bash
curl http://localhost:5003/health
```

Should show:

```json
{
  "status": "healthy",
  "asrLoaded": true,
  "asrDownloaded": true,
  "ttsLoaded": false,
  "llmLoaded": true,
  "timestamp": "2026-02-17T02:00:00.000Z"
}
```

`llmLoaded: true` indicates PersonaPlex is loaded.

### Check Application Logs

Look for:

```
[Information] PersonaPlex-7B-v1 loaded successfully
[Information] Using voice: default
```

---

## Model Details

### PersonaPlex-7B-v1 Specifications

| Property | Value |
|----------|-------|
| **Parameters** | 7 billion |
| **Architecture** | Moshi-based dual-stream transformer |
| **Context Length** | 4,096 tokens |
| **Sampling Rate** | 24 kHz audio |
| **Latency** | ~170ms time-to-first-token |
| **Voices** | 18 pre-packaged personas |
| **License** | NVIDIA Proprietary (gated) |

### Available Voices

PersonaPlex includes 18 distinct voice personas:

- Professional/Business voices
- Casual/Conversational styles
- Various age ranges and accents
- Male and female voices

Custom voice cloning is also supported (requires additional setup).

---

## Additional Resources

- **PersonaPlex Model Card**: [https://huggingface.co/nvidia/personaplex-7b-v1](https://huggingface.co/nvidia/personaplex-7b-v1)
- **NVIDIA NIM Documentation**: [https://docs.nvidia.com/nim/](https://docs.nvidia.com/nim/)
- **HuggingFace Token Docs**: [https://huggingface.co/docs/hub/security-tokens](https://huggingface.co/docs/hub/security-tokens)
- **Project README**: `../README.md`

---

## Support

If you encounter issues:

1. Check the **Troubleshooting** section above
2. Review application logs for detailed error messages
3. Verify token permissions on HuggingFace
4. Open an issue on the project repository

---

**Last Updated**: February 2026  
**Version**: 1.0
