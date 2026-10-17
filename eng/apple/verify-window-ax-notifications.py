#!/usr/bin/env python3
"""Opt-in Window AX read/write and notification acceptance from separate processes.

Requires an unlocked macOS session and existing Accessibility permission. Pass a
built HostSmoke.app. This never prompts or synthesizes keyboard/mouse input.
The observer is read-only; a second client invokes the existing public controls.
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

LIBRARIES = tuple("libjalium.native." + name + ".dylib" for name in
                  ("core", "text", "metal", "software", "platform", "media.core", "media", "browser"))
ASSEMBLIES = ("Jalium.UI.MacOS.HostSmoke.dll", "Jalium.UI.Managed.dll", "Jalium.UI.MacOS.dll")

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def stop_owned_app(app):
    prefix = str(app / "Contents/MacOS") + "/"
    rows = subprocess.check_output(["ps", "-axo", "pid=,command="], text=True)
    for row in rows.splitlines():
        pid, _, command = row.strip().partition(" ")
        if command.startswith(prefix):
            try: os.kill(int(pid), signal.SIGTERM)
            except ProcessLookupError: pass

def run_case(work, app, client_binary, observer_binary, index, libraries):
    internal = work
    output = work / ("case-" + str(index))
    output.mkdir(mode=0o700)
    commands=[];checks=[];failures=[];focusStates=[];observer=None;client=None;initial={};final={};p=None
    eventsPath=output/'events.jsonl';eventsPath.touch()
    def state():
        try:return json.loads((output/'state.json').read_text())
        except (FileNotFoundError,json.JSONDecodeError):return {}
    def events():
        values=[]
        for line in eventsPath.read_text().splitlines():
            try:values.append(json.loads(line))
            except json.JSONDecodeError:pass
        return values
    def require(value,message):
        if not value:raise RuntimeError(message)
        checks.append(message)
    def wait_for(predicate,message,seconds=2):
        end=time.monotonic()+seconds
        while time.monotonic()<end:
            value=predicate()
            if value:return value
            if p is not None and p.poll() is not None:break
            time.sleep(.02)
        raise RuntimeError(message+'; latest host='+json.dumps(state(),ensure_ascii=False))
    def call(action,**kwargs):
        request={'action':action,**kwargs};client.stdin.write(json.dumps(request,ensure_ascii=False)+'\n');client.stdin.flush()
        select=selectors.DefaultSelector();select.register(client.stdout,selectors.EVENT_READ)
        ready=select.select(6);select.close()
        if not ready:raise RuntimeError('AX command response timeout: '+action)
        raw=client.stdout.readline()
        if not raw:raise RuntimeError('AX client exited: '+action)
        response=json.loads(raw);commands.append({'request':request,'response':response})
        require(response['clientPID']!=pid and response['targetPID']==pid and response['trusted'],'separate trusted AX client identity')
        if action not in ('discover','quit'):require(response['actionError']==0,'external '+action+' accepted')
        return response
    def discover():
        value=call('discover');return {n['id']:n for n in value['nodes'] if isinstance(n.get('id'),str) and n['id']}
    def notification(after,name,identifier=None,focused=None,value=None,selection=None):
        for entry in events()[after:]:
            if entry.get('kind')!='notification' or entry.get('notification')!=name:continue
            if identifier is not None and entry.get('identifier')!=identifier:continue
            if focused is not None and entry.get('focusedIdentifier')!=focused:continue
            if value is not None and entry.get('AXValue')!=value:continue
            if selection is not None and entry.get('AXSelectedTextRange')!=selection:continue
            return entry
        return None
    def press(identifier):return call('press',id=identifier)
    def marker():return len(events())

    started=time.monotonic()
    try:
        command=['/usr/bin/open','-n','-W','--env','JALIUM_AX_NOTIFICATION_OUTPUT='+str(output),
            '--env','TMPDIR='+str(internal/'tmp'),'--env','DYLD_PRINT_LIBRARIES=1',
            '--stdout',str(output/'app.log'),'--stderr',str(output/'app.err'),str(app),
            '--args','--window-ax-notifications-case='+str(index)]
        launchLog=(output/'launch.log').open('w');p=subprocess.Popen(command,stdout=launchLog,stderr=subprocess.STDOUT)
        initial=wait_for(lambda:state() if state().get('appRunning') else None,'host ready',5);pid=initial['processId']
        observerLog=eventsPath.open('w');observerError=(output/'observer.err').open('w')
        observer=subprocess.Popen([str(observer_binary),str(pid),'40'],stdout=observerLog,stderr=observerError)
        wait_for(lambda:any(e.get('kind')=='subscription' and e.get('identifier')=='notification-editor' and
            e['results'].get('AXValueChanged')==0 and e['results'].get('AXSelectedTextChanged')==0 for e in events()),
            'editor notification subscriptions registered',4)
        client=subprocess.Popen([str(client_binary),str(pid)],stdin=subprocess.PIPE,stdout=subprocess.PIPE,
            stderr=(output/'client.err').open('w'),text=True,bufsize=1)
        wait_for(lambda:any(e.get('kind')=='ready' and e.get('pid')==pid for e in events()),
            'observer is subscribed to the target application')
        require(observer.pid != pid and observer.pid != client.pid and client.pid != pid,
            'observer, action client and host use distinct processes')
        nodes=discover();require('notification-note' in nodes and 'notification-editor' in nodes,'live tree exposes identified controls')
        oldNote=nodes['notification-note']['token'];oldEditor=nodes['notification-editor']['token']
        time.sleep(.2)
        text='跨🙂 e\u0301 中文';projected=text+('\n' if index//2==1 else '');start=marker();require(nodes['notification-editor']['valueSettable'],'native AX value capability is writable');call('set_value',id='notification-editor',value=text)
        wait_for(lambda:state().get('editor')==projected,'AX value writes reach the managed editor')
        event=wait_for(lambda:notification(start,'AXValueChanged','notification-editor',value=projected),'external value notification')
        require(event.get('AXValue')==projected,'value callback reads committed Unicode text')
        start=marker();press('notification-sample');sample='示🙂 e\u0301 中文'+('\n' if index//2==1 else '')
        wait_for(lambda:state().get('editor')==sample,'ordinary button updates text')
        event=wait_for(lambda:notification(start,'AXValueChanged','notification-editor',value=sample),'model-originated value notification')
        require(event.get('AXValue')==sample,'model-originated callback reads committed text')
        start=marker();call('set_selection',id='notification-editor',start=1,length=5)
        wait_for(lambda:state().get('selectionStart')==1 and state().get('selectionLength')==5,'UTF16 selection reaches model')
        event=wait_for(lambda:notification(start,'AXSelectedTextChanged','notification-editor',value=sample,selection={'location':1,'length':5}),'external selection notification')
        require(event.get('AXSelectedText')=='🙂 e\u0301','selection callback preserves emoji and combining text')
        def focused_in_foreground(identifier):
            current = state()
            return current if (current.get('focused') == identifier and current.get('frontmostPID') == pid
                and current.get('active') and current.get('appActive') and current.get('appRunning')) else None
        for identifier in ['notification-second','notification-editor']:
            start=marker();call('focus',id=identifier)
            # A real AX focus request may activate a background application.
            # Require the resulting physical window and logical focus together.
            focusStates.append(wait_for(lambda:focused_in_foreground(identifier),
                'AX focus reaches the actual foreground '+identifier))
            wait_for(lambda:notification(start,'AXFocusedUIElementChanged',focused=identifier),'external focus notification '+identifier)
        require(len(focusStates)==2,'focus checks have actual foreground conditions')
        start=marker();press('notification-hide-note')
        wait_for(lambda:state().get('noteVisible') is False,'note hides')
        wait_for(lambda:notification(start,'AXLayoutChanged'),'hide emits layout notification')
        require('notification-note' not in discover(),'hidden note is absent from live tree')
        press('notification-hide-note');wait_for(lambda:state().get('noteVisible') is True,'note restores')
        nodes=discover();require(nodes['notification-note']['token']==oldNote,'visibility restoration preserves AX identity')
        start=marker();press('notification-remove-note')
        wait_for(lambda:state().get('noteAttached') is False,'note detaches')
        wait_for(lambda:notification(start,'AXUIElementDestroyed','notification-note'),'detaching note emits destruction')
        require('notification-note' not in discover(),'detached note is absent from live tree')
        stale=call('read',token=oldNote)['node'];require(stale['errors']['value']!=0,'retired AX reference rejects value reads')
        press('notification-remove-note');wait_for(lambda:state().get('noteAttached') is True,'note reattaches')
        nodes=discover();require(nodes['notification-note']['token']!=oldNote,'reattached node receives a new AX identity')
        start=marker();press('notification-size');wait_for(lambda:state().get('width')==520,'window reaches minimum size')
        wait_for(lambda:notification(start,'AXWindowResized'),'native resize notification')
        press('notification-size');wait_for(lambda:state().get('width')==640,'window restores normal size')
        press('notification-hide-window');wait_for(lambda:state().get('windowVisible') is False,'window hides')
        wait_for(lambda:state().get('windowRestores')==1 and state().get('windowVisible') is True,'window restores',3)
        nodes=discover();require(nodes['notification-editor']['token']==oldEditor,'window restoration preserves editor identity')
        require(state()['editor']==sample and state()['selectionStart']==1 and state()['selectionLength']==5,'window restoration preserves text and selection')
        start=marker();press('notification-finish');wait_for(lambda:state().get('closed') is True,'window closes')
        wait_for(lambda:notification(start,'AXUIElementDestroyed','notification-editor'),'window close retires editor AX object')
        stale=call('read',token=oldEditor)['node'];require(stale['errors']['value']!=0,'closed window AX reference rejects value reads')
        final=state()
    except Exception as error:
        failures.append(str(error));print('FAIL',str(error),flush=True);final=state()
    finally:
        (output/'finish.signal').write_text('done')
        if client is not None:
            try:call('quit')
            except Exception as error:failures.append('client cleanup: '+str(error))
            try:client.wait(timeout=3)
            except subprocess.TimeoutExpired:client.terminate();client.wait(timeout=3)
        if p is not None:
            try:p.wait(timeout=6)
            except subprocess.TimeoutExpired:
                stop_owned_app(app)
                p.terminate();p.wait(timeout=3);failures.append('host cleanup timeout')
        if observer is not None:
            try:observer.wait(timeout=4)
            except subprocess.TimeoutExpired:observer.terminate();observer.wait(timeout=3);failures.append('observer cleanup timeout')
        eventList=events();receipt={'index':index,'passed':not failures,'checks':checks,'failures':failures,
            'focusStates':focusStates,'hostPID':initial.get('processId'),'observerPID':None if observer is None else observer.pid,
            'clientPID':None if client is None else client.pid,
            'initial':initial,'final':final,'commands':commands,'events':eventList,'physicalInput':False,'externalAX':True,
            'seconds':round(time.monotonic()-started,3),'output':str(output)}
        host_path = output / 'host-result.json'
        host = json.loads(host_path.read_text()) if host_path.exists() else None
        err_path = output / 'app.err'
        err = err_path.read_text(errors='replace') if err_path.exists() else ''
        loaded = {name: any(str(path) in err for path in copies) for name, copies in libraries.items()}
        if not host or host.get('timedOut') or not host.get('closed'):
            failures.append('host did not finish a closed, non-timeout case')
        if p is not None and p.returncode != 0: failures.append('launcher exited unsuccessfully')
        if client is not None and client.returncode != 0: failures.append('AX client exited unsuccessfully')
        if observer is not None and observer.returncode != 0: failures.append('AX observer exited unsuccessfully')
        if not all(loaded.values()): failures.append('host did not load every copied native library')
        for stream in ('launchLog', 'observerLog', 'observerError'):
            handle = locals().get(stream)
            if handle is not None: handle.close()
        receipt.update({'passed':not failures, 'host':host, 'loadedNative':loaded,
            'launcherExitCode':None if p is None else p.returncode,
            'clientExitCode':None if client is None else client.returncode,
            'observerExitCode':None if observer is None else observer.returncode})
        (output/'receipt.json').write_text(json.dumps(receipt,ensure_ascii=False))
    return receipt

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app", type=Path, required=True)
    parser.add_argument("--cases", default="0,1,2,3,4,5")
    parser.add_argument("--work-parent", type=Path, default=Path.home() / "Library/Caches")
    parser.add_argument("--results-file", type=Path)
    args = parser.parse_args()
    source = args.app.resolve()
    if not source.is_dir() or source.suffix != ".app":
        parser.error("--app must name a built HostSmoke .app directory")
    try: cases = [int(value) for value in args.cases.split(",")]
    except ValueError: parser.error("--cases must contain integer indices")
    if not cases or len(set(cases)) != len(cases) or any(index not in range(6) for index in cases):
        parser.error("--cases must contain unique indices from 0 to 5")
    receipts = []
    with tempfile.TemporaryDirectory(prefix="jalium-window-notification-ax-", dir=args.work_parent) as name:
        work = Path(name)
        (work / "tmp").mkdir()
        app = work / "HostSmoke.app"
        subprocess.run(["/usr/bin/ditto", str(source), str(app)], check=True)
        libraries, library_hashes, assembly_hashes = {}, {}, {}
        for filename in LIBRARIES:
            originals = list(source.rglob(filename))
            copies = list(app.rglob(filename))
            if not originals or len(originals) != len(copies):
                raise RuntimeError("Missing native payload: " + filename)
            for original in originals:
                if digest(original) != digest(app / original.relative_to(source)):
                    raise RuntimeError("Copied native payload changed: " + filename)
            libraries[filename] = copies
            library_hashes[filename] = [digest(path) for path in copies]
        for filename in ASSEMBLIES:
            originals = list(source.rglob(filename))
            if len(originals) != 1: raise RuntimeError("Expected one managed assembly: " + filename)
            original = originals[0]
            if digest(original) != digest(app / original.relative_to(source)):
                raise RuntimeError("Copied managed payload changed: " + filename)
            assembly_hashes[filename] = digest(original)
        client_source = Path(__file__).with_name("window-ax-action-client.mm")
        observer_source = Path(__file__).resolve().parents[2] / "src/native/jalium.native.platform/tests/platform_apple_notification_observer.mm"
        client, observer = work / "action-client", work / "observer"
        for path, output in ((client_source, client), (observer_source, observer)):
            subprocess.run(["xcrun", "clang++", "-std=c++17", "-fobjc-arc", str(path),
                "-framework", "Foundation", "-framework", "ApplicationServices", "-o", str(output)], check=True)
        try:
            for index in cases:
                receipt = run_case(work, app, client, observer, index, libraries)
                receipts.append(receipt)
                print(json.dumps({"case":index,"passed":receipt["passed"],"checks":len(receipt["checks"]),
                    "failures":receipt["failures"]}), flush=True)
                if any(command['response'].get('trusted') == 0 for command in receipt['commands']): break
        finally:
            stop_owned_app(app)
        result = {"cases":receipts,"passed":sum(item["passed"] for item in receipts),"total":len(cases),
            "physicalInput":False,"externalAX":True,"nativeSHA256":library_hashes,"managedSHA256":assembly_hashes,
            "clientSHA256":digest(client),"clientSourceSHA256":digest(client_source),
            "observerSHA256":digest(observer),"observerSourceSHA256":digest(observer_source),"temporaryRoot":str(work)}
    result["temporaryRootRemoved"] = not Path(result["temporaryRoot"]).exists()
    if args.results_file: args.results_file.write_text(json.dumps(result,ensure_ascii=False))
    print(json.dumps({key:result[key] for key in ("passed","total","physicalInput","externalAX","temporaryRootRemoved")}),flush=True)
    return 0 if result["passed"] == result["total"] else 1

if __name__ == "__main__":
    raise SystemExit(main())
