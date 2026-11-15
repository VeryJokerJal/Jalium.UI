from pathlib import Path
from fontTools.ttLib import TTFont, woff2
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib.tables.ttProgram import Program
import brotli
import struct

folder = Path(__file__).resolve().parent
source = folder / 'Ahem.ttf'
for name, transforms in [('Ahem-transformed', {'glyf', 'loca'}), ('Ahem-null', set()), ('Ahem-hmtx', {'glyf', 'loca', 'hmtx'}), ('Ahem-hmtx-only', {'hmtx'})]:
    woff2.compress(source, folder / (name + '.woff2'), transform_tables=transforms)

font = TTFont(source, recalcTimestamp=False)
glyphs = font['glyf']
names = font.getBestCmap()
a, b, c = [names[ord(character)] for character in 'ABC']
pen = TTGlyphPen(None)
pen.moveTo((-1200, -700))
pen.lineTo((12, -700))
pen.qCurveTo((29, -37), (1500, 1300))
pen.lineTo((-40, 1500))
pen.closePath()
pen.moveTo((0, 0)); pen.lineTo((1, 0)); pen.lineTo((1, 1)); pen.closePath()
glyphs[a] = pen.glyph()
glyphs[a].flags[0] |= 0x40
glyphs[a].program = Program(); glyphs[a].program.fromBytecode([0x00])
pen = TTGlyphPen(glyphs)
pen.addComponent(a, (1.25, 0, 0, 1.25, 50, -20))
pen.addComponent(c, (1, .25, .125, 1, -400, 300))
glyphs[b] = pen.glyph()
glyphs[b].program = Program(); glyphs[b].program.fromBytecode([0x01])
font['head'].indexToLocFormat = 1
font.save(folder / 'Woff2Shapes.ttf')
woff2.compress(folder / 'Woff2Shapes.ttf', folder / 'Woff2Shapes.woff2', transform_tables={'glyf', 'loca', 'hmtx'})

# A collection with two faces sharing all tables. Its physical table order is
# explicitly adjusted to the WOFF 2 collection glyf/loca adjacency requirement.
raw = (folder / 'Ahem-hmtx.woff2').read_bytes()
position = 48
def base128():
    global position
    value = 0
    while True:
        byte = raw[position]; position += 1
        value = value * 128 + (byte & 127)
        if byte < 128: return value
entries = []
for _ in range(struct.unpack_from('>H', raw, 12)[0]):
    start = position; flag = raw[position]; position += 1
    index = flag & 63
    if index == 63: position += 4
    original = base128()
    transformed = (flag >> 6) == 0 if index in (10, 11) else (flag >> 6) != 0
    length = base128() if transformed else original
    entries.append([index, raw[start:position], length, None])
payload = brotli.decompress(raw[position:position + struct.unpack_from('>I', raw, 20)[0]])
at = 0
for entry in entries:
    entry[3] = payload[at:at + entry[2]]; at += entry[2]
location = next(entry for entry in entries if entry[0] == 11)
entries.remove(location)
entries.insert(next(i for i, entry in enumerate(entries) if entry[0] == 10) + 1, location)
directory = b''.join(entry[1] for entry in entries)
font_entry = bytes([len(entries)]) + raw[4:8] + bytes(range(len(entries)))
collection = struct.pack('>I', 0x00010000) + bytes([2]) + font_entry * 2
compressed = brotli.compress(b''.join(entry[3] for entry in entries))
header = bytearray(raw[:48])
struct.pack_into('>I', header, 4, 0x74746366)
struct.pack_into('>I', header, 8, 48 + len(directory) + len(collection) + len(compressed))
struct.pack_into('>I', header, 20, len(compressed))
(folder / 'Ahem-collection.woff2').write_bytes(header + directory + collection + compressed)
print('Generated independent WOFF 2 fixtures with fontTools.')
