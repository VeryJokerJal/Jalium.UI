#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>

namespace jalium::css_blend {
struct Rgb {
    double r, g, b;
    double Min() const { return std::min({r, g, b}); }
    double Max() const { return std::max({r, g, b}); }
    double Lum() const { return .3*r + .59*g + .11*b; }
    Rgb operator+(double v) const { return {r+v, g+v, b+v}; }
    Rgb operator-(double v) const { return *this + -v; }
    Rgb operator*(double v) const { return {r*v, g*v, b*v}; }
};
inline Rgb SetLum(Rgb c, double l) {
    c = c + (l-c.Lum());
    double lum=c.Lum(), n=c.Min(), x=c.Max();
    if(n<0) c=(c-lum)*(lum/(lum-n))+lum;
    if(x>1) c=(c-lum)*((1-lum)/(x-lum))+lum;
    return c;
}
inline Rgb SetSat(Rgb c, double s) {
    double n=c.Min(), x=c.Max();
    return x==n ? Rgb{} : (c-n)*(s/(x-n));
}
inline double Channel(double b, double s, uint32_t mode) {
    switch(mode) {
    case 1: return b*s;
    case 2: return b+s-b*s;
    case 3: return b<=.5 ? 2*b*s : 1-2*(1-b)*(1-s);
    case 4: return std::min(b,s);
    case 5: return std::max(b,s);
    case 6: return b==0 ? 0 : s==1 ? 1 : std::min(1.,b/(1-s));
    case 7: return b==1 ? 1 : s==0 ? 0 : 1-std::min(1.,(1-b)/s);
    case 8: return s<=.5 ? 2*b*s : 1-2*(1-b)*(1-s);
    case 9: return s<=.5 ? b-(1-2*s)*b*(1-b)
        : b+(2*s-1)*((b<=.25 ? ((16*b-12)*b+4)*b : std::sqrt(b))-b);
    case 10: return std::abs(b-s);
    case 11: return b+s-2*b*s;
    default: return s;
    }
}
// Framebuffers use straight BGRA. Blend in sRGB before source-over; apply the
// group's opacity and destination clip to source alpha exactly once.
inline void Composite(const uint8_t* source, uint8_t* backdrop, uint32_t mode, double opacity) {
    const double sa=source[3]/255.*std::clamp(opacity,0.,1.), ba=backdrop[3]/255.;
    const Rgb s{source[2]/255.,source[1]/255.,source[0]/255.};
    const Rgb b{backdrop[2]/255.,backdrop[1]/255.,backdrop[0]/255.};
    double alpha; Rgb result;
    if(mode==16) {
        alpha=std::min(1.,sa+ba);
        result={std::min(1.,sa*s.r+ba*b.r),std::min(1.,sa*s.g+ba*b.g),std::min(1.,sa*s.b+ba*b.b)};
    } else {
        alpha=sa+ba*(1-sa);
        Rgb mixed;
        switch(mode) {
        case 12: mixed=SetLum(SetSat(s,b.Max()-b.Min()),b.Lum()); break;
        case 13: mixed=SetLum(SetSat(b,s.Max()-s.Min()),b.Lum()); break;
        case 14: mixed=SetLum(s,b.Lum()); break;
        case 15: mixed=SetLum(b,s.Lum()); break;
        default: mixed={Channel(b.r,s.r,mode),Channel(b.g,s.g,mode),Channel(b.b,s.b,mode)};
        }
        auto part=[&](double cs,double cb,double blend){return sa*(1-ba)*cs+sa*ba*blend+(1-sa)*ba*cb;};
        result={part(s.r,b.r,mixed.r),part(s.g,b.g,mixed.g),part(s.b,b.b,mixed.b)};
    }
    auto byte=[](double c){return uint8_t(std::clamp(std::lround(c*255),0l,255l));};
    backdrop[0]=alpha>0 ? byte(result.b/alpha) : 0;
    backdrop[1]=alpha>0 ? byte(result.g/alpha) : 0;
    backdrop[2]=alpha>0 ? byte(result.r/alpha) : 0;
    backdrop[3]=byte(alpha);
}
}
