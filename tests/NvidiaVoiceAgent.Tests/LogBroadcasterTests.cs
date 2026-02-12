using FluentAssertions;
using NvidiaVoiceAgent.Services;

namespace NvidiaVoiceAgent.Tests;

/// <summary>
/// Tests for ILogBroadcaster client management.
/// Uses a test implementation since LogBroadcaster isn't implemented yet.
/// </summary>
public class LogBroadcasterTests
{
    [Fact]
    public void RegisterClient_AddsClient()
    {
        // Arrange
        var broadcaster = new TestLogBroadcaster();

        // Act
        broadcaster.RegisterClient("client-1");

        // Assert
        broadcaster.ClientCount.Should().Be(1);
    }

    [Fact]
    public void RegisterClient_HandlesDuplicateId()
    {
        // Arrange
        var broadcaster = new TestLogBroadcaster();

        // Act
        broadcaster.RegisterClient("client-1");
        broadcaster.RegisterClient("client-1");

        // Assert - should handle gracefully (no exception)
        broadcaster.ClientCount.Should().Be(1);
    }

    [Fact]
    public void UnregisterClient_RemovesClient()
    {
        // Arrange
        var broadcaster = new TestLogBroadcaster();
        broadcaster.RegisterClient("client-1");

        // Act
        broadcaster.UnregisterClient("client-1");

        // Assert
        broadcaster.ClientCount.Should().Be(0);
    }

    [Fact]
    public void UnregisterClient_HandlesNonExistentClient()
    {
        // Arrange
        var broadcaster = new TestLogBroadcaster();

        // Act & Assert - should not throw
        var act = () => broadcaster.UnregisterClient("non-existent");
        act.Should().NotThrow();
    }

    [Fact]
    public async Task BroadcastLog_WithNoClients_Succeeds()
    {
        // Arrange
        var broadcaster = new TestLogBroadcaster();

        // Act & Assert - should not throw
        await broadcaster.BroadcastLogAsync("Test message");
    }

    [Fact]
    public async Task BroadcastLog_RecordsMessage()
    {
        // Arrange
        var broadcaster = new TestLogBroadcaster();
        broadcaster.RegisterClient("client-1");

        // Act
        await broadcaster.BroadcastLogAsync("Test message", "info");

        // Assert
        broadcaster.BroadcastedMessages.Should().ContainSingle()
            .Which.Should().Be(("Test message", "info"));
    }

    [Fact]
    public async Task BroadcastLog_DefaultsToInfoLevel()
    {
        // Arrange
        var broadcaster = new TestLogBroadcaster();
        broadcaster.RegisterClient("client-1");

        // Act
        await broadcaster.BroadcastLogAsync("Test message");

        // Assert
        broadcaster.BroadcastedMessages.Should().ContainSingle()
            .Which.Item2.Should().Be("info");
    }

    [Fact]
    public void MultipleClients_AreTrackedCorrectly()
    {
        // Arrange
        var broadcaster = new TestLogBroadcaster();

        // Act
        broadcaster.RegisterClient("client-1");
        broadcaster.RegisterClient("client-2");
        broadcaster.RegisterClient("client-3");

        // Assert
        broadcaster.ClientCount.Should().Be(3);

        // Act - remove one
        broadcaster.UnregisterClient("client-2");

        // Assert
        broadcaster.ClientCount.Should().Be(2);
    }

    /// <summary>
    /// Test implementation of ILogBroadcaster.
    /// Replace with actual LogBroadcaster once implemented.
    /// </summary>
    private class TestLogBroadcaster : ILogBroadcaster
    {
        private readonly HashSet<string> _clients = new();
        private readonly List<(string Message, string Level)> _messages = new();

        public int ClientCount => _clients.Count;
        public List<(string Message, string Level)> BroadcastedMessages => _messages;

        public Task BroadcastLogAsync(string message, string level = "info")
        {
            if (_clients.Count > 0)
            {
                _messages.Add((message, level));
            }
            return Task.CompletedTask;
        }

        public void RegisterClient(string connectionId)
        {
            _clients.Add(connectionId);
        }

        public void UnregisterClient(string connectionId)
        {
            _clients.Remove(connectionId);
        }
    }
}
