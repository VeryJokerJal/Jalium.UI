#!/usr/bin/env python3
"""Verify our process-local AppKit Dock timer, drawing and required content polling."""
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
    app = args.app.resolve()
    try:
        cases = [int(value) for value in args.cases.split(',')]
    except ValueError:
        parser.error('cases must be integers')
    if not app.is_dir() or app.suffix != '.app' or not cases or len(set(cases)) != len(cases) or any(i not in range(8) for i in cases):
        parser.error('supply a built app and unique cases from 0 through 7')
    if not 0 <= args.hold_seconds <= 30:
        parser.error('hold-seconds must be between 0 and 30')
    output = args.results_directory.resolve()
    output.mkdir(parents=True, exist_ok=False)
    fixture = Path(__file__).resolve().parents[2] / 'tests/Jalium.UI.MacOS.HostSmoke'
    helper = output / 'libjalium.dock-motion-test.dylib'
    subprocess.run(['xcrun', 'clang++', '-std=c++20', '-fobjc-arc', '-dynamiclib', '-arch', 'arm64',
                    '-mmacosx-version-min=15.0', str(fixture / 'MotionPreferenceOverride.mm'),
                    str(fixture / 'DockMotionDisplayProbe.mm'), '-framework', 'AppKit', '-framework', 'Foundation',
                    '-o', str(helper)], check=True)
    subprocess.run(['/usr/bin/codesign', '--force', '--sign', '-', str(helper)], check=True, capture_output=True)
    helper_hash = hashlib.sha256(helper.read_bytes()).hexdigest()
    allowed = {name + '.dylib' for name in ('core', 'text', 'metal', 'software', 'platform', 'media.core', 'media', 'browser')}
    expected_scenarios = {'initial-policy', 'animated-progress', 'stop-for-reduced-motion',
                          'unchanged-reduced-notification', 'resume-animation', 'mutable-overlay-with-reduced-motion',
                          'remove-overlay', 'static-progress-states', 'hide-and-show', 'taskbar-inclusion',
                          'minimized-description-only', 'minimized-thumbnail-content',
                          'static-progress-during-thumbnail-polling', 'deminiaturized-static-progress',
                          'multiple-window-owners', 'close-and-reopen'}
    receipts = []
    for index in cases:
        case = output / ('case-' + str(index)); case.mkdir(); (case / 'tmp').mkdir()
        env = os.environ.copy()
        env.update(JALIUM_DOCK_MOTION_OUTPUT=str(case), JALIUM_MOTION_TEST_LIBRARY=str(helper),
                   JALIUM_DOCK_MOTION_HOLD_SECONDS=str(args.hold_seconds), TMPDIR=str(case / 'tmp'), DYLD_PRINT_LIBRARIES='1')
        with (case / 'app.log').open('w') as log, (case / 'app.err').open('w') as error:
            process = subprocess.Popen([str(app / 'Contents/MacOS/Jalium.UI.MacOS.HostSmoke'),
                                        '--window-dock-motion-case=' + str(index)], env=env, stdout=log, stderr=error)
            (case / 'active-process.json').write_text(json.dumps({'pid': process.pid, 'app': str(app)}))
            try:
                code = process.wait(timeout=45 + args.hold_seconds)
            except subprocess.TimeoutExpired:
                (case / 'timeout.json').write_text(json.dumps({'pid': process.pid, 'seconds': 45 + args.hold_seconds}))
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill(); process.wait()
                code = 124
        result = json.loads((case / 'result.json').read_text()) if (case / 'result.json').exists() else None
        dyld = (case / 'app.err').read_text(errors='replace')
        lines = [line for line in dyld.splitlines() if '/libjalium.native.' in line]
        own = {line.split('/libjalium.native.')[-1].split()[0] for line in lines if str(app) in line}
        foreign = [line for line in lines if str(app) not in line]
        required = {'core.dylib', 'text.dylib', 'platform.dylib', 'software.dylib' if index >= 4 else 'metal.dylib'}
        passed = bool(code == 0 and result and result['passed'] and len(result['Scenarios']) == result['expected'] == 16
                      and {item['name'] for item in result['Scenarios']} == expected_scenarios
                      and all(item['checks'] for item in result['Scenarios'])
                      and all(item['passed'] for item in result['Scenarios']) and result['overrideRestored']
                      and result['displayProbeRestored'] and required <= own <= allowed and not foreign and str(helper) in dyld)
        receipt = {'index': index, 'passed': passed, 'exitCode': code, 'result': result,
                   'ownNativeLibraries': sorted(own), 'foreignNativeLibraryCount': len(foreign), 'helperLoaded': str(helper) in dyld}
        receipts.append(receipt)
        print(json.dumps({'index': index, 'passed': passed,
                          'scenariosPassed': sum(item['passed'] for item in result['Scenarios']) if result else None,
                          'failureCount': len(result['Failures']) if result else None}), flush=True)
    assert hashlib.sha256(helper.read_bytes()).hexdigest() == helper_hash
    receipt = {'cases': receipts, 'passed': sum(item['passed'] for item in receipts), 'total': len(cases),
               'helperSHA256': helper_hash, 'physicalInput': False, 'systemPreferencesWritten': False, 'foregroundRequired': False}
    (output / 'receipt.json').write_text(json.dumps(receipt, ensure_ascii=False))
    return 0 if receipt['passed'] == receipt['total'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
