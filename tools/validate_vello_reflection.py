#!/usr/bin/env python3
"""Validate the canonical Vello SPIR-V ABI before producing an Apple metallib."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


EXPECTED_BINDINGS: dict[str, set[int]] = {
    "pathtag_reduce": {0, 16, 48},
    "pathtag_reduce2": {16, 48},
    "pathtag_scan1": {16, 17, 48},
    "pathtag_scan": {0, 16, 17, 48},
    "pathtag_scan_small": {0, 16, 17, 48},
    "bbox_clear": {0, 48},
    "flatten": {0, 16, 17, 48, 49, 50},
    "draw_reduce": {0, 16, 48},
    "draw_leaf": {0, 16, 17, 18, 48, 49, 50},
    "clip_reduce": {16, 17, 48, 49},
    "clip_leaf": {0, 16, 17, 18, 19, 48, 49},
    "binning": {0, 16, 17, 18, 48, 49, 50, 51},
    "tile_alloc": {0, 16, 17, 48, 49, 50},
    "path_count_setup": {48, 49},
    "path_count": {0, 16, 17, 48, 49, 50},
    "backdrop": {0, 16, 48, 49},
    "coarse": {0, 16, 17, 18, 19, 20, 48, 49, 50},
    "path_tiling_setup": {48, 49, 50},
    "path_tiling": {16, 17, 18, 19, 48, 49},
    "fine": {0, 16, 17, 18, 19, 20, 48, 49},
}

RESOURCE_KEYS = (
    "ubos", "ssbos", "push_constant_buffers", "storage_images", "images",
    "textures", "separate_images", "separate_samplers", "subpass_inputs",
)


def entry_workgroup(document: dict) -> tuple[int, int, int]:
    entries = document.get("entryPoints") or document.get("entry_points") or []
    if len(entries) != 1:
        raise ValueError(f"expected one entry point, found {len(entries)}")
    workgroup = entries[0].get("workgroup_size") or entries[0].get("workgroupSize")
    if not isinstance(workgroup, list) or len(workgroup) != 3:
        raise ValueError("entry point has no three-dimensional workgroup_size")
    return tuple(int(value) for value in workgroup)


def resource_bindings(document: dict) -> set[int]:
    bindings: set[int] = set()
    for key in RESOURCE_KEYS:
        for resource in document.get(key, []):
            if int(resource.get("set", 0)) != 0:
                raise ValueError(f"resource {resource.get('name')} uses descriptor set {resource.get('set')}")
            if "binding" in resource:
                bindings.add(int(resource["binding"]))
    return bindings


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("reflection_directory", type=Path)
    args = parser.parse_args()

    failures: list[str] = []
    for stage, expected in EXPECTED_BINDINGS.items():
        path = args.reflection_directory / f"vello_{stage}.json"
        try:
            document = json.loads(path.read_text(encoding="utf-8"))
            actual = resource_bindings(document)
            if actual != expected:
                failures.append(
                    f"{stage}: bindings expected {sorted(expected)}, got {sorted(actual)}")
            expected_group = (4, 16, 1) if stage == "fine" else (
                (1, 1, 1) if stage in {"path_count_setup", "path_tiling_setup"}
                else (256, 1, 1))
            actual_group = entry_workgroup(document)
            if actual_group != expected_group:
                failures.append(
                    f"{stage}: workgroup expected {expected_group}, got {actual_group}")
        except (OSError, ValueError, TypeError, json.JSONDecodeError) as error:
            failures.append(f"{stage}: {error}")

    if failures:
        for failure in failures:
            print(f"ERROR: {failure}")
        return 1
    print("PASS: Vello shader resources and workgroup sizes match the Metal ABI.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
