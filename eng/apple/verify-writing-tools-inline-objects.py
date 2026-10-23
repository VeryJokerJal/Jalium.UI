#!/usr/bin/env python3
"""Verify semantic inline identity through own-process AppKit SDK replacements."""
from pathlib import Path
import argparse
import json
import os
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app', type=Path, required=True)
    parser.add_argument('--results-directory', type=Path, required=True)
    parser.add_argument('--cases', default=','.join(map(str, range(24))))
    parser.add_argument('--hold-seconds', type=int, default=0)
    args = parser.parse_args()
    app = args.app.resolve()
    try:
        cases = [int(value) for value in args.cases.split(',')]
    except ValueError:
        parser.error('cases must be integers')
    if not app.is_dir() or app.suffix != '.app' or not cases or len(cases) != len(set(cases)) or any(i not in range(24) for i in cases):
        parser.error('supply a built app and unique cases from 0 through 23')
    if not 0 <= args.hold_seconds <= 60:
        parser.error('hold-seconds must be between 0 and 60')
    output = args.results_directory.resolve(); output.mkdir(parents=True, exist_ok=False)
    receipts = []
    allowed = {name + '.dylib' for name in ('core', 'text', 'metal', 'software', 'platform', 'media.core', 'media', 'browser')}
    expected = {'unchanged', 'format-and-history', 'rewrite-and-history', 'semantic-behavior', 'subsequent-acceptance-or-atomic-rejection'}
    for index in cases:
        case = output / ('case-' + str(index)); case.mkdir(); (case / 'tmp').mkdir()
        env = os.environ.copy()
        env.update({'JALIUM_WRITING_ZERO_OUTPUT': str(case), 'JALIUM_WRITING_ZERO_HOLD_SECONDS': str(args.hold_seconds),
                    'TMPDIR': str(case / 'tmp'), 'DYLD_PRINT_LIBRARIES': '1'})
        with (case / 'app.log').open('w') as log, (case / 'app.err').open('w') as error:
            process = subprocess.Popen([str(app / 'Contents/MacOS/Jalium.UI.MacOS.HostSmoke'),
                                        '--window-writing-tools-zero-inline-case=' + str(index)], env=env, stdout=log, stderr=error)
            (case / 'active-process.json').write_text(json.dumps({'pid': process.pid, 'app': str(app)}))
            try:
                code = process.wait(timeout=90 + args.hold_seconds)
            except subprocess.TimeoutExpired:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill(); process.wait()
                code = 124
        result = json.loads((case / 'result.json').read_text()) if (case / 'result.json').exists() else None
        lines = [line for line in (case / 'app.err').read_text(errors='replace').splitlines() if '/libjalium.native.' in line]
        own = {line.split('/libjalium.native.')[-1].split()[0] for line in lines if str(app) in line}
        foreign = [line for line in lines if str(app) not in line]
        required = {'core.dylib', 'text.dylib', 'platform.dylib', 'software.dylib' if index % 4 >= 2 else 'metal.dylib'}
        captures = {(item['stage'], item['dpi']): item['sha256'] for item in result['captures']} if result else {}
        pixels = len(captures) == 12 and all(captures['before', dpi] == captures[stage, dpi] for dpi in (1, 2)
                    for stage in ('unchanged', 'format-undo', 'rewrite-undo')) and all(captures['before', dpi] != captures[stage, dpi]
                    for dpi in (1, 2) for stage in ('format', 'rewritten'))
        passed = bool(code == 0 and result and not result['failures'] and set(result['scenarios']) == expected
                      and len(result['scenarios']) == result['expectedScenarios'] == 5 and pixels
                      and required <= own <= allowed and not foreign)
        receipt = {'index': index, 'passed': passed, 'exitCode': code, 'result': result, 'pixelContracts': pixels,
                   'ownNativeLibraries': sorted(own), 'foreignNativeLibraryCount': len(foreign)}
        receipts.append(receipt)
        print(json.dumps({'index': index, 'passed': passed, 'scenariosPassed': len(result['scenarios']) if result else None,
                          'failureCount': len(result['failures']) if result else None}), flush=True)
    final = {'cases': receipts, 'passed': sum(x['passed'] for x in receipts), 'total': len(cases),
             'physicalInput': False, 'appleIntelligenceService': False, 'foregroundRequired': True}
    (output / 'receipt.json').write_text(json.dumps(final, ensure_ascii=False))
    return 0 if final['passed'] == final['total'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
