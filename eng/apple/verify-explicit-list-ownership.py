#!/usr/bin/env python3
"""Check explicit list-item retirement from an independent AX client.

Uses existing AX permission and own-process SDK actions. No physical input is
synthesized, and backend images are offscreen readbacks rather than screenshots.
"""
from pathlib import Path
import argparse
import hashlib
import importlib.util
import json
import selectors
import shutil
import subprocess
import tempfile
import time

spec = importlib.util.spec_from_file_location('item_lifetime', Path(__file__).with_name('verify-item-lifetime.py'))
base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)

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
    def nodes_ready(*ids):
        def current():
            nodes = {n['id']: n for n in call('discover')['nodes'] if isinstance(n.get('id'), str)}
            return nodes if all(key in nodes for key in ids) else None
        return wait(current, 'current ownership controls are exposed')
    def press(id): require(call('press', id=id)['actionError'] == 0, 'ordinary control accepts ' + id)
    def healthy():
        current = state(); require(not current.get('errors'), 'host SDK actions completed without errors')
        require(current['editorText'] == '中文🙂，继续编辑' and current['editorSelectionStart'] == 2 and current['editorSelectionLength'] == 2,
                'Chinese text and UTF-16 selection remain intact')
        return current
    started = time.monotonic()
    try:
        launcher = subprocess.Popen(['/usr/bin/open', '-n', '-W', '--env', 'JALIUM_SELECTION_OUTPUT=' + str(output),
            '--env', 'TMPDIR=' + str(work / 'tmp'), '--env', 'DYLD_PRINT_LIBRARIES=1',
            '--stdout', str(output / 'app.log'), '--stderr', str(output / 'app.err'), str(app),
            '--args', '--window-explicit-list-case=' + str(index)], stdout=(output / 'launch.log').open('w'), stderr=subprocess.STDOUT)
        initial = wait(lambda: state() if state().get('appRunning') else None, 'explicit ownership host ready'); pid = initial['processId']
        client = subprocess.Popen([str(client_binary), str(pid)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  text=True, bufsize=1, stderr=(output / 'client.err').open('w'))
        require(initial['backend'] == initial['expectedBackend'], 'actual backend agrees with the case')
        healthy()
        for phase in range(1, 5):
            nodes = nodes_ready('ownership-row-1', 'ownership-change', 'ownership-check', 'ownership-finish')
            old = nodes['ownership-row-1']['token']
            press('ownership-change'); wait(lambda: state().get('phase') == phase, 'collection mutation completed')
            wait(lambda: call('read', token=old)['node'].get('id') is None, 'obsolete native row reference is retired')
            call('press', token=old); time.sleep(.08)
            require(healthy()['selectedIndex'] == 0, 'retired native reference cannot change current selection')
            nodes_ready('ownership-check'); press('ownership-check')
            current = wait(lambda: state() if state().get('checkedPhase') == phase else None, 'retained SDK references checked')
            require(current['detached'], 'discarded direct peer no longer exposes the former list')
            require(current['logicalRetired'], 'discarded logical selection provider rejects actions')
            require(current['oldActionsPreserved'] and current['editingPreserved'], 'old direct actions and routed click preserve current selection and editing')
            require(current['retired'] == phase, 'each mutation retains its own old container')
            healthy()
            focus = call('read', id='ownership-editor')
            require(focus['focusedId'] == 'ownership-editor' and focus['focusReadError'] == 0,
                    'native AX focus returns to the editor after old-item checks')
        wait(lambda: 400 <= state().get('width', 0) <= 421, 'narrow width has settled')
        nodes_ready('ownership-move'); press('ownership-move'); wait(lambda: state().get('moved'), 'move retains current item membership')
        nodes = nodes_ready('ownership-row-2', 'ownership-reuse')
        require(call('press', token=nodes['ownership-row-2']['token'])['actionError'] == 0, 'current moved native row remains actionable')
        wait(lambda: state().get('selectedIndex') == 2, 'current moved row selects its new index')
        require(call('read', token=nodes['ownership-row-2']['token'])['node']['selected'] is True,
                'native AX reads the moved row as selected')
        press('ownership-reuse'); wait(lambda: state().get('phase') == 5 and state().get('reused'), 'old container is reused by its new list')
        nodes = nodes_ready('ownership-reused', 'ownership-finish')
        reused = call('read', token=nodes['ownership-reused']['token'])
        require(reused['node']['selected'] is True, 'native AX reads reused row selection in its new list')
        require(reused['node'].get('parent', {}).get('id') == 'ownership-other', 'native AX parent of the reused row is its current list')
        require(healthy()['selectedIndex'] == 2, 'new owner actions preserve the former list selection')
        press('ownership-finish'); wait(lambda: state().get('closed'), 'ownership window closes'); launcher.wait(timeout=6); final = state()
        require(launcher.returncode == 0 and not final['timedOut'], 'ownership host exits normally')
    except Exception as error:
        failures.append(str(error)); failure_state = state()
    finally:
        if client is not None:
            try: call('quit'); client.wait(timeout=3)
            except Exception: client.terminate(); client.wait(timeout=3)
        if launcher is not None and launcher.poll() is None:
            (output / 'finish.signal').touch()
            try: launcher.wait(timeout=6)
            except subprocess.TimeoutExpired: base.stop_owned_app(app); launcher.wait(timeout=6)
        final = state(); log = (output / 'app.err').read_text(errors='replace') if (output / 'app.err').exists() else ''
        loaded = [name for name, paths in libraries.items() if any(str(path) in log for path in paths)]
        failures += ['loaded payload not proven: ' + name for name in libraries if name not in loaded]
        return {'index': index, 'passed': not failures, 'checks': checks, 'failures': failures,
                'initial': initial, 'final': final, 'failureState': locals().get('failure_state'), 'commands': commands,
                'hostPID': pid, 'clientPID': None if client is None else client.pid, 'loadedNative': loaded,
                'externalAX': True, 'physicalInput': False, 'desktopScreenshot': False,
                'seconds': round(time.monotonic() - started, 3), 'dyldLogSHA256': hashlib.sha256(log.encode()).hexdigest()}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app', type=Path, required=True)
    parser.add_argument('--work-parent', type=Path, default=Path.home() / 'Library/Caches')
    parser.add_argument('--results-file', type=Path)
    parser.add_argument('--image-directory', type=Path)
    parser.add_argument('--cases', default=','.join(map(str, range(8))))
    args = parser.parse_args(); source = args.app.resolve()
    if not source.is_dir() or source.suffix != '.app': parser.error('--app must name a built .app')
    try: cases = [int(x) for x in args.cases.split(',')]
    except ValueError: parser.error('--cases must contain integer indices')
    if not cases or len(set(cases)) != len(cases) or any(x not in range(8) for x in cases): parser.error('invalid cases')
    if args.image_directory: args.image_directory.mkdir(exist_ok=False, parents=True)
    receipts = []
    with tempfile.TemporaryDirectory(prefix='jalium-explicit-list-ax-', dir=args.work_parent) as name:
        work = Path(name); (work / 'tmp').mkdir(); app = work / 'HostSmoke.app'
        subprocess.run(['/usr/bin/ditto', str(source), str(app)], check=True)
        libraries, hashes, assemblies = {}, {}, {}
        for filename in base.LIBRARIES + base.ASSEMBLIES:
            originals = list(source.rglob(filename)); copies = [app / p.relative_to(source) for p in originals]
            if not originals or any(base.digest(a) != base.digest(b) for a, b in zip(originals, copies)): raise RuntimeError('payload changed: ' + filename)
            if filename in base.LIBRARIES:
                libraries[filename] = copies; hashes[filename] = [base.digest(p) for p in copies]
            else:
                if len(originals) != 1: raise RuntimeError('ambiguous assembly: ' + filename)
                assemblies[filename] = base.digest(copies[0])
        client_source = Path(__file__).with_name('window-ax-action-client.mm'); client = work / 'client'
        subprocess.run(['xcrun', 'clang++', '-std=c++17', '-fobjc-arc', str(client_source), '-framework',
                        'Foundation', '-framework', 'ApplicationServices', '-o', str(client)], check=True)
        try:
            for index in cases:
                receipt = run_case(work, app, client, index, libraries); receipts.append(receipt)
                if args.image_directory:
                    for picture in (work / ('case-' + str(index))).glob('*.png'):
                        shutil.copy2(picture, args.image_directory / ('case-' + str(index) + '-' + picture.name))
                print(json.dumps({'case': index, 'passed': receipt['passed'], 'checks': len(receipt['checks']), 'failures': receipt['failures']}), flush=True)
        finally: base.stop_owned_app(app)
        result = {'cases': receipts, 'passed': sum(x['passed'] for x in receipts), 'total': len(cases),
                  'externalAX': True, 'physicalInput': False, 'desktopScreenshot': False,
                  'nativeSHA256': hashes, 'managedSHA256': assemblies, 'clientSHA256': base.digest(client),
                  'clientSourceSHA256': base.digest(client_source), 'temporaryRoot': str(work)}
    result['temporaryRootRemoved'] = not Path(result['temporaryRoot']).exists()
    if args.results_file: args.results_file.write_text(json.dumps(result, ensure_ascii=False))
    print(json.dumps({k: result[k] for k in ('passed', 'total', 'externalAX', 'physicalInput', 'temporaryRootRemoved')}), flush=True)
    return 0 if result['passed'] == result['total'] else 1

if __name__ == '__main__': raise SystemExit(main())
