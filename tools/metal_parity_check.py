#!/usr/bin/env python3
"""Fail when Metal silently inherits a D3D12 parity-required virtual.

This is intentionally source-only so it runs on Windows/Linux before an Apple
SDK is available. Runtime/pixel tests remain the authority for implementation
quality; this guard prevents a newly added D3D12 override from being forgotten.
"""

from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
D3D_HEADERS = [
    ROOT / "src/native/jalium.native.d3d12/include/d3d12_backend.h",
    ROOT / "src/native/jalium.native.d3d12/include/d3d12_render_target.h",
]
METAL_HEADER = ROOT / "src/native/jalium.native.metal/include/metal_backend.h"

# Backend-specific handles/race injectors have no Metal semantic counterpart.
# Their cross-platform behavior is covered by Metal's display-link pacing and
# simulated device-loss/resource-retirement tests instead.
ALLOWED_D3D12_ONLY = {
    "GetFrameLatencyWaitable",
    "DebugForceLeakedCommandListResize",
    "DebugForceVelloOutputOrphan",
}


def overrides(path: pathlib.Path) -> set[str]:
    text = path.read_text(encoding="utf-8")
    text = re.sub(r"/\*.*?\*/", " ", text, flags=re.S)
    text = re.sub(r"//[^\n]*", " ", text)
    pattern = re.compile(
        r"(?<!~)\b([A-Za-z_]\w*)\s*\((?:[^(){};]|\([^(){};]*\))*\)\s*"
        r"(?:const\s*)?(?:noexcept\s*)?override\b",
        re.S,
    )
    return {match.group(1) for match in pattern.finditer(text)}


def main() -> int:
    d3d: set[str] = set()
    for header in D3D_HEADERS:
        d3d |= overrides(header)
    metal = overrides(METAL_HEADER)
    missing = sorted(d3d - metal - ALLOWED_D3D12_ONLY)
    extra = sorted(metal - d3d)
    print(f"D3D12 overrides: {len(d3d)}")
    print(f"Metal overrides: {len(metal)}")
    if extra:
        print("Metal-specific/additional overrides: " + ", ".join(extra))
    if missing:
        print("ERROR: parity-required D3D12 overrides missing in Metal:", file=sys.stderr)
        for name in missing:
            print(f"  - {name}", file=sys.stderr)
        return 1
    print("PASS: Metal declares every cross-platform D3D12 capability.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
