#!/usr/bin/env python3
"""Validate actual selection transactions with independent native AX.
Own NSView SDK events and backend readbacks are separate from physical input.
Exit3 records unavailable foreground checks without counting them as passed.
"""
from pathlib import Path
import argparse,hashlib,json,selectors,shutil,subprocess,tempfile,time
LIBRARIES=tuple('libjalium.native.'+n+'.dylib' for n in ('core','text','metal','software','platform','media.core','media','browser'))
ASSEMBLIES=('Jalium.UI.MacOS.HostSmoke.dll','Jalium.UI.Managed.dll','Jalium.UI.MacOS.dll','Jalium.UI.Xaml.dll')
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def run_case(work,app,binary,index,libraries,images):
 output=work/('case-'+str(index));output.mkdir();commands=[];checks=[];failures=[];groups=[];focus=[];launcher=client=None;pid=None;initial={};retired=[]
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
  require(bool(ready),'AX reply received');line=client.stdout.readline();require(bool(line),'AX client remains live');response=json.loads(line);commands.append({'request':request,'response':response});require(response.get('trusted') and response['clientPID']!=pid and response['targetPID']==pid,'AX permission and separate process identities match');return response
 def discover():return {n['id']:n for n in call('discover').get('nodes',[]) if isinstance(n.get('id'),str)}
 def press(identifier):require(call('press',id=identifier).get('actionError')==0,'current AX action accepted '+identifier)
 def action(identifier):
  phase=state()['phase'];press(identifier);wait(lambda:state().get('phase',0)>phase and not state().get('running'),'queued target action completes '+identifier)
 def attempt(name,callback,local):
  try:callback()
  except Exception as error:local.append(name+': '+str(error));failures.append(name+': '+str(error))
 def selection(root,expected):
  def matches():
   node=call('read',token=root).get('node',{});s=state();tokens=[x['token'] for x in node.get('AXSelectedChildren',[])];require(s.get('selectedCount')==len(expected),'completed target action publishes the requested model count');return tokens==expected
  wait(matches,'native selected children agree with independently requested row identities');require(True,'native selected children and model count agree')
 def focus_editor(token):
  require(call('focus',token=token).get('actionError')==0,'current editor accepts native focus request');require(call('set_selection',token=token,start=1,length=2).get('actionError')==0,'current editor accepts native text selection')
  def read():
   node=call('read',token=token).get('node',{});s=state()
   if node.get('value')!='保留中文🙂 Miii' or node.get('selectedText')!='留中' or s.get('selectionStart')!=1 or s.get('selectionLength')!=2:return None
   if s.get('appActive') and s.get('frontmostPID')==pid:return (True,s) if node.get('focused') else None
   if s.get('appActive') is False and s.get('frontmostPID')!=pid and node.get('focused') is False:return False,s
   return None
  available,s=wait(read,'native editor focus and selected text settle');focus.append({'available':available,'appActive':s['appActive'],'frontmostPID':s['frontmostPID'],'targetPID':pid});require(True,'foreground editor focus and Unicode selection read back' if available else 'inactive AX focus is false; foreground acceptance unavailable')
 started=time.monotonic()
 try:
  launcher=subprocess.Popen(['/usr/bin/open','-n','-W','--env','JALIUM_SELECTION_TRANSACTION_OUTPUT='+str(output),'--env','TMPDIR='+str(work/'tmp'),'--env','DYLD_PRINT_LIBRARIES=1','--stdout',str(output/'app.log'),'--stderr',str(output/'app.err'),str(app),'--args','--window-selection-transaction-case='+str(index)],stdout=(output/'launch.log').open('w'),stderr=subprocess.STDOUT)
  initial=wait(lambda:state() if state().get('appRunning') and state().get('dataKind')==0 and not state().get('running') else None,'transaction host ready',20);pid=initial['processId'];client=subprocess.Popen([str(binary),str(pid)],stdin=subprocess.PIPE,stdout=subprocess.PIPE,text=True,bufsize=1,stderr=(output/'client.err').open('w'))
  for kind in range(3):
   start=len(checks);local=[]
   try:
    wait(lambda:state().get('dataKind')==kind and not state().get('running'),'data shape ready');nodes=discover();require(all(n in nodes for n in ['tx-list','tx-row-0','tx-row-1','tx-editor','tx-next']),'actual AX list exposes both repeated rows, editor and actions');root=nodes['tx-list']['token'];first=nodes['tx-row-0']['token'];second=nodes['tx-row-1']['token'];require(first!=second and not any(t in (first,second) for t in retired),'new view has distinct current native row identities')
    attempt('first native selection',lambda:selection(root,[first]),local);attempt('first editor focus',lambda:focus_editor(nodes['tx-editor']['token']),local)
    action('tx-add');attempt('second native occurrence',lambda:selection(root,[first,second]),local)
    action('tx-insert');attempt('inserted native occurrence',lambda:selection(root,[first,second]),local)
    for identifier in ['tx-reentry','tx-duplicate','tx-getter','tx-throw']:action(identifier)
    attempt('retained native selection',lambda:selection(root,[first,second]),local)
    node=call('read',token=nodes['tx-combo']['token']).get('node',{});attempt('native combo value',lambda:require(node.get('value')=='项目 3 · 中文🙂','native combo value is the final requested row'),local)
    attempt('current editor after mutations',lambda:focus_editor(nodes['tx-editor']['token']),local);captures=state()['captures'];action('tx-capture');wait(lambda:state()['captures']>captures,'current backend readbacks captured')
    retired.extend([first,second])
   except Exception as error:local.append(str(error));failures.append('kind='+str(kind)+' '+str(error))
   groups.append({'kind':kind,'passed':not local,'checks':checks[start:],'failures':local,'state':state()})
   if kind<2:
    try:
     press('tx-next');wait(lambda:state().get('dataKind')==kind+1 and not state().get('running'),'next data shape completes',12)
     rejected=call('press',token=second,diagnostics=True);require(rejected.get('actionError') not in (None,0) and rejected.get('requestElapsedSeconds',99)<.25,'intentional retired-row action is promptly rejected')
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
    (output/'observation-pending.json').write_text(json.dumps({'launcherPID':launcher.pid,'hostPID':pid,'reason':'Awaiting this same launched process after initial shutdown observation'}));print('OBSERVATION pending same launcher '+str(launcher.pid),flush=True);launcher.wait();failures.append('host exceeded initial shutdown observation')
  final=state();log=(output/'app.err').read_text(errors='replace') if (output/'app.err').exists() else '';loaded=[]
  for name,paths in libraries.items():
   if any(str(p) in log for p in paths):loaded.append(name)
   else:failures.append('native payload load unproven '+name)
  if final.get('failures'):failures.extend('SDK '+x for x in final['failures'])
  if not final.get('closed') or len(final.get('scenarios',[]))!=30:failures.append('complete terminal30-scenario SDK evidence unavailable')
  receipts=[]
  if images:
   target=images/('case-'+str(index));target.mkdir(parents=True,exist_ok=True)
   for p in output.glob('*.png'):shutil.copyfile(p,target/p.name);receipts.append({'path':str(target/p.name),'sha256':digest(p)})
  result={'index':index,'passed':not failures and all(x['available'] for x in focus),'qualifiedWithoutForeground':not failures,'checks':checks,'failures':failures,'groups':groups,'initial':initial,'final':final,'commands':commands,'focusObservations':focus,'foregroundFocusPassed':sum(x['available'] for x in focus),'foregroundFocusUnavailable':sum(not x['available'] for x in focus),'hostPID':pid,'clientPID':None if client is None else client.pid,'loadedNative':loaded,'images':receipts,'externalAX':True,'ownNSViewSDK':True,'physicalInput':False,'desktopScreenshot':False,'seconds':round(time.monotonic()-started,3),'failureLogTails':{name:(output/name).read_text(errors='replace')[-4000:] for name in ['progress.log','app.log','app.err','launch.log'] if (output/name).exists()}};(output/'receipt.json').write_text(json.dumps(result,ensure_ascii=False))
 return result
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--app',type=Path,required=True);parser.add_argument('--work-parent',type=Path,required=True);parser.add_argument('--results-file',type=Path,required=True);parser.add_argument('--image-directory',type=Path);parser.add_argument('--cases',default=','.join(map(str,range(12))));args=parser.parse_args();source=args.app.resolve();cases=list(map(int,args.cases.split(',')))
 if not source.is_dir() or source.suffix!='.app' or not cases or len(cases)!=len(set(cases)) or any(i not in range(12) for i in cases):parser.error('invalid app or case matrix')
 receipts=[]
 with tempfile.TemporaryDirectory(prefix='jalium-selection-transactions-',dir=args.work_parent) as name:
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
   item=run_case(work,app,binary,index,libraries,args.image_directory);receipts.append(item);print(json.dumps({'case':index,'passed':item['passed'],'qualifiedWithoutForeground':item['qualifiedWithoutForeground'],'foregroundFocusUnavailable':item['foregroundFocusUnavailable'],'AXchecks':len(item['checks']),'SDKscenarios':len(item['final'].get('scenarios',[])),'failures':[x.split('; state=')[0][:250] for x in item['failures']]},ensure_ascii=False),flush=True)
  result={'passed':sum(x['passed'] for x in receipts),'qualifiedWithoutForeground':sum(x['qualifiedWithoutForeground'] for x in receipts),'foregroundFocusPassed':sum(x['foregroundFocusPassed'] for x in receipts),'foregroundFocusUnavailable':sum(x['foregroundFocusUnavailable'] for x in receipts),'total':len(cases),'cases':receipts,'nativeSHA256':hashes,'managedSHA256':assemblies,'clientSHA256':digest(binary),'clientSourceSHA256':digest(client_source),'temporaryRoot':str(work),'externalAX':True,'ownNSViewSDK':True,'physicalInput':False,'desktopScreenshot':False}
 result['temporaryRootRemoved']=not Path(result['temporaryRoot']).exists();args.results_file.write_text(json.dumps(result,ensure_ascii=False));print(json.dumps({k:result[k] for k in ['passed','qualifiedWithoutForeground','foregroundFocusPassed','foregroundFocusUnavailable','total','temporaryRootRemoved']}),flush=True);return 1 if result['qualifiedWithoutForeground']!=result['total'] else 3 if result['foregroundFocusUnavailable'] else 0
if __name__=='__main__':raise SystemExit(main())
