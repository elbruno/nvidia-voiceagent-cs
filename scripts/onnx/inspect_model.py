"""
Inspect an ONNX encoder model to extract its structure, constants, and
shape formulas.

This script is the first step in our workflow:
  1. inspect_model.py  — Extract structure and constants from the ONNX graph
  2. patch_encoder.py  — Apply fixes discovered during inspection
  3. verify_encoder.py — Confirm the patched model works end-to-end

Usage:
    python scripts/onnx/inspect_model.py [--model-path PATH]

The default model path is:
    NvidiaVoiceAgent/Models/parakeet-tdt-0.6b/onnx/encoder.onnx
"""

import argparse
import os
import sys

import onnx
import numpy as np


def get_default_model_path() -> str:
    """Return the default encoder path relative to the repo root."""
    repo_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    return os.path.join(
        repo_root, "NvidiaVoiceAgent", "Models",
        "parakeet-tdt-0.6b", "onnx", "encoder.onnx",
    )


def print_model_metadata(model: onnx.ModelProto) -> None:
    print("=" * 60)
    print("MODEL METADATA")
    print("=" * 60)
    print(f"IR version : {model.ir_version}")
    print(f"Producer   : {model.producer_name} v{model.producer_version}")
    for op in model.opset_import:
        print(f"Opset      : domain={op.domain!r}, version={op.version}")
    for p in model.metadata_props:
        print(f"Meta       : {p.key} = {p.value}")
    print()


def print_io(graph: onnx.GraphProto) -> None:
    print("=" * 60)
    print("INPUTS / OUTPUTS")
    print("=" * 60)
    for inp in graph.input:
        shape = [d.dim_param or d.dim_value for d in inp.type.tensor_type.shape.dim]
        print(f"  INPUT  {inp.name}: {shape}  dtype={inp.type.tensor_type.elem_type}")
    for out in graph.output:
        shape = [d.dim_param or d.dim_value for d in out.type.tensor_type.shape.dim]
        print(f"  OUTPUT {out.name}: {shape}  dtype={out.type.tensor_type.elem_type}")
    print()


def print_pre_encode_chain(graph: onnx.GraphProto) -> None:
    """Print the length-computation chain inside /pre_encode/."""
    print("=" * 60)
    print("PRE-ENCODE LENGTH FORMULA CHAIN")
    print("=" * 60)

    # Find the constants used in the chain
    constant_names = [
        "/pre_encode/Constant_output_0",
        "/pre_encode/Constant_1_output_0",
        "/pre_encode/Constant_2_output_0",
    ]
    constants = {}
    for init in graph.initializer:
        if init.name in constant_names:
            constants[init.name] = onnx.numpy_helper.to_array(init)

    for name, val in constants.items():
        print(f"  {name} = {val}")

    # Print the Add/Div/Floor chain
    print()
    for node in graph.node:
        if "/pre_encode/" in node.name and node.op_type in ("Add", "Div", "Floor", "Cast"):
            print(f"  {node.name}: {node.op_type}({list(node.input)}) -> {list(node.output)}")

    c0 = constants.get("/pre_encode/Constant_output_0", "?")
    c1 = constants.get("/pre_encode/Constant_1_output_0", "?")
    c2 = constants.get("/pre_encode/Constant_2_output_0", "?")
    print()
    print(f"  Formula per stage: floor((L + {c0}) / {c1} + {c2})")
    print(f"  Applied 3 times  : L -> L1 -> L2 -> L3")
    print()


def print_self_attn_structure(graph: onnx.GraphProto, layer: int = 0) -> None:
    """Print the self-attention nodes for a given layer."""
    prefix = f"/layers.{layer}/self_attn/"
    print("=" * 60)
    print(f"SELF-ATTENTION STRUCTURE (layer {layer})")
    print("=" * 60)

    for node in graph.node:
        if node.name.startswith(prefix):
            print(f"  {node.name}: {node.op_type}({list(node.input)}) -> {list(node.output)}")
    print()


def print_slice3_axis_values(graph: onnx.GraphProto) -> None:
    """
    Print the axis value used by every Slice_3 node in self_attn layers.

    This is the key diagnostic: in a correct Conformer export the rel_shift
    Slice_3 must slice on axis=2 (the time dimension after reshape).
    A buggy export has axis=3, which leaves the 2T-1 positional dimension
    un-sliced and causes a broadcast error at Add_2.
    """
    print("=" * 60)
    print("SLICE_3 AXIS VALUES (rel_shift diagnostic)")
    print("=" * 60)

    slice3_nodes = [n for n in graph.node if "self_attn/Slice_3" in n.name]
    print(f"  Total Slice_3 nodes: {len(slice3_nodes)}")
    print()

    # Collect unique axis initializer names
    axis_names = set()
    for node in slice3_nodes:
        if len(node.input) >= 4:
            axis_names.add(node.input[3])  # axes parameter

    for axis_name in sorted(axis_names):
        for init in graph.initializer:
            if init.name == axis_name:
                val = onnx.numpy_helper.to_array(init)
                status = "OK" if int(val) == 2 else "BUG — should be 2"
                print(f"  {axis_name} = {val}  [{status}]")
    print()


def print_pos_enc_info(graph: onnx.GraphProto) -> None:
    """Print the positional encoding table size and slice formula."""
    print("=" * 60)
    print("POSITIONAL ENCODING")
    print("=" * 60)

    for init in graph.initializer:
        if init.name == "onnx::Slice_780":
            print(f"  Table: {init.name}  shape={list(init.dims)}  dtype={init.data_type}")

    targets = [
        "/pos_enc/Constant_output_0",
        "/pos_enc/Constant_1_output_0",
        "/pos_enc/Constant_2_output_0",
    ]
    for init in graph.initializer:
        if init.name in targets:
            val = onnx.numpy_helper.to_array(init)
            print(f"  {init.name} = {val}")

    print("  Slice formula: pos_table[5000-T : 5000+T-1]  → length = 2T-1")
    print()


def print_key_constants(graph: onnx.GraphProto) -> None:
    """Print constants used across the graph that affect shapes."""
    print("=" * 60)
    print("KEY SHAPE CONSTANTS")
    print("=" * 60)

    targets = {
        "/pre_encode/Constant_9_output_0": "step size (Slice)",
        "/pre_encode/Constant_10_output_0": "zero (Range start, Gather axis)",
        "/pre_encode/Constant_11_output_0": "two (Gather dim index)",
        "/pre_encode/Constant_12_output_0": "negative one (reshape)",
        "/layers.0/self_attn/Constant_2_output_0": "n_heads",
        "/layers.0/self_attn/Constant_3_output_0": "d_head",
        "/layers.0/self_attn/Constant_104_output_0": "Slice_3 axis (rel_shift)",
        "onnx::Unsqueeze_759": "unsqueeze dim",
    }

    for init in graph.initializer:
        if init.name in targets:
            val = onnx.numpy_helper.to_array(init)
            print(f"  {init.name} = {val}  # {targets[init.name]}")
    print()


def main() -> None:
    parser = argparse.ArgumentParser(description="Inspect ONNX encoder model structure")
    parser.add_argument("--model-path", default=get_default_model_path(),
                        help="Path to encoder.onnx")
    args = parser.parse_args()

    if not os.path.exists(args.model_path):
        print(f"ERROR: Model not found at {args.model_path}")
        sys.exit(1)

    print(f"Loading model (graph only): {args.model_path}")
    model = onnx.load(args.model_path, load_external_data=False)
    graph = model.graph
    print(f"Loaded: {len(graph.node)} nodes, {len(graph.initializer)} initializers\n")

    print_model_metadata(model)
    print_io(graph)
    print_key_constants(graph)
    print_pre_encode_chain(graph)
    print_pos_enc_info(graph)
    print_slice3_axis_values(graph)
    print_self_attn_structure(graph, layer=0)


if __name__ == "__main__":
    main()
