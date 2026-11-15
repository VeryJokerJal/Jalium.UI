"""Derive a selectable GSUB fixture from the existing WPT font, offline."""
from pathlib import Path
import hashlib,json,struct,shutil
root=Path(__file__).resolve().parent;source=root.parent/'FontNumeric/FontWithFancyFeatures.otf';original=source.read_bytes()
assert hashlib.sha256(original).hexdigest()=='0f7e550009d5d7348fdbaf79365e9cdbe010feb04e3af00bacc49f825e1f93f2'
u16=lambda b,o:struct.unpack_from('>H',b,o)[0]
u32=lambda b,o:struct.unpack_from('>I',b,o)[0]
p16=lambda *v:struct.pack('>'+'H'*len(v),*v)
p32=lambda *v:struct.pack('>'+'I'*len(v),*v)
tables={original[12+16*i:16+16*i]:bytearray(original[u32(original,20+16*i):u32(original,20+16*i)+u32(original,24+16*i)]) for i in range(u16(original,4))}
# Keep original glyph outlines and the preferred Unicode cmap12 mapping.
# a=27, b=28, c=29, d=30, x=50.
# Three visibly distinct, in-range alternate selections; z is not covered.
for glyph,width in [(28,512),(29,768),(30,1536)]:struct.pack_into('>H',tables[b'hmtx'],glyph*4,width)
lookups=[]
for glyph in [50,27,50,50]:
 coverage=p16(1,1,glyph);alternates=p16(3,28,29,30)
 sub=p16(1,8,1,8+len(coverage))+coverage+alternates
 lookups.append(p16(3,0,1,8)+sub)
lookup_offsets=[];at=2+2*len(lookups)
for value in lookups:lookup_offsets.append(at);at+=len(value)
lookup_list=p16(4,*lookup_offsets)+b''.join(lookups)
feature_list=p16(4)+b''.join(tag+p16(26+6*i) for i,tag in enumerate([b'salt',b'smcp',b'subs',b'sups']))+b''.join(p16(0,1,i) for i in range(4))
lang=p16(0,0xffff,4,0,1,2,3);script=p16(4,0)+lang
script_list=p16(2)+b'DFLT'+p16(14)+b'latn'+p16(14)+script
start=10;tables[b'GSUB']=p16(1,0,start,start+len(script_list),start+len(script_list)+len(feature_list))+script_list+feature_list+lookup_list
# Resources are loaded privately by graphics face; original name records
# remain under the retained WPT license and are not installed system-wide.
struct.pack_into('>I',tables[b'head'],8,0)
def checksum(data):
 data=bytes(data)+b'\0'*(-len(data)%4);return sum(struct.unpack('>'+'I'*(len(data)//4),data))&0xffffffff
count=len(tables);power=1<<(count.bit_length()-1);header=b'OTTO'+p16(count,16*power,power.bit_length()-1,16*count-16*power);records=[];payload=bytearray();offset=12+16*count;head_offset=None
for tag,value in sorted(tables.items()):
 if tag==b'head':head_offset=offset
 records.append(tag+p32(checksum(value),offset,len(value)));payload+=value;payload+=b'\0'*(-len(value)%4);offset=12+16*count+len(payload)
result=bytearray(header+b''.join(records)+payload);struct.pack_into('>I',result,head_offset+8,(0xb1b0afba-checksum(result))&0xffffffff);assert checksum(result)==0xb1b0afba
out=root/'selectable-variants.otf';out.write_bytes(result);shutil.copy2(root.parent/'FontNumeric/LICENSE.md',root/'LICENSE.md')
(root/'PROVENANCE.json').write_text(json.dumps({'source':'../FontNumeric/FontWithFancyFeatures.otf','source_sha256':hashlib.sha256(original).hexdigest(),'result_sha256':hashlib.sha256(result).hexdigest(),'license':'BSD-3-Clause; retained in LICENSE.md','outlines_unchanged':True,'changes':['Replace GSUB with DFLT/latn salt, smcp, subs and sups alternate-substitution lookups.','smcp maps a to b/c/d; salt, subs and sups map x to b/c/d.','Set b/c/d advances to 512/768/1536 in the original 1024 units per em.','Rebuild sfnt checksums.'],'literal_reference':{'salt 0':'x','salt 1':'b','salt 2':'c','salt 3':'d'},'registered_boolean_tags':'Compare exact requested values with independent CoreText; out-of-range boolean glyph behavior is not asserted.','uncovered_glyph':'z'},indent=2)+'\n');print('Generated',out.name,len(result),'bytes')
