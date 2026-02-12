using System;

namespace NvidiaVoiceAgent.Core.Services;

/// <summary>
/// Extracts mel-spectrogram features from audio for ASR models.
/// Configuration matches Parakeet-TDT requirements:
/// - 80 mel bins
/// - 25ms window (400 samples at 16kHz)
/// - 10ms hop (160 samples at 16kHz)
/// - 16kHz sample rate
/// </summary>
public class MelSpectrogramExtractor
{
    private const int SampleRate = 16000;
    private const int NMels = 80;
    private const int NFFt = 512;
    private const int WinLength = 400;  // 25ms at 16kHz
    private const int HopLength = 160;  // 10ms at 16kHz
    private const float FMin = 0f;
    private const float FMax = 8000f;   // Nyquist for 16kHz

    private readonly float[] _window;
    private readonly float[,] _melFilterbank;

    public MelSpectrogramExtractor()
    {
        _window = CreateHannWindow(WinLength);
        _melFilterbank = CreateMelFilterbank(NFFt, SampleRate, NMels, FMin, FMax);
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
            return new float[0, NMels];
        }

        // Calculate number of frames
        int numFrames = Math.Max(1, (audioSamples.Length - WinLength) / HopLength + 1);
        var melSpectrogram = new float[numFrames, NMels];

        // Pre-allocate buffers
        var windowedFrame = new float[NFFt];
        var fftReal = new float[NFFt];
        var fftImag = new float[NFFt];
        var powerSpectrum = new float[NFFt / 2 + 1];

        for (int frame = 0; frame < numFrames; frame++)
        {
            int startSample = frame * HopLength;

            // Clear buffers
            Array.Clear(windowedFrame, 0, windowedFrame.Length);

            // Apply window
            int copyLength = Math.Min(WinLength, audioSamples.Length - startSample);
            for (int i = 0; i < copyLength; i++)
            {
                windowedFrame[i] = audioSamples[startSample + i] * _window[i];
            }

            // Compute FFT
            ComputeRealFFT(windowedFrame, fftReal, fftImag);

            // Compute power spectrum
            for (int i = 0; i <= NFFt / 2; i++)
            {
                powerSpectrum[i] = fftReal[i] * fftReal[i] + fftImag[i] * fftImag[i];
            }

            // Apply mel filterbank
            for (int mel = 0; mel < NMels; mel++)
            {
                float sum = 0;
                for (int k = 0; k <= NFFt / 2; k++)
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
    /// Normalize mel spectrogram using global mean/std (approximate values for speech).
    /// </summary>
    public float[,] Normalize(float[,] melSpectrogram)
    {
        int numFrames = melSpectrogram.GetLength(0);
        int numMels = melSpectrogram.GetLength(1);

        // Approximate mean/std for log-mel spectrograms of speech
        const float meanValue = -4.0f;
        const float stdValue = 4.0f;

        var normalized = new float[numFrames, numMels];
        for (int t = 0; t < numFrames; t++)
        {
            for (int m = 0; m < numMels; m++)
            {
                normalized[t, m] = (melSpectrogram[t, m] - meanValue) / stdValue;
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
