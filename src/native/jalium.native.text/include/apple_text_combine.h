#pragma once
#ifdef __APPLE__
#include "apple_vertical_features.h"
#include "jalium_font_features.h"
#include <CoreText/CoreText.h>
#include <map>
#include <memory>
#include <vector>

namespace jalium {
namespace text_combine {
inline void Release(const void* value) { if (value) CFRelease(value); }
using Owner = std::unique_ptr<const void, decltype(&Release)>;

inline bool VisibleUnit(CFStringRef text, CFRange range) {
    auto controls = CFCharacterSetGetPredefined(kCFCharacterSetControl);
    for (CFIndex at = range.location; at < range.location + range.length; ++at)
        if (!CFCharacterSetIsCharacterMember(controls, CFStringGetCharacterAtIndex(text, at))) return true;
    return false;
}
inline CFIndex UnitCount(CFStringRef text) {
    CFIndex count = 0;
    for (CFIndex at = 0, length = CFStringGetLength(text); at < length;) {
        auto range = CFStringGetRangeOfComposedCharactersAtIndex(text, at);
        if (VisibleUnit(text, range)) ++count;
        at = range.location + range.length;
    }
    return count;
}

inline CTFontRef WidthFont(CTFontRef font, CFStringRef tag, bool enabled) {
    Owner previous(CTFontCopyAttribute(font, kCTFontFeatureSettingsAttribute), Release);
    Owner list(CFArrayCreateMutable(kCFAllocatorDefault, 0, &kCFTypeArrayCallBacks), Release);
    if (!list) return nullptr;
    auto settings = const_cast<CFMutableArrayRef>(static_cast<CFArrayRef>(list.get()));
    if (previous && CFGetTypeID(previous.get()) == CFArrayGetTypeID()) {
        auto values = static_cast<CFArrayRef>(previous.get());
        for (CFIndex i = 0; i < CFArrayGetCount(values); ++i) {
            auto value = static_cast<CFDictionaryRef>(CFArrayGetValueAtIndex(values, i));
            auto key = CFDictionaryGetValue(value, kCTFontOpenTypeFeatureTag);
            if (!key || (!CFEqual(key, CFSTR("hwid")) && !CFEqual(key, CFSTR("twid")) && !CFEqual(key, CFSTR("qwid"))))
                CFArrayAppendValue(settings, value);
        }
    }
    for (auto feature : {CFSTR("hwid"), CFSTR("twid"), CFSTR("qwid")}) {
        int value = enabled && CFEqual(feature, tag) ? 1 : 0;
        Owner number(CFNumberCreate(kCFAllocatorDefault, kCFNumberIntType, &value), Release);
        const void* keys[] = {kCTFontOpenTypeFeatureTag, kCTFontOpenTypeFeatureValue};
        const void* values[] = {feature, number.get()};
        Owner setting(number ? CFDictionaryCreate(kCFAllocatorDefault, keys, values, 2,
            &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks) : nullptr, Release);
        if (!setting) return nullptr;
        CFArrayAppendValue(settings, setting.get());
    }
    return CopyAppleFontFeatures(font, settings);
}

// Ask the actual shaper, including its fallback fonts and script selection.
// A font-wide feature tag alone does not establish coverage of every unit.
inline std::vector<std::vector<CGGlyph>> Glyphs(CTLineRef line, CFStringRef text) {
    std::vector<std::vector<CGGlyph>> result(CFStringGetLength(text));
    auto runs = CTLineGetGlyphRuns(line);
    for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
        auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i));
        auto count = CTRunGetGlyphCount(run);
        std::vector<CGGlyph> glyphs(count); std::vector<CFIndex> indices(count);
        CTRunGetGlyphs(run, CFRangeMake(0, 0), glyphs.data());
        CTRunGetStringIndices(run, CFRangeMake(0, 0), indices.data());
        for (CFIndex j = 0; j < count; ++j) if (indices[j] >= 0 && indices[j] < CFStringGetLength(text)) {
            auto range = CFStringGetRangeOfComposedCharactersAtIndex(text, indices[j]);
            result[range.location].push_back(glyphs[j]);
        }
    }
    return result;
}

inline CFAttributedStringRef TryWidth(CFAttributedStringRef input, CTLineRef natural, CFStringRef tag) {
    auto text = CFAttributedStringGetString(input); const auto length = CFStringGetLength(text);
    Owner baseline(CFAttributedStringCreateMutableCopy(kCFAllocatorDefault, 0, input), Release);
    Owner selected(CFAttributedStringCreateMutableCopy(kCFAllocatorDefault, 0, input), Release);
    if (!baseline || !selected) return nullptr;
    std::map<CTFontRef, std::pair<Owner, Owner>> fonts;
    auto runs = CTLineGetGlyphRuns(natural);
    for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
        auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i));
        auto font = static_cast<CTFontRef>(CFDictionaryGetValue(CTRunGetAttributes(run), kCTFontAttributeName));
        if (!font) return nullptr;
        auto found = fonts.find(font);
        if (found == fonts.end()) found = fonts.emplace(font, std::make_pair(
            Owner(WidthFont(font, tag, false), Release), Owner(WidthFont(font, tag, true), Release))).first;
        if (!found->second.first || !found->second.second) return nullptr;
        auto range = CTRunGetStringRange(run);
        CFAttributedStringSetAttribute(const_cast<CFMutableAttributedStringRef>(static_cast<CFAttributedStringRef>(baseline.get())), range,
            kCTFontAttributeName, found->second.first.get());
        CFAttributedStringSetAttribute(const_cast<CFMutableAttributedStringRef>(static_cast<CFAttributedStringRef>(selected.get())), range,
            kCTFontAttributeName, found->second.second.get());
    }
    Owner before(CTLineCreateWithAttributedString(static_cast<CFAttributedStringRef>(baseline.get())), Release);
    Owner after(CTLineCreateWithAttributedString(static_cast<CFAttributedStringRef>(selected.get())), Release);
    if (!before || !after) return nullptr;
    auto a = Glyphs(static_cast<CTLineRef>(before.get()), text), b = Glyphs(static_cast<CTLineRef>(after.get()), text);
    for (CFIndex at = 0; at < length;) {
        auto range = CFStringGetRangeOfComposedCharactersAtIndex(text, at);
        if (VisibleUnit(text, range) && (a[at].empty() || b[at].empty() || a[at] == b[at])) return nullptr;
        at = range.location + range.length;
    }
    return static_cast<CFAttributedStringRef>(selected.release());
}

inline CFAttributedStringRef SelectWidth(CFAttributedStringRef input, CGFloat em) {
    const auto length = CFAttributedStringGetLength(input);
    if (!length) return static_cast<CFAttributedStringRef>(CFRetain(input));
    auto text = CFAttributedStringGetString(input);
    auto count = UnitCount(text);
    if (count < 2) return static_cast<CFAttributedStringRef>(CFRetain(input));
    Owner natural(CTLineCreateWithAttributedString(input), Release);
    if (!natural) return nullptr;
    if (CTLineGetTypographicBounds(static_cast<CTLineRef>(natural.get()), nullptr, nullptr, nullptr) <= em)
        return static_cast<CFAttributedStringRef>(CFRetain(input));
    std::vector<CFStringRef> tags = count == 2 ? std::vector<CFStringRef>{CFSTR("hwid"), CFSTR("twid"), CFSTR("qwid")} :
        count == 3 ? std::vector<CFStringRef>{CFSTR("twid"), CFSTR("hwid"), CFSTR("qwid")} :
        std::vector<CFStringRef>{CFSTR("qwid"), CFSTR("twid"), CFSTR("hwid")};
    for (auto tag : tags) if (auto selected = TryWidth(input, static_cast<CTLineRef>(natural.get()), tag)) return selected;
    return static_cast<CFAttributedStringRef>(CFRetain(input));
}

inline CFAttributedStringRef HorizontalText(CFAttributedStringRef input, CFRange range, int32_t direction, CGFloat em) {
    Owner piece(CFAttributedStringCreateWithSubstring(kCFAllocatorDefault, input, range), Release);
    Owner owner(piece ? CFAttributedStringCreateMutableCopy(kCFAllocatorDefault, 0, static_cast<CFAttributedStringRef>(piece.get())) : nullptr, Release);
    if (!owner) return nullptr;
    auto text = const_cast<CFMutableAttributedStringRef>(static_cast<CFAttributedStringRef>(owner.get()));
    auto whole = CFRangeMake(0, range.length);
    for (auto key : {CFSTR("JaliumSpanIndex"), kCTBaselineClassAttributeName, kCTBaselineReferenceInfoAttributeName,
        kCTBaselineInfoAttributeName, kCTRunDelegateAttributeName}) CFAttributedStringRemoveAttribute(text, whole, key);
    CFAttributedStringSetAttribute(text, whole, kCTVerticalFormsAttributeName, kCFBooleanFalse);
    CTWritingDirection dir = direction < 0 ? kCTWritingDirectionNatural : direction ? kCTWritingDirectionRightToLeft : kCTWritingDirectionLeftToRight;
    CTParagraphStyleSetting setting{kCTParagraphStyleSpecifierBaseWritingDirection, sizeof(dir), &dir};
    Owner style(CTParagraphStyleCreate(&setting, 1), Release);
    if (!style) return nullptr;
    CFAttributedStringSetAttribute(text, whole, kCTParagraphStyleAttributeName, style.get());
    // Forced line breaks have no horizontal composition advance. Remove them
    // privately; the public source map still includes every original code unit.
    for (CFIndex at = range.length; at > 0;) {
        auto c = CFStringGetCharacterAtIndex(CFAttributedStringGetString(text), --at);
        if (c == '\r' || c == '\n' || c == 0x2028 || c == 0x2029 || c == 0xfeff || c == 0x00ad || c == 0x200b)
            CFAttributedStringReplaceString(text, CFRangeMake(at, 1), CFSTR(""));
    }
    // Multiple full-width units regain their proportional forms before width
    // substitution, including source transformed by text-transform:full-width.
    auto string = CFAttributedStringGetString(text); const auto length = CFStringGetLength(string);
    if (UnitCount(string) > 1) {
        for (CFIndex at = 0; at < length; ++at) {
            UniChar c = CFStringGetCharacterAtIndex(CFAttributedStringGetString(text), at);
            if ((c >= 0xff01 && c <= 0xff5e) || c == 0x3000) {
                c = c == 0x3000 ? 0x20 : c - 0xfee0;
                Owner value(CFStringCreateWithCharacters(kCFAllocatorDefault, &c, 1), Release);
                if (!value) return nullptr;
                CFAttributedStringReplaceString(text, CFRangeMake(at, 1), static_cast<CFStringRef>(value.get()));
            }
        }
    }
    return SelectWidth(text, em);
}
} // namespace text_combine
} // namespace jalium
#endif
