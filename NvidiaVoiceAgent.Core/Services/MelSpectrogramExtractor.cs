using System;

namespace NvidiaVoiceAgent.Core.Services;

/// <summary>
/// Extracts mel-spectrogram features from audio for ASR models.
/// Default configuration matches Parakeet-TDT requirements.
/// The number of mel bins can be adjusted at construction time to match the model.
/// </summary>
public class MelSpectrogramExtractor
{
    private readonly int _sampleRate;
    private readonly int _nMels;
    private readonly int _nFft;
    private readonly int _winLength;
    private readonly int _hopLength;
    private readonly float _fMin;
    private readonly float _fMax;

    private readonly float[] _window;
    private readonly float[,] _melFilterbank;

    /// <summary>
    /// Number of mel bins this extractor produces.
    /// </summary>
    public int NumMels => _nMels;

    /// <summary>
    /// Create a mel-spectrogram extractor with configurable parameters.
    /// </summary>
    /// <param name="nMels">Number of mel filter bins (default 128 for Parakeet-TDT-V2).</param>
    /// <param name="nFft">FFT size (default 512).</param>
    /// <param name="winLength">Window length in samples (default 400 = 25ms at 16kHz).</param>
    /// <param name="hopLength">Hop length in samples (default 160 = 10ms at 16kHz).</param>
    /// <param name="sampleRate">Audio sample rate (default 16000).</param>
    /// <param name="fMin">Minimum frequency for the mel filterbank (default 0).</param>
    /// <param name="fMax">Maximum frequency for the mel filterbank (default 8000 = Nyquist at 16kHz).</param>
    public MelSpectrogramExtractor(
        int nMels = 128,
        int nFft = 512,
        int winLength = 400,
        int hopLength = 160,
        int sampleRate = 16000,
        float fMin = 0f,
        float fMax = 8000f)
    {
        _nMels = nMels;
        _nFft = nFft;
        _winLength = winLength;
        _hopLength = hopLength;
        _sampleRate = sampleRate;
        _fMin = fMin;
        _fMax = fMax;

        _window = CreateHannWindow(_winLength);
        _melFilterbank = CreateMelFilterbank(_nFft, _sampleRate, _nMels, _fMin, _fMax);
    }

    /// <summary>
    /// Extract log-mel spectrogram from audio samples.
    /// </summary>
    /// <param name="audioSamples">16kHz mono float audio samples in range [-1, 1]</param>
    /// <returns>Log-mel spectrogram [time, mel_bins]</returns>
    public float[,] Extract(float[] audioSamples)
    {
        if (audioSamples == null || audioSamples.Length == 0)
        {
            return new float[0, _nMels];
        }

        // Validate audio length has at least one complete frame
        if (audioSamples.Length < _winLength)
        {
            // Return minimal spectrogram rather than throwing
            return new float[0, _nMels];
        }

        // Calculate number of frames
        int numFrames = Math.Max(1, (audioSamples.Length - _winLength) / _hopLength + 1);
        var melSpectrogram = new float[numFrames, _nMels];

        // Pre-allocate buffers
        var windowedFrame = new float[_nFft];
        var fftReal = new float[_nFft];
        var fftImag = new float[_nFft];
        var powerSpectrum = new float[_nFft / 2 + 1];

        for (int frame = 0; frame < numFrames; frame++)
        {
            int startSample = frame * _hopLength;

            // Clear buffers
            Array.Clear(windowedFrame, 0, windowedFrame.Length);

            // Apply window
            int copyLength = Math.Min(_winLength, audioSamples.Length - startSample);
            for (int i = 0; i < copyLength; i++)
            {
                windowedFrame[i] = audioSamples[startSample + i] * _window[i];
            }

            // Compute FFT
            ComputeRealFFT(windowedFrame, fftReal, fftImag);

            // Compute power spectrum
            for (int i = 0; i <= _nFft / 2; i++)
            {
                powerSpectrum[i] = fftReal[i] * fftReal[i] + fftImag[i] * fftImag[i];
            }

            // Apply mel filterbank
            for (int mel = 0; mel < _nMels; mel++)
            {
                float sum = 0;
                for (int k = 0; k <= _nFft / 2; k++)
                {
                    sum += powerSpectrum[k] * _melFilterbank[mel, k];
                }
                // Log mel with floor to avoid log(0)
                melSpectrogram[frame, mel] = MathF.Log(Math.Max(sum, 1e-10f));
            }
        }

        return melSpectrogram;
    }

    /// <summary>
    /// Normalize mel spectrogram using per-feature (per-mel-bin) normalization.
    /// Each mel bin is independently normalized to zero mean and unit variance
    /// across the time dimension, matching NeMo's "per_feature" normalization.
    /// </summary>
    public float[,] Normalize(float[,] melSpectrogram)
    {
        int numFrames = melSpectrogram.GetLength(0);
        int numMels = melSpectrogram.GetLength(1);

        if (numFrames == 0)
        {
            return new float[numFrames, numMels];
        }

        var normalized = new float[numFrames, numMels];

        for (int m = 0; m < numMels; m++)
        {
            // Compute mean for this mel bin
            float sum = 0;
            for (int t = 0; t < numFrames; t++)
            {
                sum += melSpectrogram[t, m];
            }
            float mean = sum / numFrames;

            // Compute std for this mel bin
            float sumSq = 0;
            for (int t = 0; t < numFrames; t++)
            {
                float diff = melSpectrogram[t, m] - mean;
                sumSq += diff * diff;
            }
            float std = MathF.Sqrt(sumSq / numFrames) + 1e-5f;

            // Normalize
            for (int t = 0; t < numFrames; t++)
            {
                normalized[t, m] = (melSpectrogram[t, m] - mean) / std;
            }
        }

        return normalized;
    }

    /// <summary>
    /// Convert mel spectrogram to flat array for ONNX input [1, time, mel_bins].
    /// </summary>
    public float[] ToFlatArray(float[,] melSpectrogram)
    {
        int numFrames = melSpectrogram.GetLength(0);
        int numMels = melSpectrogram.GetLength(1);
        var flat = new float[numFrames * numMels];

        for (int t = 0; t < numFrames; t++)
        {
            for (int m = 0; m < numMels; m++)
            {
                flat[t * numMels + m] = melSpectrogram[t, m];
            }
        }

        return flat;
    }

    private static float[] CreateHannWindow(int length)
    {
        var window = new float[length];
        for (int i = 0; i < length; i++)
        {
            window[i] = 0.5f * (1 - MathF.Cos(2 * MathF.PI * i / (length - 1)));
        }
        return window;
    }

    private static float[,] CreateMelFilterbank(int nfft, int sampleRate, int nMels, float fMin, float fMax)
    {
        int numBins = nfft / 2 + 1;
        var filterbank = new float[nMels, numBins];

        // Convert Hz to Mel scale
        float melMin = HzToMel(fMin);
        float melMax = HzToMel(fMax);

        // Create equally spaced mel points
        var melPoints = new float[nMels + 2];
        for (int i = 0; i < nMels + 2; i++)
        {
            melPoints[i] = melMin + i * (melMax - melMin) / (nMels + 1);
        }

        // Convert back to Hz and then to FFT bin indices
        var binIndices = new int[nMels + 2];
        for (int i = 0; i < nMels + 2; i++)
        {
            float hz = MelToHz(melPoints[i]);
            binIndices[i] = (int)MathF.Floor((nfft + 1) * hz / sampleRate);
        }

        // Create triangular filters
        for (int mel = 0; mel < nMels; mel++)
        {
            int startBin = binIndices[mel];
            int centerBin = binIndices[mel + 1];
            int endBin = binIndices[mel + 2];

            // Rising edge
            for (int k = startBin; k < centerBin && k < numBins; k++)
            {
                if (centerBin != startBin)
                {
                    filterbank[mel, k] = (float)(k - startBin) / (centerBin - startBin);
                }
            }

            // Falling edge
            for (int k = centerBin; k < endBin && k < numBins; k++)
            {
                if (endBin != centerBin)
                {
                    filterbank[mel, k] = (float)(endBin - k) / (endBin - centerBin);
                }
            }
        }

        return filterbank;
    }

    private static float HzToMel(float hz)
    {
        return 2595 * MathF.Log10(1 + hz / 700);
    }

    private static float MelToHz(float mel)
    {
        return 700 * (MathF.Pow(10, mel / 2595) - 1);
    }

    /// <summary>
    /// Simple real-valued FFT using Cooley-Tukey algorithm.
    /// Not the fastest but pure C# with no dependencies.
    /// </summary>
    private static void ComputeRealFFT(float[] input, float[] real, float[] imag)
    {
        int n = input.Length;

        // Bit-reversal permutation
        for (int i = 0; i < n; i++)
        {
            real[i] = input[BitReverse(i, n)];
            imag[i] = 0;
        }

        // Cooley-Tukey FFT
        for (int size = 2; size <= n; size *= 2)
        {
            int halfSize = size / 2;
            float angleStep = -2 * MathF.PI / size;

            for (int i = 0; i < n; i += size)
            {
                for (int j = 0; j < halfSize; j++)
                {
                    float angle = angleStep * j;
                    float cos = MathF.Cos(angle);
                    float sin = MathF.Sin(angle);

                    int evenIdx = i + j;
                    int oddIdx = i + j + halfSize;

                    float tempReal = real[oddIdx] * cos - imag[oddIdx] * sin;
                    float tempImag = real[oddIdx] * sin + imag[oddIdx] * cos;

                    real[oddIdx] = real[evenIdx] - tempReal;
                    imag[oddIdx] = imag[evenIdx] - tempImag;
                    real[evenIdx] = real[evenIdx] + tempReal;
                    imag[evenIdx] = imag[evenIdx] + tempImag;
                }
            }
        }
    }

    private static int BitReverse(int n, int bits)
    {
        int log2 = (int)Math.Log2(bits);
        int reversed = 0;
        for (int i = 0; i < log2; i++)
        {
            reversed = (reversed << 1) | (n & 1);
            n >>= 1;
        }
        return reversed;
    }
}
