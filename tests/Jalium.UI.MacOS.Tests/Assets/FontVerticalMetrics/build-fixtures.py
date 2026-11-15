"""Deterministic, offline metric/context fixtures from Jalium's MIT outlines."""
from pathlib import Path
import hashlib
import json
import shutil
import struct

root = Path(__file__).resolve().parent
p16 = lambda *v: struct.pack('>' + 'H' * len(v), *v)
s16 = lambda *v: struct.pack('>' + 'h' * len(v), *v)
p32 = lambda *v: struct.pack('>' + 'I' * len(v), *v)
u16 = lambda b, at: struct.unpack_from('>H', b, at)[0]
u32 = lambda b, at: struct.unpack_from('>I', b, at)[0]

def checksum(b):
    b = bytes(b) + b'\0' * (-len(b) % 4)
    return sum(struct.unpack('>' + 'I' * (len(b) // 4), b)) & 0xffffffff

def coverage(glyph):
    return p16(1, 1, glyph)

def lookup(kind, sub):
    return p16(kind, 0, 1, 8) + sub

def layout(tag, selected, lookups):
    language = p16(0, 0xffff, 1, 0)
    scripts = p16(1) + b'DFLT' + p16(8) + p16(4, 0) + language
    features = p16(1) + tag + p16(8) + p16(0, 1, selected)
    offsets = []; at = 2 + 2 * len(lookups)
    for item in lookups:
        offsets.append(at); at += len(item)
    listing = p16(len(lookups), *offsets) + b''.join(lookups)
    return p16(1, 0, 10, 10 + len(scripts), 10 + len(scripts) + len(features)) + scripts + features + listing

def sfnt(tables):
    tables = {k: bytearray(v) for k, v in tables.items()}
    struct.pack_into('>I', tables[b'head'], 8, 0)
    count = len(tables); power = 1 << (count.bit_length() - 1)
    header = p32(0x10000) + p16(count, power * 16, power.bit_length() - 1, count * 16 - power * 16)
    records = []; body = bytearray(); offset = 12 + count * 16
    for tag, value in sorted(tables.items()):
        if tag == b'head': head = offset
        records.append(tag + p32(checksum(value), offset, len(value)))
        body += value; body += b'\0' * (-len(value) % 4)
        offset = 12 + count * 16 + len(body)
    result = bytearray(header + b''.join(records) + body)
    struct.pack_into('>I', result, head + 8, (0xb1b0afba - checksum(result)) & 0xffffffff)
    assert checksum(result) == 0xb1b0afba
    return result

source = root.parent / 'FontSizeAdjust/Primary.ttf'
original = source.read_bytes(); source_sha = hashlib.sha256(original).hexdigest()
manifest = json.loads((source.parent / 'PROVENANCE.json').read_text())
assert source_sha == next(x['sha256'] for x in manifest['files'] if x['file'] == source.name)
base = {original[12 + 16*i:16 + 16*i]: original[u32(original, 20 + 16*i):u32(original, 20 + 16*i) + u32(original, 24 + 16*i)] for i in range(u16(original, 4))}
scalars = [(32, 1), (65, 2), (0x3002, 4), (0x3099, 3), (0x4e2d, 2), (0x56fd, 5), (0x6587, 6)]
rows = []
for name in ['MetricsMixed', 'MetricsKern', 'MetricsLigature', 'MetricsContext', 'MetricsMultiple', 'MetricsMark']:
    tables = dict(base)
    sub = p16(12, 0) + p32(16 + len(scalars)*12, 0, len(scalars)) + b''.join(p32(cp, cp, gid) for cp, gid in scalars)
    tables[b'cmap'] = p16(0, 1, 3, 10) + p32(12) + sub
    glyph_count = u16(tables[b'maxp'], 4)
    advances = {2: 1200, 3: 0, 4: 800, 5: 1600, 6: 600}
    tables[b'vmtx'] = b''.join(p16(advances.get(g, 1200)) + tables[b'vmtx'][g*4+2:g*4+4] for g in range(glyph_count))
    hmtx = bytearray(tables[b'hmtx']); struct.pack_into('>H', hmtx, 12, 0); tables[b'hmtx'] = hmtx
    if name == 'MetricsKern':
        pair = p16(1, 12, 8, 0, 1, 18) + coverage(2) + p16(1, 6) + s16(-100)
        tables[b'GPOS'] = layout(b'vkrn', 0, [lookup(2, pair)])
    if name == 'MetricsLigature':
        sub = p16(1, 18, 1, 8) + p16(1, 4) + p16(5, 2, 2) + coverage(2)
        tables[b'GSUB'] = layout(b'liga', 0, [lookup(4, sub)])
    if name == 'MetricsContext':
        single = lookup(1, p16(2, 8, 1, 4) + coverage(2))
        context = lookup(6, p16(3, 0, 2, 18, 24, 0, 1, 0, 0) + coverage(2) + coverage(6))
        tables[b'GSUB'] = layout(b'calt', 1, [single, context])
    if name == 'MetricsMultiple':
        tables[b'GSUB'] = layout(b'ccmp', 0, [lookup(2, p16(1, 14, 1, 8) + p16(2, 4, 6) + coverage(2))])
    if name == 'MetricsMark':
        marks = p16(1, 0, 6) + p16(1, 100, 100)
        bases = p16(1, 4) + p16(1, 300, 650)
        sub = p16(1, 12, 18, 1, 24, 36) + coverage(3) + coverage(2) + marks + bases
        tables[b'GPOS'] = layout(b'mark', 0, [lookup(4, sub)])
        tables[b'GDEF'] = p32(0x10000) + p16(12, 0, 0, 0) + p16(2, 2, 2, 2, 1, 3, 3, 3)
    strings = b''; records = []
    for ident, value in [(1, 'Jalium '+name), (2, 'Regular'), (4, 'Jalium '+name+' Regular'), (6, 'Jalium'+name), (5, 'Version 1.0')]:
        value = value.encode('utf-16-be'); records.append(p16(3, 1, 0x409, ident, len(value), len(strings))); strings += value
    tables[b'name'] = p16(0, len(records), 6 + len(records)*12) + b''.join(records) + strings
    result = sfnt(tables); p = root/(name+'.ttf'); p.write_bytes(result)
    rows.append({'file':p.name, 'sha256':hashlib.sha256(result).hexdigest(), 'source':'FontSizeAdjust/Primary.ttf', 'sourceSha256':source_sha, 'unitsPerEm':1000, 'verticalAdvances':advances, 'scalars':scalars})
shutil.copy2(source.parent/'LICENSE.md', root/'LICENSE.md')
(root/'PROVENANCE.json').write_text(json.dumps({'origin':'Jalium MIT geometric outlines with nonuniform vertical metrics and independent GSUB/GPOS programs; generated offline.', 'files':rows}, indent=2)+'\n')
print('Generated', len(rows), 'vertical metric/context fixtures')
