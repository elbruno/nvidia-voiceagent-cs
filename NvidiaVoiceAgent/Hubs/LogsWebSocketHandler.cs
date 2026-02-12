using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NvidiaVoiceAgent.Models;
using NvidiaVoiceAgent.Services;

namespace NvidiaVoiceAgent.Hubs;

/// <summary>
/// WebSocket handler for log broadcasting (/ws/logs).
/// Streams real-time log messages to connected clients.
/// </summary>
public class LogsWebSocketHandler
{
    private readonly ILogger<LogsWebSocketHandler> _logger;
    private readonly ILogBroadcaster _logBroadcaster;

    public LogsWebSocketHandler(ILogger<LogsWebSocketHandler> logger, ILogBroadcaster logBroadcaster)
    {
        _logger = logger;
        _logBroadcaster = logBroadcaster;
    }

    /// <summary>
    /// Handle an incoming WebSocket connection for log streaming.
    /// </summary>
    public async Task HandleAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var connectionId = Guid.NewGuid().ToString();
        _logger.LogInformation("Logs WebSocket connection established: {ConnectionId}", connectionId);

        // Register with the broadcaster
        if (_logBroadcaster is LogBroadcaster broadcaster)
        {
            broadcaster.RegisterClient(connectionId, webSocket);
        }

        try
        {
            // Send initial connection message
            var welcomeEntry = new LogEntry
            {
                Level = "info",
                Message = "Connected to log stream",
                Timestamp = DateTime.UtcNow
            };
            var welcomeJson = JsonSerializer.Serialize(welcomeEntry);
            var welcomeBytes = Encoding.UTF8.GetBytes(welcomeJson);
            await webSocket.SendAsync(
                welcomeBytes,
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);

            // Keep connection alive until client disconnects
            var buffer = new byte[1024];
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(buffer, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Client closed connection",
                        cancellationToken);
                    break;
                }
            }
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "WebSocket error during log streaming");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Logs WebSocket connection cancelled");
        }
        finally
        {
            // Unregister from the broadcaster
            if (_logBroadcaster is LogBroadcaster logBroadcaster)
            {
                logBroadcaster.UnregisterClient(connectionId);
            }
        }

        _logger.LogInformation("Logs WebSocket connection closed: {ConnectionId}", connectionId);
    }
}
