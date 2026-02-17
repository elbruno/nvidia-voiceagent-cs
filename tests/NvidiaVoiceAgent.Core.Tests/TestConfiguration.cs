using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NvidiaVoiceAgent.Core.Models;
using NvidiaVoiceAgent.Core.Services;

namespace NvidiaVoiceAgent.Core.Tests;

/// <summary>
/// Provides configuration for integration tests.
/// Loads settings from appsettings.Test.json (same structure as the main app).
/// </summary>
public class TestConfiguration
{
    private static readonly Lazy<TestConfiguration> _instance = new(() => new TestConfiguration());

    public static TestConfiguration Instance => _instance.Value;

    public IConfiguration Configuration { get; }
    public ModelConfig ModelConfig { get; }
    public ILogger<AsrService> AsrLogger { get; }
    public ILogger<PersonaPlexService> LlmLogger { get; }

    private TestConfiguration()
    {
        // Build configuration from appsettings.Test.json
        Configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Test.json", optional: false, reloadOnChange: false)
            .Build();

        // Load ModelConfig section
        ModelConfig = new ModelConfig();
        Configuration.GetSection("ModelConfig").Bind(ModelConfig);

        // Create loggers
        AsrLogger = NullLogger<AsrService>.Instance;
        LlmLogger = NullLogger<PersonaPlexService>.Instance;
    }

    /// <summary>
    /// Check if ASR model exists at the configured path.
    /// </summary>
    public bool AsrModelExists()
    {
        var encoderPath = Path.Combine(ModelConfig.AsrModelPath, "onnx", "encoder.onnx");
        return File.Exists(encoderPath);
    }

    /// <summary>
    /// Check if PersonaPlex LLM model exists at the configured path.
    /// </summary>
    public bool PersonaPlexModelExists()
    {
        var modelPath = Path.Combine(ModelConfig.PersonaPlexModelPath, "model.safetensors");
        return File.Exists(modelPath) || File.Exists(Path.Combine(ModelConfig.PersonaPlexModelPath, "model-00001-of-00002.safetensors"));
    }

    /// <summary>
    /// Get the absolute path to the model from a relative path.
    /// Resolves paths relative to the solution root (2 levels up from test bin directory).
    /// </summary>
    public string GetAbsoluteModelPath(string relativePath)
    {
        // Test bin directory: tests/NvidiaVoiceAgent.Core.Tests/bin/Debug/net10.0
        // Solution root: Go up to tests/, then up to solution root
        var binDir = AppContext.BaseDirectory;
        var testProjectDir = Path.GetFullPath(Path.Combine(binDir, "..", "..", ".."));
        var testsDir = Path.GetFullPath(Path.Combine(testProjectDir, ".."));
        var solutionRoot = Path.GetFullPath(Path.Combine(testsDir, ".."));

        return Path.GetFullPath(Path.Combine(solutionRoot, relativePath));
    }

    /// <summary>
    /// Create a logger for a specific type.
    /// </summary>
    public ILogger<T> CreateLogger<T>()
    {
        return NullLogger<T>.Instance;
    }
}
