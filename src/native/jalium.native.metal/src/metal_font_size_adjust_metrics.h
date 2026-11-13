#pragma once
#ifdef __APPLE__
#include <CoreText/CoreText.h>
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <vector>
#include "opentype_gvar_metrics.h"
namespace jalium::font_size_adjust {
// CoreText's advance API reports horizontal widths even for vertical orientation
// on some fonts. Read the defined vertical advance, including variable deltas.
struct Table {
    CFDataRef data=nullptr;
    Table(CTFontRef font,uint32_t tag):data(CTFontCopyTable(font,tag,0)){}
    ~Table(){if(data)CFRelease(data);}
    Table(const Table&)=delete;Table& operator=(const Table&)=delete;
    bool Has(size_t at,size_t count) const {return data&&at<=static_cast<size_t>(CFDataGetLength(data))&&count<=static_cast<size_t>(CFDataGetLength(data))-at;}
    uint8_t U8(size_t at) const{return Has(at,1)?CFDataGetBytePtr(data)[at]:0;}
    uint16_t U16(size_t at) const{return Has(at,2)?static_cast<uint16_t>((uint16_t(U8(at))<<8)|U8(at+1)):0;}
    int16_t S16(size_t at) const{return static_cast<int16_t>(U16(at));}
    uint32_t U32(size_t at) const{return Has(at,4)?(uint32_t(U16(at))<<16)|U16(at+2):0;}
    int32_t S32(size_t at) const{return static_cast<int32_t>(U32(at));}
    font::ByteReader Reader() const {return {data?CFDataGetBytePtr(data):nullptr,data?static_cast<size_t>(CFDataGetLength(data)):0};}
};
inline std::pair<uint32_t,uint32_t> DeltaIndex(const Table& t,size_t map,uint32_t glyph){
    if(!map)return {0,glyph};if(!t.Has(map,4)||t.U8(map)>1)return {0,0};
    uint32_t count=t.U8(map)==0?t.U16(map+2):t.U32(map+2);if(!count)return {0,0};
    unsigned bytes=((t.U8(map+1)>>4)&3)+1,bits=(t.U8(map+1)&15)+1;
    size_t at=map+(t.U8(map)==0?4:6)+size_t(std::min(glyph,count-1))*bytes;if(!t.Has(at,bytes))return {0,0};
    uint32_t value=0;for(unsigned i=0;i<bytes;++i)value=(value<<8)|t.U8(at+i);
    return {value>>bits,value&((uint32_t(1)<<bits)-1)};
}
inline double VariationDelta(const Table& t,size_t store,uint32_t outer,uint32_t inner,const std::vector<double>& coordinates){
    if(!store||!t.Has(store,8)||t.U16(store)!=1||outer>=t.U16(store+6)||!t.Has(store+8+size_t(outer)*4,4))return 0;
    size_t region=store+t.U32(store+2),data=store+t.U32(store+8+size_t(outer)*4);
    if(!t.Has(region,4)||!t.Has(data,6)||inner>=t.U16(data))return 0;
    unsigned axes=t.U16(region),regionCount=t.U16(region+2),indices=t.U16(data+4),wordField=t.U16(data+2),words=wordField&0x7fff;bool longWords=(wordField&0x8000)!=0;
    if(axes!=coordinates.size()||axes>256||indices>16384||words>indices||!t.Has(data+6,size_t(indices)*2)||!t.Has(region+4,size_t(regionCount)*axes*6))return 0;
    size_t stride=size_t(words)*(longWords?4:2)+size_t(indices-words)*(longWords?2:1),at=data+6+size_t(indices)*2+size_t(inner)*stride;
    if(!t.Has(at,stride))return 0;double result=0;
    for(unsigned i=0;i<indices;++i){
        int32_t delta=0;if(i<words){delta=longWords?t.S32(at):t.S16(at);at+=longWords?4:2;}else{delta=longWords?t.S16(at):static_cast<int8_t>(t.U8(at));at+=longWords?2:1;}
        unsigned index=t.U16(data+6+size_t(i)*2);if(index>=regionCount)return 0;double scalar=1;
        for(unsigned axis=0;axis<axes;++axis){size_t r=region+4+(size_t(index)*axes+axis)*6;double start=t.S16(r)/16384.0,peak=t.S16(r+2)/16384.0,end=t.S16(r+4)/16384.0,coordinate=coordinates[axis];
            if(peak==0||start>peak||peak>end||(start<0&&end>0))continue;
            if(coordinate<start||coordinate>end){scalar=0;break;}
            if(coordinate<peak)scalar*=peak==start?1:(coordinate-start)/(peak-start);
            else if(coordinate>peak)scalar*=peak==end?1:(end-coordinate)/(end-peak);
        }
        result+=delta*scalar;
    }
    return result;
}
inline std::vector<double> Coordinates(CTFontRef font){
    Table fvar(font,0x66766172),avar(font,0x61766172);if(!fvar.Has(0,16))return {};
    unsigned count=fvar.U16(8),size=fvar.U16(10);size_t start=fvar.U16(4);if(count>256||size<20||!fvar.Has(start,size_t(count)*size))return {};
    auto current=CTFontCopyVariation(font);std::vector<double> values(count);
    for(unsigned i=0;i<count;++i){size_t at=start+size_t(i)*size;int32_t tag=static_cast<int32_t>(fvar.U32(at));double low=fvar.S32(at+4)/65536.0,normal=fvar.S32(at+8)/65536.0,high=fvar.S32(at+12)/65536.0,value=normal;
        auto key=CFNumberCreate(kCFAllocatorDefault,kCFNumberSInt32Type,&tag);auto number=current&&key?static_cast<CFNumberRef>(CFDictionaryGetValue(current,key)):nullptr;if(number)CFNumberGetValue(number,kCFNumberDoubleType,&value);if(key)CFRelease(key);
        values[i]=std::clamp(value<normal?(normal>low?(value-normal)/(normal-low):0):(high>normal?(value-normal)/(high-normal):0),-1.0,1.0);
    }
    if(current)CFRelease(current);
    if(!avar.Has(0,8)||avar.U16(6)!=count)return values;size_t at=8;
    for(unsigned i=0;i<count;++i){if(!avar.Has(at,2))return values;unsigned pairs=avar.U16(at);at+=2;if(pairs>4096||!avar.Has(at,size_t(pairs)*4))return values;
        for(unsigned j=1;j<pairs;++j){double a=avar.S16(at+(j-1)*4)/16384.0,b=avar.S16(at+j*4)/16384.0;if(values[i]>=a&&values[i]<=b&&b>a){double x=avar.S16(at+(j-1)*4+2)/16384.0,y=avar.S16(at+j*4+2)/16384.0;values[i]=x+(y-x)*(values[i]-a)/(b-a);break;}}
        at+=size_t(pairs)*4;
    }
    if(avar.U16(0)==2&&avar.Has(at,8)){size_t map=avar.U32(at),store=avar.U32(at+4);auto before=values;
        for(unsigned i=0;i<count;++i){auto index=DeltaIndex(avar,map,i);values[i]=std::clamp(before[i]+VariationDelta(avar,store,index.first,index.second,before)/16384.0,-1.0,1.0);}}
    return values;
}
inline bool TryVerticalAspect(CTFontRef font,CGGlyph glyph,double& aspect){
    Table vhea(font,0x76686561),vmtx(font,0x766d7478),vvar(font,0x56564152);
    if(!vhea.Has(0,36))return false;unsigned count=vhea.U16(34);if(!count||!vmtx.Has(size_t(std::min<unsigned>(glyph,count-1))*4,2))return false;
    double advance=vmtx.U16(size_t(std::min<unsigned>(glyph,count-1))*4);
    if(vvar.Has(0,12)&&vvar.U16(0)==1){auto index=DeltaIndex(vvar,vvar.U32(8),glyph);advance+=VariationDelta(vvar,vvar.U32(4),index.first,index.second,Coordinates(font));}
    else {
        Table gvar(font,0x67766172);
        if(gvar.Has(0,20)) {
            Table glyf(font,0x676c7966),loca(font,0x6c6f6361),head(font,0x68656164),maxp(font,0x6d617870);
            jalium::font::GvarVerticalMetrics metrics(gvar.Reader(),glyf.Reader(),loca.Reader(),head.Reader(),maxp.Reader(),vhea.Reader(),vmtx.Reader());
            double varied=0;if(metrics.Advance(glyph,Coordinates(font),varied))advance=varied;
        }
    }
    unsigned units=CTFontGetUnitsPerEm(font);if(!units||!std::isfinite(advance)||advance<0)return false;
    aspect=advance/units;return true;
}
// Keep font tables and normalized coordinates once per paragraph/font. Origin
// lookup is independent of advance lookup: a VVAR font can vary its TSB or
// vertical origin through different optional mappings.
class VerticalOriginMetrics {
    CTFontRef font_;
    Table vhea_,vmtx_,vvar_,glyf_,loca_,head_,maxp_,gvar_,vorg_;
    std::vector<double> coordinates_;
    font::GvarVerticalMetrics metrics_;
    double size_,units_;
    bool TopBearing(CGGlyph glyph,double& value) const {
        if(!vhea_.Has(0,36))return false;
        unsigned count=vhea_.U16(34);if(!count)return false;
        size_t at=glyph<count?size_t(glyph)*4+2:size_t(count)*4+size_t(glyph-count)*2;
        if(!vmtx_.Has(at,2))return false;value=vmtx_.S16(at);return true;
    }
    bool OutlineTop(CGGlyph glyph,double& value) const {
        auto path=CTFontCreatePathForGlyph(font_,glyph,nullptr);
        if(!path){value=0;return true;}
        auto matrix=CTFontGetMatrix(font_);double determinant=matrix.a*matrix.d-matrix.b*matrix.c;
        if(!std::isfinite(determinant)||std::abs(determinant)<1e-20){CGPathRelease(path);return false;}
        auto inverse=CGAffineTransformInvert(matrix);auto plain=CGPathCreateCopyByTransformingPath(path,&inverse);
        auto bounds=CGPathGetPathBoundingBox(plain?plain:path);
        value=CGRectIsNull(bounds)?0:CGRectGetMaxY(bounds)*units_/size_;
        if(plain)CGPathRelease(plain);CGPathRelease(path);return std::isfinite(value);
    }
    bool Vorg(CGGlyph glyph,double& value) const {
        if(!vorg_.Has(0,8)||vorg_.U16(0)!=1||vorg_.U16(2)!=0)return false;
        unsigned count=vorg_.U16(6);if(!vorg_.Has(8,size_t(count)*4))return false;
        value=vorg_.S16(4);unsigned first=0,last=count;
        while(first<last){unsigned middle=first+(last-first)/2;unsigned id=vorg_.U16(8+size_t(middle)*4);
            if(id<glyph)first=middle+1;else last=middle;}
        if(first<count&&vorg_.U16(8+size_t(first)*4)==glyph)value=vorg_.S16(10+size_t(first)*4);
        return true;
    }
public:
    explicit VerticalOriginMetrics(CTFontRef font):font_(font),vhea_(font,0x76686561),vmtx_(font,0x766d7478),
        vvar_(font,0x56564152),glyf_(font,0x676c7966),loca_(font,0x6c6f6361),head_(font,0x68656164),
        maxp_(font,0x6d617870),gvar_(font,0x67766172),vorg_(font,0x564f5247),coordinates_(Coordinates(font)),
        metrics_(gvar_.Reader(),glyf_.Reader(),loca_.Reader(),head_.Reader(),maxp_.Reader(),vhea_.Reader(),vmtx_.Reader()),
        size_(CTFontGetSize(font)),units_(CTFontGetUnitsPerEm(font)) {}
    bool OriginY(CGGlyph glyph,double& value) const {
        if(!(size_>0)||!(units_>0)||!maxp_.Has(0,6)||glyph>=maxp_.U16(4))return false;
        double bearing=0;if(!TopBearing(glyph,bearing))return false;
        double origin=0;
        const bool trueType=glyf_.data!=nullptr;
        if(trueType){if(!metrics_.BaseOrigin(glyph,origin))return false;}
        else if(!Vorg(glyph,origin)){double top=0;if(!OutlineTop(glyph,top))return false;origin=top+bearing;}
        bool mapped=false;
        if(vvar_.Has(0,24)&&vvar_.U16(0)==1) {
            // vOrg mappings are CFF2-only. A TrueType font must ignore one
            // even when it also supplies a valid independent TSB mapping.
            if(size_t map=trueType?0:vvar_.U32(20)) {
                auto index=DeltaIndex(vvar_,map,glyph);origin+=VariationDelta(vvar_,vvar_.U32(4),index.first,index.second,coordinates_);mapped=true;
            } else if(size_t map=vvar_.U32(12)) {
                double top=0;if(!OutlineTop(glyph,top))return false;
                auto index=DeltaIndex(vvar_,map,glyph);origin=top+bearing+VariationDelta(vvar_,vvar_.U32(4),index.first,index.second,coordinates_);mapped=true;
            }
        }
        if(!mapped&&trueType&&gvar_.Has(0,20)) {
            double advance=0,varied=0;if(metrics_.Metrics(glyph,coordinates_,advance,varied))origin=varied;
        }
        value=origin*size_/units_;return std::isfinite(value);
    }
};
inline bool TryVerticalOriginY(CTFontRef font,CGGlyph glyph,double& value) {
    return VerticalOriginMetrics(font).OriginY(glyph,value);
}
inline double VerticalAspect(CTFontRef font,CGGlyph glyph){
    double aspect=0;TryVerticalAspect(font,glyph,aspect);return aspect;
}
inline bool HasHeight(CTFontRef font,bool cap){
    Table os2(font,0x4f532f32);size_t at=cap?88:86;if(os2.Has(at,2)&&os2.U16(0)>=2&&os2.S16(at)>0)return true;
    UniChar character=cap?'H':'x';CGGlyph glyph=0;return CTFontGetGlyphsForCharacters(font,&character,&glyph,1)&&glyph!=0;
}
} // namespace jalium::font_size_adjust
#endif
