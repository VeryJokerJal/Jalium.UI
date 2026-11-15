#pragma once
#ifdef __APPLE__
#include <CoreText/CoreText.h>
#include <vector>
namespace jalium {
// Retain CoreText's glyphs, contextual positioning and original UTF-16 text;
// compensate only the difference from the face's actual vertical advances.
CFAttributedStringRef CreateAppleVerticalMetricsText(CFAttributedStringRef input, bool strongLtr,
    CFRange range = CFRangeMake(0, 0));
bool HasAppleVerticalMetrics(CFAttributedStringRef text);
// A metric correction belongs to the last character's cell, rather than the
// hanging tracking space CoreText normally excludes from line fitting.
double AppleVerticalMetricTail(CTLineRef line, CFAttributedStringRef text);
// Tracking supplies the correct cluster extent. A decomposed cluster can have
// different advances for its component glyphs; retain their individual origins.
std::vector<double> AppleVerticalMetricGlyphShifts(CTLineRef line, CFAttributedStringRef text);
inline bool HasAppleVerticalMetricTracking(CFDictionaryRef attributes) {
    return CFDictionaryContainsKey(attributes, CFSTR("JaliumVerticalMetricTracking"));
}
}
#endif
