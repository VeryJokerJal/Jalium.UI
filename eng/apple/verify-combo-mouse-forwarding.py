#!/usr/bin/env python3
"""Collect interactive AppKit ComboBox relay evidence; input comes from CUA or the user."""
import argparse
import json
import os
from pathlib import Path
import subprocess

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app', type=Path, required=True)
    parser.add_argument('--case', type=int, choices=range(4), required=True)
    parser.add_argument('--results-directory', type=Path, required=True)
    args = parser.parse_args()
    app = args.app.resolve()
    if not app.is_dir() or app.suffix != '.app':
        parser.error('supply a built HostSmoke app')
    output = args.results_directory.resolve()
    output.mkdir(parents=True, exist_ok=False)
    environment = os.environ.copy()
    environment.update(JALIUM_COMBO_MOUSE_OUTPUT=str(output), DYLD_PRINT_LIBRARIES='1')
    with (output / 'app.log').open('w') as log, (output / 'app.err').open('w') as error:
        process = subprocess.Popen([str(app / 'Contents/MacOS/Jalium.UI.MacOS.HostSmoke'),
                                    '--window-combo-mouse-case=' + str(args.case)],
                                   env=environment, stdout=log, stderr=error)
        (output / 'active-process.json').write_text(json.dumps({'pid': process.pid, 'app': str(app)}))
        try:
            code = process.wait(timeout=615)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
            code = 124
    result_path = output / 'result.json'
    result = json.loads(result_path.read_text()) if result_path.exists() else None
    lines = [line for line in (output / 'app.err').read_text(errors='replace').splitlines()
             if '/libjalium.native.' in line]
    foreign = [line for line in lines if str(app) not in line]
    own = {line.split('/libjalium.native.')[-1].split()[0] for line in lines if str(app) in line}
    required = {'core.dylib', 'text.dylib', 'platform.dylib',
                'software.dylib' if args.case >= 2 else 'metal.dylib'}
    allowed = {name + '.dylib' for name in ['core', 'text', 'platform', 'metal', 'software',
                                         'media.core', 'media', 'browser']}
    passed = bool(code == 0 and result and result['passed'] and result['completed'] and
                  not result['timedOut'] and result['appActive'] and
                  result['frontmostPID'] == result['processId'] and result['nativeKey'] is not None and
                  result['nativeInputObserved'] and
                  not result['simulatedRoutedEvents'] and required <= own <= allowed and not foreign)
    receipt = {'case': args.case, 'passed': passed, 'exitCode': code, 'result': result,
               'ownNativeLibraries': sorted(own), 'foreignNativeLibraryCount': len(foreign),
               'physicalInput': False}
    (output / 'receipt.json').write_text(json.dumps(receipt, ensure_ascii=False))
    print(json.dumps({'case': args.case, 'passed': passed, 'exitCode': code,
                      'observations': len(result['observations']) if result else 0}), flush=True)
    return 0 if passed else 1

if __name__ == '__main__':
    raise SystemExit(main())
