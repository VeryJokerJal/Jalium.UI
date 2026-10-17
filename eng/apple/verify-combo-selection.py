#!/usr/bin/env python3
"""ComboBox selection, Unicode value, dropdown and item lifetime in AX.

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
        return wait(current, 'live ComboBox controls are exposed')
    def press(id):
        require(call('press', id=id)['actionError'] == 0, 'ordinary control accepts ' + id)
    def expand(value):
        require(call('set_expanded', id='combo-root', expanded=value)['actionError'] == 0, 'native expanded attribute accepts update')
        wait(lambda: state().get('open') == value, 'model reflects native expanded attribute')
    def selected(id):
        current = wait(lambda: state() if state().get('selectedId') == id else None, 'logical selection committed')
        require(all(row['selected'] == (row['id'] == id) for row in current['rows'] if row['realized']),
                'realized row states agree with the selected model')
        nodes = nodes_ready('combo-root'); root = nodes['combo-root']
        expected = current['text'] if current['editable'] or id >= 0 else ''
        require(root['value'] == expected, 'native value agrees with editable text or selected display text')
        if current['open']:
            require([n['id'] for n in root.get('AXSelectedChildren', [])] == ([] if id < 0 else ['combo-row-' + str(id)]),
                    'native selected children agree with the public selection')
        return root
    def rows():
        nodes = nodes_ready('combo-root', 'combo-row-0', 'combo-row-1', 'combo-row-2', 'combo-row-3', 'combo-row-4')
        require(nodes['combo-root']['role'] == 'AXComboBox', 'selector retains native ComboBox role')
        require(nodes['combo-row-1']['name'] == '中文🙂', 'data and own rows expose their display text')
        require(nodes['combo-row-4']['enabled'] is False, 'disabled row is announced as disabled')
        for id in range(5):
            row = nodes['combo-row-' + str(id)]
            require(row['role'] == 'AXCell', 'own and data options retain the ListItem native role')
            require(row['size']['width'] >= 200 and row['size']['height'] >= 20, 'dropdown row has a usable native extent')
        return nodes
    started = time.monotonic()
    try:
        launcher = subprocess.Popen(['/usr/bin/open', '-n', '-W', '--env', 'JALIUM_SELECTION_OUTPUT=' + str(output),
            '--env', 'TMPDIR=' + str(work / 'tmp'), '--env', 'DYLD_PRINT_LIBRARIES=1',
            '--stdout', str(output / 'app.log'), '--stderr', str(output / 'app.err'), str(app),
            '--args', '--window-combo-selection-case=' + str(index)], stdout=(output / 'launch.log').open('w'), stderr=subprocess.STDOUT)
        initial = wait(lambda: state() if state().get('appRunning') else None, 'ComboBox host ready'); pid = initial['processId']
        client = subprocess.Popen([str(client_binary), str(pid)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  text=True, bufsize=1, stderr=(output / 'client.err').open('w'))
        nodes = nodes_ready('combo-root'); selected(1)
        require(not any(id.startswith('combo-row-') for id in nodes), 'initially collapsed dropdown exposes no zero-size rows')
        require(nodes['combo-root']['valueSettable'] == initial['editable'], 'only editable ComboBoxes declare writable value')
        require(call('focus', id='combo-root')['actionError'] == 0, 'native selector accepts focus')
        wait(lambda: state().get('focusWithin'), 'focus reaches the selector or its editable child')
        expand(True); nodes = rows(); old = nodes['combo-row-1']['token']
        require(call('set_selected', id='combo-row-3', selected=True)['actionError'] == 0, 'native row setter selects')
        root = selected(3); require(state()['selectedIndex'] == 3, 'equal display strings retain the requested index')
        require(call('set_selected', id='combo-row-3', selected=False)['actionError'] == 0, 'native row setter deselects')
        selected(-1)
        require(call('press', id='combo-row-1')['actionError'] == 0, 'native row press selects'); selected(1)
        before = state()['changes']; denied = call('set_selected', id='combo-row-4', selected=True)
        time.sleep(.2)
        require(denied['node']['AXSelectedSettable'] is False and denied['node']['selected'] is False
                and state()['changes'] == before and state()['selectedId'] == 1,
                'disabled native item is read only and cannot change selection')
        expand(False)
        wait(lambda: call('read', token=old)['node'].get('id') is None, 'collapsed dropdown retires its old row reference')
        call('set_selected', token=old, selected=True); selected(1)
        press('combo-clear'); wait(lambda: state().get('selectedIndex') == -1, 'ordinary clear button clears selection'); selected(-1)
        if initial['editable']:
            require(call('set_value', id='combo-root', value='自定义 🧪')['actionError'] == 0, 'editable native value accepts Unicode')
            wait(lambda: state().get('text') == '自定义 🧪', 'Unicode edit reaches the control'); selected(-1)
            press('combo-readonly'); wait(lambda: state().get('readOnly'), 'editable selector becomes read only')
            root = nodes_ready('combo-root')['combo-root']; before = state()['text']
            require(root['valueSettable'] is False, 'read-only editing removes native value writability')
            denied = call('set_value', id='combo-root', value='blocked'); time.sleep(.2)
            require(denied['node']['valueSettable'] is False and denied['node']['value'] == before and state()['text'] == before,
                    'read-only editable value cannot be replaced')
            expand(True); nodes_ready('combo-row-1'); call('press', id='combo-row-1'); selected(1)
            require(state()['readOnly'], 'read-only editing still permits dropdown item selection')
            expand(False); press('combo-readonly'); wait(lambda: not state()['readOnly'], 'editing can be reenabled')

        else:
            denied = call('set_value', id='combo-root', value='forbidden'); time.sleep(.2)
            require(denied['node']['valueSettable'] is False and denied['node']['value'] == ''
                    and state()['selectedId'] == -1, 'noneditable native value remains read only and unchanged')
            selected(-1)
        press('combo-narrow'); wait(lambda: state().get('narrowed') and state()['width'] <= 481, 'narrow layout applied')
        expand(True); nodes = rows(); current = nodes['combo-row-1']['token']
        require(nodes['combo-root']['size']['width'] >= 350, 'selection control fits the narrow window')
        call('press', token=current); selected(1)
        press('combo-replace'); wait(lambda: state().get('phase') == 1, 'selected logical item replaced')
        nodes = nodes_ready('combo-root', 'combo-row-5'); require('combo-row-1' not in nodes, 'removed row is absent from native discovery')
        wait(lambda: call('read', token=current)['node'].get('id') is None, 'removed row reference is retired')
        before = state()['changes']; call('set_selected', token=current, selected=True)
        require(state()['changes'] == before, 'retired native reference cannot select a replacement item')
        selected(5)
        press('combo-disable'); wait(lambda: not state().get('enabled'), 'selector disabled')
        root = nodes_ready('combo-root')['combo-root']; require(root['enabled'] is False, 'native selector enabled state agrees')
        denied = call('set_expanded', id='combo-root', expanded=True); time.sleep(.2)
        require(denied['node']['AXExpandedSettable'] is False and not state()['open'],
                'disabled selector cannot expand and declares expanded read only')
        press('combo-enable'); wait(lambda: state().get('enabled'), 'selector reenabled'); expand(True)
        nodes_ready('combo-row-0'); press('combo-close-next')
        require(call('press', id='combo-row-0')['actionError'] == 0, 'row action accepts close callback')
        wait(lambda: state().get('closed'), 'selection callback closes its owner'); launcher.wait(timeout=6); final = state()
        require(final['selectedId'] == 0 and final['changes'][-1] == 0, 'closing selection is committed')
        require(launcher.returncode == 0 and not final['timedOut'], 'ComboBox host exits normally')
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
    parser.add_argument('--cases', default='0,1,2,3,4,5,6,7')
    args = parser.parse_args(); source = args.app.resolve()
    if not source.is_dir() or source.suffix != '.app': parser.error('--app must name a built .app')
    try: cases = [int(x) for x in args.cases.split(',')]
    except ValueError: parser.error('--cases must contain integer indices')
    if not cases or len(set(cases)) != len(cases) or any(x not in range(8) for x in cases): parser.error('invalid cases')
    receipts = []
    with tempfile.TemporaryDirectory(prefix='jalium-combo-selection-ax-', dir=args.work_parent) as name:
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
