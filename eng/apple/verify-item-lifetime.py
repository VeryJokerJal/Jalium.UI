#!/usr/bin/env python3
"""Dynamic item leases, source rebinding and model release in actual windows.

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
        request = {'action': action, **kwargs}; client.stdin.write(json.dumps(request) + '\n'); client.stdin.flush()
        poll = selectors.DefaultSelector(); poll.register(client.stdout, selectors.EVENT_READ)
        ready = poll.select(6); poll.close(); require(bool(ready), 'AX reply received')
        response = json.loads(client.stdout.readline()); commands.append({'request': request, 'response': response})
        require(response['trusted'] and response['clientPID'] != pid and response['targetPID'] == pid,
                'trusted independent AX client identities match')
        return response
    def discover(): return {n['id']: n for n in call('discover')['nodes'] if isinstance(n.get('id'), str)}
    def nodes_ready(*ids):
        def current():
            nodes = discover(); return nodes if all(id in nodes for id in ids) else None
        return wait(current, 'current lifetime controls are exposed')
    def press(id): require(call('press', id=id)['actionError'] == 0, 'ordinary control accepts ' + id)
    def live_nodes():
        press('lifetime-open')
        ids = ['lifetime-root','lifetime-row-1']
        if initial['kind'] != 2: ids.append('lifetime-command-1')
        return nodes_ready(*ids)
    def check_retained(total):
        press('lifetime-check')
        current = wait(lambda: state() if state().get('metadataRetired') else None, 'old logical lease reports disabled, offscreen and empty bounds')
        require(current['selected'] == (-1 if initial['kind'] == 0 else 0), 'retained provider preserves the survivor selection')
        expected = 0 if initial['kind'] == 0 else total
        require(len(current['rejected']) == expected and all(x == 'retired' for x in current['rejected']),
                'old logical selection methods reject a reinserted object')
    def current_action(nodes, expected_clicks):
        if initial['kind'] == 2:
            require(call('press', token=nodes['lifetime-row-1']['token'])['actionError'] == 0, 'new row selects normally')
            wait(lambda: state().get('selected') == 1, 'current ComboBox item changes selection')
        else:
            require(call('press', token=nodes['lifetime-command-1']['token'])['actionError'] == 0, 'current row command invokes')
            wait(lambda: state().get('invocations') == expected_clicks, 'current row command reaches the right data')
    def stale_tokens(nodes):
        keys = ['lifetime-row-1'] + ([] if initial['kind'] == 2 else ['lifetime-command-1'])
        return [nodes[id]['token'] for id in keys]
    def reject_native(tokens):
        for token in tokens:
            wait(lambda: call('read', token=token)['node'].get('id') is None, 'obsolete native reference is retired')
        before = state()
        for token in tokens: call('press', token=token)
        time.sleep(.2)
        require(state()['selected'] == before['selected'] and state()['invocations'] == before['invocations'],
                'old native references cannot select or invoke current data')
    started = time.monotonic()
    try:
        launcher = subprocess.Popen(['/usr/bin/open', '-n', '-W', '--env', 'JALIUM_SELECTION_OUTPUT=' + str(output),
            '--env', 'TMPDIR=' + str(work / 'tmp'), '--env', 'DYLD_PRINT_LIBRARIES=1',
            '--stdout', str(output / 'app.log'), '--stderr', str(output / 'app.err'), str(app),
            '--args', '--window-item-lifetime-case=' + str(index)], stdout=(output / 'launch.log').open('w'), stderr=subprocess.STDOUT)
        initial = wait(lambda: state() if state().get('appRunning') else None, 'item lifetime host ready'); pid = initial['processId']
        client = subprocess.Popen([str(client_binary), str(pid)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  text=True, bufsize=1, stderr=(output / 'client.err').open('w'))
        require(initial['logicalNames'] == ['保留项目','中文🙂','末项'], 'initial logical peers represent current data')
        nodes_ready('lifetime-open','lifetime-reinsert','lifetime-rebind','lifetime-check')
        nodes = live_nodes(); old = stale_tokens(nodes)
        press('lifetime-reinsert'); wait(lambda: state().get('phase') == 1, 'same object removed and reinserted')
        require(state()['logicalNames'] == ['保留项目','末项','中文🙂'], 'logical peers follow the new item order')
        reject_native(old); check_retained(3)
        nodes = live_nodes(); current_action(nodes,[1]); press('lifetime-capture')
        wait(lambda: state().get('captures') == 2, 'new logical reference captured'); old = stale_tokens(nodes)
        press('lifetime-rebind'); wait(lambda: state().get('phase') == 2, 'same objects rebound through a new collection')
        require(state()['logicalNames'] == ['保留项目','中文🙂','末项'], 'rebound logical peers represent the current collection')
        reject_native(old); check_retained(6)
        press('lifetime-narrow'); wait(lambda: state().get('narrowed') and state()['width'] <= 541, 'narrow window layout applied')
        nodes = live_nodes(); current_action(nodes,[1,1])
        press('lifetime-probe'); wait(lambda: state().get('probeCreated'), 'probe data was added, queried and removed')
        def released():
            press('lifetime-collect'); current = state()
            return current if current.get('probeDataAlive') is False and current.get('probePeerAlive') is False else None
        wait(released, 'removed probe data and peer are collected while the window remains alive')
        require(state()['ids'] == [0,1,2], 'current models survive collection of the obsolete model')
        nodes = live_nodes(); current_action(nodes,[1,1,1])
        require(state()['logicalNames'] == ['保留项目','中文🙂','末项'], 'live logical peers remain usable after collection')
        press('lifetime-finish'); wait(lambda: state().get('closed'), 'lifetime window closes'); launcher.wait(timeout=6); final = state()
        require(launcher.returncode == 0 and not final['timedOut'], 'item lifetime host exits normally')
    except Exception as error:
        failures.append(str(error)); failure_state = state()
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
                  'initial': initial, 'final': final, 'failureState': locals().get('failure_state'), 'commands': commands, 'hostPID': pid,
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
    with tempfile.TemporaryDirectory(prefix='jalium-item-lifetime-ax-', dir=args.work_parent) as name:
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
