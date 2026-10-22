#!/usr/bin/env python3
"""Validate reentrant popup ownership through own-process AppKit SDK calls."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import re
import shutil
import signal
import subprocess
import tempfile
import time

LIBRARIES = tuple('libjalium.native.' + name + '.dylib' for name in
                  ('core', 'text', 'metal', 'software', 'platform', 'media.core', 'media', 'browser'))
ASSEMBLIES = ('Jalium.UI.MacOS.HostSmoke.dll', 'Jalium.UI.Managed.dll', 'Jalium.UI.Core.dll',
              'Jalium.UI.Controls.dll', 'Jalium.UI.Interop.dll', 'Jalium.UI.MacOS.dll', 'Jalium.UI.Xaml.dll')
IMPLEMENTATIONS = ('Jalium.UI.Managed.dll', 'Jalium.UI.MacOS.dll', 'Jalium.UI.MacOS.HostSmoke.dll')
SCENES = ('remove-before', 'remove-after', 'add-before', 'add-after', 'binding-retarget', 'binding-reopen', 'native-reopen', 'ordinary')
IMAGES = tuple(name + '-' + phase + suffix for name in SCENES for phase in ('before', 'after') for suffix in ('', '-mask'))

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app', type=Path, required=True)
    parser.add_argument('--work-parent', type=Path, default=Path.home() / 'Library/Caches')
    parser.add_argument('--results-file', type=Path, required=True)
    parser.add_argument('--image-directory', type=Path, required=True)
    parser.add_argument('--ordinary-only', action='store_true', help='run only the stable-owner alignment scene against a product control')
    parser.add_argument('--cases', default=','.join(map(str, range(4))))
    args = parser.parse_args()
    source = args.app.resolve()
    if not source.is_dir() or source.suffix != '.app':
        parser.error('supply an existing HostSmoke app')
    try:
        cases = [int(value) for value in args.cases.split(',')]
    except ValueError:
        parser.error('--cases must contain integer indices')
    if not cases or len(set(cases)) != len(cases) or any(value not in range(4) for value in cases):
        parser.error('invalid case selection')
    if args.results_file.exists():
        parser.error('results file already exists')
    args.image_directory.mkdir(parents=True, exist_ok=False)
    scenes = ('ordinary',) if args.ordinary_only else SCENES
    expected_images = tuple(name + '-' + phase + suffix for name in scenes for phase in ('before', 'after') for suffix in ('', '-mask'))
    receipts = []
    environment = {**os.environ}
    for key in ['JALIUM_RENDER_THREAD', 'DYLD_INSERT_LIBRARIES', 'DYLD_LIBRARY_PATH']:
        environment.pop(key, None)
    with tempfile.TemporaryDirectory(prefix='jalium-popup-owners-', dir=args.work_parent) as directory:
        work = Path(directory)
        (work / 'tmp').mkdir()
        app = work / 'HostSmoke.app'
        subprocess.run(['/usr/bin/ditto', str(source), str(app)], check=True)
        native_paths, native_hashes, assemblies = {}, {}, {}
        for filename in LIBRARIES + ASSEMBLIES:
            originals = list(source.rglob(filename))
            copies = [app / path.relative_to(source) for path in originals]
            if not originals or any(digest(a) != digest(b) for a, b in zip(originals, copies)):
                raise RuntimeError('payload changed: ' + filename)
            if filename in LIBRARIES:
                native_paths[filename] = {str(path) for path in copies}
                native_hashes[filename] = [digest(path) for path in copies]
            else:
                if len(copies) != 1:
                    raise RuntimeError('ambiguous assembly: ' + filename)
                assemblies[filename] = digest(copies[0])
        try:
            for index in cases:
                output = work / ('case-' + str(index))
                output.mkdir()
                started = time.monotonic()
                command = ['/usr/bin/open', '-n', '-W', '--env', 'JALIUM_POPUP_OWNER_OUTPUT=' + str(output),
                           '--env', 'JALIUM_POPUP_OWNER_ORDINARY_ONLY=' + ('1' if args.ordinary_only else '0'),
                           '--env', 'TMPDIR=' + str(work / 'tmp'), '--env', 'DYLD_PRINT_LIBRARIES=1',
                           '--stdout', str(output / 'app.log'), '--stderr', str(output / 'app.err'),
                           str(app), '--args', '--window-popup-owner-case=' + str(index)]
                with (output / 'launcher.log').open('w') as log:
                    process = subprocess.Popen(command, env=environment, stdout=log, stderr=subprocess.STDOUT)
                    code = process.wait()
                result = json.loads((output / 'result.json').read_text()) if (output / 'result.json').exists() else None
                dyld = (output / 'app.err').read_text(errors='replace') if (output / 'app.err').exists() else ''
                loaded_paths = {match.group(1) for line in dyld.splitlines()
                                if (match := re.match(r'^dyld\[\d+\]: <[^>]+> (.+)$', line))}
                loaded = [name for name, paths in native_paths.items() if paths & loaded_paths]
                foreign_native = [path for path in loaded_paths if Path(path).name.startswith('libjalium.native.') and
                                  path not in native_paths.get(Path(path).name, set())]
                failures = []
                if not result or not result['passed'] or result['completed'] != result['expected'] or result['expected'] != len(scenes) or len(result['scenes']) != len(scenes) or (not args.ordinary_only and len(result['checks']) != 365):
                    failures.append('host scenes incomplete or failed')
                if result and (not result.get('requireForeground') or not result.get('foregroundQualified') or
                               result.get('renderThreadOverride') is not None or not result.get('focus') or
                               any(not item['ready'] or not item['active'] or item['frontmost'] != item['processId'] or
                                   item['responder'] != item['ownResponder'] for item in result['focus'])):
                    failures.append('native foreground/responder qualification incomplete')
                if result and (tuple(scene['name'] for scene in result.get('scenes', [])) != scenes or
                               len(result.get('focus', [])) != len(scenes) * 2 or
                               any(scene['text'] != 'Miii中文🙂e\u0301' or scene['selectedText'] != '中文🙂' or
                                   scene['selection'] != [4, 4] or scene['expectedParent'] != scene['actualParent'] or
                                   scene['sourceFont'] != scene['actualFont'] or
                                   scene['replacedHost'] != (scene['name'] in ('binding-reopen', 'native-reopen'))
                                   for scene in result.get('scenes', []))):
                    failures.append('owner/text/font/selection contract differs')
                if result and (len(result.get('pixelChecks', [])) != len(scenes) * 2 or any(not item['passed'] for item in result.get('pixelChecks', []))):
                    failures.append('independent glyph/selection pixel checks incomplete')
                expected_managed = {str(app / 'Contents/MonoBundle' / name) for name in IMPLEMENTATIONS}
                if result and set(result.get('managedLoadPaths', [])) != expected_managed:
                    failures.append('implementation assembly load paths differ')
                expected_preload = {str(app / 'Contents/MonoBundle' / name) for name in LIBRARIES}
                if result and set(result.get('nativePreloadRequests', [])) != expected_preload:
                    failures.append('complete native load preflight missing')
                if foreign_native:
                    failures.append('foreign native library loaded: ' + ', '.join(sorted(foreign_native)))
                if code:
                    failures.append('launcher exit ' + str(code))
                if len(loaded) != 8:
                    failures.append('matching native load paths not proven')
                images = {}
                for name in expected_images:
                    image = output / (name + '.png')
                    if image.exists():
                        images[name] = digest(image)
                        shutil.copy2(image, args.image_directory / ('case-' + str(index) + '-' + name + '.png'))
                    else:
                        failures.append('missing renderer readback: ' + name)
                receipt = {'index': index, 'passed': not failures, 'failures': failures, 'hostResult': result,
                           'loadedNative': loaded, 'launcherExitCode': code, 'seconds': round(time.monotonic() - started, 3),
                           'dyldSHA256': hashlib.sha256(dyld.encode()).hexdigest(), 'imageSHA256': images,
                           'diagnostics': {name: (output / name).read_text(errors='replace')[-12000:] for name in
                                           ['launcher.log', 'app.log', 'app.err'] if (output / name).exists()} }
                receipts.append(receipt)
                print(json.dumps({'case': index, 'passed': receipt['passed'], 'completed': result.get('completed') if result else None,
                                  'failures': result.get('failures') if result else failures}), flush=True)
        finally:
            prefix = str(app / 'Contents/MacOS') + '/'
            for line in subprocess.check_output(['ps', '-axo', 'pid=,command='], text=True).splitlines():
                pid, _, command = line.strip().partition(' ')
                if command.startswith(prefix):
                    try:
                        os.kill(int(pid), signal.SIGTERM)
                    except ProcessLookupError:
                        pass
        final = {'cases': receipts, 'passed': sum(item['passed'] for item in receipts), 'total': len(cases),
                 'nativeSHA256': native_hashes, 'managedSHA256': assemblies, 'physicalInput': False,
                 'desktopScreenshot': False, 'popupOwnerReentry': True, 'temporaryRoot': str(work)}
    final['temporaryRootRemoved'] = not Path(final['temporaryRoot']).exists()
    args.results_file.write_text(json.dumps(final, ensure_ascii=False))
    print(json.dumps({key: final[key] for key in ['passed', 'total', 'temporaryRootRemoved']}), flush=True)
    return 0 if final['passed'] == final['total'] else 1

if __name__ == '__main__':
    raise SystemExit(main())
