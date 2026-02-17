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

// Note: PersonaPlex LLM service is registered in AddVoiceAgentCore()
// TODO: Register TTS service when implementation is ready
// builder.Services.AddSingleton<ITtsService, TtsService>();

// Add controllers for API endpoints
builder.Services.AddControllers();

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

// Map API controllers
app.MapControllers();

// Legacy health endpoint (for backward compatibility with old scripts/tests)
app.MapGet("/health", (IAsrService asrService, ILlmService? llmService, IModelDownloadService modelDownload) => new HealthStatus
{
    Status = "healthy",
    AsrLoaded = asrService.IsModelLoaded,
    AsrDownloaded = modelDownload.IsModelAvailable(ModelType.Asr),
    TtsLoaded = false,
    LlmLoaded = llmService?.IsModelLoaded ?? false,
    Timestamp = DateTime.UtcNow
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
