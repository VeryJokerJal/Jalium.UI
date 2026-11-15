from pathlib import Path
import struct,json,hashlib
root=Path(__file__).resolve().parent
assets=root.parent
u16=lambda b,a:struct.unpack_from('>H',b,a)[0];u32=lambda b,a:struct.unpack_from('>I',b,a)[0]
p16=lambda *x:struct.pack('>'+'H'*len(x),*x);s16=lambda *x:struct.pack('>'+'h'*len(x),*x);p32=lambda *x:struct.pack('>'+'I'*len(x),*x)
def checksum(b):
 b=bytes(b)+b'\0'*(-len(b)%4);return sum(struct.unpack('>'+'I'*(len(b)//4),b))&0xffffffff
def read(p):
 b=p.read_bytes();return {b[12+16*i:16+16*i]:b[u32(b,20+16*i):u32(b,20+16*i)+u32(b,24+16*i)] for i in range(u16(b,4))}
def write(t):
 t={k:bytearray(v) for k,v in t.items()};struct.pack_into('>I',t[b'head'],8,0);n=len(t);power=1<<(n.bit_length()-1);head=(b'OTTO' if b'CFF ' in t else p32(0x10000))+p16(n,power*16,power.bit_length()-1,n*16-power*16);body=b'';directory=b''
 for tag,b in sorted(t.items()):
  at=12+n*16+len(body)
  if tag==b'head':adjust=at+8
  directory+=tag+p32(checksum(b),at,len(b));body+=b+b'\0'*(-len(b)%4)
 out=bytearray(head+directory+body);struct.pack_into('>I',out,adjust,(0xb1b0afba-checksum(out))&0xffffffff);return out
def axis(low,high):
 tags=[b'ideo',b'idtp',b'romn'];coords=[low,high,0];bt=p16(3)+b''.join(tags);bv=p16(0,3)+p16(10,14,18)+b''.join(p16(1)+s16(v) for v in coords);bs=p16(6,0,0)+bv;sl=p16(1)+b'DFLT'+p16(8)+bs;return p16(4,4+len(bt))+bt+sl
def base(vertHigh,horizontalLow=-200,horizontalHigh=800):
 h=axis(horizontalLow,horizontalHigh);v=axis(0,vertHigh);return p16(1,0,8,8+len(h))+h+v
def vvar(poison=False):
 # One positive region. Advance rows 2 and 7; independent TSB rows 8 and 9.
 values=[0,0,200,0,0,0,0,200,300,-200]+([700,-600] if poison else [])
 region=p16(1,1)+s16(0,16384,16384);data=p16(len(values),1,1,0)+b''.join(s16(x) for x in values)
 store=p16(1)+p32(12)+p16(1)+p32(12+len(region))+region+data
 mapping=bytes([0,3])+p16(8)+bytes([0,0,8,0,0,0,0,9])
 originMap=(bytes([0,3])+p16(8)+bytes([0,0,10,0,0,0,0,11])) if poison else b''
 return p16(1,0)+p32(24,0,24+len(store),0,24+len(store)+len(mapping) if poison else 0)+store+mapping+originMap
def number(value):
 if -107<=value<=107:return bytes([value+139])
 if 108<=value<=1131:return bytes([(value-108)//256+247,(value-108)%256])
 if -1131<=value<=-108:return bytes([(-value-108)//256+251,(-value-108)%256])
 return bytes([28])+s16(value)
def index(items):
 if not items:return p16(0)
 offsets=[1]
 for item in items:offsets.append(offsets[-1]+len(item))
 return p16(len(items))+bytes([2])+p16(*offsets)+b''.join(items)
def cff(name):
 programs=[]
 for gid in range(8):
  advance=1000 if gid==7 else 600
  if gid==1:programs.append(number(advance)+bytes([14]));continue
  width=400 if gid==0 else 900 if gid==7 else 500;height=700 if gid==0 else 500 if gid==5 else 750
  programs.append(number(advance)+number(0)+number(0)+bytes([21])+b''.join(number(x) for x in [width,0,0,height,-width,0,0,-height])+bytes([5,14]))
 nameIndex=index([name.encode('ascii')]);bbox=b''.join(number(x) for x in [0,0,900,750])+bytes([5]);top=bbox+bytes([29])+p32(0)+bytes([17]);at=4+len(nameIndex)+len(index([top]))+4;top=bbox+bytes([29])+p32(at)+bytes([17]);return bytes([1,0,4,4])+nameIndex+index([top])+p16(0,0)+index(programs)
def variable_base():
 def axis_variable():
  tags=p16(3)+b'ideoidtpromn';entries=[p16(1)+s16(0),p16(3)+s16(1000)+p16(6,0,0,0x8000),p16(1)+s16(0)];offsets=[];at=10
  for e in entries:offsets.append(at);at+=len(e)
  values=p16(0,3)+p16(*offsets)+b''.join(entries);scripts=p16(1)+b'DFLT'+p16(8)+p16(6,0,0)+values;return p16(4,4+len(tags))+tags+scripts
 h=axis(-200,800);v=axis_variable();region=p16(1,1)+s16(0,16384,16384);store=p16(1)+p32(12)+p16(1)+p32(12+len(region))+region+p16(1,1,1,0)+s16(200);return p16(1,1,12,12+len(h))+p32(12+len(h)+len(v))+h+v+store
rows=[]
for name in ['OriginZero','OriginPositive','OriginNegative','OriginShort','OriginShifted','OriginGvar','OriginVvarTsb','OriginVvarPoison','OriginBaseWide','OriginBaseNarrow','OriginBaseVariable','OriginMissing','OriginCffVorg','OriginCffNoVorg']:
 p=assets/('FontGvarMetrics/PhantomEmbedded.ttf' if name=='OriginGvar' else 'FontSizeAdjust/VerticalVariable.ttf');t=read(p);n=u16(t[b'maxp'],4)
 if name!='OriginGvar':
  for k in [b'gvar',b'fvar',b'VVAR',b'avar']:t.pop(k,None)
 t[b'name']=p16(0,1,18,3,1,0x409,1,len(('Jalium'+name).encode('utf-16-be')),0)+('Jalium'+name).encode('utf-16-be')
 # Include PostScript and full name records for reliable private font identity.
 names=[(1,'Jalium'+name),(2,'Regular'),(4,'Jalium'+name),(6,'Jalium'+name),(256,'Weight')];pool=b'';records=[]
 for nid,value in names:
  b=value.encode('utf-16-be');records.append(p16(3,1,0x409,nid,len(b),len(pool)));pool+=b
 t[b'name']=p16(0,len(names),6+12*len(names))+b''.join(records)+pool
 if name in ['OriginPositive','OriginNegative']:
  v=bytearray(t[b'vmtx'])
  for gid in [2,7]:struct.pack_into('>h',v,4*gid+2,250 if name=='OriginPositive' else -250)
  t[b'vmtx']=v
 if name=='OriginShort':
  v=bytearray(t[b'vhea']);struct.pack_into('>H',v,34,1);t[b'vhea']=v;t[b'vmtx']=p16(1200)+s16(0)+b''.join(s16(200 if gid==2 else -100 if gid==7 else 0) for gid in range(1,n))
 if name=='OriginShifted':
  g=bytearray(t[b'glyf']);h=bytearray(t[b'hmtx']);v=bytearray(t[b'vmtx'])
  for gid in [2,7]:
   at=u32(t[b'loca'],4*gid);xmin=200;ymin=-100;xmax=700 if gid==2 else 1100;ymax=650
   struct.pack_into('>hhhh',g,at+2,xmin,ymin,xmax,ymax);struct.pack_into('>h',g,at+18,xmin);struct.pack_into('>h',g,at+26,ymin);struct.pack_into('>h',h,gid*4+2,xmin);struct.pack_into('>h',v,gid*4+2,100)
  t[b'glyf']=g;t[b'hmtx']=h;t[b'vmtx']=v
 if name in ['OriginVvarTsb','OriginVvarPoison']:
  t[b'fvar']=read(p)[b'fvar'];t[b'gvar']=read(p)[b'gvar'];t[b'VVAR']=vvar(name=='OriginVvarPoison')
  # TrueType must ignore both VORG and the CFF2-only VVAR vOrg mapping.
  if name=='OriginVvarPoison':t[b'VORG']=p16(1,0)+s16(1600)+p16(2,2)+s16(1500)+p16(7)+s16(1700)
 if name in ['OriginBaseWide','OriginBaseNarrow']:t[b'BASE']=base(1200 if name=='OriginBaseWide' else 800)
 if name=='OriginBaseVariable':
  t[b'fvar']=read(p)[b'fvar'];t[b'gvar']=read(p)[b'gvar'];t[b'BASE']=variable_base()
 if name=='OriginMissing':t.pop(b'vmtx');t.pop(b'vhea')
 if name.startswith('OriginCff'):
  t[b'CFF ']=cff('Jalium'+name);t.pop(b'glyf');t.pop(b'loca');t[b'maxp']=p32(0x5000)+p16(n)
  v=bytearray(t[b'vmtx'])
  for gid in range(n):
   top=0 if gid==1 else 700 if gid==0 else 500 if gid==5 else 750
   origin=(700 if gid==2 else 1100 if gid==7 else 900) if name=='OriginCffVorg' else (900 if gid==2 else 650 if gid==7 else top)
   struct.pack_into('>h',v,gid*4+2,origin-top)
  t[b'vmtx']=v
  if name=='OriginCffVorg':t[b'VORG']=p16(1,0)+s16(900)+p16(2,2)+s16(700)+p16(7)+s16(1100)
 # Keep header extrema consistent with the generated geometric glyphs.
 if b'glyf' in t:
  boxes=[];bearings=[];advances=[];metricsCount=u16(t[b'vhea'],34) if b'vhea' in t else n
  for gid in range(n):
   at=u32(t[b'loca'],4*gid);end=u32(t[b'loca'],4*(gid+1));box=(0,0,0,0) if at==end else struct.unpack_from('>hhhh',t[b'glyf'],at+2);boxes.append(box)
   if b'vmtx' in t:
    advance=u16(t[b'vmtx'],min(gid,metricsCount-1)*4);offset=gid*4+2 if gid<metricsCount else metricsCount*4+(gid-metricsCount)*2;bearing=struct.unpack_from('>h',t[b'vmtx'],offset)[0];advances.append(advance);bearings.append(bearing)
  head=bytearray(t[b'head']);struct.pack_into('>hhhh',head,36,min(b[0] for b in boxes),min(b[1] for b in boxes),max(b[2] for b in boxes),max(b[3] for b in boxes));t[b'head']=head
  if b'vhea' in t:
   vhea=bytearray(t[b'vhea']);struct.pack_into('>Hhhh',vhea,10,max(advances),min(bearings),min(a-b-(box[3]-box[1]) for a,b,box in zip(advances,bearings,boxes)),max(b+box[3]-box[1] for b,box in zip(bearings,boxes)));t[b'vhea']=vhea
 extension='.otf' if b'CFF ' in t else '.ttf';data=write(t);file=name+extension;(root/file).write_bytes(data)
 source='FontGvarMetrics/PhantomEmbedded.ttf' if name=='OriginGvar' else 'FontSizeAdjust/VerticalVariable.ttf'
 rows.append({'file':file,'name':name,'sha256':hashlib.sha256(data).hexdigest(),'source':source,'sourceSha256':hashlib.sha256((assets/source).read_bytes()).hexdigest()})
(root/'PROVENANCE.json').write_text(json.dumps({'origin':'Generated offline from Jalium MIT geometric contours; CFF Type2 rectangles authored by this script.','files':rows},indent=2)+'\n')
import shutil
shutil.copy2(assets/'FontSizeAdjust/LICENSE.md',root/'LICENSE.md')
print('Generated',len(rows),'origin and BASE fonts')
