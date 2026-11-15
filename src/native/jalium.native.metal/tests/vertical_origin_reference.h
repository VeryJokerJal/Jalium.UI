#pragma once
#import <CoreText/CoreText.h>
#include <cmath>
#include <functional>
#include <vector>

// Test-only independent default origin reader. It deliberately does not call
// the renderer's origin, gvar, table, or coordinate-normalization helpers.
namespace vertical_reference {
class Table {
    CFDataRef bytes_;
public:
    Table(CTFontRef font, uint32_t tag) : bytes_(CTFontCopyTable(font, tag, kCTFontTableOptionNoOptions)) {}
    ~Table() { if (bytes_) CFRelease(bytes_); }
    bool Has(size_t at, size_t length) const {
        size_t size=bytes_?CFDataGetLength(bytes_):0; return at<=size && length<=size-at;
    }
    uint16_t U16(size_t at) const { auto p=CFDataGetBytePtr(bytes_)+at; return (uint16_t(p[0])<<8)|p[1]; }
    int16_t S16(size_t at) const { return int16_t(U16(at)); }
    uint32_t U32(size_t at) const { return (uint32_t(U16(at))<<16)|U16(at+2); }
};
inline bool DefaultOrigin(CTFontRef font, CGGlyph glyph, double& pixels) {
    Table head(font,0x68656164),vhea(font,0x76686561),vmtx(font,0x766d7478),
        loca(font,0x6c6f6361),glyf(font,0x676c7966);
    if(!head.Has(0,54)||!vhea.Has(0,36))return false;
    unsigned metrics=vhea.U16(34);if(!metrics)return false;
    size_t bearing=glyph<metrics?size_t(glyph)*4+2:size_t(metrics)*4+size_t(glyph-metrics)*2;
    bool longLoca=head.S16(50)==1;size_t stride=longLoca?4:2;
    if(!vmtx.Has(bearing,2)||!loca.Has(size_t(glyph)*stride,stride*2))return false;
    size_t first=longLoca?loca.U32(size_t(glyph)*4):size_t(loca.U16(size_t(glyph)*2))*2;
    size_t last=longLoca?loca.U32(size_t(glyph+1)*4):size_t(loca.U16(size_t(glyph+1)*2))*2;
    if(last<first||!glyf.Has(first,last-first)||(last!=first&&last-first<10))return false;
    pixels=((first==last?0:glyf.S16(first+8))+vmtx.S16(bearing))*CTFontGetSize(font)/CTFontGetUnitsPerEm(font);
    return true;
}
inline CGGlyph FirstGlyph(CTLineRef line) {
    auto runs=CTLineGetGlyphRuns(line);
    for(CFIndex i=0;i<CFArrayGetCount(runs);++i){auto run=(CTRunRef)CFArrayGetValueAtIndex(runs,i);CGGlyph glyph=0;
        if(CTRunGetGlyphCount(run)){CTRunGetGlyphs(run,CFRangeMake(0,1),&glyph);return glyph;}}
    return 0;
}
// Retain CoreText's independent GPOS/mark shaping, but correct each supplied
// font origin explicitly instead of trusting CoreText's synthesized TSB.
inline void DrawVertical(CTLineRef line, CGContextRef context,
    const std::function<bool(CTFontRef,CGGlyph,double&)>& origin, bool attachedMark=false) {
    auto runs=CTLineGetGlyphRuns(line);
    auto pen=CGContextGetTextPosition(context);
    CGContextSaveGState(context);CGContextTranslateCTM(context,pen.x,pen.y);
    double baseShift=0;bool firstGlyph=true;
    for(CFIndex i=0;i<CFArrayGetCount(runs);++i){auto run=(CTRunRef)CFArrayGetValueAtIndex(runs,i);
        auto font=(CTFontRef)CFDictionaryGetValue(CTRunGetAttributes(run),kCTFontAttributeName);
        CFIndex count=CTRunGetGlyphCount(run);std::vector<CGGlyph> glyphs(count);std::vector<CGSize> translations(count);
        CTRunGetGlyphs(run,CFRangeMake(0,0),glyphs.data());CTFontGetVerticalTranslationsForGlyphs(font,glyphs.data(),translations.data(),count);
        for(CFIndex j=0;j<count;++j){double supplied=0,shift=origin(font,glyphs[j],supplied)?supplied+translations[j].height:0;
            if(attachedMark&&!firstGlyph)shift=baseShift;
            if(firstGlyph){baseShift=shift;firstGlyph=false;}
            CGContextSaveGState(context);CGContextTranslateCTM(context,shift,0);
            auto matrix=CTRunGetTextMatrix(run);matrix.tx=matrix.ty=0;CGContextSetTextMatrix(context,matrix);
            CTRunDraw(run,context,CFRangeMake(j,1));CGContextRestoreGState(context);
        }
    }
    CGContextRestoreGState(context);
}
}
