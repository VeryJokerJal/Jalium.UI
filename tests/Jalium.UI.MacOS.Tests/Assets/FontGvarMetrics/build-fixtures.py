"""Offline gvar phantom-point fixtures using Jalium's MIT geometric outlines."""
from pathlib import Path
import hashlib
import json
import shutil
import struct

root = Path(__file__).resolve().parent
p16 = lambda *v: struct.pack('>' + 'H' * len(v), *v)
s16 = lambda *v: struct.pack('>' + 'h' * len(v), *v)
p32 = lambda *v: struct.pack('>' + 'I' * len(v), *v)
u16 = lambda b, p: struct.unpack_from('>H', b, p)[0]
u32 = lambda b, p: struct.unpack_from('>I', b, p)[0]

def checksum(b):
    b = bytes(b) + b'\0' * (-len(b) % 4)
    return sum(struct.unpack('>' + 'I' * (len(b) // 4), b)) & 0xffffffff

def read_font(name):
    p = root.parent/'FontSizeAdjust'/(name + '.ttf')
    b = p.read_bytes()
    manifest = json.loads((p.parent/'PROVENANCE.json').read_text())
    digest = hashlib.sha256(b).hexdigest()
    assert digest == next(x['sha256'] for x in manifest['files'] if x['file'] == p.name)
    return {b[12+16*i:16+16*i]: b[u32(b, 20+16*i):u32(b, 20+16*i)+u32(b, 24+16*i)]
            for i in range(u16(b, 4))}, digest

def sfnt(tables):
    tables = {k: bytearray(v) for k, v in tables.items()}
    struct.pack_into('>I', tables[b'head'], 8, 0)
    count = len(tables)
    power = 1 << (count.bit_length() - 1)
    prefix = p32(0x10000) + p16(count, power*16, power.bit_length()-1, count*16-power*16)
    records = []
    body = bytearray()
    offset = 12 + count*16
    for tag, value in sorted(tables.items()):
        if tag == b'head': head = offset
        records.append(tag + p32(checksum(value), offset, len(value)))
        body += value + b'\0' * (-len(value) % 4)
        offset = 12 + count*16 + len(body)
    b = bytearray(prefix + b''.join(records) + body)
    struct.pack_into('>I', b, head+8, (0xb1b0afba-checksum(b)) & 0xffffffff)
    assert checksum(b) == 0xb1b0afba
    return b

def points(ids, words=False):
    if ids is None: return b'\0'
    assert 0 < len(ids) < 128
    deltas = [ids[0]] + [b-a for a, b in zip(ids, ids[1:])]
    return bytes([len(ids), (0x80 if words else 0) | (len(ids)-1)]) + (p16(*deltas) if words else bytes(deltas))

def deltas(values):
    # Deliberately include zero, signed byte and signed word runs.
    out = bytearray()
    kind = lambda value: 0 if value == 0 else 1 if -128 <= value <= 127 else 2
    at = 0
    while at < len(values):
        end = at+1
        while end < len(values) and end-at < 64 and kind(values[end]) == kind(values[at]): end += 1
        run = values[at:end]
        if kind(run[0]) == 0: out.append(0x80 | (len(run)-1))
        elif kind(run[0]) == 1: out += bytes([len(run)-1]) + bytes(value & 255 for value in run)
        else: out += bytes([0x40 | (len(run)-1)]) + s16(*run)
        at = end
    return bytes(out)

def glyph_data(point_count, kind, axes, amount):
    total = point_count + 4
    top, bottom = point_count+2, point_count+3
    ids = [top, bottom]
    if kind == 'PhantomAllPoints': ids = None
    elif kind == 'PhantomTopOnly': ids = [top]
    elif kind == 'PhantomBottomOnly': ids = [bottom]
    elif kind == 'PhantomUnreferenced': ids = [0]
    shared = kind == 'PhantomShared'
    intermediate = kind == 'PhantomIntermediate'
    indices = list(range(total)) if ids is None else ids
    headers, payloads = [], []
    for i, sign in enumerate([-1, 1]):
        if kind == 'PhantomWithVvar' and sign < 0: continue
        values = []
        for at in indices:
            if kind == 'PhantomTopOnly': value = sign*amount
            elif kind == 'PhantomBottomOnly': value = -sign*amount
            elif kind == 'PhantomUnreferenced': value = sign*amount
            elif at == top: value = sign*(amount//2)
            elif at == bottom: value = -sign*(amount-amount//2)
            else: value = 0
            values.append(value)
        payload = (b'' if shared else points(ids, words=kind == 'PhantomBottomOnly')) + deltas([0]*len(indices)) + deltas(values)
        flags = i if shared else 0x8000 | 0x2000
        peak = b'' if shared else s16(*([sign*(8192 if intermediate else 16384)]*axes))
        if intermediate:
            flags |= 0x4000
            peak += s16(*([(-12288 if sign < 0 else 4096)]*axes))
            peak += s16(*([(-4096 if sign < 0 else 12288)]*axes))
        headers.append(p16(len(payload), flags) + peak)
        payloads.append(payload)
    data_offset = 4 + sum(map(len, headers))
    return p16(len(headers) | (0x8000 if shared else 0), data_offset) + b''.join(headers) + (points(ids) if shared else b'') + b''.join(payloads)

def gvar(glyphs, kind, axes, count):
    long = kind != 'PhantomShortOffsets'
    shared = kind == 'PhantomShared'
    tuples = s16(-16384, 16384) if shared else b''
    header_end = 20 + (count+1)*(4 if long else 2)
    data_start = (header_end + len(tuples) + 3) & ~3
    offsets, data = [0], b''
    for gid in range(count):
        chunk = glyphs.get(gid, b'')
        data += chunk + b'\0' * (-len(chunk) % 2)
        offsets.append(len(data))
    header = p16(1, 0, axes, 2 if shared else 0) + p32(header_end if shared else 0) + p16(count, int(long)) + p32(data_start)
    header += p32(*offsets) if long else p16(*(n//2 for n in offsets))
    return header + tuples + b'\0'*(data_start-len(header)-len(tuples)) + data

names = ['PhantomEmbedded', 'PhantomShared', 'PhantomAllPoints', 'PhantomShortOffsets',
         'PhantomTopOnly', 'PhantomBottomOnly', 'PhantomUnreferenced', 'PhantomIntermediate',
         'PhantomComposite', 'PhantomUseMyMetrics', 'PhantomLastMetrics', 'PhantomAvar',
         'PhantomAvar2', 'PhantomMultiAxis', 'PhantomWithVvar', 'ZeroAdvance', 'MissingVertical']
rows = []
for name in names:
    source = 'VerticalAvar' if name == 'PhantomAvar' else 'VerticalAvar2' if name == 'PhantomAvar2' else 'VerticalVariable'
    tables, source_sha = read_font(source)
    if name != 'PhantomWithVvar': tables.pop(b'VVAR', None)
    count = u16(tables[b'maxp'], 4)
    axes = 2 if name == 'PhantomMultiAxis' else 1
    if axes == 2:
        tables[b'fvar'] = p16(1, 0, 16, 2, 2, 20, 0, 12) + tables[b'fvar'][16:36] + b'wdth' + struct.pack('>iii', 50*65536, 100*65536, 150*65536) + p16(0, 257)
    composite = name in ['PhantomComposite', 'PhantomUseMyMetrics', 'PhantomLastMetrics']
    if composite:
        original = tables[b'glyf']
        offsets = [u32(tables[b'loca'], 4*i) for i in range(count+1)]
        first = 3 | 0x20 | (0x200 if name != 'PhantomComposite' else 0)
        last = 3 | (0x200 if name == 'PhantomLastMetrics' else 0)
        water = s16(-1, 0, 0, 1150, 750) + p16(first, 2) + s16(0, 0) + p16(last, 5) + s16(650, 0)
        new_glyf, new_offsets = b'', [0]
        for gid in range(count):
            item = water if gid == 7 else original[offsets[gid]:offsets[gid+1]]
            new_glyf += item + b'\0' * (-len(item) % 4)
            new_offsets.append(len(new_glyf))
        tables[b'glyf'], tables[b'loca'] = new_glyf, p32(*new_offsets)
        maxp = bytearray(tables[b'maxp'])
        for at, value in [(10, 8), (12, 2), (28, 2), (30, 1)]: struct.pack_into('>H', maxp, at, value)
        tables[b'maxp'] = maxp
        head = bytearray(tables[b'head']); struct.pack_into('>h', head, 40, 1150); tables[b'head'] = head
    glyphs = {2:glyph_data(4, name, axes, 200), 7:glyph_data(2 if composite else 4, name, axes, 300 if composite else 200)}
    if name == 'PhantomWithVvar': glyphs.pop(2)
    tables[b'gvar'] = gvar(glyphs, name, axes, count)
    if name == 'PhantomShortOffsets':
        tables[b'loca'] = p16(*(u32(tables[b'loca'], 4*i)//2 for i in range(count+1)))
        head = bytearray(tables[b'head']); struct.pack_into('>h', head, 50, 0); tables[b'head'] = head
    if name in ['ZeroAdvance', 'MissingVertical']:
        for tag in [b'gvar', b'fvar', b'VVAR']: tables.pop(tag, None)
        if name == 'MissingVertical':
            for tag in [b'vhea', b'vmtx']: tables.pop(tag, None)
        else:
            vmtx = bytearray(tables[b'vmtx'])
            for gid in [2, 7]: struct.pack_into('>H', vmtx, gid*4, 0)
            tables[b'vmtx'] = vmtx
    strings, records = b'', []
    for ident, value in [(1,'Jalium '+name), (2,'Regular'), (4,'Jalium '+name+' Regular'), (6,'Jalium'+name), (5,'Version 1.0')]:
        b = value.encode('utf-16-be'); records.append(p16(3, 1, 0x409, ident, len(b), len(strings))); strings += b
    tables[b'name'] = p16(0, len(records), 6+len(records)*12) + b''.join(records) + strings
    data = sfnt(tables)
    (root/(name+'.ttf')).write_bytes(data)
    rows.append({'file':name+'.ttf', 'sha256':hashlib.sha256(data).hexdigest(), 'source':'FontSizeAdjust/'+source+'.ttf', 'sourceSha256':source_sha, 'unitsPerEm':1000, 'baseVerticalAdvance':0 if name == 'ZeroAdvance' else None if name == 'MissingVertical' else 1200, 'axes':axes if name not in ['ZeroAdvance','MissingVertical'] else 0, 'gvarPhantoms':name not in ['ZeroAdvance','MissingVertical'], 'composite':composite})
shutil.copy2(root.parent/'FontSizeAdjust/LICENSE.md', root/'LICENSE.md')
(root/'PROVENANCE.json').write_text(json.dumps({'origin':'Original Jalium MIT geometric contours; gvar programs, metric tables and names generated offline using only Python stdlib.', 'files':rows}, indent=2)+'\n')
print('Generated', len(rows), 'gvar and zero/missing-metric fixtures')
