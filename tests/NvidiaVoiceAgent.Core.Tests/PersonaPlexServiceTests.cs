using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NvidiaVoiceAgent.Core.Models;
using NvidiaVoiceAgent.Core.Services;
using NvidiaVoiceAgent.ModelHub;

namespace NvidiaVoiceAgent.Core.Tests;

/// <summary>
/// Tests for PersonaPlexService implementation.
/// These tests verify the service behavior in mock mode (without actual model).
/// </summary>
public class PersonaPlexServiceTests
{
    private static PersonaPlexService CreateService(
        ModelConfig? config = null,
        IModelDownloadService? modelDownloadService = null)
    {
        var logger = NullLogger<PersonaPlexService>.Instance;
        
        var modelConfig = config ?? new ModelConfig();
        var options = Options.Create(modelConfig);
        
        return new PersonaPlexService(logger, options, modelDownloadService);
    }

    [Fact]
    public void Constructor_InitializesWithDefaultVoice()
    {
        // Arrange & Act
        var service = CreateService();

        // Assert
        service.CurrentVoice.Should().Be("voice_0");
        service.IsModelLoaded.Should().BeFalse();
    }

    [Fact]
    public void Constructor_InitializesWithConfiguredVoice()
    {
        // Arrange
        var config = new ModelConfig { PersonaPlexVoice = "voice_5" };

        // Act
        var service = CreateService(config);

        // Assert
        service.CurrentVoice.Should().Be("voice_5");
    }

    [Fact]
    public void SetVoice_UpdatesCurrentVoice()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.SetVoice("voice_3");

        // Assert
        service.CurrentVoice.Should().Be("voice_3");
    }

    [Fact]
    public void SetVoice_WithEmptyString_KeepsCurrentVoice()
    {
        // Arrange
        var service = CreateService();
        var originalVoice = service.CurrentVoice;

        // Act
        service.SetVoice("");

        // Assert
        service.CurrentVoice.Should().Be(originalVoice);
    }

    [Fact]
    public async Task LoadModelAsync_WithNoModel_EntersMockMode()
    {
        // Arrange
        var service = CreateService();

        // Act
        await service.LoadModelAsync();

        // Assert
        service.IsModelLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task LoadModelAsync_CalledMultipleTimes_LoadsOnlyOnce()
    {
        // Arrange
        var service = CreateService();

        // Act
        await service.LoadModelAsync();
        await service.LoadModelAsync();
        await service.LoadModelAsync();

        // Assert
        service.IsModelLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateResponseAsync_InMockMode_ReturnsSimulatedResponse()
    {
        // Arrange
        var service = CreateService();
        var prompt = "Hello, how are you?";

        // Act
        var response = await service.GenerateResponseAsync(prompt);

        // Assert
        response.Should().NotBeNullOrEmpty();
        response.Should().Contain(prompt);
    }

    [Fact]
    public async Task GenerateResponseAsync_LoadsModelLazily()
    {
        // Arrange
        var service = CreateService();
        service.IsModelLoaded.Should().BeFalse();

        // Act
        await service.GenerateResponseAsync("Test prompt");

        // Assert
        service.IsModelLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateSpeechResponseAsync_InMockMode_ReturnsSilence()
    {
        // Arrange
        var service = CreateService();
        var audioInput = new float[16000]; // 1 second at 16kHz

        // Act
        var audioOutput = await service.GenerateSpeechResponseAsync(audioInput);

        // Assert
        audioOutput.Should().NotBeNull();
        audioOutput.Should().HaveCount(24000); // PersonaPlex outputs at 24kHz
    }

    [Fact]
    public async Task GenerateSpeechResponseAsync_LoadsModelLazily()
    {
        // Arrange
        var service = CreateService();
        service.IsModelLoaded.Should().BeFalse();

        // Act
        await service.GenerateSpeechResponseAsync(new float[16000]);

        // Assert
        service.IsModelLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task ILlmService_GenerateResponseAsync_WorksViaCast()
    {
        // Arrange
        ILlmService llmService = CreateService();

        // Act
        var response = await llmService.GenerateResponseAsync("Test prompt");

        // Assert
        response.Should().NotBeNullOrEmpty();
        llmService.IsModelLoaded.Should().BeTrue();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert - should not throw
        service.Dispose();
        service.Dispose();
        service.Dispose();
    }

    [Fact]
    public async Task ConcurrentLoadModelAsync_LoadsOnlyOnce()
    {
        // Arrange
        var service = CreateService();

        // Act - simulate concurrent loading
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => service.LoadModelAsync()))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - model should be loaded exactly once
        service.IsModelLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateResponseAsync_WithCancellation_IsCancellable()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var act = async () => await service.GenerateResponseAsync("Test", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void SetVoice_WithNull_KeepsCurrentVoice()
    {
        // Arrange
        var service = CreateService();
        var originalVoice = service.CurrentVoice;

        // Act
        service.SetVoice(null!);

        // Assert
        service.CurrentVoice.Should().Be(originalVoice);
    }

    [Fact]
    public async Task GenerateResponseAsync_IncludesVoiceInMockResponse()
    {
        // Arrange
        var service = CreateService();
        service.SetVoice("voice_7");

        // Act
        var response = await service.GenerateResponseAsync("Hello");

        // Assert
        response.Should().Contain("voice_7");
    }
}
