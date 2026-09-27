"""Generate the Unicode 18 CSS text-transform full-width mappings."""

from __future__ import annotations

import urllib.request
from pathlib import Path


VERSION = "18.0.0"
SOURCE = f"https://www.unicode.org/Public/{VERSION}/ucd/UnicodeData.txt"
OUTPUT = (
    Path(__file__).resolve().parents[1]
    / "src/managed/Jalium.UI.Controls/CssUnicodeWidthMappings.cs"
)


def main() -> None:
    with urllib.request.urlopen(SOURCE) as response:
        source = response.read().decode("utf-8")

    mappings: dict[int, int] = {}
    for line in source.splitlines():
        fields = line.split(";")
        codepoint = int(fields[0], 16)
        decomposition = fields[5].split()
        if not decomposition or decomposition[0] not in {"<wide>", "<narrow>"}:
            continue
        if len(decomposition) != 2:
            raise ValueError(f"Unexpected width decomposition: {line}")
        if decomposition[0] == "<wide>":
            source_codepoint, target_codepoint = int(decomposition[1], 16), codepoint
        elif decomposition[0] == "<narrow>":
            source_codepoint, target_codepoint = codepoint, int(decomposition[1], 16)
        else:
            continue
        if source_codepoint in mappings:
            raise ValueError(f"Duplicate full-width mapping: U+{source_codepoint:04X}")
        mappings[source_codepoint] = target_codepoint

    lines = [
        f"// Generated from {SOURCE}; run tools/generate-css-width-mappings.py to refresh.",
        "namespace Jalium.UI.Controls;",
        "",
        "internal static class CssUnicodeWidthMappings",
        "{",
        "    internal static int FullWidth(int codepoint) => codepoint switch",
        "    {",
    ]
    lines.extend(
        f"        0x{source_codepoint:X} => 0x{target_codepoint:X},"
        for source_codepoint, target_codepoint in sorted(mappings.items())
    )
    lines.extend(["        _ => codepoint,", "    };", "}"])
    OUTPUT.write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
