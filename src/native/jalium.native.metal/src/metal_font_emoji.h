#pragma once
#ifdef __APPLE__
#include "metal_font_caps.h"
#include "jalium_emoji_data.h"

namespace jalium {
inline void SetAppleEmojiAttributes(CFMutableAttributedStringRef text, CFRange span, uint32_t variant, uint32_t after)
{
    if (!span.length || (!variant && !after)) return;
    int32_t values[] = {static_cast<int32_t>(variant), static_cast<int32_t>(after)};
    CFStringRef keys[] = {CFSTR("JaliumEmojiVariant"), CFSTR("JaliumEmojiAfter")};
    for (unsigned i = 0; i < 2; ++i) {
        auto number = CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt32Type, &values[i]);
        if (!number) throw std::bad_alloc();
        CFAttributedStringSetAttribute(text, span, keys[i], number); CFRelease(number);
    }
}

// Insert presentation selectors into paint text only. The public UTF-16 text,
// grapheme ownership, caret positions and selection ranges remain source based.
inline CFAttributedStringRef CreateAppleEmojiText(CFAttributedStringRef input, AppleCapsMap& mapping, bool metrics = true)
{
    bool requested = false;
    CFIndex inputLength = CFAttributedStringGetLength(input);
    for (CFIndex at = 0; at < inputLength;) {
        CFRange range{};
        auto attributes = CFAttributedStringGetAttributes(input, at, &range);
        if (CFDictionaryContainsKey(attributes, CFSTR("JaliumEmojiVariant"))) { requested = true; break; }
        if (range.length <= 0) break;
        at = range.location + range.length;
    }
    if (!requested) return static_cast<CFAttributedStringRef>(CFRetain(input));
    auto release = [](const void* value) { if (value) CFRelease(value); };
    using Owner = std::unique_ptr<const void, decltype(release)>;
    Owner owned(CFAttributedStringCreateMutable(kCFAllocatorDefault, 0), release);
    if (!owned) return nullptr;
    auto output = const_cast<CFMutableAttributedStringRef>(static_cast<CFAttributedStringRef>(owned.get()));
    auto source = CFAttributedStringGetString(input);
    CFIndex length = CFStringGetLength(source);
    AppleCapsMap emoji;
    bool changed = false;
    auto numberAt = [&](CFIndex at, CFStringRef key) {
        int32_t value = 0;
        auto attribute = CFAttributedStringGetAttribute(input, at, key, nullptr);
        if (attribute && CFGetTypeID(attribute) == CFNumberGetTypeID())
            CFNumberGetValue(static_cast<CFNumberRef>(attribute), kCFNumberSInt32Type, &value);
        return value;
    };
    for (CFIndex start = 0; start < length;) {
        auto cluster = CFStringGetRangeOfComposedCharactersAtIndex(source, start);
        CFIndex paintStart = CFAttributedStringGetLength(output);
        for (CFIndex at = start; at < cluster.location + cluster.length;) {
            UniChar high = CFStringGetCharacterAtIndex(source, at);
            CFIndex size = 1; uint32_t scalar = high;
            if (CFStringIsSurrogateHighCharacter(high) && at + 1 < length) {
                auto low = CFStringGetCharacterAtIndex(source, at + 1);
                if (CFStringIsSurrogateLowCharacter(low)) { scalar = CFStringGetLongCharacterForSurrogatePair(high, low); size = 2; }
            }
            auto range = CFRangeMake(at, size);
            Owner piece(CFAttributedStringCreateWithSubstring(kCFAllocatorDefault, input, range), release);
            if (!piece) return nullptr;
            CFAttributedStringReplaceAttributedString(output, CFRangeMake(CFAttributedStringGetLength(output), 0), static_cast<CFAttributedStringRef>(piece.get()));
            bool explicitSelector = at + size < length &&
                (CFStringGetCharacterAtIndex(source, at + size) == 0xfe0e || CFStringGetCharacterAtIndex(source, at + size) == 0xfe0f);
            uint32_t selector = 0;
            if (!explicitSelector && emoji_data::Contains(emoji_data::Participating, scalar)) {
                int32_t after = at + size == length ? numberAt(at, CFSTR("JaliumEmojiAfter")) : 0;
                auto mode = numberAt(at, CFSTR("JaliumEmojiVariant"));
                selector = after == 1 ? 0xfe0e : after == 2 ? 0xfe0f : emoji_data::Selector(scalar, mode);
            }
            if (selector) {
                UniChar code = static_cast<UniChar>(selector);
                Owner value(CFStringCreateWithCharacters(kCFAllocatorDefault, &code, 1), release);
                Owner decorated(value ? CFAttributedStringCreate(kCFAllocatorDefault, static_cast<CFStringRef>(value.get()),
                    CFAttributedStringGetAttributes(input, at, nullptr)) : nullptr, release);
                if (!decorated) return nullptr;
                CFAttributedStringReplaceAttributedString(output, CFRangeMake(CFAttributedStringGetLength(output), 0), static_cast<CFAttributedStringRef>(decorated.get()));
                changed = true;
            }
            at += size;
        }
        emoji.parts.push_back({cluster, CFRangeMake(paintStart, CFAttributedStringGetLength(output) - paintStart)});
        start = cluster.location + cluster.length;
    }
    if (!changed) return static_cast<CFAttributedStringRef>(CFRetain(input));
    emoji.source = static_cast<CFStringRef>(CFRetain(source));
    if (mapping.Active()) {
        for (auto& part : mapping.parts) part.paint = emoji.PaintRange(part.paint);
    } else {
        mapping.source = static_cast<CFStringRef>(CFRetain(source));
        mapping.parts = std::move(emoji.parts);
    }
    if (metrics && mapping.original) {
        AppleCapsMap metric;
        auto original = CreateAppleEmojiText(mapping.original, metric, false);
        if (!original) return nullptr;
        CFRelease(mapping.original); mapping.original = original;
        mapping.metricParts = std::move(metric.parts);
    }
    return static_cast<CFAttributedStringRef>(CFRetain(output));
}
}
#endif
