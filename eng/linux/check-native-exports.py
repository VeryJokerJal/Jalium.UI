#!/usr/bin/env python3
"""Validate the Linux Jalium shared-library ABI against source declarations."""

from __future__ import annotations

import os
import re
import subprocess
import sys
from pathlib import Path
from typing import Optional


LIBRARIES = {
    "libjalium.native.core.so": "core",
    "libjalium.native.media.core.so": "media_core",
    "libjalium.native.media.so": "media",
    "libjalium.native.platform.so": "platform",
    "libjalium.native.software.so": "software",
    "libjalium.native.text.so": "text",
    "libjalium.native.vulkan.so": "vulkan",
}

MEDIA_CORE_SYMBOLS = {
    "jalium_media_aligned_alloc",
    "jalium_media_aligned_free",
    "jalium_media_status_string",
    "jalium_media_swap_rb_inplace",
}

FIXED_SYMBOLS = {
    "software": {"jalium_software_init"},
    "vulkan": {"jalium_vulkan_init"},
}

API_RE_TEMPLATE = r"\b(?:%s)\b(?:(?![;{}]).)*?\b(jalium_[A-Za-z0-9_]+)\s*\("
COMMENT_RE = re.compile(r"/\*.*?\*/|//[^\r\n]*", re.DOTALL)
CONST_STRING_RE = re.compile(
    r"\b(?:const|static\s+readonly)\s+string\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*\"([^\"]+)\""
)
PINVOKE_RE = re.compile(
    r"\[(?:LibraryImport|DllImport)\(\s*([^,\)]+)(.*?)\)\]"
    r"(?:(?!\[(?:LibraryImport|DllImport)).)*?"
    r"\b([A-Za-z_][A-Za-z0-9_]*)\s*\(",
    re.DOTALL,
)
ENTRYPOINT_RE = re.compile(r"\bEntryPoint\s*=\s*\"(jalium_[A-Za-z0-9_]+)\"")


def fail(message: str) -> None:
    print(message, file=sys.stderr)


def extract_api_symbols(paths: list[Path], macros: tuple[str, ...]) -> set[str]:
    pattern = re.compile(API_RE_TEMPLATE % "|".join(map(re.escape, macros)), re.DOTALL)
    result: set[str] = set()
    for path in paths:
        text = COMMENT_RE.sub(" ", path.read_text(encoding="utf-8", errors="replace"))
        result.update(pattern.findall(text))
    return result


def normalize_library(expression: str, constants: dict[str, str]) -> Optional[str]:
    expression = expression.strip()
    if expression.startswith('"') and expression.endswith('"'):
        value = expression[1:-1]
    else:
        value = constants.get(expression) or constants.get(expression.rsplit(".", 1)[-1])
    if not value:
        return None
    if value.startswith("lib"):
        value = value[3:]
    if value.endswith(".so"):
        value = value[:-3]
    return {
        "jalium.native.core": "core",
        "jalium.native.media.core": "media_core",
        "jalium.native.media": "media",
        "jalium.native.platform": "platform",
        "jalium.native.software": "software",
        "jalium.native.text": "text",
        "jalium.native.vulkan": "vulkan",
    }.get(value)


def extract_pinvokes(repo: Path) -> dict[str, set[str]]:
    listed = subprocess.run(
        ["git", "-C", str(repo), "ls-files", "-co", "--exclude-standard", "--", "src/managed", "tests"],
        check=False,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
    )
    if listed.returncode == 0:
        files = sorted(
            repo / relative for relative in listed.stdout.splitlines()
            if relative.endswith(".cs") and (repo / relative).is_file()
        )
    else:
        files = []
        for root in (repo / "src" / "managed", repo / "tests"):
            files.extend(
                path for path in root.rglob("*.cs")
                if not {"bin", "obj"}.intersection(path.relative_to(root).parts)
            )
        files.sort()
    constants: dict[str, str] = {}
    texts: list[tuple[Path, str]] = []
    for path in files:
        text = COMMENT_RE.sub(" ", path.read_text(encoding="utf-8", errors="replace"))
        texts.append((path, text))
        for name, value in CONST_STRING_RE.findall(text):
            constants[name] = value

    result = {name: set() for name in set(LIBRARIES.values())}
    for _, text in texts:
        for match in PINVOKE_RE.finditer(text):
            library = normalize_library(match.group(1), constants)
            if library is None:
                continue
            entrypoint = ENTRYPOINT_RE.search(match.group(2))
            symbol = entrypoint.group(1) if entrypoint else match.group(3)
            # This validator is for Linux payloads. Android entry points share
            # NativeMethods.cs but are intentionally absent from Linux DSOs.
            if symbol.startswith("jalium_android_"):
                continue
            if symbol.startswith("jalium_"):
                result[library].add(symbol)
    return result


def dynamic_symbols(path: Path) -> tuple[set[str], set[str]]:
    completed = subprocess.run(
        ["readelf", "--dyn-syms", "--wide", str(path)],
        check=True,
        text=True,
        stdout=subprocess.PIPE,
    )
    defined: set[str] = set()
    undefined: set[str] = set()
    for line in completed.stdout.splitlines():
        parts = line.split()
        if len(parts) < 8 or not parts[0].endswith(":"):
            continue
        bind, ndx = parts[4], parts[6]
        if bind not in {"GLOBAL", "WEAK"}:
            continue
        symbol = parts[7].split("@", 1)[0]
        if not symbol or symbol.startswith("JALIUM_"):
            continue
        (undefined if ndx == "UND" else defined).add(symbol)
    return defined, undefined


def demangle(symbols: list[str]) -> dict[str, str]:
    if not symbols:
        return {}
    completed = subprocess.run(
        ["c++filt"],
        input="\n".join(symbols) + "\n",
        check=True,
        text=True,
        stdout=subprocess.PIPE,
    )
    lines = completed.stdout.splitlines()
    if len(lines) != len(symbols):
        raise RuntimeError("c++filt returned an unexpected number of symbols")
    return dict(zip(symbols, lines))


def is_allowed_jalium_cxx(name: str) -> bool:
    return name.startswith(
        (
            "jalium::",
            "typeinfo for jalium::",
            "typeinfo name for jalium::",
            "vtable for jalium::",
            "VTT for jalium::",
            "construction vtable for jalium::",
            "guard variable for jalium::",
            "non-virtual thunk to jalium::",
            "virtual thunk to jalium::",
        )
    )


def main() -> int:
    if len(sys.argv) not in {2, 3}:
        fail("Usage: check-native-exports.py <payload-dir> [repo-root]")
        return 2

    payload = Path(sys.argv[1]).resolve()
    repo = Path(sys.argv[2]).resolve() if len(sys.argv) == 3 else Path(__file__).resolve().parents[2]
    native = repo / "src" / "native"
    if not payload.is_dir():
        fail(f"Native payload directory does not exist: {payload}")
        return 1

    actual_library_names = {path.name for path in payload.glob("*.so")}
    expected_library_names = set(LIBRARIES)
    unexpected_libraries = sorted(actual_library_names - expected_library_names)
    absent_libraries = sorted(expected_library_names - actual_library_names)
    if unexpected_libraries or absent_libraries:
        if unexpected_libraries:
            fail(f"Unexpected shared libraries: {', '.join(unexpected_libraries)}")
        if absent_libraries:
            fail(f"Missing shared libraries: {', '.join(absent_libraries)}")
        return 1

    core_headers = sorted((native / "jalium.native.core" / "include").glob("*.h"))
    platform_headers = sorted((native / "jalium.native.platform" / "include").glob("*.h"))
    media_headers = sorted((native / "jalium.native.media.core" / "include").glob("*.h"))

    header_symbols = {
        "core": extract_api_symbols(core_headers, ("JALIUM_API",)),
        "platform": extract_api_symbols(platform_headers, ("JALIUM_PLATFORM_API",)),
        "media": extract_api_symbols(media_headers, ("JALIUM_MEDIA_API",)),
    }
    header_symbols["media_core"] = set(MEDIA_CORE_SYMBOLS)
    header_symbols["media"] -= MEDIA_CORE_SYMBOLS

    pinvokes = extract_pinvokes(repo)
    # Managed media opens jalium.native.media; ELF symbol lookup is allowed to
    # resolve through its DT_NEEDED media.core dependency. Attribute the four
    # shared helpers to the DSO that actually owns and versions them.
    pinvokes["media_core"].update(pinvokes["media"] & MEDIA_CORE_SYMBOLS)
    pinvokes["media"] -= MEDIA_CORE_SYMBOLS
    allowed = {name: set() for name in set(LIBRARIES.values())}
    required = {name: set() for name in set(LIBRARIES.values())}
    for library in ("core", "platform", "media_core", "media"):
        allowed[library].update(header_symbols[library])
        required[library].update(
            symbol for symbol in header_symbols[library] if not symbol.startswith("jalium_test_")
        )
    for library, symbols in pinvokes.items():
        allowed[library].update(symbols)
        required[library].update(symbols)
    for library, symbols in FIXED_SYMBOLS.items():
        allowed[library].update(symbols)
        required[library].update(symbols)

    all_defined: dict[str, set[str]] = {}
    all_undefined: dict[str, set[str]] = {}
    errors: list[str] = []
    for filename, library in LIBRARIES.items():
        path = payload / filename
        if not path.is_file():
            errors.append(f"missing required shared library: {path}")
            continue
        defined, undefined = dynamic_symbols(path)
        all_defined[library] = defined
        all_undefined[library] = undefined

        mangled = sorted(symbol for symbol in defined if symbol.startswith("_Z"))
        demangled = demangle(mangled)
        c_exports = {symbol for symbol in defined if symbol.startswith("jalium_")}
        unexpected_c = sorted(c_exports - allowed[library])
        if unexpected_c:
            errors.append(f"{filename}: undeclared C ABI exports: {', '.join(unexpected_c)}")

        unexpected_other: list[str] = []
        for symbol in sorted(defined - c_exports):
            if library in {"core", "text"} and symbol in demangled and is_allowed_jalium_cxx(demangled[symbol]):
                continue
            unexpected_other.append(f"{symbol} ({demangled.get(symbol, symbol)})")
        if unexpected_other:
            errors.append(f"{filename}: non-whitelisted exports: {', '.join(unexpected_other)}")

        missing = sorted(required[library] - c_exports)
        if missing:
            errors.append(f"{filename}: declared/PInvoke C ABI missing: {', '.join(missing)}")

        cxx_count = sum(1 for symbol in defined if symbol.startswith("_Z"))
        print(
            f"{filename}: exports={len(defined)} c-abi={len(c_exports)} "
            f"jalium-cxx-abi={cxx_count}"
        )

    # Prove that every Jalium C/C++ undefined reference between the seven DSOs
    # is supplied by another member of this exact payload.
    provided = set().union(*all_defined.values()) if all_defined else set()
    for library, symbols in all_undefined.items():
        unresolved_jalium = sorted(
            symbol for symbol in symbols
            if (symbol.startswith("jalium_") or "6jalium" in symbol) and symbol not in provided
        )
        if unresolved_jalium:
            errors.append(f"{library}: unresolved Jalium cross-library ABI: {', '.join(unresolved_jalium)}")

    if errors:
        for error in errors:
            fail(f"ERROR: {error}")
        return 1

    print(
        f"Validated {len(LIBRARIES)} shared libraries against public headers, "
        "managed P/Invokes, and the Jalium cross-library C++ ABI."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
