using Microsoft.AspNetCore.Mvc;
using NvidiaVoiceAgent.Core.Services;
using NvidiaVoiceAgent.ModelHub;
using NvidiaVoiceAgent.Models;

namespace NvidiaVoiceAgent.Controllers;

/// <summary>
/// API controller for health and system status endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly IAsrService _asrService;
    private readonly ILlmService? _llmService;
    private readonly IModelDownloadService _modelDownloadService;

    public HealthController(
        IAsrService asrService,
        ILlmService? llmService,
        IModelDownloadService modelDownloadService)
    {
        _asrService = asrService;
        _llmService = llmService;
        _modelDownloadService = modelDownloadService;
    }

    /// <summary>
    /// Get system health status including model availability.
    /// </summary>
    /// <returns>Health status information</returns>
    [HttpGet]
    [ProducesResponseType(typeof(HealthStatus), 200)]
    public IActionResult GetHealth()
    {
        return Ok(new HealthStatus
        {
            Status = "healthy",
            AsrLoaded = _asrService.IsModelLoaded,
            AsrDownloaded = _modelDownloadService.IsModelAvailable(ModelType.Asr),
            TtsLoaded = false,
            LlmLoaded = _llmService?.IsModelLoaded ?? false,
            Timestamp = DateTime.UtcNow
        });
    }
}
