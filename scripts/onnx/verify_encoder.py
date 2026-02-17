"""
Verify that a patched encoder.onnx works correctly with ONNX Runtime.

Runs the encoder with several synthetic inputs of varying frame lengths
and (optionally) with a real WAV file to confirm that no dimension-mismatch
errors occur in the self-attention layers.

Usage:
    python scripts/onnx/verify_encoder.py [--model-path PATH] [--wav PATH]

Defaults:
    --model-path  NvidiaVoiceAgent/Models/parakeet-tdt-0.6b/onnx/encoder.onnx
    --wav         tests/NvidiaVoiceAgent.Core.Tests/TestData/hey_can_you_help_me.wav
"""

import argparse
import os
import sys
import wave

import numpy as np
import onnxruntime as ort


def get_repo_root() -> str:
    return os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def get_default_model_path() -> str:
    return os.path.join(
        get_repo_root(), "NvidiaVoiceAgent", "Models",
        "parakeet-tdt-0.6b", "onnx", "encoder.onnx",
    )


def get_default_wav_path() -> str:
    return os.path.join(
        get_repo_root(), "tests",
        "NvidiaVoiceAgent.Core.Tests", "TestData",
        "hey_can_you_help_me.wav",
    )


def create_session(model_path: str) -> ort.InferenceSession:
    """Create an ONNX Runtime session with memory reuse disabled."""
    opts = ort.SessionOptions()
    # Disable memory pattern to avoid buffer-reuse issues with patched shapes
    opts.enable_mem_pattern = False
    opts.enable_mem_reuse = False
    return ort.InferenceSession(model_path, opts, providers=["CPUExecutionProvider"])


def run_encoder(session: ort.InferenceSession, frames: int) -> tuple[tuple, np.ndarray]:
    """Run the encoder with random mel-spectrogram input."""
    audio_signal = np.random.randn(1, 128, frames).astype(np.float32)
    length = np.array([frames], dtype=np.int64)
    outputs = session.run(None, {"audio_signal": audio_signal, "length": length})
    return outputs[0].shape, outputs[1]


def test_synthetic(session: ort.InferenceSession) -> bool:
    """Test with several synthetic frame counts."""
    print("=" * 60)
    print("SYNTHETIC INPUT TESTS")
    print("=" * 60)

    test_frames = [64, 128, 280, 305, 312, 500, 1024]
    all_ok = True

    for frames in test_frames:
        try:
            shape, enc_lengths = run_encoder(session, frames)
            print(f"  frames={frames:>5}  ->  output={shape}  enc_len={enc_lengths}  OK")
        except Exception as e:
            err = str(e)[:200]
            print(f"  frames={frames:>5}  ->  FAILED: {err}")
            all_ok = False

    print()
    return all_ok


def test_real_audio(session: ort.InferenceSession, wav_path: str) -> bool:
    """Test with a real WAV file (uses random mel-spec with correct frame count)."""
    print("=" * 60)
    print("REAL AUDIO TEST")
    print("=" * 60)

    if not os.path.exists(wav_path):
        print(f"  WAV file not found: {wav_path}")
        print("  Skipping real audio test.")
        print()
        return True

    with wave.open(wav_path, "rb") as wf:
        n_channels = wf.getnchannels()
        sample_width = wf.getsampwidth()
        framerate = wf.getframerate()
        n_frames = wf.getnframes()

    print(f"  File     : {os.path.basename(wav_path)}")
    print(f"  Format   : {n_channels}ch, {sample_width * 8}bit, {framerate}Hz")
    print(f"  Samples  : {n_frames}  ({n_frames / framerate:.3f}s)")

    # Calculate mel frame count (matching C# MelSpectrogramExtractor defaults)
    hop_length = 160
    win_length = 400
    num_frames = max(1, (n_frames - win_length) // hop_length + 1)
    padded_frames = ((num_frames + 7) // 8) * 8

    print(f"  Mel frames : {num_frames} (padded to {padded_frames})")
    print()

    try:
        shape, enc_lengths = run_encoder(session, padded_frames)
        print(f"  Encoder output : {shape}  enc_len={enc_lengths}  OK")
        print()
        return True
    except Exception as e:
        err = str(e)[:300]
        print(f"  FAILED: {err}")
        print()
        return False


def main() -> None:
    parser = argparse.ArgumentParser(description="Verify patched encoder.onnx with ONNX Runtime")
    parser.add_argument("--model-path", default=get_default_model_path(),
                        help="Path to encoder.onnx")
    parser.add_argument("--wav", default=get_default_wav_path(),
                        help="Path to a WAV file for real-audio test")
    args = parser.parse_args()

    if not os.path.exists(args.model_path):
        print(f"ERROR: Model not found at {args.model_path}")
        sys.exit(1)

    print(f"ONNX Runtime version: {ort.get_version_string()}")
    print(f"Loading model: {args.model_path}\n")

    session = create_session(args.model_path)

    synth_ok = test_synthetic(session)
    wav_ok = test_real_audio(session, args.wav)

    print("=" * 60)
    if synth_ok and wav_ok:
        print("ALL TESTS PASSED")
    else:
        print("SOME TESTS FAILED")
        sys.exit(1)
    print("=" * 60)


if __name__ == "__main__":
    main()
