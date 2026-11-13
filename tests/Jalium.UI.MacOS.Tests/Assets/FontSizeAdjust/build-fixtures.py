"""Deterministic TrueType metrics fixtures; geometric outlines, no font dependency."""
from pathlib import Path
import struct,json,hashlib
root=Path(__file__).resolve().parent
u16=lambda *v:struct.pack('>'+'H'*len(v),*v)
i16=lambda *v:struct.pack('>'+'h'*len(v),*v)
u32=lambda *v:struct.pack('>'+'I'*len(v),*v)
def checksum(data):
 data+=b'\0'*((-len(data))%4);return sum(struct.unpack('>'+'I'*(len(data)//4),data))&0xffffffff
def rectangle(width,height):
 return i16(1,0,0,width,height)+u16(3,0)+b'\1'*4+i16(0,width,0,-width)+i16(0,0,height,0)
def make(name,scale,primary=False,missing=False,variable=None):
 chars=[(32,'space'),(65,'cap'),(72,'cap'),(77,'cap'),(120,'lower'),(48,'zero'),(0x6c34,'water')]
 if scale==.5 and not missing:chars.append((88,'cap'))
 if primary:chars=[p for p in chars if p[0]!=65]
 if missing:chars=[p for p in chars if p[0] in [32,65,77]]
 names=['.notdef']+[str(cp) for cp,_ in chars];glyphs=[rectangle(400,700)];hmetrics=[(600,0)];vmetrics=[(1200,0)]
 for cp,kind in chars:
  height=500 if kind=='lower' else 750;width=1000 if kind=='water' else 600
  glyphs.append(b'' if kind=='space' else rectangle(round((width-100)*scale),round(height*scale)))
  hmetrics.append((round(width*scale),0));vmetrics.append((round(1200*scale),0))
 loca=[0];glyf=b''
 for glyph in glyphs:
  glyf+=glyph;glyf+=b'\0'*((-len(glyf))%4);loca.append(len(glyf))
 n=len(glyphs);tables={b'glyf':glyf,b'loca':u32(*loca),b'hmtx':b''.join(u16(a)+i16(b) for a,b in hmetrics),b'vmtx':b''.join(u16(a)+i16(b) for a,b in vmetrics)}
 tables[b'head']=u32(0x10000,0x10000,0,0x5f0f3cf5)+u16(3,1000)+struct.pack('>QQ',0,0)+i16(0,0,1000,800)+u16(0,8)+i16(2,1,0)
 tables[b'hhea']=u32(0x10000)+i16(round(750*scale),round(-250*scale),0)+u16(1000)+i16(0,0,1000,1,0,0,0,0,0,0,0)+u16(n)
 tables[b'vhea']=u32(0x11000)+i16(round(750*scale),round(-250*scale),0)+u16(1200)+i16(0,0,1000,0,1,0,0,0,0,0,0)+u16(n)
 tables[b'maxp']=u32(0x10000)+u16(n,4,1,0,0,2,0,0,0,0,0,0,0,0)
 os2=bytearray(96);struct.pack_into('>HHHH',os2,0,4,500,400,5);os2[58:62]=b'JALM';struct.pack_into('>I',os2,42,1);struct.pack_into('>I',os2,46,1<<27);struct.pack_into('>HHHhhhHH',os2,62,0xc0,32,0x6c34,round(750*scale),round(-250*scale),0,round(750*scale),round(250*scale));struct.pack_into('>hhHHH',os2,86,0 if missing else round(500*scale),0 if missing else round(750*scale),0,32,1);tables[b'OS/2']=bytes(os2)
 cmap=sorted((cp,index+1) for index,(cp,_) in enumerate(chars));sub=u16(12,0)+u32(16+12*len(cmap),0,len(cmap))+b''.join(u32(cp,cp,gid) for cp,gid in cmap);tables[b'cmap']=u16(0,1,3,10)+u32(12)+sub
 strings=b'';records=[]
 for ident,value in [(1,name),(2,'Regular'),(4,name+' Regular'),(6,name.replace(' ','')),(5,'Version 1.0')]:
  data=value.encode('utf-16-be');records.append(u16(3,1,0x409,ident,len(data),len(strings)));strings+=data
 tables[b'name']=u16(0,len(records),6+12*len(records))+b''.join(records)+strings;tables[b'post']=u32(0x30000,0)+i16(-100,50)+u32(0,0,0,0,0)
 if variable:
  # A real variable face with a single wght axis. Only the vertical water
  # advance varies: +200 units at the maximum, independent of outlines.
  tables[b'fvar']=u16(1,0,16,2,1,20,0,8)+b'wght'+struct.pack('>iii',100*65536,400*65536,700*65536)+u16(0,256)
  tables[b'gvar']=u16(1,0,1,0)+u32(20+4*(n+1))+u16(n,1)+u32(20+4*(n+1))+u32(*([0]*(n+1)))
  def store(deltas):
   regions=u16(1,1)+i16(0,16384,16384)
   data=u16(len(deltas),1,1,0)+i16(*deltas)
   return u16(1)+u32(12)+u16(1)+u32(12+len(regions))+regions+data
  water=next(index+1 for index,(cp,_) in enumerate(chars) if cp==0x6c34)
  mapped=variable!='identity';deltas=[0,200] if mapped else [200 if index==water else 0 for index in range(n)]
  variation=store(deltas);mapping=b'\0\0'+u16(n)+bytes(1 if index==water else 0 for index in range(n)) if mapped else b''
  tables[b'VVAR']=u16(1,0)+u32(24,24+len(variation) if mapping else 0,0,0,0)+variation+mapping
  if variable in ['avar','avar2']:
   segment=u16(4)+i16(-16384,-16384,0,0,8192,4096,16384,16384)
   tables[b'avar']=u16(2 if variable=='avar2' else 1,0,0,1)+segment
   if variable=='avar2':
    # Format-1 index map and a +0.5 normalized correction at the peak.
    at=len(tables[b'avar'])+8;index_map=b'\1\0'+u32(1)+b'\0'
    tables[b'avar']+=u32(at,at+len(index_map))+index_map+store([8192])
 count=len(tables);power=2**(count.bit_length()-1);data=u32(0x10000)+u16(count,16*power,power.bit_length()-1,16*count-16*power);body=b'';at=12+16*count;offsets={}
 for tag,value in sorted(tables.items()):
  offsets[tag]=at;data+=tag+u32(checksum(value),at,len(value));body+=value+b'\0'*((-len(value))%4);at=12+16*count+len(body)
 data=bytearray(data+body);struct.pack_into('>I',data,offsets[b'head']+8,(0xb1b0afba-checksum(bytes(data)))&0xffffffff);assert checksum(bytes(data))==0xb1b0afba
 file=root/(name+'.ttf');file.write_bytes(data)
 return {'file':file.name,'sha256':hashlib.sha256(data).hexdigest(),'unitsPerEm':1000,'xHeight':0 if missing else .5*scale,'capHeight':0 if missing else .75*scale,'zeroAdvance':0 if missing else .6*scale,'waterAdvance':0 if missing else scale,'waterHeight':0 if missing else 1.2*scale,'sourceScalars':[cp for cp,_ in chars]}
rows=[make('Primary',1,primary=True),make('Secondary',.5),make('Missing',1,missing=True)]+[make(name,1,variable=kind) for name,kind in [('VerticalVariable','identity'),('VerticalMapped','mapped'),('VerticalAvar','avar'),('VerticalAvar2','avar2')]];(root/'PROVENANCE.json').write_text(json.dumps({'origin':'Generated exclusively from geometric contours by build-fixtures.py; no external fonts or packages.','files':rows},indent=2)+'\n')
