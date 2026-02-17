"""
Patch the Parakeet-TDT decoder.onnx output list.

The decoder export includes an output named `prednet_lengths` that has
no producing node in the graph. ONNX Runtime rejects the model with a
"no producer" error when this output is present.

This script removes the stale output definition while keeping the rest
of the graph intact.

Usage:
    python scripts/onnx/patch_decoder.py [--model-dir DIR] [--dry-run]

Defaults to: NvidiaVoiceAgent/Models/parakeet-tdt-0.6b/onnx/
"""

import argparse
import os
import sys

import onnx


def get_default_model_dir() -> str:
    repo_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    return os.path.join(
        repo_root, "NvidiaVoiceAgent", "Models",
        "parakeet-tdt-0.6b", "onnx",
    )


def find_output_index(graph: onnx.GraphProto, name: str) -> int:
    for idx, output in enumerate(graph.output):
        if output.name == name:
            return idx
    return -1


def remove_output(graph: onnx.GraphProto, name: str) -> bool:
    removed = False
    index = find_output_index(graph, name)
    if index >= 0:
        graph.output.pop(index)
        removed = True

    # Also remove any stale value_info entries
    for i in reversed(range(len(graph.value_info))):
        if graph.value_info[i].name == name:
            graph.value_info.pop(i)

    return removed


def main() -> None:
    parser = argparse.ArgumentParser(description="Patch Parakeet-TDT decoder.onnx outputs")
    parser.add_argument("--model-dir", default=get_default_model_dir(),
                        help="Directory containing decoder.onnx")
    parser.add_argument("--dry-run", action="store_true",
                        help="Diagnose only, do not write files")
    args = parser.parse_args()

    decoder_path = os.path.join(args.model_dir, "decoder.onnx")
    if not os.path.exists(decoder_path):
        print(f"ERROR: decoder.onnx not found in {args.model_dir}")
        sys.exit(1)

    print(f"Loading decoder graph: {decoder_path}")
    model = onnx.load(decoder_path, load_external_data=False)

    if find_output_index(model.graph, "prednet_lengths") < 0:
        print("  Output 'prednet_lengths' not present — decoder is OK, nothing to patch.")
        return

    print("  Found stale output: prednet_lengths")

    if args.dry_run:
        print("  --dry-run specified, stopping here.")
        return

    removed = remove_output(model.graph, "prednet_lengths")
    if not removed:
        print("  Nothing removed. Decoder output list unchanged.")
        return

    backup_path = os.path.join(args.model_dir, "decoder_original_buggy.onnx")
    if not os.path.exists(backup_path):
        os.rename(decoder_path, backup_path)
        print(f"  Original backed up to: {backup_path}")

    print(f"  Saving patched decoder to: {decoder_path}")
    onnx.save(model, decoder_path)
    print("  Done!\n")


if __name__ == "__main__":
    main()
