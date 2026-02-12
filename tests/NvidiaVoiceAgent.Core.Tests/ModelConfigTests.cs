using FluentAssertions;
using NvidiaVoiceAgent.Core.Models;

namespace NvidiaVoiceAgent.Core.Tests;

/// <summary>
/// Tests for ModelConfig defaults.
/// </summary>
public class ModelConfigTests
{
    [Fact]
    public void ModelConfig_HasCorrectDefaults()
    {
        // Arrange & Act
        var config = new ModelConfig();

        // Assert
        config.AsrModelPath.Should().Be("models/parakeet-tdt-0.6b");
        config.FastPitchModelPath.Should().Be("models/fastpitch");
        config.HifiGanModelPath.Should().Be("models/hifigan");
        config.LlmModelPath.Should().Be("models/tinyllama");
        config.UseGpu.Should().BeTrue();
        config.Use4BitQuantization.Should().BeTrue();
    }
}
