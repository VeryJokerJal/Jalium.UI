#pragma once
#include "jalium_backend.h"
#include <vector>
#include <algorithm>
#include <cstdint>
namespace jalium { struct FontCascadeEntry { const void* font; const void* canonical; const void* characters = nullptr; bool disablesKerning = false; }; }
#ifdef __APPLE__
#include <CoreFoundation/CoreFoundation.h>
#include <CoreText/CoreText.h>
namespace jalium {
inline void ConfigureFontPositioning(CGContextRef context, bool fractional)
{
    // Bitmap contexts otherwise quantize glyph positions, including whole-pixel
    // vertical placement. Keep the caller's fractional origin inside the cropped
    // texture; its integer allocation/composite bounds must not snap the glyphs.
    CGContextSetAllowsFontSubpixelPositioning(context, fractional);
    CGContextSetShouldSubpixelPositionFonts(context, fractional);
    CGContextSetAllowsFontSubpixelQuantization(context, false);
    CGContextSetShouldSubpixelQuantizeFonts(context, false);
}

inline bool UsesFractionalFontPositioning(const TextFormat& format)
{
    return format.GetTextFormattingMode() == 0 || format.GetSubpixelPositioning() ||
        format.GetTextHintingMode() == 2;
}

inline bool CharacterSetContainsString(CFCharacterSetRef set, CFStringRef text)
{
    if (!set) return false;
    for (CFIndex i = 0; i < CFStringGetLength(text); ++i) {
        UTF32Char scalar = CFStringGetCharacterAtIndex(text, i);
        if (scalar >= 0xd800 && scalar <= 0xdbff && i + 1 < CFStringGetLength(text)) {
            UniChar low = CFStringGetCharacterAtIndex(text, i + 1);
            if (low >= 0xdc00 && low <= 0xdfff) { scalar = 0x10000 + ((scalar - 0xd800) << 10) + low - 0xdc00; ++i; }
        }
        if (!CFCharacterSetIsLongCharacterMember(set, scalar)) return false;
    }
    return true;
}

inline bool FontContainsString(CTFontRef font, CFStringRef text)
{
    CFCharacterSetRef set = CTFontCopyCharacterSet(font);
    bool contains = CharacterSetContainsString(set, text);
    if (set) CFRelease(set);
    return contains;
}

inline void ApplyFontFeatureAttributes(CFMutableAttributedStringRef text, CFRange span, bool disabled)
{
    CFAttributedStringRemoveAttribute(text, span, kCTKernAttributeName);
    if (!disabled) return;
    int32_t zero = 0;
    CFNumberRef number = CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt32Type, &zero);
    if (number) { CFAttributedStringSetAttribute(text, span, kCTKernAttributeName, number); CFRelease(number); }
}

template<class Visitor>
inline void ForEachCanonicalFontRange(CFAttributedStringRef attributed, CFRange span, const std::vector<FontCascadeEntry>& fonts, Visitor visitor)
{
    // Retain the actual selected face. A name-based CoreText cascade can merge
    // subsets of one font and discard their different feature settings.
    if (fonts.empty() || span.length <= 0) return;
    CFStringRef text = CFAttributedStringGetString(attributed);
    std::vector<CFCharacterSetRef> coverage;
    coverage.reserve(fonts.size());
    for (const auto& entry : fonts) {
        coverage.push_back(entry.characters ? static_cast<CFCharacterSetRef>(CFRetain(entry.characters)) : CTFontCopyCharacterSet(static_cast<CTFontRef>(entry.font)));
    }
    CFIndex runStart = span.location;
    int selected = -1;
    auto flush = [&](CFIndex end) {
        if (selected < 0 || end <= runStart) return;
        visitor(static_cast<size_t>(selected), CFRangeMake(runStart, end - runStart));
    };
    for (CFIndex position = span.location; position < span.location + span.length;) {
        CFRange cluster = CFStringGetRangeOfComposedCharactersAtIndex(text, position);
        position = cluster.location + cluster.length;
        int chosen = -1;
        if (cluster.length == 1 && CFStringGetCharacterAtIndex(text, cluster.location) < 128) {
            UniChar character = CFStringGetCharacterAtIndex(text, cluster.location);
            for (size_t i = 0; i < coverage.size(); ++i)
                if (coverage[i] && CFCharacterSetIsCharacterMember(coverage[i], character)) { chosen = static_cast<int>(i); break; }
        } else {
            CFStringRef original = CFStringCreateWithSubstring(kCFAllocatorDefault, text, cluster);
            CFMutableStringRef normalized = original ? CFStringCreateMutableCopy(kCFAllocatorDefault, 0, original) : nullptr;
            if (normalized) CFStringNormalize(normalized, kCFStringNormalizationFormC);
            if (original && normalized) for (size_t i = 0; i < coverage.size(); ++i)
                if (CharacterSetContainsString(coverage[i], original) ||
                    (!CFEqual(original, normalized) && CharacterSetContainsString(coverage[i], normalized))) { chosen = static_cast<int>(i); break; }
            if (normalized) CFRelease(normalized);
            if (original) CFRelease(original);
        }
        if (chosen != selected) {
            CFIndex boundary = std::max(span.location, cluster.location);
            flush(boundary); runStart = boundary; selected = chosen;
        }
    }
    flush(span.location + span.length);
    for (auto set : coverage) if (set) CFRelease(set);
}

inline void ApplyCanonicalFontFallbacks(CFMutableAttributedStringRef attributed, CFRange span, const std::vector<FontCascadeEntry>& fonts)
{
    ForEachCanonicalFontRange(attributed, span, fonts, [&](size_t selected, CFRange range) {
        CFAttributedStringSetAttribute(attributed, range, kCTFontAttributeName, fonts[selected].canonical);
        ApplyFontFeatureAttributes(attributed, range, fonts[selected].disablesKerning);
    });
}

inline void ApplyFontVisibility(CFMutableAttributedStringRef attributed, CFRange span, CFCharacterSetRef invisible)
{
    if (!invisible || !span.length) return;
    CFStringRef string = CFAttributedStringGetString(attributed);
    const CFIndex length = CFStringGetLength(string);
    for (CFIndex position = span.location; position < span.location + span.length;) {
        CFRange cluster = CFStringGetRangeOfComposedCharactersAtIndex(string, position);
        bool hidden = false;
        for (CFIndex index = cluster.location; index < cluster.location + cluster.length; ++index) {
            UTF32Char value = CFStringGetCharacterAtIndex(string, index);
            if (value >= 0xd800 && value <= 0xdbff && index + 1 < length) {
                UniChar low = CFStringGetCharacterAtIndex(string, index + 1);
                if (low >= 0xdc00 && low <= 0xdfff) { value = 0x10000 + ((value - 0xd800) << 10) + low - 0xdc00; ++index; }
            }
            hidden |= CFCharacterSetIsLongCharacterMember(invisible, value);
        }
        if (!hidden && (cluster.length > 1 || CFStringGetCharacterAtIndex(string, cluster.location) >= 128)) {
            CFStringRef original = CFStringCreateWithSubstring(kCFAllocatorDefault, string, cluster);
            CFMutableStringRef normalized = original ? CFStringCreateMutableCopy(kCFAllocatorDefault, 0, original) : nullptr;
            if (normalized) {
                CFStringNormalize(normalized, kCFStringNormalizationFormC);
                for (CFIndex i = 0; i < CFStringGetLength(normalized); ++i)
                    hidden |= CFCharacterSetIsCharacterMember(invisible, CFStringGetCharacterAtIndex(normalized, i));
                CFRelease(normalized);
            }
            if (original) CFRelease(original);
        }
        if (hidden) CFAttributedStringSetAttribute(attributed, cluster, CFSTR("JaliumFontInvisible"), kCFBooleanTrue);
        position = cluster.location + cluster.length;
    }
}
inline bool IsFontRunInvisible(CFDictionaryRef attributes)
{ return CFDictionaryGetValue(attributes, CFSTR("JaliumFontInvisible")) == kCFBooleanTrue; }

inline CFIndex FontGlyphSourceEnd(CFStringRef text, CFIndex start, CFIndex end)
{
    if (!text || start < 0 || end <= start || end > CFStringGetLength(text)) return end;
    // CoreText can append a zero-glyph formatting control to the preceding font
    // run's source range. It does not contribute to that glyph's ownership.
    while (end > start) {
        UniChar value = CFStringGetCharacterAtIndex(text, end - 1);
        if (!(value == 0x061c || value == 0x200b || value == 0x200e || value == 0x200f ||
            value == 0x2060 || value == 0xfeff ||
            (value >= 0x202a && value <= 0x202e) || (value >= 0x2066 && value <= 0x2069))) break;
        --end;
    }
    if (end > start) {
        auto cluster = CFStringGetRangeOfComposedCharactersAtIndex(text, end - 1);
        end = cluster.location + cluster.length;
    }
    return end;
}

inline void DrawFontRunRanges(CTRunRef run, CGContextRef context, CGPoint origin,
    CFStringRef text, const uint32_t* ranges, uint32_t count, int32_t mode,
    CFRange glyphRange = CFRangeMake(0, 0), CFRange ownedSource = CFRangeMake(-1, 0), bool contextForeground = false)
{
    CGContextSaveGState(context);
    // Position retained clusters in the same coordinate space as an ordinary
    // line draw. CoreText color glyphs sample fractional text-matrix offsets
    // differently from context translations.
    CGContextTranslateCTM(context, origin.x, origin.y);
    CGAffineTransform matrix = CTRunGetTextMatrix(run);
    matrix.tx = 0; matrix.ty = 0;
    CGContextSetTextMatrix(context, matrix);
    auto paintRange = [&](CFRange range) {
        if (!contextForeground) { CTRunDraw(run, context, range); return; }
        auto font = static_cast<CTFontRef>(CFDictionaryGetValue(CTRunGetAttributes(run), kCTFontAttributeName));
        if (!font) return;
        CFIndex count = range.length ? range.length : CTRunGetGlyphCount(run) - range.location;
        if (count <= 0) return;
        std::vector<CGGlyph> glyphs(static_cast<size_t>(count));
        std::vector<CGPoint> positions(static_cast<size_t>(count));
        CTRunGetGlyphs(run, CFRangeMake(range.location, count), glyphs.data());
        CTRunGetPositions(run, CFRangeMake(range.location, count), positions.data());
        // Reuse the immutable run's glyph IDs and positions. Recreating a line
        // with new color attributes can change ligatures across source seams.
        CTFontDrawGlyphs(font, glyphs.data(), positions.data(), count, context);
    };
    if (mode == 0 && ownedSource.location < 0) paintRange(glyphRange);
    else {
        CFIndex n = CTRunGetGlyphCount(run);
        if (n == 0) { CGContextRestoreGState(context); return; }
        std::vector<CFIndex> indices(static_cast<size_t>(n));
        CTRunGetStringIndices(run, CFRangeMake(0, n), indices.data());
        auto boundaries = indices;
        auto span = CTRunGetStringRange(run);
        boundaries.push_back(span.location + span.length);
        std::sort(boundaries.begin(), boundaries.end());
        boundaries.erase(std::unique(boundaries.begin(), boundaries.end()), boundaries.end());
        CFIndex begin = glyphRange.location;
        CFIndex limit = glyphRange.length ? begin + glyphRange.length : n;
        CFIndex first = -1;
        for (CFIndex i = begin; i <= limit; ++i) {
            bool draw = false;
            if (i < limit) {
                CFIndex start = indices[static_cast<size_t>(i)];
                auto next = std::upper_bound(boundaries.begin(), boundaries.end(), start);
                CFIndex end = next == boundaries.end() ? start + 1 : *next;
                if (text && start >= 0 && end > start && end <= CFStringGetLength(text)) {
                    auto a = CFStringGetRangeOfComposedCharactersAtIndex(text, start);
                    end = FontGlyphSourceEnd(text, start, end); start = a.location;
                }
                // A cluster crossing a source seam is painted once, by its
                // final contributing source. Earlier inline backgrounds have
                // then been painted before overhanging cluster ink.
                const bool owned = ownedSource.location < 0 || (end > ownedSource.location &&
                    end - ownedSource.location <= ownedSource.length);
                // The target normalizes and merges ranges. Find the first end
                // after this cluster in logarithmic time, including RTL runs.
                uint32_t lo = 0, hi = count;
                while (lo < hi) {
                    uint32_t mid = lo + (hi - lo) / 2;
                    if (static_cast<uint64_t>(ranges[2 * mid]) + ranges[2 * mid + 1] <= static_cast<uint64_t>(std::max<CFIndex>(0, start))) lo = mid + 1;
                    else hi = mid;
                }
                bool selected = start >= 0 && lo < count && static_cast<CFIndex>(ranges[2 * lo]) < end;
                draw = owned && (mode == 0 || (mode == 1 ? selected : !selected));
            }
            if (draw && first < 0) first = i;
            if (!draw && first >= 0) {
                paintRange(CFRangeMake(first, i - first));
                first = -1;
            }
        }
    }
    CGContextRestoreGState(context);
}

inline void DrawFontRun(CTRunRef run, CGContextRef context, CGPoint origin)
{
    // CTRunDraw does not install the run's font matrix as CTLineDraw does.
    // Keep synthetic slant and other font transforms, with the line origin in
    // tx/ty as required by CTRunGetTextMatrix. Isolate any context changes.
    CGContextSaveGState(context);
    CGAffineTransform matrix = CTRunGetTextMatrix(run);
    matrix.tx = origin.x; matrix.ty = origin.y;
    CGContextSetTextMatrix(context, matrix);
    CTRunDraw(run, context, CFRangeMake(0, 0));
    CGContextRestoreGState(context);
}
}
#endif
