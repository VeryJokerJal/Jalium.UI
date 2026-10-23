#!/usr/bin/env python3
"""Verify the SDK Writing Tools delegate, including reentrant selection replies.

Requires an unlocked interactive macOS session and an available foreground slot.
Uses the SDK delegate in the test process; it does not inject OS/HID input or
invoke the Apple Intelligence service.
"""
import argparse
import contextlib
import json
import os
from pathlib import Path
import subprocess
import plistlib
import select
import shutil
import time


def prepare_bundle(app, output, backend, hold_seconds, startup_seconds):
    output.mkdir(parents=True, exist_ok=False)
    case = output / backend
    case.mkdir()
    (case / 'tmp').mkdir()
    bundle = case / ('WritingTools-' + backend + '.app')
    shutil.copytree(app, bundle, symlinks=True)
    plist = bundle / 'Contents/Info.plist'
    info = plistlib.loads(plist.read_bytes())
    info['CFBundleIdentifier'] += '.selection-' + backend
    info['CFBundleDisplayName'] = 'Jalium Writing Tools ' + backend.title()
    plist.write_bytes(plistlib.dumps(info))
    config = {'backend': backend, 'resultsDirectory': str(case),
              'temporaryDirectory': str(case / 'tmp'), 'holdSeconds': hold_seconds, 'startupSeconds': startup_seconds}
    resource = bundle / 'Contents/Resources/jalium-writing-tools-launch.json'
    resource.parent.mkdir(parents=True, exist_ok=True)
    resource.write_text(json.dumps(config))
    subprocess.run(['codesign', '--force', '--deep', '--sign', '-', str(bundle)], check=True, capture_output=True)
    subprocess.run(['codesign', '--verify', '--deep', '--strict', str(bundle)], check=True, capture_output=True)
    prepared = {'app': str(bundle), 'caseDirectory': str(case), 'backend': backend, 'holdSeconds': hold_seconds, 'startupSeconds': startup_seconds}
    (output / 'prepared.json').write_text(json.dumps(prepared))
    print(json.dumps({'prepared': True, 'launched': False, **prepared}), flush=True)
    return 0


def collect_bundle(app, output):
    prepared = json.loads((output / 'prepared.json').read_text())
    assert app == Path(prepared['app']).resolve()
    case = Path(prepared['caseDirectory'])
    launch = json.loads((case / 'launch.json').read_text())
    pid = launch['pid']
    command = subprocess.check_output(['ps', '-p', str(pid), '-o', 'command='], text=True).strip()
    assert command == str(app / 'Contents/MacOS/Jalium.UI.MacOS.HostSmoke'), command
    # Public Darwin NOTE_EXITSTATUS also permits a same-user non-child target.
    # sys/event.h defines this flag even where Python does not expose its name.
    note_exit_status = 0x04000000
    with contextlib.closing(select.kqueue()) as queue:
        event = select.kevent(pid, filter=select.KQ_FILTER_PROC,
                             flags=select.KQ_EV_ADD | select.KQ_EV_ENABLE,
                             fflags=select.KQ_NOTE_EXIT | note_exit_status)
        queue.control([event], 0, 0)
        (case / 'exit-watch-registered.json').write_text(json.dumps({'pid': pid, 'command': command}))
        deadline = time.monotonic() + 180 + prepared['holdSeconds'] + prepared['startupSeconds']
        events = []
        while not events and time.monotonic() < deadline:
            events = queue.control([], 1, min(30, max(0, deadline - time.monotonic())))
    assert events and events[0].fflags & note_exit_status, 'No authoritative exit status received'
    code = os.waitstatus_to_exitcode(events[0].data)
    result = json.loads((case / 'host-result.json').read_text())
    images = result['nativeImages']
    own = {Path(image).name.removeprefix('libjalium.native.') for image in images if str(app) in image}
    foreign = [image for image in images if str(app) not in image]
    backend = prepared['backend']
    messages = ['SDK selection enters the managed request and completes once',
                'exact SDK selection is acknowledged',
                'reentrant SDK selection still completes exactly once',
                'bridge refuses to acknowledge a range changed by SelectionChanged',
                "SDK selection preserves the application's replacement range"]
    counts = {message: sum(check.endswith(': ' + message) for check in result['checks']) for message in messages}
    allowed = {name + '.dylib' for name in ['core', 'text', 'platform', 'metal', 'software', 'media.core', 'media', 'browser']}
    expected = 12 if backend == 'both' else 6
    required = {'core.dylib', 'text.dylib', 'platform.dylib'} | ({'metal.dylib', 'software.dylib'} if backend == 'both' else {backend + '.dylib'})
    passed = bool(code == 0 and result['completed'] == result['expected'] == expected and not result['failures'] and
                  all(count == expected for count in counts.values()) and required <= own <= allowed and not foreign)
    receipt = {'passed': passed, 'backend': backend, 'exitCode': code, 'rawExitStatus': events[0].data,
               'exitStatusSource': 'Darwin EVFILT_PROC NOTE_EXITSTATUS', 'result': result,
               'selectionCheckCounts': counts, 'ownNativeLibraries': sorted(own), 'foreignNativeLibraries': foreign,
               'physicalInput': False, 'foregroundRequired': True, 'appleIntelligenceService': False}
    (output / 'receipt.json').write_text(json.dumps(receipt))
    print(json.dumps({'passed': passed, 'backend': backend, 'exitCode': code, 'completed': result['completed'],
                      'checks': len(result['checks']), 'failures': result['failures']}), flush=True)
    return 0 if passed else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app', type=Path, required=True)
    parser.add_argument('--results-directory', type=Path, required=True)
    parser.add_argument('--backend', choices=['all', 'metal', 'software', 'both'], default='all')
    parser.add_argument('--hold-seconds', type=int, default=0)
    parser.add_argument('--startup-seconds', type=int, default=12)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--prepare-bundle', action='store_true')
    mode.add_argument('--collect-bundle', action='store_true')
    args = parser.parse_args()
    app = args.app.resolve()
    if not app.is_dir() or app.suffix != '.app' or not 0 <= args.hold_seconds <= 120 or not 12 <= args.startup_seconds <= 600:
        parser.error('supply a built app and hold-seconds between 0 and 120')
    output = args.results_directory.resolve()
    if args.prepare_bundle:
        if args.backend == 'all':
            parser.error('prepare one backend per CUA launch')
        return prepare_bundle(app, output, args.backend, args.hold_seconds, args.startup_seconds)
    if args.collect_bundle:
        return collect_bundle(app, output)
    if args.backend == 'both':
        parser.error('both requires the prepared CUA bundle launch mode')
    output.mkdir(parents=True, exist_ok=False)
    backends = ['metal', 'software'] if args.backend == 'all' else [args.backend]
    required_checks = [
        'SDK selection enters the managed request and completes once',
        'exact SDK selection is acknowledged',
        'reentrant SDK selection still completes exactly once',
        'bridge refuses to acknowledge a range changed by SelectionChanged',
        "SDK selection preserves the application's replacement range",
    ]
    allowed = {name + '.dylib' for name in
               ['core', 'text', 'platform', 'metal', 'software', 'media.core', 'media', 'browser']}
    receipts = []
    for backend in backends:
        case = output / backend
        case.mkdir()
        (case / 'tmp').mkdir()
        env = os.environ.copy()
        env.update(JALIUM_WRITING_TOOLS_OUTPUT=str(case),
                   JALIUM_WRITING_TOOLS_HOLD_SECONDS=str(args.hold_seconds),
                   TMPDIR=str(case / 'tmp'), DYLD_PRINT_LIBRARIES='1')
        flag = '--window-writing-tools' + ('-software' if backend == 'software' else '')
        with (case / 'app.log').open('w') as log, (case / 'app.err').open('w') as error:
            process = subprocess.Popen([str(app / 'Contents/MacOS/Jalium.UI.MacOS.HostSmoke'), flag],
                                       env=env, stdout=log, stderr=error)
            (case / 'active-process.json').write_text(json.dumps({'pid': process.pid, 'app': str(app)}))
            timed_out = False
            try:
                code = process.wait(timeout=120 + args.hold_seconds)
            except subprocess.TimeoutExpired:
                timed_out = True
                process.terminate()
                try:
                    code = process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    code = process.wait()
        result_file = case / 'host-result.json'
        result = json.loads(result_file.read_text()) if result_file.exists() else None
        dyld = (case / 'app.err').read_text(errors='replace')
        lines = [line for line in dyld.splitlines() if '/libjalium.native.' in line]
        own = {line.split('/libjalium.native.')[-1].split()[0] for line in lines if str(app) in line}
        foreign = [line for line in lines if str(app) not in line]
        checks = result.get('checks', []) if result else []
        selection_counts = {message: sum(check.endswith(': ' + message) for check in checks)
                            for message in required_checks}
        passed = bool(code == 0 and not timed_out and result and
                      result['completed'] == result['expected'] == 6 and not result['failures'] and
                      all(count == 6 for count in selection_counts.values()) and
                      {'core.dylib', 'text.dylib', 'platform.dylib', backend + '.dylib'} <= own <= allowed and
                      not foreign)
        receipt = {'backend': backend, 'passed': passed, 'exitCode': code, 'timedOut': timed_out,
                   'result': result, 'selectionCheckCounts': selection_counts,
                   'ownNativeLibraries': sorted(own), 'foreignNativeLibraries': foreign}
        receipts.append(receipt)
        print(json.dumps({'backend': backend, 'passed': passed, 'exitCode': code,
                          'completed': result['completed'] if result else 0,
                          'checks': len(checks), 'failures': result['failures'] if result else ['Missing result']}), flush=True)
        if not passed:
            break
    final = {'passed': sum(case['passed'] for case in receipts), 'total': len(backends),
             'attempted': len(receipts), 'cases': receipts, 'physicalInput': False,
             'foregroundRequired': True, 'appleIntelligenceService': False}
    (output / 'receipt.json').write_text(json.dumps(final))
    return 0 if final['passed'] == final['total'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
