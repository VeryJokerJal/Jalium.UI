"""Deterministic geometric text-combine fixtures, using only Python's stdlib."""
from pathlib import Path
import hashlib, json, struct

root = Path(__file__).resolve().parent
u16 = lambda *v: struct.pack('>' + 'H' * len(v), *v)
s16 = lambda *v: struct.pack('>' + 'h' * len(v), *v)
u32 = lambda *v: struct.pack('>' + 'I' * len(v), *v)

def checksum(data):
    data = bytes(data) + b'\0' * (-len(data) % 4)
    return sum(struct.unpack('>' + 'I' * (len(data) // 4), data)) & 0xffffffff

def rectangle(width, height):
    return s16(1, 0, 0, width, height) + u16(3, 0) + b'\1' * 4 + s16(0, width, 0, -width) + s16(0, 0, height, 0)

def gsub(features, partial, empty):
    tags = sorted(features)
    lookups = []
    for tag in tags:
        pairs = [] if empty else [(1, {'hwid': 3, 'twid': 4, 'qwid': 5}[tag])]
        if not partial and not empty: pairs.append((2, {'hwid': 3, 'twid': 4, 'qwid': 5}[tag]))
        coverage = u16(1, len(pairs), *(source for source, _ in pairs))
        sub = u16(2, 6 + len(pairs) * 2, len(pairs), *(target for _, target in pairs)) + coverage
        lookups.append(u16(1, 0, 1, 8) + sub)
    offsets = []; at = 2 + len(lookups) * 2
    for lookup in lookups: offsets.append(at); at += len(lookup)
    lookup_list = u16(len(lookups), *offsets) + b''.join(lookups)
    feature_list = u16(len(tags)) + b''.join(tag.encode() + u16(2 + len(tags)*6 + i*6) for i, tag in enumerate(tags)) + b''.join(u16(0, 1, i) for i in range(len(tags)))
    script = u16(4, 0, 0, 0xffff, len(tags), *range(len(tags)))
    script_list = u16(2) + b'DFLT' + u16(14) + b'latn' + u16(14) + script
    return u16(1, 0, 10, 10+len(script_list), 10+len(script_list)+len(feature_list)) + script_list + feature_list + lookup_list

def make(name, features, partial=False, empty=False, narrow=False):
    glyphs = [rectangle(400, 700), rectangle(900, 700), rectangle(850, 700), rectangle(400, 550), rectangle(250, 600), rectangle(150, 650), b'']
    advances = [600, 400 if narrow else 1000, 400 if narrow else 1000, 500, 333, 250, 250]
    loca = [0]; glyf = b''
    for glyph in glyphs: glyf += glyph + b'\0' * (-len(glyph) % 4); loca.append(len(glyf))
    n = len(glyphs)
    tables = {b'glyf': glyf, b'loca': u32(*loca), b'hmtx': b''.join(u16(a)+s16(0) for a in advances), b'vmtx': b''.join(u16(1200)+s16(0) for _ in glyphs)}
    tables[b'head'] = u32(0x10000, 0x10000, 0, 0x5f0f3cf5)+u16(3, 1000)+struct.pack('>QQ', 0, 0)+s16(0, 0, 1000, 750)+u16(0, 8)+s16(2, 1, 0)
    tables[b'hhea'] = u32(0x10000)+s16(750, -250, 0)+u16(1000)+s16(0, 0, 1000, 1, 0, 0, 0, 0, 0, 0, 0)+u16(n)
    tables[b'vhea'] = u32(0x11000)+s16(750, -250, 0)+u16(1200)+s16(0, 0, 1000, 0, 1, 0, 0, 0, 0, 0, 0)+u16(n)
    tables[b'maxp'] = u32(0x10000)+u16(n, 4, 1, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0)
    os2 = bytearray(96); struct.pack_into('>HHHH', os2, 0, 4, 500, 400, 5); os2[58:62] = b'JALM'
    struct.pack_into('>HHHhhhHH', os2, 62, 0xc0, 32, 0xff5e, 750, -250, 0, 750, 250)
    struct.pack_into('>hhHHH', os2, 86, 500, 700, 0, 32, 1); tables[b'OS/2'] = os2
    chars = [(cp, 1 if cp in (49, 65, 0xff11, 0xff21) else 2) for cp in list(range(48,58))+[65,66,0x6c34,0xff11,0xff12,0xff21,0xff22]] + [(32,6)]
    chars.sort(); sub = u16(12,0)+u32(16+len(chars)*12,0,len(chars))+b''.join(u32(cp,cp,gid) for cp,gid in chars)
    tables[b'cmap'] = u16(0,1,3,10)+u32(12)+sub
    pool = b''; records = []
    for ident, value in [(1,'Jalium'+name),(2,'Regular'),(4,'Jalium'+name),(6,'Jalium'+name),(5,'Version 1.0')]:
        data=value.encode('utf-16-be');records.append(u16(3,1,0x409,ident,len(data),len(pool)));pool+=data
    tables[b'name']=u16(0,len(records),6+12*len(records))+b''.join(records)+pool
    tables[b'post']=u32(0x30000,0)+s16(-100,50)+u32(0,0,0,0,0)
    if features: tables[b'GSUB']=gsub(features,partial,empty)
    count=len(tables);power=1<<(count.bit_length()-1);directory=u32(0x10000)+u16(count,power*16,power.bit_length()-1,count*16-power*16);body=b'';head=0
    for tag,value in sorted(tables.items()):
        at=12+count*16+len(body)
        if tag==b'head':head=at
        directory+=tag+u32(checksum(value),at,len(value));body+=value+b'\0'*(-len(value)%4)
    data=bytearray(directory+body);struct.pack_into('>I',data,head+8,(0xb1b0afba-checksum(data))&0xffffffff);assert checksum(data)==0xb1b0afba
    path=root/(name+'.ttf');path.write_bytes(data)
    return {'file':path.name,'sha256':hashlib.sha256(data).hexdigest(),'features':features,'partial':partial,'empty':empty,'narrow':narrow,'glyphAdvances':advances,'unitsPerEm':1000,'ascent':750,'descent':250,'verticalAdvance':1200}

rows=[make('CombineWidths',['hwid','twid','qwid']),make('CombineHalf',['hwid']),make('CombinePartial',['hwid','twid','qwid'],partial=True),make('CombineNone',[]),make('CombineEmpty',['hwid','twid','qwid'],empty=True),make('CombineNarrow',[],narrow=True)]
(root/'PROVENANCE.json').write_text(json.dumps({'origin':'Original geometric outlines and OpenType tables generated offline without external packages or fonts.','files':rows},indent=2)+'\n')
print('Generated',len(rows),'text-combine fonts')
