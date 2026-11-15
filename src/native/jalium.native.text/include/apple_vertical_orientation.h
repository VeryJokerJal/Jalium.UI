#pragma once
#ifdef __APPLE__
#include "unicode_vertical_orientation_data.h"
#include <CoreText/CoreText.h>
#include <vector>

namespace jalium {
inline int32_t AppleSourceTextOrientation(CFAttributedStringRef text, CFIndex index)
{
    auto value = static_cast<CFNumberRef>(CFAttributedStringGetAttribute(text, index,
        CFSTR("JaliumTextOrientation"), nullptr));
    int32_t result = -1;
    if (value && CFGetTypeID(value) == CFNumberGetTypeID()) CFNumberGetValue(value, kCFNumberSInt32Type, &result);
    return result;
}

inline bool ApplyAppleSourceTextOrientations(CFMutableAttributedStringRef text)
{
    const auto string = CFAttributedStringGetString(text);
    const auto length = CFStringGetLength(string);
    const int32_t direction = kCTWritingDirectionLeftToRight | kCTWritingDirectionOverride;
    auto number = CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt32Type, &direction);
    if (!number) return false;
    const void* value = number;
    auto directions = CFArrayCreate(kCFAllocatorDefault, &value, 1, &kCFTypeArrayCallBacks);
    CFRelease(number);
    if (!directions) return false;
    for (CFIndex at = 0; at < length;) {
        const auto range = CFStringGetRangeOfComposedCharactersAtIndex(string, at);
        const auto orientation = AppleSourceTextOrientation(text, range.location);
        // One typographic unit cannot acquire two orientations at a source seam.
        if (orientation == 2) CFAttributedStringSetAttribute(text, range, kCTVerticalFormsAttributeName, kCFBooleanFalse);
        if (orientation == 1) CFAttributedStringSetAttribute(text, range, kCTWritingDirectionAttributeName, directions);
        at = range.location + range.length;
    }
    CFRelease(directions);
    return true;
}

inline CTTypesetterRef CreateAppleTextTypesetter(CFAttributedStringRef text, bool strongLtr = false)
{
    if (!strongLtr) return CTTypesetterCreateWithAttributedString(text);
    const int32_t level = 0;
    auto number = CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt32Type, &level);
    if (!number) return nullptr;
    const void* key = kCTTypesetterOptionForcedEmbeddingLevel;
    const void* value = number;
    auto options = CFDictionaryCreate(kCFAllocatorDefault, &key, &value, 1,
        &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    CFRelease(number);
    if (!options) return nullptr;
    auto result = CTTypesetterCreateWithAttributedStringAndOptions(text, options);
    CFRelease(options);
    return result;
}

inline bool AppleMixedVerticalClusterIsUpright(CFStringRef text, CFRange cluster)
{
    bool upright = false;
    for (CFIndex at = cluster.location; at < cluster.location + cluster.length;) {
        const CFIndex first = at;
        UTF32Char scalar = CFStringGetCharacterAtIndex(text, at++);
        if (CFStringIsSurrogateHighCharacter(scalar) && at < cluster.location + cluster.length) {
            const UniChar low = CFStringGetCharacterAtIndex(text, at);
            if (CFStringIsSurrogateLowCharacter(low)) {
                scalar = CFStringGetLongCharacterForSurrogatePair(scalar, low);
                ++at;
            }
        }
        if (first == cluster.location) upright = vertical_orientation::Property(scalar) != 0;
        // UAX #50: any enclosing mark makes the whole cluster upright,
        // including a keycap whose initial digit normally rotates.
        if (vertical_orientation::Enclosing(scalar)) return true;
    }
    return upright;
}

inline bool ApplyAppleMixedVerticalOrientation(CFMutableAttributedStringRef text, bool centralBaseline = true)
{
    const auto string = CFAttributedStringGetString(text);
    const auto length = CFStringGetLength(string);
    if (!length) return true;
    CFIndex start = 0;
    bool upright = AppleMixedVerticalClusterIsUpright(string,
        CFStringGetRangeOfComposedCharactersAtIndex(string, 0));
    for (CFIndex at = 0; at < length;) {
        const auto cluster = CFStringGetRangeOfComposedCharactersAtIndex(string, at);
        const bool next = AppleMixedVerticalClusterIsUpright(string, cluster);
        if (next != upright) {
            CFAttributedStringSetAttribute(text, CFRangeMake(start, at - start),
                kCTVerticalFormsAttributeName, upright ? kCFBooleanTrue : kCFBooleanFalse);
            start = at;
            upright = next;
        }
        at = cluster.location + cluster.length;
    }
    CFAttributedStringSetAttribute(text, CFRangeMake(start, length - start),
        kCTVerticalFormsAttributeName, upright ? kCFBooleanTrue : kCFBooleanFalse);
    // Mixed vertical text uses the central baseline. The zero reference
    // anchors it at the caller's column center while CoreText reads each
    // actual font's baseline table (and synthesizes missing baselines).
    const CGFloat zero = 0;
    auto offset = CFNumberCreate(kCFAllocatorDefault, kCFNumberCGFloatType, &zero);
    if (!offset) return false;
    const auto baselineClass = centralBaseline ? kCTBaselineClassIdeographicCentered : kCTBaselineClassRoman;
    const void* key = baselineClass;
    const void* value = offset;
    auto reference = CFDictionaryCreate(kCFAllocatorDefault, &key, &value, 1,
        &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    CFRelease(offset);
    if (!reference) return false;
    CFAttributedStringSetAttribute(text, CFRangeMake(0, length),
        kCTBaselineClassAttributeName, baselineClass);
    CFAttributedStringSetAttribute(text, CFRangeMake(0, length),
        kCTBaselineReferenceInfoAttributeName, reference);
    CFRelease(reference);
    return true;
}

inline void AppleLogicalRunPositions(CTRunRef run, std::vector<CGPoint>& positions)
{
    const auto count = CTRunGetGlyphCount(run);
    positions.resize(static_cast<size_t>(count));
    if (!count) return;
    CTRunGetPositions(run, CFRangeMake(0, 0), positions.data());
    const auto attributes = CTRunGetAttributes(run);
    if (CFDictionaryGetValue(attributes, kCTVerticalFormsAttributeName) != kCFBooleanTrue) return;
    auto font = static_cast<CTFontRef>(CFDictionaryGetValue(attributes, kCTFontAttributeName));
    if (!font) return;
    std::vector<CGGlyph> glyphs(static_cast<size_t>(count));
    std::vector<CGSize> origins(static_cast<size_t>(count));
    CTRunGetGlyphs(run, CFRangeMake(0, 0), glyphs.data());
    CTFontGetVerticalTranslationsForGlyphs(font, glyphs.data(), origins.data(), count);
    const auto matrix = CTRunGetTextMatrix(run);
    for (CFIndex glyph = 0; glyph < count; ++glyph)
        positions[glyph] = CGPointApplyAffineTransform(CGPointMake(
            positions[glyph].x - origins[glyph].width,
            positions[glyph].y - origins[glyph].height), matrix);
}
} // namespace jalium
#endif
