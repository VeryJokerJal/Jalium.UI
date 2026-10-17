#!/usr/bin/env python3
"""Opt-in Window selection and notification acceptance from separate processes.

Requires an unlocked macOS session and existing Accessibility permission. Pass a
built HostSmoke.app. This never prompts or synthesizes keyboard/mouse input.
The observer is read-only; a second client invokes ordinary selection controls.
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

def run_case(work,app,client_binary,observer_binary,index,libraries):
    internal=work
    output=work/('case-'+str(index))
    output.mkdir(mode=0o700)
    eventsPath=output/'events.jsonl';eventsPath.touch();commands=[];checks=[];failures=[];observer=None;client=None;initial={};final={};pid=None;p=None
    attribute='AXSelectedRows' if index//2 else 'AXSelectedChildren';notificationName='AXSelectedRowsChanged' if index//2 else 'AXSelectedChildrenChanged'
    def state():
     try:return json.loads((output/'state.json').read_text())
     except (FileNotFoundError,json.JSONDecodeError):return {}
    def events():
     values=[]
     for line in eventsPath.read_text().splitlines():
      try:values.append(json.loads(line))
      except json.JSONDecodeError:pass
     return values
    def require(condition,message):
     if not condition:raise RuntimeError(message)
     checks.append(message)
    def wait(predicate,message,seconds=2):
     end=time.monotonic()+seconds
     while time.monotonic()<end:
      value=predicate()
      if value:return value
      time.sleep(.02)
     raise RuntimeError(message+'; state='+json.dumps(state(),ensure_ascii=False))
    def call(action,**kw):
     request={'action':action,**kw};client.stdin.write(json.dumps(request)+'\n');client.stdin.flush();poll=selectors.DefaultSelector();poll.register(client.stdout,selectors.EVENT_READ);ready=poll.select(6);poll.close();require(bool(ready),'AX response received');response=json.loads(client.stdout.readline());commands.append({'request':request,'response':response});require(response['clientPID']!=pid and response['targetPID']==pid and response['trusted'],'AX process identities and permission match')
     if action not in ('discover','quit'):require(response['actionError']==0,'AX '+action+' accepted')
     return response
    def selected_ids(values):return sorted(x.get('AXIdentifier',x.get('id')) for x in values if isinstance(x,dict) and x.get('AXIdentifier',x.get('id')) is not None)
    def wait_selection(after,indices):
     expected=sorted('selection-row-'+str(i) for i in indices)
     wait(lambda:state().get('selected')==indices,'selection reaches public model')
     def received():
      for event in events()[after:]:
       if event.get('kind')=='notification' and event.get('identifier')=='selection-container' and event.get('notification')==notificationName and len(event.get(attribute,[]))==len(indices) and selected_ids(event.get(attribute,[]))==expected:return event
      return None
     event=wait(received,'external '+notificationName+' reads selected '+str(indices))
     require(event.get(attribute) is not None,'selection callback exposes the native collection')
     value=call('read',id='selection-container')['node'];require(len(value.get(attribute,[]))==len(indices) and selected_ids(value.get(attribute,[]))==expected,'AX selected collection matches public model')
    started=time.monotonic()
    try:
     command=['/usr/bin/open','-n','-W','--env','JALIUM_SELECTION_OUTPUT='+str(output),'--env','TMPDIR='+str(internal/'tmp'),'--env','DYLD_PRINT_LIBRARIES=1','--stdout',str(output/'app.log'),'--stderr',str(output/'app.err'),str(app),'--args','--window-selection-notifications-case='+str(index)];p=subprocess.Popen(command,stdout=(output/'launch.log').open('w'),stderr=subprocess.STDOUT);initial=wait(lambda:state() if state().get('appRunning') else None,'host ready',5);pid=initial['processId']
     observer=subprocess.Popen([str(observer_binary),str(pid),'40'],stdout=eventsPath.open('w'),stderr=(output/'observer.err').open('w'))
     wait(lambda:any(e.get('kind')=='subscription' and e.get('identifier')=='selection-container' and e.get('results',{}).get(notificationName)==0 for e in events()),'selection notifications subscribed',4)
     client=subprocess.Popen([str(client_binary),str(pid)],stdin=subprocess.PIPE,stdout=subprocess.PIPE,text=True,bufsize=1,stderr=(output/'client.err').open('w'))
     nodes=call('discover')['nodes'];identified={x['id']:x for x in nodes if isinstance(x.get('id'),str) and x['id']};require('selection-container' in identified and all('selection-row-'+str(i) in identified for i in range(3)),'container and live rows are exposed');require(len({pid,client.pid,observer.pid})==3,'host, observer and client are separate processes')
     for button,indices in [('selection-second',[1]),('selection-change',[2] if index//2 else [0,2]),('selection-shrink',[0] if index//2 else [2]),('selection-clear',[])]:
      before=len(events());call('press',id=button);wait_selection(before,indices)
     before=len(events());call('set_selected',id='selection-row-1',selected=True);wait_selection(before,[1])
     before=len(events());call('set_selected',id='selection-row-1',selected=False);wait_selection(before,[])
     oldRow=identified['selection-row-1']['token'];call('press',id='selection-finish');wait(lambda:state().get('closed'),'window closes')
     stale=call('read',token=oldRow)['node'];require(stale['errors']['role']!=0,'closed window row rejects stale reads');final=state()
    except Exception as error:failures.append(str(error));print('FAIL',str(error),flush=True);final=state()
    finally:
     (output/'finish.signal').write_text('done')
     if client is not None:
      try:call('quit');client.wait(timeout=3)
      except Exception as error:failures.append('client cleanup '+str(error));client.terminate();client.wait(timeout=3)
     if p is not None:
      try:p.wait(timeout=6)
      except subprocess.TimeoutExpired:
       stop_owned_app(app)
       p.terminate();p.wait(timeout=3);failures.append('owned host cleanup timeout')
     if observer is not None:
      try:observer.wait(timeout=4)
      except subprocess.TimeoutExpired:observer.terminate();observer.wait(timeout=3);failures.append('observer cleanup timeout')
     hostPath=output/'host-result.json'
     host=json.loads(hostPath.read_text()) if hostPath.exists() else None
     errPath=output/'app.err';err=errPath.read_text(errors='replace') if errPath.exists() else ''
     loaded={name:any(str(path) in err for path in copies) for name,copies in libraries.items()}
     if not host or host.get('timedOut') or not host.get('closed') or host.get('phase')!=5:failures.append('host did not finish the close-during-selection callback')
     if not all(loaded.values()):failures.append('host did not load all copied native libraries')
     for name,process in [('launcher',p),('observer',observer),('client',client)]:
      if process is not None and process.returncode!=0:failures.append(name+' exited unsuccessfully')
     result={'index':index,'passed':not failures,'checks':checks,'failures':failures,'initial':initial,'final':final,'host':host,'commands':commands,'events':events(),'observerPID':None if observer is None else observer.pid,'clientPID':None if client is None else client.pid,'hostPID':pid,'loadedNative':loaded,'physicalInput':False,'externalAX':True,'seconds':round(time.monotonic()-started,3),'output':str(output)}
     (output/'receipt.json').write_text(json.dumps(result,ensure_ascii=False))
    return result

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app", type=Path, required=True)
    parser.add_argument("--cases", default="0,1,2,3")
    parser.add_argument("--work-parent", type=Path, default=Path.home() / "Library/Caches")
    parser.add_argument("--results-file", type=Path)
    args = parser.parse_args()
    source = args.app.resolve()
    if not source.is_dir() or source.suffix != ".app":
        parser.error("--app must name a built HostSmoke .app directory")
    try: cases = [int(value) for value in args.cases.split(",")]
    except ValueError: parser.error("--cases must contain integer indices")
    if not cases or len(set(cases)) != len(cases) or any(index not in range(4) for index in cases):
        parser.error("--cases must contain unique indices from 0 to 3")
    receipts = []
    with tempfile.TemporaryDirectory(prefix="jalium-selection-notification-ax-", dir=args.work_parent) as name:
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
