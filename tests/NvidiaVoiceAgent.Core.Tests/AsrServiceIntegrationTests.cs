using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NvidiaVoiceAgent.Core.Models;
using NvidiaVoiceAgent.Core.Services;
using Xunit.Abstractions;

namespace NvidiaVoiceAgent.Core.Tests;

public class AsrServiceIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public AsrServiceIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TranscribeAsync_WithRealModel_DoesNotCrash()
    {
        // Arrange
        var modelPath = FindRealModelPath();
        if (modelPath == null)
        {
            _output.WriteLine("Skipped: No model found.");
            // We return here, passing success.
            return;
        }

        _output.WriteLine($"Using model: {modelPath}");

        var config = new ModelConfig
        {
            AsrModelPath = modelPath,
            UseGpu = false // Force CPU for consistent testing environment
        };

        var logger = new TestLogger<AsrService>(_output);
        using var service = new AsrService(logger, Options.Create(config));

        // Create 0.25 seconds of silence (approx 4000 samples -> 25 frames)
        // This matches the error observed: 13 by 25
        var audio = new float[4000];

        // Act
        // This is the critical step that was failing with "RuntimeException" due to tensor mismatch
        var result = await service.TranscribeAsync(audio);

        // Assert
        // We expect either empty string (silence) or valid text, but never "[Transcription error]"
        _output.WriteLine($"Result: {result}");
        result.Should().NotBeNull();

        // Explicit check to debug why assertion might be passing
        if (result.Contains("[Transcription error]"))
        {
            throw new Exception($"Test Failed: Service returned error: {result}");
        }

        result.Should().NotContain("[Transcription error]", "The service should handle tensor shapes correctly without crashing");
    }

    private string? FindRealModelPath()
    {
        // Start from current directory (bin/Debug/net10.0)
        var currentDir = Directory.GetCurrentDirectory();

        // Try to find the solution root
        var rootDir = FindSolutionRoot(currentDir);
        if (rootDir == null) return null;

        // Path to models in the main app project
        // Structure: NvidiaVoiceAgent/Models/parakeet-tdt-0.6b/
        var possiblePaths = new[]
        {
            Path.Combine(rootDir, "NvidiaVoiceAgent", "Models", "parakeet-tdt-0.6b", "onnx", "encoder.onnx"),
            Path.Combine(rootDir, "NvidiaVoiceAgent", "Models", "parakeet-tdt-0.6b", "encoder.onnx"),
            // Also check for the generic download location if used
            Path.Combine(rootDir, "models", "voice-model", "encoder.onnx")
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path)) return path;
        }

        // Fallback: search recursively for any encoder.onnx in the solution
        try
        {
            var files = Directory.GetFiles(rootDir, "encoder.onnx", SearchOption.AllDirectories);
            return files.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private string? FindSolutionRoot(string startPath)
    {
        var dir = new DirectoryInfo(startPath);
        while (dir != null)
        {
            if (dir.GetFiles("*.slnx").Any() || dir.GetFiles("*.sln").Any())
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }
}

public class TestLogger<T> : ILogger<T>, IDisposable
{
    private readonly ITestOutputHelper _output;

    public TestLogger(ITestOutputHelper output)
    {
        _output = output;
    }

    public IDisposable BeginScope<TState>(TState state) => this!;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            _output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
            if (exception != null)
            {
                _output.WriteLine($"Exception: {exception}");
            }
        }
        catch
        {
            // Ignore logging errors
        }
    }

    public void Dispose() { }
}