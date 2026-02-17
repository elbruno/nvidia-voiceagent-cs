using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NvidiaVoiceAgent.Core.Models;
using NvidiaVoiceAgent.Core.Services;
using NvidiaVoiceAgent.Models;
using NvidiaVoiceAgent.Services;

namespace NvidiaVoiceAgent.Hubs;

/// <summary>
/// WebSocket handler for voice processing (/ws/voice).
/// Receives binary audio, processes through ASR → LLM → TTS pipeline,
/// returns synthesized audio response.
/// 
/// Supports two modes:
/// 1. Standard Mode: Send complete audio chunk, receive full response
/// 2. Realtime Mode (Phase 1): Continuous audio buffering with pause detection
/// </summary>
public class VoiceWebSocketHandler
{
    private readonly ILogger<VoiceWebSocketHandler> _logger;
    private readonly ILogBroadcaster? _logBroadcaster;
    private readonly IAsrService? _asrService;
    private readonly ITtsService? _ttsService;
    private readonly ILlmService? _llmService;
    private readonly IAudioProcessor? _audioProcessor;
    private readonly AudioStreamBuffer _audioStreamBuffer;
    private readonly IVoiceActivityDetector _vad;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class SessionLogWriter : IAsyncDisposable
    {
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _lock = new(1, 1);

        private SessionLogWriter(StreamWriter writer, string filePath)
        {
            _writer = writer;
            FilePath = filePath;
        }

        public string FilePath { get; }

        public static SessionLogWriter Create(string directory, string fileName)
        {
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, fileName);
            var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
            return new SessionLogWriter(writer, filePath);
        }

        public async Task WriteAsync(string level, string message)
        {
            var timestamp = DateTimeOffset.Now.ToString("O");
            await _lock.WaitAsync(CancellationToken.None);
            try
            {
                await _writer.WriteLineAsync($"{timestamp} [{level}] {message}");
            }
            finally
            {
                _lock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _writer.DisposeAsync();
            _lock.Dispose();
        }
    }

    public VoiceWebSocketHandler(
        ILogger<VoiceWebSocketHandler> logger,
        AudioStreamBuffer audioStreamBuffer,
        IVoiceActivityDetector vad,
        ILogBroadcaster? logBroadcaster = null,
        IAsrService? asrService = null,
        ITtsService? ttsService = null,
        ILlmService? llmService = null,
        IAudioProcessor? audioProcessor = null)
    {
        _logger = logger;
        _audioStreamBuffer = audioStreamBuffer;
        _vad = vad;
        _logBroadcaster = logBroadcaster;
        _asrService = asrService;
        _ttsService = ttsService;
        _llmService = llmService;
        _audioProcessor = audioProcessor;
    }

    /// <summary>
    /// Handle an incoming WebSocket connection for voice processing.
    /// Supports both standard and realtime conversation modes.
    /// </summary>
    public async Task HandleAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        SessionLogWriter? sessionLog = null;
        try
        {
            var sessionId = Guid.NewGuid().ToString("N");
            var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs", "sessions");
            var logFileName = $"voice-session_{DateTimeOffset.Now:yyyyMMdd_HHmmss}_{sessionId}.log";
            sessionLog = SessionLogWriter.Create(logDirectory, logFileName);
            _logger.LogInformation("Session log file: {Path}", sessionLog.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create session log file");
        }

        _logger.LogInformation("Voice WebSocket connection established");
        await BroadcastLogAsync("Voice WebSocket connection established", "info", sessionLog);

        if (sessionLog != null)
        {
            await sessionLog.WriteAsync("info", $"Session log file: {sessionLog.FilePath}");
            await BroadcastLogAsync($"Session log file: {sessionLog.FilePath}", "info", sessionLog);
        }

        var sessionState = new VoiceSessionState();
        var buffer = new byte[256 * 1024]; // 256KB buffer for audio chunks
        var messageBuffer = new MemoryStream();

        // Realtime mode: subscribe to pause detection events
        EventHandler? pauseHandler = null;
        if (sessionState.RealtimeMode)
        {
            pauseHandler = async (s, e) =>
            {
                var pendingAudio = _audioStreamBuffer.GetAndClear();
                if (pendingAudio.Length > 0)
                {
                    await ProcessAudioChunkAsync(webSocket, pendingAudio, sessionState, sessionLog, cancellationToken);
                }
            };
            _audioStreamBuffer.OnPauseDetected += pauseHandler;
        }

        try
        {
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

                // Accumulate message fragments
                messageBuffer.Write(buffer, 0, result.Count);

                if (!result.EndOfMessage)
                    continue;

                var messageData = messageBuffer.ToArray();
                messageBuffer.SetLength(0);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    await HandleTextMessageAsync(webSocket, messageData, sessionState, sessionLog, cancellationToken);
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    await HandleBinaryMessageAsync(webSocket, messageData, sessionState, sessionLog, cancellationToken);
                }
            }
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "WebSocket error during voice processing");
            if (sessionLog != null)
            {
                await sessionLog.WriteAsync("warn", $"WebSocket error during voice processing: {ex.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Voice WebSocket connection cancelled");
            if (sessionLog != null)
            {
                await sessionLog.WriteAsync("info", "Voice WebSocket connection cancelled");
            }
        }
        finally
        {
            // Cleanup realtime mode handler
            if (pauseHandler != null)
            {
                _audioStreamBuffer.OnPauseDetected -= pauseHandler;
            }
            _audioStreamBuffer.Clear();

            if (sessionLog != null)
            {
                await sessionLog.WriteAsync("info", "Voice WebSocket connection closed");
                await sessionLog.DisposeAsync();
            }
        }

        _logger.LogInformation("Voice WebSocket connection closed");
        await BroadcastLogAsync("Voice WebSocket connection closed", "info", null);
    }

    private async Task HandleTextMessageAsync(
        WebSocket webSocket,
        byte[] messageData,
        VoiceSessionState sessionState,
        SessionLogWriter? sessionLog,
        CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetString(messageData);
        _logger.LogDebug("Received text message: {Json}", json);

        try
        {
            var message = JsonSerializer.Deserialize<ConfigMessage>(json, JsonOptions);
            if (message == null) return;

            switch (message.Type?.ToLowerInvariant())
            {
                case "config":
                    await HandleConfigMessageAsync(message, sessionState, sessionLog);
                    break;

                case "clear_history":
                    sessionState.ChatHistory.Clear();
                    _logger.LogInformation("Chat history cleared");
                    await BroadcastLogAsync("Chat history cleared", "info", sessionLog);
                    break;

                default:
                    _logger.LogWarning("Unknown message type: {Type}", message.Type);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON message");
            if (sessionLog != null)
            {
                await sessionLog.WriteAsync("warn", $"Failed to parse JSON message: {ex.Message}");
            }
        }
    }

    private async Task HandleConfigMessageAsync(ConfigMessage message, VoiceSessionState sessionState, SessionLogWriter? sessionLog)
    {
        if (message.SmartMode.HasValue)
        {
            sessionState.SmartMode = message.SmartMode.Value;
            _logger.LogInformation("Smart mode set to: {SmartMode}", sessionState.SmartMode);
            await BroadcastLogAsync($"Smart mode set to: {sessionState.SmartMode}", "info", sessionLog);
        }

        if (!string.IsNullOrEmpty(message.SmartModel))
        {
            sessionState.SmartModel = message.SmartModel;
            _logger.LogInformation("Smart model set to: {SmartModel}", sessionState.SmartModel);
            await BroadcastLogAsync($"Smart model set to: {sessionState.SmartModel}", "info", sessionLog);
        }

        // Support for realtime mode config (added in Phase 1)
        // Example: { "type": "config", "realtimeMode": true, "pauseThresholdMs": 800 }
        var configJson = JsonSerializer.Serialize(message, JsonOptions);
        using var configDoc = JsonDocument.Parse(configJson);
        var root = configDoc.RootElement;

        if (root.TryGetProperty("realtimeMode", out var realtimeModeElem) && realtimeModeElem.ValueKind == JsonValueKind.True)
        {
            sessionState.RealtimeMode = true;
            _logger.LogInformation("Realtime conversation mode enabled");
            await BroadcastLogAsync("✨ Realtime conversation mode enabled", "info", sessionLog);
        }

        if (root.TryGetProperty("pauseThresholdMs", out var thresholdElem) && thresholdElem.TryGetInt32(out int threshold))
        {
            sessionState.PauseThresholdMs = threshold;
            _logger.LogInformation("Pause threshold set to: {Threshold}ms", threshold);
            await BroadcastLogAsync($"Pause threshold: {threshold}ms", "info", sessionLog);
        }
    }

    private async Task HandleBinaryMessageAsync(
        WebSocket webSocket,
        byte[] audioData,
        VoiceSessionState sessionState,
        SessionLogWriter? sessionLog,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Received {Bytes} bytes of audio data", audioData.Length);
        await BroadcastLogAsync($"Received {audioData.Length} bytes of audio data", "info", sessionLog);

        try
        {
            // Step 1: Decode WAV to float samples
            float[] samples = DecodeWavAudio(audioData);
            _logger.LogDebug("Decoded {Samples} audio samples", samples.Length);

            if (sessionState.RealtimeMode)
            {
                // Realtime mode: buffer audio and detect pauses
                _audioStreamBuffer.AddSamples(samples);

                // Send buffer status to client
                var bufferStatus = new { type = "buffer_status", fillPercent = _audioStreamBuffer.FillPercentage, sampleCount = _audioStreamBuffer.SampleCount };
                await SendJsonAsync(webSocket, bufferStatus, cancellationToken);
            }
            else
            {
                // Standard mode: Process immediately (existing behavior)
                await ProcessAudioChunkAsync(webSocket, samples, sessionState, sessionLog, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing audio");
            await BroadcastLogAsync($"Error processing audio: {ex.Message}", "error", sessionLog);
        }
    }

    /// <summary>
    /// Process a single audio chunk (for both realtime and standard modes).
    /// </summary>
    private async Task ProcessAudioChunkAsync(
        WebSocket webSocket,
        float[] audioSamples,
        VoiceSessionState sessionState,
        SessionLogWriter? sessionLog,
        CancellationToken cancellationToken)
    {
        try
        {
            // Step 2: Run ASR to get transcript (with confidence for streaming)
            var (transcript, confidence) = await RunAsrPartialAsync(audioSamples, cancellationToken);
            _logger.LogInformation("ASR transcript: {Transcript} (confidence: {Conf})", transcript, confidence);
            await BroadcastLogAsync($"ASR transcript: {transcript}", "info", sessionLog);

            // Send partial transcript with confidence
            if (sessionState.RealtimeMode)
            {
                await SendJsonAsync(webSocket, new PartialTranscriptResponse
                {
                    Transcript = transcript,
                    Confidence = confidence,
                    IsPartial = true
                }, cancellationToken);
            }
            else
            {
                // Standard mode: use old response format for compatibility
                await SendJsonAsync(webSocket, new TranscriptResponse { Transcript = transcript }, cancellationToken);
            }

            // Step 3: Determine response text
            string responseText;
            if (sessionState.SmartMode)
            {
                // Send thinking indicator
                await SendJsonAsync(webSocket, new ThinkingResponse(), cancellationToken);
                await BroadcastLogAsync("LLM thinking...", "info", sessionLog);

                // Run LLM to generate response
                responseText = await RunLlmAsync(transcript, sessionState, cancellationToken);
                _logger.LogInformation("LLM response: {Response}", responseText);
                await BroadcastLogAsync($"LLM response: {responseText}", "info", sessionLog);

                // Update chat history
                sessionState.ChatHistory.Add(new ChatMessage { Role = "user", Content = transcript });
                sessionState.ChatHistory.Add(new ChatMessage { Role = "assistant", Content = responseText });
            }
            else
            {
                // Echo mode: repeat back the transcript
                responseText = transcript;
            }

            // Step 4: Run TTS to generate audio response
            byte[] responseAudio = await RunTtsAsync(responseText, cancellationToken);
            string audioBase64 = Convert.ToBase64String(responseAudio);
            await BroadcastLogAsync($"Generated {responseAudio.Length} bytes of TTS audio", "info", sessionLog);

            // Step 5: Send final response
            if (sessionState.RealtimeMode)
            {
                // Realtime mode: send partial LLM response and audio chunks
                await SendJsonAsync(webSocket, new PartialLlmResponse
                {
                    Text = responseText,
                    IsComplete = true
                }, cancellationToken);

                await SendJsonAsync(webSocket, new AudioStreamChunk
                {
                    AudioBase64 = audioBase64,
                    IsFinal = true
                }, cancellationToken);
            }
            else
            {
                // Standard mode: send full response (existing format)
                var response = new VoiceResponse
                {
                    Transcript = transcript,
                    Response = responseText,
                    Audio = audioBase64
                };
                await SendJsonAsync(webSocket, response, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing audio chunk");
            await BroadcastLogAsync($"Error processing audio: {ex.Message}", "error", sessionLog);
        }
    }

    private float[] DecodeWavAudio(byte[] wavData)
    {
        if (_audioProcessor != null)
        {
            return _audioProcessor.DecodeWav(wavData);
        }

        // TODO: Implement actual WAV decoding with AudioProcessor service
        // For now, return mock samples
        _logger.LogWarning("AudioProcessor not available, returning mock samples");
        return new float[16000]; // 1 second of silence at 16kHz
    }

    private async Task<string> RunAsrAsync(float[] samples, CancellationToken cancellationToken)
    {
        if (_asrService != null)
        {
            return await _asrService.TranscribeAsync(samples, cancellationToken);
        }

        // Fallback when ASR service is not registered in DI
        _logger.LogWarning("ASR service not available, returning mock transcript");
        await Task.Delay(100, cancellationToken);
        return "Hello, this is a test transcription.";
    }

    /// <summary>
    /// Run ASR with partial result capability (new for Phase 1).
    /// </summary>
    private async Task<(string transcript, float confidence)> RunAsrPartialAsync(float[] samples, CancellationToken cancellationToken)
    {
        if (_asrService != null)
        {
            return await _asrService.TranscribePartialAsync(samples, cancellationToken);
        }

        // Fallback
        _logger.LogWarning("ASR service not available, returning mock transcript");
        await Task.Delay(100, cancellationToken);
        return ("Hello, this is a test transcription.", 0.8f);
    }

    private async Task<string> RunLlmAsync(string prompt, VoiceSessionState sessionState, CancellationToken cancellationToken)
    {
        if (_llmService != null && _llmService.IsModelLoaded)
        {
            return await _llmService.GenerateResponseAsync(prompt, cancellationToken);
        }

        // TODO: Implement actual LLM inference with Phi-3 or TinyLlama via ONNX Runtime GenAI
        // Build context from chat history for actual implementation
        await Task.Delay(200, cancellationToken); // Simulate LLM thinking time
        return $"I heard you say: \"{prompt}\". How can I help you further?";
    }

    private async Task<byte[]> RunTtsAsync(string text, CancellationToken cancellationToken)
    {
        if (_ttsService != null && _ttsService.AreModelsLoaded && _audioProcessor != null)
        {
            var samples = await _ttsService.SynthesizeAsync(text, cancellationToken);
            return _audioProcessor.EncodeWav(samples, 22050);
        }

        // TODO: Implement actual TTS with FastPitch + HiFiGAN via ONNX Runtime
        // Return a minimal valid WAV file (silence) for testing
        await Task.Delay(100, cancellationToken);
        return GenerateMockWav();
    }

    private static byte[] GenerateMockWav()
    {
        // Generate a minimal valid WAV file with 0.5 seconds of silence at 22050Hz
        const int sampleRate = 22050;
        const int durationMs = 500;
        const int numSamples = sampleRate * durationMs / 1000;
        const int bytesPerSample = 2; // 16-bit audio
        const int dataSize = numSamples * bytesPerSample;

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // RIFF header
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize); // File size - 8
        writer.Write("WAVE"u8);

        // fmt chunk
        writer.Write("fmt "u8);
        writer.Write(16); // Chunk size
        writer.Write((short)1); // Audio format (PCM)
        writer.Write((short)1); // Number of channels
        writer.Write(sampleRate); // Sample rate
        writer.Write(sampleRate * bytesPerSample); // Byte rate
        writer.Write((short)bytesPerSample); // Block align
        writer.Write((short)16); // Bits per sample

        // data chunk
        writer.Write("data"u8);
        writer.Write(dataSize);

        // Write silence
        for (int i = 0; i < numSamples; i++)
        {
            writer.Write((short)0);
        }

        return ms.ToArray();
    }

    private async Task SendJsonAsync<T>(WebSocket webSocket, T message, CancellationToken cancellationToken)
    {
        if (webSocket.State != WebSocketState.Open)
            return;

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await webSocket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    private async Task BroadcastLogAsync(string message, string level, SessionLogWriter? sessionLog)
    {
        if (_logBroadcaster != null)
        {
            await _logBroadcaster.BroadcastLogAsync(message, level);
        }

        if (sessionLog != null)
        {
            await sessionLog.WriteAsync(level, message);
        }
    }
}
