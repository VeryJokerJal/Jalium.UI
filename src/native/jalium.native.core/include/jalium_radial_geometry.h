#pragma once
#include "jalium_types.h"
#include <algorithm>
#include <cmath>
#include <limits>

namespace jalium {

// Additive side interface: old Brush factories/vtables retain their ABI.
class RadialGradientGeometryProvider {
public:
    virtual JaliumResult SetRadialGeometry(float focalRadius, uint32_t flags) = 0;
protected:
    ~RadialGradientGeometryProvider() = default;
};

class RadialGradientGeometryState : public RadialGradientGeometryProvider {
public:
    float focalRadius = 0;
    uint32_t radialFlags = 0; // bit0=SVG2, bit1=boundary spread, bit2=linear/tangent family
    JaliumResult SetRadialGeometry(float radius, uint32_t flags) override {
        if (!std::isfinite(radius) || radius < 0 ||
            (flags != 0 && flags != 1 && flags != 3 && flags != 5) ||
            ((flags == 0 || flags == 3) && radius != 0)) return JALIUM_ERROR_INVALID_ARGUMENT;
        focalRadius = radius; radialFlags = flags; return JALIUM_OK;
    }
    template<class T> void CopyRadialGeometry(T& target) const {
        target.focalRadius = focalRadius; target.radialFlags = radialFlags;
    }
};

// Canvas circle-family solve; infinity denotes SVG's boundary spread limit.
// Double intermediates avoid cancellation after user-space float coordinates.
inline double SvgRadialParameter(double px, double py, double cx, double cy,
    double ox, double oy, double rx, double ry, double fr, uint32_t flags)
{
    rx=std::fabs(rx); ry=std::fabs(ry);
    if ((rx==0)!=(ry==0)) return std::numeric_limits<double>::quiet_NaN();
    const double sx=rx>0?rx:std::max({1.0,fr,std::fabs(ox-cx),std::fabs(oy-cy)});
    const double sy=ry>0?ry:sx;
    const double fx=(ox-cx)/sx,fy=(oy-cy)/sy;
    const double start=fr/sx,dr=(rx>0?1.0:0.0)-start;
    if (fx==0 && fy==0 && dr==0) return std::numeric_limits<double>::quiet_NaN();
    const bool boundary=(flags&2)!=0;
    const double a=(flags&6)?0:fx*fx+fy*fy-dr*dr;
    if(a<0){
        const double limit=-fr/(rx-fr);
        const double ax=ox+limit*(cx-ox),ay=oy+limit*(cy-oy);
        const double tolerance=8*std::numeric_limits<float>::epsilon()*std::max({1.0,std::fabs(ax),std::fabs(ay),rx,ry});
        if(std::fabs(px-ax)<=tolerance&&std::fabs(py-ay)<=tolerance)
            return dr<0?double(std::nextafter(float(limit),-std::numeric_limits<float>::infinity())):limit;
    }
    const double qx=(px-cx)/sx-fx,qy=(py-cy)/sy-fy;
    const double b=2*(qx*fx+qy*fy-start*dr),c=qx*qx+qy*qy-start*start;
    double result=std::numeric_limits<double>::quiet_NaN();
    auto accept=[&](double t){if(std::isfinite(t)&&start+t*dr>0&&(std::isnan(result)||t>result))result=t;};
    if(a==0){if(b!=0)accept(-c/b);}
    else{
        const double discriminant=b*b-4*a*c;
        if(discriminant>=0){
            const double q=-.5*(b+std::copysign(std::sqrt(discriminant),b));
            accept(q/a);if(q!=0)accept(c/q);
        }
    }
    return std::isnan(result)&&boundary?std::numeric_limits<double>::infinity():result;
}

// Stops must be sorted; integrate straight RGBA in the interpolation space.
template<class Stop> inline void AverageGradientStops(const Stop* stops, uint32_t count,
    uint32_t mode, float& red, float& green, float& blue, float& alpha)
{
    double sum[4]={};
    auto decode=[&](double v){return mode==0?(v<=.04045?v/12.92:std::pow((v+.055)/1.055,2.4)):v;};
    auto accumulate=[&](const Stop& a,const Stop& b,double start,double end){
        const double left=std::max(start,0.0),right=std::min(end,1.0);
        if(right<=left)return;
        const double u=end>start?((left+right)*.5-start)/(end-start):0;
        const double av[]={decode(a.r),decode(a.g),decode(a.b),a.a};
        const double bv[]={decode(b.r),decode(b.g),decode(b.b),b.a};
        for(int k=0;k<4;++k)sum[k]+=(av[k]+(bv[k]-av[k])*u)*(right-left);
    };
    if(count){
        accumulate(stops[0],stops[0],0,std::max(double(stops[0].position),0.0));
        for(uint32_t i=1;i<count;++i)accumulate(stops[i-1],stops[i],stops[i-1].position,stops[i].position);
        accumulate(stops[count-1],stops[count-1],std::min(double(stops[count-1].position),1.0),1);
    }
    auto encode=[&](double v){return float(std::clamp(mode==0?(v<=.0031308?v*12.92:1.055*std::pow(v,1/2.4)-.055):v,0.0,1.0));};
    red=encode(sum[0]);green=encode(sum[1]);blue=encode(sum[2]);alpha=float(std::clamp(sum[3],0.0,1.0));
}
}
