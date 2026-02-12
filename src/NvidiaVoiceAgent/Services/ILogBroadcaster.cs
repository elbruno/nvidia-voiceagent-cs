namespace NvidiaVoiceAgent.Services;

/// <summary>
/// Service for broadcasting log messages to connected WebSocket clients.
/// </summary>
public interface ILogBroadcaster
{
    /// <summary>
    /// Broadcast a log message to all connected log clients.
    /// </summary>
    /// <param name="message">Log message to broadcast</param>
    /// <param name="level">Log level (info, warning, error)</param>
    Task BroadcastLogAsync(string message, string level = "info");

    /// <summary>
    /// Register a new log client connection.
    /// </summary>
    void RegisterClient(string connectionId);

    /// <summary>
    /// Unregister a log client connection.
    /// </summary>
    void UnregisterClient(string connectionId);
}
