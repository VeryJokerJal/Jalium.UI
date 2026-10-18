#!/usr/bin/env python3
"""Own-process AppKit wheel/tablet reentry, with no physical event injection."""
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
    parser.add_argument('--bridge', type=Path, required=True)
    parser.add_argument('--work-parent', type=Path, default=Path.home() / 'Library/Caches')
    parser.add_argument('--results-file', type=Path, required=True)
    parser.add_argument('--image-directory', type=Path, required=True)
    parser.add_argument('--cases', default=','.join(map(str, range(12))))
    args = parser.parse_args()
    source = args.app.resolve(); bridge_source = args.bridge.resolve()
    if not source.is_dir() or source.suffix != '.app' or not bridge_source.is_file():
        parser.error('supply an existing app and test-only SDK bridge')
    try: cases = [int(value) for value in args.cases.split(',')]
    except ValueError: parser.error('--cases must contain integer indices')
    if not cases or len(set(cases)) != len(cases) or any(value not in range(12) for value in cases):
        parser.error('invalid case selection')
    args.image_directory.mkdir(parents=True, exist_ok=False)
    receipts = []
    with tempfile.TemporaryDirectory(prefix='jalium-input-lifetime-', dir=args.work_parent) as directory:
        work = Path(directory); (work / 'tmp').mkdir(); app = work / 'HostSmoke.app'
        subprocess.run(['/usr/bin/ditto', str(source), str(app)], check=True)
        bridge = work / 'libJaliumWindowGestureBridge.dylib'; shutil.copy2(bridge_source, bridge)
        assert digest(bridge) == digest(bridge_source)
        subprocess.run(['/usr/bin/codesign', '--verify', str(bridge)], check=True, capture_output=True)
        native_paths, native_hashes, assemblies = {}, {}, {}
        for filename in LIBRARIES + ASSEMBLIES:
            originals = list(source.rglob(filename)); copies = [app / path.relative_to(source) for path in originals]
            if not originals or any(digest(a) != digest(b) for a, b in zip(originals, copies)):
                raise RuntimeError('payload changed: ' + filename)
            if filename in LIBRARIES:
                native_paths[filename] = copies; native_hashes[filename] = [digest(path) for path in copies]
            else:
                if len(copies) != 1: raise RuntimeError('ambiguous assembly: ' + filename)
                assemblies[filename] = digest(copies[0])
        try:
            for index in cases:
                output = work / ('case-' + str(index)); output.mkdir(); started = time.monotonic()
                command = ['/usr/bin/open', '-n', '-W', '--env', 'JALIUM_INPUT_LIFETIME_OUTPUT=' + str(output),
                           '--env', 'JALIUM_GESTURE_TEST_BRIDGE=' + str(bridge), '--env', 'TMPDIR=' + str(work / 'tmp'),
                           '--env', 'DYLD_PRINT_LIBRARIES=1', '--stdout', str(output / 'app.log'),
                           '--stderr', str(output / 'app.err'), str(app), '--args', '--window-input-lifetime-case=' + str(index)]
                with (output / 'launcher.log').open('w') as log:
                    process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT); code = process.wait()
                result = json.loads((output / 'result.json').read_text()) if (output / 'result.json').exists() else None
                dyld = (output / 'app.err').read_text(errors='replace') if (output / 'app.err').exists() else ''
                loaded = [filename for filename, paths in native_paths.items() if any(str(path) in dyld for path in paths)]
                failures = [] if result and result['passed'] and result['completed'] == result['expected'] == 5 else ['host scenarios incomplete or failed']
                if code: failures.append('launcher exit ' + str(code))
                if len(loaded) != 8 or str(bridge) not in dyld: failures.append('matching native/helper load paths not proven')
                image = output / 'input.png'
                if image.exists(): shutil.copy2(image, args.image_directory / ('case-' + str(index) + '.png'))
                receipt = {'index': index, 'passed': not failures, 'failures': failures, 'hostResult': result,
                           'loadedNative': loaded, 'helperLoaded': str(bridge) in dyld, 'launcherExitCode': code,
                           'seconds': round(time.monotonic() - started, 3), 'dyldSHA256': hashlib.sha256(dyld.encode()).hexdigest(),
                           'imageSHA256': digest(image) if image.exists() else None}
                receipts.append(receipt)
                print(json.dumps({'case': index, 'passed': receipt['passed'], 'completed': result.get('completed') if result else None,
                                  'failures': result.get('failures') if result else failures}), flush=True)
        finally:
            prefix = str(app / 'Contents/MacOS') + '/'
            for line in subprocess.check_output(['ps', '-axo', 'pid=,command='], text=True).splitlines():
                pid, _, command = line.strip().partition(' ')
                if command.startswith(prefix):
                    try: os.kill(int(pid), signal.SIGTERM)
                    except ProcessLookupError: pass
        final = {'cases': receipts, 'passed': sum(item['passed'] for item in receipts), 'total': len(cases),
                 'nativeSHA256': native_hashes, 'managedSHA256': assemblies, 'bridgeSHA256': digest(bridge),
                 'physicalInput': False, 'desktopScreenshot': False, 'mixedDPIDevice': False, 'temporaryRoot': str(work)}
    final['temporaryRootRemoved'] = not Path(final['temporaryRoot']).exists()
    args.results_file.write_text(json.dumps(final, ensure_ascii=False))
    print(json.dumps({key: final[key] for key in ['passed', 'total', 'temporaryRootRemoved']}), flush=True)
    return 0 if final['passed'] == final['total'] else 1

if __name__ == '__main__':
    raise SystemExit(main())
