using System.Text.Json;
using FluentAssertions;
using NvidiaVoiceAgent.Models;

namespace NvidiaVoiceAgent.Tests;

/// <summary>
/// Tests for configuration message parsing.
/// </summary>
public class ConfigMessageTests
{
    [Fact]
    public void VoiceMessage_DeserializesType()
    {
        // Arrange
        var json = """{"type": "config"}""";

        // Act
        var message = JsonSerializer.Deserialize<VoiceMessage>(json);

        // Assert
        message.Should().NotBeNull();
        message!.Type.Should().Be("config");
    }

    [Fact]
    public void VoiceMessage_DeserializesMode()
    {
        // Arrange
        var json = """{"type": "config", "mode": "smart"}""";

        // Act
        var message = JsonSerializer.Deserialize<VoiceMessage>(json);

        // Assert
        message.Should().NotBeNull();
        message!.Mode.Should().Be("smart");
    }

    [Fact]
    public void VoiceMessage_HandlesNullMode()
    {
        // Arrange
        var json = """{"type": "config"}""";

        // Act
        var message = JsonSerializer.Deserialize<VoiceMessage>(json);

        // Assert
        message.Should().NotBeNull();
        message!.Mode.Should().BeNull();
    }

    [Fact]
    public void VoiceMessage_SerializesCorrectly()
    {
        // Arrange
        var message = new VoiceMessage
        {
            Type = "config",
            Mode = "echo"
        };

        // Act
        var json = JsonSerializer.Serialize(message);
        var doc = JsonDocument.Parse(json);

        // Assert
        doc.RootElement.GetProperty("type").GetString().Should().Be("config");
        doc.RootElement.GetProperty("mode").GetString().Should().Be("echo");
    }

    [Theory]
    [InlineData("echo")]
    [InlineData("smart")]
    public void VoiceMode_ParsesValidModes(string modeValue)
    {
        // Arrange
        var json = $$$"""{"type": "config", "mode": "{{{modeValue}}}"}""";

        // Act
        var message = JsonSerializer.Deserialize<VoiceMessage>(json);
        var mode = Enum.TryParse<VoiceMode>(message?.Mode, ignoreCase: true, out var parsed);

        // Assert
        mode.Should().BeTrue();
        parsed.Should().BeOneOf(VoiceMode.Echo, VoiceMode.Smart);
    }

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

    [Fact]
    public void HealthStatus_SerializesWithSnakeCasePropertyNames()
    {
        // Arrange
        var status = new HealthStatus
        {
            Status = "healthy",
            AsrLoaded = true,
            TtsLoaded = false,
            LlmLoaded = true
        };

        // Act
        var json = JsonSerializer.Serialize(status);
        var doc = JsonDocument.Parse(json);

        // Assert
        doc.RootElement.TryGetProperty("asr_loaded", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("tts_loaded", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("llm_loaded", out _).Should().BeTrue();
    }

    [Fact]
    public void LogEntry_HasCorrectDefaults()
    {
        // Arrange & Act
        var entry = new LogEntry();

        // Assert
        entry.Level.Should().Be("info");
        entry.Message.Should().BeEmpty();
        entry.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void LogEntry_SerializesCorrectly()
    {
        // Arrange
        var entry = new LogEntry
        {
            Level = "error",
            Message = "Test error message"
        };

        // Act
        var json = JsonSerializer.Serialize(entry);
        var doc = JsonDocument.Parse(json);

        // Assert
        doc.RootElement.GetProperty("level").GetString().Should().Be("error");
        doc.RootElement.GetProperty("message").GetString().Should().Be("Test error message");
    }
}
