#include "apple_vertical_features.h"
#ifdef __APPLE__
#include "opentype_vertical_alias.h"
#include "apple_vertical_orientation.h"
#include <CoreGraphics/CoreGraphics.h>
#include <cstring>
#include <list>
#include <map>
#include <memory>
#include <mutex>
#include <tuple>

namespace jalium {
namespace {
void Release(const void* value) { if (value) CFRelease(value); }
using Owner = std::unique_ptr<const void, decltype(&Release)>;
constexpr size_t MaxFaceBytes = 256 * 1024 * 1024, CacheBytes = 32 * 1024 * 1024;
struct Face {
    CGFontRef source = nullptr, mapped = nullptr;
    font::VerticalAliases aliases;
    size_t bytes = 0;
    ~Face() { if (mapped) CGFontRelease(mapped); if (source) CGFontRelease(source); }
};
uint32_t Checksum(const std::vector<uint8_t>& bytes) {
    uint32_t sum = 0;
    for (size_t i = 0; i < bytes.size(); i += 4) {
        uint32_t value = 0;
        for (size_t j = 0; j < 4; ++j) value = (value << 8) | (i + j < bytes.size() ? bytes[i + j] : 0);
        sum += value;
    }
    return sum;
}
std::shared_ptr<Face> MakeFace(CGFontRef graphics) {
    auto result = std::make_shared<Face>(); result->source = CGFontRetain(graphics);
    Owner table(CGFontCopyTableForTag(graphics, 0x47535542), Release);
    if (!table) return result;
    const auto data = static_cast<CFDataRef>(table.get());
    if (CFDataGetLength(data) < 10 || size_t(CFDataGetLength(data)) > MaxFaceBytes) return nullptr;
    std::vector<uint8_t> gsub(CFDataGetBytePtr(data), CFDataGetBytePtr(data) + CFDataGetLength(data));
    if (!font::AliasVerticalFeatures(gsub, result->aliases)) return nullptr;
    if (!result->aliases.Any()) return result;
    Owner available(CGFontCopyTableTags(graphics), Release);
    if (!available || CFGetTypeID(available.get()) != CFArrayGetTypeID()) return nullptr;
    const auto tags = static_cast<CFArrayRef>(available.get());
    if (CFArrayGetCount(tags) > 4095) return nullptr;
    std::map<uint32_t, std::vector<uint8_t>> tables;
    size_t total = 0;
    for (CFIndex i = 0; i < CFArrayGetCount(tags); ++i) {
        // CGFont's table-tag array stores raw four-byte values, not CFNumber.
        const auto tag = static_cast<uint32_t>(reinterpret_cast<uintptr_t>(CFArrayGetValueAtIndex(tags, i)));
        if (tag == 0x44534947) continue; // Original DSIG cannot sign modified layout metadata.
        Owner original(CGFontCopyTableForTag(graphics, tag), Release);
        if (!original || CFGetTypeID(original.get()) != CFDataGetTypeID()) return nullptr;
        auto value = static_cast<CFDataRef>(original.get()); auto length = CFDataGetLength(value);
        if (length < 0 || size_t(length) > MaxFaceBytes - total) return nullptr;
        auto& output = tables[tag];
        if (tag == 0x47535542) output = gsub;
        else output.assign(CFDataGetBytePtr(value), CFDataGetBytePtr(value) + length);
        total += size_t(length);
    }
    auto head = tables.find(0x68656164);
    if (head == tables.end() || head->second.size() < 12 || !tables.count(0x47535542)) return nullptr;
    font::VerticalWrite32(head->second, 8, 0);
    const uint16_t count = static_cast<uint16_t>(tables.size());
    uint16_t power = 1, exponent = 0;
    while (power * 2 <= count) { power *= 2; ++exponent; }
    std::vector<uint8_t> sfnt(12 + size_t(count) * 16);
    font::VerticalWrite32(sfnt, 0, tables.count(0x43464620) || tables.count(0x43464632) ? 0x4f54544f : 0x00010000);
    font::VerticalWrite16(sfnt, 4, count); font::VerticalWrite16(sfnt, 6, power * 16);
    font::VerticalWrite16(sfnt, 8, exponent); font::VerticalWrite16(sfnt, 10, count * 16 - power * 16);
    size_t record = 12, headOffset = 0;
    for (const auto& [tag, bytes] : tables) {
        if (bytes.size() > MaxFaceBytes - 3 || sfnt.size() > MaxFaceBytes - bytes.size() - 3) return nullptr;
        if (tag == 0x68656164) headOffset = sfnt.size();
        font::VerticalWrite32(sfnt, record, tag); font::VerticalWrite32(sfnt, record + 4, Checksum(bytes));
        font::VerticalWrite32(sfnt, record + 8, uint32_t(sfnt.size())); font::VerticalWrite32(sfnt, record + 12, uint32_t(bytes.size()));
        sfnt.insert(sfnt.end(), bytes.begin(), bytes.end()); sfnt.resize((sfnt.size() + 3) & ~size_t(3)); record += 16;
    }
    font::VerticalWrite32(sfnt, headOffset + 8, 0xb1b0afba - Checksum(sfnt));
    Owner buffer(CFDataCreate(kCFAllocatorDefault, sfnt.data(), sfnt.size()), Release);
    Owner provider(buffer ? CGDataProviderCreateWithCFData(static_cast<CFDataRef>(buffer.get())) : nullptr, Release);
    result->mapped = provider ? CGFontCreateWithDataProvider(static_cast<CGDataProviderRef>(const_cast<void*>(provider.get()))) : nullptr;
    if (!result->mapped) return nullptr;
    result->bytes = sfnt.size();
    return result;
}
std::shared_ptr<Face> CachedFace(CGFontRef graphics) {
    // Cache source faces, not point sizes or variation requests. The immutable
    // rebuilt face retains the same outlines and all original variable tables.
    static std::mutex mutex;
    static std::list<std::shared_ptr<Face>> cache;
    static size_t size = 0;
    std::lock_guard lock(mutex);
    for (auto entry = cache.begin(); entry != cache.end(); ++entry) if (CFEqual((*entry)->source, graphics)) {
        auto result = *entry; cache.splice(cache.begin(), cache, entry); return result;
    }
    auto result = MakeFace(graphics);
    if (!result || result->bytes > CacheBytes) return result;
    while (!cache.empty() && (cache.size() >= 32 || size + result->bytes > CacheBytes)) {
        size -= cache.back()->bytes; cache.pop_back();
    }
    cache.push_front(result); size += result->bytes;
    return result;
}
CTFontRef FontWithFeatures(CTFontRef input, CGFontRef graphics, CFArrayRef settings, CFArrayRef cascade) {
    auto matrix = CTFontGetMatrix(input);
    Owner base(CTFontCreateWithGraphicsFont(graphics, CTFontGetSize(input), &matrix, nullptr), Release);
    Owner descriptor(base ? CTFontCopyFontDescriptor(static_cast<CTFontRef>(base.get())) : nullptr, Release);
    Owner changesOwner(CFDictionaryCreateMutable(kCFAllocatorDefault, 0,
        &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks), Release);
    auto changes = const_cast<CFMutableDictionaryRef>(static_cast<CFDictionaryRef>(changesOwner.get()));
    if (!descriptor || !changes) return nullptr;
    // Keep the data descriptor of the graphics face. A variable run's old
    // instance descriptor can select a system fallback against a new face.
    const CFStringRef inherited[] = {kCTFontVariationAttribute, kCTFontOpticalSizeAttribute,
        kCTFontTraitsAttribute, kCTFontCascadeListAttribute, kCTFontCharacterSetAttribute,
        kCTFontLanguagesAttribute, kCTFontBaselineAdjustAttribute, kCTFontFixedAdvanceAttribute,
        kCTFontOrientationAttribute};
    for (auto attribute : inherited) {
        Owner value(CTFontCopyAttribute(input, attribute), Release);
        if (value) CFDictionarySetValue(changes, attribute, value.get());
    }
    Owner sourceDescriptor(CTFontCopyFontDescriptor(input), Release);
    Owner sourceAttributes(sourceDescriptor ? CTFontDescriptorCopyAttributes(
        static_cast<CTFontDescriptorRef>(sourceDescriptor.get())) : nullptr, Release);
    if (sourceAttributes) {
        auto dict = static_cast<CFDictionaryRef>(sourceAttributes.get());
        std::vector<const void*> keys(CFDictionaryGetCount(dict)), values(keys.size());
        CFDictionaryGetKeysAndValues(dict, keys.data(), values.data());
        for (size_t i = 0; i < keys.size(); ++i)
            if (CFGetTypeID(keys[i]) == CFStringGetTypeID() &&
                CFStringHasPrefix(static_cast<CFStringRef>(keys[i]), CFSTR("Jalium")))
                CFDictionarySetValue(changes, keys[i], values[i]);
    }
    CFDictionarySetValue(changes, kCTFontFeatureSettingsAttribute, settings);
    if (cascade) CFDictionarySetValue(changes, kCTFontCascadeListAttribute, cascade);
    Owner selected(CTFontDescriptorCreateCopyWithAttributes(
        static_cast<CTFontDescriptorRef>(descriptor.get()), changes), Release);
    return selected ? CTFontCreateCopyWithAttributes(static_cast<CTFontRef>(base.get()), 0, nullptr,
        static_cast<CTFontDescriptorRef>(selected.get())) : nullptr;
}
CTFontRef SelectFont(CTFontRef input, int64_t vertical, int64_t rotation) {
    Owner graphics(CTFontCopyGraphicsFont(input, nullptr), Release);
    if (!graphics) return nullptr;
    auto face = CachedFace(static_cast<CGFontRef>(const_cast<void*>(graphics.get())));
    if (!face) return nullptr;
    if (!face->aliases.Any()) return static_cast<CTFontRef>(CFRetain(input));
    Owner previous(CTFontCopyAttribute(input, kCTFontFeatureSettingsAttribute), Release);
    Owner listOwner(CFArrayCreateMutable(kCFAllocatorDefault, 0, &kCFTypeArrayCallBacks), Release);
    auto list = const_cast<CFMutableArrayRef>(static_cast<CFArrayRef>(listOwner.get()));
    if (!list) return nullptr;
    if (previous && CFGetTypeID(previous.get()) == CFArrayGetTypeID()) {
        auto settings = static_cast<CFArrayRef>(previous.get());
        for (CFIndex i = 0; i < CFArrayGetCount(settings); ++i) {
            auto setting = static_cast<CFDictionaryRef>(CFArrayGetValueAtIndex(settings, i));
            if (!setting || CFGetTypeID(setting) != CFDictionaryGetTypeID()) return nullptr;
            auto tag = CFDictionaryGetValue(setting, kCTFontOpenTypeFeatureTag);
            int32_t type = -1; auto number = static_cast<CFNumberRef>(CFDictionaryGetValue(setting, kCTFontFeatureTypeIdentifierKey));
            if (number && CFGetTypeID(number) == CFNumberGetTypeID()) CFNumberGetValue(number, kCFNumberSInt32Type, &type);
            if (type == 4 || (tag && (CFEqual(tag, CFSTR("vert")) || CFEqual(tag, CFSTR("vrt2"))))) continue;
            CFArrayAppendValue(list, setting);
        }
    }
    for (unsigned kind = 0; kind < 2; ++kind) {
        auto value = kind == 0 ? vertical : rotation; const auto mapped = face->aliases.tags[kind];
        if (!mapped) continue;
        const UInt8 bytes[] = {UInt8(mapped >> 24), UInt8(mapped >> 16), UInt8(mapped >> 8), UInt8(mapped)};
        Owner tag(CFStringCreateWithBytes(kCFAllocatorDefault, bytes, 4, kCFStringEncodingASCII, false), Release);
        Owner number(CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt64Type, &value), Release);
        const void* keys[] = {kCTFontOpenTypeFeatureTag, kCTFontOpenTypeFeatureValue};
        const void* values[] = {tag.get(), number.get()};
        Owner setting(tag && number ? CFDictionaryCreate(kCFAllocatorDefault, keys, values, 2,
            &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks) : nullptr, Release);
        if (!setting) return nullptr;
        CFArrayAppendValue(list, setting.get());
    }
    return FontWithFeatures(input, face->mapped, list, nullptr);
}
} // namespace
CTFontRef CopyAppleFontFeatures(CTFontRef input, CFArrayRef settings, CFArrayRef cascade) {
    Owner graphics(CTFontCopyGraphicsFont(input, nullptr), Release);
    if (!graphics) return nullptr;
    Owner axes(CTFontCopyVariationAxes(input), Release);
    if (axes && CFArrayGetCount(static_cast<CFArrayRef>(axes.get())))
        return FontWithFeatures(input, static_cast<CGFontRef>(const_cast<void*>(graphics.get())), settings, cascade);
    // Static faces retain their existing descriptor metadata. Rebuilding all
    // descriptors changes CoreText's unicase/small-cap shaping boundaries.
    // Only variable faces need a new identity to avoid named-instance fallback.
    Owner original(CTFontCopyFontDescriptor(input), Release);
    const void* clearKey = kCTFontFeatureSettingsAttribute; const void* clearValue = kCFNull;
    Owner clear(CFDictionaryCreate(kCFAllocatorDefault, &clearKey, &clearValue, 1,
        &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks), Release);
    Owner cleared(original && clear ? CTFontDescriptorCreateCopyWithAttributes(
        static_cast<CTFontDescriptorRef>(original.get()), static_cast<CFDictionaryRef>(clear.get())) : nullptr, Release);
    const void* keys[] = {kCTFontFeatureSettingsAttribute, kCTFontCascadeListAttribute};
    const void* values[] = {settings, cascade};
    Owner changes(CFDictionaryCreate(kCFAllocatorDefault, keys, values, cascade ? 2 : 1,
        &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks), Release);
    Owner descriptor(cleared && changes ? CTFontDescriptorCreateCopyWithAttributes(
        static_cast<CTFontDescriptorRef>(cleared.get()), static_cast<CFDictionaryRef>(changes.get())) : nullptr, Release);
    auto matrix = CTFontGetMatrix(input);
    return descriptor ? CTFontCreateWithGraphicsFont(static_cast<CGFontRef>(const_cast<void*>(graphics.get())),
        CTFontGetSize(input), &matrix, static_cast<CTFontDescriptorRef>(descriptor.get())) : nullptr;
}
CFAttributedStringRef CreateAppleVerticalFeatureText(CFAttributedStringRef input, bool upright) {
    if (!CFAttributedStringGetLength(input)) return static_cast<CFAttributedStringRef>(CFRetain(input));
    Owner outputOwner(CFAttributedStringCreateMutableCopy(kCFAllocatorDefault, 0, input), Release);
    Owner baseline(CTLineCreateWithAttributedString(input), Release);
    if (!outputOwner || !baseline) return nullptr;
    auto output = const_cast<CFMutableAttributedStringRef>(static_cast<CFAttributedStringRef>(outputOwner.get()));
    std::map<std::tuple<CTFontRef, int64_t, int64_t>, Owner> fonts;
    auto runs = CTLineGetGlyphRuns(static_cast<CTLineRef>(baseline.get()));
    for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
        auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i));
        auto range = CTRunGetStringRange(run);
        auto attributes = CTRunGetAttributes(run);
        if (CFDictionaryContainsKey(attributes, kCTRunDelegateAttributeName)) continue;
        if (!upright && AppleSourceTextOrientation(input, range.location) != 1 &&
            CFDictionaryGetValue(attributes, kCTVerticalFormsAttributeName) != kCFBooleanTrue) continue;
        auto font = static_cast<CTFontRef>(CFDictionaryGetValue(attributes, kCTFontAttributeName));
        if (!font) return nullptr;
        std::array<int64_t, 2> values{-1, -1};
        auto data=static_cast<CFDataRef>(CFAttributedStringGetAttribute(input,range.location,CFSTR("JaliumVerticalFeatureOverrides"),nullptr));
        if(data && CFGetTypeID(data)==CFDataGetTypeID() && CFDataGetLength(data)==sizeof(int64_t)*2)
            std::memcpy(values.data(),CFDataGetBytePtr(data),sizeof(int64_t)*2);
        const int64_t vertical = values[0] < 0 ? 1 : values[0], rotation = values[1] < 0 ? 0 : values[1];
        auto key = std::make_tuple(font, vertical, rotation);
        auto found = fonts.find(key);
        if (found == fonts.end()) found = fonts.emplace(key, Owner(SelectFont(font, vertical, rotation), Release)).first;
        if (!found->second) return nullptr;
        CFAttributedStringSetAttribute(output, range, kCTFontAttributeName, found->second.get());
    }
    return static_cast<CFAttributedStringRef>(outputOwner.release());
}
} // namespace jalium
#endif
