#pragma once
#ifdef __APPLE__
#include "jalium_types.h"
#include <CoreText/CoreText.h>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <new>

namespace jalium {
struct AppleFontLineMetrics {
    double ascent = 0, descent = 0, leading = 0;
    bool overridden = false;
};
struct AppleFontMetricOverrides {
    uint32_t flags = 0;
    double ascent = 0, descent = 0, leading = 0;
};
inline CFStringRef AppleFontMetricsKey() { return CFSTR("JaliumFontLineMetricOverrides"); }
inline bool HasAppleFontMetrics(const JaliumFontFaceMetrics& value) {
    return value.ascent >= 0 || value.descent >= 0 || value.lineGap >= 0;
}
inline double BoundAppleFontMetric(long double value) {
    return static_cast<double>(std::clamp(value, 0.0L, 1048576.0L));
}
inline void SetAppleFontLineMetrics(CFMutableAttributedStringRef text, CFRange range,
    const JaliumFontFaceMetrics& descriptor, double usedSize) {
    AppleFontMetricOverrides values{};
    if (descriptor.ascent >= 0) { values.flags |= 1; values.ascent = BoundAppleFontMetric(static_cast<long double>(usedSize)*descriptor.ascent); }
    if (descriptor.descent >= 0) { values.flags |= 2; values.descent = BoundAppleFontMetric(static_cast<long double>(usedSize)*descriptor.descent); }
    if (descriptor.lineGap >= 0) { values.flags |= 4; values.leading = BoundAppleFontMetric(static_cast<long double>(usedSize)*descriptor.lineGap); }
    if (!values.flags) { CFAttributedStringRemoveAttribute(text, range, AppleFontMetricsKey()); return; }
    auto data = CFDataCreate(kCFAllocatorDefault, reinterpret_cast<const UInt8*>(&values), sizeof(values));
    if (!data) throw std::bad_alloc();
    CFAttributedStringSetAttribute(text, range, AppleFontMetricsKey(), data); CFRelease(data);
}
inline bool ReadAppleFontLineMetrics(CFDictionaryRef attributes, AppleFontMetricOverrides& values) {
    auto value = CFDictionaryGetValue(attributes, AppleFontMetricsKey());
    if (!value || CFGetTypeID(value) != CFDataGetTypeID()) return false;
    auto data = static_cast<CFDataRef>(value);
    if (CFDataGetLength(data) != sizeof(values)) return false;
    std::memcpy(&values, CFDataGetBytePtr(data), sizeof(values));
    return values.flags && values.flags <= 7 && std::isfinite(values.ascent) && values.ascent >= 0 &&
        std::isfinite(values.descent) && values.descent >= 0 && std::isfinite(values.leading) && values.leading >= 0;
}
inline bool HasAppleFontLineMetrics(CFAttributedStringRef text) {
    auto length = CFAttributedStringGetLength(text);
    for (CFIndex at = 0; at < length;) {
        CFRange range{};
        if (CFAttributedStringGetAttribute(text, at, AppleFontMetricsKey(), &range)) return true;
        at = std::max(at+1, range.location+range.length);
    }
    return false;
}
inline AppleFontLineMetrics ApplyAppleFontLineMetrics(CTFontRef font, const JaliumFontFaceMetrics& descriptor, double usedSize) {
    return {descriptor.ascent < 0 ? CTFontGetAscent(font) : BoundAppleFontMetric(static_cast<long double>(usedSize)*descriptor.ascent),
        descriptor.descent < 0 ? CTFontGetDescent(font) : BoundAppleFontMetric(static_cast<long double>(usedSize)*descriptor.descent),
        descriptor.lineGap < 0 ? CTFontGetLeading(font) : BoundAppleFontMetric(static_cast<long double>(usedSize)*descriptor.lineGap), HasAppleFontMetrics(descriptor)};
}
// CTLine's native font metrics cannot be replaced through a font descriptor.
// Keep the glyphs and their positions, and replace only the line-box metrics.
inline bool GetAppleFontLineMetrics(CTLineRef line, AppleFontLineMetrics& result,
    const AppleFontLineMetrics* strut = nullptr, bool force = false) {
    auto runs = CTLineGetGlyphRuns(line); bool requested = force;
    for (CFIndex i=0; !requested && i<CFArrayGetCount(runs); ++i) {
        AppleFontMetricOverrides values{};
        requested = ReadAppleFontLineMetrics(CTRunGetAttributes(static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs,i))), values);
    }
    if (!requested) return false;
    result = strut ? *strut : AppleFontLineMetrics{};result.overridden=true;
    for (CFIndex i=0; i<CFArrayGetCount(runs); ++i) {
        auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs,i)); auto attributes = CTRunGetAttributes(run);
        auto font = static_cast<CTFontRef>(CFDictionaryGetValue(attributes,kCTFontAttributeName));
        auto caps = static_cast<CTFontRef>(CFDictionaryGetValue(attributes,CFSTR("JaliumCapsBaseFont")));
        auto position = static_cast<CTFontRef>(CFDictionaryGetValue(attributes,CFSTR("JaliumPositionBaseFont")));
        if (position) font=position; else if (caps) font=caps;
        if (!font) continue;
        CGFloat ascent=0,descent=0,leading=0;
        CTRunGetTypographicBounds(run,CFRangeMake(0,0),&ascent,&descent,&leading);
        AppleFontLineMetrics current{std::max<double>(ascent,CTFontGetAscent(font)),
            std::max<double>(descent,CTFontGetDescent(font)),std::max<double>(leading,CTFontGetLeading(font))};
        AppleFontMetricOverrides values{};
        if (ReadAppleFontLineMetrics(attributes,values)) {
            if (values.flags&1) current.ascent=values.ascent;
            if (values.flags&2) current.descent=values.descent;
            if (values.flags&4) current.leading=values.leading;
        }
        result.ascent=std::max(result.ascent,current.ascent);
        result.descent=std::max(result.descent,current.descent);
        result.leading=std::max(result.leading,current.leading);
    }
    return true;
}
}
#endif
