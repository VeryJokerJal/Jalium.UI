#!/usr/bin/env python3
"""Verify SPIR-V descriptor indices against the Metal argument-buffer table."""

import argparse
import json
from pathlib import Path
import re


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reflection", type=Path)
    args = parser.parse_args()
    source = (Path(__file__).resolve().parents[1] /
              "src/native/jalium.native.metal/src/metal_vello.cpp").read_text(encoding="utf-8")
    tables = {
        name: {int(index) for index in re.findall(r"B\((\d+),", body)}
        for name, body in re.findall(r"const Binding (b\d+)\[\]=\{(.*?)\};", source)
    }
    stages = re.findall(r'STAGE\("(\w+)",(b\d+)\)', source)
    if len(stages) != 19:
        raise SystemExit("Could not read the Metal Vello stage table")
    for stage, table in stages:
        path = args.reflection / f"vello_{stage}.json"
        reflection = json.loads(path.read_text(encoding="utf-8"))
        resources = [resource for group in reflection.values() if isinstance(group, list)
                     for resource in group if isinstance(resource, dict) and "binding" in resource]
        if any(resource.get("set") != 0 for resource in resources):
            raise SystemExit(f"Unexpected descriptor set in {path}")
        actual = {resource["binding"] for resource in resources}
        if actual != tables[table]:
            raise SystemExit(f"{stage}: reflected bindings {sorted(actual)} != Metal {sorted(tables[table])}")
    print(f"Validated {len(stages)} Metal Vello binding tables")


if __name__ == "__main__":
    main()
