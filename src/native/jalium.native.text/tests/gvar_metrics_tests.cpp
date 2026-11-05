#include "opentype_gvar_metrics.h"
#include "font_data.h"
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <string>
#include <tuple>
#include <utility>
#include <limits>
#include <stdexcept>

using jalium::font::ByteReader;
using jalium::font::GvarVerticalMetrics;
namespace {
unsigned checks = 0;
void Check(bool value, const char* message) {
    if (!value) throw std::runtime_error(message);
    ++checks;
}
void Near(double actual, double expected) {
    if (std::abs(actual - expected) > .000001) {
        std::cerr << actual << " != " << expected << '\n';
        throw std::runtime_error("gvar phantom advance");
    }
    ++checks;
}
struct Face {
    std::vector<uint8_t> bytes;
    jalium::font::SfntTables tables;
    explicit Face(const std::filesystem::path& path) {
        std::ifstream file(path, std::ios::binary);
        bytes.assign(std::istreambuf_iterator<char>(file), {});
        Check(tables.Parse(ByteReader(bytes), 0), "Read geometric fixture sfnt");
    }
};
using Inputs = std::array<ByteReader, 7>;
Inputs Tables(const Face& face) {
    return {face.tables.Table(0x67766172), face.tables.Table(0x676c7966),
        face.tables.Table(0x6c6f6361), face.tables.Table(0x68656164),
        face.tables.Table(0x6d617870), face.tables.Table(0x76686561), face.tables.Table(0x766d7478)};
}
bool Advance(const Inputs& t, std::vector<double> coordinates, double& result, uint16_t glyph = 7) {
    return GvarVerticalMetrics(t[0], t[1], t[2], t[3], t[4], t[5], t[6]).Advance(glyph, coordinates, result);
}
void Write16(std::vector<uint8_t>& bytes, size_t at, uint16_t value) {
    bytes.at(at) = uint8_t(value >> 8); bytes.at(at + 1) = uint8_t(value);
}
void Write32(std::vector<uint8_t>& bytes, size_t at, uint32_t value) {
    Write16(bytes, at, uint16_t(value >> 16)); Write16(bytes, at + 2, uint16_t(value));
}
void Fixtures(const std::filesystem::path& root) {
    for (const char* name : {"PhantomEmbedded", "PhantomShared", "PhantomAllPoints",
        "PhantomShortOffsets", "PhantomTopOnly", "PhantomBottomOnly", "PhantomUseMyMetrics"}) {
        Face face(root/(std::string(name) + ".ttf")); auto tables = Tables(face);
        for (double coordinate : {-1., -.5, 0., .25, .5, .75, 1.}) {
            double advance = 0;
            Check(Advance(tables, {coordinate}, advance), "Embedded/shared/private/short/word phantom program");
            Near(advance, 1200 + coordinate*200);
            Check(Advance(tables, {coordinate}, advance, 2), "Upright horizontal-script glyph uses the same metric parser");
            Near(advance, 1200 + coordinate*200);
        }
    }
    for (const char* name : {"PhantomUnreferenced", "PhantomLastMetrics"}) {
        Face face(root/(std::string(name) + ".ttf"));
        for (double coordinate : {-1., 0., .5, 1.}) {
            double advance = 0; Check(Advance(Tables(face), {coordinate}, advance), "Omitted phantoms / last USE_MY_METRICS"); Near(advance, 1200);
        }
    }
    Face composite(root/"PhantomComposite.ttf");
    for (double coordinate : {-1., 0., .5, 1.}) {
        double advance = 0; Check(Advance(Tables(composite), {coordinate}, advance), "Composite pseudo-point count"); Near(advance, 1200 + coordinate*300);
    }
    Face intermediate(root/"PhantomIntermediate.ttf");
    for (auto [coordinate, expected] : std::vector<std::pair<double, double>>{
        {-1,1200},{-.75,1200},{-.5,1000},{-.375,1100},{-.25,1200},{0,1200},
        {.25,1200},{.375,1300},{.5,1400},{.625,1300},{.75,1200},{1,1200}}) {
        double advance = 0; Check(Advance(Tables(intermediate), {coordinate}, advance), "Intermediate start/peak/end scalar"); Near(advance, expected);
    }
    for (auto [name, coordinate, expected] : std::vector<std::tuple<const char*, double, double>>{
        {"PhantomAvar",.25,1250},{"PhantomAvar2",.375,1275}}) {
        Face face(root/(std::string(name) + ".ttf")); double advance = 0;
        Check(Advance(Tables(face), {coordinate}, advance), "Already-normalized avar coordinates"); Near(advance, expected);
    }
    Face multiple(root/"PhantomMultiAxis.ttf");
    for (auto [coordinates, expected] : std::vector<std::pair<std::vector<double>, double>>{
        {{0,0},1200},{{1,1},1400},{{.5,.5},1250},{{.5,0},1200},{{-1,1},1200},{{-1,-1},1000},{{1,-1},1200}}) {
        double advance = 0; Check(Advance(Tables(multiple), coordinates, advance), "Product of participating axes"); Near(advance, expected);
    }
}

// The origin is an independent metric: advance-only callers may still read
// a font whose optional bearing tail is truncated, while origin callers must
// reject that tail instead of silently using an advance as a bearing.
void Origins(const std::filesystem::path& root) {
    auto origins = root.parent_path()/"FontVerticalOrigins";
    for (auto [name, a, water] : std::vector<std::tuple<const char*, double, double>>{
        {"OriginZero",750,750}, {"OriginPositive",1000,1000},
        {"OriginNegative",500,500}, {"OriginShort",950,650},
        {"OriginShifted",750,750}}) {
        Face face(origins/(std::string(name)+".ttf")); auto t=Tables(face);
        // No gvar is required for an ordinary static TrueType origin.
        GvarVerticalMetrics metrics(t[0],t[1],t[2],t[3],t[4],t[5],t[6]);
        double value=0;
        Check(metrics.BaseOrigin(2,value), "Static Latin TSB and glyph yMax"); Near(value,a);
        Check(metrics.BaseOrigin(7,value), "Static CJK long/trailing TSB index"); Near(value,water);
        Check(!metrics.BaseOrigin(8,value), "Static origin glyph count boundary");
        for (unsigned table : {1u,2u,3u,4u,5u,6u}) {
            for (size_t length=0;length<t[table].Size();++length) {
                auto changed=t;changed[table]=ByteReader(t[table].Data(),length);
                double truncated=0;
                bool valid=GvarVerticalMetrics(changed[0],changed[1],changed[2],changed[3],changed[4],changed[5],changed[6]).BaseOrigin(7,truncated);
                Check(!valid || truncated==water, "Truncated static origin data never invents a bearing");
            }
        }
    }
    for (auto [name,top] : std::vector<std::pair<const char*,double>>{
        {"PhantomEmbedded",100},{"PhantomShared",100},{"PhantomAllPoints",100},
        {"PhantomShortOffsets",100},{"PhantomTopOnly",200},{"PhantomBottomOnly",0},
        {"PhantomUnreferenced",0},{"PhantomUseMyMetrics",100}}) {
        Face face(root/(std::string(name)+".ttf"));auto t=Tables(face);
        GvarVerticalMetrics metrics(t[0],t[1],t[2],t[3],t[4],t[5],t[6]);
        for (double coordinate : {-1.,-.5,0.,.5,1.}) for (uint16_t glyph : {uint16_t(2),uint16_t(7)}) {
            double advance=0,origin=0;
            Check(metrics.Metrics(glyph,{coordinate},advance,origin), "Advance and top phantom are independent");
            Near(origin,750+top*coordinate);
            Near(advance,std::string(name)=="PhantomUnreferenced"?1200:1200+coordinate*200);
        }
    }
    for (auto [name,origin,advance] : std::vector<std::tuple<const char*,double,double>>{
        {"PhantomComposite",900,1500},{"PhantomLastMetrics",500,1200}}) {
        Face face(root/(std::string(name)+".ttf"));auto t=Tables(face);double a=0,o=0;
        Check(GvarVerticalMetrics(t[0],t[1],t[2],t[3],t[4],t[5],t[6]).Metrics(7,{1},a,o), "Composite/last USE_MY_METRICS origin");
        Near(o,origin);Near(a,advance);
    }
    Face face(root/"PhantomEmbedded.ttf"); auto t=Tables(face);double a=0,o=0;
    auto shortTail=t;shortTail[6]=ByteReader(t[6].Data(),7*4+2);
    GvarVerticalMetrics partial(shortTail[0],shortTail[1],shortTail[2],shortTail[3],shortTail[4],shortTail[5],shortTail[6]);
    Check(partial.Advance(7,{1},a), "Existing advance does not require an unused TSB");Near(a,1400);
    Check(!partial.BaseOrigin(7,o)&&!partial.Metrics(7,{1},a,o), "Missing TSB is rejected by origin callers");
    // Empty glyphs use yMax=0, but still retain their signed top side bearing.
    auto empty=t;std::vector<uint8_t> loca(t[2].Data(),t[2].Data()+t[2].Size());
    Write32(loca,8*4,t[2].U32(7*4));empty[2]=ByteReader(loca);
    Check(GvarVerticalMetrics(empty[0],empty[1],empty[2],empty[3],empty[4],empty[5],empty[6]).BaseOrigin(7,o), "Empty outline origin");Near(o,0);
}

void LargePointRuns(const std::filesystem::path& root) {
    Face base(root/"PhantomEmbedded.ttf");
    for (unsigned pointCount : {1000u, 65536u}) for (bool sparse : {false, true}) for (bool implicit : {false, true}) {
        if (sparse && implicit) continue;
        std::array<std::vector<uint8_t>, 7> owned;
        auto source = Tables(base);
        for (unsigned i = 0; i < owned.size(); ++i)
            owned[i].assign(source[i].Data(), source[i].Data()+source[i].Size());
        Write16(owned[4], 4, 1); Write16(owned[5], 34, 1); owned[6].resize(4);
        auto& glyf = owned[1]; glyf.assign(14, 0);
        Write16(glyf, 0, 1); Write16(glyf, 10, uint16_t(pointCount-1));
        for (unsigned left = pointCount; left;) {
            unsigned run = std::min(left, 256u);
            glyf.push_back(run == 1 ? 0x31 : 0x39);
            if (run > 1) glyf.push_back(uint8_t(run-1));
            left -= run;
        }
        auto& loca = owned[2]; loca.assign(8, 0); Write32(loca, 4, uint32_t(glyf.size()));
        std::vector<uint8_t> serialized;
        unsigned logical = pointCount+4;
        if (!sparse) {
            if (!implicit) serialized.push_back(0); // All points, including indices beyond 65535.
        } else if (pointCount == 65536) {
            // Relative 16-bit deltas can address an absolute index > 65535.
            serialized = {3,0x82,0xff,0xff,0,3,0,1}; logical = 3;
        } else {
            // Extended point count (1004) and multiple byte-number runs.
            serialized = {uint8_t(0x80 | (logical >> 8)),uint8_t(logical)};
            for (unsigned at = 0; at < logical;) {
                unsigned run = std::min(logical-at,128u);
                serialized.push_back(uint8_t(run-1));
                for (unsigned i = 0; i < run; ++i) serialized.push_back(at+i ? 1 : 0);
                at += run;
            }
        }
        auto zero = [&](unsigned count) {
            while (count) { unsigned run=std::min(count,64u); serialized.push_back(uint8_t(0x80 | (run-1))); count-=run; }
        };
        zero(logical); zero(logical-2);
        serialized.insert(serialized.end(), {0x41,1,0x2c,0xff,0x9c}); // +300 / -100.
        auto& gvar = owned[0]; gvar.assign(38,0);
        Write16(gvar,0,1); Write16(gvar,4,1); Write16(gvar,12,1); Write16(gvar,14,1);
        Write32(gvar,16,28); Write32(gvar,24,uint32_t(10+serialized.size()));
        Write16(gvar,28,1); Write16(gvar,30,10); Write16(gvar,32,uint16_t(serialized.size()));
        Write16(gvar,34,implicit?0x8000:0xa000); Write16(gvar,36,0x4000);
        gvar.insert(gvar.end(),serialized.begin(),serialized.end());
        Inputs input;
        for (unsigned i=0;i<input.size();++i) input[i]=ByteReader(owned[i]);
        double advance=0;
        Check(Advance(input,{1},advance,0), "Large all-point / extended-count / relative-word streams"); Near(advance,1600);
        Check(Advance(input,{.5},advance,0), "Large stream interpolation"); Near(advance,1400);
    }
}

void TupleCombinations(const std::filesystem::path& root) {
    Face shared(root/"PhantomShared.ttf"); auto input=Tables(shared);
    std::vector<uint8_t> data(input[0].Data(),input[0].Data()+input[0].Size());
    const size_t glyph=input[0].U32(16)+input[0].U32(48);
    const size_t high=glyph+input[0].U16(glyph+2)+4+input[0].U16(glyph+4);
    const unsigned oldBytes=input[0].U16(glyph+8);
    const std::vector<uint8_t> privateTop{1,0,6,0x80,0x40,1,0x2c};
    data.erase(data.begin()+high,data.begin()+high+oldBytes);
    data.insert(data.begin()+high,privateTop.begin(),privateTop.end());
    Write16(data,glyph+8,uint16_t(privateTop.size()));Write16(data,glyph+10,0x2001);
    Write32(data,52,input[0].U32(52)+uint32_t(privateTop.size())-oldBytes);
    input[0]=ByteReader(data);
    double advance=0;
    Check(Advance(input,{1},advance),"Private point list overrides shared glyph point numbers");Near(advance,1500);
    Check(Advance(input,{.5},advance),"Private override uses its own logical delta count");Near(advance,1350);
    Check(Advance(input,{-1},advance),"Other tuple retains shared numbers");Near(advance,1000);

    Face embedded(root/"PhantomEmbedded.ttf");input=Tables(embedded);
    data.assign(input[0].Data(),input[0].Data()+input[0].Size());
    const size_t at=input[0].U32(16)+input[0].U32(48);
    Write16(data,at+12,0xa123);input[0]=ByteReader(data);
    Check(Advance(input,{1},advance),"Embedded peak ignores the low shared-index bits");Near(advance,1400);
    Write16(data,at+14,0x2000);Write16(data,at+8,0xe000);input[0]=ByteReader(data);
    for(auto [coordinate,expected]:std::vector<std::pair<double,double>>{{.25,1300},{.75,1400},{-.25,1100},{-.75,1000}}){
        Check(Advance(input,{coordinate},advance),"Implicit regions saturate beyond positive and negative peaks");Near(advance,expected);
    }

    Face intermediate(root/"PhantomIntermediate.ttf");input=Tables(intermediate);
    data.assign(input[0].Data(),input[0].Data()+input[0].Size());
    const size_t region=input[0].U32(16)+input[0].U32(48);
    Write16(data,region+20,0x2000);Write16(data,region+22,0x2000);input[0]=ByteReader(data);
    Check(Advance(input,{.5},advance),"Collapsed intermediate region includes its peak");Near(advance,1400);
    Check(Advance(input,{.49},advance),"Collapsed intermediate region excludes other coordinates");Near(advance,1200);

    Face multiple(root/"PhantomMultiAxis.ttf");input=Tables(multiple);
    data.assign(input[0].Data(),input[0].Data()+input[0].Size());
    const size_t multi=input[0].U32(16)+input[0].U32(48);
    Write16(data,multi+10,0);Write16(data,multi+18,0);input[0]=ByteReader(data);
    Check(Advance(input,{.5,-1},advance),"Zero peak makes the other axis non-participating");Near(advance,1300);
}

void DeepMetricsChain(const std::filesystem::path& root) {
    Face base(root/"PhantomEmbedded.ttf"); auto source=Tables(base);
    std::array<std::vector<uint8_t>,7> owned;
    for(unsigned i=0;i<owned.size();++i)owned[i].assign(source[i].Data(),source[i].Data()+source[i].Size());
    constexpr unsigned count=1024;
    Write16(owned[4],4,count);Write16(owned[4],28,1);Write16(owned[4],30,count-1);Write16(owned[5],34,count);
    owned[1].clear();owned[2].assign((count+1)*4,0);owned[6].assign(count*4,0);
    for(unsigned i=0;i<count;++i){
        Write32(owned[2],i*4,uint32_t(owned[1].size()));Write16(owned[6],i*4,1200);
        size_t at=owned[1].size();owned[1].resize(at+(i+1<count?18:12));
        if(i+1<count){Write16(owned[1],at,0xfffe);Write16(owned[1],at+10,0x203);Write16(owned[1],at+12,uint16_t(i+1));}
    }
    Write32(owned[2],count*4,uint32_t(owned[1].size()));
    owned[0].assign(20+(count+1)*4,0);Write16(owned[0],0,1);Write16(owned[0],4,1);Write16(owned[0],12,count);Write16(owned[0],14,1);Write32(owned[0],16,uint32_t(owned[0].size()));
    Inputs input;for(unsigned i=0;i<input.size();++i)input[i]=ByteReader(owned[i]);
    double advance=0;Check(Advance(input,{1},advance,0),"Deep negative-contour USE_MY_METRICS chain avoids stack recursion");Near(advance,1200);
    Write16(owned[1],(count-2)*18+12,0);input[1]=ByteReader(owned[1]);
    Check(!Advance(input,{1},advance,0),"Deep cycle remains bounded");
}

void Malformed(const std::filesystem::path& root) {
    Face face(root/"PhantomEmbedded.ttf"); const auto tables = Tables(face);
    double result = 0;
    for (unsigned table : {0u,1u,2u,3u,4u,5u}) {
        const size_t needed = table == 3 ? 54 : table == 4 ? 6 : table == 5 ? 36 : tables[table].Size();
        for (size_t length = 0; length < needed; ++length) {
            auto input = tables; input[table] = ByteReader(tables[table].Data(), length);
            Check(!Advance(input, {1}, result), "Every truncated required prefix is rejected");
        }
    }
    auto original = std::vector<uint8_t>(tables[0].Data(), tables[0].Data()+tables[0].Size());
    auto reject = [&](std::vector<uint8_t> changed) {
        auto input = tables; input[0] = ByteReader(changed);
        Check(!Advance(input, {1}, result), "Malformed gvar metadata / point / delta run rejected");
    };
    for (auto [offset, value] : std::vector<std::pair<size_t, uint16_t>>{
        {0,0},{2,1},{4,0},{4,257},{6,4097},{12,7},{14,2}}) {
        auto changed = original; Write16(changed, offset, value); reject(std::move(changed));
    }
    for (size_t offset : {8u,16u,48u,52u}) {
        auto changed = original;
        if (offset == 8) Write16(changed, 6, 1);
        Write32(changed, offset, uint32_t(changed.size()+100)); reject(std::move(changed));
    }
    const size_t glyph = tables[0].U32(16) + tables[0].U32(20+7*4);
    for (auto [offset, value] : std::vector<std::pair<size_t, uint16_t>>{
        {glyph,0x1002},{glyph+2,0},{glyph+6,0x1000},{glyph+6,0x2000}}) {
        auto changed = original; Write16(changed, offset, value); reject(std::move(changed));
    }
    const size_t positive = glyph + tables[0].U16(glyph+2) + tables[0].U16(glyph+4);
    for (auto [offset, value] : std::vector<std::pair<size_t, uint8_t>>{
        {positive+1,0x7f},{positive+2,8},{positive+3,0},{positive+4,0xff},{positive+5,0xff}}) {
        auto changed = original; changed.at(offset) = value; reject(std::move(changed));
    }
    Check(!Advance(tables, {}, result), "Axis count mismatch");
    Check(!Advance(tables, {2}, result), "Out-of-range coordinate");
    Check(!Advance(tables, {std::numeric_limits<double>::quiet_NaN()}, result), "NaN coordinate");
    Check(!Advance(tables, {1}, result, 8), "Glyph count boundary");

    Face composite(root/"PhantomUseMyMetrics.ttf"); auto input = Tables(composite);
    auto glyf = std::vector<uint8_t>(input[1].Data(), input[1].Data()+input[1].Size());
    size_t water = input[2].U32(7*4);
    Write16(glyf, water+12, 7); input[1] = ByteReader(glyf);
    Check(!Advance(input, {1}, result), "USE_MY_METRICS cycle rejected without recursion");

    uint32_t random = 0x17345a9b;
    for (unsigned i = 0; i < 20000; ++i) {
        random ^= random << 13; random ^= random >> 17; random ^= random << 5;
        unsigned table = random % tables.size();
        auto bytes = std::vector<uint8_t>(tables[table].Data(), tables[table].Data()+tables[table].Size());
        bytes[random % bytes.size()] ^= uint8_t(1u << ((random >> 8) & 7));
        auto changed = tables; changed[table] = ByteReader(bytes);
        bool valid = Advance(changed, {.5}, result);
        Check(!valid || (std::isfinite(result) && result >= 0), "Deterministic malformed-table corpus stays bounded");
        double origin=0;valid=GvarVerticalMetrics(changed[0],changed[1],changed[2],changed[3],changed[4],changed[5],changed[6]).Metrics(7,{.5},result,origin);
        Check(!valid || (std::isfinite(result) && result>=0 && std::isfinite(origin)), "Malformed joint origin data stays bounded");
    }
}
} // namespace
int main(int argc, const char** argv) {
    try {
        Check(argc == 2, "Fixture directory argument");
        Fixtures(argv[1]); Origins(argv[1]); LargePointRuns(argv[1]); TupleCombinations(argv[1]); DeepMetricsChain(argv[1]); Malformed(argv[1]);
        std::cout << "PASS gvar metrics: " << checks << " checks\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL gvar metrics: " << error.what() << '\n'; return 1;
    }
}
