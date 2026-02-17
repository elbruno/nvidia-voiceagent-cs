"""Test TDT decoding in Python to verify correct output."""
import onnxruntime as ort
import numpy as np
import wave

# Load models
enc_sess = ort.InferenceSession('E:/models-cache/parakeet-tdt-0.6b/onnx/encoder.onnx')
dec_sess = ort.InferenceSession('E:/models-cache/parakeet-tdt-0.6b/onnx/decoder.onnx')

# Load vocab
with open('E:/models-cache/parakeet-tdt-0.6b/vocab.txt', encoding='utf-8') as f:
    vocab = f.read().splitlines()
print(f'Vocab size: {len(vocab)}')

# Load audio
wav_path = 'D:/elbruno/nvidia-voiceagent-cs/tests/NvidiaVoiceAgent.Core.Tests/TestData/hey_can_you_help_me.wav'
with wave.open(wav_path, 'rb') as wf:
    frames = wf.readframes(wf.getnframes())
    sr = wf.getframerate()
    samples = np.frombuffer(frames, dtype=np.int16).astype(np.float32) / 32768.0
print(f'Audio: {len(samples)} samples, {sr}Hz, duration={len(samples)/sr:.2f}s')

# Mel spectrogram
import librosa
mel = librosa.feature.melspectrogram(
    y=samples, sr=sr, n_fft=512, hop_length=160, win_length=400,
    n_mels=128, fmin=0, fmax=8000
)
log_mel = np.log(mel + 1e-9)

# Try both normalization approaches
for norm_name, norm_fn in [
    ("fixed_mean_std", lambda m: (m - (-4.0)) / 4.0),
    ("per_feature", lambda m: (m - m.mean(axis=1, keepdims=True)) / (m.std(axis=1, keepdims=True) + 1e-5)),
    ("no_normalization", lambda m: m),
]:
    mel_norm = norm_fn(log_mel.copy())
    print(f'\n=== {norm_name} (range [{mel_norm.min():.2f}, {mel_norm.max():.2f}]) ===')

    # Pad
    T = mel_norm.shape[1]
    padded_T = ((T + 7) // 8) * 8
    if padded_T != T:
        mel_norm = np.pad(mel_norm, ((0, 0), (0, padded_T - T)))

    # Encoder
    audio_signal = mel_norm[np.newaxis, :, :].astype(np.float32)
    length = np.array([padded_T], dtype=np.int64)
    enc_out = enc_sess.run(None, {'audio_signal': audio_signal, 'length': length})
    enc_hidden = enc_out[0]
    enc_T = int(enc_out[1][0])

    # TDT decode
    blank_id = 1024
    tdt_durations = [0, 1, 2, 3, 4]
    lstm_h = np.zeros((2, 1, 640), dtype=np.float32)
    lstm_c = np.zeros((2, 1, 640), dtype=np.float32)

    tokens = []
    t = 0
    last_label = blank_id
    iters = 0
    while t < enc_T and iters < enc_T * 10:
        iters += 1
        targets = np.array([[last_label]], dtype=np.int32)
        dec_out = dec_sess.run(None, {
            'encoder_outputs': enc_hidden,
            'targets': targets,
            'input_states_1': lstm_h,
            'input_states_2': lstm_c,
        })
        joint = dec_out[0]
        lstm_h = dec_out[1]
        lstm_c = dec_out[2]
        logits = joint[0, t, 0, :]
        best = int(np.argmax(logits[:blank_id + 1]))
        if best == blank_id:
            t += 1
        else:
            tokens.append(best)
            last_label = best
            dur_idx = int(np.argmax(logits[blank_id + 1:]))
            t += max(1, tdt_durations[dur_idx])

    text = ''.join(vocab[tid] for tid in tokens if tid < len(vocab)).replace('\u2581', ' ').strip()
    print(f'  Tokens: {len(tokens)}, Iters: {iters}')
    print(f'  TRANSCRIPT: "{text}"')

