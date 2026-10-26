#!/usr/bin/env python3
"""Check standalone AppKit window appearance without changing system preferences or requiring foreground."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app', type=Path, required=True)
    parser.add_argument('--results-directory', type=Path, required=True)
    parser.add_argument('--cases', default=','.join(map(str, range(8))))
    parser.add_argument('--hold-seconds', type=int, default=0)
    args = parser.parse_args()
    try:
        cases = [int(value) for value in args.cases.split(',')]
    except ValueError:
        parser.error('cases must be integers')
    app = args.app.resolve()
    if not app.is_dir() or app.suffix != '.app' or not cases or len(set(cases)) != len(cases) or any(i not in range(8) for i in cases):
        parser.error('supply a built app and unique cases from 0 through 7')
    if not 0 <= args.hold_seconds <= 120:
        parser.error('hold-seconds must be between 0 and 120')
    output = args.results_directory.resolve(); output.mkdir(parents=True, exist_ok=False)
    copied = list(app.rglob('libjalium.native.*.dylib'))
    names = {name + '.dylib' for name in ('core', 'text', 'platform', 'metal', 'software', 'media.core', 'media', 'browser')}
    assert len(copied) == len(names) and {p.name.removeprefix('libjalium.native.') for p in copied} == names, \
        ['Expected one copy of each native library', *[str(p.relative_to(app)) for p in copied]]
    hashes = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in copied}
    receipts = []
    for index in cases:
        case = output / ('case-' + str(index)); case.mkdir(); (case / 'tmp').mkdir()
        env = os.environ.copy(); env.update({'JALIUM_STANDALONE_APPEARANCE_OUTPUT': str(case),
            'JALIUM_STANDALONE_APPEARANCE_HOLD_SECONDS': str(args.hold_seconds), 'TMPDIR': str(case / 'tmp'), 'DYLD_PRINT_LIBRARIES': '1'})
        with (case / 'app.log').open('w') as log, (case / 'app.err').open('w') as error:
            process = subprocess.Popen([str(app / 'Contents/MacOS/Jalium.UI.MacOS.HostSmoke'), '--window-standalone-appearance-case=' + str(index)],
                env=env, stdout=log, stderr=error)
            (case / 'active-process.json').write_text(json.dumps({'pid': process.pid, 'app': str(app)}))
            try:
                code = process.wait(timeout=60 + args.hold_seconds)
            except subprocess.TimeoutExpired:
                (case / 'timeout.json').write_text(json.dumps({'pid': process.pid, 'seconds': 60 + args.hold_seconds}))
                process.terminate()
                try: code = process.wait(timeout=5)
                except subprocess.TimeoutExpired: process.kill(); code = process.wait()
        result = json.loads((case / 'result.json').read_text()) if (case / 'result.json').exists() else None
        images = result.get('nativeImages', []) if result else []
        foreign = [p for p in images if str(app) not in p]
        own = {Path(p).name.removeprefix('libjalium.native.') for p in images if str(app) in p}
        backend = 'Software' if index % 4 >= 2 else 'Metal'
        required = {'core.dylib', 'text.dylib', 'platform.dylib', backend.lower() + '.dylib'}
        passed = bool(code == 0 and result and result['passed'] and len(result['Checks']) == 32 and not result['Failures']
            and result['applicationStillNull'] and result['renderBackend'] == backend and len(result['Observed']) == 3
            and len(list(case.glob('*.png'))) == 12 and required <= own <= names and not foreign)
        receipt = {'index': index, 'passed': passed, 'exitCode': code, 'result': result,
            'ownNativeLibraries': sorted(own), 'foreignNativeLibraries': foreign}
        receipts.append(receipt)
        print(json.dumps({'index': index, 'passed': passed, 'exitCode': code,
            'checks': len(result['Checks']) if result else 0, 'failures': result['Failures'] if result else ['Missing result.json']}), flush=True)
    assert hashes == {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in copied}
    receipt = {'passed': sum(c['passed'] for c in receipts), 'total': len(cases), 'cases': receipts,
        'copiedNativeSHA256': hashes, 'foregroundRequired': False, 'physicalInput': False, 'systemPreferencesWritten': False}
    (output / 'receipt.json').write_text(json.dumps(receipt, ensure_ascii=False))
    return 0 if receipt['passed'] == receipt['total'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
