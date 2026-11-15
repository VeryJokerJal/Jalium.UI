#include "apple_vertical_metrics.h"
#ifdef __APPLE__
#include "apple_vertical_orientation.h"
#include "metal_font_size_adjust_metrics.h"
#include <algorithm>
#include <cmath>
#include <map>
#include <memory>
#include <vector>

namespace jalium {
namespace {
void Release(const void* value) { if (value) CFRelease(value); }
using Owner = std::unique_ptr<const void, decltype(&Release)>;
constexpr double Epsilon = .000001;
struct Cell { CFRange range{}; double delta = 0; unsigned glyphs = 0; };
bool IsControl(CFStringRef string, CFRange range) {
    const auto controls = CFCharacterSetGetPredefined(kCFCharacterSetControl);
    for (CFIndex at = range.location; at < range.location + range.length; ++at)
        if (!CFCharacterSetIsCharacterMember(controls, CFStringGetCharacterAtIndex(string, at))) return false;
    return true;
}
double Number(CFTypeRef value) {
    double result = 0;
    if (value && CFGetTypeID(value) == CFNumberGetTypeID())
        CFNumberGetValue(static_cast<CFNumberRef>(value), kCFNumberDoubleType, &result);
    return result;
}
bool IsMark(const font_size_adjust::Table& gdef, CGGlyph glyph, CFStringRef string, CFIndex source,
    double horizontalAdvance) {
    // CoreText can distribute the cluster's advance across its base and marks,
    // so CTRunGetAdvances alone cannot identify a non-spacing attachment.
    const size_t classes = gdef.U16(4);
    if (classes && gdef.Has(classes, 6)) {
        if (gdef.U16(classes) == 1) {
            const unsigned first = gdef.U16(classes + 2), count = gdef.U16(classes + 4);
            if (glyph >= first && unsigned(glyph) - first < count &&
                gdef.Has(classes + 6 + size_t(glyph - first) * 2, 2) &&
                gdef.U16(classes + 6 + size_t(glyph - first) * 2) == 3) return true;
        } else if (gdef.U16(classes) == 2) {
            const unsigned count = gdef.U16(classes + 2);
            if (gdef.Has(classes + 4, size_t(count) * 6)) for (unsigned i = 0; i < count; ++i) {
                const size_t at = classes + 4 + size_t(i) * 6;
                if (glyph >= gdef.U16(at) && glyph <= gdef.U16(at + 2)) return gdef.U16(at + 4) == 3;
            }
        }
    }
    return std::abs(horizontalAdvance) < Epsilon && source >= 0 && source < CFStringGetLength(string) &&
        CFCharacterSetIsCharacterMember(CFCharacterSetGetPredefined(kCFCharacterSetNonBase),
            CFStringGetCharacterAtIndex(string, source));
}
}
CFAttributedStringRef CreateAppleVerticalMetricsText(CFAttributedStringRef input, bool strongLtr, CFRange sourceRange) {
    const auto string = CFAttributedStringGetString(input);
    const auto length = CFStringGetLength(string);
    if (!length) return static_cast<CFAttributedStringRef>(CFRetain(input));
    Owner typesetter(CreateAppleTextTypesetter(input, strongLtr), Release);
    if (!sourceRange.length) sourceRange = CFRangeMake(0, length);
    Owner line(typesetter ? CTTypesetterCreateLine(static_cast<CTTypesetterRef>(typesetter.get()), sourceRange) : nullptr, Release);
    if (!line) return nullptr;
    std::vector<Cell> cells;
    std::map<std::pair<CTFontRef, CGGlyph>, double> corrections;
    const auto runs = CTLineGetGlyphRuns(static_cast<CTLineRef>(line.get()));
    for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
        auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i));
        auto attributes = CTRunGetAttributes(run);
        if (CFDictionaryGetValue(attributes, kCTVerticalFormsAttributeName) != kCFBooleanTrue ||
            CFDictionaryContainsKey(attributes, kCTRunDelegateAttributeName)) continue;
        auto font = static_cast<CTFontRef>(CFDictionaryGetValue(attributes, kCTFontAttributeName));
        if (!font) return nullptr;
        font_size_adjust::Table gdef(font, 0x47444546);
        const auto count = CTRunGetGlyphCount(run);
        const auto range = CTRunGetStringRange(run);
        std::vector<CGGlyph> glyphs(count);
        std::vector<CFIndex> indices(count);
        std::vector<CGSize> native(count), horizontal(count), advances(count);
        CTRunGetGlyphs(run, CFRangeMake(0, 0), glyphs.data());
        CTRunGetStringIndices(run, CFRangeMake(0, 0), indices.data());
        CTRunGetAdvances(run, CFRangeMake(0, 0), advances.data());
        CTFontGetAdvancesForGlyphs(font, kCTFontOrientationVertical, glyphs.data(), native.data(), count);
        CTFontGetAdvancesForGlyphs(font, kCTFontOrientationHorizontal, glyphs.data(), horizontal.data(), count);
        auto boundaries = indices; boundaries.push_back(range.location + range.length);
        std::sort(boundaries.begin(), boundaries.end());
        boundaries.erase(std::unique(boundaries.begin(), boundaries.end()), boundaries.end());
        for (CFIndex at = 0; at < count;) {
            CFIndex last = at + 1;
            while (last < count && indices[last] == indices[at]) ++last;
            auto start = indices[at];
            if (start < 0 || start >= length) { at = last; continue; }
            auto cluster = CFStringGetRangeOfComposedCharactersAtIndex(string, start);
            auto next = std::upper_bound(boundaries.begin(), boundaries.end(), start);
            auto end = next == boundaries.end() ? start + 1 : *next;
            end = std::max(end, cluster.location + cluster.length);
            Cell cell{CFRangeMake(cluster.location, end - cluster.location), 0};
            if (!IsControl(string, cell.range)) for (CFIndex glyph = at; glyph < last; ++glyph) {
                // Zero-width marks do not acquire their font's default cell.
                if (IsMark(gdef, glyphs[glyph], string, indices[glyph], horizontal[glyph].width) ||
                    (std::abs(advances[glyph].width) < Epsilon &&
                    std::abs(horizontal[glyph].width) < Epsilon)) continue;
                auto key = std::make_pair(font, glyphs[glyph]);
                auto found = corrections.find(key);
                if (found == corrections.end()) {
                    double aspect = 0;
                    const bool hasMetric = font_size_adjust::TryVerticalAspect(font, glyphs[glyph], aspect);
                    const double expected = CTFontGetSize(font) * (hasMetric ? aspect : 1);
                    const double nominal = native[glyph].width ? native[glyph].width : std::abs(native[glyph].height);
                    if (!std::isfinite(expected) || !std::isfinite(nominal)) return nullptr;
                    found = corrections.emplace(key, expected - nominal).first;
                }
                cell.delta += found->second;
                ++cell.glyphs;
            }
            cells.push_back(cell); at = last;
        }
    }
    if (cells.empty()) return static_cast<CFAttributedStringRef>(CFRetain(input));
    std::sort(cells.begin(), cells.end(), [](auto& a, auto& b) { return a.range.location < b.range.location; });
    std::vector<Cell> merged;
    for (const auto& cell : cells) {
        if (!merged.empty() && cell.range.location < merged.back().range.location + merged.back().range.length) {
            auto& prior = merged.back(); prior.range.length = std::max(prior.range.length,
                cell.range.location + cell.range.length - prior.range.location); prior.delta += cell.delta;
            prior.glyphs += cell.glyphs;
        } else merged.push_back(cell);
    }
    Owner outputOwner(CFAttributedStringCreateMutableCopy(kCFAllocatorDefault, 0, input), Release);
    if (!outputOwner) return nullptr;
    auto output = const_cast<CFMutableAttributedStringRef>(static_cast<CFAttributedStringRef>(outputOwner.get()));
    const int32_t ligature = 1;
    Owner ligatureValue(CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt32Type, &ligature), Release);
    if (!ligatureValue) return nullptr;
    for (const auto& cell : merged) {
        if (std::abs(cell.delta) < Epsilon || !cell.glyphs) continue;
        // Set one tracking value over an existing shaped cluster. Splitting its
        // component characters would destroy optional ligatures and marks.
        const double rate = cell.delta / cell.glyphs;
        const double tracking = rate + Number(CFAttributedStringGetAttribute(input,
            cell.range.location, kCTTrackingAttributeName, nullptr));
        Owner value(CFNumberCreate(kCFAllocatorDefault, kCFNumberDoubleType, &tracking), Release);
        Owner delta(CFNumberCreate(kCFAllocatorDefault, kCFNumberDoubleType, &rate), Release);
        if (!value || !delta) return nullptr;
        CFAttributedStringSetAttribute(output, cell.range, kCTTrackingAttributeName, value.get());
        CFAttributedStringSetAttribute(output, cell.range, CFSTR("JaliumVerticalMetricTracking"), delta.get());
        if (!CFAttributedStringGetAttribute(input, cell.range.location, kCTLigatureAttributeName, nullptr))
            CFAttributedStringSetAttribute(output, cell.range, kCTLigatureAttributeName, ligatureValue.get());
    }
    return static_cast<CFAttributedStringRef>(outputOwner.release());
}
bool HasAppleVerticalMetrics(CFAttributedStringRef text) {
    const auto length = CFAttributedStringGetLength(text);
    for (CFIndex at = 0; at < length;) {
        CFRange range{};
        if (CFAttributedStringGetAttribute(text, at, CFSTR("JaliumVerticalMetricTracking"), &range)) return true;
        if (range.length <= 0) return false;
        at = range.location + range.length;
    }
    return false;
}
double AppleVerticalMetricTail(CTLineRef line, CFAttributedStringRef text) {
    auto range = CTLineGetStringRange(line);
    auto string = CFAttributedStringGetString(text);
    auto end = range.location + range.length;
    auto controls = CFCharacterSetGetPredefined(kCFCharacterSetControl);
    while (end > range.location && CFCharacterSetIsCharacterMember(controls, CFStringGetCharacterAtIndex(string, end - 1))) --end;
    if (end <= range.location || CFCharacterSetIsCharacterMember(CFCharacterSetGetPredefined(kCFCharacterSetWhitespace),
        CFStringGetCharacterAtIndex(string, end - 1))) return 0;
    double delta = 0;
    auto runs = CTLineGetGlyphRuns(line);
    for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
        auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i));
        auto source = CTRunGetStringRange(run);
        if (end - 1 >= source.location && end - 1 < source.location + source.length)
            delta = Number(CFDictionaryGetValue(CTRunGetAttributes(run), CFSTR("JaliumVerticalMetricTracking")));
    }
    return std::min(std::max(0.0, delta), CTLineGetTrailingWhitespaceWidth(line));
}
std::vector<double> AppleVerticalMetricGlyphShifts(CTLineRef line, CFAttributedStringRef text) {
    std::vector<double> result;
    struct Cluster { double accumulated = 0, anchor = 0, origin = 0; };
    std::map<CFIndex, Cluster> clusters;
    auto string = CFAttributedStringGetString(text);
    auto runs = CTLineGetGlyphRuns(line);
    std::map<CTFontRef,std::unique_ptr<font_size_adjust::VerticalOriginMetrics>> origins;
    for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
        auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i));
        const auto count = CTRunGetGlyphCount(run);
        auto attributes = CTRunGetAttributes(run);
        if (CFDictionaryGetValue(attributes,kCTVerticalFormsAttributeName)!=kCFBooleanTrue ||
            CFDictionaryContainsKey(attributes,kCTRunDelegateAttributeName)) { result.resize(result.size()+count); continue; }
        auto font = static_cast<CTFontRef>(CFDictionaryGetValue(attributes, kCTFontAttributeName));
        if(!font){result.resize(result.size()+count);continue;}
        auto& originMetrics=origins[font];if(!originMetrics)originMetrics=std::make_unique<font_size_adjust::VerticalOriginMetrics>(font);
        font_size_adjust::Table gdef(font, 0x47444546);
        const auto rate = Number(CFDictionaryGetValue(attributes, CFSTR("JaliumVerticalMetricTracking")));
        std::vector<CGGlyph> glyphs(count);
        std::vector<CFIndex> indices(count);
        std::vector<CGSize> native(count), horizontal(count), advances(count), translations(count);
        CTRunGetGlyphs(run, CFRangeMake(0,0), glyphs.data());
        CTRunGetStringIndices(run, CFRangeMake(0,0), indices.data());
        CTRunGetAdvances(run, CFRangeMake(0,0), advances.data());
        CTFontGetAdvancesForGlyphs(font, kCTFontOrientationVertical, glyphs.data(), native.data(), count);
        CTFontGetAdvancesForGlyphs(font, kCTFontOrientationHorizontal, glyphs.data(), horizontal.data(), count);
        CTFontGetVerticalTranslationsForGlyphs(font,glyphs.data(),translations.data(),count);
        for (CFIndex glyph=0;glyph<count;++glyph) {
            if (indices[glyph]<0 || indices[glyph]>=CFStringGetLength(string)) {result.push_back(0);continue;}
            auto source=CFStringGetRangeOfComposedCharactersAtIndex(string,indices[glyph]);
            auto& cluster=clusters[source.location];
            if (IsMark(gdef, glyphs[glyph], string, indices[glyph], horizontal[glyph].width) ||
                (std::abs(advances[glyph].width)<Epsilon && std::abs(horizontal[glyph].width)<Epsilon)) {
                result.push_back(cluster.anchor+cluster.origin);continue;
            }
            double origin=0;
            cluster.origin=originMetrics->OriginY(glyphs[glyph],origin)?origin+translations[glyph].height:0;
            result.push_back(cluster.accumulated+cluster.origin);cluster.anchor=cluster.accumulated;
            double aspect=0;
            const bool hasMetric=font_size_adjust::TryVerticalAspect(font,glyphs[glyph],aspect);
            const auto expected=CTFontGetSize(font)*(hasMetric?aspect:1);
            const auto nominal=native[glyph].width?native[glyph].width:std::abs(native[glyph].height);
            cluster.accumulated+=expected-nominal-rate;
        }
    }
    return result;
}
}
#endif
