#pragma once
#ifdef __APPLE__
#include <CoreText/CoreText.h>
#include <algorithm>
#include <cmath>
#include <map>
#include <memory>
#include <tuple>
#include <vector>

namespace jalium {
inline void SetApplePositionAttributes(CFMutableAttributedStringRef text, CFRange range,
    uint32_t variant, uint32_t flags, uint32_t runMode, int64_t featureOverride = -1)
{
    if (!variant || !range.length) return;
    const CFStringRef keys[] = {CFSTR("JaliumPositionVariant"), CFSTR("JaliumPositionFlags"), CFSTR("JaliumPositionRun")};
    const int32_t values[] = {static_cast<int32_t>(variant), static_cast<int32_t>(flags), static_cast<int32_t>(runMode)};
    for (int i = 0; i < 3; ++i) {
        auto number = CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt32Type, &values[i]);
        if (!number) throw std::bad_alloc();
        CFAttributedStringSetAttribute(text, range, keys[i], number); CFRelease(number);
    }
    if (flags & 2) {
        if (featureOverride < 0) featureOverride = (flags & 4) ? 1 : 0;
        auto number = CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt64Type, &featureOverride);
        if (!number) throw std::bad_alloc();
        CFAttributedStringSetAttribute(text, range, CFSTR("JaliumPositionFeatureOverride"), number); CFRelease(number);
    } else CFAttributedStringRemoveAttribute(text, range, CFSTR("JaliumPositionFeatureOverride"));
}

// Position synthesis changes attributes only. Every source UTF-16 unit remains
// intact, and the immutable resolution table can be reused after line fitting.
inline CFAttributedStringRef CreateApplePositionText(CFAttributedStringRef input, std::vector<uint8_t>* modes = nullptr)
{
    const CFIndex length = CFAttributedStringGetLength(input);
    bool requested = false;
    for (CFIndex at = 0; at < length;) {
        CFRange range{};
        if (CFAttributedStringGetAttribute(input, at, CFSTR("JaliumPositionVariant"), &range)) { requested = true; break; }
        if (range.length <= 0) break;
        at = range.location + range.length;
    }
    if (modes) modes->assign(static_cast<size_t>(length), 0);
    if (!requested) return static_cast<CFAttributedStringRef>(CFRetain(input));
    auto release = [](const void* value) { if (value) CFRelease(value); };
    using Owner = std::unique_ptr<const void, decltype(release)>;
    Owner baseline(CTLineCreateWithAttributedString(input), release);
    Owner outputOwner(CFAttributedStringCreateMutableCopy(kCFAllocatorDefault, 0, input), release);
    auto output = const_cast<CFMutableAttributedStringRef>(static_cast<CFAttributedStringRef>(outputOwner.get()));
    if (!baseline || !output) return nullptr;
    auto runs = CTLineGetGlyphRuns(static_cast<CTLineRef>(baseline.get()));
    // Resolve the complete original run before choosing native or synthesized
    // forms, including actual fallback faces and private font resources.
    for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
        auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i));
        auto font = CFDictionaryGetValue(CTRunGetAttributes(run), kCTFontAttributeName);
        if (font) CFAttributedStringSetAttribute(output, CTRunGetStringRange(run), kCTFontAttributeName, font);
    }
    std::map<std::tuple<CTFontRef, uint32_t, int64_t, bool>, std::shared_ptr<const void>> fonts;
    auto selectedFont = [&](CTFontRef font, uint32_t variant, int64_t enabled, bool synthetic) -> CTFontRef {
        auto key = std::make_tuple(font, variant, enabled, synthetic);
        if (auto found = fonts.find(key); found != fonts.end()) return static_cast<CTFontRef>(found->second.get());
        auto tag = variant == 1 ? CFSTR("subs") : CFSTR("sups");
        Owner previous(CTFontCopyAttribute(font, kCTFontFeatureSettingsAttribute), release);
        Owner listOwner(CFArrayCreateMutable(kCFAllocatorDefault, 0, &kCFTypeArrayCallBacks), release);
        auto list = const_cast<CFMutableArrayRef>(static_cast<CFArrayRef>(listOwner.get()));
        if (!list) return nullptr;
        if (previous && CFGetTypeID(previous.get()) == CFArrayGetTypeID()) {
            auto settings = static_cast<CFArrayRef>(previous.get());
            for (CFIndex i = 0; i < CFArrayGetCount(settings); ++i) {
                auto setting = static_cast<CFDictionaryRef>(CFArrayGetValueAtIndex(settings, i));
                auto old = CFDictionaryGetValue(setting, kCTFontOpenTypeFeatureTag);
                if (!old || !CFEqual(old, tag)) CFArrayAppendValue(list, setting);
            }
        }
        Owner number(CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt64Type, &enabled), release);
        const void* keys[] = {kCTFontOpenTypeFeatureTag, kCTFontOpenTypeFeatureValue};
        const void* values[] = {tag, number.get()};
        Owner setting(number ? CFDictionaryCreate(kCFAllocatorDefault, keys, values, 2,
            &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks) : nullptr, release);
        if (!setting) return nullptr;
        CFArrayAppendValue(list, setting.get());
        const void* attribute = kCTFontFeatureSettingsAttribute; const void* value = list;
        Owner changes(CFDictionaryCreate(kCFAllocatorDefault, &attribute, &value, 1,
            &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks), release);
        Owner original(CTFontCopyFontDescriptor(font), release);
        const void* clearValue = kCFNull;
        Owner clear(CFDictionaryCreate(kCFAllocatorDefault, &attribute, &clearValue, 1,
            &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks), release);
        Owner cleared(original && clear ? CTFontDescriptorCreateCopyWithAttributes(
            static_cast<CTFontDescriptorRef>(original.get()), static_cast<CFDictionaryRef>(clear.get())) : nullptr, release);
        Owner descriptor(cleared && changes ? CTFontDescriptorCreateCopyWithAttributes(
            static_cast<CTFontDescriptorRef>(cleared.get()), static_cast<CFDictionaryRef>(changes.get())) : nullptr, release);
        Owner graphics(CTFontCopyGraphicsFont(font, nullptr), release);
        auto matrix = CTFontGetMatrix(font);
        double size = CTFontGetSize(font);
        // Small text uses a gentler reduction to keep script forms legible.
        if (synthetic) size = static_cast<float>(size * std::clamp(.65 + (20 - size) * .015, .65, .8));
        auto selected = descriptor && graphics ? CTFontCreateWithGraphicsFont(static_cast<CGFontRef>(const_cast<void*>(graphics.get())),
            size, &matrix, static_cast<CTFontDescriptorRef>(descriptor.get())) : nullptr;
        if (selected) fonts.emplace(key, std::shared_ptr<const void>(selected, release));
        return selected;
    };
    auto nativePiece = [&](CFRange range, uint32_t variant, int32_t enabled) -> CFAttributedStringRef {
        Owner source(CFAttributedStringCreateWithSubstring(kCFAllocatorDefault, output, range), release);
        auto result = source ? CFAttributedStringCreateMutableCopy(kCFAllocatorDefault, 0,
            static_cast<CFAttributedStringRef>(source.get())) : nullptr;
        if (!result) return nullptr;
        for (CFIndex at = 0; at < range.length;) {
            CFRange fontRange{};
            auto font = static_cast<CTFontRef>(CFAttributedStringGetAttribute(result, at, kCTFontAttributeName, &fontRange));
            auto selected = font ? selectedFont(font, variant, enabled, false) : nullptr;
            if (!selected || fontRange.length <= 0) { CFRelease(result); return nullptr; }
            CFAttributedStringSetAttribute(result, fontRange, kCTFontAttributeName, selected);
            at = fontRange.location + fontRange.length;
        }
        return result;
    };
    auto glyphs = [&](CFAttributedStringRef text) {
        Owner line(CTLineCreateWithAttributedString(text), release);
        if (!line) throw std::bad_alloc();
        std::vector<std::tuple<CGGlyph, double, double, double, double>> result;
        auto list = CTLineGetGlyphRuns(static_cast<CTLineRef>(line.get()));
        for (CFIndex i = 0; i < CFArrayGetCount(list); ++i) {
            auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(list, i));
            for (CFIndex j = 0; j < CTRunGetGlyphCount(run); ++j) {
                CGGlyph glyph{}; CGPoint point{}; CGSize advance{};
                CTRunGetGlyphs(run, CFRangeMake(j, 1), &glyph); CTRunGetPositions(run, CFRangeMake(j, 1), &point);
                CTRunGetAdvances(run, CFRangeMake(j, 1), &advance);
                result.emplace_back(glyph, point.x, point.y, advance.width, advance.height);
            }
        }
        return result;
    };
    auto string = CFAttributedStringGetString(input);
    for (CFIndex start = 0; start < length;) {
        CFRange vr{}, fr{}, rr{}; int32_t variant = 0, flags = 1, forced = 0;
        auto number = static_cast<CFNumberRef>(CFAttributedStringGetAttributeAndLongestEffectiveRange(input, start,
            CFSTR("JaliumPositionVariant"), CFRangeMake(0,length), &vr));
        if (!number) { start = vr.location + vr.length; continue; }
        CFNumberGetValue(number, kCFNumberSInt32Type, &variant);
        number = static_cast<CFNumberRef>(CFAttributedStringGetAttributeAndLongestEffectiveRange(input, start,
            CFSTR("JaliumPositionFlags"), CFRangeMake(0,length), &fr));
        if (number) CFNumberGetValue(number, kCFNumberSInt32Type, &flags); else fr = vr;
        number = static_cast<CFNumberRef>(CFAttributedStringGetAttributeAndLongestEffectiveRange(input, start,
            CFSTR("JaliumPositionRun"), CFRangeMake(0,length), &rr));
        if (number) CFNumberGetValue(number, kCFNumberSInt32Type, &forced); else rr = vr;
        CFIndex end = std::min({vr.location + vr.length, fr.location + fr.length, rr.location + rr.length});
        if (end <= start || variant < 1 || variant > 2 || flags < 0 || flags > 7 || forced < 0 || forced > 3) return nullptr;
        // A paragraph separator ends the contiguous script run.
        auto isBreak = [](UniChar c) { return c == '\n' || c == '\r' || c == 0x2028 || c == 0x2029 || c == 0xfffc; };
        if (isBreak(CFStringGetCharacterAtIndex(string, start))) { ++start; continue; }
        for (CFIndex at = start; at < end; ++at) if (isBreak(CFStringGetCharacterAtIndex(string, at))) { end = at; break; }
        uint8_t mode = static_cast<uint8_t>(forced);
        if (!mode) {
            bool available = true;
            for (CFIndex at = start; at < end && available;) {
                auto cluster = CFStringGetRangeOfComposedCharactersAtIndex(string, at);
                CFIndex clusterEnd = std::min(end, cluster.location + cluster.length);
                cluster.location = std::max(cluster.location, start); cluster.length = clusterEnd - cluster.location;
                if (cluster.length <= 0) return nullptr;
                bool blank = true;
                for (CFIndex i = cluster.location; i < cluster.location + cluster.length; ++i) {
                    UniChar ch = CFStringGetCharacterAtIndex(string, i);
                    blank &= CFCharacterSetIsCharacterMember(CFCharacterSetGetPredefined(kCFCharacterSetWhitespaceAndNewline), ch) ||
                        ch == 0x00ad || ch == 0x061c || (ch >= 0x200b && ch <= 0x200f) ||
                        (ch >= 0x202a && ch <= 0x202e) || (ch >= 0x2060 && ch <= 0x206f) || ch == 0xfeff;
                }
                if (!blank) {
                    Owner normal(nativePiece(cluster, variant, 0), release), selected(nativePiece(cluster, variant, 1), release);
                    if (!normal || !selected) return nullptr;
                    available = glyphs(static_cast<CFAttributedStringRef>(normal.get())) != glyphs(static_cast<CFAttributedStringRef>(selected.get()));
                }
                at = cluster.location + cluster.length;
            }
            mode = available ? 1 : (flags & 1) ? 2 : 3;
        }
        if (modes) std::fill(modes->begin() + start, modes->begin() + end, mode);
        for (CFIndex at = start; at < end;) {
            CFRange range{};
            auto font = static_cast<CTFontRef>(CFAttributedStringGetAttribute(output, at, kCTFontAttributeName, &range));
            CFIndex next = std::min(end, range.location + range.length);
            int64_t enabled = mode == 2 ? 0 : 1;
            if (flags & 2) {
                CFRange overrideRange{};
                auto value = static_cast<CFNumberRef>(CFAttributedStringGetAttributeAndLongestEffectiveRange(input, at,
                    CFSTR("JaliumPositionFeatureOverride"), CFRangeMake(start, end - start), &overrideRange));
                enabled = (flags & 4) ? 1 : 0;
                if (value && !CFNumberGetValue(value, kCFNumberSInt64Type, &enabled)) return nullptr;
                if (enabled < 0 || enabled > UINT32_MAX) return nullptr;
                next = std::min(next, overrideRange.location + overrideRange.length);
            }
            if (!font || next <= at) return nullptr;
            range = CFRangeMake(at, next - at);
            CFAttributedStringSetAttribute(output, range, CFSTR("JaliumPositionBaseFont"), font);
            if (mode != 3) {
                // Low-level element settings are the final feature layer, even
                // when the high-level position needed whole-run synthesis.
                auto selected = selectedFont(font, variant, enabled, mode == 2); if (!selected) return nullptr;
                CFAttributedStringSetAttribute(output, range, kCTFontAttributeName, selected);
                if (mode == 2) {
                    CGFloat shift = CTFontGetSize(font) * (variant == 1 ? -.2 : .35);
                    Owner offset(CFNumberCreate(kCFAllocatorDefault, kCFNumberCGFloatType, &shift), release);
                    if (!offset) return nullptr;
                    CFAttributedStringSetAttribute(output, range, kCTBaselineOffsetAttributeName, offset.get());
                }
            }
            at = next;
        }
        start = end;
    }
    return static_cast<CFAttributedStringRef>(outputOwner.release());
}
} // namespace jalium
#endif
