#!/usr/bin/env python3
"""Recover the checked-in Vulkan bytecode for the Apple offline shader build."""

import argparse
from pathlib import Path
import re
import struct


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("header", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    source = args.header.read_text(encoding="utf-8")
    pattern = re.compile(
        r"// (vello_\w+\.spv) \((\d+) bytes\)\s*"
        r"inline constexpr uint32_t \w+\[\] = \{(.*?)\};", re.S)
    modules = pattern.findall(source)
    if len(modules) != 20:
        raise SystemExit(f"Expected 20 Vello modules in {args.header}; found {len(modules)}")
    args.output.mkdir(parents=True, exist_ok=True)
    for name, size, body in modules:
        words = [int(word, 16) for word in re.findall(r"0x([0-9a-fA-F]+)u", body)]
        payload = struct.pack(f"<{len(words)}I", *words)
        if len(payload) != int(size) or words[0] != 0x07230203:
            raise SystemExit(f"Invalid SPIR-V module: {name}")
        (args.output / name).write_bytes(payload)


if __name__ == "__main__":
    main()
