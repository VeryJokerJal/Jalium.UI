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
class NativeInteractionUnavailable(RuntimeError):pass
def run_case(work,app,binary,index,libraries,images):
 output=work/('case-'+str(index));output.mkdir();commands=[];checks=[];failures=[];groups=[];focus=[];launcher=client=None;pid=None;initial={};retired=[];native_combo_paths={};native_combo_observations=[];native_unavailable=[];native_discovery_observations=[];surface_discovery_observations=[]
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
 def surface_nodes():
  started=time.monotonic()
  def current():
   nodes=discover();ready=all(n in nodes for n in ['tx-list','tx-row-0','tx-row-1','tx-editor','tx-next']);snapshot=state();surface_discovery_observations.append({'ready':ready,'elapsedSeconds':time.monotonic()-started,'identifiers':sorted(nodes),'processId':snapshot.get('processId'),'appActive':snapshot.get('appActive'),'frontmostPID':snapshot.get('frontmostPID')});return nodes if ready else None
  return wait(current,'same-process AX surface exposes both repeated rows, editor and actions')
 def press(identifier):require(call('press',id=identifier).get('actionError')==0,'current AX action accepted '+identifier)
 def action(identifier):
  phase=state()['phase'];press(identifier);wait(lambda:state().get('phase',0)>phase and not state().get('running'),'queued target action completes '+identifier)
 def attempt(name,callback,local):
  try:callback()
  except NativeInteractionUnavailable as error:native_unavailable.append({'step':name,'reason':str(error),'state':{k:state().get(k) for k in ['dataKind','repeatOpen','appActive','frontmostPID','processId']}})
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
 def combo_eligibility():
  snapshot=state()
  if snapshot.get('repeatOpen') is False and snapshot.get('appActive') is False and snapshot.get('frontmostPID')!=pid:
   raise NativeInteractionUnavailable('the popup was dismissed while another application was foreground; this native interaction is unverified')
 def repeat_rows():
  def current_nodes():
   combo_eligibility();allnodes=call('discover').get('nodes',[]);identifiers={n.get('id') for n in allnodes};ready=all('tx-repeat-row-'+str(i) in identifiers for i in range(3));native_discovery_observations.append({'ready':ready,'repeatOpen':state().get('repeatOpen'),'appActive':state().get('appActive'),'frontmostPID':state().get('frontmostPID')});return allnodes if ready else None
  allnodes=wait(current_nodes,'current native combo rows settle after the same open operation');roots=[n for n in allnodes if n.get('id')=='tx-repeat-combo'];require(len(roots)==1,'current repeated combo has one native root');root=roots[0]['token'];by={n['token']:n for n in allnodes};direct=[n for n in allnodes if n.get('parent',{}).get('token')==root]
  rows=[next((n for n in allnodes if n.get('id')=='tx-repeat-row-'+str(i)),{}) for i in range(3)]
  require(all(rows) and len({n['token'] for n in rows})==3,'current native combo has three distinct ordered row identifiers')
  def top(token):
   seen=set()
   while token in by and token not in seen:
    seen.add(token);node=by[token]
    if node.get('role')=='AXWindow':return token
    token=node.get('parent',{}).get('token')
   return None
  same=all(n.get('parent',{}).get('token')==root for n in rows)
  if same:require(len(direct)==3,'same-window combo exposes the three row children')
  else:
   require(not direct and len({n.get('parent',{}).get('token') for n in rows})==1,'separate-window combo rows share their actual physical parent')
   require(top(rows[0]['token']) is not None and top(rows[0]['token'])!=top(root),'popup rows belong to another actual AX window')
  native_combo_paths[root]={'mode':'combo-children' if same else 'separate-window-rows','rootWindow':top(root),'rowWindow':top(rows[0]['token']),'rowParent':rows[0].get('parent',{}).get('token')}
  for i,node in enumerate(rows):require(node.get('name')=='第 '+str(i)+' 行 · 重复条目🙂 Miii','native row name describes its own realized occurrence')
  return root,[n['token'] for n in rows]
 def repeat_selection(root,rows,index):
  expected=[i==index for i in range(3)]
  def matches():
   combo_eligibility();nodes=[call('read',token=token).get('node',{}) for token in rows];node=call('read',token=root).get('node',{});snapshot=state();path=native_combo_paths[root]
   require(snapshot.get('repeatIndex')==index,'completed native combo operation publishes the independently requested control index')
   selected=[n['token'] for n in node.get('AXSelectedChildren',[])];native=[n.get('selected') for n in nodes]
   native_combo_observations.append({'expectedIndex':index,'controlIndex':snapshot.get('repeatIndex'),'providerFlags':snapshot.get('repeatSelected'),'nativeFlags':native,'nativeSelectedChildren':selected,'nativeValue':node.get('value'),'rowTokens':rows,'root':root,**path})
   expected_children=[] if index<0 or path['mode']=='separate-window-rows' else [rows[index]]
   return native==expected and selected==expected_children and node.get('value')==('' if index<0 else '重复条目🙂 Miii')
  wait(matches,'native combo row flags, physical-child relationship and value agree');require(True,'native combo selection agrees with independently requested row identity')
 def write_repeat(root,rows,row,selected,index):
  response=call('set_selected',token=rows[row],selected=selected,diagnostics=True);require(response.get('actionError')==0,'current native combo row accepts selected attribute write')
  after=response['diagnosticsAfter']['unixMicros'];wait(lambda:state().get('recordedUnixMicros',0)>after,'target snapshot follows completed native selected write');repeat_selection(root,rows,index)
 def repeat_flow(local):
  action('tx-repeat-open');action('tx-repeat-reset');root,rows=repeat_rows();attempt('native repeated combo initial selection',lambda:repeat_selection(root,rows,1),local)
  for row,selected,index in [(1,True,1),(0,False,1),(1,False,-1),(0,True,0)]:
   action('tx-repeat-reset');root,rows=repeat_rows();attempt('native combo selected write '+str(row)+' '+str(selected),lambda row=row,selected=selected,index=index:write_repeat(root,rows,row,selected,index),local)
  action('tx-repeat-reset');root,rows=repeat_rows();old=rows;action('tx-repeat-rebuild');response=call('set_selected',token=old[1],selected=True,diagnostics=True,expectedRetired=True);require(response.get('actionError') not in (None,0) and response.get('requestElapsedSeconds',99)<.25,'intentional retired combo row is promptly rejected')
  root,rows=repeat_rows();require(not set(rows).intersection(old),'replacement native combo rows do not reuse retired row identities');attempt('native replacement combo selection',lambda:repeat_selection(root,rows,1),local);attempt('native replacement combo selected write',lambda:write_repeat(root,rows,1,True,1),local)
 started=time.monotonic()
 try:
  launcher=subprocess.Popen(['/usr/bin/open','-n','-W','--env','JALIUM_SELECTION_TRANSACTION_OUTPUT='+str(output),'--env','TMPDIR='+str(work/'tmp'),'--env','DYLD_PRINT_LIBRARIES=1','--stdout',str(output/'app.log'),'--stderr',str(output/'app.err'),str(app),'--args','--window-selection-transaction-case='+str(index)],stdout=(output/'launch.log').open('w'),stderr=subprocess.STDOUT)
  initial=wait(lambda:state() if state().get('appRunning') and state().get('dataKind')==0 and not state().get('running') else None,'transaction host ready',20);pid=initial['processId'];client=subprocess.Popen([str(binary),str(pid)],stdin=subprocess.PIPE,stdout=subprocess.PIPE,text=True,bufsize=1,stderr=(output/'client.err').open('w'))
  for kind in range(3):
   start=len(checks);local=[];unavailable_start=len(native_unavailable)
   try:
    wait(lambda:state().get('dataKind')==kind and not state().get('running'),'data shape ready');nodes=surface_nodes();require(True,'actual AX list exposes both repeated rows, editor and actions');root=nodes['tx-list']['token'];first=nodes['tx-row-0']['token'];second=nodes['tx-row-1']['token'];require(first!=second and not any(t in (first,second) for t in retired),'new view has distinct current native row identities')
    attempt('first native selection',lambda:selection(root,[first]),local);attempt('first editor focus',lambda:focus_editor(nodes['tx-editor']['token']),local)
    action('tx-add');attempt('second native occurrence',lambda:selection(root,[first,second]),local)
    action('tx-insert');attempt('inserted native occurrence',lambda:selection(root,[first,second]),local)
    for identifier in ['tx-reentry','tx-duplicate','tx-getter','tx-throw']:action(identifier)
    attempt('retained native selection',lambda:selection(root,[first,second]),local)
    node=call('read',token=nodes['tx-combo']['token']).get('node',{});attempt('native combo value',lambda:require(node.get('value')=='项目 3 · 中文🙂','native combo value is the final requested row'),local)
    try:repeat_flow(local)
    except NativeInteractionUnavailable as error:
     native_unavailable.append({'step':'combo flow','reason':str(error),'state':{k:state().get(k) for k in ['dataKind','repeatOpen','appActive','frontmostPID','processId']}})
     if not any(x.get('dataKind')==kind and x.get('name')=='combo-provider-view-replacement' for x in state().get('scenarios',[])):action('tx-repeat-rebuild')
    captures=state()['captures'];action('tx-capture');wait(lambda:state()['captures']>captures,'current backend readbacks captured');attempt('current editor after combo mutations',lambda:focus_editor(nodes['tx-editor']['token']),local)
    retired.extend([first,second])
   except Exception as error:local.append(str(error));failures.append('kind='+str(kind)+' '+str(error))
   groups.append({'nativeInteractionUnavailable':native_unavailable[unavailable_start:],'kind':kind,'passed':not local and len(native_unavailable)==unavailable_start,'checks':checks[start:],'failures':local,'state':state()})
   if kind<2:
    try:
     press('tx-next');wait(lambda:state().get('dataKind')==kind+1 and not state().get('running'),'next data shape completes',12)
     rejected=call('press',token=second,diagnostics=True,expectedRetired=True);require(rejected.get('actionError') not in (None,0) and rejected.get('requestElapsedSeconds',99)<.25,'intentional retired-row action is promptly rejected')
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
  if not final.get('closed') or len(final.get('scenarios',[]))!=45:failures.append('complete terminal45-scenario SDK evidence unavailable')
  receipts=[]
  if images:
   target=images/('case-'+str(index));target.mkdir(parents=True,exist_ok=True)
   for p in output.glob('*.png'):shutil.copyfile(p,target/p.name);receipts.append({'path':str(target/p.name),'sha256':digest(p)})
  result={'nativeSurfaceDiscoveryObservations':surface_discovery_observations,'nativeInteractionUnavailable':native_unavailable,'nativeDiscoveryObservations':native_discovery_observations,'nativeComboObservations':native_combo_observations,'nativeComboPaths':native_combo_paths,'index':index,'passed':not failures and not native_unavailable and all(x['available'] for x in focus),'qualifiedWithoutForeground':not failures and not native_unavailable,'sdkAndAvailableNativeAXPassed':not failures,'checks':checks,'failures':failures,'groups':groups,'initial':initial,'final':final,'commands':commands,'focusObservations':focus,'foregroundFocusPassed':sum(x['available'] for x in focus),'foregroundFocusUnavailable':sum(not x['available'] for x in focus),'hostPID':pid,'clientPID':None if client is None else client.pid,'loadedNative':loaded,'images':receipts,'externalAX':True,'ownNSViewSDK':True,'physicalInput':False,'desktopScreenshot':False,'seconds':round(time.monotonic()-started,3),'failureLogTails':{name:(output/name).read_text(errors='replace')[-4000:] for name in ['progress.log','app.log','app.err','launch.log'] if (output/name).exists()}};(output/'receipt.json').write_text(json.dumps(result,ensure_ascii=False))
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
  result={'passed':sum(x['passed'] for x in receipts),'qualifiedWithoutForeground':sum(x['qualifiedWithoutForeground'] for x in receipts),'nativeInteractionUnavailable':sum(len(x['nativeInteractionUnavailable']) for x in receipts),'sdkAndAvailableNativeAXPassed':sum(x['sdkAndAvailableNativeAXPassed'] for x in receipts),'foregroundFocusPassed':sum(x['foregroundFocusPassed'] for x in receipts),'foregroundFocusUnavailable':sum(x['foregroundFocusUnavailable'] for x in receipts),'total':len(cases),'cases':receipts,'nativeSHA256':hashes,'managedSHA256':assemblies,'clientSHA256':digest(binary),'clientSourceSHA256':digest(client_source),'temporaryRoot':str(work),'externalAX':True,'ownNSViewSDK':True,'physicalInput':False,'desktopScreenshot':False}
 result['temporaryRootRemoved']=not Path(result['temporaryRoot']).exists();args.results_file.write_text(json.dumps(result,ensure_ascii=False));print(json.dumps({k:result[k] for k in ['passed','qualifiedWithoutForeground','foregroundFocusPassed','foregroundFocusUnavailable','total','temporaryRootRemoved']}),flush=True);return 1 if result['sdkAndAvailableNativeAXPassed']!=result['total'] else 3 if result['foregroundFocusUnavailable'] or result['nativeInteractionUnavailable'] else 0
if __name__=='__main__':raise SystemExit(main())
