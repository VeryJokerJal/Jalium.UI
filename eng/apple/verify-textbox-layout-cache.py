#!/usr/bin/env python3
"""Verify TextBox cache lifetime and unchanged paint through own-process SDK calls."""
from pathlib import Path
import argparse
import json
import subprocess
import os
import sys

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--app', type=Path, required=True)
parser.add_argument('--results-directory', type=Path, required=True)
args = parser.parse_args()
app = args.app.resolve()
if not app.is_dir() or app.suffix != '.app':
    parser.error('supply a built HostSmoke app')
out = args.results_directory.resolve()
out.mkdir(parents=True, exist_ok=False)
results = []
allowed = {x + '.dylib' for x in ['core', 'text', 'platform', 'metal', 'software', 'media.core', 'media', 'browser']}
for index in [37,38,39,40]:
 case=out/str(index);case.mkdir();env=os.environ.copy();env.update(JALIUM_MANAGED_FONT_CAPTURE_DIR=str(case),DYLD_PRINT_LIBRARIES='1')
 with (case/'app.log').open('w') as log,(case/'app.err').open('w') as error:
  process=subprocess.Popen([str(app/'Contents/MacOS/Jalium.UI.MacOS.HostSmoke'),'--window-font-case='+str(index)],env=env,stdout=log,stderr=error);(case/'active-process.json').write_text(json.dumps({'pid':process.pid,'app':str(app)}));code=process.wait(timeout=120)
 baselines=list(case.glob('*-before-*x.bgra'));pixels=len(baselines)==12
 for baseline in baselines:
  before=baseline.read_bytes();stem=baseline.name
  peers={name:case/stem.replace('-before-', '-'+name+'-') for name in ['changed','restored','evicted']}
  pixels=pixels and all(path.exists() for path in peers.values()) and before!=peers['changed'].read_bytes() and before==peers['restored'].read_bytes()==peers['evicted'].read_bytes()
 receipt=json.loads((case/'cache-result.json').read_text()) if (case/'cache-result.json').exists() else None;lines=[x for x in (case/'app.err').read_text(errors='replace').splitlines() if '/libjalium.native.' in x];own={x.split('/libjalium.native.')[-1].split()[0] for x in lines if str(app) in x};foreign=[x for x in lines if str(app) not in x];required={'core.dylib','text.dylib','platform.dylib','software.dylib' if index>=39 else 'metal.dylib'};passed=bool(code==0 and pixels and receipt and receipt['phases']==receipt['expectedPhases']==12 and receipt['checks']==85 and receipt['ownerCachesRetired'] and len(list(case.glob('*.bgra')))==48 and required<=own<=allowed and not foreign);d={'index':index,'passed':passed,'exitCode':code,'result':receipt,'pixelContracts':pixels,'ownNativeLibraries':sorted(own),'foreignNativeLibraryCount':len(foreign)};results.append(d);print(json.dumps({'index':index,'passed':passed,'phases':receipt['phases'] if receipt else None,'checks':receipt['checks'] if receipt else None}),flush=True)
final={'cases':results,'passed':sum(x['passed'] for x in results),'total':4,'physicalInput':False,'foregroundRequired':False};(out/'receipt.json').write_text(json.dumps(final));raise SystemExit(0 if final['passed']==4 else 1)
