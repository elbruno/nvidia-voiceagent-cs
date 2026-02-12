using FluentAssertions;
using NvidiaVoiceAgent.ModelHub;

namespace NvidiaVoiceAgent.ModelHub.Tests;

/// <summary>
/// Tests for DownloadProgress DTO.
/// </summary>
public class DownloadProgressTests
{
    [Fact]
    public void ProgressPercent_WithKnownTotal_ReturnsCorrectPercentage()
    {
        // Arrange
        var progress = new DownloadProgress
        {
            BytesDownloaded = 50,
            TotalBytes = 100
        };

        // Assert
        progress.ProgressPercent.Should().Be(50.0);
    }

    [Fact]
    public void ProgressPercent_WithUnknownTotal_ReturnsNegativeOne()
    {
        // Arrange
        var progress = new DownloadProgress
        {
            BytesDownloaded = 50,
            TotalBytes = -1
        };

        // Assert
        progress.ProgressPercent.Should().Be(-1);
    }

    [Fact]
    public void ProgressPercent_AtZeroProgress_ReturnsZero()
    {
        // Arrange
        var progress = new DownloadProgress
        {
            BytesDownloaded = 0,
            TotalBytes = 100
        };

        // Assert
        progress.ProgressPercent.Should().Be(0.0);
    }

    [Fact]
    public void ProgressPercent_AtComplete_ReturnsHundred()
    {
        // Arrange
        var progress = new DownloadProgress
        {
            BytesDownloaded = 100,
            TotalBytes = 100
        };

        // Assert
        progress.ProgressPercent.Should().Be(100.0);
    }
}
