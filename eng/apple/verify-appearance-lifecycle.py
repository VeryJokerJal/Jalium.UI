#!/usr/bin/env python3
"""Exercise appearance monitoring and owner cleanup in real AppKit processes.

Uses the existing user session and programmatic SDK actions. Never requests
accessibility permission or synthesizes physical keyboard/mouse input.
"""
from pathlib import Path
import argparse
import hashlib
import json
import os
import signal
import shutil
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
    parser.add_argument('--cases', default=','.join(map(str, range(8))))
    args = parser.parse_args()
    source = args.app.resolve()
    if not source.is_dir() or source.suffix != '.app': parser.error('--app must name a built .app')
    try: cases = [int(x) for x in args.cases.split(',')]
    except ValueError: parser.error('--cases must contain integer indices')
    if not cases or len(cases) != len(set(cases)) or any(x not in range(8) for x in cases): parser.error('invalid cases')
    if args.image_directory:
        args.image_directory.mkdir(exist_ok=False, parents=True)
    receipts = []
    with tempfile.TemporaryDirectory(prefix='jalium-appearance-lifecycle-', dir=args.work_parent) as name:
        work = Path(name); (work / 'tmp').mkdir(); app = work / 'HostSmoke.app'
        subprocess.run(['/usr/bin/ditto', str(source), str(app)], check=True)
        libraries, hashes, assemblies = {}, {}, {}
        for filename in LIBRARIES + ASSEMBLIES:
            originals = list(source.rglob(filename)); copies = [app / p.relative_to(source) for p in originals]
            if not originals or any(digest(a) != digest(b) for a, b in zip(originals, copies)): raise RuntimeError('payload changed: ' + filename)
            if filename in LIBRARIES:
                libraries[filename] = copies; hashes[filename] = [digest(p) for p in copies]
            else:
                if len(originals) != 1: raise RuntimeError('ambiguous assembly: ' + filename)
                assemblies[filename] = digest(copies[0])
        try:
            for index in cases:
                output = work / ('case-' + str(index)); output.mkdir(); started = time.monotonic()
                command = ['/usr/bin/open', '-n', '-W', '--env', 'JALIUM_APPEARANCE_OUTPUT=' + str(output),
                           '--env', 'TMPDIR=' + str(work / 'tmp'), '--env', 'DYLD_PRINT_LIBRARIES=1',
                           '--stdout', str(output / 'app.log'), '--stderr', str(output / 'app.err'),
                           str(app), '--args', '--window-appearance-case=' + str(index)]
                with (output / 'launcher.log').open('w') as log:
                    process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT)
                    code = process.wait()
                host = json.loads((output / 'result.json').read_text()) if (output / 'result.json').exists() else None
                dyld = (output / 'app.err').read_text(errors='replace') if (output / 'app.err').exists() else ''
                loaded = [filename for filename, paths in libraries.items() if any(str(path) in dyld for path in paths)]
                required = ['libjalium.native.' + n + '.dylib' for n in ('core', 'text', 'platform', 'metal' if index % 4 < 2 else 'software')]
                failures = [] if host and host['passed'] else [host.get('failure') if host else 'host did not report a result']
                if code != 0: failures.append('launcher exit code: ' + str(code))
                failures += ['required payload not loaded: ' + filename for filename in required if filename not in loaded]
                image = output / 'host-light.png'
                receipt = {'index': index, 'passed': not failures, 'failures': failures, 'hostResult': host,
                           'loadedNative': loaded, 'launcherExitCode': code, 'physicalInput': False,
                           'desktopScreenshot': False, 'seconds': round(time.monotonic() - started, 3),
                           'dyldLogSHA256': hashlib.sha256(dyld.encode()).hexdigest(),
                           'imageSHA256': digest(image) if image.exists() else None}
                if image.exists() and args.image_directory:

                    for picture in output.glob('*.png'):
                        shutil.copy2(picture, args.image_directory / ('case-' + str(index) + '-' + picture.name))
                receipts.append(receipt)
                print(json.dumps({'case': index, 'passed': receipt['passed'], 'checks': len(host['checks']) if host else 0,
                                  'failures': failures}, ensure_ascii=False), flush=True)
        finally:
            prefix = str(app / 'Contents/MacOS') + '/'
            for row in subprocess.check_output(['ps', '-axo', 'pid=,command='], text=True).splitlines():
                pid, _, command = row.strip().partition(' ')
                if command.startswith(prefix):
                    try: os.kill(int(pid), signal.SIGTERM)
                    except ProcessLookupError: pass
    result = {'passed': sum(x['passed'] for x in receipts), 'total': len(cases), 'cases': receipts,
              'nativeSHA256': hashes, 'managedSHA256': assemblies, 'physicalInput': False,
              'desktopScreenshot': False, 'temporaryRootRemoved': not work.exists()}
    if args.results_file: args.results_file.write_text(json.dumps(result, ensure_ascii=False))
    print(json.dumps({k: result[k] for k in ['passed', 'total', 'physicalInput', 'desktopScreenshot', 'temporaryRootRemoved']}), flush=True)
    return 0 if result['passed'] == result['total'] else 1

if __name__ == '__main__':
    raise SystemExit(main())
