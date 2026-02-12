using FluentAssertions;
using NvidiaVoiceAgent.ModelHub;

namespace NvidiaVoiceAgent.ModelHub.Tests;

/// <summary>
/// Tests for ModelHubOptions defaults.
/// </summary>
public class ModelHubOptionsTests
{
    [Fact]
    public void Defaults_AutoDownloadIsTrue()
    {
        var options = new ModelHubOptions();
        options.AutoDownload.Should().BeTrue();
    }

    [Fact]
    public void Defaults_UseInt8QuantizationIsTrue()
    {
        var options = new ModelHubOptions();
        options.UseInt8Quantization.Should().BeTrue();
    }

    [Fact]
    public void Defaults_ModelCachePathIsModels()
    {
        var options = new ModelHubOptions();
        options.ModelCachePath.Should().Be("models");
    }

    [Fact]
    public void Defaults_HuggingFaceTokenIsNull()
    {
        var options = new ModelHubOptions();
        options.HuggingFaceToken.Should().BeNull();
    }

    [Fact]
    public void SectionName_IsModelHub()
    {
        ModelHubOptions.SectionName.Should().Be("ModelHub");
    }
}
