"""Generate CSS encoding labels and legacy decoder indexes from WHATWG data.

The generated C# source is checked in; runtime stylesheet loading never fetches
the standard's data files.
"""

from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path
from urllib.request import urlopen


COMMIT = "a985b62a9b45c17da3e17a9f0a0b4e30c34c4a8"
BASE = f"https://raw.githubusercontent.com/whatwg/encoding/{COMMIT}/"
OUTPUT = Path(__file__).resolve().parents[1] / (
    "src/managed/Jalium.UI.Core/Css/CssStyleSheet.Encoding.Data.g.cs"
)


def get(name: str) -> bytes:
    with urlopen(BASE + name, timeout=30) as response:
        return response.read()


def single_byte_table(name: str) -> str:
    index_name = "iso-8859-8" if name == "ISO-8859-8-I" else name.lower()
    source = get(f"index-{index_name}.txt").decode("utf-8")
    points = [0xFFFD] * 128
    seen: set[int] = set()
    for line in source.splitlines():
        match = re.match(r"\s*(\d+)\s+0x([0-9a-fA-F]+)\b", line)
        if match:
            pointer, point = int(match[1]), int(match[2], 16)
            if not (0 <= pointer < 128 and point <= 0xFFFF) or pointer in seen:
                raise ValueError(f"unsupported index entry: {name}: {line}")
            seen.add(pointer)
            points[pointer] = point
    if not seen:
        raise ValueError(f"empty single-byte index: {name}")
    return "".join(f"\\u{point:04X}" for point in points)


def gb18030_index() -> str:
    source = get("index-gb18030.txt").decode("utf-8")
    points = [0xFFFD] * 23940
    seen: set[int] = set()
    for line in source.splitlines():
        match = re.match(r"\s*(\d+)\s+0x([0-9a-fA-F]+)\b", line)
        if match:
            pointer, point = int(match[1]), int(match[2], 16)
            if not (0 <= pointer < len(points) and point <= 0xFFFF) or pointer in seen:
                raise ValueError(f"unsupported GB18030 index entry: {line}")
            seen.add(pointer)
            points[pointer] = point
    if len(seen) != len(points):
        raise ValueError(f"GB18030 index has {len(seen)} of {len(points)} pointers")
    return "".join(f"\\u{point:04X}" for point in points)


def gb18030_ranges() -> list[tuple[int, int]]:
    source = get("index-gb18030-ranges.txt").decode("utf-8")
    ranges: list[tuple[int, int]] = []
    for line in source.splitlines():
        match = re.match(r"\s*(\d+)\s+0x([0-9a-fA-F]+)\b", line)
        if match:
            ranges.append((int(match[1]), int(match[2], 16)))
    if len(ranges) != 207 or ranges[0] != (0, 0x80) or ranges[-1] != (189000, 0x10000):
        raise ValueError("unexpected GB18030 range data")
    if any(left[0] >= right[0] for left, right in zip(ranges, ranges[1:])):
        raise ValueError("GB18030 range pointers are not increasing")
    return ranges


def big5_index() -> list[int]:
    source = get("index-big5.txt").decode("utf-8")
    points = [0] * 19782
    seen: set[int] = set()
    for line in source.splitlines():
        match = re.match(r"\s*(\d+)\s+0x([0-9a-fA-F]+)\b", line)
        if match:
            pointer, point = int(match[1]), int(match[2], 16)
            if not (0 <= pointer < len(points) and 0 < point <= 0x10FFFF) or pointer in seen:
                raise ValueError(f"unsupported Big5 index entry: {line}")
            seen.add(pointer)
            points[pointer] = point
    if len(seen) != 18590:
        raise ValueError(f"Big5 index has {len(seen)} rather than 18590 pointers")
    return points


def sparse_bmp_index(name: str, slots: int, entries: int) -> str:
    source = get(f"index-{name}.txt").decode("utf-8")
    points = [0] * slots
    seen: set[int] = set()
    for line in source.splitlines():
        match = re.match(r"\s*(\d+)\s+0x([0-9a-fA-F]+)\b", line)
        if match:
            pointer, point = int(match[1]), int(match[2], 16)
            if not (0 <= pointer < slots and 0 < point <= 0xFFFF) or pointer in seen:
                raise ValueError(f"unsupported {name} index entry: {line}")
            seen.add(pointer)
            points[pointer] = point
    if len(seen) != entries:
        raise ValueError(f"{name} index has {len(seen)} rather than {entries} entries")
    return "".join(f"\\u{point:04X}" for point in points)


def append_string_constant(lines: list[str], name: str, table: str) -> None:
    lines.extend(["", f"    internal const string {name} ="])
    chunks = [table[i:i + 128 * 6] for i in range(0, len(table), 128 * 6)]
    for index, chunk in enumerate(chunks):
        suffix = ";" if index == len(chunks) - 1 else " +"
        lines.append(f'        "{chunk}"{suffix}')


def main() -> None:
    source = get("encodings.json")
    groups = json.loads(source)
    labels: list[tuple[str, str]] = []
    tables: list[tuple[str, str]] = []
    for group in groups:
        for encoding in group["encodings"]:
            name = encoding["name"]
            labels.extend((label, name) for label in encoding["labels"])
            if group["heading"] == "Legacy single-byte encodings":
                tables.append((name, single_byte_table(name)))

    labels.sort(key=lambda item: item[0])
    tables.sort(key=lambda item: item[0])
    if len({label for label, _ in labels}) != len(labels):
        raise ValueError("duplicate WHATWG encoding label")
    lines = [
        "// Generated by tools/generate-css-encoding-data.py from WHATWG Encoding data.",
        f"// Source commit: {COMMIT}",
        f"// encodings.json SHA-256: {hashlib.sha256(source).hexdigest()}",
        "// https://encoding.spec.whatwg.org/",
        "namespace Jalium.UI.Styling;",
        "",
        "internal static class CssStyleSheetEncodingData",
        "{",
        "    internal static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)",
        "    {",
    ]
    lines.extend(f'        ["{label}"] = "{name}",' for label, name in labels)
    lines.extend([
        "    };",
        "",
        "    internal static string? SingleByteTable(string canonicalName) => canonicalName switch",
        "    {",
    ])
    lines.extend(f'        "{name}" => "{table}",' for name, table in tables)
    lines.extend(["        _ => null,", "    };"])
    gb_index = gb18030_index()
    append_string_constant(lines, "Gb18030Index", gb_index)
    lines.extend([
        "",
        "    internal static readonly (int Pointer, int CodePoint)[] Gb18030Ranges =",
        "    [",
    ])
    ranges = gb18030_ranges()
    lines.extend(f"        ({pointer}, 0x{point:X})," for pointer, point in ranges)
    lines.extend([
        "    ];",
        "",
        "    internal static readonly int[] Big5Index =",
        "    [",
    ])
    big5 = big5_index()
    for start in range(0, len(big5), 16):
        lines.append("        " + ", ".join(f"0x{point:X}" for point in big5[start:start + 16]) + ",")
    lines.append("    ];")
    euc_kr = sparse_bmp_index("euc-kr", 23750, 17048)
    jis0208 = sparse_bmp_index("jis0208", 11104, 7724)
    jis0212 = sparse_bmp_index("jis0212", 7211, 6067)
    append_string_constant(lines, "EucKrIndex", euc_kr)
    append_string_constant(lines, "Jis0208Index", jis0208)
    append_string_constant(lines, "Jis0212Index", jis0212)
    lines.extend(["}", ""])
    OUTPUT.write_text("\n".join(lines), encoding="utf-8", newline="\n")
    print(f"wrote {OUTPUT} ({len(labels)} labels, {len(tables)} single-byte indexes, "
          f"{len(gb_index) // 6} GB18030 mappings, {len(ranges)} ranges, "
          f"{len(big5)} Big5 slots, {len(euc_kr) // 6} EUC-KR slots, "
          f"{len(jis0208) // 6} JIS0208 slots, {len(jis0212) // 6} JIS0212 slots)")


if __name__ == "__main__":
    main()
