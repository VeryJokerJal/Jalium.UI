#!/usr/bin/env python3
"""Reject macOS native payloads that lack the current core/platform C ABI."""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path


COMMENT_RE = re.compile(r"/\*.*?\*/|//[^\r\n]*", re.DOTALL)
SYMBOL_RE = re.compile(r"\b_(jalium_[A-Za-z0-9_]+)$", re.MULTILINE)


def complete_native_build(build: Path, native: Path, payload: Path, configuration: str) -> None:
    """Finish an existing matching CMake build; never manufacture its stamp."""
    cache = build.resolve() / "CMakeCache.txt"
    values = {}
    for line in cache.read_text(encoding="utf-8").splitlines():
        if line.startswith(("#", "//")) or "=" not in line or ":" not in line:
            continue
        key, value = line.split("=", 1)
        values[key.split(":", 1)[0]] = value
    output = Path(values.get("JALIUM_NATIVE_OUTPUT_ROOT", ""))
    rid = values.get("JALIUM_NATIVE_RID", "")
    configurations = values.get("CMAKE_CONFIGURATION_TYPES", values.get("CMAKE_BUILD_TYPE", "")).split(";")
    if (Path(values.get("CMAKE_HOME_DIRECTORY", "")).resolve() != native
            or output.joinpath(rid, configuration).resolve() != payload
            or values.get("JALIUM_BUILD_STATIC", "").upper() not in ("OFF", "FALSE", "0", "NO")
            or configuration not in configurations):
        raise ValueError(f"CMake build does not match the requested dynamic macOS payload: {build}")
    cmake = Path(values.get("CMAKE_COMMAND", ""))
    if not cmake.is_file():
        raise ValueError(f"Configured CMake executable not found: {cmake}")
    print(f"Completing macOS native payload from {build}", flush=True)
    subprocess.run(
        [str(cmake), "--build", str(build.resolve()), "--config", configuration,
         "--target", "jalium.native.package.complete", "--parallel", "4"],
        check=True,
    )


def declared_symbols(directory: Path, macro: str) -> set[str]:
    pattern = re.compile(
        rf"\b{macro}\b(?:(?![;{{}}]).)*?\b(jalium_[A-Za-z0-9_]+)\s*\(",
        re.DOTALL,
    )
    headers = sorted(directory.glob("*.h"))
    if not headers:
        raise ValueError(f"Native API headers not found: {directory}")
    symbols: set[str] = set()
    for header in headers:
        symbols.update(pattern.findall(COMMENT_RE.sub(" ", header.read_text(encoding="utf-8"))))
    # Test hooks include Linux-only helpers and are not part of the runtime ABI.
    return {symbol for symbol in symbols if not symbol.startswith("jalium_test_")}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("payload", type=Path)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--configuration", choices=("Debug", "Release"), default="Debug")
    parser.add_argument("--complete-build", type=Path,
                        help="Complete this existing CMake build if the payload stamp is missing")
    args = parser.parse_args()
    payload = args.payload.resolve()
    native = args.repo_root.resolve() / "src" / "native"
    errors: list[str] = []
    try:
        if args.complete_build is not None and not (payload / ".jalium-native-complete").is_file():
            complete_native_build(args.complete_build, native, payload, args.configuration)
        required = {
            "core": declared_symbols(native / "jalium.native.core" / "include", "JALIUM_API"),
            "platform": declared_symbols(native / "jalium.native.platform" / "include", "JALIUM_PLATFORM_API"),
            "metal": {"jalium_metal_init"},
        }
        # Text outlines live in the text library on macOS, and in core on Windows.
        required["core"].discard("jalium_text_copy_outline_path")
        if not (payload / ".jalium-native-complete").is_file():
            errors.append(f"Native payload completion stamp not found: {payload}")
        for module, symbols in required.items():
            library = payload / f"libjalium.native.{module}.dylib"
            if not library.is_file():
                errors.append(f"Native library not found: {library}")
                continue
            result = subprocess.run(
                ["/usr/bin/nm", "-gU", str(library)],
                check=True, capture_output=True, text=True,
            )
            missing = sorted(symbols - set(SYMBOL_RE.findall(result.stdout)))
            if missing:
                errors.append(f"{library.name}: missing native entry points: {', '.join(missing)}")
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        errors.append(f"Cannot validate macOS native payload: {error}")
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        command = args.repo_root.resolve() / "eng" / "apple" / "build-native.sh"
        print(f'Rebuild the native payload: bash "{command}" macos {args.configuration} --development', file=sys.stderr)
        return 1
    print(f"Validated macOS core/platform/Metal entry points in {payload}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
