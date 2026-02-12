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

// Register services
builder.Services.AddSingleton<ILogBroadcaster, LogBroadcaster>();
builder.Services.AddSingleton<IAudioProcessor, AudioProcessor>();

// Register WebSocket handlers
builder.Services.AddSingleton<VoiceWebSocketHandler>();
builder.Services.AddSingleton<LogsWebSocketHandler>();

// Register AI services
builder.Services.AddSingleton<IAsrService, AsrService>();
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
app.MapGet("/health", (IAsrService asrService) => new HealthStatus
{
    Status = "healthy",
    AsrLoaded = asrService.IsModelLoaded,
    TtsLoaded = false,  // TODO: Check actual service status
    LlmLoaded = false,
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
