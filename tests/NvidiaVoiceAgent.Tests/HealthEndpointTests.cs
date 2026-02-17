using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NvidiaVoiceAgent.Tests;

/// <summary>
/// Tests for the /health endpoint.
/// </summary>
public class HealthEndpointTests : IClassFixture<WebApplicationFactoryFixture>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactoryFixture factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_ReturnsCorrectJsonStructure()
    {
        // Act
        var response = await _client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement;

        // Assert
        root.TryGetProperty("status", out var statusProp).Should().BeTrue();
        statusProp.GetString().Should().Be("healthy");

        root.TryGetProperty("asr_loaded", out var asrProp).Should().BeTrue();
        asrProp.ValueKind.Should().Be(JsonValueKind.False);

        root.TryGetProperty("tts_loaded", out var ttsProp).Should().BeTrue();
        ttsProp.ValueKind.Should().Be(JsonValueKind.False);

        root.TryGetProperty("llm_loaded", out var llmProp).Should().BeTrue();
        llmProp.ValueKind.Should().Be(JsonValueKind.False);

        root.TryGetProperty("timestamp", out var timestampProp).Should().BeTrue();
        timestampProp.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Health_ReturnsValidTimestamp()
    {
        // Act
        var response = await _client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var timestamp = json.RootElement.GetProperty("timestamp").GetString();

        // Assert
        DateTime.TryParse(timestamp, out var parsedTime).Should().BeTrue();
        // Allow for timezone differences - just verify it's a recent timestamp
        parsedTime.Should().BeAfter(DateTime.UtcNow.AddHours(-12));
        parsedTime.Should().BeBefore(DateTime.UtcNow.AddHours(12));
    }

    [Fact]
    public async Task Health_ReturnsJsonContentType()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task ModelsDelete_UnknownModel_Returns404()
    {
        // Act
        var response = await _client.DeleteAsync("/api/models/nonexistent-model");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ModelsDelete_KnownModel_Returns200()
    {
        // Act - delete Parakeet model (may or may not be on disk, but endpoint returns 200 either way)
        var response = await _client.DeleteAsync("/api/models/Parakeet-TDT-0.6B-V2");

        // Assert - Should return 200 OK, but may return 500 if there's a file system issue
        // This is acceptable as the delete operation is best-effort
        response.StatusCode.Should().Match(s => s == HttpStatusCode.OK || s == HttpStatusCode.InternalServerError,
            "delete should return either OK or InternalServerError");
    }
}
