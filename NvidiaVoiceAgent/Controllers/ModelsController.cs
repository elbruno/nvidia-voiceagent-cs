using Microsoft.AspNetCore.Mvc;
using NvidiaVoiceAgent.ModelHub;
using NvidiaVoiceAgent.Models;

namespace NvidiaVoiceAgent.Controllers;

/// <summary>
/// API controller for model management operations.
/// Handles listing, downloading, and deleting AI models.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ModelsController : ControllerBase
{
    private readonly IModelRegistry _registry;
    private readonly IModelDownloadService _downloadService;
    private readonly ILogger<ModelsController> _logger;

    public ModelsController(
        IModelRegistry registry,
        IModelDownloadService downloadService,
        ILogger<ModelsController> logger)
    {
        _registry = registry;
        _downloadService = downloadService;
        _logger = logger;
    }

    /// <summary>
    /// Get all registered models with their current status.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ModelStatusResponse>), 200)]
    public IActionResult GetAllModels()
    {
        var allModels = _registry.GetAllModels();
        var modelStatuses = allModels.Select(model =>
        {
            var isAvailable = _downloadService.IsModelAvailable(model.Type);
            var localPath = _downloadService.GetModelPath(model.Type);
            
            return new ModelStatusResponse
            {
                Name = model.Name,
                Type = model.Type.ToString(),
                Status = isAvailable ? "downloaded" : "not_downloaded",
                RepoId = model.RepoId,
                LocalPath = localPath != null ? Path.GetFullPath(localPath) : null,
                ExpectedSizeMb = model.ExpectedSizeBytes / (1024.0 * 1024.0),
                IsRequired = model.IsRequired,
                IsAvailableForDownload = model.IsAvailableForDownload,
                RequiresAuthentication = model.Type == ModelType.PersonaPlex
            };
        }).ToList();

        return Ok(modelStatuses);
    }

    /// <summary>
    /// Download a specific model by name.
    /// </summary>
    /// <param name="name">Model name (e.g., "PersonaPlex-7B-v1")</param>
    [HttpPost("{name}/download")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> DownloadModel(string name)
    {
        var allModels = _registry.GetAllModels();
        var modelToDownload = allModels.FirstOrDefault(m => 
            m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (modelToDownload == null)
        {
            return NotFound(new { error = $"Model '{name}' not found in registry." });
        }

        if (!modelToDownload.IsAvailableForDownload)
        {
            return BadRequest(new 
            { 
                error = $"Model '{name}' is not yet available for download.",
                message = "This model is marked as coming soon."
            });
        }

        _logger.LogInformation("Starting download for model: {ModelName}", name);
        
        var downloadResult = await _downloadService.DownloadModelAsync(modelToDownload.Type);
        
        if (downloadResult.Success)
        {
            return Ok(new 
            { 
                message = $"Model '{name}' downloaded successfully.", 
                path = downloadResult.ModelPath 
            });
        }

        return StatusCode(500, new { error = downloadResult.ErrorMessage });
    }

    /// <summary>
    /// Delete a model and all its associated files.
    /// </summary>
    /// <param name="name">Model name to delete</param>
    [HttpDelete("{name}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public IActionResult DeleteModel(string name)
    {
        var allModels = _registry.GetAllModels();
        var modelToDelete = allModels.FirstOrDefault(m => 
            m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (modelToDelete == null)
        {
            return NotFound(new { error = $"Model '{name}' not found in registry." });
        }

        var wasDeleted = _downloadService.DeleteModel(modelToDelete.Type);
        
        if (wasDeleted)
        {
            _logger.LogInformation("Model deleted: {ModelName}", name);
            return Ok(new { message = $"Model '{name}' deleted successfully." });
        }

        return Ok(new { message = $"Model '{name}' was not found on disk (already deleted)." });
    }
}
