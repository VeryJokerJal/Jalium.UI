#!/usr/bin/env python3
"""Extract a named C++ raw-string Metal source without duplicating shader text."""

from __future__ import annotations

import argparse
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("header", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--delimiter", default="METAL")
    args = parser.parse_args()

    text = args.header.read_text(encoding="utf-8")
    opening = f'R"{args.delimiter}('
    closing = f'){args.delimiter}"'
    start = text.find(opening)
    if start < 0:
        raise SystemExit(f"raw-string opener {opening!r} was not found in {args.header}")
    start += len(opening)
    end = text.find(closing, start)
    if end < 0:
        raise SystemExit(f"raw-string closer {closing!r} was not found in {args.header}")

    source = text[start:end]
    if not source.endswith("\n"):
        source += "\n"
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(source, encoding="utf-8", newline="\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
