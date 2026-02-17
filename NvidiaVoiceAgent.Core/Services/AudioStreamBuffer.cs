using Microsoft.Extensions.Logging;
using NvidiaVoiceAgent.Core.Models;

namespace NvidiaVoiceAgent.Core.Services;

/// <summary>
/// Buffers incoming audio stream and detects pauses for real-time conversation mode.
/// Accumulates audio chunks and emits events on pause detection.
/// </summary>
public class AudioStreamBuffer
{
    private readonly ILogger<AudioStreamBuffer> _logger;
    private float[] _buffer;
    private int _writePos = 0;
    private DateTime _lastAudioTime = DateTime.UtcNow;
    private readonly int _pauseThresholdMs;
    private readonly int _bufferCapacity;
    private readonly object _lockObj = new();

    /// <summary>
    /// Fired when a new audio chunk is added to the buffer.
    /// </summary>
    public event EventHandler<AudioChunkEventArgs>? OnAudioChunk;

    /// <summary>
    /// Fired when a pause (silence) is detected after the pause threshold.
    /// </summary>
    public event EventHandler? OnPauseDetected;

    /// <summary>
    /// Create a new audio stream buffer.
    /// </summary>
    /// <param name="pauseThresholdMs">Pause detection threshold in milliseconds (default: 800ms)</param>
    /// <param name="bufferCapacitySamples">Total buffer capacity in samples (default: 512000, ~32s @ 16kHz)</param>
    /// <param name="logger">Optional logger instance</param>
    public AudioStreamBuffer(
        int pauseThresholdMs = 800,
        int bufferCapacitySamples = 512000,
        ILogger<AudioStreamBuffer>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AudioStreamBuffer>.Instance;
        _pauseThresholdMs = pauseThresholdMs;
        _bufferCapacity = bufferCapacitySamples;
        _buffer = new float[bufferCapacitySamples];
    }

    /// <summary>
    /// Add audio samples to the buffer and check for pause condition.
    /// </summary>
    public void AddSamples(float[] samples)
    {
        if (samples == null || samples.Length == 0)
            return;

        lock (_lockObj)
        {
            var now = DateTime.UtcNow;
            var timeSinceLastAudio = now - _lastAudioTime;

            // Check for pause condition (threshold passed since last audio)
            if (timeSinceLastAudio.TotalMilliseconds >= _pauseThresholdMs && _writePos > 0)
            {
                _logger.LogDebug(
                    "Pause detected: {Ms}ms since last audio (threshold: {Threshold}ms)",
                    timeSinceLastAudio.TotalMilliseconds, _pauseThresholdMs);

                OnPauseDetected?.Invoke(this, EventArgs.Empty);
            }

            // Append to buffer with overflow handling
            var samplesToWrite = Math.Min(samples.Length, _bufferCapacity - _writePos);
            if (samplesToWrite > 0)
            {
                Array.Copy(samples, 0, _buffer, _writePos, samplesToWrite);
                _writePos += samplesToWrite;

                if (samplesToWrite < samples.Length)
                {
                    _logger.LogWarning(
                        "Audio buffer overflow: dropped {Dropped} samples (buffer full)",
                        samples.Length - samplesToWrite);
                }
            }

            _lastAudioTime = now;

            // Emit chunk event
            var eventArgs = new AudioChunkEventArgs
            {
                Samples = samples,
                SampleRate = 16000,
                ReceivedAt = now
            };
            OnAudioChunk?.Invoke(this, eventArgs);
        }
    }

    /// <summary>
    /// Get all buffered audio and clear the buffer.
    /// </summary>
    /// <returns>Array of audio samples, or empty if nothing buffered</returns>
    public float[] GetAndClear()
    {
        lock (_lockObj)
        {
            if (_writePos == 0)
                return Array.Empty<float>();

            var result = _buffer.Take(_writePos).ToArray();
            _writePos = 0;
            _lastAudioTime = DateTime.UtcNow;

            _logger.LogDebug("Cleared buffer: retrieved {Samples} samples", result.Length);
            return result;
        }
    }

    /// <summary>
    /// Get current buffered audio without clearing.
    /// </summary>
    public float[] GetCurrent()
    {
        lock (_lockObj)
        {
            return _writePos > 0 ? _buffer.Take(_writePos).ToArray() : Array.Empty<float>();
        }
    }

    /// <summary>
    /// Get the number of samples currently in buffer.
    /// </summary>
    public int SampleCount
    {
        get
        {
            lock (_lockObj)
            {
                return _writePos;
            }
        }
    }

    /// <summary>
    /// Clear the buffer without processing.
    /// </summary>
    public void Clear()
    {
        lock (_lockObj)
        {
            _writePos = 0;
            _lastAudioTime = DateTime.UtcNow;
            _logger.LogDebug("Buffer manually cleared");
        }
    }

    /// <summary>
    /// Get buffer fill percentage (0-100).
    /// </summary>
    public float FillPercentage
    {
        get
        {
            lock (_lockObj)
            {
                return (_writePos / (float)_bufferCapacity) * 100f;
            }
        }
    }
}
