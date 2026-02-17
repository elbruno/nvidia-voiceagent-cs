using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace NvidiaVoiceAgent.Tests;

/// <summary>
/// Tests for the /api/models endpoint to verify PersonaPlex model is registered.
/// </summary>
public class ModelsEndpointTests : IClassFixture<WebApplicationFactoryFixture>
{
    private readonly HttpClient _client;

    public ModelsEndpointTests(WebApplicationFactoryFixture factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ModelsEndpoint_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/models");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ModelsEndpoint_ReturnsJsonArray()
    {
        // Act
        var response = await _client.GetAsync("/api/models");
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        // Assert
        json.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task ModelsEndpoint_IncludesPersonaPlex()
    {
        // Act
        var response = await _client.GetAsync("/api/models");
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        // Assert
        var models = json.RootElement.EnumerateArray();
        var personaPlex = models.FirstOrDefault(m => 
            m.TryGetProperty("name", out var name) && 
            name.GetString() == "PersonaPlex-7B-v1");

        personaPlex.ValueKind.Should().NotBe(JsonValueKind.Undefined);
    }

    [Fact]
    public async Task ModelsEndpoint_PersonaPlexHasCorrectProperties()
    {
        // Act
        var response = await _client.GetAsync("/api/models");
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        // Assert
        var models = json.RootElement.EnumerateArray();
        var personaPlex = models.FirstOrDefault(m => 
            m.TryGetProperty("name", out var name) && 
            name.GetString() == "PersonaPlex-7B-v1");

        personaPlex.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        
        // Check required properties
        personaPlex.TryGetProperty("type", out var type).Should().BeTrue();
        type.GetString().Should().Be("PersonaPlex");

        personaPlex.TryGetProperty("repoId", out var repoId).Should().BeTrue();
        repoId.GetString().Should().Be("nvidia/personaplex-7b-v1");

        personaPlex.TryGetProperty("status", out var status).Should().BeTrue();
        status.GetString().Should().Be("not_downloaded");

        personaPlex.TryGetProperty("isRequired", out var isRequired).Should().BeTrue();
        isRequired.GetBoolean().Should().BeFalse();

        personaPlex.TryGetProperty("isAvailableForDownload", out var isAvailable).Should().BeTrue();
        isAvailable.GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ModelsEndpoint_ReturnsAllFiveModelTypes()
    {
        // Act
        var response = await _client.GetAsync("/api/models");
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        // Assert - should have 5 models (Asr, Tts, Vocoder, Llm, PersonaPlex)
        var models = json.RootElement.EnumerateArray().ToList();
        models.Should().HaveCount(5);

        var modelTypes = models.Select(m => m.GetProperty("type").GetString()).ToList();
        modelTypes.Should().Contain(new[] { "Asr", "Tts", "Vocoder", "Llm", "PersonaPlex" });
    }

    [Fact]
    public async Task ModelsEndpoint_PersonaPlexHasExpectedSize()
    {
        // Act
        var response = await _client.GetAsync("/api/models");
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        // Assert
        var models = json.RootElement.EnumerateArray();
        var personaPlex = models.FirstOrDefault(m => 
            m.TryGetProperty("name", out var name) && 
            name.GetString() == "PersonaPlex-7B-v1");

        personaPlex.TryGetProperty("expectedSizeMb", out var sizeMb).Should().BeTrue();
        var size = sizeMb.GetDouble();
        
        // PersonaPlex is ~16.7 GB = ~17,000 MB
        size.Should().BeGreaterThan(15000);
        size.Should().BeLessThan(20000);
    }
}
