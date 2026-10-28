#include "jalium_impeller_stroke.h"
#include "jalium_scanline_rasterizer.h"
#include <atomic>
#include <chrono>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <new>
#include <random>
#include <stdexcept>
#include <string>

static std::atomic<size_t> allocationCount{0};
static bool countAllocations = false;
void* operator new(size_t size) {
    if (countAllocations) allocationCount.fetch_add(1, std::memory_order_relaxed);
    if (void* p = std::malloc(size ? size : 1)) return p;
    throw std::bad_alloc();
}
void* operator new[](size_t size) { return ::operator new(size); }
void operator delete(void* p) noexcept { std::free(p); }
void operator delete[](void* p) noexcept { std::free(p); }
void operator delete(void* p, size_t) noexcept { std::free(p); }
void operator delete[](void* p, size_t) noexcept { std::free(p); }

namespace {
using namespace jalium;
struct Vertex { float x,y,r,g,b,a; };
struct Stroke {
    std::vector<float> points;
    float width = 2;
    ImpellerJoin join = ImpellerJoin::Miter;
    ImpellerCap cap = ImpellerCap::Butt;
    bool closed = false;
};
#if USE_STROKE_BUFFER
using Buffer = StrokeContourBuffer;
#else
struct Buffer {};
#endif

void Require(bool ok, const char* message) {
    if (!ok) throw std::runtime_error(message);
}

struct Work {
    Buffer buffer;
    std::vector<Contour> contours;
    std::vector<Vertex> vertices;
    std::vector<uint32_t> indices;
    std::vector<PixelRect> rects;

    void Run(const Stroke& s) {
        rects.clear();
        vertices.clear();
        indices.clear();
#if USE_STROKE_BUFFER
        buffer.Reset();
        ExpandStrokePath(vertices, indices, s.points.data(), uint32_t(s.points.size()/2),
            s.width, s.join, 4.0f, s.cap, s.closed, 1,1,1,1, nullptr, 1.0f, &buffer);
        RasterizePathToRects(buffer.Data(), buffer.Count(), FillRule::NonZero, rects);
#else
        contours.clear();
        ExpandStrokePath(vertices, indices, s.points.data(), uint32_t(s.points.size()/2),
            s.width, s.join, 4.0f, s.cap, s.closed, 1,1,1,1, &contours);
        RasterizePathToRects(contours, FillRule::NonZero, rects);
#endif
        Require(vertices.empty() && indices.empty(), "Analytic expansion emitted a mesh.");
    }
};

uint64_t Mix(uint64_t hash, uint32_t value) {
    for (int i=0; i<4; ++i) { hash ^= (value >> (i*8)) & 255u; hash *= 1099511628211ull; }
    return hash;
}
uint64_t Digest(uint64_t hash, const std::vector<PixelRect>& rects) {
    hash = Mix(hash, uint32_t(rects.size()));
    for (const auto& r : rects) {
        hash=Mix(hash,r.x); hash=Mix(hash,r.y); hash=Mix(hash,r.w); hash=Mix(hash,r.h);
        uint32_t alpha; std::memcpy(&alpha,&r.alpha,sizeof(alpha)); hash=Mix(hash,alpha);
    }
    return hash;
}

Contour Rectangle(float x0,float y0,float x1,float y1) {
    return {{x0,y0,x1,y0,x1,y1,x0,y1},true};
}
float CoverageAt(const std::vector<PixelRect>& rects,int x,int y) {
    float coverage=0;
    for (const auto& r : rects)
        if (x>=r.x && y>=r.y && x<r.x+r.w && y<r.y+r.h) coverage+=r.alpha;
    return coverage;
}
void VerifyFillCoverage() {
    std::vector<Contour> input{Rectangle(-1.25f,2.125f,3.5f,5.625f)};
    std::vector<PixelRect> rects;
    RasterizePathToRects(input,FillRule::NonZero,rects);
    for(int y=0;y<8;y++) for(int x=-4;x<6;x++) {
        float width=std::max(0.0f,std::min(float(x+1),3.5f)-std::max(float(x),-1.25f));
        float height=std::max(0.0f,std::min(float(y+1),5.625f)-std::max(float(y),2.125f));
        float expected=std::floor(width*height*255+0.5f)/255;
        Require(std::abs(CoverageAt(rects,x,y)-expected)<1e-6f,"Fractional rectangle coverage changed.");
    }
    input={Rectangle(0,0,12,12),Rectangle(3,3,9,9)};
    rects.clear(); RasterizePathToRects(input,FillRule::EvenOdd,rects);
    Require(CoverageAt(rects,4,4)==0 && CoverageAt(rects,1,1)==1,"Even-odd hole coverage changed.");
    rects.clear(); RasterizePathToRects(input,FillRule::NonZero,rects);
    Require(CoverageAt(rects,4,4)==1,"Overlapping contours double-painted.");
    auto& hole=input[1].points;
    for(size_t i=0;i<hole.size()/4;i++) {
        size_t j=hole.size()/2-1-i;
        std::swap(hole[i*2],hole[j*2]); std::swap(hole[i*2+1],hole[j*2+1]);
    }
    rects.clear(); RasterizePathToRects(input,FillRule::NonZero,rects);
    Require(CoverageAt(rects,4,4)==0,"Opposite winding did not preserve the hole.");
    const size_t before=rects.size();
    RasterizePathToRects(std::vector<Contour>{},FillRule::NonZero,rects);
    Require(rects.size()==before,"Empty input changed appended output.");
}

void VerifyVerticalSpanQuantization() {
    const PixelRect prefix{-10,-20,1,1,0.5f};
    for (bool wide : {false,true}) for (bool lowQuality : {false,true})
    for (auto rule : {FillRule::NonZero,FillRule::EvenOdd}) {
        std::vector<Contour> input{Rectangle(-0.1249f,0.09377f,wide?5.43125f:0.43125f,8.81249f)};
        // A horizontal contour changes the work budget without adding crossings.
        if (lowQuality) input.push_back({{0,120001,1,120001},false});
        std::vector<PixelRect> expected;
        if (lowQuality) {
            expected={{-1,0,1,8,32/255.0f},{-1,8,1,1,24/255.0f}};
            expected.insert(expected.begin()+1,{wide?5:0,0,1,8,110/255.0f});
            expected.push_back({wide?5:0,8,1,1,82/255.0f});
            if (wide) {
                expected.insert(expected.begin()+1,{0,0,5,8,1.0f});
                expected.insert(expected.end()-1,{0,8,5,1,191/255.0f});
            }
        } else {
            expected={{-1,0,1,1,28/255.0f},{wide?5:0,0,1,1,96/255.0f},
                      {-1,1,1,7,32/255.0f},{wide?5:0,1,1,7,110/255.0f},
                      {-1,8,1,1,26/255.0f},{wide?5:0,8,1,1,89/255.0f}};
            if (wide) {
                expected.insert(expected.begin()+1,{0,0,5,1,223/255.0f});
                expected.insert(expected.begin()+4,{0,1,5,7,1.0f});
                expected.insert(expected.end()-1,{0,8,5,1,207/255.0f});
            }
        }
        expected.insert(expected.begin(),prefix);
        std::vector<PixelRect> actual{prefix};
        RasterizePathToRects(input,rule,actual);
        Require(actual.size()==expected.size(),"Vertical span rectangle count changed.");
        for (size_t i=0;i<actual.size();i++) {
            const auto& a=actual[i]; const auto& b=expected[i];
            Require(a.x==b.x&&a.y==b.y&&a.w==b.w&&a.h==b.h&&a.alpha==b.alpha,
                    "Vertical span sub-row quantization or coalescing changed.");
        }
    }
    std::vector<PixelRect> emptyCoverage{prefix};
    RasterizePathToRects(std::vector<Contour>{Rectangle(.1249f,0,.1250f,9)},FillRule::NonZero,emptyCoverage);
    Require(emptyCoverage.size()==1&&emptyCoverage[0].alpha==prefix.alpha,
            "Sub-threshold coverage changed appended output.");
}

Stroke Curve(int points,float width,float height,float shift,float thickness) {
    Stroke s; s.width=thickness;
    for(int i=0;i<points;i++) {
        double t=double(i)/(points-1),u=1-t;
        s.points.push_back(float(3*u*u*t*width*0.5+3*u*t*t*width*0.5+t*t*t*width)+shift);
        s.points.push_back(float((3*u*t*t+t*t*t)*height)+shift*0.25f);
    }
    return s;
}
std::vector<Stroke> Workload(bool longCurves) {
    std::vector<Stroke> cases;
    for(int i=0;i<(longCurves?42:96);i++) {
        auto s=Curve(longCurves?128:4+i%22,longCurves?650.0f+i*13:10.0f+i*0.75f,
            longCurves?float(80+i%11*30):float(i%9*3),i*0.125f,1.8f+(i%3)*4);
        cases.push_back(std::move(s));
    }
    return cases;
}

void VerifyRepeatedStartCoverage() {
    for (auto join : {ImpellerJoin::Miter, ImpellerJoin::Bevel, ImpellerJoin::Round})
    for (auto cap : {ImpellerCap::Butt, ImpellerCap::Square, ImpellerCap::Round})
    for (bool closed : {false, true}) for (float width : {0.1f, 1.0f, 3.25f}) {
        Stroke canonical{{0,0,8,0,5,7}, width, join, cap, closed};
        for (int variant = 0; variant < 5; ++variant) {
            Stroke repeated = canonical;
            if (variant < 3) {
                for (int duplicate = 0; duplicate <= variant; ++duplicate)
                    repeated.points.insert(repeated.points.begin(), {0.0f, 0.0f});
            } else {
                // General compaction keeps the original first point even if
                // the redundant second point differs in bits or by epsilon.
                repeated.points.insert(repeated.points.begin() + 2,
                    {variant == 3 ? -0.0f : 1e-7f, 0.0f});
            }
            auto input = repeated.points;
            Work expected, actual; expected.Run(canonical); actual.Run(repeated);
            Require(Digest(0, expected.rects) == Digest(0, actual.rects),
                "Repeated start changed analytic coverage.");
            std::vector<Vertex> expectedVertices, actualVertices;
            std::vector<uint32_t> expectedIndices, actualIndices;
            auto mesh = [&](const Stroke& stroke, std::vector<Vertex>& vertices, std::vector<uint32_t>& indices) {
                ExpandStrokePath(vertices, indices, stroke.points.data(), uint32_t(stroke.points.size()/2),
                    width, join, 4.0f, cap, closed, 0.2f,0.3f,0.7f,0.8f);
            };
            mesh(canonical, expectedVertices, expectedIndices); mesh(repeated, actualVertices, actualIndices);
            Require(expectedIndices == actualIndices && expectedVertices.size() == actualVertices.size(),
                "Repeated start changed mesh topology.");
            Require(expectedVertices.empty() || std::memcmp(expectedVertices.data(), actualVertices.data(),
                expectedVertices.size()*sizeof(Vertex)) == 0, "Repeated start changed vertex bits.");
            Require(input.size() == repeated.points.size() && std::memcmp(input.data(), repeated.points.data(),
                input.size()*sizeof(float)) == 0, "Stroke expansion modified its input.");
        }
    }
    Work dot;
    dot.Run(Stroke{{1,2,1,2}, 1.0f, ImpellerJoin::Miter, ImpellerCap::Round});
    Require(!dot.rects.empty(), "A round-capped zero-length stroke lost its dot.");
    for (uint32_t count : {3u, 5u}) {
        Stroke repeated{{}, 1.0f, ImpellerJoin::Miter, ImpellerCap::Round};
        for (uint32_t index = 0; index < count; ++index) repeated.points.insert(repeated.points.end(), {1.0f,2.0f});
        Work actual; actual.Run(repeated);
        Require(Digest(0, dot.rects) == Digest(0, actual.rects), "All-coincident input changed its round-cap dot.");
    }
}

uint64_t VerifyStrokeCoverage() {
    Work current;
#if USE_STROKE_BUFFER
    std::vector<Vertex> referenceVertices;
    std::vector<uint32_t> referenceIndices;
    std::vector<Contour> referenceContours;
    std::vector<PixelRect> referenceRects;
#endif
    uint64_t hash=1469598103934665603ull;
    std::mt19937 random(20261027);
    for(int i=0;i<1500;i++) {
        Stroke s;
        s.width=0.1f+float(random()%240)*0.125f;
        s.join=ImpellerJoin(i%3); s.cap=ImpellerCap((i/3)%3); s.closed=i%7==0;
        int count=2+random()%25;
        for(int p=0;p<count;p++) {
            s.points.push_back(float(int(random()%2000)-500)*0.125f);
            s.points.push_back(float(int(random()%1600)-400)*0.125f);
            if(p%7==0) {
                float x=s.points[s.points.size()-2],y=s.points.back();
                s.points.push_back(x); s.points.push_back(y);
            }
        }
        current.Run(s); hash=Digest(hash,current.rects);
#if USE_STROKE_BUFFER
        referenceContours.clear(); referenceRects.clear();
        ExpandStrokePath(referenceVertices,referenceIndices,s.points.data(),uint32_t(s.points.size()/2),
            s.width,s.join,4.0f,s.cap,s.closed,1,1,1,1,&referenceContours);
        RasterizePathToRects(referenceContours,FillRule::NonZero,referenceRects);
        Require(current.rects.size()==referenceRects.size(),"Reused contours changed output rectangle count.");
        for(size_t r=0;r<referenceRects.size();r++) {
            const auto& a=current.rects[r]; const auto& b=referenceRects[r];
            Require(a.x==b.x && a.y==b.y && a.w==b.w && a.h==b.h && a.alpha==b.alpha,
                "Reused contours changed analytic coverage.");
        }
#endif
        if(i%17==0) {
            current.Run(Stroke{{3.25f,4.5f,3.25f,4.5f},0,ImpellerJoin::Miter,ImpellerCap::Butt});
            Require(current.rects.empty(),"Previous contours leaked into a degenerate stroke.");
        }
    }
#if USE_STROKE_BUFFER
    auto large=Curve(800,1600,300,0,2);
    current.Run(large);
    current.buffer.Reset();
    Require(current.buffer.Count()==0,"Reset retained active contours.");
    current.Run(Stroke{{0,0,8,0},2});
    Require(CoverageAt(current.rects,4,0)==1 && CoverageAt(current.rects,20,20)==0,
        "A short stroke replayed the unused buffer tail.");
#endif
    return hash;
}

void Benchmark(const char* name,const std::vector<Stroke>& cases,uint64_t digest) {
    Work work;
    for(int warm=0;warm<3;warm++) for(const auto& s:cases) work.Run(s);
    allocationCount.store(0); countAllocations=true;
    for(const auto& s:cases) work.Run(s);
    countAllocations=false;
    size_t allocations=allocationCount.load();
    size_t rectCount=0;
    auto start=std::chrono::steady_clock::now();
    constexpr int repeats=12;
    for(int repeat=0;repeat<repeats;repeat++) for(const auto& s:cases) { work.Run(s); rectCount+=work.rects.size(); }
    double ms=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count()/repeats;
    std::cout<<"{\"case\":\""<<name<<"\",\"strokes\":"<<cases.size()<<",\"milliseconds\":"<<ms
        <<",\"allocations\":"<<allocations<<",\"rects\":"<<rectCount/repeats
        <<",\"digest\":\""<<std::hex<<digest<<std::dec<<"\"}\n";
}
}

int main() {
    try {
        VerifyFillCoverage();
        VerifyVerticalSpanQuantization();
        VerifyRepeatedStartCoverage();
        uint64_t digest=VerifyStrokeCoverage();
        Benchmark("flow-fragments",Workload(false),digest);
        Benchmark("full-curves",Workload(true),digest);
        return 0;
    } catch(const std::exception& ex) {
        countAllocations=false;
        std::cerr<<ex.what()<<"\n"; return 1;
    }
}
