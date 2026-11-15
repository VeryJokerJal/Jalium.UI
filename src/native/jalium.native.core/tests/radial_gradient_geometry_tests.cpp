#include "jalium_gradient_sample.h"
#include "jalium_radial_raster.h"
#include "jalium_vello_encode.h"
#include <cstdio>
#include <stdexcept>

namespace {
void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);}
long double Reference(long double x,long double y,long double fx,long double fy,long double fr,long double r,bool boundary)
{
    const long double dx=96-fx,dy=64-fy,dr=r-fr,qx=x-fx,qy=y-fy;
    if(dx==0&&dy==0&&dr==0)return NAN;
    const auto a=dx*dx+dy*dy-dr*dr,b=-2*(qx*dx+qy*dy+fr*dr),c=qx*qx+qy*qy-fr*fr;
    long double t=NAN;
    auto accept=[&](long double v){if(fr+v*dr>0&&(std::isnan(t)||v>t))t=v;};
    if(a==0){if(b!=0)accept(-c/b);}
    else{const auto d=b*b-4*a*c;if(d>=0){accept((-b+std::sqrt(d))/(2*a));accept((-b-std::sqrt(d))/(2*a));}}
    return std::isnan(t)&&boundary?INFINITY:t;
}
int Samples()
{
    const double circles[][4]={{96,64,0,40},{96,64,16,40},{96,64,60,40},{96,64,40,40},
        {96,64,0,0},{96,64,24,0},{80,64,8,40},{88,64,64,40},{56,64,40,40},
        {168,64,0,40},{168,64,12,40},{136,64,0,40},{124,64,12,40},{116,64,60,40},{96,136,8,40}};
    const jalium::EngineBrushData::GradientStop stops[]={{0,0,0,0,.25f},{1,1,1,1,.85f}};
    const float wire[]={0,0,0,0,.25f,1,1,1,1,.85f};int n=0;
    for(const auto& circle:circles)for(uint32_t spread=0;spread<3;++spread)for(int y=0;y<128;y+=3)for(int x=0;x<200;x+=3){
        const bool boundary=circle[0]==136&&circle[1]==64&&circle[2]==0;
        const auto wanted=Reference(x+.5,y+.5,circle[0],circle[1],circle[2],circle[3],boundary);
        jalium::EngineBrushData b;b.type=2;b.centerX=96;b.centerY=64;b.originX=circle[0];b.originY=circle[1];b.focalRadius=circle[2];b.radiusX=b.radiusY=circle[3];b.radialFlags=boundary?3:(circle[0]==124&&circle[2]==12?5:1);b.spreadMethod=spread;b.stops=stops;b.stopCount=2;
        const auto actual=jalium::GradientParameter(b,x+.5f,y+.5f);
        Check(std::isnan(wanted)==std::isnan(actual),"transparent cone domain");
        if(std::isfinite(wanted))Check(std::fabs(actual-wanted)<std::max(2e-5L,std::fabs(wanted)*2e-7L),"last positive-radius intersection");
        const auto color=jalium::SampleBrushGradient(b,wire,x+.5f,y+.5f);
        if(std::isnan(wanted))Check(color.a==0,"outside cone stays transparent");
        else{
            double t=double(wanted);
            if(std::isinf(t))t=spread==0?1:.5;
            else if(spread==1)t-=std::floor(t);
            else if(spread==2){t=std::fmod(std::fabs(t),2);if(t>1)t=2-t;}
            else t=std::clamp(t,0.0,1.0);
            // Float phase is compared away from repeat seams.
            if(spread!=1||(t>1e-4&&t<1-1e-4))Check(std::fabs(color.r-t)<.002&&std::fabs(color.a-(.25+.6*t))<.002,"spread and alpha");
        }
        ++n;
    }
    return n;
}
void StateAndAverage()
{
    jalium::RadialGradientGeometryState state;Check(state.SetRadialGeometry(16,1)==JALIUM_OK,"set fr");
    for(float value:{-1.f,INFINITY,NAN}){Check(state.SetRadialGeometry(value,1)==JALIUM_ERROR_INVALID_ARGUMENT,"invalid fr");Check(state.focalRadius==16&&state.radialFlags==1,"invalid mutation preserves state");}
    for(uint32_t flags:{0u,2u,3u,4u,UINT32_MAX})Check(state.SetRadialGeometry(16,flags)==JALIUM_ERROR_INVALID_ARGUMENT,"invalid flag/radius combination");
    const JaliumGradientStop stops[]={{0,0,0,0,.2f},{.25f,1,1,1,.8f},{1,0,0,0,.6f}};
    float r,g,b,a;
    for(uint32_t mode:{0u,1u}){jalium::AverageGradientStops(stops,3,mode,r,g,b,a);const double wanted=mode==0?1.055*std::pow(.5,1/2.4)-.055:.5;Check(std::fabs(r-wanted)<1e-6&&r==g&&g==b&&std::fabs(a-.65)<1e-6,"offset weighted color space and alpha");}
}
void PixelCoverage()
{
    jalium::Contour source;source.points={20,12,180,12,180,108,20,108};source.closed=true;
    const jalium::EngineBrushData::GradientStop stops[]={{0,0,0,0,.25f},{1,1,1,1,.85f}};
    jalium::EngineBrushData b;b.type=2;b.centerX=96;b.centerY=64;b.originX=136;b.originY=64;b.radiusX=b.radiusY=40;b.radialFlags=3;b.spreadMethod=1;b.stops=stops;b.stopCount=2;
    int count=0;const jalium::PixelRect clip{144,48,24,16,1};
    Check(jalium::RasterizeRadialPaint({source},b,{},jalium::FillRule::NonZero,[&](float x,float y,const jalium::GradientColor& c){
        Check(x>=144&&x<168&&y>=48&&y<64,"raster scissor");Check(std::fabs(c.r-.5)<1e-6&&std::fabs(c.a-.55)<1e-6,"boundary raster mean");++count;
    },&clip),"raster fallback consumes paint");Check(count==384,"device cell count");
    auto outline=jalium::RadialStrokeContours({source},8,true,0,4,0,nullptr,0,0,1);
    Check(!outline.empty(),"mature closed stroke expansion");
    jalium::VelloSceneEncoder encoder;encoder.BeginFrame(200,128);
    const float small[]={0,150,48,0,150,50,0,144,50,5};
    Check(encoder.EncodeFillPath(144,48,small,10,b,jalium::FillRule::NonZero,{}),"Vello boundary fallback");
    Check(encoder.scene().numPaths==12,"Vello boundary cells preserve coverage");
}
void ApexLimits()
{
    for(double focal:{96.5,84.5})for(double fr:{0.,16.,60.}) {
        const double t=-fr/(40-fr),x=focal+t*(96.5-focal);
        const double actual=jalium::SvgRadialParameter(x,64.5,96.5,64.5,focal,64.5,40,40,fr,1);
        Check(std::isfinite(actual)&&std::fabs(actual-t)<1e-5,"nested zero-radius endpoint limit");
        if(fr==60)Check(actual<t,"shrinking endpoint approaches repeating seam from below");
    }
    jalium::EngineBrushData b;b.type=2;b.radialFlags=1;b.centerX=b.originX=96.5f;b.centerY=b.originY=64.5f;b.radiusX=b.radiusY=40;
    Check(jalium::SvgRadialNeedsLimitRaster(b,{}),"pixel-center Vello endpoint compatibility");
    b.centerX=b.originX=96;Check(!jalium::SvgRadialNeedsLimitRaster(b,{}),"off-center endpoint retains GPU circle");
}
void VelloCircleData()
{
    jalium::VelloSceneEncoder encoder;encoder.BeginFrame(200,128);
    const jalium::EngineBrushData::GradientStop stops[]={{0,0,0,0,1},{1,1,1,1,1}};
    jalium::EngineBrushData b;b.type=2;b.centerX=96;b.centerY=64;b.originX=168;b.originY=64;b.radiusX=40;b.radiusY=24;b.focalRadius=12;b.radialFlags=1;b.stops=stops;b.stopCount=2;
    const float path[]={0,180,12,0,180,108,0,20,108,5};
    Check(encoder.EncodeFillPath(20,12,path,10,b,jalium::FillRule::NonZero,{}),"Vello encode cone");
    const auto& words=encoder.scene().drawData;Check(words.size()>=7,"radial draw payload");
    float fr,r;std::memcpy(&fr,&words[words.size()-2],4);std::memcpy(&r,&words.back(),4);
    Check(fr==12&&r==40,"Vello start and end radii");
    encoder.BeginFrame(200,128);
    b.centerX=b.centerY=.5f;b.originX=.5000002f;b.originY=.5f;
    b.radiusX=b.radiusY=b.focalRadius=.25f;
    Check(encoder.EncodeFillPath(20,12,path,10,b,jalium::FillRule::NonZero,{}),"Vello nearly coincident circles");
    Check(encoder.scene().drawTags.back()==jalium::kVelloDrawTagRadialGradient,
        "distinct representable focal centers must not collapse to transparent");
}
}
int main(){try{int n=Samples();StateAndAverage();PixelCoverage();ApexLimits();VelloCircleData();std::printf("PASS radial circle geometry: %d independent long-double domain/parameter/color samples; state; weighted averages; Vello circle payload\n",n);return 0;}catch(const std::exception& e){std::fprintf(stderr,"FAIL radial circle geometry: %s\n",e.what());return 1;}}
