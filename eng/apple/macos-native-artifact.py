#!/usr/bin/env python3
"""Transfer one complete macOS RID between jobs without mixing build revisions."""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tarfile
import tempfile

RIDS = ('osx-arm64', 'osx-x64')
MAX_NATIVE_FILE_SIZE = 2 * 1024 ** 3
MAX_NATIVE_FILES = 64
REQUIRED = {'.jalium-native-complete', 'jalium_core.metallib', 'jalium_vello.metallib', 'jalium_metal_manifest.json'}
REQUIRED_DYLIBS = {'libjalium.native.' + module + '.dylib' for module in
                   ('core', 'text', 'metal', 'software', 'platform', 'media.core', 'media', 'browser')}


def filename(name):
    return (name in REQUIRED or re.fullmatch(r'libjalium[.]native[.][a-z0-9_.-]+[.]dylib', name) is not None)


def validate(payload, source, rid, configuration):
    missing = sorted(name for name in REQUIRED | REQUIRED_DYLIBS if not (payload / name).is_file())
    if missing:
        raise ValueError('Incomplete native resources: ' + ', '.join(missing))
    metal = json.loads((payload / 'jalium_metal_manifest.json').read_text())
    if metal.get('formatVersion') != 1 or metal.get('rid') != rid:
        raise ValueError('Metal manifest does not match the native RID')
    for name in ('jalium_core.metallib', 'jalium_vello.metallib'):
        data = (payload / name).read_bytes()
        entry = metal.get('libraries', {}).get(name, {})
        if entry.get('size') != len(data) or entry.get('sha256') != hashlib.sha256(data).hexdigest():
            raise ValueError('Metal library differs from its manifest: ' + name)
    subprocess.run([sys.executable, str(source / 'eng/apple/check-native-exports.py'), str(payload),
                    '--repo-root', str(source), '--configuration', configuration,
                    '--runtime-identifier', rid], check=True)


def create(payload, archive, source, rid, configuration, revision):
    validate(payload, source, rid, configuration)
    paths = [path for path in payload.iterdir() if filename(path.name)]
    if not paths or not any(path.suffix == '.dylib' for path in paths):
        raise ValueError('Native dylibs are missing')
    files = []
    for path in sorted(paths):
        if not path.resolve().is_relative_to(payload) or not path.is_file():
            raise ValueError('Native asset resolves outside its payload: ' + str(path))
        if path.stat().st_size > MAX_NATIVE_FILE_SIZE:
            raise ValueError('Native asset exceeds the size limit: ' + path.name)
        files.append({'name': path.name, 'size': path.stat().st_size,
                      'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'mode': path.stat().st_mode & 0o777})
    if len(files) > MAX_NATIVE_FILES:
        raise ValueError('Too many native assets')
    receipt = {'schema': 1, 'rid': rid, 'configuration': configuration, 'sourceRevision': revision, 'files': files}
    archive.parent.mkdir(parents=True, exist_ok=True)
    # The caller owns the destination, but never replace an earlier receipt.
    with archive.open('xb') as stream, tarfile.open(fileobj=stream, mode='w') as tar:
        data = json.dumps(receipt, sort_keys=True).encode()
        member = tarfile.TarInfo('manifest.json'); member.size = len(data); member.mode = 0o644
        tar.addfile(member, io.BytesIO(data))
        for entry in files:
            data = (payload / entry['name']).read_bytes()
            if len(data) != entry['size'] or hashlib.sha256(data).hexdigest() != entry['sha256']:
                raise ValueError('Native source changed while archiving: ' + entry['name'])
            member = tarfile.TarInfo('payload/' + entry['name']); member.size = len(data); member.mode = entry['mode']
            tar.addfile(member, io.BytesIO(data))
    return receipt


def unpack(archive, output, source, rid, configuration, revision):
    target = output / rid / configuration
    if target.exists():
        raise ValueError('Native target already exists: ' + str(target))
    with tarfile.open(archive, 'r:') as tar:
        members = tar.getmembers()
        names = [member.name for member in members]
        if len(names) != len(set(names)) or any(not member.isfile() for member in members):
            raise ValueError('Duplicate or non-regular archive member')
        manifests = [member for member in members if member.name == 'manifest.json']
        if len(manifests) != 1 or manifests[0].size > 65536:
            raise ValueError('Missing or excessive native manifest')
        with tar.extractfile(manifests[0]) as stream:
            receipt = json.load(stream)
        if (receipt.get('schema') != 1 or receipt.get('rid') != rid or
                receipt.get('configuration') != configuration or receipt.get('sourceRevision') != revision):
            raise ValueError('Native manifest does not match the requested RID, configuration and source revision')
        files = receipt.get('files')
        if not isinstance(files, list) or not 1 <= len(files) <= MAX_NATIVE_FILES:
            raise ValueError('Native manifest file list is missing')
        declared = [entry.get('name') for entry in files if isinstance(entry, dict)]
        if (len(declared) != len(files) or any(not isinstance(name, str) or not filename(name) for name in declared)
                or len(declared) != len(set(declared))):
            raise ValueError('Invalid native asset names')
        if not REQUIRED <= set(declared) or set(names) != {'manifest.json', *('payload/' + name for name in declared)}:
            raise ValueError('Native archive contents do not match its complete manifest')
        output.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='.native-', dir=output) as temporary:
            staging = Path(temporary)
            by_name = {member.name: member for member in members}
            for entry in files:
                member = by_name['payload/' + entry['name']]
                size, mode = entry.get('size'), entry.get('mode')
                if (not isinstance(size, int) or not 0 <= size <= MAX_NATIVE_FILE_SIZE or member.size != size or
                        not isinstance(mode, int) or mode < 0 or mode > 0o777):
                    raise ValueError('Invalid native file size or mode')
                with tar.extractfile(member) as stream:
                    data = stream.read()
                if hashlib.sha256(data).hexdigest() != entry.get('sha256'):
                    raise ValueError('Native asset hash mismatch: ' + entry['name'])
                path = staging / entry['name']; path.write_bytes(data); path.chmod(mode)
            validate(staging, source, rid, configuration)
            target.parent.mkdir(parents=True, exist_ok=True)
            os.replace(staging, target)
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=('validate', 'create', 'extract'))
    parser.add_argument('--source', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--runtime-identifier', choices=RIDS, required=True)
    parser.add_argument('--configuration', choices=('Debug', 'Release'), default='Release')
    parser.add_argument('--source-revision')
    parser.add_argument('--archive', type=Path)
    parser.add_argument('--payload', type=Path)
    parser.add_argument('--output-root', type=Path)
    args = parser.parse_args()
    if args.operation == 'validate':
        if not args.payload:
            parser.error('validate requires --payload')
        validate(args.payload.resolve(), args.source.resolve(), args.runtime_identifier, args.configuration)
        print(json.dumps({'operation': 'validate', 'rid': args.runtime_identifier, 'passed': True}))
        return
    if not args.archive:
        parser.error('create and extract require --archive')
    if not re.fullmatch(r'[0-9a-f]{40}', args.source_revision or ''):
        parser.error('Supply the exact source commit SHA')
    if args.operation == 'create':
        if not args.payload:
            parser.error('create requires --payload')
        receipt = create(args.payload.resolve(), args.archive.resolve(), args.source.resolve(),
                         args.runtime_identifier, args.configuration, args.source_revision)
    else:
        if not args.output_root:
            parser.error('extract requires --output-root')
        receipt = unpack(args.archive.resolve(), args.output_root.resolve(), args.source.resolve(),
                         args.runtime_identifier, args.configuration, args.source_revision)
    print(json.dumps({'operation': args.operation, 'rid': receipt['rid'], 'files': len(receipt['files']),
                      'sourceRevision': receipt['sourceRevision'], 'passed': True}))


if __name__ == '__main__':
    main()
