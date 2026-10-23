#!/usr/bin/env python3
"""Own-AppKit popup placement with controlled owner DPI, checked by independent AX.
Does not synthesize system input, install screens, or qualify physical mixed DPI.
"""
from pathlib import Path
import argparse,hashlib,json,selectors,shutil,subprocess,tempfile,time
LIBRARIES=tuple('libjalium.native.'+name+'.dylib' for name in ('core','text','metal','software','platform','media.core','media','browser'))
ASSEMBLIES=('Jalium.UI.MacOS.HostSmoke.dll','Jalium.UI.Managed.dll','Jalium.UI.MacOS.dll','Jalium.UI.Xaml.dll')
def digest(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def run_case(work,app,binary,index,libraries,images):
 output=work/('case-'+str(index));output.mkdir();commands=[];checks=[];failures=[];groups=[];focusObservations=[];launcher=client=None;pid=None;initial={}
 def state():
  try:return json.loads((output/'state.json').read_text())
  except (FileNotFoundError,json.JSONDecodeError):return {}
 def require(ok,message):
  if not ok:raise RuntimeError(message)
  checks.append(message)
 def wait(predicate,message,seconds=5):
  end=time.monotonic()+seconds
  while time.monotonic()<end:
   value=predicate()
   if value:return value
   time.sleep(.02)
  raise RuntimeError(message+'; state='+json.dumps(state(),ensure_ascii=False))
 def call(action,**kwargs):
  request={'action':action,**kwargs};client.stdin.write(json.dumps(request)+'\n');client.stdin.flush();poll=selectors.DefaultSelector();poll.register(client.stdout,selectors.EVENT_READ);ready=poll.select(6);poll.close()
  require(bool(ready),'AX reply received');line=client.stdout.readline();require(bool(line),'AX client remains live');response=json.loads(line);commands.append({'request':request,'response':response})
  require(response.get('trusted') and response['clientPID']!=pid and response['targetPID']==pid,'AX permission and separate process identities match');return response
 def discover():return {n['id']:n for n in call('discover').get('nodes',[]) if isinstance(n.get('id'),str)}
 def press(identifier):
  response=call('press',id=identifier);require(response.get('actionError')==0,'current button accepted '+identifier)
 def attempt(name,action,local):
  try:action()
  except Exception as error:local.append(name+': '+str(error));failures.append(name+': '+str(error))
 def check_editor(token):
  def matching_geometry():
   response=call('read',token=token);node=response.get('node',{});s=state();point=node.get('position') or {}
   require(node.get('id')=='boundary-editor' and node.get('value')=='保留中文🙂 Miii','native editor identity and Unicode text remain current')
   matches=abs(point.get('x',1e9)-s['expectedAXEditorX'])<=2 and abs(point.get('y',1e9)-s['expectedAXEditorY'])<=2
   commands[-1]['nativeSurfaceComparison']={'nativeX':s.get('nativeX'),'nativeY':s.get('nativeY'),'expectedEditorX':s['expectedAXEditorX'],'expectedEditorY':s['expectedAXEditorY'],'matches':matches}
   return matches
  # IPC and the host's timer snapshot are separate observations during layout.
  # Keep the same operation and existing five-second observation limit.
  wait(matching_geometry,'AX geometry settles to the current native text surface');require(True,'independent AX editor position agrees with the current native text surface')
 def focus_and_select(token):
  require(call('focus',token=token).get('actionError')==0,'native editor accepts focus');require(call('set_selection',token=token,start=1,length=2).get('actionError')==0,'native text selection remains writable')
  wait(lambda:state().get('selectionStart')==1 and state().get('selectionLength')==2,'public selection retained after native request')
  def read_focused_selection():
   node=call('read',token=token)['node'];s=state()
   if node.get('selectedText')!='留中':return None
   if s.get('appActive') and s.get('frontmostPID')==pid:
    return {'available':True,'node':node,'nativeState':s} if node.get('focused') else None
   if s.get('appActive') is False and s.get('frontmostPID')!=pid and node.get('focused') is False:
    return {'available':False,'node':node,'nativeState':s}
   return None
  observation=wait(read_focused_selection,'AX focus and selection reach the current editor');focusObservations.append({'available':observation['available'],'focused':observation['node'].get('focused'),'nativeState':{k:observation['nativeState'].get(k) for k in ['appActive','frontmostPID','processId','ownerNativeKey','popupNativeKey','nativeKeyWindow','popupResponder','popupViewHandle']}})
  require(True,'AX focus and selected Chinese text match the current editor' if observation['available'] else 'AX correctly reports inactive content; foreground acceptance unavailable')
 started=time.monotonic()
 try:
  launcher=subprocess.Popen(['/usr/bin/open','-n','-W','--env','JALIUM_PLACEMENT_OUTPUT='+str(output),'--env','TMPDIR='+str(work/'tmp'),'--env','DYLD_PRINT_LIBRARIES=1','--stdout',str(output/'app.log'),'--stderr',str(output/'app.err'),str(app),'--args','--window-placement-boundary-case='+str(index)],stdout=(output/'launch.log').open('w'),stderr=subprocess.STDOUT)
  initial=wait(lambda:state() if state().get('appRunning') and state().get('dataKind')==0 and not state().get('running') else None,'placement host ready',20);pid=initial['processId']
  client=subprocess.Popen([str(binary),str(pid)],stdin=subprocess.PIPE,stdout=subprocess.PIPE,text=True,bufsize=1,stderr=(output/'client.err').open('w'))
  for kind in range(3):
   start=len(checks);local=[]
   try:
    wait(lambda:state().get('dataKind')==kind and not state().get('running'),'data group ready');nodes=discover();require(all(x in nodes for x in ['boundary-editor','boundary-move','boundary-reopen','boundary-next']),'actual AX windows expose editor and owner actions');old=nodes['boundary-editor']['token']
    attempt('first editor geometry',lambda:check_editor(old),local);attempt('first editor focus',lambda:focus_and_select(old),local)
    phase=state()['phase'];press('boundary-move');wait(lambda:state()['phase']>phase,'anchor move completes');attempt('moved editor geometry',lambda:check_editor(old),local)
    phase=state()['phase'];press('boundary-reopen');wait(lambda:state()['phase']>phase,'popup reopens');nodes=discover();current=nodes['boundary-editor']['token'];require(current!=old,'reopened editor has a new native AX identity')
    rejected=call('focus',token=old,diagnostics=True);require(rejected.get('actionError') not in (None,0) and rejected.get('requestElapsedSeconds',99)<.25,'deliberately retired focus request is promptly rejected')
    attempt('reopened editor geometry',lambda:check_editor(current),local);attempt('reopened editor focus',lambda:focus_and_select(current),local)
    count=state()['captures'];press('boundary-capture');wait(lambda:state()['captures']>count,'owner and popup backend images captured')
   except Exception as error:local.append(str(error));failures.append('kind='+str(kind)+' '+str(error))
   groups.append({'kind':kind,'passed':not local,'checks':checks[start:],'failures':local,'state':state()})
   if kind<2:
    try:discover();press('boundary-next');wait(lambda:state().get('dataKind')==kind+1 and not state().get('running'),'next data shape completes',12)
    except Exception as error:failures.append('advance '+str(error));break
 except Exception as error:failures.append(str(error))
 finally:
  if client is not None:
   try:call('quit');client.wait(timeout=3)
   except Exception:client.terminate();client.wait(timeout=3)
  if launcher is not None and launcher.poll() is None:
   (output/'finish.signal').touch()
   try:launcher.wait(timeout=8)
   except subprocess.TimeoutExpired:
    (output/'observation-pending.json').write_text(json.dumps({'launcherPID':launcher.pid,'hostPID':pid,'app':str(app),'reason':'No terminal response observed; awaiting this same launched operation'}));print('OBSERVATION pending, same launcher PID '+str(launcher.pid),flush=True);launcher.wait();failures.append('host did not finish within initial shutdown observation')
  final=state();log=(output/'app.err').read_text(errors='replace') if (output/'app.err').exists() else '';loaded=[]
  for name,paths in libraries.items():
   if any(str(p) in log for p in paths):loaded.append(name)
   else:failures.append('loaded native payload not proven '+name)
  if final.get('failures'):failures.extend('SDK '+x for x in final['failures'])
  if not final.get('closed') or len(final.get('scenarios',[]))!=21:failures.append('complete terminal 21-scenario SDK evidence unavailable')
  receipts=[]
  if images:
   target=images/('case-'+str(index));target.mkdir(parents=True,exist_ok=True)
   for p in output.glob('*.png'):shutil.copyfile(p,target/p.name);receipts.append({'path':str(target/p.name),'sha256':digest(p)})
  result={'index':index,'passed':not failures and all(x['available'] for x in focusObservations),'geometryAndSelectionPassed':not failures,'focusObservations':focusObservations,'foregroundFocusPassed':sum(x['available'] for x in focusObservations),'foregroundFocusUnavailable':sum(not x['available'] for x in focusObservations),'checks':checks,'failures':failures,'groups':groups,'initial':initial,'final':final,'commands':commands,'hostPID':pid,'clientPID':None if client is None else client.pid,'loadedNative':loaded,'images':receipts,'externalAX':True,'ownNSViewSDK':True,'controlledOwnerDPI':True,'physicalMixedDPI':False,'physicalInput':False,'desktopScreenshot':False,'seconds':round(time.monotonic()-started,3),'failureLogTails':{name:(output/name).read_text(errors='replace')[-4000:] for name in ['progress.log','app.log','app.err','launch.log'] if (output/name).exists()}};(output/'receipt.json').write_text(json.dumps(result,ensure_ascii=False))
 return result

def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--app',type=Path,required=True);parser.add_argument('--work-parent',type=Path,required=True);parser.add_argument('--results-file',type=Path,required=True);parser.add_argument('--image-directory',type=Path);parser.add_argument('--cases',default=','.join(map(str,range(12))));args=parser.parse_args();source=args.app.resolve();cases=list(map(int,args.cases.split(',')))
 if not source.is_dir() or source.suffix!='.app' or not cases or len(cases)!=len(set(cases)) or any(i not in range(12) for i in cases):parser.error('invalid app or case matrix')
 receipts=[]
 with tempfile.TemporaryDirectory(prefix='jalium-placement-boundary-',dir=args.work_parent) as name:
  work=Path(name);(work/'tmp').mkdir();app=work/'HostSmoke.app';subprocess.run(['/usr/bin/ditto',str(source),str(app)],check=True);libraries={};hashes={};assemblies={}
  for filename in LIBRARIES+ASSEMBLIES:
   originals=list(source.rglob(filename));copies=[app/p.relative_to(source) for p in originals]
   if not originals or any(digest(a)!=digest(b) for a,b in zip(originals,copies)):raise RuntimeError('payload changed '+filename)
   if filename in LIBRARIES:libraries[filename]=copies;hashes[filename]=[digest(p) for p in copies]
   else:
    if len(copies)!=1:raise RuntimeError('ambiguous assembly '+filename)
    assemblies[filename]=digest(copies[0])
  client_source=Path(__file__).with_name('window-ax-action-client.mm');binary=work/'client';subprocess.run(['xcrun','clang++','-std=c++17','-fobjc-arc',str(client_source),'-framework','Foundation','-framework','ApplicationServices','-o',str(binary)],check=True)
  for index in cases:
   item=run_case(work,app,binary,index,libraries,args.image_directory);receipts.append(item);print(json.dumps({'case':index,'passed':item['passed'],'geometryAndSelectionPassed':item['geometryAndSelectionPassed'],'foregroundFocusUnavailable':item['foregroundFocusUnavailable'],'AXchecks':len(item['checks']),'SDKscenarios':len(item['final'].get('scenarios',[])),'failures':item['failures']},ensure_ascii=False),flush=True)
  result={'passed':sum(x['passed'] for x in receipts),'geometryAndSelectionPassed':sum(x['geometryAndSelectionPassed'] for x in receipts),'foregroundFocusPassed':sum(x['foregroundFocusPassed'] for x in receipts),'foregroundFocusUnavailable':sum(x['foregroundFocusUnavailable'] for x in receipts),'total':len(cases),'cases':receipts,'nativeSHA256':hashes,'managedSHA256':assemblies,'clientSHA256':digest(binary),'clientSourceSHA256':digest(client_source),'temporaryRoot':str(work),'externalAX':True,'controlledOwnerDPI':True,'physicalMixedDPI':False,'physicalInput':False,'desktopScreenshot':False}
 result['temporaryRootRemoved']=not Path(result['temporaryRoot']).exists();args.results_file.write_text(json.dumps(result,ensure_ascii=False));print(json.dumps({k:result[k] for k in ['passed','geometryAndSelectionPassed','foregroundFocusPassed','foregroundFocusUnavailable','total','externalAX','controlledOwnerDPI','physicalMixedDPI','temporaryRootRemoved']}),flush=True);return 1 if result['geometryAndSelectionPassed']!=result['total'] else 3 if result['foregroundFocusUnavailable'] else 0
if __name__=='__main__':raise SystemExit(main())
