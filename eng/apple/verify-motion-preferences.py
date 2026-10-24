#!/usr/bin/env python3
"""Observe application-only macOS motion preferences through real AppKit notifications.

Only the launched test process overrides its NSWorkspace getter. No global system
setting or physical input is changed. Exit 3 retains unavailable foreground checks.
"""
from pathlib import Path
import argparse, hashlib, json, shutil, subprocess, tempfile, time

LIBRARIES=tuple('libjalium.native.'+n+'.dylib' for n in ('core','text','metal','software','platform','media.core','media','browser'))
ASSEMBLIES=('Jalium.UI.MacOS.HostSmoke.dll','Jalium.UI.Managed.dll','Jalium.UI.MacOS.dll','Jalium.UI.Xaml.dll')
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app',type=Path,required=True)
    parser.add_argument('--work-parent',type=Path,required=True)
    parser.add_argument('--results-file',type=Path,required=True)
    parser.add_argument('--image-directory',type=Path)
    parser.add_argument('--cases',default=','.join(map(str,range(16))))
    args=parser.parse_args();source=args.app.resolve();cases=list(map(int,args.cases.split(',')))
    if not source.is_dir() or source.suffix!='.app' or not cases or len(cases)!=len(set(cases)) or any(i not in range(16) for i in cases):parser.error('invalid app or cases')
    checkout=Path(__file__).resolve().parents[2]
    helper_source=checkout/'tests/Jalium.UI.MacOS.HostSmoke/MotionPreferenceOverride.mm'
    receipts=[]
    with tempfile.TemporaryDirectory(prefix='jalium-motion-preferences-',dir=args.work_parent) as name:
        work=Path(name);(work/'tmp').mkdir();app=work/'HostSmoke.app'
        subprocess.run(['/usr/bin/ditto',str(source),str(app)],check=True)
        helper=work/'libjalium.window-motion-test.dylib'
        command=['/usr/bin/xcrun','clang++','-std=c++17','-fobjc-arc','-dynamiclib','-arch','arm64','-mmacosx-version-min=15.0',str(helper_source),'-framework','AppKit','-framework','Foundation','-o',str(helper)]
        built=subprocess.run(command,capture_output=True,text=True)
        if built.returncode:raise RuntimeError('SDK helper compile failed: '+built.stderr)
        subprocess.run(['/usr/bin/codesign','--force','--sign','-',str(helper)],capture_output=True,check=True)
        helper_hash=digest(helper)
        libraries={};native_hashes={};assemblies={}
        for filename in LIBRARIES+ASSEMBLIES:
            originals=list(source.rglob(filename));copies=[app/p.relative_to(source) for p in originals]
            if not originals or any(digest(a)!=digest(b) for a,b in zip(originals,copies)):raise RuntimeError('payload changed: '+filename)
            if filename in LIBRARIES:libraries[filename]=copies;native_hashes[filename]=[digest(p) for p in copies]
            else:
                if len(copies)!=1:raise RuntimeError('ambiguous assembly: '+filename)
                assemblies[filename]=digest(copies[0])
        for index in cases:
            output=work/('case-'+str(index));output.mkdir();started=time.monotonic()
            command=['/usr/bin/open','-n','-W','--env','JALIUM_APPEARANCE_OUTPUT='+str(output),'--env','JALIUM_MOTION_TEST_LIBRARY='+str(helper),
                     '--env','TMPDIR='+str(work/'tmp'),'--env','DYLD_PRINT_LIBRARIES=1','--stdout',str(output/'app.log'),'--stderr',str(output/'app.err'),
                     str(app),'--args','--window-appearance-case='+str(index+8)]
            with (output/'launch.log').open('w') as log:
                process=subprocess.Popen(command,stdout=log,stderr=subprocess.STDOUT)
                (work/'active-process.json').write_text(json.dumps({'launcherPID':process.pid,'case':index,'app':str(app)}))
                try:code=process.wait(timeout=90)
                except subprocess.TimeoutExpired:
                    print('OBSERVATION same launched process pending '+str(process.pid),flush=True)
                    (output/'observation-pending.json').write_text(json.dumps({'launcherPID':process.pid,'reason':'Continue observing this same launched process; no restart or termination.'}))
                    code=process.wait()
            host=json.loads((output/'result.json').read_text()) if (output/'result.json').exists() else None
            dyld=(output/'app.err').read_text(errors='replace') if (output/'app.err').exists() else ''
            loaded=[f for f,paths in libraries.items() if any(str(p) in dyld for p in paths)]
            failures=[]
            if code:failures.append('launcher exit '+str(code))
            if host is None:failures.append('missing terminal host result')
            else:failures.extend(host.get('Failures',[]))
            failures.extend('unproven native payload '+f for f in LIBRARIES if f not in loaded)
            if str(helper) not in dyld:failures.append('process-local SDK helper load unproven')
            images=[]
            if args.image_directory:
                target=args.image_directory/('case-'+str(index));target.mkdir(parents=True,exist_ok=False)
                for picture in sorted(output.glob('*.png')):
                    p=target/picture.name;shutil.copyfile(picture,p);images.append({'path':str(p),'sha256':digest(p)})
            unavailable=host.get('ForegroundUnavailable',0) if host else 0
            qualified=not failures and bool(host and host['qualifiedWithoutForeground'])
            receipt={'index':index,'passed':qualified and unavailable==0,'qualifiedWithoutForeground':qualified,'foregroundUnavailable':unavailable,'failures':failures,
                     'hostResult':host,'loadedNative':loaded,'launcherExitCode':code,'seconds':round(time.monotonic()-started,3),'images':images,
                     'SDKHelperLoaded':str(helper) in dyld,'dyldLogSHA256':hashlib.sha256(dyld.encode()).hexdigest(),
                     'failureLogTails':{n:(output/n).read_text(errors='replace')[-4000:] for n in ['app.log','app.err','launch.log'] if (output/n).exists()}}
            receipts.append(receipt)
            print(json.dumps({'case':index,'passed':receipt['passed'],'qualifiedWithoutForeground':qualified,'scenarios':len(host.get('Scenarios',[])) if host else 0,
                              'foregroundUnavailable':unavailable,'failures':[f.split('\n')[0] for f in failures]},ensure_ascii=False),flush=True)
        assert digest(helper)==helper_hash
    result={'passed':sum(x['passed'] for x in receipts),'qualifiedWithoutForeground':sum(x['qualifiedWithoutForeground'] for x in receipts),'foregroundUnavailable':sum(x['foregroundUnavailable'] for x in receipts),
            'total':len(cases),'cases':receipts,'nativeSHA256':native_hashes,'managedSHA256':assemblies,'helperSourceSHA256':digest(helper_source),'helperSHA256':helper_hash,
            'appKitPreferenceOverride':True,'nativeWorkspaceNotifications':True,'systemPreferencesWritten':False,'physicalInput':False,'desktopScreenshot':False,'temporaryRootRemoved':not work.exists()}
    args.results_file.write_text(json.dumps(result,ensure_ascii=False))
    print(json.dumps({k:result[k] for k in ['passed','qualifiedWithoutForeground','foregroundUnavailable','total','temporaryRootRemoved']}),flush=True)
    return 0 if result['passed']==len(cases) else 3 if result['qualifiedWithoutForeground']==len(cases) else 1

if __name__=='__main__':raise SystemExit(main())
