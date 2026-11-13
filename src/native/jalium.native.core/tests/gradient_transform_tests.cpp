#include "jalium_gradient_sample.h"
#include "jalium_vello_encode.h"
#include <cstdio>
#include <limits>
#include <stdexcept>

namespace {
void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);}
double Parameter(double x,double y,bool radial)
{
    if(!radial)return ((x-.1)*.8+(y-.2)*.55)/(.8*.8+.55*.55);
    const double fx=-2.0/9,dx=(x-.45)/.45-fx,dy=(y-.5)/.6;
    const double a=dx*dx+dy*dy,b=fx*dx;
    return a>1e-20?a/(-b+std::sqrt(b*b+a*(1-fx*fx))):0;
}
int CoordinateSamples()
{
    const JaliumMatrix transforms[]={{160,0,0,64,20,12},{168,14.08f,-49.6f,54.4f,32.8f,8.8f},
        {128,-16,19.2f,70.4f,48.8f,14.56f},{-144,12.8f,32,64,188,8},
        {.0001f,.00003f,-.00002f,.0002f,0,0}};
    const jalium::EngineBrushData::GradientStop stops[]={{0,0,0,0,1},{1,1,1,1,1}};
    const float wire[]={0,0,0,0,1,1,1,1,1,1};int count=0;
    for(const auto& m:transforms)for(bool radial:{false,true})for(uint32_t spread:{0u,1u,2u}){
        jalium::GradientPaintTransformState state;Check(state.SetPaintTransform(m)==JALIUM_OK,"affine paint map");
        jalium::EngineBrushData brush;state.CopyPaintTransform(brush);brush.type=radial?2:1;
        brush.startX=.1f;brush.startY=.2f;brush.endX=.9f;brush.endY=.75f;
        brush.centerX=.45f;brush.centerY=.5f;brush.radiusX=.45f;brush.radiusY=.6f;brush.originX=.35f;brush.originY=.5f;
        brush.stops=stops;brush.stopCount=2;brush.spreadMethod=spread;
        for(int i=-16;i<=48;++i)for(int j=-8;j<=24;++j){
            const double x=i/32.0,y=j/16.0;
            float px,py;jalium::TransformGradientPoint(m,float(x),float(y),px,py);
            // Scalar inverse oracle uses the original matrix and sampled float
            // pixel, independently of the production inverse and parameter.
            const double dx=double(px)-m.m31,dy=double(py)-m.m32,det=double(m.m11)*m.m22-double(m.m12)*m.m21;
            double t=Parameter((dx*m.m22-dy*m.m21)/det,(dy*m.m11-dx*m.m12)/det,radial);
            Check(std::abs(jalium::GradientParameter(brush,px,py)-t)<1e-4,"inverse-space parameter");
            if(spread==1)t-=std::floor(t);
            else if(spread==2){t=std::fmod(t,2);if(t<0)t+=2;if(t>1)t=2-t;}
            else t=std::clamp(t,0.0,1.0);
            const auto color=jalium::SampleBrushGradient(brush,wire,px,py);
            // Repeat wraps exactly at a seam; float arithmetic can land on
            // either side. Compare away from that sub-pixel discontinuity.
            if(spread!=1 || (t>1e-4&&t<1-1e-4))Check(std::abs(color.r-t)<1e-4&&std::abs(color.g-t)<1e-4&&std::abs(color.b-t)<1e-4&&color.a==1,"transformed gradient color");
            ++count;
        }
    }
    return count;
}
void InvalidMaps()
{
    jalium::GradientPaintTransformState state;const JaliumMatrix valid{1,.2f,-.3f,.85f,8,-5};
    Check(state.SetPaintTransform(valid)==JALIUM_OK,"valid mutation");
    for(const auto& bad:{JaliumMatrix{0,0,0,1,0,0},JaliumMatrix{1,2,2,4,0,0},
        JaliumMatrix{1,0,0,1,std::numeric_limits<float>::infinity(),0},
        JaliumMatrix{std::numeric_limits<float>::quiet_NaN(),0,0,1,0,0}}){
        Check(state.SetPaintTransform(bad)==JALIUM_ERROR_INVALID_ARGUMENT,"reject invalid paint map");
        Check(state.gradientToUser.m12==valid.m12&&state.gradientToUser.m31==valid.m31,"failed mutation retains paint");
    }
}
void VelloPaintMaps()
{
    jalium::VelloSceneEncoder encoder;encoder.BeginFrame(200,96);
    const jalium::EngineBrushData::GradientStop stops[]={{0,0,0,0,1},{1,1,1,1,1}};
    jalium::EngineBrushData brush;brush.type=1;brush.endX=1;brush.endY=1;brush.stops=stops;brush.stopCount=2;
    jalium::GradientPaintTransformState state;Check(state.SetPaintTransform({160,0,0,64,20,12})==JALIUM_OK,"bbox map");state.CopyPaintTransform(brush);
    const float path[]={0,180,12,0,180,76,0,20,76,5};
    Check(encoder.EncodeFillPath(20,12,path,10,brush,jalium::FillRule::NonZero,{}),"Vello transformed linear");
    brush.type=2;brush.centerX=.45f;brush.centerY=.5f;brush.radiusX=.45f;brush.radiusY=.6f;brush.originX=.35f;brush.originY=.5f;
    Check(encoder.EncodeFillPath(20,12,path,10,brush,jalium::FillRule::NonZero,{}),"Vello transformed focal ellipse");
    bool box=false,ellipse=false;
    for(const auto& m:encoder.scene().transforms){
        if(std::abs(m.matrix[0]-160)<1e-5&&std::abs(m.matrix[3]-64)<1e-5&&std::abs(m.translation[0]-20)<1e-5)box=true;
        if(std::abs(m.matrix[0]-160)<1e-5&&std::abs(m.matrix[3]-64*.6/.45)<1e-4)ellipse=true;
    }
    Check(box&&ellipse,"Vello composes brush and elliptical maps in order");
    Check(encoder.scene().rampCount==1,"coordinate mutation reuses the same color ramp");
}
}
int main(){try{const auto n=CoordinateSamples();InvalidMaps();VelloPaintMaps();std::printf("PASS gradient transforms: %d inverse-coordinate/color samples; invalid maps; Vello box and focal ellipse\n",n);return 0;}
catch(const std::exception& e){std::fprintf(stderr,"FAIL gradient transforms: %s\n",e.what());return 1;}}
