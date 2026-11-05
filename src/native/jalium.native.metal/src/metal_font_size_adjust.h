#pragma once
#include "metal_font_visibility.h"
#include "metal_font_size_adjust_metrics.h"
#include "metal_font_line_metrics.h"
#include <cmath>
#include <cstring>
#include <memory>
#include <limits>
#ifdef __APPLE__
namespace jalium {
namespace font_size_adjust {
inline JaliumFontFaceMetrics InitialMetrics() {
    JaliumFontFaceMetrics value{};value.structSize=sizeof(value);value.sizeAdjust=1;
    value.ascent=value.descent=value.lineGap=value.verticalAscent=value.verticalDescent=value.verticalLineGap=-1;return value;
}
inline bool IsInitial(const JaliumFontFaceMetrics& value) {
    return value.sizeAdjust==1&&value.ascent<0&&value.descent<0&&value.lineGap<0&&value.verticalAscent<0&&value.verticalDescent<0&&value.verticalLineGap<0;
}
inline bool ValidMetrics(const JaliumFontFaceMetrics& value) {
    if(value.structSize!=sizeof(JaliumFontFaceMetrics)||!std::isfinite(value.sizeAdjust)||value.sizeAdjust<0)return false;
    for(double item:{value.ascent,value.descent,value.lineGap,value.verticalAscent,value.verticalDescent,value.verticalLineGap})
        if(!std::isfinite(item)||(item<0&&item!=-1))return false;
    return true;
}
inline double BoundSize(long double value) { return static_cast<double>(std::clamp(value,0.0L,1048576.0L)); }
inline CFStringRef FaceKey() { return CFSTR("JaliumFaceMetricDescriptors"); }
inline bool ReadMetrics(const void* value,JaliumFontFaceMetrics& metrics) {
    if(!value||CFGetTypeID(value)!=CFDataGetTypeID())return false;auto data=static_cast<CFDataRef>(value);
    if(CFDataGetLength(data)!=sizeof(metrics))return false;std::memcpy(&metrics,CFDataGetBytePtr(data),sizeof(metrics));return ValidMetrics(metrics);
}
inline JaliumFontFaceMetrics MetricsFor(CFDictionaryRef settings,CTFontRef font) {
    JaliumFontFaceMetrics metrics=InitialMetrics();
    auto tag=CTFontCopyAttribute(font,FaceKey());bool tagged=ReadMetrics(tag,metrics);if(tag)CFRelease(tag);
    if(tagged)return metrics;
    auto fonts=static_cast<CFArrayRef>(CFDictionaryGetValue(settings,CFSTR("fonts")));
    auto values=static_cast<CFArrayRef>(CFDictionaryGetValue(settings,CFSTR("descriptors")));
    if(fonts&&values&&CFArrayGetCount(fonts)==CFArrayGetCount(values))for(CFIndex i=0;i<CFArrayGetCount(fonts);++i)
        if(CFEqual(CFArrayGetValueAtIndex(fonts,i),font)&&ReadMetrics(CFArrayGetValueAtIndex(values,i),metrics))return metrics;
    return InitialMetrics();
}
// A synthetic capital can require a different face. Keep the authored cascade
// and its Unicode restrictions when CoreText loses the face descriptor while
// choosing that capital; do not change emoji presentation fallbacks.
inline CTFontRef CapsFallback(CFDictionaryRef settings,CFDictionaryRef attributes,
    CTFontRef selected,CFStringRef text,CFRange range) {
    if(!CFDictionaryGetValue(attributes,CFSTR("JaliumCapsBaseFont")))return selected;
    auto fonts=static_cast<CFArrayRef>(CFDictionaryGetValue(settings,CFSTR("fonts")));
    auto coverage=static_cast<CFArrayRef>(CFDictionaryGetValue(settings,CFSTR("coverage")));
    if(!fonts||!coverage||CFArrayGetCount(fonts)!=CFArrayGetCount(coverage))return selected;
    auto sample=CFStringCreateWithSubstring(kCFAllocatorDefault,text,range);if(!sample)return selected;
    for(CFIndex i=0;i<CFArrayGetCount(fonts);++i) {
        auto characters=static_cast<CFCharacterSetRef>(CFArrayGetValueAtIndex(coverage,i));
        auto candidate=static_cast<CTFontRef>(CFArrayGetValueAtIndex(fonts,i));
        if(CharacterSetContainsString(characters,sample)&&FontContainsString(candidate,sample)) {
            CFRelease(sample);return candidate;
        }
    }
    CFRelease(sample);return selected;
}
inline CFStringRef Key() { return CFSTR("JaliumFontSizeAdjustment"); }
inline CFStringRef FinalKey() { return CFSTR("JaliumFontSizeAdjustmentFinal"); }
inline CFStringRef BaseKey() { return CFSTR("JaliumFontSizeAdjustmentBase"); }
struct Policy {
    uint32_t metric = 0, optical = 0;
    double aspect = 0, size = 0;
    uint32_t axisCount = 0, reserved = 0;
};
inline double Aspect(CTFontRef font, uint32_t metric) {
    if (!font || metric < 1 || metric > 5) return 0;
    double size = CTFontGetSize(font), value = 0;
    if (!(size > 0)) return 0;
    if (metric == 1) { if(!HasHeight(font,false))return 0;value = CTFontGetXHeight(font); }
    else if (metric == 2) { if(!HasHeight(font,true))return 0;value = CTFontGetCapHeight(font); }
    else {
        UniChar character = metric == 3 ? '0' : 0x6c34; CGGlyph glyph = 0;
        if (!CTFontGetGlyphsForCharacters(font, &character, &glyph, 1) || !glyph) return 0;
        if(metric==5)return VerticalAspect(font,glyph);
        CGSize advance{};
        CTFontGetAdvancesForGlyphs(font, metric == 5 ? kCTFontOrientationVertical : kCTFontOrientationHorizontal, &glyph, &advance, 1);
        value = metric == 5 ? std::abs(advance.height) : advance.width;
    }
    return std::isfinite(value) && value > 0 ? value / size : 0;
}

inline CTFontRef AtSize(CTFontRef font, double size, uint32_t optical, const std::vector<JaliumFontVariation>& axes, bool known, bool automatic, bool privateFace = false) {
    auto release=[](const void* p){if(p) CFRelease(p);}; using Owner=std::unique_ptr<const void,decltype(release)>;
    Owner descriptor(CTFontCopyFontDescriptor(font),release), graphics(CTFontCopyGraphicsFont(font,nullptr),release);
    Owner old(CTFontCopyVariation(font),release);
    Owner coordinates(old ? CFDictionaryCreateMutableCopy(kCFAllocatorDefault,0,static_cast<CFDictionaryRef>(old.get())) : CFDictionaryCreateMutable(kCFAllocatorDefault,0,&kCFTypeDictionaryKeyCallBacks,&kCFTypeDictionaryValueCallBacks),release);
    Owner supported(CTFontCopyVariationAxes(font),release);
    if(!coordinates || !descriptor || !graphics) return nullptr;
    auto values=const_cast<CFMutableDictionaryRef>(static_cast<CFDictionaryRef>(coordinates.get()));
    Owner opticalValue(CTFontCopyAttribute(font,kCTFontOpticalSizeAttribute),release);
    Owner originalOptical(opticalValue?CFRetain(opticalValue.get()):nullptr,release);
    for(CFIndex i=0;supported && i<CFArrayGetCount(static_cast<CFArrayRef>(supported.get()));++i){
        auto axis=static_cast<CFDictionaryRef>(CFArrayGetValueAtIndex(static_cast<CFArrayRef>(supported.get()),i));
        auto key=static_cast<CFNumberRef>(CFDictionaryGetValue(axis,kCTFontVariationAxisIdentifierKey));int64_t tag=0;
        if(!key || !CFNumberGetValue(key,kCFNumberSInt64Type,&tag)) continue;
        auto number=[&](CFStringRef k){double v=0;auto n=static_cast<CFNumberRef>(CFDictionaryGetValue(axis,k));if(n)CFNumberGetValue(n,kCFNumberDoubleType,&v);return v;};
        double value=number(kCTFontVariationAxisDefaultValueKey);
        if(auto n=static_cast<CFNumberRef>(CFDictionaryGetValue(values,key)))CFNumberGetValue(n,kCFNumberDoubleType,&value);
        bool change=false;
        if(tag==0x6f70737a && (known ? automatic : optical!=0)) { value=(known || optical==1)?size:number(kCTFontVariationAxisDefaultValueKey);change=true; }
        if(!known)for(const auto& requested:axes)if(requested.tag==static_cast<uint32_t>(tag)){value=requested.value;change=true;}
        if(!change) continue;
        value=std::clamp(value,number(kCTFontVariationAxisMinimumValueKey),number(kCTFontVariationAxisMaximumValueKey));
        Owner coordinate(CFNumberCreate(kCFAllocatorDefault,kCFNumberDoubleType,&value),release);if(!coordinate)return nullptr;
        CFDictionarySetValue(values,key,coordinate.get());if(tag==0x6f70737a)opticalValue.reset(CFRetain(coordinate.get()));
    }
    const void* keys[]={kCTFontVariationAttribute,kCTFontOpticalSizeAttribute};const void* items[]={coordinates.get(),opticalValue?opticalValue.get():kCFNull};
    Owner attributes(CFDictionaryCreate(kCFAllocatorDefault,keys,items,2,&kCFTypeDictionaryKeyCallBacks,&kCFTypeDictionaryValueCallBacks),release);
    Owner next(attributes?CTFontDescriptorCreateCopyWithAttributes(static_cast<CTFontDescriptorRef>(descriptor.get()),static_cast<CFDictionaryRef>(attributes.get())):nullptr,release);
    auto matrix=CTFontGetMatrix(font);
    // Retain identical private instances and apply changes to their live
    // identity instead of rematching a named-instance descriptor.
    if(privateFace && supported && CFArrayGetCount(static_cast<CFArrayRef>(supported.get()))) {
        bool sameAxes=old?CFEqual(old.get(),coordinates.get()):CFDictionaryGetCount(values)==0;
        bool sameOptical=originalOptical&&opticalValue?CFEqual(originalOptical.get(),opticalValue.get()):!originalOptical&&!opticalValue;
        if(size==CTFontGetSize(font)&&sameAxes&&sameOptical)return static_cast<CTFontRef>(CFRetain(font));
        Owner instance(attributes?CTFontDescriptorCreateWithAttributes(static_cast<CFDictionaryRef>(attributes.get())):nullptr,release);
        return instance?CTFontCreateCopyWithAttributes(font,size,&matrix,static_cast<CTFontDescriptorRef>(instance.get())):nullptr;
    }
    return next?CTFontCreateWithGraphicsFont(static_cast<CGFontRef>(const_cast<void*>(graphics.get())),size,&matrix,static_cast<CTFontDescriptorRef>(next.get())):nullptr;
}

inline CTFontRef Copy(CTFontRef font,const Policy& policy,const std::vector<JaliumFontVariation>& axes,bool known,bool automatic,
    double* used=nullptr,const JaliumFontFaceMetrics& descriptor=InitialMetrics(),bool privateFace=false) {
    double initial=BoundSize(static_cast<long double>(policy.size)*descriptor.sizeAdjust);
    auto base=AtSize(font,initial==0?1e-20:initial,policy.optical,axes,known,automatic,privateFace);if(!base)return nullptr;
    double aspect=policy.metric&&initial>0?Aspect(base,policy.metric):0;
    if(!(aspect>0)) { if(used)*used=initial;return base; }
    // The descriptor scales the aspect as well as the glyphs, so its
    // multiplier cancels when the element normalizes this selected metric.
    double size=BoundSize(static_cast<long double>(policy.size)*policy.aspect/aspect);
    auto result=AtSize(base,size==0?1e-20:size,policy.optical,axes,known,automatic,privateFace);CFRelease(base);
    if(used)*used=size;return result;
}

inline bool Read(CFDictionaryRef dictionary,Policy& policy,std::vector<JaliumFontVariation>& axes){
    auto data=static_cast<CFDataRef>(CFDictionaryGetValue(dictionary,CFSTR("policy")));
    if(!data || CFDataGetLength(data)<static_cast<CFIndex>(sizeof(policy)))return false;
    std::memcpy(&policy,CFDataGetBytePtr(data),sizeof(policy));
    if(policy.metric>5||policy.optical>2||policy.axisCount>16384||!(policy.size>0)||!std::isfinite(policy.size)||policy.aspect<0||!std::isfinite(policy.aspect)||
        CFDataGetLength(data)!=static_cast<CFIndex>(sizeof(policy)+policy.axisCount*sizeof(JaliumFontVariation)))return false;
    axes.resize(policy.axisCount);if(!axes.empty())std::memcpy(axes.data(),CFDataGetBytePtr(data)+sizeof(policy),axes.size()*sizeof(JaliumFontVariation));return true;
}
inline bool Contains(CFArrayRef fonts,CTFontRef font){
    if(!fonts)return false;for(CFIndex i=0;i<CFArrayGetCount(fonts);++i)if(CFEqual(CFArrayGetValueAtIndex(fonts,i),font))return true;return false;
}
// A vertical CoreText run can carry a rotated font matrix. Resize the
// authored private font with the same graphics face, preserving its intended
// matrix and settings instead of baking that temporary run rotation into it.
inline CTFontRef AuthoredPrivateFont(CFArrayRef fonts,CTFontRef font) {
    if(!fonts)return nullptr;auto graphics=CTFontCopyGraphicsFont(font,nullptr);if(!graphics)return nullptr;
    CTFontRef found=nullptr;
    for(CFIndex i=0;i<CFArrayGetCount(fonts)&&!found;++i) {
        auto candidate=static_cast<CTFontRef>(CFArrayGetValueAtIndex(fonts,i));
        auto other=CTFontCopyGraphicsFont(candidate,nullptr);
        if(CFEqual(candidate,font)||(other&&CFEqual(other,graphics)))found=candidate;
        if(other)CGFontRelease(other);
    }
    CGFontRelease(graphics);return found;
}
} // namespace font_size_adjust

// Resolve fonts with the entire original shaping context, then adjust each
// selected face. CoreText's implicit CJK/emoji fallbacks participate as well.
// Call before synthetic caps/position resizing, so that synthesis keeps its ratio.
inline CFAttributedStringRef CreateAppleSizeAdjustedText(CFAttributedStringRef source){
    auto length=CFAttributedStringGetLength(source);bool active=false;
    for(CFIndex at=0;at<length;){CFRange range{};if(CFAttributedStringGetAttribute(source,at,font_size_adjust::Key(),&range)){active=true;break;}at=std::max(at+1,range.location+range.length);}
    if(!active)return static_cast<CFAttributedStringRef>(CFRetain(source));
    auto result=CFAttributedStringCreateMutableCopy(kCFAllocatorDefault,0,source);if(!result)return nullptr;
    auto line=CTLineCreateWithAttributedString(source);if(!line){CFRelease(result);return nullptr;}
    auto runs=CTLineGetGlyphRuns(line);
    struct Entry{CTFontRef font,adjusted;CFDictionaryRef settings;double used;JaliumFontFaceMetrics descriptor;};std::vector<Entry> cache;
    bool success=true;
    for(CFIndex i=0;i<CFArrayGetCount(runs);++i){
        auto run=static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs,i));auto attributes=CTRunGetAttributes(run);
        auto settings=static_cast<CFDictionaryRef>(CFDictionaryGetValue(attributes,font_size_adjust::Key()));if(!settings)continue;
        auto font=static_cast<CTFontRef>(CFDictionaryGetValue(attributes,kCTFontAttributeName));if(!font)continue;
        CTFontRef adjusted=nullptr;double used=0;auto descriptor=font_size_adjust::InitialMetrics();
        for(const auto& entry:cache)if(CFEqual(entry.font,font)&&CFEqual(entry.settings,settings)){adjusted=entry.adjusted;used=entry.used;descriptor=entry.descriptor;break;}
        if(!adjusted){
            font_size_adjust::Policy policy;std::vector<JaliumFontVariation> axes;if(!font_size_adjust::Read(settings,policy,axes)){success=false;break;}
            auto known=static_cast<CFArrayRef>(CFDictionaryGetValue(settings,CFSTR("fonts")));auto automatic=static_cast<CFArrayRef>(CFDictionaryGetValue(settings,CFSTR("automatic")));
            auto privateFonts=static_cast<CFArrayRef>(CFDictionaryGetValue(settings,CFSTR("private")));
            auto authored=font_size_adjust::AuthoredPrivateFont(privateFonts,font);
            auto selected=authored?authored:font;
            descriptor=font_size_adjust::MetricsFor(settings,selected);
            adjusted=font_size_adjust::Copy(selected,policy,axes,font_size_adjust::Contains(known,selected),font_size_adjust::Contains(automatic,selected),&used,descriptor,authored!=nullptr);
            if(!adjusted){success=false;break;}cache.push_back({font,adjusted,settings,used,descriptor});
        }
        auto range=CTRunGetStringRange(run);
        CFAttributedStringSetAttribute(result,range,kCTFontAttributeName,adjusted);
        CFAttributedStringSetAttribute(result,range,font_size_adjust::FinalKey(),settings);
        CFAttributedStringSetAttribute(result,range,font_size_adjust::BaseKey(),adjusted);
        SetAppleFontLineMetrics(result,range,descriptor,used);
    }
    for(const auto& entry:cache)CFRelease(entry.adjusted);CFRelease(line);
    CFAttributedStringRemoveAttribute(result,CFRangeMake(0,length),font_size_adjust::Key());
    if(!success){CFRelease(result);return nullptr;}return result;
}
// Case synthesis and presentation selectors can select a different fallback
// face. Resolve once more after those transformations, retaining their scale
// and baseline shift relative to the already adjusted source font.
inline CFAttributedStringRef FinalizeAppleSizeAdjustedText(CFAttributedStringRef source) {
    auto length=CFAttributedStringGetLength(source);bool active=false;
    for(CFIndex at=0;at<length;) { CFRange range{};
        if(CFAttributedStringGetAttribute(source,at,font_size_adjust::FinalKey(),&range)){active=true;break;}
        at=std::max(at+1,range.location+range.length);
    }
    if(!active)return static_cast<CFAttributedStringRef>(CFRetain(source));
    auto release=[](const void* p){if(p)CFRelease(p);};using Owner=std::unique_ptr<const void,decltype(release)>;
    Owner output(CFAttributedStringCreateMutableCopy(kCFAllocatorDefault,0,source),release);
    Owner shaped(CTLineCreateWithAttributedString(source),release);if(!output||!shaped)return nullptr;
    auto result=const_cast<CFMutableAttributedStringRef>(static_cast<CFAttributedStringRef>(output.get()));
    auto runs=CTLineGetGlyphRuns(static_cast<CTLineRef>(shaped.get()));
    for(CFIndex i=0;i<CFArrayGetCount(runs);++i) {
        auto run=static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs,i));auto attributes=CTRunGetAttributes(run);
        auto settings=static_cast<CFDictionaryRef>(CFDictionaryGetValue(attributes,font_size_adjust::FinalKey()));if(!settings)continue;
        auto font=static_cast<CTFontRef>(CFDictionaryGetValue(attributes,kCTFontAttributeName));
        auto range=CTRunGetStringRange(run);auto authored=static_cast<CTFontRef>(CFAttributedStringGetAttribute(source,range.location,kCTFontAttributeName,nullptr));
        auto previous=static_cast<CTFontRef>(CFDictionaryGetValue(attributes,font_size_adjust::BaseKey()));
        if(!font||!authored||!previous)return nullptr;
        Owner actualGraphics(CTFontCopyGraphicsFont(font,nullptr),release),authoredGraphics(CTFontCopyGraphicsFont(authored,nullptr),release);
        if(CFEqual(font,authored)||(actualGraphics&&authoredGraphics&&CFEqual(actualGraphics.get(),authoredGraphics.get())&&CTFontGetSize(font)==CTFontGetSize(authored)))continue;
        font=font_size_adjust::CapsFallback(settings,attributes,font,CFAttributedStringGetString(source),range);
        font_size_adjust::Policy policy;std::vector<JaliumFontVariation> axes;if(!font_size_adjust::Read(settings,policy,axes))return nullptr;
        auto known=static_cast<CFArrayRef>(CFDictionaryGetValue(settings,CFSTR("fonts")));
        auto automatic=static_cast<CFArrayRef>(CFDictionaryGetValue(settings,CFSTR("automatic")));
        auto privateFonts=static_cast<CFArrayRef>(CFDictionaryGetValue(settings,CFSTR("private")));
        auto privateFont=font_size_adjust::AuthoredPrivateFont(privateFonts,font);
        bool privateFace=privateFont!=nullptr;if(privateFont)font=privateFont;
        auto descriptor=font_size_adjust::MetricsFor(settings,font);
        double used=0;Owner base(font_size_adjust::Copy(font,policy,axes,font_size_adjust::Contains(known,font),font_size_adjust::Contains(automatic,font),&used,descriptor,privateFace),release);
        if(!base)return nullptr;
        double oldSize=CTFontGetSize(previous),scale=oldSize>0?CTFontGetSize(authored)/oldSize:1;
        auto scaled=[&](double ratio) { return font_size_adjust::AtSize(static_cast<CTFontRef>(base.get()),used==0?1e-20:std::min(used*ratio,1048576.0),0,{},true,false,privateFace); };
        Owner selected(scaled(scale),release);if(!selected)return nullptr;
        CFAttributedStringSetAttribute(result,range,kCTFontAttributeName,selected.get());
        SetAppleFontLineMetrics(result,range,descriptor,used);
        for(auto key:{CFSTR("JaliumCapsBaseFont"),CFSTR("JaliumPositionBaseFont")}) {
            auto prior=static_cast<CTFontRef>(CFDictionaryGetValue(attributes,key));if(!prior)continue;
            Owner metric(scaled(oldSize>0?CTFontGetSize(prior)/oldSize:1),release);if(!metric)return nullptr;
            CFAttributedStringSetAttribute(result,range,key,metric.get());
        }
        auto offset=static_cast<CFNumberRef>(CFDictionaryGetValue(attributes,kCTBaselineOffsetAttributeName));
        if(offset&&oldSize>0) { double value=0;if(CFNumberGetValue(offset,kCFNumberDoubleType,&value)) {
            value*=used/oldSize;Owner number(CFNumberCreate(kCFAllocatorDefault,kCFNumberDoubleType,&value),release);if(!number)return nullptr;
            CFAttributedStringSetAttribute(result,range,kCTBaselineOffsetAttributeName,number.get());
        }}
    }
    CFAttributedStringRemoveAttribute(result,CFRangeMake(0,length),font_size_adjust::FinalKey());
    CFAttributedStringRemoveAttribute(result,CFRangeMake(0,length),font_size_adjust::BaseKey());
    return static_cast<CFAttributedStringRef>(CFRetain(output.get()));
}
} // namespace jalium
#endif
