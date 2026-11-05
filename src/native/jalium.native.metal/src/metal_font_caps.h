#pragma once
#ifdef __APPLE__
#include "jalium_font_features.h"
#include "jalium_unicode_casing.h"
#include "apple_vertical_orientation.h"
#include "apple_vertical_metrics.h"
#include "metal_font_line_metrics.h"
#include <CoreText/CoreText.h>
#include <algorithm>
#include <array>
#include <map>
#include <memory>
#include <tuple>
#include <vector>

namespace jalium {

// CoreText shapes paint UTF-16; every public geometry API addresses the source.
// One source grapheme may own several paint graphemes (e.g. sharp-s -> SS).
struct AppleCapsMap {
    struct Part { CFRange source, paint; };
    CFStringRef source = nullptr;
    CFAttributedStringRef original = nullptr;
    std::vector<Part> parts;
    std::vector<Part> metricParts;
    std::vector<uint8_t> operations;
    bool rebuildLines = false;
    ~AppleCapsMap() { if (source) CFRelease(source); if (original) CFRelease(original); }
    AppleCapsMap() = default;
    AppleCapsMap(const AppleCapsMap&) = delete;
    AppleCapsMap& operator=(const AppleCapsMap&) = delete;
    bool Active() const { return source != nullptr; }
    CFIndex SourceLength(CFIndex ordinary) const { return source ? CFStringGetLength(source) : ordinary; }
    const Part* PaintPart(CFIndex position) const {
        auto it = std::upper_bound(parts.begin(), parts.end(), position,
            [](CFIndex p, const Part& v) { return p < v.paint.location; });
        if (it == parts.begin()) return nullptr;
        --it;
        return position < it->paint.location + it->paint.length ? &*it : nullptr;
    }
    CFIndex ToPaint(CFIndex position, bool end = false) const {
        if (!source || parts.empty()) return position;
        if (position >= CFStringGetLength(source)) return parts.back().paint.location + parts.back().paint.length;
        auto it = std::upper_bound(parts.begin(), parts.end(), position,
            [](CFIndex p, const Part& v) { return p < v.source.location; });
        if (it == parts.begin()) return 0;
        --it;
        return it->paint.location + (end && position > it->source.location ? it->paint.length : 0);
    }
    CFIndex ToSource(CFIndex position, bool end = false) const {
        if (!source) return position;
        if (const auto* p = PaintPart(position))
            return p->source.location + (end && position > p->paint.location ? p->source.length : 0);
        return CFStringGetLength(source);
    }
    bool Boundary(CFIndex position) const {
        const auto* p = PaintPart(position);
        return !p || p->paint.location == position;
    }
    CFRange Cluster(CFStringRef paint, CFIndex position) const {
        if (const auto* p = PaintPart(position)) return p->paint;
        return CFStringGetRangeOfComposedCharactersAtIndex(paint, position);
    }
    CFRange PaintRange(CFRange range) const {
        CFIndex first = ToPaint(range.location), last = ToPaint(range.location + range.length, true);
        return CFRangeMake(first, std::max<CFIndex>(0, last - first));
    }
    // Match the shared layout's ownership: the source containing a
    // grapheme's final code unit paints that whole glyph cluster once.
    // Selection and user glyph masks can still expand partial ranges.
    CFRange PaintOwnedRange(CFRange range) const {
        CFIndex start = ToPaint(range.location), end = ToPaint(range.location + range.length);
        return CFRangeMake(start, std::max<CFIndex>(0, end - start));
    }
    CFRange SourceRange(CFRange range) const {
        CFIndex first = ToSource(range.location), last = ToSource(range.location + range.length, true);
        return CFRangeMake(first, std::max<CFIndex>(0, last - first));
    }
    CFRange MetricRange(CFRange range) const {
        if (metricParts.empty()) return range;
        auto boundary = [&](CFIndex at, bool end) {
            if (at >= SourceLength(0)) return metricParts.back().paint.location + metricParts.back().paint.length;
            auto it = std::upper_bound(metricParts.begin(), metricParts.end(), at,
                [](CFIndex p, const Part& v) { return p < v.source.location; });
            if (it == metricParts.begin()) return CFIndex(0);
            --it;
            return it->paint.location + (end && at > it->source.location ? it->paint.length : 0);
        };
        auto first = boundary(range.location, false), last = boundary(range.location + range.length, true);
        return CFRangeMake(first, std::max<CFIndex>(0, last - first));
    }
    std::vector<uint32_t> PaintRanges(const uint32_t* ranges, uint32_t count) const {
        std::vector<uint32_t> result; result.reserve(static_cast<size_t>(count) * 2);
        for (uint32_t i = 0; i < count; ++i) {
            auto range = PaintRange(CFRangeMake(ranges[i * 2], ranges[i * 2 + 1]));
            if (range.length) { result.push_back(static_cast<uint32_t>(range.location)); result.push_back(static_cast<uint32_t>(range.length)); }
        }
        return result;
    }
};

inline void SetAppleCapsAttributes(CFMutableAttributedStringRef text, CFRange span,
    uint32_t variant, bool synthesize, CFStringRef language, int64_t smallOverride = -1, uint32_t boundaries = 0,
    uint32_t continuation = 0)
{
    if (!variant || !span.length) return;
    int32_t mode = static_cast<int32_t>(variant);
    auto number = CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt32Type, &mode);
    if (!number) throw std::bad_alloc();
    CFAttributedStringSetAttribute(text, span, CFSTR("JaliumCapsVariant"), number);
    CFRelease(number);
    CFAttributedStringSetAttribute(text, span, CFSTR("JaliumCapsSynthesis"), synthesize ? kCFBooleanTrue : kCFBooleanFalse);
    if (smallOverride >= 0) {
        auto value = CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt64Type, &smallOverride);
        if (!value) throw std::bad_alloc();
        CFAttributedStringSetAttribute(text, span, CFSTR("JaliumCapsSmcpOverride"), value); CFRelease(value);
    }
    if (language) CFAttributedStringSetAttribute(text, span, CFSTR("JaliumCapsLanguage"), language);
    if (boundaries) {
        auto value = CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt32Type, &boundaries);
        if (!value) throw std::bad_alloc();
        CFAttributedStringSetAttribute(text, span, CFSTR("JaliumCapsBoundaries"), value); CFRelease(value);
    }
    if (continuation) {
        auto value = CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt32Type, &continuation);
        if (!value) throw std::bad_alloc();
        CFAttributedStringSetAttribute(text, span, CFSTR("JaliumCapsContinuation"), value); CFRelease(value);
    }
}

// Returns a retained transformed attributed string, or retains the input when
// no character needs synthesis. Native-feature availability is cached per face,
// never inferred from a particular character's substitution result.
inline CFAttributedStringRef CreateAppleCapsText(CFAttributedStringRef input, AppleCapsMap& mapping)
{
    CFIndex length = CFAttributedStringGetLength(input);
    bool requested = false;
    for (CFIndex at = 0; at < length;) {
        CFRange range{};
        if (CFAttributedStringGetAttribute(input, at, CFSTR("JaliumCapsVariant"), &range)) { requested = true; break; }
        if (range.length <= 0) break;
        at = range.location + range.length;
    }
    if (!requested) return static_cast<CFAttributedStringRef>(CFRetain(input));
    mapping.operations.assign(static_cast<size_t>(length), 0);
    auto release = [](const void* p) { if (p) CFRelease(p); };
    using Owner = std::unique_ptr<const void, decltype(release)>;
    Owner line(CTLineCreateWithAttributedString(input), release);
    if (!line) return nullptr;
    struct FaceRun { CFRange range; CTFontRef font; };
    std::vector<FaceRun> resolved;
    auto runs = CTLineGetGlyphRuns(static_cast<CTLineRef>(line.get()));
    for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
        auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i));
        auto font = static_cast<CTFontRef>(CFDictionaryGetValue(CTRunGetAttributes(run), kCTFontAttributeName));
        if (font) resolved.push_back({CTRunGetStringRange(run), font});
    }
    std::sort(resolved.begin(), resolved.end(), [](const auto& a, const auto& b) { return a.range.location < b.range.location; });
    struct Features { bool small, upper, petite, petiteUpper, unicase; };
    std::map<CTFontRef, Features> support;
    std::map<std::tuple<CTFontRef, bool, int64_t>, std::shared_ptr<const void>> derived;
    auto features = [&](CTFontRef font) {
        auto found = support.find(font);
        if (found != support.end()) return found->second;
        std::vector<uint32_t> available;
        Owner table(CTFontCopyTable(font, kCTFontTableGSUB, kCTFontTableOptionNoOptions), release);
        if (table) font_features::Collect(CFDataGetBytePtr(static_cast<CFDataRef>(table.get())),
            static_cast<size_t>(CFDataGetLength(static_cast<CFDataRef>(table.get()))), available);
        const uint32_t tags[] = {0x736d6370, 0x63327363, 0x70636170, 0x63327063, 0x756e6963};
        uint8_t values[5]{}; font_features::Query(available, tags, 5, values);
        Features value{values[0] != 0, values[1] != 0, values[2] != 0, values[3] != 0, values[4] != 0};
        support.emplace(font, value); return value;
    };
    Owner outputOwner(CFAttributedStringCreateMutable(kCFAllocatorDefault, 0), release);
    auto output = const_cast<CFMutableAttributedStringRef>(static_cast<CFAttributedStringRef>(outputOwner.get()));
    if (!output) return nullptr;
    auto source = CFAttributedStringGetString(input);
    std::u16string sourceUnits;
    std::map<uint32_t, std::unique_ptr<unicode_casing::Context>> contexts;
    std::vector<AppleCapsMap::Part> parts, scalarParts;
    bool changed = false;
    CFIndex pending = 0, paintOffset = 0;
    auto appendOriginal = [&](CFIndex first, CFIndex count) {
        if (!count) return true;
        Owner piece(CFAttributedStringCreateWithSubstring(kCFAllocatorDefault, input, CFRangeMake(first, count)), release);
        if (!piece) return false;
        CFAttributedStringReplaceAttributedString(output, CFRangeMake(CFAttributedStringGetLength(output), 0), static_cast<CFAttributedStringRef>(piece.get()));
        return true;
    };
    size_t runIndex = 0;
    for (CFIndex position = 0; position < length;) {
        auto cluster = CFStringGetRangeOfComposedCharactersAtIndex(source, position);
        if (cluster.location < position || cluster.length <= 0) return nullptr;
        CFIndex clusterStart = cluster.location + paintOffset;
        bool inheritedShrink = false, inheritedNative = false, inheritedSynthesis = true;
        int32_t inheritedVariant = 0; int64_t inheritedOverride = -1;
        for (CFIndex position = cluster.location; position < cluster.location + cluster.length;) {
            UTF32Char scalar = CFStringGetCharacterAtIndex(source, position);
            CFIndex scalarLength = 1;
            if (CFStringIsSurrogateHighCharacter(scalar) && position + 1 < length) {
                UniChar low = CFStringGetCharacterAtIndex(source, position + 1);
                if (CFStringIsSurrogateLowCharacter(low)) { scalar = CFStringGetLongCharacterForSurrogatePair(scalar, low); scalarLength = 2; }
            }
            auto scalarRange = CFRangeMake(position, scalarLength);
            auto attributes = CFAttributedStringGetAttributes(input, position, nullptr);
            int32_t variant = 0;
            auto number = static_cast<CFNumberRef>(CFDictionaryGetValue(attributes, CFSTR("JaliumCapsVariant")));
            if (number) CFNumberGetValue(number, kCFNumberSInt32Type, &variant);
            bool synthesize = CFDictionaryGetValue(attributes, CFSTR("JaliumCapsSynthesis")) != kCFBooleanFalse;
            auto language = static_cast<CFStringRef>(CFDictionaryGetValue(attributes, CFSTR("JaliumCapsLanguage")));
            int64_t smallOverride = -1;
            auto overrideValue = static_cast<CFNumberRef>(CFDictionaryGetValue(attributes, CFSTR("JaliumCapsSmcpOverride")));
            if (overrideValue) CFNumberGetValue(overrideValue, kCFNumberSInt64Type, &smallOverride);
            int32_t boundaries = 0;
            auto boundaryValue = static_cast<CFNumberRef>(CFDictionaryGetValue(attributes, CFSTR("JaliumCapsBoundaries")));
            if (boundaryValue) CFNumberGetValue(boundaryValue, kCFNumberSInt32Type, &boundaries);
            if (boundaries < 0 || boundaries > 63) return nullptr;
            while (runIndex + 1 < resolved.size() && resolved[runIndex].range.location + resolved[runIndex].range.length <= position) ++runIndex;
            CTFontRef font = !resolved.empty() && position >= resolved[runIndex].range.location &&
                position < resolved[runIndex].range.location + resolved[runIndex].range.length
                ? resolved[runIndex].font : static_cast<CTFontRef>(CFDictionaryGetValue(attributes, kCTFontAttributeName));
            bool lower = unicode_casing::IsLowercase(scalar), upper = unicode_casing::IsUppercase(scalar);
            int32_t authoredVariant = variant;
            bool shrink = false, lowerForNative = false;
            if (font && variant && (lower || upper)) {
                auto available = features(font);
                if (variant == 3 && !available.petite) variant = 1;
                if (variant == 4 && (!available.petite || !available.petiteUpper)) variant = 2;
                if (variant == 1 || variant == 2)
                    shrink = synthesize && ((lower && !available.small) || (upper && variant == 2 && !available.upper));
                if (variant == 5 && !available.unicase && upper) {
                    lowerForNative = available.small;
                    shrink = !available.small && synthesize;
                }
            }
            if (!lower && !upper && font && variant == inheritedVariant && synthesize == inheritedSynthesis &&
                smallOverride == inheritedOverride && (unicode_casing::Contains(unicode_casing::data::Above, scalar) ||
                    unicode_casing::Contains(unicode_casing::data::OtherCombining, scalar))) {
                // Caseless combining marks keep their own font/style attributes but
                // follow their cased base's synthesized size and casing operation.
                shrink = inheritedShrink; lowerForNative = inheritedNative;
            }
            if (position == 0 && !lower && !upper && font && variant &&
                (unicode_casing::Contains(unicode_casing::data::Above, scalar) ||
                 unicode_casing::Contains(unicode_casing::data::OtherCombining, scalar))) {
                int32_t operation = 0;
                auto value = static_cast<CFNumberRef>(CFDictionaryGetValue(attributes, CFSTR("JaliumCapsContinuation")));
                if (value) CFNumberGetValue(value, kCFNumberSInt32Type, &operation);
                if (operation < 0 || operation > 2) return nullptr;
                shrink = operation == 1; lowerForNative = operation == 2;
            }
            for (CFIndex unit = 0; unit < scalarLength; ++unit)
                mapping.operations[static_cast<size_t>(position + unit)] = shrink ? 1 : lowerForNative ? 2 : 0;
            inheritedShrink = shrink; inheritedNative = lowerForNative;
            inheritedVariant = authoredVariant; inheritedSynthesis = synthesize; inheritedOverride = smallOverride;
            if (!shrink && !lowerForNative) {
                scalarParts.push_back({scalarRange, CFRangeMake(position + paintOffset, scalarLength)});
                position += scalarLength;
                continue;
            }
            if (!appendOriginal(pending, position - pending)) return nullptr;
            Owner original(CFAttributedStringCreateWithSubstring(kCFAllocatorDefault, input, scalarRange), release);
            if (!original) return nullptr;
            Owner pieceOwner(CFAttributedStringCreateMutableCopy(kCFAllocatorDefault, 0, static_cast<CFAttributedStringRef>(original.get())), release);
            auto piece = const_cast<CFMutableAttributedStringRef>(static_cast<CFAttributedStringRef>(pieceOwner.get()));
            if (!piece) return nullptr;
            if (shrink || lowerForNative) {
                if (sourceUnits.empty()) {
                    sourceUnits.resize(static_cast<size_t>(length));
                    CFStringGetCharacters(source, CFRangeMake(0, length), reinterpret_cast<UniChar*>(sourceUnits.data()));
                }
                auto& casing = contexts[static_cast<uint32_t>(boundaries)];
                if (!casing) casing = std::make_unique<unicode_casing::Context>(sourceUnits, boundaries);
                char tag[129]{};
                if (language && !CFStringGetCString(language, tag, sizeof(tag), kCFStringEncodingASCII)) return nullptr;
                auto letters = casing->Transform(position, scalarLength,
                    lowerForNative || variant == 5 ? unicode_casing::Case::Lower : unicode_casing::Case::Upper, tag);
                if (variant == 5 && !lowerForNative) {
                    unicode_casing::Context lowered(letters);
                    letters = lowered.Transform(0, letters.size(), unicode_casing::Case::Upper, tag);
                }
                Owner painted(CFStringCreateWithCharacters(kCFAllocatorDefault,
                    reinterpret_cast<const UniChar*>(letters.data()), letters.size()), release);
                if (!painted) return nullptr;
                CFAttributedStringReplaceString(piece, CFRangeMake(0, scalarLength), static_cast<CFStringRef>(painted.get()));
                const auto key = std::make_tuple(font, shrink, smallOverride);
                auto cached = derived.find(key);
                if (cached == derived.end()) {
                    Owner originalDescriptor(CTFontCopyFontDescriptor(font), release);
                    if (!originalDescriptor) return nullptr;
                    Owner descriptor(nullptr, release);
                    if (lowerForNative) {
                        // Unicase fallback enables smcp only on lowercased source
                        // capitals. Keep descriptor settings below this selection
                        // and explicit element smcp settings above it.
                        Owner previous(CTFontCopyAttribute(font, kCTFontFeatureSettingsAttribute), release);
                        Owner listOwner(CFArrayCreateMutable(kCFAllocatorDefault, 0, &kCFTypeArrayCallBacks), release);
                        auto list = const_cast<CFMutableArrayRef>(static_cast<CFArrayRef>(listOwner.get()));
                        if (!list) return nullptr;
                        if (previous && CFGetTypeID(previous.get()) == CFArrayGetTypeID()) {
                            auto settings = static_cast<CFArrayRef>(previous.get());
                            for (CFIndex i = 0; i < CFArrayGetCount(settings); ++i) {
                                auto setting = static_cast<CFDictionaryRef>(CFArrayGetValueAtIndex(settings, i));
                                auto tag = CFDictionaryGetValue(setting, kCTFontOpenTypeFeatureTag);
                                if (!tag || !CFEqual(tag, CFSTR("smcp"))) CFArrayAppendValue(list, setting);
                            }
                        }
                        int64_t enabled = smallOverride < 0 ? 1 : smallOverride;
                        Owner value(CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt64Type, &enabled), release);
                        const void* keys[] = {kCTFontOpenTypeFeatureTag, kCTFontOpenTypeFeatureValue};
                        const void* values[] = {CFSTR("smcp"), value.get()};
                        Owner setting(value ? CFDictionaryCreate(kCFAllocatorDefault, keys, values, 2,
                            &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks) : nullptr, release);
                        if (!setting) return nullptr;
                        CFArrayAppendValue(list, setting.get());
                        const void* attribute = kCTFontFeatureSettingsAttribute; const void* settingList = list;
                        Owner changes(CFDictionaryCreate(kCFAllocatorDefault, &attribute, &settingList, 1,
                            &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks), release);
                        if (!changes) return nullptr;
                        descriptor.reset(CTFontDescriptorCreateCopyWithAttributes(static_cast<CTFontDescriptorRef>(originalDescriptor.get()), static_cast<CFDictionaryRef>(changes.get())));
                        if (!descriptor) return nullptr;
                    }
                    Owner graphics(CTFontCopyGraphicsFont(font, nullptr), release);
                    auto matrix = CTFontGetMatrix(font);
                    // Match the public format API's float point size, including
                    // an explicit CSS/font-size reference at 80% of this em.
                    CGFloat size = shrink ? static_cast<float>(CTFontGetSize(font) * .8) : CTFontGetSize(font);
                    CTFontRef selected = graphics ? CTFontCreateWithGraphicsFont(static_cast<CGFontRef>(const_cast<void*>(graphics.get())),
                        size, &matrix, static_cast<CTFontDescriptorRef>(descriptor ? descriptor.get() : originalDescriptor.get())) : nullptr;
                    if (!selected) return nullptr;
                    cached = derived.emplace(key, std::shared_ptr<const void>(selected, release)).first;
                }
                auto whole = CFRangeMake(0, CFAttributedStringGetLength(piece));
                if (whole.length) {
                    CFAttributedStringSetAttribute(piece, whole, kCTFontAttributeName, cached->second.get());
                    CFAttributedStringSetAttribute(piece, whole, CFSTR("JaliumCapsBaseFont"), font);
                }
                changed = true;
            }
            CFIndex start = CFAttributedStringGetLength(output), size = CFAttributedStringGetLength(piece);
            CFAttributedStringReplaceAttributedString(output, CFRangeMake(start, 0), piece);
            scalarParts.push_back({scalarRange, CFRangeMake(start, size)});
            paintOffset += size - scalarLength;
            position += scalarLength;
            pending = position;
        }
        parts.push_back({cluster, CFRangeMake(clusterStart, cluster.location + cluster.length + paintOffset - clusterStart)});
        position = cluster.location + cluster.length;
    }
    if (!changed) return static_cast<CFAttributedStringRef>(CFRetain(input));
    if (!appendOriginal(pending, length - pending)) return nullptr;
    mapping.source = static_cast<CFStringRef>(CFRetain(source));
    mapping.rebuildLines = true;
    if (!mapping.original) mapping.original = static_cast<CFAttributedStringRef>(CFRetain(input));
    mapping.parts = std::move(parts);
    // The metric annotation must not add a shaping seam just at a changed
    // character. Give unchanged neighbors the same source-face annotation.
    AppleCapsMap metricMap;
    metricMap.source = static_cast<CFStringRef>(CFRetain(source));
    metricMap.parts = std::move(scalarParts);
    for (const auto& run : resolved) {
        auto range = metricMap.PaintRange(run.range);
        if (range.length) CFAttributedStringSetAttribute(output, range, CFSTR("JaliumCapsBaseFont"), run.font);
    }
    // Concatenating equal dictionaries can leave a shaping seam in CoreText.
    // Collapse those seams while retaining distinct paint/opacity attributes.
    for (CFIndex at = 0, size = CFAttributedStringGetLength(output); at < size;) {
        CFRange range{}; auto attributes = CFAttributedStringGetAttributes(output, at, &range);
        Owner retained(CFRetain(attributes), release);
        CFIndex end = range.location + range.length;
        while (end < size) {
            CFRange next{}; auto other = CFAttributedStringGetAttributes(output, end, &next);
            if (!other || next.length <= 0 || !CFEqual(retained.get(), other)) break;
            end = next.location + next.length;
        }
        CFAttributedStringSetAttributes(output, CFRangeMake(at, end - at), static_cast<CFDictionaryRef>(retained.get()), true);
        at = end;
    }
    return static_cast<CFAttributedStringRef>(outputOwner.release());
}

// Keep an expansion inside its original grapheme when CoreText suggests a
// character/emergency wrap between its replacement letters. The ordinary frame
// stays untouched for text which does not need synthesis.
class AppleCapsLines {
    CTFrameRef frame_ = nullptr; // borrowed
    CFArrayRef lines_ = nullptr; // owned only for synthesized text
    std::vector<CGPoint> origins_;
    CFRange visible_{};
    CTFontRef base_ = nullptr;
    AppleFontLineMetrics strut_{};
    bool metricOverrides_ = false;
    bool verticalMetrics_ = false;
    std::map<CTLineRef, std::array<CGFloat, 3>> sourceMetrics_;
public:
    AppleCapsLines(CTFrameRef frame, CFAttributedStringRef text, const AppleCapsMap& map,
        CGFloat width, CGFloat height, int32_t wrapping, int32_t alignment,
        CTFontRef base, CGFloat minHeight = 0, bool preserveSourcePlacement = false, AppleFontLineMetrics strut = {}, bool strongLtr = false, bool verticalMetrics = false, CFAttributedStringRef verticalSource = nullptr) : frame_(frame), base_(map.rebuildLines || map.original ? base : nullptr), strut_(strut)
    {
        metricOverrides_=strut.overridden||HasAppleFontLineMetrics(text);
        verticalMetrics_=verticalMetrics&&verticalSource;
        if (!map.rebuildLines && !map.original && !metricOverrides_ && !verticalMetrics_) return;
        auto release = [](const void* p) { if (p) CFRelease(p); };
        using Owner = std::unique_ptr<const void, decltype(release)>;
        Owner typesetter(CreateAppleTextTypesetter(text, strongLtr), release);
        Owner array(CFArrayCreateMutable(kCFAllocatorDefault, 0, &kCFTypeArrayCallBacks), release);
        if (!typesetter || !array) return;
        auto createLine = [&](CFRange range) -> CTLineRef {
            if (!verticalMetrics_)
                return CTTypesetterCreateLine(static_cast<CTTypesetterRef>(typesetter.get()), range);
            // A legal break can remove a contextual substitution or split an
            // optional ligature. Recompute metrics from the resulting glyphs,
            // retaining the paragraph's original UTF-16 indices and features.
            Owner adjusted(CreateAppleVerticalMetricsText(verticalSource, strongLtr, range), release);
            Owner selected(adjusted ? CreateAppleTextTypesetter(static_cast<CFAttributedStringRef>(adjusted.get()), strongLtr) : nullptr, release);
            return selected ? CTTypesetterCreateLine(static_cast<CTTypesetterRef>(selected.get()), range) : nullptr;
        };
        auto values = const_cast<CFMutableArrayRef>(static_cast<CFArrayRef>(array.get()));
        auto string = CFAttributedStringGetString(text);
        CFIndex length = CFStringGetLength(string), position = 0;
        CGFloat y = 0;
        auto isBreak = [](UniChar c) { return c == '\r' || c == '\n' || c == 0x2028 || c == 0x2029; };
        CFIndex hard = 0, hardEnd = 0;
        while (position < length) {
            if (position >= hardEnd) {
                hard = position;
                while (hard < length && !isBreak(CFStringGetCharacterAtIndex(string, hard))) ++hard;
                hardEnd = hard;
                if (hardEnd < length) {
                    if (CFStringGetCharacterAtIndex(string, hardEnd++) == '\r' && hardEnd < length && CFStringGetCharacterAtIndex(string, hardEnd) == '\n') ++hardEnd;
                }
            }
            CFIndex end = hardEnd;
            if (wrapping != 1) {
                auto suggest = [&](CGFloat available) {
                    CFIndex suggested = wrapping == 2
                        ? CTTypesetterSuggestClusterBreak(static_cast<CTTypesetterRef>(typesetter.get()), position, available)
                        : CTTypesetterSuggestLineBreak(static_cast<CTTypesetterRef>(typesetter.get()), position, available);
                    CFIndex result = std::min(hardEnd, position + std::max<CFIndex>(1, suggested));
                    if (!map.Boundary(result)) {
                        auto group = map.Cluster(string, result);
                        result = group.location > position ? group.location : group.location + group.length;
                    }
                    if (result == hard && hardEnd > hard) result = hardEnd;
                    return result;
                };
                end = suggest(width);
                if (verticalMetrics_) while (end > position) {
                    Owner trial(createLine(CFRangeMake(position, end-position)), release);
                    if (!trial) return;
                    auto candidate = static_cast<CTLineRef>(trial.get());
                    const auto tail = AppleVerticalMetricTail(candidate, text);
                    const auto advance = CTLineGetTypographicBounds(candidate,nullptr,nullptr,nullptr) - CTLineGetTrailingWhitespaceWidth(candidate) + tail;
                    if (advance <= width + .000001 || end == suggest(0)) break;
                    // Ask CoreText for the previous legal break. Searching its
                    // width parameter preserves language-specific break rules.
                    CGFloat low=0, high=width;
                    CFIndex previous=suggest(0);
                    for (unsigned attempt=0;attempt<48 && high-low>.0000001;++attempt) {
                        auto middle=low+(high-low)/2;auto proposed=suggest(middle);
                        if (proposed<end) {previous=proposed;low=middle;} else high=middle;
                    }
                    if (previous>=end) break;
                    end=previous;
                }
            }
            if (end <= position) return;
            Owner line(createLine(CFRangeMake(position, end - position)), release);
            if (!line) return;
            if (alignment == 3 && end < hard) {
                auto available=width-(verticalMetrics_?AppleVerticalMetricTail(static_cast<CTLineRef>(line.get()),text):0);
                CTLineRef justified = CTLineCreateJustifiedLine(static_cast<CTLineRef>(line.get()), 1, available);
                if (justified) line.reset(justified);
            }
            CGFloat ascent = 0, descent = 0, leading = 0;
            Metrics(static_cast<CTLineRef>(line.get()), &ascent, &descent, &leading);
            CGFloat lineHeight = std::max(minHeight, ascent + descent + leading);
            CGFloat baseline = ascent;
            if (preserveSourcePlacement && map.original && !metricOverrides_) {
                auto sourceRange = map.MetricRange(map.SourceRange(CFRangeMake(position, end - position)));
                Owner original(CFAttributedStringCreateWithSubstring(kCFAllocatorDefault, map.original, sourceRange), release);
                Owner sourceLine(original ? CTLineCreateWithAttributedString(static_cast<CFAttributedStringRef>(original.get())) : nullptr, release);
                if (!original || !sourceLine) return;
                double advance = CTLineGetTypographicBounds(static_cast<CTLineRef>(sourceLine.get()), &ascent, &descent, &leading);
                CGFloat sourceHeight = std::max(height, std::ceil(ascent + descent + leading) + 1);
                Owner setter(CTFramesetterCreateWithAttributedString(static_cast<CFAttributedStringRef>(original.get())), release);
                Owner path(CGPathCreateWithRect(CGRectMake(0, 0, std::max(width, static_cast<CGFloat>(advance + 1)), sourceHeight), nullptr), release);
                Owner sourceFrame(setter && path ? CTFramesetterCreateFrame(static_cast<CTFramesetterRef>(setter.get()), CFRangeMake(0, 0), static_cast<CGPathRef>(path.get()), nullptr) : nullptr, release);
                if (!sourceFrame) return;
                auto sourceLines = CTFrameGetLines(static_cast<CTFrameRef>(sourceFrame.get()));
                if (CFArrayGetCount(sourceLines) == 0) return;
                auto first = static_cast<CTLineRef>(CFArrayGetValueAtIndex(sourceLines, 0));
                CTLineGetTypographicBounds(first, &ascent, &descent, &leading);
                CGPoint origin{}; CTFrameGetLineOrigins(static_cast<CTFrameRef>(sourceFrame.get()), CFRangeMake(0, 1), &origin);
                baseline = sourceHeight - origin.y;
                // Direct formats have fixed native line spacing. Caseless
                // fallback fonts may overhang it, as in the original frame.
                lineHeight = minHeight > 0 ? minHeight : ascent + descent + leading;
                sourceMetrics_.emplace(static_cast<CTLineRef>(line.get()), std::array<CGFloat, 3>{ascent, descent, leading});
            }
            if (y + lineHeight > height + .001) break;
            CGFloat flush = alignment == 1 ? 1 : alignment == 2 ? .5 : 0;
            auto available=width-(verticalMetrics_?AppleVerticalMetricTail(static_cast<CTLineRef>(line.get()),text):0);
            CGFloat x = CTLineGetPenOffsetForFlush(static_cast<CTLineRef>(line.get()), flush, available);
            origins_.push_back(CGPointMake(x, height - y - baseline));
            CFArrayAppendValue(values, line.get());
            position = end; y += lineHeight;
        }
        visible_ = CFRangeMake(0, position);
        lines_ = static_cast<CFArrayRef>(array.release());
    }
    ~AppleCapsLines() { if (lines_) CFRelease(lines_); }
    AppleCapsLines(const AppleCapsLines&) = delete;
    AppleCapsLines& operator=(const AppleCapsLines&) = delete;
    CFArrayRef Lines() const { return lines_ ? lines_ : frame_ ? CTFrameGetLines(frame_) : nullptr; }
    bool HasMetricOverrides() const { return metricOverrides_; }
    bool HasVerticalMetrics() const { return verticalMetrics_; }
    CFRange Visible() const { return lines_ ? visible_ : frame_ ? CTFrameGetVisibleStringRange(frame_) : CFRangeMake(0, 0); }
    void Origins(CFRange range, CGPoint* output) const {
        if (!lines_) { if (frame_) CTFrameGetLineOrigins(frame_, range, output); return; }
        CFIndex count = range.length ? range.length : origins_.size() - range.location;
        for (CFIndex i = 0; i < count; ++i) output[i] = origins_[range.location + i];
    }
    void Metrics(CTLineRef line, CGFloat* ascent, CGFloat* descent, CGFloat* leading) const {
        AppleFontLineMetrics metrics;
        if(metricOverrides_&&GetAppleFontLineMetrics(line,metrics,&strut_,true)) {
            *ascent=metrics.ascent;*descent=metrics.descent;*leading=metrics.leading;return;
        }
        if (auto found = sourceMetrics_.find(line); found != sourceMetrics_.end()) {
            *ascent = found->second[0]; *descent = found->second[1]; *leading = found->second[2]; return;
        }
        CTLineGetTypographicBounds(line, ascent, descent, leading);
        if (!base_) return;
        *ascent = std::max(*ascent, CTFontGetAscent(base_));
        *descent = std::max(*descent, CTFontGetDescent(base_));
        *leading = std::max(*leading, CTFontGetLeading(base_));
        auto runs = CTLineGetGlyphRuns(line);
        for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
            auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i));
            auto font = static_cast<CTFontRef>(CFDictionaryGetValue(CTRunGetAttributes(run), CFSTR("JaliumCapsBaseFont")));
            auto positionBase = static_cast<CTFontRef>(CFDictionaryGetValue(CTRunGetAttributes(run), CFSTR("JaliumPositionBaseFont")));
            if (positionBase) font = positionBase;
            if (!font) continue;
            *ascent = std::max(*ascent, CTFontGetAscent(font));
            *descent = std::max(*descent, CTFontGetDescent(font));
            *leading = std::max(*leading, CTFontGetLeading(font));
        }
    }
};

} // namespace jalium
#endif
