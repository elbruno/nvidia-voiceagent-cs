using FluentAssertions;
using Microsoft.Extensions.Options;
using NvidiaVoiceAgent.ModelHub;

namespace NvidiaVoiceAgent.ModelHub.Tests;

/// <summary>
/// Tests for ModelRegistry model definitions.
/// </summary>
public class ModelRegistryTests
{
    private static IOptions<ModelHubOptions> CreateOptions(Action<ModelHubOptions>? configure = null)
    {
        var options = new ModelHubOptions();
        configure?.Invoke(options);
        return Options.Create(options);
    }

    [Fact]
    public void GetAllModels_ReturnsRegisteredModels()
    {
        // Arrange
        var registry = new ModelRegistry(CreateOptions());

        // Act
        var models = registry.GetAllModels();

        // Assert
        models.Should().NotBeEmpty();
    }

    [Fact]
    public void GetModel_Asr_ReturnsAsrModel()
    {
        // Arrange
        var registry = new ModelRegistry(CreateOptions());

        // Act
        var model = registry.GetModel(ModelType.Asr);

        // Assert
        model.Should().NotBeNull();
        model!.Type.Should().Be(ModelType.Asr);
        model.Name.Should().NotBeNullOrEmpty();
        model.RepoId.Should().NotBeNullOrEmpty();
        model.Filename.Should().NotBeNullOrEmpty();
        model.LocalDirectory.Should().NotBeNullOrEmpty();
        model.IsRequired.Should().BeTrue();
    }

    [Fact]
    public void GetModel_Asr_WithInt8_ReturnsInt8Model()
    {
        // Arrange
        var registry = new ModelRegistry(CreateOptions(o => o.UseInt8Quantization = true));

        // Act
        var model = registry.GetModel(ModelType.Asr);

        // Assert
        model.Should().NotBeNull();
        model!.Filename.Should().Contain("int8");
    }

    [Fact]
    public void GetModel_Asr_WithoutInt8_ReturnsFullModel()
    {
        // Arrange
        var registry = new ModelRegistry(CreateOptions(o => o.UseInt8Quantization = false));

        // Act
        var model = registry.GetModel(ModelType.Asr);

        // Assert
        model.Should().NotBeNull();
        model!.Filename.Should().NotContain("int8");
    }

    [Fact]
    public void GetModel_UnregisteredType_ReturnsNull()
    {
        // Arrange
        var registry = new ModelRegistry(CreateOptions());

        // Act
        var model = registry.GetModel(ModelType.Llm);

        // Assert
        model.Should().BeNull();
    }

    [Fact]
    public void GetRequiredModels_ReturnsOnlyRequiredModels()
    {
        // Arrange
        var registry = new ModelRegistry(CreateOptions());

        // Act
        var required = registry.GetRequiredModels();

        // Assert
        required.Should().NotBeEmpty();
        required.Should().OnlyContain(m => m.IsRequired);
    }

    [Fact]
    public void AsrModel_HasExpectedRepository()
    {
        // Arrange
        var registry = new ModelRegistry(CreateOptions());

        // Act
        var model = registry.GetModel(ModelType.Asr);

        // Assert
        model.Should().NotBeNull();
        model!.RepoId.Should().Be("onnx-community/parakeet-tdt-0.6b-v2-ONNX");
    }

    [Fact]
    public void AsrModel_HasPositiveExpectedSize()
    {
        // Arrange
        var registry = new ModelRegistry(CreateOptions());

        // Act
        var model = registry.GetModel(ModelType.Asr);

        // Assert
        model.Should().NotBeNull();
        model!.ExpectedSizeBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AsrModel_HasAdditionalFiles()
    {
        // Arrange
        var registry = new ModelRegistry(CreateOptions());

        // Act
        var model = registry.GetModel(ModelType.Asr);

        // Assert
        model.Should().NotBeNull();
        model!.AdditionalFiles.Should().NotBeEmpty();
        model.AdditionalFiles.Should().Contain("config.json");
    }
}
