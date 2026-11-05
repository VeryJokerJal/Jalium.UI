#include "opentype_vertical_alias.h"
#include <cstdio>
#include <fstream>
#include <iterator>
#include <stdexcept>
#include <string>
using namespace jalium::font;
namespace {
int checks = 0;
void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); ++checks; }
std::vector<uint8_t> Table(const std::string& path) {
    std::ifstream stream(path, std::ios::binary);
    std::vector<uint8_t> bytes((std::istreambuf_iterator<char>(stream)), {}); ByteReader r(bytes);
    Check(r.InBounds(0,12),"Fixture sfnt");
    for (size_t i = 0; i < r.U16(4); ++i) {
        size_t at = 12 + i * 16;
        Check(r.InBounds(at,16),"Fixture table directory");
        if (r.U32(at) == 0x47535542) {
            size_t off = r.U32(at+8), length = r.U32(at+12); Check(r.InBounds(off,length),"Fixture GSUB bounds");
            return {bytes.begin()+off,bytes.begin()+off+length};
        }
    }
    throw std::runtime_error("Fixture GSUB missing");
}
void Verify(std::vector<uint8_t> original, bool variable) {
    ByteReader before(original); const size_t sl = before.U16(4), fl = before.U16(6);
    Check(before.U32(fl+2)==0x73616c74,"Original salt first");
    size_t firstScript=sl+before.U16(sl+6),lang=firstScript+before.U16(firstScript);
    // DFLT and latn share this LangSys; required index must be remapped once.
    VerticalWrite16(original,lang+2,1);
    auto output=original;VerticalAliases aliases;
    Check(AliasVerticalFeatures(output,aliases),"Alias valid GSUB"); ByteReader after(output);
    Check(aliases.tags[0]==0x4a763030 && aliases.tags[1]==0x4a763031,"Collision-free default private tags");
    Check(after.U32(fl+2)==0x4a763030 && after.U32(fl+8)==0x4a763031 && after.U32(fl+14)==0x73616c74,"FeatureList sorted and unrelated tag retained");
    Check(after.U16(lang+2)==0,"Required feature remapped once despite shared LangSys");
    size_t han=sl+after.U16(sl+12),hanLang=han+after.U16(han);
    Check(after.U16(hanLang+4)==2 && after.U16(hanLang+6)==0 && after.U16(hanLang+8)==1,"Script-specific language indices remapped");
    // Lookup data is byte-for-byte unchanged, including extension/contextual
    // subtables unknown to this adapter and interpreted by CoreText itself.
    const size_t lookups=before.U16(8),end=variable?before.U32(10):original.size();
    Check(std::equal(original.begin()+lookups,original.begin()+end,output.begin()+lookups),"Original lookup program retained");
    if(variable){
        size_t fv=after.U32(10),sub=fv+after.U32(fv+12);
        Check(after.U16(sub+6)==0,"FeatureVariations references remapped vertical feature");
        size_t alt=sub+after.U32(sub+8);Check(after.U16(alt+4)==3,"Alternate lookup retained");
        auto multiple=original;size_t fresh=multiple.size();multiple.resize(fresh+30);
        VerticalWrite16(multiple,fresh,1);VerticalWrite16(multiple,fresh+2,0);VerticalWrite16(multiple,fresh+4,2);
        VerticalWrite16(multiple,fresh+6,0);VerticalWrite32(multiple,fresh+8,18);
        VerticalWrite16(multiple,fresh+12,1);VerticalWrite32(multiple,fresh+14,24);
        for(size_t i=0;i<2;++i){size_t at=fresh+18+i*6;VerticalWrite16(multiple,at,0);VerticalWrite16(multiple,at+2,1);VerticalWrite16(multiple,at+4,uint16_t(i));}
        VerticalWrite32(multiple,fv+12,uint32_t(fresh-fv));
        Check(AliasVerticalFeatures(multiple,aliases),"Multiple conditional feature substitutions");ByteReader m(multiple);
        Check(m.U16(fresh+6)==0 && m.U16(fresh+12)==2,"Conditional records sorted after feature remapping");
        Check(m.U16(fresh+m.U32(fresh+8)+4)==1 && m.U16(fresh+m.U32(fresh+14)+4)==0,"Conditional feature payloads retained");
    }
    auto collision=original;VerticalWrite32(collision,fl+2,0x4a763030);
    Check(AliasVerticalFeatures(collision,aliases),"Existing private-tag collision");
    Check(aliases.tags[0]==0x4a763031 && aliases.tags[1]==0x4a763032,"Existing tag never overwritten");
    for(size_t length=0;length<original.size();++length){
        std::vector<uint8_t> cut(original.begin(),original.begin()+length),copy=cut;VerticalAliases ignored;
        bool valid=AliasVerticalFeatures(cut,ignored);Check(valid||cut==copy,"Truncation cannot partially modify input");
    }
    for(size_t at=0;at<std::min<size_t>(original.size(),160);++at){
        auto bad=original;bad[at]^=0xff;auto copy=bad;VerticalAliases ignored;
        bool valid=AliasVerticalFeatures(bad,ignored);Check(valid||bad==copy,"Invalid table cannot partially modify input");
    }
}
}
int main(int argc,char**argv){try{Check(argc>1,"Fixture directory");Verify(Table(std::string(argv[1])+"/VerticalBoth.ttf"),false);Verify(Table(std::string(argv[1])+"/VerticalVariable.ttf"),true);std::printf("PASS vertical alias adapter: %d checks; language/required/conditional feature references, collisions and truncated inputs\n",checks);return 0;}catch(const std::exception&e){std::fprintf(stderr,"FAIL vertical alias adapter: %s\n",e.what());return 1;}}
