#pragma once
#include <vector>
namespace jalium { struct FontCascadeEntry { const void* font; const void* canonical; }; }
#ifdef __APPLE__
#include <CoreFoundation/CoreFoundation.h>
#include <CoreText/CoreText.h>
namespace jalium {
inline bool FontContainsString(CTFontRef font, CFStringRef text)
{
    CFCharacterSetRef set = CTFontCopyCharacterSet(font);
    if (!set) return false;
    bool contains = true;
    for (CFIndex i = 0; i < CFStringGetLength(text); ++i) {
        UTF32Char scalar = CFStringGetCharacterAtIndex(text, i);
        if (scalar >= 0xd800 && scalar <= 0xdbff && i + 1 < CFStringGetLength(text)) {
            UniChar low = CFStringGetCharacterAtIndex(text, i + 1);
            if (low >= 0xdc00 && low <= 0xdfff) { scalar = 0x10000 + ((scalar - 0xd800) << 10) + low - 0xdc00; ++i; }
        }
        contains &= CFCharacterSetIsLongCharacterMember(set, scalar);
    }
    CFRelease(set); return contains;
}

inline void ApplyCanonicalFontFallbacks(CFMutableAttributedStringRef attributed, CFRange span, const std::vector<FontCascadeEntry>& fonts)
{
    // Restricted descriptors may exclude the decomposed scalars even though
    // their NFC glyph is available. Select the same original face for that
    // cluster without replacing any source text or insertion positions.
    if (fonts.empty()) return;
    CFStringRef text = CFAttributedStringGetString(attributed);
    for (CFIndex position = span.location; position < span.location + span.length;) {
        CFRange cluster = CFStringGetRangeOfComposedCharactersAtIndex(text, position);
        position = cluster.location + cluster.length;
        if (cluster.length == 1 && CFStringGetCharacterAtIndex(text, cluster.location) < 128) continue;
        CFStringRef original = CFStringCreateWithSubstring(kCFAllocatorDefault, text, cluster);
        CFMutableStringRef normalized = original ? CFStringCreateMutableCopy(kCFAllocatorDefault, 0, original) : nullptr;
        if (normalized) CFStringNormalize(normalized, kCFStringNormalizationFormC);
        if (original && normalized && !CFEqual(original, normalized)) for (const auto& entry : fonts) {
            auto font = static_cast<CTFontRef>(entry.font);
            if (FontContainsString(font, original)) break;
            if (!FontContainsString(font, normalized)) continue;
            CFAttributedStringSetAttribute(attributed, cluster, kCTFontAttributeName, entry.canonical);
            break;
        }
        if (normalized) CFRelease(normalized);
        if (original) CFRelease(original);
    }
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
