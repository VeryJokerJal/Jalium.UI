#include "jalium_gradient_sample.h"
#include "jalium_vello_encode.h"
#include <cstdio>
#include <stdexcept>

namespace {
void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
double Decode(double c) { return c<=.04045 ? c/12.92 : std::pow((c+.055)/1.055,2.4); }
double Encode(double c) { return c<=.0031308 ? c*12.92 : 1.055*std::pow(c,1/2.4)-.055; }
double Channel(double a,double b,double t,int mode) {
    return std::clamp(mode==0 ? Encode(Decode(a)+(Decode(b)-Decode(a))*t) : a+(b-a)*t,0.0,1.0);
}
int RampSamples() {
    int count=0;
    for (int mode : {0,1}) for (int variant=0;variant<8;++variant) {
        const float a=float(variant+1)/11, b=float(9-variant)/11;
        std::vector<JaliumGradientStop> source={{0,a,.2f,.8f,.3f},{1,b,.9f,.1f,.8f}};
        auto prepared=jalium::PrepareSrgbGradientStops(source,mode);
        Check(prepared.size()>=2 && prepared.size()<4096,"bounded ramp");
        std::vector<float> wire;
        for (const auto& s:prepared) wire.insert(wire.end(),{s.position,s.r,s.g,s.b,s.a});
        std::vector<jalium::EngineBrushData::GradientStop> engineStops;
        for (const auto& s:prepared) engineStops.push_back({s.position,s.r,s.g,s.b,s.a});
        jalium::EngineBrushData engine;engine.stops=engineStops.data();engine.stopCount=uint32_t(engineStops.size());
        std::vector<float> bands;jalium::BuildGradientRampBreakpoints(engine,bands);
        for (int i=0;i<=4096;++i) {
            double t=i/4096.0;
            auto color=jalium::SampleGradientStops(float(t),wire.data(),uint32_t(prepared.size()));
            const double expected[]={Channel(a,b,t,mode),Channel(.2f,.9f,t,mode),Channel(.8f,.1f,t,mode)};
            const double actual[]={color.r,color.g,color.b};
            double alpha=.3f+(.8f-.3f)*t;
            Check(std::abs(color.a-alpha)<1e-5,"alpha remains linear");
            for (int c=0;c<3;++c) {
                Check(std::abs(actual[c]-expected[c])<1.0/768,"straight color precision");
                Check(std::abs(actual[c]*color.a-expected[c]*alpha)<1.0/768,"premultiplied color precision");
            }
            // GPU vertex-color pipelines interpolate premultiplied endpoints.
            // Test the actual retained band list, not only the prepared stops.
            auto upper=std::upper_bound(bands.begin(),bands.end(),float(t));
            const float lo=upper==bands.begin()?bands.front():*(upper-1), hi=upper==bands.end()?bands.back():*upper;
            const float f=hi>lo?(float(t)-lo)/(hi-lo):0;
            auto l=jalium::SampleGradientStops(lo,wire.data(),uint32_t(prepared.size()));
            auto r=jalium::SampleGradientStops(hi,wire.data(),uint32_t(prepared.size()));
            const double x[]={l.r*l.a,l.g*l.a,l.b*l.a},y[]={r.r*r.a,r.g*r.a,r.b*r.a};
            for(int c=0;c<3;++c) Check(std::abs(x[c]+(y[c]-x[c])*f-expected[c]*alpha)<1.0/768,"vertex pipeline premultiplied precision");
            ++count;
        }
    }
    const float hard[]={0,1,0,0,1, 0,0,1,0,1, .5f,0,0,1,1, .5f,1,1,1,1, 1,0,0,0,1};
    for (uint32_t mode : {0u,1u}) {
        auto first=jalium::SampleGradientStops(0,hard,5,mode), middle=jalium::SampleGradientStops(.5f,hard,5,mode);
        Check(first.g>.99999f && first.r==0 && middle.r>.99999f && middle.g>.99999f && middle.b>.99999f,"hard stops retain source order");
    }
    const float extended[]={0,1.1f,-2.5f,.2f,.77f, 1,-2.5f,1.1f,.8f,.43f};
    for (uint32_t mode : {0u,1u}) {
        auto a=jalium::SampleGradientStops(-1,extended,2,mode), b=jalium::SampleGradientStops(2,extended,2,mode);
        Check(a.r==1 && a.g==0 && b.r==0 && b.g==1,"endpoint display clipping occurs before premultiplication");
    }
    return count;
}
void VelloModes() {
    jalium::VelloSceneEncoder encoder; encoder.BeginFrame(128,48);
    jalium::EngineBrushData::GradientStop stops[]={{0,.1f,.4f,.9f,.3f},{1,.9f,.2f,.1f,.8f}};
    jalium::EngineBrushData brush; brush.type=1;brush.endX=128;brush.stops=stops;brush.stopCount=2;
    const float path[]={0,128,0, 0,128,48, 0,0,48, 5};
    for (uint32_t mode : {0u,1u,0u}) {
        brush.colorInterpolationMode=mode;
        Check(encoder.EncodeFillPath(0,0,path,10,brush,jalium::FillRule::NonZero,{}),"encode gradient");
    }
    const auto& scene=encoder.scene();
    Check(scene.rampCount==2,"ramp cache distinguishes interpolation modes and reuses matching mode");
    for (int mode=0;mode<2;++mode) for (int i=0;i<512;++i) {
        double t=i/511.0, alpha=.3f+(.8f-.3f)*t;
        const auto pixel=scene.rampData[mode*512+i];
        const double expected[]={Channel(.1f,.9f,t,mode),Channel(.4f,.2f,t,mode),Channel(.9f,.1f,t,mode)};
        for (int c=0;c<3;++c) Check(std::abs(int((pixel>>(8*c))&255)-int(expected[c]*alpha*255+.5))<=1,"Vello ramp formula");
    }
}
}
int main() {
    try { int n=RampSamples();VelloModes();std::printf("PASS gradient interpolation: %d independent ramp samples; hard stops and two cached Vello modes\n",n);return 0; }
    catch (const std::exception& e) {std::fprintf(stderr,"FAIL gradient interpolation: %s\n",e.what());return 1;}
}
