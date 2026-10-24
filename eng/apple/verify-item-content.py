#!/usr/bin/env python3
"""Interactive item content retains its controls, focus and lifetime in AX.

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
    def press(identifier):
        r = call('press', id=identifier); require(r['actionError'] == 0, 'ordinary control accepts ' + identifier)
    def discover(): return {n['id']: n for n in call('discover')['nodes'] if isinstance(n.get('id'), str)}
    def discover_ready(generation):
        expected = ['item-root'] + ['item-' + kind + '-' + str(generation) for kind in ('check','action','editor')]
        def current():
            nodes = discover()
            return nodes if all(identifier in nodes for identifier in expected) else None
        return wait(current, 'three live row controls are exposed')
    def verify_controls(nodes, generation):
        ids = {kind: 'item-' + kind + '-' + str(generation) for kind in ('check', 'action', 'editor')}
        require(all(value in nodes for value in ids.values()), 'three live row controls are exposed')
        require(nodes[ids['check']]['role'] == 'AXCheckBox' and nodes[ids['action']]['role'] == 'AXButton'
                and nodes[ids['editor']]['role'] == 'AXTextField', 'row controls retain their native roles')
        require(all(nodes[value].get('parent', {}).get('id') == 'item-root' for value in ids.values()),
                'row controls retain the row as their semantic parent')
        require(all(nodes[value].get('enabled') is True for value in ids.values()), 'row controls remain enabled')
        require(sum(key.startswith('item-check-') for key in nodes) == 1, 'obsolete checkbox is absent from discovery')
        row = nodes['item-root']; bounds = row['position']; extent = row['size']
        for kind, minimum in [('check', 88), ('action', 100), ('editor', 210)]:
            node = nodes[ids[kind]]; position = node['position']; size = node['size']
            require(size['width'] >= minimum and size['height'] >= 30, kind + ' has an unclipped usable extent')
            require(position['x'] >= bounds['x'] - 1 and position['y'] >= bounds['y'] - 1
                    and position['x'] + size['width'] <= bounds['x'] + extent['width'] + 1
                    and position['y'] + size['height'] <= bounds['y'] + extent['height'] + 1,
                    kind + ' lies within its visible row')
        return {kind: nodes[value]['token'] for kind, value in ids.items()}
    def remains_unselected():
        require(state()['selected'] is False, 'row interaction preserves empty item selection')
    started = time.monotonic()
    try:
        launcher = subprocess.Popen(['/usr/bin/open', '-n', '-W', '--env', 'JALIUM_SELECTION_OUTPUT=' + str(output),
            '--env', 'TMPDIR=' + str(work / 'tmp'), '--env', 'DYLD_PRINT_LIBRARIES=1',
            '--stdout', str(output / 'app.log'), '--stderr', str(output / 'app.err'), str(app),
            '--args', '--window-item-content-case=' + str(index)], stdout=(output / 'launch.log').open('w'), stderr=subprocess.STDOUT)
        initial = wait(lambda: state() if state().get('appRunning') and state().get('generation', 0) > 0 else None,
                       'item content host ready'); pid = initial['processId']; generation = initial['generation']
        client = subprocess.Popen([str(client_binary), str(pid)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  text=True, bufsize=1, stderr=(output / 'client.err').open('w'))
        nodes = discover_ready(generation); old = verify_controls(nodes, generation); remains_unselected()
        measured = initial['measuredHeaders']
        require(measured['treePlain'] == 28 and measured['selectorPlain'] == 28, 'default plain header heights remain 28')
        require(measured['treeTall'] >= 48 and measured['selectorTall'] >= 48, 'both default tree header styles measure tall content')
        press('item-narrow'); wait(lambda: state().get('narrowed') and state().get('width', 0) <= 601, 'narrow window layout applied')
        nodes = discover_ready(generation); verify_controls(nodes, generation)
        if initial['outline']:
            require('item-nested' in nodes, 'expanded outline exposes its nested row')
            press('item-collapse'); wait(lambda: state()['expanded'] is False, 'outline collapses')
            nodes = discover_ready(generation); verify_controls(nodes, generation)
            require('item-nested' not in nodes, 'collapse hides the nested row and retains header controls')
        call('focus', token=old['check'])
        focused = wait(lambda: (node if (node := call('read', token=old['check'])['node']).get('focused') is True and state()['checkFocused'] else None),
                       'model and native checkbox focus agree'); require(focused['focused'] is True, 'native AX reports checkbox focus')
        call('press', token=old['check']); wait(lambda: state()['checkedValue'] is True, 'row checkbox toggles'); remains_unselected()
        call('press', token=old['action']); wait(lambda: state()['invocations'] == [generation], 'row button invokes actual content'); remains_unselected()
        call('focus', token=old['editor']); wait(lambda: state()['editorFocused'] and call('read', token=old['editor'])['node'].get('focused') is True, 'model and native row editor focus agree')
        call('set_value', token=old['editor'], value='中文🙂'); wait(lambda: state()['text'] == '中文🙂', 'row editor accepts Unicode text'); remains_unselected()
        press('item-replace'); replaced = wait(lambda: state() if state()['phase'] == 1 and state()['generation'] != generation else None,
                                             'content or selected template is replaced')
        new_generation = replaced['generation']; nodes = discover_ready(new_generation); current = verify_controls(nodes, new_generation)
        require(all(current[k] != old[k] for k in old), 'replacement controls have fresh AX identities')
        before = state(); old_widgets = before['widgets']; old_invocations = before['invocations']
        for kind, token in old.items():
            value = call('read', token=token)['node']
            require(value.get('id') is None, 'obsolete ' + kind + ' reference is invalid')
        call('press', token=old['check']); call('press', token=old['action']); call('set_value', token=old['editor'], value='obsolete')
        time.sleep(.2); require(state()['widgets'] == old_widgets and state()['invocations'] == old_invocations,
                               'obsolete references cannot toggle, invoke or edit any item')
        call('press', token=current['check']); wait(lambda: state()['checkedValue'] is True, 'replacement checkbox remains operable')
        call('focus', token=current['editor']); wait(lambda: state()['editorFocused'] and call('read', token=current['editor'])['node'].get('focused') is True, 'model and native replacement editor focus agree')
        call('set_value', token=current['editor'], value='替换后🙂'); wait(lambda: state()['text'] == '替换后🙂', 'replacement editor remains writable'); remains_unselected()
        if initial['outline']:
            press('item-collapse'); wait(lambda: state()['expanded'] is False, 'replacement outline collapses')
            nodes = discover_ready(new_generation); verify_controls(nodes, new_generation); require('item-nested' not in nodes, 'replacement nested row stays hidden')
            press('item-expand'); wait(lambda: state()['expanded'] is True, 'replacement outline expands')
            require('item-nested' in discover(), 'nested row is exposed after expansion')
        press('item-close-next'); call('press', token=current['action']); wait(lambda: state().get('closed'), 'row action closes its own window')
        launcher.wait(timeout=6); final = state()
        require(final['invocations'] == [generation, new_generation] and not final['selected'], 'final row action is committed once without selecting the row')
        require(launcher.returncode == 0 and not final['timedOut'], 'item content host exits successfully')
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
                  'launcherExitCode': None if launcher is None else launcher.poll(),
                  'hostErrorTail': log[-12000:] if failures else None,
                  'hostOutputTail': (output / 'app.log').read_text(errors='replace')[-12000:] if failures and (output / 'app.log').exists() else None,
                  'launcherOutputTail': (output / 'launch.log').read_text(errors='replace')[-12000:] if failures and (output / 'launch.log').exists() else None,
                  'externalAX': True, 'physicalInput': False, 'seconds': round(time.monotonic() - started, 3)}
        (output / 'receipt.json').write_text(json.dumps(result, ensure_ascii=False))
    return result

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app', type=Path, required=True)
    parser.add_argument('--work-parent', type=Path, default=Path.home() / 'Library/Caches')
    parser.add_argument('--results-file', type=Path)
    parser.add_argument('--cases', default='0,1,2,3,4,5,6,7,8,9,10,11')
    args = parser.parse_args(); source = args.app.resolve()
    if not source.is_dir() or source.suffix != '.app': parser.error('--app must name a built .app')
    try: cases = [int(x) for x in args.cases.split(',')]
    except ValueError: parser.error('--cases must contain integer indices')
    if not cases or len(set(cases)) != len(cases) or any(x not in range(12) for x in cases): parser.error('invalid cases')
    receipts = []
    with tempfile.TemporaryDirectory(prefix='jalium-item-content-ax-', dir=args.work_parent) as name:
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
