#!/usr/bin/env python3
"""Verify keyboard packet retirement in actual macOS nested AppKit loops.

Calls only the target's own NSView, including nested AppKit run loops. It
does not inject system input, capture the desktop or change system settings.
"""
from pathlib import Path
import argparse,hashlib,json,os,shutil,signal,struct,subprocess,tempfile,time,zlib

LIBRARIES=tuple('libjalium.native.'+n+'.dylib' for n in ('core','text','metal','software','platform','media.core','media','browser'))
ASSEMBLIES=('Jalium.UI.MacOS.HostSmoke.dll','Jalium.UI.Managed.dll','Jalium.UI.MacOS.dll','Jalium.UI.Xaml.dll')
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def stop_owned_app(app):
    prefix=str(app/'Contents/MacOS')+'/'
    for line in subprocess.check_output(['ps','-axo','pid=,command='],text=True).splitlines():
        pid,_,command=line.strip().partition(' ')
        if command.startswith(prefix):
            try:os.kill(int(pid),signal.SIGTERM)
            except ProcessLookupError:pass
def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app',type=Path,required=True);parser.add_argument('--work-parent',type=Path,required=True)
    parser.add_argument('--results-file',type=Path,required=True);parser.add_argument('--image-directory',type=Path,required=True)
    parser.add_argument('--cases',default=','.join(map(str,range(12))));args=parser.parse_args()
    source=args.app.resolve();cases=[int(x) for x in args.cases.split(',')]
    if not source.is_dir() or source.suffix!='.app' or not cases or len(set(cases))!=len(cases) or any(i not in range(12) for i in cases):parser.error('invalid app or cases')
    args.image_directory.mkdir(parents=True,exist_ok=True);receipts=[]
    with tempfile.TemporaryDirectory(prefix='jalium-keyboard-packet-',dir=args.work_parent) as name:
        work=Path(name);(work/'tmp').mkdir();app=work/'HostSmoke.app';subprocess.run(['/usr/bin/ditto',str(source),str(app)],check=True)
        native={};managed={};paths={}
        for filename in LIBRARIES+ASSEMBLIES:
            originals=list(source.rglob(filename));copies=[app/p.relative_to(source) for p in originals]
            if not originals or any(p.read_bytes()!=q.read_bytes() for p,q in zip(originals,copies)):raise RuntimeError('payload changed: '+filename)
            paths[filename]=copies
            if filename in LIBRARIES:native[filename]=[digest(p) for p in copies]
            else:
                if len(copies)!=1:raise RuntimeError('ambiguous assembly: '+filename)
                managed[filename]=digest(copies[0])
        try:
            for index in cases:
                output=work/('case-'+str(index));output.mkdir();started=time.monotonic()
                command=['/usr/bin/open','-n','-W','--env','JALIUM_KEYBOARD_PACKET_OUTPUT='+str(output),
                    '--env','TMPDIR='+str(work/'tmp'),'--env','DYLD_PRINT_LIBRARIES=1','--stdout',str(output/'app.log'),'--stderr',str(output/'app.err'),
                    str(app),'--args','--window-keyboard-packet-case='+str(index)]
                with (output/'launch.log').open('w') as log:
                    launcher=subprocess.Popen(command,stdout=log,stderr=subprocess.STDOUT);code=launcher.wait()
                result_path=output/'host-result.json';host=json.loads(result_path.read_text()) if result_path.exists() else None
                stderr=(output/'app.err').read_text(errors='replace') if (output/'app.err').exists() else ''
                loaded=[n for n in LIBRARIES if any(str(p) in stderr for p in paths[n])]
                failures=[]
                if code != 0:failures.append('launcher exit code '+str(code))
                if host is None:failures.append('host result missing')
                elif not host['passed']:failures.extend(host['failures'])
                if len(loaded)!=8:failures.append('all eight native payload paths were not observed')
                images={}
                for image in output.glob('*.png'):
                    dest=args.image_directory/('case-'+str(index)+'-'+image.name);shutil.copy2(image,dest);images[str(dest)]=digest(dest)
                receipt={'index':index,'passed':not failures,'failures':failures,'hostResult':host,'loadedNative':loaded,'launcherExitCode':code,
                    'dyldSHA256':hashlib.sha256(stderr.encode()).hexdigest(),'images':images,'seconds':round(time.monotonic()-started,3),
                    'failureLogTails':{n:(output/n).read_text(errors='replace')[-6000:] for n in ('app.log','app.err','launch.log') if (output/n).exists()} if failures else {}}
                receipts.append(receipt);print(json.dumps({'case':index,'passed':receipt['passed'],'checks':len(host['checks']) if host else 0,'failures':failures}),flush=True)
        finally:stop_owned_app(app)
        result={'passed':sum(c['passed'] for c in receipts),'total':len(cases),'cases':receipts,'nativeSHA256':native,'managedSHA256':managed,
            'temporaryRoot':str(work),'physicalInput':False,'desktopScreenshot':False,'mixedDPIDevice':False}
    result['temporaryRootRemoved']=not Path(result['temporaryRoot']).exists();args.results_file.write_text(json.dumps(result))
    print(json.dumps({k:result[k] for k in ('passed','total','temporaryRootRemoved')}),flush=True)
    return 0 if result['passed']==result['total'] else 1
if __name__=='__main__':raise SystemExit(main())
