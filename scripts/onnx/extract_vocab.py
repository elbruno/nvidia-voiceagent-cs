"""
Extract a SentencePiece vocabulary file (vocab.txt) from a tokenizer model.

This script searches for a SentencePiece tokenizer model (e.g. tokenizer.model)
inside the provided model directory, extracts all pieces, and writes them to
vocab.txt (one piece per line).

Usage:
    python scripts/onnx/extract_vocab.py [--model-dir DIR] [--output PATH]

Defaults to: NvidiaVoiceAgent/Models/parakeet-tdt-0.6b/
"""

import argparse
import os
import sys
from pathlib import Path

import sentencepiece as spm


def get_repo_root() -> str:
    return os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def get_default_model_dir() -> str:
    return os.path.join(
        get_repo_root(), "NvidiaVoiceAgent", "Models", "parakeet-tdt-0.6b"
    )


def find_tokenizer_model(model_dir: str) -> Path | None:
    candidates = []
    for path in Path(model_dir).rglob("*.model"):
        name = path.name.lower()
        if "tokenizer" in name:
            candidates.append(path)

    if not candidates:
        return None

    # Prefer exact tokenizer.model if present
    for path in candidates:
        if path.name == "tokenizer.model":
            return path

    return candidates[0]


def main() -> None:
    parser = argparse.ArgumentParser(description="Extract vocab.txt from SentencePiece tokenizer")
    parser.add_argument("--model-dir", default=get_default_model_dir(),
                        help="Model directory containing tokenizer.model")
    parser.add_argument("--output", default=None,
                        help="Output vocab.txt path (defaults to <model-dir>/vocab.txt)")
    parser.add_argument("--expected-size", type=int, default=1024,
                        help="Expected vocab size for sanity check (default: 1024)")
    args = parser.parse_args()

    model_dir = os.path.abspath(args.model_dir)
    if not os.path.exists(model_dir):
        print(f"ERROR: model directory not found: {model_dir}")
        sys.exit(1)

    tokenizer_path = find_tokenizer_model(model_dir)
    if tokenizer_path is None:
        print("ERROR: no tokenizer model found (expected tokenizer.model).")
        print("Looked under:")
        print(f"  {model_dir}")
        sys.exit(1)

    output_path = args.output or os.path.join(model_dir, "vocab.txt")

    print(f"Loading tokenizer: {tokenizer_path}")
    sp = spm.SentencePieceProcessor()
    sp.load(str(tokenizer_path))

    pieces = [sp.id_to_piece(i) for i in range(sp.get_piece_size())]

    if args.expected_size and len(pieces) != args.expected_size:
        print(
            f"WARNING: vocab size is {len(pieces)}, expected {args.expected_size}. "
            "Continue only if this matches your model spec."
        )

    with open(output_path, "w", encoding="utf-8") as f:
        for piece in pieces:
            f.write(piece + "\n")

    print(f"Wrote vocab: {output_path} ({len(pieces)} tokens)")


if __name__ == "__main__":
    main()
