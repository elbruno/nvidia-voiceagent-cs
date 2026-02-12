using NvidiaVoiceAgent.Core;
using NvidiaVoiceAgent.Core.Models;
using NvidiaVoiceAgent.Core.Services;
using NvidiaVoiceAgent.Hubs;
using NvidiaVoiceAgent.Models;
using NvidiaVoiceAgent.ModelHub;
using NvidiaVoiceAgent.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure model settings from appsettings.json
builder.Services.Configure<ModelConfig>(
    builder.Configuration.GetSection("ModelConfig"));

// Register ModelHub services for automatic model downloading
builder.Services.AddModelHub(options =>
{
    var section = builder.Configuration.GetSection(ModelHubOptions.SectionName);
    options.AutoDownload = section.GetValue("AutoDownload", true);
    options.UseInt8Quantization = section.GetValue("UseInt8Quantization", true);
    options.ModelCachePath = section.GetValue("ModelCachePath", "models") ?? "models";
    options.HuggingFaceToken = section.GetValue<string?>("HuggingFaceToken", null);
});

// Register core voice agent services (ASR, AudioProcessor) from NvidiaVoiceAgent.Core
builder.Services.AddVoiceAgentCore();

// Register services
builder.Services.AddSingleton<ILogBroadcaster, LogBroadcaster>();

// Override the default ConsoleProgressReporter with WebProgressReporter
// to broadcast download progress to WebSocket clients
builder.Services.AddSingleton<IProgressReporter, WebProgressReporter>();

// Register WebSocket handlers
builder.Services.AddSingleton<VoiceWebSocketHandler>();
builder.Services.AddSingleton<LogsWebSocketHandler>();

// TODO: Register remaining AI services when implementations are ready
// builder.Services.AddSingleton<ITtsService, TtsService>();
// builder.Services.AddSingleton<ILlmService, LlmService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Download required models on startup (if auto-download is enabled)
var modelDownloadService = app.Services.GetRequiredService<IModelDownloadService>();
await modelDownloadService.EnsureModelsAsync();

// Enable WebSockets
app.UseWebSockets();

// Serve static files from wwwroot (browser UI)
app.UseDefaultFiles();
app.UseStaticFiles();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Health check endpoint
app.MapGet("/health", (IAsrService asrService, IModelDownloadService modelDownload) => new HealthStatus
{
    Status = "healthy",
    AsrLoaded = asrService.IsModelLoaded,
    AsrDownloaded = modelDownload.IsModelAvailable(ModelType.Asr),
    TtsLoaded = false,  // TODO: Check actual service status
    LlmLoaded = false,
    Timestamp = DateTime.UtcNow
});

// Models status endpoint - returns detailed info for each registered model
app.MapGet("/api/models", (IModelRegistry registry, IModelDownloadService modelDownload) =>
{
    var models = registry.GetAllModels();
    var results = models.Select(m =>
    {
        var isAvailable = modelDownload.IsModelAvailable(m.Type);
        var localPath = modelDownload.GetModelPath(m.Type);
        return new ModelStatusResponse
        {
            Name = m.Name,
            Type = m.Type.ToString(),
            Status = isAvailable ? "downloaded" : "not_downloaded",
            RepoId = m.RepoId,
            LocalPath = localPath != null ? Path.GetFullPath(localPath) : null,
            ExpectedSizeMb = m.ExpectedSizeBytes / (1024.0 * 1024.0),
            IsRequired = m.IsRequired,
            IsAvailableForDownload = m.IsAvailableForDownload
        };
    }).ToList();
    return results;
});

// Trigger download for a specific model by name
app.MapPost("/api/models/{name}/download", async (string name, IModelRegistry registry, IModelDownloadService modelDownload) =>
{
    var allModels = registry.GetAllModels();
    var model = allModels.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (model == null)
    {
        return Results.NotFound(new { error = $"Model '{name}' not found." });
    }

    if (!model.IsAvailableForDownload)
    {
        return Results.Json(new { error = $"Model '{name}' is not yet available for download (coming soon)." }, statusCode: 400);
    }

    var result = await modelDownload.DownloadModelAsync(model.Type);
    if (result.Success)
    {
        return Results.Ok(new { message = $"Model '{name}' downloaded successfully.", path = result.ModelPath });
    }
    return Results.Json(new { error = result.ErrorMessage }, statusCode: 500);
});

// WebSocket endpoint for voice processing
app.Map("/ws/voice", async (HttpContext context, VoiceWebSocketHandler handler) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await handler.HandleAsync(webSocket, context.RequestAborted);
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

// WebSocket endpoint for log streaming
app.Map("/ws/logs", async (HttpContext context, LogsWebSocketHandler handler) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await handler.HandleAsync(webSocket, context.RequestAborted);
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

app.Run();

// Make Program class accessible for integration tests
public partial class Program { }
