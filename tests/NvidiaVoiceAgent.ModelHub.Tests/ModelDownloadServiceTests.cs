using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NvidiaVoiceAgent.ModelHub;

namespace NvidiaVoiceAgent.ModelHub.Tests;

/// <summary>
/// Tests for ModelDownloadService (unit tests without network calls).
/// </summary>
public class ModelDownloadServiceTests
{
    private static ModelDownloadService CreateService(
        ModelHubOptions? options = null,
        IModelRegistry? registry = null)
    {
        var opts = Options.Create(options ?? new ModelHubOptions { AutoDownload = false });
        registry ??= new ModelRegistry(opts);
        var logger = NullLogger<ModelDownloadService>.Instance;
        var progressLogger = NullLogger<ConsoleProgressReporter>.Instance;
        var progressReporter = new ConsoleProgressReporter(progressLogger);

        return new ModelDownloadService(logger, registry, progressReporter, opts);
    }

    [Fact]
    public void IsModelAvailable_WhenModelNotDownloaded_ReturnsFalse()
    {
        // Arrange
        var service = CreateService(new ModelHubOptions
        {
            ModelCachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
            AutoDownload = false
        });

        // Act
        var available = service.IsModelAvailable(ModelType.Asr);

        // Assert
        available.Should().BeFalse();
    }

    [Fact]
    public void GetModelPath_WhenModelNotDownloaded_ReturnsNull()
    {
        // Arrange
        var service = CreateService(new ModelHubOptions
        {
            ModelCachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
            AutoDownload = false
        });

        // Act
        var path = service.GetModelPath(ModelType.Asr);

        // Assert
        path.Should().BeNull();
    }

    [Fact]
    public void GetModelPath_WhenUnregisteredType_ReturnsNull()
    {
        // Arrange
        var service = CreateService(new ModelHubOptions
        {
            ModelCachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
            AutoDownload = false
        });

        // Act
        var path = service.GetModelPath(ModelType.Llm);

        // Assert
        path.Should().BeNull();
    }

    [Fact]
    public async Task EnsureModelsAsync_WhenAutoDownloadDisabled_ReturnsFailure()
    {
        // Arrange
        var service = CreateService(new ModelHubOptions
        {
            ModelCachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
            AutoDownload = false
        });

        // Act
        var results = await service.EnsureModelsAsync();

        // Assert
        results.Should().NotBeEmpty();
        results.Should().OnlyContain(r => !r.Success);
        results.First().ErrorMessage.Should().Contain("auto-download is disabled");
    }

    [Fact]
    public async Task DownloadModelAsync_WhenAutoDownloadDisabled_ReturnsNotDownloaded()
    {
        // Arrange
        var service = CreateService(new ModelHubOptions
        {
            ModelCachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
            AutoDownload = false
        });

        // Act - LLM is now registered but model files don't exist,
        // and auto-download is disabled so it won't try to fetch them.
        var result = await service.DownloadModelAsync(ModelType.Llm);

        // Assert - Download should fail (404 from HuggingFace or network error)
        // since the placeholder repo/filename doesn't exist as a real ONNX model
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task EnsureModelsAsync_SupportsCancellation()
    {
        // Arrange
        var service = CreateService(new ModelHubOptions
        {
            ModelCachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
            AutoDownload = true
        });
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.EnsureModelsAsync(cts.Token));
    }
}
