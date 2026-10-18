#!/usr/bin/env python3
"""Exercise Writing Tools mixed structures through real native/managed SDK callbacks.

This does not invoke Apple Intelligence or inject physical keyboard/mouse input.
"""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import signal
import subprocess
import tempfile
import time

LIBRARIES = tuple('libjalium.native.' + name + '.dylib' for name in
                  ('core', 'text', 'metal', 'software', 'platform', 'media.core', 'media', 'browser'))
ASSEMBLIES = ('Jalium.UI.MacOS.HostSmoke.dll', 'Jalium.UI.Managed.dll', 'Jalium.UI.MacOS.dll', 'Jalium.UI.Xaml.dll')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app', type=Path, required=True)
    parser.add_argument('--work-parent', type=Path, default=Path.home() / 'Library/Caches')
    parser.add_argument('--results-file', type=Path)
    parser.add_argument('--image-directory', type=Path)
    parser.add_argument('--cases', default=','.join(map(str, range(24))))
    args = parser.parse_args()
    source = args.app.resolve()
    if not source.is_dir() or source.suffix != '.app':
        parser.error('--app must name a built .app')
    try:
        cases = [int(value) for value in args.cases.split(',')]
    except ValueError:
        parser.error('--cases must contain integer indices')
    if not cases or len(cases) != len(set(cases)) or any(value not in range(24) for value in cases):
        parser.error('invalid cases')
    if args.image_directory:
        args.image_directory.mkdir(exist_ok=False, parents=True)
    receipts = []
    with tempfile.TemporaryDirectory(prefix='jalium-writing-tools-structure-', dir=args.work_parent) as name:
        work = Path(name)
        (work / 'tmp').mkdir()
        app = work / 'HostSmoke.app'
        subprocess.run(['/usr/bin/ditto', str(source), str(app)], check=True)
        libraries, hashes, assemblies = {}, {}, {}
        for filename in LIBRARIES + ASSEMBLIES:
            originals = list(source.rglob(filename))
            copies = [app / value.relative_to(source) for value in originals]
            if not originals or any(digest(a) != digest(b) for a, b in zip(originals, copies)):
                raise RuntimeError('payload changed: ' + filename)
            if filename in LIBRARIES:
                libraries[filename] = copies
                hashes[filename] = [digest(value) for value in copies]
            else:
                if len(originals) != 1:
                    raise RuntimeError('ambiguous assembly: ' + filename)
                assemblies[filename] = digest(copies[0])
        try:
            for index in cases:
                output = work / ('case-' + str(index))
                output.mkdir()
                started = time.monotonic()
                command = ['/usr/bin/open', '-n', '-W', '--env', 'JALIUM_WRITING_STRUCTURE_OUTPUT=' + str(output),
                           '--env', 'TMPDIR=' + str(work / 'tmp'), '--env', 'DYLD_PRINT_LIBRARIES=1',
                           '--stdout', str(output / 'app.log'), '--stderr', str(output / 'app.err'),
                           str(app), '--args', '--window-writing-tools-structure-case=' + str(index)]
                with (output / 'launcher.log').open('w') as log:
                    process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT)
                    code = process.wait()
                result = output / 'result.json'
                host = json.loads(result.read_text()) if result.exists() else None
                dyld = (output / 'app.err').read_text(errors='replace') if (output / 'app.err').exists() else ''
                loaded = [filename for filename, paths in libraries.items() if any(str(path) in dyld for path in paths)]
                required = ['libjalium.native.' + value + '.dylib' for value in
                            ('core', 'text', 'platform', 'software' if index % 4 >= 2 else 'metal')]
                failures = list(host['failures']) if host else ['host did not report a terminal result']
                if code != 0:
                    failures.append('launcher exit code: ' + str(code))
                failures += ['required payload not loaded: ' + filename for filename in required if filename not in loaded]
                unavailable = host.get('foregroundUnavailable', 0) if host else 0
                qualified = not failures and bool(host and host['qualifiedWithoutForeground'])
                pictures = []
                if args.image_directory:
                    for picture in sorted(output.glob('*.png')):
                        target = args.image_directory / ('case-' + str(index) + '-' + picture.name)
                        shutil.copy2(picture, target)
                        pictures.append({'path': str(target), 'sha256': digest(target)})
                receipt = {'index': index, 'passed': qualified and unavailable == 0,
                           'qualifiedWithoutForeground': qualified, 'foregroundUnavailable': unavailable,
                           'failures': failures, 'hostResult': host, 'loadedNative': loaded,
                           'launcherExitCode': code, 'physicalInput': False, 'desktopScreenshot': False,
                           'appleIntelligenceService': False, 'images': pictures,
                           'seconds': round(time.monotonic() - started, 3),
                           'dyldLogSHA256': hashlib.sha256(dyld.encode()).hexdigest()}
                receipts.append(receipt)
                print(json.dumps({'case': index, 'passed': receipt['passed'], 'qualifiedWithoutForeground': qualified,
                                  'scenarios': len(host['scenarios']) if host else 0, 'foregroundUnavailable': unavailable,
                                  'failures': [value.split('\n')[0] for value in failures]}, ensure_ascii=False), flush=True)
        finally:
            prefix = str(app / 'Contents/MacOS') + '/'
            for row in subprocess.check_output(['ps', '-axo', 'pid=,command='], text=True).splitlines():
                pid, _, command = row.strip().partition(' ')
                if command.startswith(prefix):
                    try:
                        os.kill(int(pid), signal.SIGTERM)
                    except ProcessLookupError:
                        pass
    result = {'passed': sum(value['passed'] for value in receipts),
              'qualifiedWithoutForeground': sum(value['qualifiedWithoutForeground'] for value in receipts),
              'foregroundUnavailable': sum(value['foregroundUnavailable'] for value in receipts),
              'total': len(cases), 'cases': receipts, 'nativeSHA256': hashes, 'managedSHA256': assemblies,
              'physicalInput': False, 'desktopScreenshot': False, 'appleIntelligenceService': False,
              'temporaryRootRemoved': not work.exists()}
    if args.results_file:
        args.results_file.write_text(json.dumps(result, ensure_ascii=False))
    print(json.dumps({key: result[key] for key in ['passed', 'qualifiedWithoutForeground', 'foregroundUnavailable', 'total', 'temporaryRootRemoved']}), flush=True)
    return 0 if result['passed'] == len(cases) else 3 if result['qualifiedWithoutForeground'] == len(cases) else 1


if __name__ == '__main__':
    raise SystemExit(main())
