"""Derive caps-availability fixtures without changing any outlines or metrics."""
from pathlib import Path
import hashlib
import json
import shutil
import struct

root = Path(__file__).resolve().parent
source = root.parent / 'FontNumeric/FontWithFancyFeatures.otf'
original = source.read_bytes()
expected = '0f7e550009d5d7348fdbaf79365e9cdbe010feb04e3af00bacc49f825e1f93f2'
assert hashlib.sha256(original).hexdigest() == expected

def derive(renames):
    data = bytearray(original)
    u16 = lambda at: struct.unpack_from('>H', data, at)[0]
    u32 = lambda at: struct.unpack_from('>I', data, at)[0]
    put16 = lambda at, value: struct.pack_into('>H', data, at, value)
    put32 = lambda at, value: struct.pack_into('>I', data, at, value)
    records = {bytes(data[12 + 16*i:16 + 16*i]): 12 + 16*i for i in range(u16(4))}
    gsub = u32(records[b'GSUB'] + 8)
    assert u32(gsub) == 0x10000, 'FeatureVariations would also need index remapping'
    feature_list = gsub + u16(gsub + 6)
    count = u16(feature_list)
    entries = [bytes(data[feature_list + 2 + 6*i:feature_list + 8 + 6*i]) for i in range(count)]
    assert set(renames) <= {entry[:4] for entry in entries}
    assert not set(renames.values()) & {entry[:4] for entry in entries}
    changed = [renames.get(entry[:4], entry[:4]) + entry[4:] for entry in entries]
    order = sorted(range(count), key=lambda i: changed[i][:4])
    remap = {old: new for new, old in enumerate(order)}
    for i, old in enumerate(order): data[feature_list + 2 + 6*i:feature_list + 8 + 6*i] = changed[old]
    # Feature records must remain sorted. Every LangSys references feature
    # indices, so move those references together with their feature records.
    script_list = gsub + u16(gsub + 4)
    visited = set()
    for i in range(u16(script_list)):
        script = script_list + u16(script_list + 6 + 6*i)
        offsets = ([u16(script)] if u16(script) else []) + [u16(script + 8 + 6*j) for j in range(u16(script + 2))]
        for offset in offsets:
            lang = script + offset
            if lang in visited: continue
            visited.add(lang)
            required = u16(lang + 2)
            if required != 0xffff: put16(lang + 2, remap[required])
            for j in range(u16(lang + 4)):
                at = lang + 6 + 2*j
                put16(at, remap[u16(at)])
    head = u32(records[b'head'] + 8)
    put32(head + 8, 0)
    def checksum(value):
        value = bytes(value) + b'\0' * (-len(value) % 4)
        return sum(struct.unpack('>' + 'I' * (len(value)//4), value)) & 0xffffffff
    for record in records.values():
        offset, length = u32(record + 8), u32(record + 12)
        put32(record + 4, checksum(data[offset:offset + length]))
    put32(head + 8, (0xB1B0AFBA - checksum(data)) & 0xffffffff)
    assert checksum(data) == 0xB1B0AFBA
    return bytes(data)

files = {}
for name, renames in [
    ('no-petite.otf', {b'pcap': b'ZP01', b'c2pc': b'ZP02'}),
    ('lower-petite-only.otf', {b'c2pc': b'ZP02'}),
    ('upper-petite-only.otf', {b'pcap': b'ZP01'}),
]:
    data = derive(renames)
    (root / name).write_bytes(data)
    files[name] = {'sha256': hashlib.sha256(data).hexdigest(),
        'renamed_features': {k.decode(): v.decode() for k, v in renames.items()}}
shutil.copy2(root.parent / 'FontNumeric/LICENSE.md', root / 'LICENSE.md')
(root / 'PROVENANCE.json').write_text(json.dumps({
    'source': '../FontNumeric/FontWithFancyFeatures.otf', 'source_sha256': expected,
    'repository': 'https://github.com/web-platform-tests/wpt',
    'commit': '850b36a56cfc15b9117a9d222d32a63104662f49',
    'license': 'BSD-3-Clause; retained in LICENSE.md',
    'derivation': 'Rename optional GSUB features; re-sort records and remap LangSys indices; recompute checksums. Outlines and metrics are unchanged.',
    'files': files}, indent=2) + '\n')
print('Derived', len(files), 'caps fixtures with valid sfnt checksums.')
