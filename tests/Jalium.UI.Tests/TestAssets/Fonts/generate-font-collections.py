"""Build two-face TTC and WOFF 2 fixtures from the public-domain WPT Ahem font.

Requires fontTools 4.65.0 and Brotli 1.2.0, matching generate-woff2-fixtures.py.
"""

from pathlib import Path
import struct

import brotli
from fontTools.ttLib import TTCollection, TTFont


folder = Path(__file__).resolve().parent
first_path = folder / "Ahem.ttf"
second_path = folder / "Ahem-Second.ttf"
ttc_path = folder / "Ahem-two-face.ttc"
woff2_path = folder / "Ahem-two-face.woff2"

second = TTFont(first_path, recalcTimestamp=False)
names = second["name"]
names.names = [record for record in names.names if record.nameID != 6]
names.setName("Ahem-Second", 6, 3, 1, 0x0409)
names.setName("Ahem-Second", 6, 1, 0, 0)
x = second.getBestCmap()[ord("X")]
_, bearing = second["hmtx"].metrics[x]
second["hmtx"].metrics[x] = (1500, bearing)
second.save(second_path)
second.close()

collection = TTCollection()
collection.fonts = [TTFont(first_path, recalcTimestamp=False), TTFont(second_path, recalcTimestamp=False)]
collection.save(ttc_path)
for face in collection.fonts:
    face.close()


def sfnt_tables(path):
    data = path.read_bytes()
    flavor = struct.unpack_from(">I", data)[0]
    count = struct.unpack_from(">H", data, 4)[0]
    tables = {}
    for index in range(count):
        tag, _, offset, length = struct.unpack_from(">4sIII", data, 12 + 16 * index)
        tables[tag] = data[offset : offset + length]
    return flavor, tables


def base128(value):
    parts = [value & 0x7f]
    value >>= 7
    while value:
        parts.insert(0, (value & 0x7f) | 0x80)
        value >>= 7
    return bytes(parts)


# The order is the WOFF 2 predefined-tag order. Null glyf/loca transforms
# keep this fixture independent of the glyph reconstruction fixtures.
known_tags = [
    "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf", "loca", "prep", "CFF ", "VORG", "EBDT",
    "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea", "vmtx", "BASE", "GDEF", "GPOS", "GSUB", "EBSC", "JSTF", "MATH",
    "CBDT", "CBLC", "COLR", "CPAL", "SVG ", "sbix", "acnt", "avar", "bdat", "bloc", "bsln", "cvar", "fdsc", "feat", "fmtx", "fvar",
    "gvar", "hsty", "just", "lcar", "mort", "morx", "opbd", "prop", "trak", "Zapf", "Silf", "Glat", "Gloc", "Feat", "Sill",
]
tag_indices = {tag.encode("ascii"): index for index, tag in enumerate(known_tags)}

faces = [sfnt_tables(first_path), sfnt_tables(second_path)]
all_tables = []
indices = {}
face_indices = []
for flavor, tables in faces:
    order = sorted(tables)
    order.remove(b"loca")
    order.insert(order.index(b"glyf") + 1, b"loca")
    face = []
    for tag in order:
        key = (tag, tables[tag])
        if key not in indices:
            indices[key] = len(all_tables)
            all_tables.append(key)
        face.append(indices[key])
    face_indices.append(face)

directory = bytearray()
payload = bytearray()
for tag, data in all_tables:
    index = tag_indices.get(tag, 63)
    version = 3 if tag in (b"glyf", b"loca") else 0
    directory.append((version << 6) | index)
    if index == 63:
        directory.extend(tag)
    directory.extend(base128(len(data)))
    payload.extend(data)

collection_directory = bytearray(struct.pack(">IB", 0x00010000, len(faces)))
for (flavor, _), face in zip(faces, face_indices):
    collection_directory.append(len(face))
    collection_directory.extend(struct.pack(">I", flavor))
    collection_directory.extend(face)

compressed = brotli.compress(bytes(payload))
header = bytearray(48)
struct.pack_into(">4sIIHHII", header, 0, b"wOF2", 0x74746366,
                 48 + len(directory) + len(collection_directory) + len(compressed),
                 len(all_tables), 0, ttc_path.stat().st_size, len(compressed))
struct.pack_into(">H", header, 24, 1)
woff2_path.write_bytes(header + directory + collection_directory + compressed)
print(f"Generated {ttc_path.name} and {woff2_path.name} with distinct PostScript names and X advances.")
