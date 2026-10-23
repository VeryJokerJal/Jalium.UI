#!/usr/bin/env python3
"""Retained AX references must not retarget when a virtualized row is recycled.

Opt-in, separate-process AX acceptance. Requires existing AX permission and an
unlocked macOS session; never prompts or synthesizes mouse/keyboard input.
"""
from pathlib import Path
import argparse
import hashlib
import json
import os
import selectors
import signal
import subprocess
import tempfile
import time

LIBRARIES = tuple('libjalium.native.' + name + '.dylib' for name in
                  ('core', 'text', 'metal', 'software', 'platform', 'media.core', 'media', 'browser'))
ASSEMBLIES = ('Jalium.UI.MacOS.HostSmoke.dll', 'Jalium.UI.Managed.dll', 'Jalium.UI.MacOS.dll', 'Jalium.UI.Xaml.dll')

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def stop_owned_app(app):
    prefix = str(app / 'Contents/MacOS') + '/'
    for line in subprocess.check_output(['ps', '-axo', 'pid=,command='], text=True).splitlines():
        pid, _, command = line.strip().partition(' ')
        if command.startswith(prefix):
            try: os.kill(int(pid), signal.SIGTERM)
            except ProcessLookupError: pass

def run_case(work, app, client_binary, index, libraries):
    output = work / ('case-' + str(index)); output.mkdir(mode=0o700)
    commands, checks, failures = [], [], []
    launcher = client = None; pid = None; initial = final = {}
    def state():
        try: return json.loads((output / 'state.json').read_text())
        except (FileNotFoundError, json.JSONDecodeError): return {}
    def require(ok, message):
        if not ok: raise RuntimeError(message)
        checks.append(message)
    def wait(predicate, message, seconds=4):
        end = time.monotonic() + seconds
        while time.monotonic() < end:
            value = predicate()
            if value: return value
            time.sleep(.02)
        raise RuntimeError(message + '; state=' + json.dumps(state()))
    def call(action, **kwargs):
        request = {'action': action, **kwargs}
        client.stdin.write(json.dumps(request) + '\n'); client.stdin.flush()
        poll = selectors.DefaultSelector(); poll.register(client.stdout, selectors.EVENT_READ)
        ready = poll.select(6); poll.close(); require(bool(ready), 'AX reply received')
        response = json.loads(client.stdout.readline()); commands.append({'request': request, 'response': response})
        require(response['trusted'] and response['clientPID'] != pid and response['targetPID'] == pid,
                'AX process identities and permission match')
        return response
    def press(identifier):
        response = call('press', id=identifier)
        require(response['actionError'] == 0, 'ordinary button accepted ' + identifier)
    def stable(selection):
        time.sleep(.15)
        require(state()['selected'] == selection, 'public selection remains ' + str(selection))
    def discover():
        return {n['id']: n for n in call('discover')['nodes'] if isinstance(n.get('id'), str)}
    started = time.monotonic()
    try:
        launcher = subprocess.Popen(['/usr/bin/open', '-n', '-W', '--env', 'JALIUM_SELECTION_OUTPUT=' + str(output),
            '--env', 'TMPDIR=' + str(work / 'tmp'), '--env', 'DYLD_PRINT_LIBRARIES=1',
            '--stdout', str(output / 'app.log'), '--stderr', str(output / 'app.err'), str(app),
            '--args', '--window-virtualized-selection-case=' + str(index)],
            stdout=(output / 'launch.log').open('w'), stderr=subprocess.STDOUT)
        initial = wait(lambda: state() if state().get('appRunning') else None, 'host ready'); pid = initial['processId']
        require(1 <= len(initial['rows']) <= 50 and initial['firstIndex'] == 0,
                'bounded realized viewport begins at item zero')
        client = subprocess.Popen([str(client_binary), str(pid)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  text=True, bufsize=1, stderr=(output / 'client.err').open('w'))
        nodes = discover(); old_row = nodes['virtual-row-0']['token']; old_button = nodes['virtual-action-0']['token']
        call('set_selected', token=old_row, selected=True); wait(lambda: state()['selected'] == [0], 'initial AX row selects item zero')
        press('virtual-middle'); wait(lambda: state()['phase'] == 1, 'middle scroll completes')
        press('virtual-bottom'); bottom = wait(lambda: state() if state()['phase'] == 2 else None, 'bottom scroll completes')
        require(bottom['firstIndex'] > 0 and all(x['index'] != 0 for x in bottom['rows'])
                and any(x['index'] == bottom['firstIndex'] and x['physical'] == initial['firstPhysical'] for x in bottom['rows']),
                'same physical row is bound to another item and item zero is unrealized')
        require(bottom['selected'] == [0], 'virtualization preserves the offscreen model selection')
        for token, original in ((old_row, 'virtual-row-0'), (old_button, 'virtual-action-0')):
            value = call('read', token=token)['node']
            require(value.get('id') in (None, original), 'retained AX reference never names replacement content')
        press('virtual-clear'); wait(lambda: state()['selected'] == [], 'clear selection')
        call('press', token=old_row); stable([])
        call('set_selected', token=old_row, selected=True); stable([])
        call('press', token=old_button); require(state()['invocations'] == [], 'obsolete descendant never invokes replacement')
        nodes = discover(); current = nodes['virtual-row-499']['token']
        require(current != old_row, 'current row has a fresh AX identity')
        call('set_selected', token=current, selected=True); wait(lambda: state()['selected'] == [499], 'current AX row selects last item')
        call('set_selected_items', id='virtual-list', attribute='AXSelectedChildren', tokens=[current, old_row]); stable([499])
        call('set_selected_items', id='virtual-list', attribute='AXSelectedChildren', tokens=[old_row]); stable([499])
        call('press', token=nodes['virtual-action-499']['token'])
        wait(lambda: state()['invocations'] == ['item-499'], 'current descendant invokes its actual item')
        call('set_selected_items', id='virtual-list', attribute='AXSelectedChildren', tokens=[])
        wait(lambda: state()['selected'] == [], 'current collection setter remains writable')
        press('virtual-top'); wait(lambda: state()['phase'] == 3, 'top scroll completes')
        nodes = discover(); require(nodes['virtual-row-0']['token'] != old_row, 'scrolling back never revives obsolete identity')
        call('press', token=old_row); stable([])
        call('press', token=nodes['virtual-row-0']['token']); wait(lambda: state()['selected'] == [0], 'restored current row is actionable')
        press('virtual-finish'); wait(lambda: state().get('closed'), 'host closes'); launcher.wait(timeout=6)
        require(launcher.returncode == 0 and not state()['timedOut'], 'host exits successfully')
    except Exception as error:
        failures.append(str(error))
    finally:
        if client is not None:
            try: call('quit'); client.wait(timeout=3)
            except Exception: client.terminate(); client.wait(timeout=3)
        if launcher is not None and launcher.poll() is None:
            (output / 'finish.signal').touch()
            try: launcher.wait(timeout=6)
            except subprocess.TimeoutExpired: stop_owned_app(app); launcher.wait(timeout=6)
        final = state(); loaded = []
        log = (output / 'app.err').read_text(errors='replace') if (output / 'app.err').exists() else ''
        for name, paths in libraries.items():
            if not any(str(path) in log for path in paths): failures.append('loaded payload not proven: ' + name)
            else: loaded.append(name)
        result = {'index': index, 'passed': not failures, 'checks': checks, 'failures': failures,
                  'initial': initial, 'final': final, 'commands': commands, 'hostPID': pid,
                  'clientPID': None if client is None else client.pid, 'loadedNative': loaded,
                  'externalAX': True, 'physicalInput': False, 'seconds': round(time.monotonic() - started, 3)}
        (output / 'receipt.json').write_text(json.dumps(result, ensure_ascii=False))
    return result

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app', type=Path, required=True)
    parser.add_argument('--work-parent', type=Path, default=Path.home() / 'Library/Caches')
    parser.add_argument('--results-file', type=Path)
    parser.add_argument('--cases', default='0,1,2,3,4,5')
    args = parser.parse_args(); source = args.app.resolve()
    if not source.is_dir() or source.suffix != '.app': parser.error('--app must name a built .app')
    try: cases = [int(x) for x in args.cases.split(',')]
    except ValueError: parser.error('--cases must contain integer indices')
    if not cases or len(set(cases)) != len(cases) or any(x not in range(6) for x in cases): parser.error('invalid cases')
    receipts = []
    with tempfile.TemporaryDirectory(prefix='jalium-virtual-selection-ax-', dir=args.work_parent) as name:
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
        client_source = Path(__file__).with_name('window-ax-action-client.mm'); client = work / 'client'
        subprocess.run(['xcrun', 'clang++', '-std=c++17', '-fobjc-arc', str(client_source), '-framework',
                        'Foundation', '-framework', 'ApplicationServices', '-o', str(client)], check=True)
        try:
            for index in cases:
                receipt = run_case(work, app, client, index, libraries); receipts.append(receipt)
                print(json.dumps({'case': index, 'passed': receipt['passed'], 'checks': len(receipt['checks']), 'failures': receipt['failures']}), flush=True)
        finally: stop_owned_app(app)
        result = {'cases': receipts, 'passed': sum(x['passed'] for x in receipts), 'total': len(cases),
                  'externalAX': True, 'physicalInput': False, 'nativeSHA256': hashes, 'managedSHA256': assemblies,
                  'clientSHA256': digest(client), 'clientSourceSHA256': digest(client_source), 'temporaryRoot': str(work)}
    result['temporaryRootRemoved'] = not Path(result['temporaryRoot']).exists()
    if args.results_file: args.results_file.write_text(json.dumps(result, ensure_ascii=False))
    print(json.dumps({k: result[k] for k in ('passed', 'total', 'externalAX', 'physicalInput', 'temporaryRootRemoved')}), flush=True)
    return 0 if result['passed'] == result['total'] else 1

if __name__ == '__main__': raise SystemExit(main())
