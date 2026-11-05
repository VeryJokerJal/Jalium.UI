#!/usr/bin/env python3
"""Raise Android's transitive dependency floor without changing its compiled payload.

The entry assembly and dual-ABI AAR do not implement XML/PKCS cryptography.
They retain their original build provenance; the common managed packages are
rebuilt against the servicing fix. This also verifies every non-nuspec byte.
"""
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
import zipfile

folder = Path(sys.argv[1])
records = []
for path in sorted(folder.glob('Jalium.UI.Android.26.11.0.*')):
    if path.suffix not in ['.nupkg', '.snupkg']:
        continue
    temp = path.with_name(path.name + '.normalized')
    entries = {}
    changed = []
    with zipfile.ZipFile(path) as source, zipfile.ZipFile(temp, 'w') as target:
        for info in source.infolist():
            data = source.read(info)
            if info.filename.endswith('.nuspec'):
                tree = ET.fromstring(data)
                if tree.tag.startswith('{'):
                    ET.register_namespace('', tree.tag[1:].split('}', 1)[0])
                for dependency in tree.findall('.//{*}dependency'):
                    if dependency.get('id') in ['System.Security.Cryptography.Xml', 'System.Security.Cryptography.Pkcs'] and dependency.get('version') != '10.0.13':
                        changed.append({'id': dependency.get('id'), 'from': dependency.get('version'), 'to': '10.0.13'})
                        dependency.set('version', '10.0.13')
                data = ET.tostring(tree, encoding='utf-8', xml_declaration=True)
            else:
                entries[info.filename] = hashlib.sha256(data).hexdigest()
            target.writestr(info, data)
    with zipfile.ZipFile(temp) as target:
        assert target.testzip() is None
        assert all(hashlib.sha256(target.read(name)).hexdigest() == digest for name, digest in entries.items())
    if changed:
        temp.replace(path)
    else:
        temp.unlink()
    records.append({'package': path.name, 'dependencyChanges': changed, 'unchangedPayloadEntries': len(entries), 'rewritten': bool(changed)})
assert records, 'Android package not found'
(folder / 'android-dependency-normalization.json').write_text(json.dumps(records, indent=2) + '\n')
print(json.dumps(records, indent=2))
