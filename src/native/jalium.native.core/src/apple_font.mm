#include "jalium_apple_font.h"
#include "jalium_string_util.h"
#include <algorithm>
#include <cmath>
#include <cstring>
#include <tuple>
#include <utility>
#import <TargetConditionals.h>
#if TARGET_OS_OSX
#import <AppKit/AppKit.h>
#endif

namespace jalium {
namespace {
void ReleaseCF(const void* object) { if (object) CFRelease(object); }
CGFloat CoreTextWeight(int32_t weight)
{
    // AppKit's nine public system-font weights, with interpolation for the
    // numeric OpenType weights between them. A bold symbolic trait discards
    // Medium, Semibold, Heavy and every lighter weight.
    constexpr float values[] = {-1.0f, -.8f, -.6f, -.4f, 0, .23f, .3f, .4f, .56f, .62f, 1.0f};
    const int clamped = std::clamp(weight, 1, 1000);
    const int lower = std::min(clamped / 100, 9);
    return values[lower] + (static_cast<CGFloat>(values[lower + 1]) - values[lower]) *
        (clamped - lower * 100) / 100;
}

CGFloat FontTrait(CTFontRef font, CFStringRef key)
{
    CFDictionaryRef traits = CTFontCopyTraits(font);
    auto number = traits ? static_cast<CFNumberRef>(CFDictionaryGetValue(traits, key)) : nullptr;
    CGFloat value = 0;
    if (number && CFGetTypeID(number) == CFNumberGetTypeID())
        CFNumberGetValue(number, kCFNumberCGFloatType, &value);
    ReleaseCF(traits);
    return value;
}

CFDictionaryRef FindFontAxis(CFArrayRef axes, int32_t tag)
{
    if (!axes) return nullptr;
    for (CFIndex i = 0; i < CFArrayGetCount(axes); ++i) {
        auto axis = static_cast<CFDictionaryRef>(CFArrayGetValueAtIndex(axes, i));
        auto identifier = static_cast<CFNumberRef>(CFDictionaryGetValue(axis, kCTFontVariationAxisIdentifierKey));
        int32_t value = 0;
        if (identifier && CFNumberGetValue(identifier, kCFNumberSInt32Type, &value) && value == tag) return axis;
    }
    return nullptr;
}

CGFloat AxisValue(CFDictionaryRef axis, CFStringRef key)
{
    auto number = static_cast<CFNumberRef>(CFDictionaryGetValue(axis, key));
    CGFloat value = 0;
    if (number) CFNumberGetValue(number, kCFNumberCGFloatType, &value);
    return value;
}

bool HasOpenTypeWeightAxis(CFArrayRef axes)
{
    auto axis = FindFontAxis(axes, 0x77676874); // wght
    // Legacy AAT fonts such as Skia use relative coordinates (about 0.5-3),
    // rather than the OpenType/CSS weight scale. Use their named instances.
    return axis && AxisValue(axis, kCTFontVariationAxisDefaultValueKey) >= 100 &&
        AxisValue(axis, kCTFontVariationAxisMaximumValueKey) >= 100;
}

CFDictionaryRef OpenTypeWidthAxis(CFArrayRef axes)
{
    auto axis = FindFontAxis(axes, 0x77647468); // wdth; legacy AAT uses relative coordinates.
    return axis && AxisValue(axis, kCTFontVariationAxisDefaultValueKey) >= 10 ? axis : nullptr;
}

int FontWidthClass(CTFontRef font)
{
    CFDataRef table = CTFontCopyTable(font, kCTFontTableOS2, kCTFontTableOptionNoOptions);
    int value = 5;
    if (table && CFDataGetLength(table) >= 8) {
        const UInt8* bytes = CFDataGetBytePtr(table);
        int width = (bytes[6] << 8) | bytes[7];
        if (width >= 1 && width <= 9) value = width;
    }
    ReleaseCF(table); return value;
}

std::pair<CGFloat, CGFloat> FontWidthRange(CTFontRef font)
{
    CFArrayRef axes = CTFontCopyVariationAxes(font);
    auto axis = OpenTypeWidthAxis(axes);
    constexpr CGFloat widths[] = {50, 62.5, 75, 87.5, 100, 112.5, 125, 150, 200};
    CGFloat minimum = widths[FontWidthClass(font) - 1], maximum = minimum;
    if (axis) { minimum = AxisValue(axis, kCTFontVariationAxisMinimumValueKey); maximum = AxisValue(axis, kCTFontVariationAxisMaximumValueKey); }
    ReleaseCF(axes); return {minimum, maximum};
}

std::pair<int, CGFloat> WidthDistance(CTFontRef font, CGFloat requested)
{
    auto [minimum, maximum] = FontWidthRange(font);
    if (requested >= minimum && requested <= maximum) return {0, 0};
    const bool below = maximum < requested;
    return {below == (requested <= 100) ? 1 : 2, below ? requested - maximum : minimum - requested};
}

std::pair<CGFloat, CGFloat> FontWeightRange(CTFontRef font)
{
    CFArrayRef axes = CTFontCopyVariationAxes(font);
    auto axis = HasOpenTypeWeightAxis(axes) ? FindFontAxis(axes, 0x77676874) : nullptr;
    if (axis) {
        const auto range = std::make_pair(AxisValue(axis, kCTFontVariationAxisMinimumValueKey),
            AxisValue(axis, kCTFontVariationAxisMaximumValueKey));
        CFRelease(axes); return range;
    }
    ReleaseCF(axes);
    CFDataRef table = CTFontCopyTable(font, kCTFontTableOS2, kCTFontTableOptionNoOptions);
    int weight = 0;
    if (table && CFDataGetLength(table) >= 6) {
        const UInt8* bytes = CFDataGetBytePtr(table); weight = (bytes[4] << 8) | bytes[5];
    }
    ReleaseCF(table);
    if (weight < 1 || weight > 1000) {
        // Some legacy fonts omit useful OS/2 weights. Use the platform trait
        // scale until richer legacy instance descriptors are available.
        constexpr CGFloat values[] = {-1, -.8, -.6, -.4, 0, .23, .3, .4, .56, .62, 1};
        const CGFloat trait = std::clamp(FontTrait(font, kCTFontWeightTrait), values[0], values[10]);
        int lower = 0; while (lower < 9 && trait > values[lower + 1]) ++lower;
        weight = std::clamp(static_cast<int>(std::lround(100 * (lower + (trait - values[lower]) /
            (values[lower + 1] - values[lower])))), 1, 1000);
    }
    return {weight, weight};
}

std::pair<int, CGFloat> WeightDistance(CTFontRef font, int32_t requested)
{
    auto [minimum, maximum] = FontWeightRange(font);
    requested = std::clamp(requested, 1, 1000);
    if (requested >= minimum && requested <= maximum) return {0, 0};
    const bool below = maximum < requested;
    if (requested >= 400 && requested <= 500)
        return {!below && minimum <= 500 ? 1 : below ? 2 : 3,
            below ? requested - maximum : minimum - requested};
    return {below == (requested < 400) ? 1 : 2, below ? requested - maximum : minimum - requested};
}

using FontMatchScore = std::tuple<int, CGFloat, int, int, CGFloat>;
FontMatchScore MatchDistance(CTFontRef font, float width, int32_t weight, int32_t style)
{
    auto widthDistance = WidthDistance(font, width), weightDistance = WeightDistance(font, weight);
    CFArrayRef axes = CTFontCopyVariationAxes(font);
    const bool flexibleStyle = FindFontAxis(axes, 0x6974616c) || FindFontAxis(axes, 0x736c6e74);
    ReleaseCF(axes);
    const bool italic = (CTFontGetSymbolicTraits(font) & kCTFontItalicTrait) != 0;
    return {widthDistance.first, widthDistance.second, !flexibleStyle && italic != (style == 1 || style == 2),
        weightDistance.first, weightDistance.second};
}

CTFontRef CopyFontWithWidth(CTFontRef font, float size, float width)
{
    CFArrayRef axes = CTFontCopyVariationAxes(font);
    auto axis = OpenTypeWidthAxis(axes);
    if (!axis) { ReleaseCF(axes); return nullptr; }
    CGFloat value = std::clamp(static_cast<CGFloat>(width), AxisValue(axis, kCTFontVariationAxisMinimumValueKey),
        AxisValue(axis, kCTFontVariationAxisMaximumValueKey));
    CFDictionaryRef current = CTFontCopyVariation(font);
    CFMutableDictionaryRef variation = current ? CFDictionaryCreateMutableCopy(kCFAllocatorDefault, 0, current) :
        CFDictionaryCreateMutable(kCFAllocatorDefault, 0, &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    int32_t tag = 0x77647468;
    CFNumberRef identifier = CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt32Type, &tag);
    CFNumberRef number = CFNumberCreate(kCFAllocatorDefault, kCFNumberCGFloatType, &value);
    CFDictionarySetValue(variation, identifier, number);
    const void* key = kCTFontVariationAttribute; const void* item = variation;
    CFDictionaryRef attributes = CFDictionaryCreate(kCFAllocatorDefault, &key, &item, 1,
        &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    CTFontDescriptorRef descriptor = CTFontDescriptorCreateWithAttributes(attributes);
    CTFontRef result = CTFontCreateCopyWithAttributes(font, size, nullptr, descriptor);
    ReleaseCF(descriptor); ReleaseCF(attributes); ReleaseCF(number); ReleaseCF(identifier);
    ReleaseCF(variation); ReleaseCF(current); ReleaseCF(axes); return result;
}

CTFontRef CopyWidthFontWithStyle(CTFontRef font, float size, int32_t style)
{
    if (style == 0 || (CTFontGetSymbolicTraits(font) & kCTFontItalicTrait)) return nullptr;
    // Some width families (including SF's upright wdth face) have no italic
    // width face. Preserve the selected width and apply the default oblique
    // synthesis instead of switching back to a normal-width italic face.
    CGAffineTransform matrix = CTFontGetMatrix(font);
    matrix = CGAffineTransformConcat(CGAffineTransformMake(1, 0, std::tan(12.0 * M_PI / 180), 1, 0, 0), matrix);
    return CTFontCreateCopyWithAttributes(font, size, &matrix, nullptr);
}

CTFontRef SelectInstalledFontWidth(CTFontRef base, float size, int32_t weight, int32_t style, float width, bool systemFont)
{
#if TARGET_OS_OSX
    if (systemFont) {
        // A 100% request can use the real system italic face. Preserve
        // AppKit's UI weight and optical-size mapping for both styles.
        if (width == 100) return nullptr;
        // Match AppKit's numeric-weight mapping before varying its upright wdth
        // face. The separate italic UI face has no wdth axis on current macOS.
        NSFont* system = [NSFont systemFontOfSize:size weight:CoreTextWeight(weight)];
        return system ? CopyFontWithWidth((__bridge CTFontRef)system, size, width) : nullptr;
    }
#endif
    CFArrayRef axes = CTFontCopyVariationAxes(base);
    const bool variable = OpenTypeWidthAxis(axes) != nullptr;
    ReleaseCF(axes);
    if (variable) return CopyFontWithWidth(base, size, width);
    CFStringRef family = CTFontCopyFamilyName(base);
    if (!family) return nullptr;
    const void* key = kCTFontFamilyNameAttribute; const void* value = family;
    CFDictionaryRef attributes = CFDictionaryCreate(kCFAllocatorDefault, &key, &value, 1,
        &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    CTFontDescriptorRef descriptor = CTFontDescriptorCreateWithAttributes(attributes);
    CFSetRef required = CFSetCreate(kCFAllocatorDefault, &key, 1, &kCFTypeSetCallBacks);
    CFArrayRef matches = CTFontDescriptorCreateMatchingFontDescriptors(descriptor, required);
    FontMatchScore best = MatchDistance(base, width, weight, style); CTFontRef selected = nullptr;
    for (CFIndex i = 0; matches && i < CFArrayGetCount(matches); ++i) {
        auto candidate = CTFontCreateWithFontDescriptor(static_cast<CTFontDescriptorRef>(CFArrayGetValueAtIndex(matches, i)), size, nullptr);
        if (!candidate) continue;
        CFStringRef candidateFamily = CTFontCopyFamilyName(candidate);
        const bool same = candidateFamily && CFStringCompare(family, candidateFamily, kCFCompareCaseInsensitive) == kCFCompareEqualTo;
        ReleaseCF(candidateFamily);
        if (!same) { CFRelease(candidate); continue; }
        auto score = MatchDistance(candidate, width, weight, style);
        if (score < best) { ReleaseCF(selected); selected = candidate; best = score; }
        else CFRelease(candidate);
    }
    if (selected) {
        CTFontRef varied = CopyFontWithWidth(selected, size, width);
        if (varied) { CFRelease(selected); selected = varied; }
    }
    ReleaseCF(matches); ReleaseCF(required); ReleaseCF(descriptor); ReleaseCF(attributes); ReleaseCF(family);
    return selected;
}

CTFontRef CopyPrivateFontVariations(CTFontRef font, float size, int32_t weight, int32_t style)
{
    CFArrayRef axes = CTFontCopyVariationAxes(font);
    CFDictionaryRef current = CTFontCopyVariation(font);
    CFMutableDictionaryRef variation = current ? CFDictionaryCreateMutableCopy(kCFAllocatorDefault, 0, current) :
        CFDictionaryCreateMutable(kCFAllocatorDefault, 0, &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    const auto setAxis = [&](int32_t tag, CGFloat value) {
        auto axis = FindFontAxis(axes, tag);
        if (!axis) return;
        value = std::clamp(value, AxisValue(axis, kCTFontVariationAxisMinimumValueKey),
            AxisValue(axis, kCTFontVariationAxisMaximumValueKey));
        CFNumberRef identifier = CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt32Type, &tag);
        CFNumberRef number = CFNumberCreate(kCFAllocatorDefault, kCFNumberCGFloatType, &value);
        CFDictionarySetValue(variation, identifier, number);
        ReleaseCF(identifier); ReleaseCF(number);
    };
    if (HasOpenTypeWeightAxis(axes)) setAxis(0x77676874, std::clamp(weight, 1, 1000));
    const bool italic = style == 1 || style == 2;
    const bool hasItalic = FindFontAxis(axes, 0x6974616c) != nullptr; // ital
    const bool hasSlant = FindFontAxis(axes, 0x736c6e74) != nullptr; // slnt
    setAxis(0x6974616c, italic && (style == 1 || !hasSlant) ? 1 : 0);
    setAxis(0x736c6e74, italic && (style == 2 || !hasItalic) ? -12 : 0);
    const void* keys[] = {kCTFontVariationAttribute}; const void* values[] = {variation};
    CFDictionaryRef attributes = CFDictionaryCreate(kCFAllocatorDefault, keys, values, 1,
        &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    CTFontDescriptorRef descriptor = CTFontDescriptorCreateWithAttributes(attributes);
    CTFontRef result = CTFontCreateCopyWithAttributes(font, size, nullptr, descriptor);
    ReleaseCF(descriptor); ReleaseCF(attributes); ReleaseCF(variation); ReleaseCF(current); ReleaseCF(axes);
    return result;
}

CTFontRef CreatePrivateFont(CFDataRef data, float size, int32_t weight, int32_t style, float width, bool matchWidth)
{
    CGDataProviderRef provider = data ? CGDataProviderCreateWithCFData(data) : nullptr;
    CGFontRef graphics = provider ? CGFontCreateWithDataProvider(provider) : nullptr;
    CTFontRef base = graphics ? CTFontCreateWithGraphicsFont(graphics, size, nullptr, nullptr) : nullptr;
    ReleaseCF(graphics); ReleaseCF(provider);
    if (!base) return nullptr;
    CFArrayRef axes = CTFontCopyVariationAxes(base);
    const bool directWeight = HasOpenTypeWeightAxis(axes);
    const bool legacyVariable = FindFontAxis(axes, 0x77676874) != nullptr && !directWeight;
    ReleaseCF(axes);
    const UInt8* bytes = CFDataGetBytePtr(data);
    const bool collection = CFDataGetLength(data) >= 4 && std::memcmp(bytes, "ttcf", 4) == 0;
    if (collection || legacyVariable) {
        // Descriptors made from data are deliberately not registered with the
        // system font manager. Select within these bytes, never by family name.
        CFArrayRef descriptors = CTFontManagerCreateFontDescriptorsFromData(data);
        CFStringRef family = CTFontCopyFamilyName(base);
        FontMatchScore best{3, 0, 0, 0, 0};
        CTFontRef selected = nullptr;
        for (CFIndex i = 0; descriptors && i < CFArrayGetCount(descriptors); ++i) {
            auto descriptor = static_cast<CTFontDescriptorRef>(CFArrayGetValueAtIndex(descriptors, i));
            CTFontRef candidate = CTFontCreateWithFontDescriptor(descriptor, size, nullptr);
            if (!candidate) continue;
            CFStringRef candidateFamily = CTFontCopyFamilyName(candidate);
            const bool sameFamily = family && candidateFamily &&
                CFStringCompare(family, candidateFamily, kCFCompareCaseInsensitive) == kCFCompareEqualTo;
            ReleaseCF(candidateFamily);
            if (!sameFamily) { CFRelease(candidate); continue; }
            CFArrayRef candidateAxes = CTFontCopyVariationAxes(candidate);
            const bool flexibleStyle = FindFontAxis(candidateAxes, 0x6974616c) || FindFontAxis(candidateAxes, 0x736c6e74);
            const bool flexibleWeight = HasOpenTypeWeightAxis(candidateAxes);
            ReleaseCF(candidateAxes);
            const bool wantsItalic = style == 1 || style == 2;
            const bool isItalic = (CTFontGetSymbolicTraits(candidate) & kCTFontItalicTrait) != 0;
            const CGFloat legacyScore = (!flexibleStyle && isItalic != wantsItalic ? 10 : 0) +
                std::abs(FontTrait(candidate, kCTFontWidthTrait) - FontTrait(base, kCTFontWidthTrait)) * 4 +
                (flexibleWeight ? 0 : std::abs(FontTrait(candidate, kCTFontWeightTrait) - CoreTextWeight(weight)));
            const auto score = matchWidth ? MatchDistance(candidate, width, weight, style) : FontMatchScore{0, 0, 0, 0, legacyScore};
            if (score < best) { ReleaseCF(selected); selected = candidate; best = score; }
            else CFRelease(candidate);
        }
        ReleaseCF(family); ReleaseCF(descriptors);
        if (selected) { CFRelease(base); base = selected; }
    }
    CTFontRef varied = CopyPrivateFontVariations(base, size, weight, style);
    if (varied) { CFRelease(base); base = varied; }
    if (matchWidth) {
        CTFontRef widened = CopyFontWithWidth(base, size, width);
        if (widened) { CFRelease(base); base = widened; }
        CTFontRef styled = CopyWidthFontWithStyle(base, size, style);
        if (styled) { CFRelease(base); base = styled; }
    }
    return base;
}

CTFontRef CopyFontWithWeight(CTFontRef base, float size, int32_t weight, bool systemFont)
{
    if (!systemFont && weight == 400) return nullptr; // Preserve explicitly named faces.
    CGFloat value = CoreTextWeight(weight);
#if TARGET_OS_OSX
    if (systemFont) {
        // AppKit chooses its own named/variable instance even for intermediate
        // weights. Setting a CTFont trait directly can select a different face.
        NSFont* selected = [NSFont systemFontOfSize:size weight:value];
        return selected ? static_cast<CTFontRef>(CFRetain((__bridge CTFontRef)selected)) : nullptr;
    }
#endif
    CFNumberRef number = CFNumberCreate(kCFAllocatorDefault, kCFNumberCGFloatType, &value);
    const void* traitKeys[] = {kCTFontWeightTrait}; const void* traitValues[] = {number};
    CFDictionaryRef traits = CFDictionaryCreate(kCFAllocatorDefault, traitKeys, traitValues, 1,
        &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    CFStringRef family = systemFont ? nullptr : CTFontCopyFamilyName(base);
    const void* keys[] = {kCTFontTraitsAttribute, kCTFontFamilyNameAttribute}; const void* values[] = {traits, family};
    CFDictionaryRef attributes = CFDictionaryCreate(kCFAllocatorDefault, keys, values, systemFont ? 1 : 2,
        &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    CTFontDescriptorRef descriptor = CTFontDescriptorCreateWithAttributes(attributes);
    // A UI font carries Apple's weight/optical-size mapping. A named face must
    // instead match its family: copying attributes keeps its old face locked.
    CTFontRef result = systemFont ? CTFontCreateCopyWithAttributes(base, size, nullptr, descriptor) :
        CTFontCreateWithFontDescriptor(descriptor, size, nullptr);
    if (result && family) {
        CFStringRef resolved = CTFontCopyFamilyName(result);
        if (!resolved || CFStringCompare(family, resolved, kCFCompareCaseInsensitive) != kCFCompareEqualTo) {
            CFRelease(result); result = nullptr;
        }
        ReleaseCF(resolved);
    }
    ReleaseCF(descriptor); ReleaseCF(attributes); ReleaseCF(family); ReleaseCF(traits); ReleaseCF(number);
    return result;
}


}
CTFontRef CreateAppleTextFont(CFStringRef familyName, float size, int32_t weight, int32_t style,
    float width, bool matchWidth, FontResourceReference* resource)
{
    if (familyName && CFStringGetLength(familyName) > kMaxManagedFontFamilyCodeUnits) return nullptr;
    const auto length = familyName ? CFStringGetLength(familyName) : 0;
    std::vector<uint16_t> name(length + 1, 0);
    if (length) CFStringGetCharacters(familyName, CFRangeMake(0, length), name.data());
    const auto family = ManagedToWString(name.data(), length);
    if (!familyName || CFStringGetLength(familyName) == 0) {
        familyName = CFSTR(".AppleSystemUIFont");
    }
    auto registered = AcquireFontResource(family.c_str());
    if (resource) *resource = registered;
    CTFontRef base = nullptr;
    bool systemFont = false, familyRequest = false;
    if (registered) {
        auto bytes = AcquireRegisteredFontData(family.c_str());
        CFDataRef data = bytes ? CFDataCreate(kCFAllocatorDefault, bytes->data(), bytes->size()) : nullptr;
        if (data) base = CreatePrivateFont(data, size, weight, style, width, matchWidth);
        ReleaseCF(data);
    } else {
        const auto equals = [&](CFStringRef value) { return CFStringCompare(familyName, value, kCFCompareCaseInsensitive) == kCFCompareEqualTo; };
        if (equals(CFSTR("SF Pro")) || equals(CFSTR(".AppleSystemUIFont")) || equals(CFSTR("system-ui")) ||
            equals(CFSTR("sans-serif")) || equals(CFSTR("ui-sans-serif"))) {
            base = CTFontCreateUIFontForLanguage(kCTFontUIFontSystem, size, nullptr);
            systemFont = true;
        } else if (equals(CFSTR("monospace")) || equals(CFSTR("ui-monospace"))) {
            base = CTFontCreateWithName(CFSTR("Menlo"), size, nullptr); familyRequest = true;
        } else if (equals(CFSTR("serif")) || equals(CFSTR("ui-serif"))) {
            base = CTFontCreateWithName(CFSTR("Times New Roman"), size, nullptr); familyRequest = true;
        }
        else base = CTFontCreateWithName(familyName, size, nullptr);
        CFStringRef resolvedFamily = base ? CTFontCopyFamilyName(base) : nullptr;
        familyRequest |= resolvedFamily && CFStringCompare(familyName, resolvedFamily, kCFCompareCaseInsensitive) == kCFCompareEqualTo;
        ReleaseCF(resolvedFamily);
    }
    if (!base) return nullptr;

    if (!registered) {
        CTFontRef weighted = CopyFontWithWeight(base, size, weight, systemFont);
        if (weighted) { CFRelease(base); base = weighted; }
        if (style == 1 || style == 2) {
#if TARGET_OS_OSX
            NSFont* selected = [NSFontManager.sharedFontManager convertFont:(__bridge NSFont*)base toHaveTrait:NSItalicFontMask];
            CTFontRef italic = selected ? static_cast<CTFontRef>(CFRetain((__bridge CTFontRef)selected)) : nullptr;
#else
            CTFontRef italic = CTFontCreateCopyWithSymbolicTraits(base, size, nullptr, kCTFontItalicTrait, kCTFontItalicTrait);
#endif
            if (italic) { CFRelease(base); base = italic; }
        }
    }
    if (!registered && matchWidth && (familyRequest || systemFont || width != 100)) {
        CTFontRef selected = SelectInstalledFontWidth(base, size, weight, style, width, systemFont);
        if (selected) { CFRelease(base); base = selected; }
        if (!systemFont) {
            CTFontRef varied = CopyPrivateFontVariations(base, size, weight, style);
            if (varied) { CFRelease(base); base = varied; }
        }
        CTFontRef styled = CopyWidthFontWithStyle(base, size, style);
        if (styled) { CFRelease(base); base = styled; }
    }
    return base;
}
}
