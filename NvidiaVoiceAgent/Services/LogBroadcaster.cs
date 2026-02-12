using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NvidiaVoiceAgent.Models;

namespace NvidiaVoiceAgent.Services;

/// <summary>
/// Service for broadcasting log messages to connected WebSocket clients.
/// Maintains a thread-safe set of connected clients and handles disconnections gracefully.
/// </summary>
public class LogBroadcaster : ILogBroadcaster
{
    private readonly ILogger<LogBroadcaster> _logger;
    private readonly ConcurrentDictionary<string, WebSocket> _clients = new();

    public LogBroadcaster(ILogger<LogBroadcaster> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Register a new WebSocket client for log broadcasting.
    /// </summary>
    public void RegisterClient(string connectionId, WebSocket webSocket)
    {
        _clients.TryAdd(connectionId, webSocket);
        _logger.LogDebug("Registered log client: {ConnectionId}. Total clients: {Count}", connectionId, _clients.Count);
    }

    /// <inheritdoc />
    public void RegisterClient(string connectionId)
    {
        // Legacy interface method - WebSocket must be registered via RegisterClient(string, WebSocket)
        _logger.LogWarning("RegisterClient called without WebSocket reference for {ConnectionId}", connectionId);
    }

    /// <summary>
    /// Unregister a WebSocket client.
    /// </summary>
    public void UnregisterClient(string connectionId)
    {
        _clients.TryRemove(connectionId, out _);
        _logger.LogDebug("Unregistered log client: {ConnectionId}. Total clients: {Count}", connectionId, _clients.Count);
    }

    /// <inheritdoc />
    public async Task BroadcastLogAsync(string message, string level = "info")
    {
        if (_clients.IsEmpty)
            return;

        var logEntry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Message = message
        };

        var json = JsonSerializer.Serialize(logEntry);
        var bytes = Encoding.UTF8.GetBytes(json);

        var disconnectedClients = new List<string>();

        foreach (var (connectionId, webSocket) in _clients)
        {
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.SendAsync(
                        bytes,
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        CancellationToken.None);
                }
                else
                {
                    disconnectedClients.Add(connectionId);
                }
            }
            catch (WebSocketException)
            {
                disconnectedClients.Add(connectionId);
            }
            catch (ObjectDisposedException)
            {
                disconnectedClients.Add(connectionId);
            }
        }

        // Clean up disconnected clients
        foreach (var connectionId in disconnectedClients)
        {
            UnregisterClient(connectionId);
        }
    }

    /// <summary>
    /// Get the current number of connected clients.
    /// </summary>
    public int ClientCount => _clients.Count;
}
