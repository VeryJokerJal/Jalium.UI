"""Offline deterministic GSUB vertical-feature fixtures using Jalium geometric outlines."""
from pathlib import Path
import hashlib,json,struct,shutil
root=Path(__file__).resolve().parent
u16=lambda b,o:struct.unpack_from('>H',b,o)[0]
u32=lambda b,o:struct.unpack_from('>I',b,o)[0]
p16=lambda *v:struct.pack('>'+'H'*len(v),*v)
p32=lambda *v:struct.pack('>'+'I'*len(v),*v)
def checksum(b):
 b=bytes(b)+b'\0'*(-len(b)%4);return sum(struct.unpack('>'+'I'*(len(b)//4),b))&0xffffffff
scalars=[32,40,65,72,0x2014,0x3002,0x4e2d,0xff08]
provenance=json.loads((root.parent/'FontSizeAdjust/PROVENANCE.json').read_text())
rows=[]
for name,source,features in [('VerticalBoth','Primary',['vert','vrt2']),('VerticalOnly','Primary',['vert']),('RotationOnly','Primary',['vrt2']),('VerticalVariable','VerticalVariable',['vert','vrt2'])]:
 p=root.parent/'FontSizeAdjust'/(source+'.ttf');original=p.read_bytes();expected=next(f['sha256'] for f in provenance['files'] if f['file']==p.name);assert hashlib.sha256(original).hexdigest()==expected
 tables={original[12+16*i:16+16*i]:bytearray(original[u32(original,20+16*i):u32(original,20+16*i)+u32(original,24+16*i)]) for i in range(u16(original,4))}
 # Static original gids: 1 space, 2 H, 4 x, 6 water. The variable font has
 # 2 A, 5 x, 6 zero, 7 water. All mapped source scalars use gid 2 normally.
 sub=p16(12,0)+p32(16+12*len(scalars),0,len(scalars))+b''.join(p32(cp,cp,1 if cp==32 else 2) for cp in scalars)
 tables[b'cmap']=p16(0,1,3,10)+p32(12)+sub
 features=['salt']+features
 lookups=[]
 for feature in features:
  target=(7 if source=='VerticalVariable' else 6) if feature=='vert' else (5 if source=='VerticalVariable' else 4)
  sub=p16(2,8,1,target)+p16(1,1,2)
  lookups.append(p16(1,0,1,8)+sub)
 if source=='VerticalVariable':
  sub=p16(2,8,1,6)+p16(1,1,2);lookups.append(p16(1,0,1,8)+sub)
 offsets=[];at=2+2*len(lookups)
 for lookup in lookups:offsets.append(at);at+=len(lookup)
 lookup_list=p16(len(lookups),*offsets)+b''.join(lookups)
 at=2+6*len(features)
 feature_list=p16(len(features))+b''.join(tag.encode()+p16(at+6*i) for i,tag in enumerate(features))+b''.join(p16(0,1,i) for i in range(len(features)))
 def script(indices):return p16(4,0)+p16(0,0xffff,len(indices),*indices)
 full=script(list(range(len(features))));han=script(list(range(1,len(features))))
 # DFLT/latn share the same LangSys. hani deliberately omits salt, so
 # feature reindexing after private-tag sorting changes its numeric indices.
 script_list=p16(3)+b'DFLT'+p16(20)+b'hani'+p16(20+len(full))+b'latn'+p16(20)+full+han
 minor=1 if source=='VerticalVariable' else 0;header_size=14 if minor else 10
 variation=b''
 if minor:
  condition=p16(1)+p32(6)+p16(1,0,12288,16384)
  substitute=p16(1,0,1,features.index('vert'))+p32(12)+p16(0,1,len(lookups)-1)
  variation=p16(1,0)+p32(1,16,16+len(condition))+condition+substitute
 extra=p32(header_size+len(script_list)+len(feature_list)+len(lookup_list)) if minor else b''
 tables[b'GSUB']=p16(1,minor,header_size,header_size+len(script_list),header_size+len(script_list)+len(feature_list))+extra+script_list+feature_list+lookup_list+variation
 strings=b'';records=[]
 for ident,value in [(1,'Jalium '+name),(2,'Regular'),(4,'Jalium '+name+' Regular'),(6,'Jalium'+name),(5,'Version 1.0')]:
  data=value.encode('utf-16-be');records.append(p16(3,1,0x409,ident,len(data),len(strings)));strings+=data
 tables[b'name']=p16(0,len(records),6+12*len(records))+b''.join(records)+strings
 struct.pack_into('>I',tables[b'head'],8,0)
 count=len(tables);power=1<<(count.bit_length()-1);header=p32(0x10000)+p16(count,16*power,power.bit_length()-1,16*count-16*power);records=[];body=bytearray();at=12+16*count;head=None
 for tag,value in sorted(tables.items()):
  if tag==b'head':head=at
  records.append(tag+p32(checksum(value),at,len(value)));body+=value;body+=b'\0'*(-len(value)%4);at=12+16*count+len(body)
 result=bytearray(header+b''.join(records)+body);struct.pack_into('>I',result,head+8,(0xb1b0afba-checksum(result))&0xffffffff);assert checksum(result)==0xb1b0afba
 out=root/(name+'.ttf');out.write_bytes(result)
 rows.append({'file':out.name,'sha256':hashlib.sha256(result).hexdigest(),'source':str(p.relative_to(root.parent)),'sourceSha256':expected,'features':features,'glyphs':{'normal':2,'vert':(7 if source=='VerticalVariable' else 6) if 'vert' in features else None,'vrt2':(5 if source=='VerticalVariable' else 4) if 'vrt2' in features else None},'unitsPerEm':1000,'verticalAdvance':1200,'verticalHighWeightGlyph':6 if minor else None,'variableWaterAdvance':{'wght400':1200,'wght550':1300,'wght700':1400} if source=='VerticalVariable' else None})
shutil.copy2(root.parent/'FontSizeAdjust/LICENSE.md',root/'LICENSE.md')
(root/'PROVENANCE.json').write_text(json.dumps({'origin':'Generated offline from Jalium geometric fixtures; no external font or package dependency.','sourceScalars':scalars,'files':rows},indent=2)+'\n')
print('Generated',len(rows),'vertical-feature fixtures')
