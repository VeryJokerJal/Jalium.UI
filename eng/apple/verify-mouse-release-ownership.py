#!/usr/bin/env python3
"""Exercise mouse ownership through a real NSView with process-local SDK snapshots."""
from pathlib import Path
import argparse
import json
import hashlib
import os
import subprocess
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app', type=Path, required=True)
    parser.add_argument('--bridge', type=Path, required=True)
    parser.add_argument('--results-directory', type=Path, required=True)
    parser.add_argument('--cases', default=','.join(map(str, range(12))))
    parser.add_argument('--hold-seconds', type=int, default=0)
    args = parser.parse_args()
    app = args.app.resolve(); bridge = args.bridge.resolve()
    if not app.is_dir() or app.suffix != '.app' or not bridge.is_file():
        parser.error('supply the built app and test-only SDK bridge')
    cases = [int(x) for x in args.cases.split(',')]
    if not cases or len(set(cases)) != len(cases) or any(x not in range(12) for x in cases):
        parser.error('cases must be unique values from 0 through 11')
    if not 0 <= args.hold_seconds <= 60:
        parser.error('hold-seconds must be between 0 and 60')
    args.results_directory.mkdir(parents=True, exist_ok=False)
    bridge_hash = hashlib.sha256(bridge.read_bytes()).hexdigest()
    receipts = []
    executable = app / 'Contents/MacOS/Jalium.UI.MacOS.HostSmoke'
    for index in cases:
        output = args.results_directory / ('case-' + str(index)); output.mkdir()
        temporary = output / 'tmp'; temporary.mkdir()
        env = os.environ.copy()
        env.update({'JALIUM_MOUSE_RELEASE_OUTPUT': str(output.resolve()), 'JALIUM_MOUSE_RELEASE_BRIDGE': str(bridge),
                    'JALIUM_MOUSE_RELEASE_HOLD_SECONDS': str(args.hold_seconds), 'TMPDIR': str(temporary.resolve()),
                    'DYLD_PRINT_LIBRARIES': '1'})
        started = time.monotonic()
        with (output / 'app.log').open('w') as log, (output / 'app.err').open('w') as err:
            process = subprocess.Popen([str(executable), '--window-mouse-release-case=' + str(index)], env=env, stdout=log, stderr=err)
            try:
                code = process.wait(timeout=args.hold_seconds + 40)
            except subprocess.TimeoutExpired:
                process.terminate()
                try: process.wait(timeout=5)
                except subprocess.TimeoutExpired: process.kill(); process.wait()
                code = -1
        result = json.loads((output / 'result.json').read_text()) if (output / 'result.json').exists() else None
        dyld = (output / 'app.err').read_text(errors='replace')
        native_lines = [x for x in dyld.splitlines() if '/libjalium.native.' in x]
        own = [x for x in native_lines if str(app) in x]
        foreign = [x for x in native_lines if str(app) not in x]
        passed = code == 0 and result and result['passed'] and result['completed'] == result['expected'] == 35
        loaded = set(x.split('/libjalium.native.')[-1].split()[0] for x in own)
        allowed = {name + '.dylib' for name in ('core', 'text', 'metal', 'software', 'platform', 'media.core', 'media', 'browser')}
        required = {'core.dylib', 'text.dylib', 'platform.dylib', 'software.dylib' if index >= 6 else 'metal.dylib'}
        passed = bool(passed and required <= loaded <= allowed and not foreign and str(bridge) in dyld)
        receipts.append({'index': index, 'passed': passed, 'exitCode': code, 'result': result,
                         'ownNativeLibraries': sorted(loaded),
                         'foreignNativeLibraryCount': len(foreign), 'helperLoaded': str(bridge) in dyld,
                         'seconds': round(time.monotonic() - started, 3)})
        print(json.dumps({'index': index, 'passed': passed, 'completed': result.get('completed') if result else None,
                          'failureCount': len(result.get('failures', [])) if result else None}), flush=True)
    receipt = {'cases': receipts, 'passed': sum(x['passed'] for x in receipts), 'total': len(cases),
               'bridgeSHA256': bridge_hash, 'nativeSDK': True, 'physicalInput': False, 'foregroundRequired': False}
    assert bridge_hash == hashlib.sha256(bridge.read_bytes()).hexdigest()
    (args.results_directory / 'receipt.json').write_text(json.dumps(receipt, ensure_ascii=False))
    return 0 if receipt['passed'] == receipt['total'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
