using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NvidiaVoiceAgent.Tests;

/// <summary>
/// Tests for the /ws/voice WebSocket endpoint.
/// </summary>
public class VoiceWebSocketTests : IClassFixture<WebApplicationFactoryFixture>
{
    private readonly WebApplicationFactoryFixture _factory;

    public VoiceWebSocketTests(WebApplicationFactoryFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task VoiceEndpoint_AcceptsWebSocketConnection()
    {
        // Arrange
        var client = _factory.Server.CreateWebSocketClient();

        // Act
        var webSocket = await client.ConnectAsync(
            new Uri(_factory.Server.BaseAddress, "/ws/voice"),
            CancellationToken.None);

        // Assert
        webSocket.State.Should().Be(WebSocketState.Open);

        // Cleanup
        await webSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test complete",
            CancellationToken.None);
    }

    [Fact]
    public async Task VoiceEndpoint_RejectsNonWebSocketRequest()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/ws/voice");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task VoiceEndpoint_CanSendBinaryAudio()
    {
        // Arrange
        var client = _factory.Server.CreateWebSocketClient();
        var webSocket = await client.ConnectAsync(
            new Uri(_factory.Server.BaseAddress, "/ws/voice"),
            CancellationToken.None);

        // Create minimal WAV header (44 bytes) + some audio data
        var audioData = CreateMinimalWavData();

        // Act
        await webSocket.SendAsync(
            audioData,
            WebSocketMessageType.Binary,
            endOfMessage: true,
            CancellationToken.None);

        // Assert - connection should still be open after sending
        webSocket.State.Should().Be(WebSocketState.Open);

        // Cleanup
        await webSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test complete",
            CancellationToken.None);
    }

    [Fact]
    public async Task VoiceEndpoint_HandlesClientClose()
    {
        // Arrange
        var client = _factory.Server.CreateWebSocketClient();
        var webSocket = await client.ConnectAsync(
            new Uri(_factory.Server.BaseAddress, "/ws/voice"),
            CancellationToken.None);

        // Act
        await webSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Client closing",
            CancellationToken.None);

        // Assert
        webSocket.State.Should().Be(WebSocketState.Closed);
    }

    [Fact]
    public async Task LogsEndpoint_AcceptsWebSocketConnection()
    {
        // Arrange
        var client = _factory.Server.CreateWebSocketClient();

        // Act
        var webSocket = await client.ConnectAsync(
            new Uri(_factory.Server.BaseAddress, "/ws/logs"),
            CancellationToken.None);

        // Assert
        webSocket.State.Should().Be(WebSocketState.Open);

        // Cleanup
        await webSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test complete",
            CancellationToken.None);
    }

    [Fact]
    public async Task LogsEndpoint_SendsWelcomeMessage()
    {
        // Arrange
        var client = _factory.Server.CreateWebSocketClient();
        var webSocket = await client.ConnectAsync(
            new Uri(_factory.Server.BaseAddress, "/ws/logs"),
            CancellationToken.None);
        var buffer = new byte[1024];

        // Act
        var result = await webSocket.ReceiveAsync(buffer, CancellationToken.None);
        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

        // Assert
        result.MessageType.Should().Be(WebSocketMessageType.Text);
        message.Should().Contain("Connected to log stream");

        // Cleanup
        await webSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test complete",
            CancellationToken.None);
    }

    private static byte[] CreateMinimalWavData()
    {
        // Create a minimal valid WAV file with silence
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        int sampleRate = 16000;
        short bitsPerSample = 16;
        short channels = 1;
        int dataLength = sampleRate * 2; // 1 second of audio

        // RIFF header
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength); // file size - 8
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // chunk size
        writer.Write((short)1); // PCM format
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8); // byte rate
        writer.Write((short)(channels * bitsPerSample / 8)); // block align
        writer.Write(bitsPerSample);

        // data chunk
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]); // silence

        return ms.ToArray();
    }
}
