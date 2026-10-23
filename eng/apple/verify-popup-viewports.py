#!/usr/bin/env python3
"""Verify native popup viewport identity, geometry and realization without device input."""
import argparse
import json
import os
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app', type=Path, required=True)
    parser.add_argument('--results-directory', type=Path, required=True)
    parser.add_argument('--case', type=int, choices=range(8))
    parser.add_argument('--hold-seconds', type=float, default=0)
    args = parser.parse_args()
    if not 0 <= args.hold_seconds <= 120:
        parser.error('hold must be between zero and 120 seconds')
    app = args.app.resolve()
    if not app.is_dir() or app.suffix != '.app':
        parser.error('supply a built HostSmoke app')
    output = args.results_directory.resolve()
    output.mkdir(parents=True, exist_ok=False)
    receipts = []
    for index in range(8) if args.case is None else [args.case]:
        case = output / ('case-' + str(index))
        case.mkdir()
        env = os.environ.copy()
        env.update(JALIUM_POPUP_VIEWPORT_OUTPUT=str(case),
                   JALIUM_POPUP_VIEWPORT_HOLD_SECONDS=str(args.hold_seconds), DYLD_PRINT_LIBRARIES='1')
        with (case / 'app.log').open('w') as log, (case / 'app.err').open('w') as error:
            process = subprocess.Popen([str(app / 'Contents/MacOS/Jalium.UI.MacOS.HostSmoke'),
                                        '--window-popup-viewport-case=' + str(index)],
                                       env=env, stdout=log, stderr=error)
            (case / 'active-process.json').write_text(json.dumps({'pid': process.pid, 'app': str(app)}))
            try:
                code = process.wait(timeout=45 + args.hold_seconds)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
                code = 124
        result_path = case / 'result.json'
        result = json.loads(result_path.read_text()) if result_path.exists() else None
        lines = [line for line in (case / 'app.err').read_text(errors='replace').splitlines()
                 if '/libjalium.native.' in line]
        foreign = [line for line in lines if str(app) not in line]
        own = {line.split('/libjalium.native.')[-1].split()[0] for line in lines if str(app) in line}
        allowed = {name + '.dylib' for name in ['core', 'text', 'platform', 'metal', 'software',
                                              'media.core', 'media', 'browser']}
        required = {'core.dylib', 'text.dylib', 'platform.dylib',
                    'software.dylib' if index >= 4 else 'metal.dylib'}
        scenarios = result.get('Scenarios', []) if result else []
        passed = bool(code == 0 and result and result['passed'] and result['complete'] and result['cleaned']
                      and not result['Failures'] and len(scenarios) == 9 and len(result['Checks']) == 60
                      and {(item['stage'], item['depth']) for item in scenarios} ==
                          {(stage, depth) for stage in ['initial', 'owner-resized', 'popup-resized'] for depth in range(3)}
                      and all(item['nativeWindow'] > 0 for item in scenarios)
                      and required <= own <= allowed and not foreign)
        receipt = {'index': index, 'passed': passed, 'exitCode': code, 'result': result,
                   'ownNativeLibraries': sorted(own), 'foreignNativeLibraryCount': len(foreign),
                   'physicalInput': False, 'foregroundRequired': False}
        receipts.append(receipt)
        print(json.dumps({'index': index, 'passed': passed, 'exitCode': code,
                          'scenarios': len(scenarios), 'failures': result.get('Failures', []) if result else ['Missing result.json']},
                         ensure_ascii=False), flush=True)
    receipt = {'cases': receipts, 'passed': sum(item['passed'] for item in receipts), 'total': len(receipts),
               'physicalInput': False, 'foregroundRequired': False}
    (output / 'receipt.json').write_text(json.dumps(receipt, ensure_ascii=False))
    return 0 if receipt['passed'] == receipt['total'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
