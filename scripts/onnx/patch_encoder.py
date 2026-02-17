"""
Patch a buggy Parakeet-TDT encoder.onnx model.

The NVIDIA Parakeet-TDT 0.6B encoder exported to ONNX has a known bug in its
relative positional encoding (rel_shift).  In every Conformer self-attention
layer the ``Slice_3`` node — which should trim the rel-shifted positional
scores from ``[B, H, 2T-1, T]`` down to ``[B, H, T, T]`` — slices on
**axis 3** instead of **axis 2**.  This leaves the ``2T-1`` dimension
intact and causes a broadcast error when ``Add_2`` tries to add the
content scores ``[B, H, T, T]`` with the positional scores ``[B, H, 2T-1, T]``.

This script:
  1. Loads the ONNX graph (without external data for speed)
  2. Changes the shared ``Constant_104`` initializer from ``[3]`` to ``[2]``
  3. Clears stale ``value_info`` shape annotations so ONNX Runtime does not
     try to reuse buffers sized for the old (buggy) shapes
  4. Reloads the full model (with external data / weights) and saves the
     patched version alongside the original

Usage:
    python scripts/onnx/patch_encoder.py [--model-dir DIR] [--dry-run]

Defaults to: NvidiaVoiceAgent/Models/parakeet-tdt-0.6b/onnx/
"""

import argparse
import os
import sys

import onnx
import numpy as np


def get_default_model_dir() -> str:
    repo_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    return os.path.join(
        repo_root, "NvidiaVoiceAgent", "Models",
        "parakeet-tdt-0.6b", "onnx",
    )


def diagnose(graph: onnx.GraphProto) -> tuple[bool, str, int]:
    """
    Check whether the Slice_3 axis constant needs patching.

    Returns (needs_patch, constant_name, current_axis_value).
    """
    target = "/layers.0/self_attn/Constant_104_output_0"
    for init in graph.initializer:
        if init.name == target:
            val = int(onnx.numpy_helper.to_array(init))
            return (val != 2, target, val)
    return (False, target, -1)


def patch_graph(graph: onnx.GraphProto, constant_name: str) -> int:
    """
    Patch the axis constant and clear value_info.

    Returns number of value_info entries cleared.
    """
    # 1. Patch the axis constant from [3] to [2]
    for init in graph.initializer:
        if init.name == constant_name:
            old = onnx.numpy_helper.to_array(init)
            new_val = np.array([2], dtype=old.dtype)
            new_tensor = onnx.numpy_helper.from_array(new_val, name=init.name)
            init.CopyFrom(new_tensor)
            break

    # 2. Verify all Slice_3 nodes use this constant
    slice3_count = 0
    for node in graph.node:
        if "self_attn/Slice_3" in node.name:
            slice3_count += 1
            assert node.input[3] == constant_name, (
                f"Unexpected axes input for {node.name}: {node.input[3]}"
            )

    print(f"  Patched axis constant for {slice3_count} Slice_3 nodes")

    # 3. Clear stale value_info shape annotations
    vi_count = len(graph.value_info)
    while len(graph.value_info) > 0:
        graph.value_info.pop()

    return vi_count


def main() -> None:
    parser = argparse.ArgumentParser(description="Patch Parakeet-TDT encoder.onnx rel_shift bug")
    parser.add_argument("--model-dir", default=get_default_model_dir(),
                        help="Directory containing encoder.onnx and encoder.onnx_data")
    parser.add_argument("--dry-run", action="store_true",
                        help="Diagnose only, do not write files")
    args = parser.parse_args()

    encoder_path = os.path.join(args.model_dir, "encoder.onnx")
    if not os.path.exists(encoder_path):
        print(f"ERROR: encoder.onnx not found in {args.model_dir}")
        sys.exit(1)

    # --- Step 1: Diagnose ---
    print(f"Loading graph (without weights): {encoder_path}")
    model_light = onnx.load(encoder_path, load_external_data=False)
    needs_patch, constant_name, current_val = diagnose(model_light.graph)

    if not needs_patch:
        print(f"\n  Slice_3 axis is already {current_val} — model is OK, nothing to patch.")
        return

    print(f"\n  BUG DETECTED: Slice_3 axis = {current_val} (should be 2)")

    if args.dry_run:
        print("\n  --dry-run specified, stopping here.")
        return

    # --- Step 2: Patch ---
    print("\nLoading full model (with weights) for patching...")
    model = onnx.load(encoder_path)
    print(f"  Loaded: {len(model.graph.node)} nodes, {len(model.graph.initializer)} initializers")

    vi_cleared = patch_graph(model.graph, constant_name)
    print(f"  Cleared {vi_cleared} stale value_info entries")

    # Verify patch took effect
    _, _, new_val = diagnose(model.graph)
    assert new_val == 2, f"Patch failed: axis is {new_val}"
    print(f"  Verified: axis constant is now {new_val}")

    # --- Step 3: Back up original and save ---
    backup_path = os.path.join(args.model_dir, "encoder_original_buggy.onnx")
    if not os.path.exists(backup_path):
        os.rename(encoder_path, backup_path)
        data_path = encoder_path + "_data"
        backup_data = backup_path + "_data"
        if os.path.exists(data_path) and not os.path.exists(backup_data):
            os.rename(data_path, backup_data)
        print(f"\n  Original backed up to: {backup_path}")

    print(f"  Saving patched model to: {encoder_path}")
    onnx.save(
        model,
        encoder_path,
        save_as_external_data=True,
        all_tensors_to_one_file=True,
        location="encoder.onnx_data",
        size_threshold=1024,
    )
    print("  Done!\n")


if __name__ == "__main__":
    main()
