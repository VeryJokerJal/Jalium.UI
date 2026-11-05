#include "jalium_impeller_stroke.h"
#include "jalium_scanline_rasterizer.h"
#include <array>
#include <cstdio>
#include <stdexcept>

namespace {
struct Vertex { float x,y,r,g,b,a; };
void Check(bool value,const char* message) { if(!value)throw std::runtime_error(message); }
int DenseCurve(const std::array<double,6>& m,int dpi) {
    constexpr double pi=3.14159265358979323846;
    std::vector<float> points;
    for(int i=0;i<=512;i++) {
        const double angle=pi*.25*i/512;
        points.insert(points.end(),{float(32+20*std::cos(angle)),float(32+20*std::sin(angle))});
    }
    std::vector<Vertex> vertices;std::vector<uint32_t> indices;std::vector<jalium::Contour> contours;
    jalium::ExpandStrokePath(vertices,indices,points.data(),513,8,jalium::ImpellerJoin::Miter,4,
        jalium::ImpellerCap::Butt,false,1,0,0,1,&contours);
    for(auto& contour:contours)for(size_t i=0;i<contour.points.size();i+=2) {
        const double x=contour.points[i],y=contour.points[i+1];
        contour.points[i]=float((m[0]*x+m[2]*y+m[4])*dpi);
        contour.points[i+1]=float((m[1]*x+m[3]*y+m[5])*dpi);
    }
    std::vector<jalium::PixelRect> rects;
    jalium::RasterizePathToRects(contours,jalium::FillRule::NonZero,rects);
    const double det=m[0]*m[3]-m[1]*m[2];int stable=0,bad=0;
    auto inside=[&](double px,double py) {
        const double x=px/dpi-m[4],y=py/dpi-m[5];
        const double u=(m[3]*x-m[2]*y)/det-32,v=(-m[1]*x+m[0]*y)/det-32;
        const double radius=std::hypot(u,v),angle=std::atan2(v,u);
        return radius>16&&radius<24&&angle>0&&angle<pi*.25;
    };
    for(int y=0;y<128*dpi;y++)for(int x=0;x<128*dpi;x++) {
        bool full=true;
        for(double dy:{-.9,0.,.9})for(double dx:{-.9,0.,.9})full&=inside(x+.5+dx,y+.5+dy);
        if(!full)continue;
        ++stable;float alpha=0;
        for(const auto& r:rects)if(x>=r.x&&x<r.x+r.w&&y>=r.y&&y<r.y+r.h)alpha=std::max(alpha,r.alpha);
        bad+=alpha<253.f/255;
    }
    if(bad)std::fprintf(stderr,"Dense miter: DPI %d, %d/%d stable pixels contain gaps\n",dpi,bad,stable);
    Check(stable>10,"Independent annular-sector reference has no stable interior");
    Check(bad==0,"Shallow miter joins must fill the curved stroke interior");
    return stable;
}
}
int main() {
    try {
        int stable=0,cases=0;
        const std::array<std::array<double,6>,4> matrices={{{1,0,0,1,8,8},{.8,.2,-.2,.8,24,8},{1,.15,.2,1,0,0},{-1,0,0,1,100,8}}};
        for(int dpi:{1,2})for(const auto& m:matrices){stable+=DenseCurve(m,dpi);++cases;}
        std::printf("PASS dense stroke contours: %d affine/DPI cases, %d independent stable pixels\n",cases,stable);
        return 0;
    } catch(const std::exception& error) {std::fprintf(stderr,"FAIL %s\n",error.what());return 1;}
}
