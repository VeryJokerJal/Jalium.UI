"""Generate unconditional Unicode 18 full case mappings for CSS text-transform."""

from __future__ import annotations

import urllib.request
from pathlib import Path


VERSION = "18.0.0"
SOURCE = f"https://www.unicode.org/Public/{VERSION}/ucd/SpecialCasing.txt"
OUTPUT = (
    Path(__file__).resolve().parents[1]
    / "src/managed/Jalium.UI.Controls/CssUnicodeSpecialCasing.cs"
)


def literal(codepoints: str) -> str:
    return '"' + "".join(f"\\U{int(part, 16):08X}" for part in codepoints.split()) + '"'


def main() -> None:
    with urllib.request.urlopen(SOURCE) as response:
        source = response.read().decode("utf-8")

    mappings: dict[str, list[tuple[int, str]]] = {
        "Lower": [], "Title": [], "Upper": [],
    }
    for line in source.splitlines():
        fields = line.split("#", 1)[0].split(";")
        if len(fields) < 5 or fields[4].strip():
            continue  # Conditional mappings are applied with source context.
        codepoint = int(fields[0].strip(), 16)
        for name, field in zip(mappings, fields[1:4]):
            values = field.strip()
            if [int(part, 16) for part in values.split()] != [codepoint]:
                mappings[name].append((codepoint, literal(values)))

    lines = [
        f"// Generated from {SOURCE}; run tools/generate-css-special-casing.py to refresh.",
        "namespace Jalium.UI.Controls;",
        "",
        "internal static class CssUnicodeSpecialCasing",
        "{",
    ]
    for name, entries in mappings.items():
        lines.extend([
            f"    internal static string? {name}(int codepoint) => codepoint switch",
            "    {",
        ])
        lines.extend(
            f"        0x{codepoint:X} => {value}," for codepoint, value in entries
        )
        lines.extend(["        _ => null,", "    };", ""])
    lines.append("}")
    OUTPUT.write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
