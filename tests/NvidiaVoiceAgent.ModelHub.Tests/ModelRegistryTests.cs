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
    public void GetModel_Asr_ReturnsEncoderOnnxModel()
    {
        // Arrange - Note: The HuggingFace repo only has the standard encoder.onnx
        // (no int8 quantized variant available)
        var registry = new ModelRegistry(CreateOptions());

        // Act
        var model = registry.GetModel(ModelType.Asr);

        // Assert
        model.Should().NotBeNull();
        model!.Filename.Should().Be("onnx/encoder.onnx");
    }

    [Fact]
    public void GetModel_Asr_HasRequiredDataFile()
    {
        // Arrange - The encoder model is split into encoder.onnx + encoder.onnx_data
        var registry = new ModelRegistry(CreateOptions());

        // Act
        var model = registry.GetModel(ModelType.Asr);

        // Assert
        model.Should().NotBeNull();
        model!.AdditionalFiles.Should().Contain("onnx/encoder.onnx_data");
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
        model.AdditionalFiles.Should().Contain("onnx/encoder.onnx_data");
        model.AdditionalFiles.Should().Contain("onnx/decoder.onnx");
        model.AdditionalFiles.Should().HaveCount(2);
    }
}
