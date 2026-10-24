#!/usr/bin/env python3
"""Exercise equal ListBox occurrences through an independent native AX client.
Own-NSView SDK checks and renderer readbacks are separate from physical input.
Requires existing AX permission; never requests permission or injects OS input.
"""
from pathlib import Path
import argparse,hashlib,json,os,selectors,shutil,signal,subprocess,tempfile,time
LIBRARIES=tuple('libjalium.native.'+name+'.dylib' for name in ('core','text','metal','software','platform','media.core','media','browser'))
ASSEMBLIES=('Jalium.UI.MacOS.HostSmoke.dll','Jalium.UI.Managed.dll','Jalium.UI.MacOS.dll','Jalium.UI.Xaml.dll')
def digest(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def stop_owned_app(app):
 prefix=str(app/'Contents/MacOS')+'/'
 for line in subprocess.check_output(['ps','-axo','pid=,command='],text=True).splitlines():
  pid,_,command=line.strip().partition(' ')
  if command.startswith(prefix):
   try: os.kill(int(pid),signal.SIGTERM)
   except ProcessLookupError: pass

def run_case(work,app,binary,index,libraries,image_directory):
 output=work/('case-'+str(index));output.mkdir(mode=0o700)
 commands=[];checks=[];failures=[];groups=[];launcher=client=None;pid=None;initial={};old_tokens=[]
 def state():
  try: return json.loads((output/'state.json').read_text())
  except (FileNotFoundError,json.JSONDecodeError): return {}
 def require(ok,message):
  if not ok: raise RuntimeError(message)
  checks.append(message)
 def wait(predicate,message,seconds=5):
  end=time.monotonic()+seconds
  while time.monotonic()<end:
   value=predicate()
   if value: return value
   time.sleep(.02)
  raise RuntimeError(message+'; state='+json.dumps(state(),ensure_ascii=False))
 def call(action,**kwargs):
  request={'action':action,**kwargs};client.stdin.write(json.dumps(request)+'\n');client.stdin.flush()
  poll=selectors.DefaultSelector();poll.register(client.stdout,selectors.EVENT_READ);ready=poll.select(6);poll.close()
  require(bool(ready),'AX reply received');line=client.stdout.readline();require(bool(line),'AX client remains live')
  response=json.loads(line);commands.append({'request':request,'response':response})
  require(response.get('trusted') and response['clientPID']!=pid and response['targetPID']==pid,'AX permission and separate process identities match')
  return response
 def discover(): return {n['id']:n for n in call('discover').get('nodes',[]) if isinstance(n.get('id'),str)}
 def press(identifier=None,token=None):
  value=call('press',**({'token':token} if token else {'id':identifier}));require(value.get('actionError')==0,'current AX action accepted '+str(identifier or token));return value
 def selected(rows):
  return wait(lambda: state() if state().get('selectedRows')==rows else None,'model selected occurrences '+str(rows))
 def batch(root,rows,attribute='AXSelectedChildren',accept=True):
  response=call('set_selected_items',token=root,attribute=attribute,tokens=rows);error=response.get('actionError')
  require(error==0 if accept else error not in (None,-25202),'AX batch '+('accepts current occurrences' if accept else 'invalid batch completes without a messaging timeout'))
  return response
 started=time.monotonic()
 try:
  launcher=subprocess.Popen(['/usr/bin/open','-n','-W','--env','JALIUM_OCCURRENCE_OUTPUT='+str(output),'--env','TMPDIR='+str(work/'tmp'),'--env','DYLD_PRINT_LIBRARIES=1','--stdout',str(output/'app.log'),'--stderr',str(output/'app.err'),str(app),'--args','--window-occurrence-case='+str(index)],stdout=(output/'launch.log').open('w'),stderr=subprocess.STDOUT)
  initial=wait(lambda: state() if state().get('appRunning') and state().get('dataKind')==0 and not state().get('running') else None,'occurrence host ready',20);pid=initial['processId']
  client=subprocess.Popen([str(binary),str(pid)],stdin=subprocess.PIPE,stdout=subprocess.PIPE,text=True,bufsize=1,stderr=(output/'client.err').open('w'))
  for kind in range(3):
   start=len(checks);group_failures=[]
   try:
    wait(lambda:state().get('dataKind')==kind and not state().get('running'),'data group ready')
    nodes=discover();require(all(x in nodes for x in ['occurrence-list','occurrence-row-0','occurrence-row-1','occurrence-next']),'actual native AX tree exposes list, both equal rows and controls')
    root=nodes['occurrence-list']['token'];first=nodes['occurrence-row-0']['token'];second=nodes['occurrence-row-1']['token']
    require(first!=second,'equal content has distinct native AX objects');require(not any(t in (first,second) for t in old_tokens),'new item view does not revive removed AX identities')
    press(token=second);selected([1]);read=call('read',token=root)['node']
    require([x['token'] for x in read.get('AXSelectedChildren',[])]==[second],'native selected children return only the second occurrence')
    require(read.get('AXSelectedChildrenSettable'),'native list selected-children collection remains writable')
    batch(root,[first,second]);selected([0,1]);require(state()['selectedCount']==2,'two equal occurrences survive collection replacement')
    batch(root,[second]);selected([1])
    phase=state()['phase'];press('occurrence-single');wait(lambda:state()['phase']>phase,'single mode applied')
    batch(root,[first,second],accept=False);time.sleep(.08);require(state()['selectedRows']==[1],'rejected multirow batch leaves single selection unchanged');require([x['token'] for x in call('read',token=root)['node'].get('AXSelectedChildren',[])]==[second],'native selected children remain unchanged after rejected batch')
    phase=state()['phase'];press('occurrence-multiple');wait(lambda:state()['phase']>phase,'multiple mode restored')
    captures=state()['captures'];press('occurrence-capture');wait(lambda:state()['captures']>captures,'selected backend image captured')
    phase=state()['phase'];press('occurrence-insert');wait(lambda:state()['phase']>phase and state()['count']==4,'insert commits');selected([2])
    press(token=second);selected([2]);read=call('read',token=root)['node'];require([x['token'] for x in read.get('AXSelectedChildren',[])]==[second],'retained AX object follows its surviving occurrence after index shift')
    phase=state()['phase'];press('occurrence-remove');wait(lambda:state()['phase']>phase and state()['count']==3,'selected occurrence removed');selected([])
    rejected=call('press',token=second,diagnostics=True);require(rejected.get('actionError') not in (None,0) and rejected.get('requestElapsedSeconds',99)<.25,'intentional retired-row action is rejected promptly');require(rejected.get('node',{}).get('id') in (None,'occurrence-row-1','occurrence-row-2'),'retired native row never names replacement content');time.sleep(.08);require(state()['selectedRows']==[],'obsolete row never selects its equal sibling')
    press(token=first);selected([1]);batch(root,[]);selected([]);old_tokens.extend([first,second])
   except Exception as error:
    group_failures.append(str(error));failures.append('kind='+str(kind)+' '+str(error))
   groups.append({'kind':kind,'passed':not group_failures,'checks':checks[start:],'failures':group_failures,'state':state()})
   if kind<2:
    try:
     discover();press('occurrence-next');wait(lambda:state().get('dataKind')==kind+1 and not state().get('running'),'next equal-data shape completes',12)
    except Exception as error: failures.append('advance '+str(error));break
 except Exception as error: failures.append(str(error))
 finally:
  if client is not None:
   try: call('quit');client.wait(timeout=3)
   except Exception: client.terminate();client.wait(timeout=3)
  if launcher is not None and launcher.poll() is None:
   (output/'finish.signal').touch()
   try: launcher.wait(timeout=8)
   except subprocess.TimeoutExpired: stop_owned_app(app);launcher.wait(timeout=6);failures.append('owned host required termination')
  final=state();loaded=[]
  log=(output/'app.err').read_text(errors='replace') if (output/'app.err').exists() else ''
  for name,paths in libraries.items():
   if not any(str(path) in log for path in paths): failures.append('loaded payload not proven: '+name)
   else: loaded.append(name)
  if not final.get('closed'): failures.append('host terminal state unavailable')
  if final.get('failures'): failures.extend('SDK '+x for x in final['failures'])
  if len(final.get('scenarios',[]))!=33 or final.get('nestedLoops')!=12: failures.append('complete 33-scenario/12-nested-loop SDK coverage unavailable')
  image_receipts=[]
  if image_directory:
   target=image_directory/('case-'+str(index));target.mkdir(parents=True,exist_ok=True)
   for path in output.glob('*.png'):
    shutil.copyfile(path,target/path.name);image_receipts.append({'path':str(target/path.name),'sha256':digest(path)})
  result={'index':index,'passed':not failures,'checks':checks,'failures':failures,'groups':groups,'initial':initial,'final':final,'commands':commands,'hostPID':pid,'clientPID':None if client is None else client.pid,'loadedNative':loaded,'images':image_receipts,'externalAX':True,'ownNSViewSDK':True,'writingToolsDelegateSDK':True,'appleIntelligenceService':False,'physicalInput':False,'desktopScreenshot':False,'seconds':round(time.monotonic()-started,3)}
  (output/'receipt.json').write_text(json.dumps(result,ensure_ascii=False))
 return result

def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--app',type=Path,required=True);parser.add_argument('--work-parent',type=Path,required=True);parser.add_argument('--results-file',type=Path,required=True);parser.add_argument('--image-directory',type=Path);parser.add_argument('--cases',default=','.join(map(str,range(12))))
 args=parser.parse_args();source=args.app.resolve();cases=list(map(int,args.cases.split(',')))
 if not source.is_dir() or source.suffix!='.app' or not cases or len(cases)!=len(set(cases)) or any(x not in range(12) for x in cases):parser.error('invalid app or case matrix')
 receipts=[]
 with tempfile.TemporaryDirectory(prefix='jalium-occurrence-ax-',dir=args.work_parent) as name:
  work=Path(name);(work/'tmp').mkdir();app=work/'HostSmoke.app';subprocess.run(['/usr/bin/ditto',str(source),str(app)],check=True)
  libraries={};hashes={};assemblies={}
  for filename in LIBRARIES+ASSEMBLIES:
   originals=list(source.rglob(filename));copies=[app/p.relative_to(source) for p in originals]
   if not originals or any(digest(a)!=digest(b) for a,b in zip(originals,copies)):raise RuntimeError('payload changed: '+filename)
   if filename in LIBRARIES:libraries[filename]=copies;hashes[filename]=[digest(p) for p in copies]
   else:
    if len(originals)!=1:raise RuntimeError('ambiguous managed assembly: '+filename)
    assemblies[filename]=digest(copies[0])
  client_source=Path(__file__).with_name('window-ax-action-client.mm');binary=work/'client'
  subprocess.run(['xcrun','clang++','-std=c++17','-fobjc-arc',str(client_source),'-framework','Foundation','-framework','ApplicationServices','-o',str(binary)],check=True)
  try:
   for index in cases:
    receipt=run_case(work,app,binary,index,libraries,args.image_directory);receipts.append(receipt)
    print(json.dumps({'case':index,'passed':receipt['passed'],'AXchecks':len(receipt['checks']),'SDKscenarios':len(receipt['final'].get('scenarios',[])),'failures':receipt['failures']},ensure_ascii=False),flush=True)
  finally:stop_owned_app(app)
  result={'cases':receipts,'passed':sum(x['passed'] for x in receipts),'total':len(cases),'externalAX':True,'physicalInput':False,'desktopScreenshot':False,'nativeSHA256':hashes,'managedSHA256':assemblies,'clientSHA256':digest(binary),'clientSourceSHA256':digest(client_source),'temporaryRoot':str(work)}
 result['temporaryRootRemoved']=not Path(result['temporaryRoot']).exists();args.results_file.write_text(json.dumps(result,ensure_ascii=False))
 print(json.dumps({k:result[k] for k in ['passed','total','externalAX','physicalInput','temporaryRootRemoved']}),flush=True)
 return 0 if result['passed']==result['total'] else 1
if __name__=='__main__':raise SystemExit(main())
