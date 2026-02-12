using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NvidiaVoiceAgent.ModelHub;

namespace NvidiaVoiceAgent.ModelHub.Tests;

/// <summary>
/// Tests for ConsoleProgressReporter.
/// </summary>
public class ConsoleProgressReporterTests
{
    [Fact]
    public void Report_WithKnownTotalBytes_DoesNotThrow()
    {
        // Arrange
        var reporter = new ConsoleProgressReporter(NullLogger<ConsoleProgressReporter>.Instance);
        var progress = new DownloadProgress
        {
            ModelName = "TestModel",
            FileName = "model.onnx",
            BytesDownloaded = 50_000_000,
            TotalBytes = 100_000_000
        };

        // Act & Assert
        var act = () => reporter.Report(progress);
        act.Should().NotThrow();
    }

    [Fact]
    public void Report_WithUnknownTotalBytes_DoesNotThrow()
    {
        // Arrange
        var reporter = new ConsoleProgressReporter(NullLogger<ConsoleProgressReporter>.Instance);
        var progress = new DownloadProgress
        {
            ModelName = "TestModel",
            FileName = "model.onnx",
            BytesDownloaded = 50_000_000,
            TotalBytes = -1
        };

        // Act & Assert
        var act = () => reporter.Report(progress);
        act.Should().NotThrow();
    }

    [Fact]
    public void OnDownloadStarted_WithKnownSize_DoesNotThrow()
    {
        // Arrange
        var reporter = new ConsoleProgressReporter(NullLogger<ConsoleProgressReporter>.Instance);

        // Act & Assert
        var act = () => reporter.OnDownloadStarted("TestModel", 100_000_000);
        act.Should().NotThrow();
    }

    [Fact]
    public void OnDownloadStarted_WithUnknownSize_DoesNotThrow()
    {
        // Arrange
        var reporter = new ConsoleProgressReporter(NullLogger<ConsoleProgressReporter>.Instance);

        // Act & Assert
        var act = () => reporter.OnDownloadStarted("TestModel", -1);
        act.Should().NotThrow();
    }

    [Fact]
    public void OnDownloadCompleted_Success_DoesNotThrow()
    {
        // Arrange
        var reporter = new ConsoleProgressReporter(NullLogger<ConsoleProgressReporter>.Instance);

        // Act & Assert
        var act = () => reporter.OnDownloadCompleted("TestModel", success: true);
        act.Should().NotThrow();
    }

    [Fact]
    public void OnDownloadCompleted_Failure_DoesNotThrow()
    {
        // Arrange
        var reporter = new ConsoleProgressReporter(NullLogger<ConsoleProgressReporter>.Instance);

        // Act & Assert
        var act = () => reporter.OnDownloadCompleted("TestModel", success: false);
        act.Should().NotThrow();
    }
}
