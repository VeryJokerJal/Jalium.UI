#pragma once
#ifdef __APPLE__
#include "apple_vertical_orientation.h"
#include "apple_font_visibility.h"
#include "metal_font_size_adjust_metrics.h"
#include "apple_text_combine.h"
#include <cmath>
#include <memory>
#include <unordered_map>

namespace jalium {
// Upright graphemes keep their UTF-16 for CoreText line breaking. A run delegate
// changes only a horizontal-script grapheme's metrics; its retained CTLine
// supplies isolated shaping and upright ink. Text-combine uses an atomic
// private cell composed into the existing source map, preserving public UTF-16.
class AppleUprightUnits {
    struct Shape {
        CFAttributedStringRef text = nullptr, sourceKey = nullptr;
        CTLineRef line = nullptr;
        CGFloat advance = 0, horizontalAdvance = 0, halfEm = 0, originY = 0, crossBaseline = 0, scaleX = 1;
        bool combined = false, centralBaseline = true; int32_t combineDirection = 0;
        CGRect ink = CGRectNull;
        ~Shape() { if (line) CFRelease(line); if (text) CFRelease(text); if (sourceKey) CFRelease(sourceKey); }
    };
    struct Unit {
        CFRange source{};
        std::shared_ptr<Shape> shape;
    };
    std::vector<std::unique_ptr<Unit>> units_;
    std::unordered_map<CFHashCode, std::vector<std::shared_ptr<Shape>>> shapes_;

    static UTF32Char Scalar(CFStringRef text, CFIndex& at, CFIndex end) {
        UTF32Char value = CFStringGetCharacterAtIndex(text, at++);
        if (CFStringIsSurrogateHighCharacter(value) && at < end) {
            auto low = CFStringGetCharacterAtIndex(text, at);
            if (CFStringIsSurrogateLowCharacter(low)) {
                value = CFStringGetLongCharacterForSurrogatePair(value, low); ++at;
            }
        }
        return value;
    }
    static bool NeedsUnit(CFStringRef text, CFRange range) {
        if (AppleMixedVerticalClusterIsUpright(text, range)) return false;
        CFIndex at = range.location, end = at + range.length;
        auto first = Scalar(text, at, end);
        // These vertical scripts rotate intrinsically and retain their normal
        // contextual shaping. Upright changes horizontal-only scripts.
        if ((first >= 0x1800 && first <= 0x18af) ||
            (first >= 0x11660 && first <= 0x1167f) ||
            (first >= 0xa840 && first <= 0xa87f)) return false;
        auto controls = CFCharacterSetGetPredefined(kCFCharacterSetControl);
        at = range.location;
        while (at < end)
            if (!CFCharacterSetIsLongCharacterMember(controls, Scalar(text, at, end))) return true;
        return false;
    }
    static CGFloat Width(void* value) {
        auto unit = static_cast<Unit*>(value);
        // CoreText applies delegate width once per UTF-16 unit, even when a
        // combining sequence has a single glyph. Divide by that source length.
        return unit->shape->advance / unit->source.length;
    }
    static CGFloat HalfEm(void* value) { return static_cast<Unit*>(value)->shape->halfEm; }
    static void DisposeDelegate(void*) { } // The immutable paragraph owns units.

    std::shared_ptr<Shape> GetShape(CFAttributedStringRef original, CFRange range) {
        auto piece = CFAttributedStringCreateWithSubstring(kCFAllocatorDefault, original, range);
        if (!piece) return nullptr;
        auto text = CFAttributedStringCreateMutableCopy(kCFAllocatorDefault, 0, piece);
        CFRelease(piece);
        if (!text) return nullptr;
        auto whole = CFRangeMake(0, range.length);
        CFAttributedStringRemoveAttribute(text, whole, CFSTR("JaliumSpanIndex"));
        CFAttributedStringSetAttribute(text, whole, kCTVerticalFormsAttributeName, kCFBooleanFalse);
        auto hash = CFHash(text);
        auto& candidates = shapes_[hash];
        for (const auto& shape : candidates) if (!shape->combined && CFEqual(text, shape->text)) {
            CFRelease(text); return shape;
        }
        auto shape = std::make_shared<Shape>(); shape->text = text;
        shape->line = CTLineCreateWithAttributedString(text);
        if (!shape->line) return nullptr;
        shape->horizontalAdvance = CTLineGetTypographicBounds(shape->line, nullptr, nullptr, nullptr);
        auto runs = CTLineGetGlyphRuns(shape->line);
        CTFontRef metricFont = nullptr; CGGlyph firstGlyph = 0;
        for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
            auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i));
            auto attributes = CTRunGetAttributes(run);
            if (!metricFont) {
                metricFont = static_cast<CTFontRef>(CFDictionaryGetValue(attributes, CFSTR("JaliumPositionBaseFont")));
                if (!metricFont) metricFont = static_cast<CTFontRef>(CFDictionaryGetValue(attributes, CFSTR("JaliumCapsBaseFont")));
                if (!metricFont) metricFont = static_cast<CTFontRef>(CFDictionaryGetValue(attributes, kCTFontAttributeName));
                if (CTRunGetGlyphCount(run)) CTRunGetGlyphs(run, CFRangeMake(0, 1), &firstGlyph);
            }
            if (!IsFontRunInvisible(attributes)) {
                auto ink = CTRunGetImageBounds(run, nullptr, CFRangeMake(0, 0));
                if (!CGRectIsNull(ink) && !CGRectIsInfinite(ink)) shape->ink = CGRectUnion(shape->ink, ink);
            }
        }
        if (!metricFont) metricFont = static_cast<CTFontRef>(CFAttributedStringGetAttribute(text, 0, kCTFontAttributeName, nullptr));
        if (!metricFont) return nullptr;
        const auto em = CTFontGetSize(metricFont);
        double aspect = 0;
        const bool hasMetric = font_size_adjust::TryVerticalAspect(metricFont, firstGlyph, aspect);
        shape->advance = em * (hasMetric ? aspect : 1);
        shape->halfEm = em / 2;
        shape->originY = shape->advance / 2;
        double origin = 0;
        if (font_size_adjust::TryVerticalOriginY(metricFont, firstGlyph, origin)) {
            // Use the actual vertical origin, without adding horizontal BASE
            // alignment a second time. Keep the original text as the cache key.
            auto horizontal = CFAttributedStringCreateMutableCopy(kCFAllocatorDefault, 0, text);
            if (!horizontal) return nullptr;
            for (auto key : {kCTBaselineClassAttributeName, kCTBaselineReferenceInfoAttributeName, kCTBaselineInfoAttributeName})
                CFAttributedStringRemoveAttribute(horizontal, whole, key);
            auto line = CTLineCreateWithAttributedString(horizontal); CFRelease(horizontal);
            if (!line) return nullptr;
            CFRelease(shape->line); shape->line = line;
            shape->originY = origin;
            shape->horizontalAdvance = CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr);
            shape->ink = CGRectNull;
            auto painted = CTLineGetGlyphRuns(line);
            for (CFIndex i = 0; i < CFArrayGetCount(painted); ++i) {
                auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(painted, i));
                if (!IsFontRunInvisible(CTRunGetAttributes(run))) {
                    auto ink = CTRunGetImageBounds(run, nullptr, CFRangeMake(0, 0));
                    if (!CGRectIsNull(ink) && !CGRectIsInfinite(ink)) shape->ink = CGRectUnion(shape->ink, ink);
                }
            }
        }
        // Let CoreText resolve vertical BASE/bsln alignment, including script
        // selection and variable baselines, from this same source grapheme.
        // Logical positions remove the native glyph-origin translation.
        auto vertical = CFAttributedStringCreateMutableCopy(kCFAllocatorDefault, 0, text);
        if (!vertical) return nullptr;
        CFAttributedStringSetAttribute(vertical, whole, kCTVerticalFormsAttributeName, kCFBooleanTrue);
        auto baselineLine = CTLineCreateWithAttributedString(vertical); CFRelease(vertical);
        if (!baselineLine) return nullptr;
        auto baselineRuns = CTLineGetGlyphRuns(baselineLine);
        for (CFIndex i = 0; i < CFArrayGetCount(baselineRuns); ++i) {
            auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(baselineRuns, i));
            std::vector<CGPoint> positions; AppleLogicalRunPositions(run, positions);
            if (!positions.empty()) { shape->crossBaseline = positions[0].y; break; }
        }
        CFRelease(baselineLine);
        if (!std::isfinite(shape->originY) || !std::isfinite(shape->crossBaseline)) return nullptr;
        if (!std::isfinite(shape->advance) || shape->advance < 0 || !std::isfinite(shape->horizontalAdvance)) return nullptr;
        candidates.push_back(shape);
        return shape;
    }
    std::shared_ptr<Shape> GetCombinedShape(CFAttributedStringRef original, CFRange range, CGFloat em, int32_t direction, bool centralBaseline) {
        using text_combine::Owner; using text_combine::Release;
        Owner piece(CFAttributedStringCreateWithSubstring(kCFAllocatorDefault, original, range), Release);
        Owner key(piece ? CFAttributedStringCreateMutableCopy(kCFAllocatorDefault, 0, static_cast<CFAttributedStringRef>(piece.get())) : nullptr, Release);
        if (!key) return nullptr;
        CFAttributedStringRemoveAttribute(const_cast<CFMutableAttributedStringRef>(static_cast<CFAttributedStringRef>(key.get())),
            CFRangeMake(0, range.length), CFSTR("JaliumSpanIndex"));
        auto& candidates = shapes_[CFHash(key.get())];
        for (const auto& candidate : candidates)
            if (candidate->combined && candidate->advance == em && candidate->combineDirection == direction &&
                candidate->centralBaseline == centralBaseline && CFEqual(candidate->sourceKey, key.get())) return candidate;
        Owner horizontal(text_combine::HorizontalText(original, range, direction, em), Release);
        if (!horizontal) return nullptr;
        auto shape = std::make_shared<Shape>(); shape->combined = true;
        shape->text = static_cast<CFAttributedStringRef>(horizontal.release());
        shape->line = CTLineCreateWithAttributedString(shape->text);
        if (!shape->line) return nullptr;
        CGFloat ascent = 0, descent = 0;
        shape->horizontalAdvance = CTLineGetTypographicBounds(shape->line, &ascent, &descent, nullptr);
        if (!std::isfinite(shape->horizontalAdvance) || shape->horizontalAdvance < 0) return nullptr;
        shape->advance = em; shape->halfEm = em / 2;
        shape->originY = em / 2 + (ascent - descent) / 2;
        if (!centralBaseline) {
            // Sideways text has an alphabetic parent baseline. Centre the
            // upright square between that font's text-over/text-under edges,
            // before the caller's line-height and baseline alignment shifts.
            auto font = static_cast<CTFontRef>(CFAttributedStringGetAttribute(original, range.location, CFSTR("JaliumPositionBaseFont"), nullptr));
            if (!font) font = static_cast<CTFontRef>(CFAttributedStringGetAttribute(original, range.location, CFSTR("JaliumCapsBaseFont"), nullptr));
            if (!font) font = static_cast<CTFontRef>(CFAttributedStringGetAttribute(original, range.location, kCTFontAttributeName, nullptr));
            if (!font) return nullptr;
            shape->crossBaseline = (CTFontGetAscent(font) - CTFontGetDescent(font)) / 2;
        }
        shape->scaleX = shape->horizontalAdvance > em ? em / shape->horizontalAdvance : 1;
        auto runs = CTLineGetGlyphRuns(shape->line);
        for (CFIndex j = 0; j < CFArrayGetCount(runs); ++j) {
            auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, j));
            if (IsFontRunInvisible(CTRunGetAttributes(run))) continue;
            auto ink = CTRunGetImageBounds(run, nullptr, CFRangeMake(0, 0));
            if (!CGRectIsNull(ink) && !CGRectIsInfinite(ink)) shape->ink = CGRectUnion(shape->ink, ink);
        }
        shape->sourceKey = static_cast<CFAttributedStringRef>(key.release()); shape->combineDirection = direction;
        shape->centralBaseline = centralBaseline;
        candidates.push_back(shape); return shape;
    }
    const Unit* Find(CTRunRef run) const {
        auto number = static_cast<CFNumberRef>(CFDictionaryGetValue(CTRunGetAttributes(run), CFSTR("JaliumUprightUnit")));
        int64_t index = -1;
        if (!number || !CFNumberGetValue(number, kCFNumberSInt64Type, &index) || index < 0 ||
            static_cast<uint64_t>(index) >= units_.size()) return nullptr;
        return units_[index].get();
    }
    static bool Owns(const Unit& unit, CTRunRef run) {
        auto range = CTRunGetStringRange(run); const auto end = unit.source.location + unit.source.length;
        return range.location < end && range.location + range.length >= end;
    }
    static CGAffineTransform Transform(const Unit& unit, CTLineRef line, double shift) {
        auto left = CTLineGetOffsetForStringIndex(line, unit.source.location, nullptr) + shift;
        // Counter-rotate the horizontal character in the logical column.
        // Align its font-defined vertical origin and vertical central baseline.
        return CGAffineTransformMake(0, unit.shape->scaleX, -1, 0,
            left + unit.shape->originY, -unit.shape->horizontalAdvance * unit.shape->scaleX / 2 + unit.shape->crossBaseline);
    }
    static bool Visible(const Unit& unit, const uint32_t* ranges, uint32_t count, int32_t mode,
        uint32_t ownerStart, uint32_t ownerLength) {
        const auto end = unit.source.location + unit.source.length;
        if (ownerLength != UINT32_MAX && !(end > ownerStart && uint64_t(end) <= uint64_t(ownerStart) + ownerLength)) return false;
        if (!mode) return true;
        uint32_t first = 0, last = count;
        while (first < last) {
            auto mid = first + (last - first) / 2;
            if (uint64_t(ranges[mid * 2]) + ranges[mid * 2 + 1] <= uint64_t(unit.source.location)) first = mid + 1;
            else last = mid;
        }
        bool selected = first < count && ranges[first * 2] < end;
        return mode == 1 ? selected : !selected;
    }
public:
    bool HasUnits() const { return !units_.empty(); }

    // Compose with the existing caps/emoji map, so every public range continues
    // to address the author's original UTF-16. The private placeholder supplies
    // an atomic line-breaking/bidi cell, never a second public source string.
    bool ApplyCombined(CFMutableAttributedStringRef& text, AppleCapsMap& map,
        const JaliumTextCombineUnit* input, uint32_t count, bool centralBaseline) {
        using text_combine::Owner; using text_combine::Release;
        auto string = CFAttributedStringGetString(text);
        auto source = map.source ? map.source : string;
        const auto sourceLength = CFStringGetLength(source), length = CFStringGetLength(string);
        auto boundary = [&](CFIndex at) { return at == 0 || at == sourceLength ||
            CFStringGetRangeOfComposedCharactersAtIndex(source, at).location == at; };
        std::vector<AppleCapsMap::Part> before = map.parts;
        if (!map.Active()) for (CFIndex at = 0; at < length;) {
            auto range = CFStringGetRangeOfComposedCharactersAtIndex(string, at);
            before.push_back({range, range}); at = range.location + range.length;
        }
        Owner outputOwner(CFAttributedStringCreateMutable(kCFAllocatorDefault, 0), Release);
        if (!outputOwner) return false;
        auto output = const_cast<CFMutableAttributedStringRef>(static_cast<CFAttributedStringRef>(outputOwner.get()));
        std::vector<AppleCapsMap::Part> parts; size_t cursor = 0;
        auto append = [&](const AppleCapsMap::Part& part) {
            Owner piece(CFAttributedStringCreateWithSubstring(kCFAllocatorDefault, text, part.paint), Release);
            if (!piece) return false;
            auto at = CFAttributedStringGetLength(output);
            CFAttributedStringReplaceAttributedString(output, CFRangeMake(at, 0), static_cast<CFAttributedStringRef>(piece.get()));
            parts.push_back({part.source, CFRangeMake(at, part.paint.length)}); return true;
        };
        for (uint32_t i = 0; i < count; ++i) {
            const auto& requested = input[i];
            CFRange original = CFRangeMake(requested.textPosition, requested.length);
            if (!boundary(original.location) || !boundary(original.location + original.length)) return false;
            auto range = map.PaintRange(original);
            if (!range.length || range.location + range.length > length) return false;
            while (cursor < before.size() && before[cursor].paint.location < range.location) {
                if (!append(before[cursor++])) return false;
            }
            if (cursor >= before.size() || before[cursor].paint.location != range.location) return false;
            auto shape = GetCombinedShape(text, range, requested.em, requested.direction, centralBaseline);
            if (!shape) return false;
            auto unit = std::make_unique<Unit>(); unit->shape = std::move(shape);
            unit->source = CFRangeMake(CFAttributedStringGetLength(output), 1);
            int64_t index = static_cast<int64_t>(units_.size());
            CTRunDelegateCallbacks callbacks{kCTRunDelegateCurrentVersion, DisposeDelegate, HalfEm, HalfEm, Width};
            Owner delegate(CTRunDelegateCreate(&callbacks, unit.get()), Release);
            Owner number(CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt64Type, &index), Release);
            if (!delegate || !number) return false;
            CFAttributedStringReplaceString(output, CFRangeMake(unit->source.location, 0), CFSTR("\ufffc"));
            auto attributes = CFAttributedStringGetAttributes(text, range.location + range.length - 1, nullptr);
            CFAttributedStringSetAttributes(output, unit->source, attributes, true);
            CFAttributedStringRemoveAttribute(output, unit->source, CFSTR("JaliumFontInvisible"));
            CFAttributedStringSetAttribute(output, unit->source, kCTVerticalFormsAttributeName, kCFBooleanFalse);
            CFAttributedStringSetAttribute(output, unit->source, kCTRunDelegateAttributeName, delegate.get());
            CFAttributedStringSetAttribute(output, unit->source, CFSTR("JaliumUprightUnit"), number.get());
            // Upright combined cells have strong LTR bidi behavior even when
            // the surrounding mixed paragraph retains its specified direction.
            int direction = kCTWritingDirectionLeftToRight | kCTWritingDirectionOverride;
            Owner dir(CFNumberCreate(kCFAllocatorDefault, kCFNumberIntType, &direction), Release);
            const void* values[] = {dir.get()};
            Owner directions(dir ? CFArrayCreate(kCFAllocatorDefault, values, 1, &kCFTypeArrayCallBacks) : nullptr, Release);
            if (!directions) return false;
            CFAttributedStringSetAttribute(output, unit->source, kCTWritingDirectionAttributeName, directions.get());
            parts.push_back({original, unit->source}); units_.push_back(std::move(unit));
            auto end = range.location + range.length;
            while (cursor < before.size() && before[cursor].paint.location < end) ++cursor;
        }
        while (cursor < before.size()) if (!append(before[cursor++])) return false;
        if (!map.source) map.source = static_cast<CFStringRef>(CFRetain(string));
        if (!map.original) map.original = static_cast<CFAttributedStringRef>(CFRetain(text));
        map.parts = std::move(parts); map.rebuildLines = true;
        CFRelease(text); text = const_cast<CFMutableAttributedStringRef>(static_cast<CFAttributedStringRef>(outputOwner.release()));
        return true;
    }

    bool Apply(CFMutableAttributedStringRef text, bool sourceOrientationsOnly = false) {
        auto original = CFAttributedStringCreateCopy(kCFAllocatorDefault, text);
        if (!original) return false;
        std::unique_ptr<const void, decltype(&CFRelease)> keep(original, CFRelease);
        auto string = CFAttributedStringGetString(original); auto length = CFStringGetLength(string);
        for (CFIndex at = 0; at < length;) {
            auto range = CFStringGetRangeOfComposedCharactersAtIndex(string, at);
            at = range.location + range.length;
            if (sourceOrientationsOnly && AppleSourceTextOrientation(original, range.location) != 1) continue;
            if (CFAttributedStringGetAttribute(original, range.location, CFSTR("JaliumUprightUnit"), nullptr)) continue;
            if (!NeedsUnit(string, range)) continue;
            auto unit = std::make_unique<Unit>(); unit->source = range;
            unit->shape = GetShape(original, range); if (!unit->shape) return false;
            int64_t index = static_cast<int64_t>(units_.size());
            CTRunDelegateCallbacks callbacks{kCTRunDelegateCurrentVersion, DisposeDelegate, HalfEm, HalfEm, Width};
            auto delegate = CTRunDelegateCreate(&callbacks, unit.get());
            auto number = CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt64Type, &index);
            if (!delegate || !number) { if (delegate) CFRelease(delegate); if (number) CFRelease(number); return false; }
            // The source containing the final code unit owns this atomic ink.
            // Preserve all component paints/fonts in the retained real CTLine.
            auto attributes = CFAttributedStringGetAttributes(original, at - 1, nullptr);
            CFAttributedStringSetAttributes(text, range, attributes, true);
            CFAttributedStringRemoveAttribute(text, range, CFSTR("JaliumFontInvisible"));
            CFAttributedStringSetAttribute(text, range, kCTVerticalFormsAttributeName, kCFBooleanFalse);
            CFAttributedStringSetAttribute(text, range, kCTRunDelegateAttributeName, delegate);
            CFAttributedStringSetAttribute(text, range, CFSTR("JaliumUprightUnit"), number);
            units_.push_back(std::move(unit));
            CFRelease(delegate); CFRelease(number);
        }
        return true;
    }
    bool IsUnit(CTRunRef run) const { return Find(run) != nullptr; }
    bool LayoutCell(CTRunRef run, CTLineRef line, CFRange& source, double& left, double& right) const {
        auto unit = Find(run);
        if (!unit || !Owns(*unit, run)) return false;
        source = unit->source;
        left = CTLineGetOffsetForStringIndex(line, source.location, nullptr);
        right = left + unit->shape->advance;
        return true;
    }

    CGRect Bounds(CTRunRef run, CTLineRef line, double shift, uint32_t start = 0, uint32_t length = UINT32_MAX) const {
        auto unit = Find(run);
        if (!unit || !Owns(*unit, run) || !Visible(*unit, nullptr, 0, 0, start, length) || CGRectIsNull(unit->shape->ink)) return CGRectNull;
        return CGRectApplyAffineTransform(unit->shape->ink, Transform(*unit, line, shift));
    }
    void Draw(CTRunRef run, CTLineRef line, CGContextRef context, double shift, float opacity,
        const uint32_t* ranges, uint32_t rangeCount, int32_t visibility, const float* paintColor,
        uint32_t ownerStart, uint32_t ownerLength, bool contextPaint) const {
        auto unit = Find(run);
        if (!unit || !Owns(*unit, run) || !Visible(*unit, ranges, rangeCount, visibility, ownerStart, ownerLength)) return;
        CGContextSaveGState(context);
        CGContextConcatCTM(context, Transform(*unit, line, shift));
        auto runs = CTLineGetGlyphRuns(unit->shape->line);
        for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
            auto glyphs = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i));
            auto attributes = CTRunGetAttributes(glyphs);
            if (IsFontRunInvisible(attributes)) continue;
            float alpha = 1;
            auto number = static_cast<CFNumberRef>(CFDictionaryGetValue(attributes, CFSTR("JaliumSpanOpacity")));
            if (number && !paintColor) CFNumberGetValue(number, kCFNumberFloat32Type, &alpha);
            ConfigureFontPositioning(context, CFDictionaryGetValue(attributes, CFSTR("JaliumFractionalFontPositioning")) == kCFBooleanTrue);
            CGContextSetAlpha(context, std::clamp(opacity, 0.0f, 1.0f) * alpha * (paintColor ? std::clamp(paintColor[3], 0.0f, 1.0f) : 1));
            DrawFontRunRanges(glyphs, context, CGPointZero, CFAttributedStringGetString(unit->shape->text),
                nullptr, 0, 0, CFRangeMake(0, 0), CFRangeMake(-1, 0), paintColor && !contextPaint);
        }
        CGContextRestoreGState(context);
    }
};
} // namespace jalium
#endif
