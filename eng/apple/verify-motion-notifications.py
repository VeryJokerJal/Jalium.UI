#!/usr/bin/env python3
"""Verify process-local AppKit motion notifications; no system preferences or input change."""
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
    parser.add_argument('--cases', default=','.join(map(str, range(48))))
    parser.add_argument('--hold-seconds', type=int, default=0)
    args = parser.parse_args()
    app = args.app.resolve()
    try:
        cases = [int(value) for value in args.cases.split(',')]
    except ValueError:
        parser.error('cases must be integers')
    if not app.is_dir() or app.suffix != '.app' or not cases or len(cases) != len(set(cases)) or any(i not in range(48) for i in cases):
        parser.error('supply a built app and unique cases from 0 through 47')
    if not 0 <= args.hold_seconds <= 60:
        parser.error('hold-seconds must be between 0 and 60')
    output = args.results_directory.resolve(); output.mkdir(parents=True, exist_ok=False)
    helper = output / 'libjalium.window-motion-test.dylib'
    source = Path(__file__).resolve().parents[2] / 'tests/Jalium.UI.MacOS.HostSmoke/MotionPreferenceOverride.mm'
    subprocess.run(['xcrun', 'clang++', '-std=c++20', '-fobjc-arc', '-dynamiclib', '-arch', 'arm64',
                    '-mmacosx-version-min=15.0', str(source), '-framework', 'AppKit', '-framework', 'Foundation', '-o', str(helper)], check=True)
    subprocess.run(['/usr/bin/codesign', '--force', '--sign', '-', str(helper)], check=True, capture_output=True)
    helper_hash = hashlib.sha256(helper.read_bytes()).hexdigest()
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    receipts = []
    allowed = {name + '.dylib' for name in ('core', 'text', 'metal', 'software', 'platform', 'media.core', 'media', 'browser')}
    for index in cases:
        case = output / ('case-' + str(index)); case.mkdir(); (case / 'tmp').mkdir()
        env = os.environ.copy()
        env.update({'JALIUM_MOTION_NOTIFICATION_OUTPUT': str(case), 'JALIUM_MOTION_TEST_LIBRARY': str(helper),
                    'JALIUM_MOTION_NOTIFICATION_HOLD_SECONDS': str(args.hold_seconds), 'TMPDIR': str(case / 'tmp'), 'DYLD_PRINT_LIBRARIES': '1'})
        with (case / 'app.log').open('w') as log, (case / 'app.err').open('w') as error:
            process = subprocess.Popen([str(app / 'Contents/MacOS/Jalium.UI.MacOS.HostSmoke'),
                                        '--window-motion-notification-case=' + str(index)], env=env, stdout=log, stderr=error)
            (case / 'active-process.json').write_text(json.dumps({'pid': process.pid, 'app': str(app)}))
            code = process.wait()
        result = json.loads((case / 'result.json').read_text()) if (case / 'result.json').exists() else None
        dyld = (case / 'app.err').read_text(errors='replace')
        lines = [line for line in dyld.splitlines() if '/libjalium.native.' in line]
        own = {line.split('/libjalium.native.')[-1].split()[0] for line in lines if str(app) in line}
        foreign = [line for line in lines if str(app) not in line]
        required = {'core.dylib', 'text.dylib', 'platform.dylib', 'software.dylib' if index % 12 >= 6 else 'metal.dylib'}
        passed = bool(code == 0 and result and result['passed'] and len(result['Scenarios']) == result['expected'] == 15
                      and result['overrideRestored'] and required <= own <= allowed and not foreign and str(helper) in dyld
                      and result['nativeDeliveryAuditPassed'] and result['CleanupCount'] == 1 and not result['FrameFailures']
                      and len(result['WorkspacePosts']) >= 10 and len(result['DisplayPosts']) == 2
                      and all(post['valid'] for post in result['WorkspacePosts'] + result['DisplayPosts'])
                      and all(frame['valid'] and frame['onWindowThread'] for frame in result['NativeDeliveries'])
                      and sum(scene['name'] == 'background-preference-delivery' and scene['passed'] for scene in result['Scenarios']) == 1
                      and sum(scene['name'] == 'interleaved-display-notifications' and scene['passed'] for scene in result['Scenarios']) == 1)
        receipt = {'index': index, 'passed': passed, 'exitCode': code, 'result': result,
                   'ownNativeLibraries': sorted(own), 'foreignNativeLibraryCount': len(foreign), 'helperLoaded': str(helper) in dyld}
        receipts.append(receipt)
        if not passed:
            print(json.dumps({'index': index, 'exitCode': code,
                              'failedScenarios': [scene for scene in result.get('Scenarios', []) if not scene['passed']] if result else [],
                              'failures': result.get('Failures', []) if result else ['Missing result.json'],
                              'observed': result.get('Observed', []) if result else [],
                              'foreignNativeLibraries': foreign, 'helperLoaded': str(helper) in dyld}, ensure_ascii=False), flush=True)
        print(json.dumps({'index': index, 'passed': passed, 'scenariosPassed': sum(x['passed'] for x in result['Scenarios']) if result else None,
                          'failureCount': len(result['Failures']) if result else None}), flush=True)
    assert helper_hash == hashlib.sha256(helper.read_bytes()).hexdigest()
    assert source_hash == hashlib.sha256(source.read_bytes()).hexdigest()
    final = {'cases': receipts, 'passed': sum(x['passed'] for x in receipts), 'total': len(cases), 'helperSHA256': helper_hash, 'helperSourceSHA256': source_hash,
             'physicalInput': False, 'systemPreferencesWritten': False, 'foregroundRequired': False}
    (output / 'receipt.json').write_text(json.dumps(final, ensure_ascii=False))
    return 0 if final['passed'] == final['total'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
