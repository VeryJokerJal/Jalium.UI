#include "apple_vertical_features.h"
#include "apple_vertical_metrics.h"
#include "metal_text_paragraph.h"
#include "metal_word_navigation.h"
#include "metal_font_visibility.h"
#include "metal_font_size_adjust.h"
#include "apple_text_format.h"
#include "metal_font_caps.h"
#include "metal_font_position.h"
#include "metal_font_emoji.h"
#include "apple_vertical_orientation.h"
#include "apple_upright_units.h"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <limits>
#include <map>
#ifdef __APPLE__
#import <CoreText/CoreText.h>
#import <CoreGraphics/CoreGraphics.h>
#endif

namespace jalium {
namespace {
std::atomic<uint64_t> gParagraphIdentity{1};
#ifdef __APPLE__
template<class T> struct CFHandle {
    T value = nullptr;
    explicit CFHandle(T v = nullptr) : value(v) {}
    ~CFHandle() { if (value) CFRelease(value); }
    CFHandle(const CFHandle&) = delete;
    CFHandle& operator=(const CFHandle&) = delete;
};
bool IsBreak(UniChar c) { return c == '\r' || c == '\n' || c == 0x2028 || c == 0x2029; }
#endif
template<class T> JaliumResult CopyResults(const std::vector<T>& values, T* results,
    uint32_t capacity, uint32_t* count)
{
    *count = static_cast<uint32_t>(values.size());
    if (!results && !capacity) return JALIUM_OK;
    if (capacity < values.size()) return JALIUM_ERROR_INVALID_ARGUMENT;
    std::copy(values.begin(), values.end(), results);
    return JALIUM_OK;
}
}

struct MetalTextParagraph::Impl {
    uint64_t identity = gParagraphIdentity.fetch_add(1, std::memory_order_relaxed);
    uint32_t length = 0;
    int32_t direction = -1;
    bool contextPaint = false;
    bool verticalForms = false;
    bool uprightForms = false;
    AppleFontLineMetrics defaultLineMetrics{};
    struct Row {
        JaliumParagraphLineMetrics metrics{};
        std::vector<JaliumTextFragmentMetrics> fragments;
        std::vector<JaliumParagraphCaret> carets;
        std::vector<JaliumParagraphCaret> logicalCarets;
#ifdef __APPLE__
        CTLineRef line = nullptr; // retained by frame
        CGFloat originX = 0;
        struct SpacedGlyphs { CTRunRef run; CFRange glyphs; CGFloat shift; };
        std::map<CTRunRef, std::vector<SpacedGlyphs>> spacedGlyphs;
        std::map<CTRunRef, std::vector<double>> metricGlyphShifts;
        CGFloat minGlyphShift = 0, maxGlyphShift = 0;
#endif
    };
    std::vector<Row> rows;
#ifdef __APPLE__
    AppleCapsMap caps;
    AppleUprightUnits upright;
    std::vector<uint8_t> positionModes;
    CFMutableAttributedStringRef attributed = nullptr;
    CTFrameRef frame = nullptr;
    std::vector<CTLineRef> retainedLines;
    ~Impl() { for (auto line : retainedLines) CFRelease(line); if (frame) CFRelease(frame); if (attributed) CFRelease(attributed); }

    CFStringRef String() const { return CFAttributedStringGetString(attributed); }
    CFStringRef Source() const { return caps.source ? caps.source : String(); }
    CFIndex SourceLength() const { return caps.SourceLength(length); }
    void SourceCaret(JaliumParagraphCaret& value) const { value.textPosition = static_cast<uint32_t>(caps.ToSource(value.textPosition)); }
    void SourceLine(JaliumParagraphLineMetrics& value) const {
        if (caps.Active() && !length) {
            value.line.textPosition = 0; value.line.length = static_cast<uint32_t>(SourceLength());
            value.line.leftCaretPosition = 0; value.line.rightCaretPosition = value.line.length;
            return;
        }
        auto& line = value.line; auto range = caps.SourceRange(CFRangeMake(line.textPosition, line.length));
        line.textPosition = range.location; line.length = range.length;
        line.leftCaretPosition = caps.ToSource(line.leftCaretPosition);
        line.rightCaretPosition = caps.ToSource(line.rightCaretPosition);
    }
    void BuildRow(Row& row, CFIndex start, CFIndex end, float y,
        CGFloat originX, CGFloat ascent, CGFloat descent, CGFloat leading, float minHeight)
    {
        AppleFontLineMetrics overridden;
        bool hasOverrides=row.line&&GetAppleFontLineMetrics(row.line,overridden,&defaultLineMetrics,defaultLineMetrics.overridden);
        if(hasOverrides){ascent=overridden.ascent;descent=overridden.descent;leading=overridden.leading;}
        if (row.line && !hasOverrides) {
            auto runs = CTLineGetGlyphRuns(row.line);
            for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
                auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i));
                auto base = static_cast<CTFontRef>(CFDictionaryGetValue(CTRunGetAttributes(run), CFSTR("JaliumCapsBaseFont")));
                auto positionBase = static_cast<CTFontRef>(CFDictionaryGetValue(CTRunGetAttributes(run), CFSTR("JaliumPositionBaseFont")));
                if (positionBase) base = positionBase;
                if (base) { ascent = std::max(ascent, CTFontGetAscent(base)); descent = std::max(descent, CTFontGetDescent(base)); leading = std::max(leading, CTFontGetLeading(base)); }
            }
        }
        auto& m = row.metrics;
        m.line.textPosition = static_cast<uint32_t>(start);
        m.line.length = static_cast<uint32_t>(end - start);
        m.line.y = y;
        m.line.height = std::max(minHeight, static_cast<float>(ascent + descent + leading));
        m.baseline = static_cast<float>(ascent) +
            (m.line.height - static_cast<float>(ascent + descent + leading)) / 2;
        row.originX = originX;
        Row* target = &row;
        CFStringRef string = String();
        if (row.line) CTLineEnumerateCaretOffsets(row.line, ^(double offset, CFIndex index, bool leadingEdge, bool*) {
            if (index < start || index >= end) return;
            CFRange cluster = caps.Cluster(string, index);
            // Combining marks and ZWJ glyphs must not expose interior carets.
            if ((leadingEdge && index != cluster.location) ||
                (!leadingEdge && index + 1 != cluster.location + cluster.length)) return;
            target->carets.push_back({static_cast<uint32_t>(cluster.location +
                (leadingEdge ? 0 : cluster.length)), leadingEdge ? 0 : 1,
                static_cast<float>(originX + offset)});
        });
        // Around an invisible bidi control CoreText can omit an affinity, or
        // enumerate the opposite run's offset for it. Resolve these boundaries
        // against the glyph run containing the adjacent source character.
        auto bidiControl = [](UniChar value) { return value == 0x061c || value == 0x200e || value == 0x200f ||
            (value >= 0x202a && value <= 0x202e) || (value >= 0x2066 && value <= 0x2069); };
        bool controlled = false;
        for (CFIndex i = start; i < end; ++i) controlled |= bidiControl(CFStringGetCharacterAtIndex(string, i));
        if (row.line) {
            struct RunEdge { CFRange source; double left, right; bool rtl; bool verticalMetric = false; };
            std::vector<RunEdge> edges;
            CFArrayRef runs = CTLineGetGlyphRuns(row.line);
            for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
                auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i));
                if (upright.IsUnit(run)) {
                    CFRange source{}; double left = 0, right = 0;
                    if (upright.LayoutCell(run, row.line, source, left, right))
                        edges.push_back({source, originX + left, originX + right, false});
                    continue;
                }
                CFIndex count = CTRunGetGlyphCount(run); if (!count) continue;
                std::vector<CGPoint> positions(count); std::vector<CGSize> advances(count);
                AppleLogicalRunPositions(run, positions);
                CTRunGetAdvances(run, CFRangeMake(0, 0), advances.data());
                std::vector<CFIndex> indices(count);
                CTRunGetStringIndices(run, CFRangeMake(0, 0), indices.data());
                auto boundaries = indices; const auto runSource = CTRunGetStringRange(run);
                boundaries.push_back(runSource.location + runSource.length);
                std::sort(boundaries.begin(), boundaries.end());
                boundaries.erase(std::unique(boundaries.begin(), boundaries.end()), boundaries.end());
                std::map<CFIndex, RunEdge> clusters;
                for (CFIndex glyph = 0; glyph < count; ++glyph) {
                    CFIndex index = indices[glyph];
                    if (index < start || index >= end || bidiControl(CFStringGetCharacterAtIndex(string, index))) continue;
                    const auto composed = caps.Cluster(string, index);
                    const auto following = std::upper_bound(boundaries.begin(), boundaries.end(), index);
                    CFIndex limit = following == boundaries.end() ? index + 1 : *following;
                    limit = FontGlyphSourceEnd(string, index, std::max(limit, composed.location + composed.length));
                    double left = originX + positions[glyph].x;
                    double right = originX + positions[glyph].x + advances[glyph].width;
                    auto [entry, added] = clusters.try_emplace(composed.location,
                        RunEdge{CFRangeMake(composed.location, limit - composed.location), left, right,
                            (CTRunGetStatus(run) & kCTRunStatusRightToLeft) != 0,
                            HasAppleVerticalMetricTracking(CTRunGetAttributes(run))});
                    if (!added) {
                        entry->second.left = std::min(entry->second.left, left);
                        entry->second.right = std::max(entry->second.right, right);
                        entry->second.source.length = std::max(entry->second.source.length, limit - composed.location);
                    }
                }
                for (const auto& [_, edge] : clusters) edges.push_back(edge);
            }
            auto resolve = [&](uint32_t position, bool backward, double candidate) {
                CFIndex character = static_cast<CFIndex>(position) - (backward ? 1 : 0);
                for (const auto& edge : edges) {
                    if (character < edge.source.location || character >= edge.source.location + edge.source.length) continue;
                    auto inside = [&](double x) { return std::isfinite(x) && x >= edge.left - .001 && x <= edge.right + .001; };
                    // Contextual kerning can place a valid platform insertion
                    // slightly outside the glyph advance. Retain only offsets
                    // near the correct source-affinity edge, not collapsed or
                    // opposite-run offsets at a line or bidi boundary.
                    const double affinityEdge = backward == edge.rtl ? edge.left : edge.right;
                    // Font transforms can round a vertical half-cell offset
                    // just below the threshold. A midpoint is not an affinity
                    // edge, including after an RTL soft line break.
                    if (!controlled && !edge.verticalMetric && std::isfinite(candidate) &&
                        std::abs(candidate - affinityEdge) + (verticalForms ? .001 : 0) <
                            std::abs(edge.right - edge.left) * .5) return candidate;
                    // A control can make CoreText enumerate the other side of
                    // a bidi boundary. The adjacent glyph cluster is authoritative.
                    if (!backward && character == edge.source.location) return edge.rtl ? edge.right : edge.left;
                    if (backward && position == edge.source.location + edge.source.length) return edge.rtl ? edge.left : edge.right;
                    if (inside(candidate)) return candidate;
                    CGFloat secondary = 0;
                    double primary = originX + CTLineGetOffsetForStringIndex(row.line, position, &secondary);
                    secondary += originX;
                    if (inside(primary) && !inside(secondary)) return primary;
                    if (inside(secondary) && !inside(primary)) return static_cast<double>(secondary);
                    double expected = (backward ? edge.rtl : !edge.rtl) ? edge.left : edge.right;
                    if (inside(primary) && inside(secondary))
                        return std::abs(primary - expected) < std::abs(secondary - expected) ? primary : static_cast<double>(secondary);
                    return expected;
                }
                if (verticalForms && !std::isfinite(candidate))
                    return originX + CTLineGetOffsetForStringIndex(row.line, position, nullptr);
                return candidate;
            };
            for (auto& caret : row.carets) caret.x = static_cast<float>(resolve(caret.textPosition, caret.backwardAffinity != 0, caret.x));
            for (CFIndex i = start; i < end;) {
                CFRange cluster = caps.Cluster(string, i);
                CFIndex next = std::min(end, cluster.location + cluster.length);
                if (next <= i) break;
                UniChar character = CFStringGetCharacterAtIndex(string, i);
                // A tab remains a measurable source cell. CoreText can omit
                // its ending affinity when a closing bidi control shares the
                // run; recover that edge from the tab's glyph advance.
                if (verticalForms || character == '\t' || !CFCharacterSetIsCharacterMember(CFCharacterSetGetPredefined(kCFCharacterSetControl), character)) {
                    for (int affinity = 0; affinity <= 1; ++affinity) {
                        uint32_t position = static_cast<uint32_t>(affinity ? next : i);
                        if (std::none_of(row.carets.begin(), row.carets.end(), [&](const auto& caret) {
                            return caret.textPosition == position && caret.backwardAffinity == affinity;
                        })) {
                            double resolved = resolve(position, affinity != 0, std::numeric_limits<double>::quiet_NaN());
                            if (std::isfinite(resolved)) row.carets.push_back({position, affinity, static_cast<float>(resolved)});
                        }
                    }
                }
                i = next;
            }
        }
        if (row.carets.empty()) {
            row.carets.push_back({static_cast<uint32_t>(start), 0, static_cast<float>(originX)});
            if (end > start) row.carets.push_back({static_cast<uint32_t>(end), 1, static_cast<float>(originX)});
        }
        // Geometry consumers can ask for every insertion offset in a long line.
        // Keep the visual edge list, with a separate index for logarithmic lookup.
        row.logicalCarets = row.carets;
        std::stable_sort(row.logicalCarets.begin(), row.logicalCarets.end(), [](const auto& a, const auto& b) {
            return a.textPosition < b.textPosition ||
                (a.textPosition == b.textPosition && a.backwardAffinity < b.backwardAffinity);
        });
        // Keep formatting-only insertions available for explicit caret queries,
        // while point hits and selection use visible source-cluster edges.
        if (controlled) {
            std::erase_if(row.carets, [&](const auto& caret) {
                CFIndex index = static_cast<CFIndex>(caret.textPosition) - (caret.backwardAffinity ? 1 : 0);
                return index >= start && index < end && bidiControl(CFStringGetCharacterAtIndex(string, index));
            });
            if (row.carets.empty()) row.carets.push_back(row.logicalCarets.front());
        }
        auto [left, right] = std::minmax_element(row.carets.begin(), row.carets.end(),
            [](const auto& a, const auto& b) { return a.x < b.x; });
        m.line.x = left->x; m.line.width = right->x - left->x;
        m.line.leftCaretPosition = left->textPosition;
        m.line.rightCaretPosition = right->textPosition;
        m.line.leftBackwardAffinity = left->backwardAffinity;
        m.line.rightBackwardAffinity = right->backwardAffinity;
    }
#endif
};

MetalTextParagraph::MetalTextParagraph(const uint16_t* text, uint32_t length, void* defaultFont,
    const std::vector<void*>& fonts, const JaliumTextSpan* spans, uint32_t spanCount,
    float width, float minHeight, int32_t alignment, int32_t direction)
    : MetalTextParagraph(text, length, defaultFont, fonts, spans, spanCount,
        width, minHeight, alignment, direction, 0) {}

MetalTextParagraph::MetalTextParagraph(const uint16_t* text, uint32_t length, void* defaultFont,
    const std::vector<void*>& fonts, const JaliumTextSpan* spans, uint32_t spanCount,
    float width, float minHeight, int32_t alignment, int32_t direction, int32_t wrapping)
    : MetalTextParagraph(text, length, defaultFont, fonts, spans, spanCount, width,
        minHeight, alignment, direction, wrapping, {}) {}

MetalTextParagraph::MetalTextParagraph(const uint16_t* text, uint32_t length, void* defaultFont,
    const std::vector<void*>& fonts, const JaliumTextSpan* spans, uint32_t spanCount,
    float width, float minHeight, int32_t alignment, int32_t direction, int32_t wrapping,
    const std::vector<void*>& visibility, const std::vector<std::vector<FontCascadeEntry>>& canonical, bool contextPaint, const JaliumTextMetrics* defaultMetrics)
    : MetalTextParagraph(text, length, defaultFont, fonts, spans, spanCount, width, minHeight,
        alignment, direction, wrapping, visibility, canonical, contextPaint, defaultMetrics, false)
{}

MetalTextParagraph::MetalTextParagraph(const uint16_t* text, uint32_t length, void* defaultFont,
    const std::vector<void*>& fonts, const JaliumTextSpan* spans, uint32_t spanCount,
    float width, float minHeight, int32_t alignment, int32_t direction, int32_t wrapping,
    const std::vector<void*>& visibility, const std::vector<std::vector<FontCascadeEntry>>& canonical,
    bool contextPaint, const JaliumTextMetrics* defaultMetrics, bool verticalForms)
    : MetalTextParagraph(text, length, defaultFont, fonts, spans, spanCount, width, minHeight,
        alignment, direction, wrapping, visibility, canonical, contextPaint, defaultMetrics, verticalForms, false)
{}

MetalTextParagraph::MetalTextParagraph(const uint16_t* text, uint32_t length, void* defaultFont,
    const std::vector<void*>& fonts, const JaliumTextSpan* spans, uint32_t spanCount,
    float width, float minHeight, int32_t alignment, int32_t direction, int32_t wrapping,
    const std::vector<void*>& visibility, const std::vector<std::vector<FontCascadeEntry>>& canonical,
    bool contextPaint, const JaliumTextMetrics* defaultMetrics, bool verticalForms, bool uprightForms)
    : MetalTextParagraph(text, length, defaultFont, fonts, spans, spanCount, width, minHeight,
        alignment, direction, wrapping, visibility, canonical, contextPaint, defaultMetrics,
        verticalForms, uprightForms, nullptr, 0)
{}

MetalTextParagraph::MetalTextParagraph(const uint16_t* text, uint32_t length, void* defaultFont,
    const std::vector<void*>& fonts, const JaliumTextSpan* spans, uint32_t spanCount,
    float width, float minHeight, int32_t alignment, int32_t direction, int32_t wrapping,
    const std::vector<void*>& visibility, const std::vector<std::vector<FontCascadeEntry>>& canonical,
    bool contextPaint, const JaliumTextMetrics* defaultMetrics, bool verticalForms, bool uprightForms,
    const JaliumTextCombineUnit* combined, uint32_t combinedCount)
    : impl_(std::make_unique<Impl>())
{
    if (uprightForms) direction = 0;
    impl_->uprightForms = uprightForms;
    impl_->length = length;
    impl_->direction = direction;
    impl_->contextPaint = contextPaint;
    impl_->verticalForms = verticalForms;
    bool centralBaseline = true;
    if (spanCount) {
        const auto* first = dynamic_cast<const MetalTextFormat*>(reinterpret_cast<const TextFormat*>(spans[0].format));
        if (first) centralBaseline = first->CentralOrientationBaseline();
    }
#ifdef __APPLE__
    CFHandle<CFStringRef> string(CFStringCreateWithCharacters(kCFAllocatorDefault, text, length));
    if (!string.value) return;
    impl_->attributed = CFAttributedStringCreateMutable(kCFAllocatorDefault, 0);
    if (!impl_->attributed) return;
    CFAttributedStringReplaceString(impl_->attributed, CFRangeMake(0, 0), string.value);
    if (verticalForms)
        CFAttributedStringSetAttribute(impl_->attributed, CFRangeMake(0, length), kCTVerticalFormsAttributeName, kCFBooleanTrue);
    CTTextAlignment align = alignment == 1 ? kCTTextAlignmentRight :
        alignment == 2 ? kCTTextAlignmentCenter : alignment == 3 ? kCTTextAlignmentJustified : kCTTextAlignmentLeft;
    CTWritingDirection dir = direction < 0 ? kCTWritingDirectionNatural :
        direction ? kCTWritingDirectionRightToLeft : kCTWritingDirectionLeftToRight;
    CTLineBreakMode breakMode = wrapping == 1 ? kCTLineBreakByClipping :
        wrapping == 2 ? kCTLineBreakByCharWrapping : kCTLineBreakByWordWrapping;
    CTParagraphStyleSetting settings[] = {
        {kCTParagraphStyleSpecifierAlignment, sizeof(align), &align},
        {kCTParagraphStyleSpecifierBaseWritingDirection, sizeof(dir), &dir},
        {kCTParagraphStyleSpecifierLineBreakMode, sizeof(breakMode), &breakMode},
    };
    CFHandle<CTParagraphStyleRef> paragraph(CTParagraphStyleCreate(settings, 3));
    if (!paragraph.value) return;
    CFAttributedStringSetAttribute(impl_->attributed, CFRangeMake(0, length), kCTParagraphStyleAttributeName, paragraph.value);
    for (uint32_t i = 0; i < spanCount; ++i) {
        const auto& span = spans[i];
        CFRange range = CFRangeMake(span.textPosition, span.length);
        CFAttributedStringSetAttribute(impl_->attributed, range, kCTFontAttributeName, fonts[i]);
        const auto* format = reinterpret_cast<const TextFormat*>(span.format);
        const auto* native = dynamic_cast<const MetalTextFormat*>(format);
        if (verticalForms && native && native->TextOrientation() >= 0) {
            const auto orientation = native->TextOrientation();
            CFHandle<CFNumberRef> value(CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt32Type, &orientation));
            if (!value.value) return;
            CFAttributedStringSetAttribute(impl_->attributed, range, CFSTR("JaliumTextOrientation"), value.value);
        }
        ApplyFontFeatureAttributes(impl_->attributed, range, native && native->IsKerningDisabled());
        CFAttributedStringSetAttribute(impl_->attributed, range, CFSTR("JaliumFractionalFontPositioning"),
            format && UsesFractionalFontPositioning(*format) ? kCFBooleanTrue : kCFBooleanFalse);
        // Color-font glyphs ignore the foreground color's alpha. Store the
        // opacity separately and apply it to each already shaped CTRun.
        CFHandle<CGColorRef> color(CGColorCreateGenericRGB(span.r, span.g, span.b, 1));
        if (!color.value) return;
        if (contextPaint) CFAttributedStringSetAttribute(impl_->attributed, range, kCTForegroundColorFromContextAttributeName, kCFBooleanTrue);
        else CFAttributedStringSetAttribute(impl_->attributed, range, kCTForegroundColorAttributeName, color.value);
        int32_t id = static_cast<int32_t>(i);
        CFHandle<CFNumberRef> number(CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt32Type, &id));
        if (!number.value) return;
        if (!contextPaint) CFAttributedStringSetAttribute(impl_->attributed, range, CFSTR("JaliumSpanIndex"), number.value);
        float alpha = contextPaint ? 1.0f : std::clamp(span.a, 0.0f, 1.0f);
        CFHandle<CFNumberRef> opacity(CFNumberCreate(kCFAllocatorDefault, kCFNumberFloat32Type, &alpha));
        if (!opacity.value) return;
        CFAttributedStringSetAttribute(impl_->attributed, range, CFSTR("JaliumSpanOpacity"), opacity.value);
        if (native) {
            native->ApplySizeAdjustAttributes(impl_->attributed, span.textPosition, span.length);
            native->ApplyCapsAttributes(impl_->attributed, span.textPosition, span.length);
            native->ApplyEmojiAttributes(impl_->attributed, span.textPosition, span.length);
            native->ApplyPositionAttributes(impl_->attributed, span.textPosition, span.length);
        }
        if (i < canonical.size()) ApplyCanonicalFontFallbacks(impl_->attributed, range, canonical[i]);
        if (verticalForms && native) native->ApplyVerticalFeatureAttributes(impl_->attributed, span.textPosition, span.length);
        if (i < visibility.size()) ApplyFontVisibility(impl_->attributed, range, static_cast<CFCharacterSetRef>(visibility[i]));
    }
    {
        auto sized=CreateAppleSizeAdjustedText(impl_->attributed);
        if(!sized)return;
        CFRelease(impl_->attributed);impl_->attributed=const_cast<CFMutableAttributedStringRef>(sized);
        auto positioned = CreateApplePositionText(impl_->attributed, &impl_->positionModes);
        if (!positioned) return;
        CFRelease(impl_->attributed); impl_->attributed = const_cast<CFMutableAttributedStringRef>(positioned);
        auto transformed = CreateAppleCapsText(impl_->attributed, impl_->caps);
        if (!transformed) return;
        CFRelease(impl_->attributed);
        impl_->attributed = const_cast<CFMutableAttributedStringRef>(transformed);
        auto emoji = CreateAppleEmojiText(impl_->attributed, impl_->caps);
        if (!emoji) return;
        CFRelease(impl_->attributed); impl_->attributed = const_cast<CFMutableAttributedStringRef>(emoji);
        auto finalized=FinalizeAppleSizeAdjustedText(impl_->attributed);
        if(!finalized)return;
        CFRelease(impl_->attributed);impl_->attributed=const_cast<CFMutableAttributedStringRef>(finalized);
        if (combinedCount && !impl_->upright.ApplyCombined(impl_->attributed, impl_->caps, combined, combinedCount, verticalForms && centralBaseline)) return;
        CFRelease(string.value); string.value = static_cast<CFStringRef>(CFRetain(impl_->String()));
        length = static_cast<uint32_t>(CFStringGetLength(string.value)); impl_->length = length;
    }
    if (verticalForms) {
        if (!ApplyAppleMixedVerticalOrientation(impl_->attributed, centralBaseline)) return;
        if (!ApplyAppleSourceTextOrientations(impl_->attributed)) return;
    }
    if (contextPaint) {
        // CoreText can retain an insertion boundary even after individual
        // attributes on both sides become equal. Assign the complete equal
        // dictionary over the combined range so a source/paint seam cannot
        // split an optional ligature or a composed character.
        for (CFIndex start = 0; start < length;) {
            CFRange range{};
            auto attributes = CFAttributedStringGetAttributes(impl_->attributed, start, &range);
            if (!attributes || range.length <= 0) return;
            CFHandle<CFDictionaryRef> retained(static_cast<CFDictionaryRef>(CFRetain(attributes)));
            CFIndex end = range.location + range.length;
            while (end < length) {
                CFRange next{};
                auto other = CFAttributedStringGetAttributes(impl_->attributed, end, &next);
                if (!other || !CFEqual(retained.value, other) || next.length <= 0) break;
                end = next.location + next.length;
            }
            CFAttributedStringSetAttributes(impl_->attributed, CFRangeMake(start, end - start), retained.value, true);
            start = end;
        }
    }
    if (verticalForms) {
        auto selected = CreateAppleVerticalFeatureText(impl_->attributed, uprightForms);
        if (!selected) return;
        CFRelease(impl_->attributed);
        impl_->attributed = const_cast<CFMutableAttributedStringRef>(selected);
    }
    if (uprightForms && !impl_->upright.Apply(impl_->attributed)) return;
    if (verticalForms && !uprightForms && !impl_->upright.Apply(impl_->attributed, true)) return;
    CFHandle<CFAttributedStringRef> verticalMetricSource(verticalForms ?
        static_cast<CFAttributedStringRef>(CFRetain(impl_->attributed)) : nullptr);
    if (verticalForms) {
        auto metrics = CreateAppleVerticalMetricsText(impl_->attributed, uprightForms);
        if (!metrics) return;
        CFRelease(impl_->attributed);
        impl_->attributed = const_cast<CFMutableAttributedStringRef>(metrics);
    }
    auto font = static_cast<CTFontRef>(defaultFont);
    impl_->defaultLineMetrics=defaultMetrics?AppleFontLineMetrics{defaultMetrics->ascent,defaultMetrics->descent,defaultMetrics->lineGap,true}:
        AppleFontLineMetrics{CTFontGetAscent(font),CTFontGetDescent(font),CTFontGetLeading(font),false};
    if (!length) {
        Impl::Row row;
        row.line = CTLineCreateWithAttributedString(impl_->attributed);
        if (!row.line) return;
        impl_->retainedLines.push_back(row.line);
        impl_->BuildRow(row, 0, 0, 0, 0, impl_->defaultLineMetrics.ascent, impl_->defaultLineMetrics.descent, impl_->defaultLineMetrics.leading, minHeight);
        impl_->rows.push_back(std::move(row));
        return;
    }
    CFHandle<CTTypesetterRef> typesetter(uprightForms ? CreateAppleTextTypesetter(impl_->attributed, true) : nullptr);
    if (uprightForms && !typesetter.value) return;
    CFHandle<CTFramesetterRef> setter(uprightForms ? CTFramesetterCreateWithTypesetter(typesetter.value)
        : CTFramesetterCreateWithAttributedString(impl_->attributed));
    if (!setter.value) return;
    CGSize suggested = CTFramesetterSuggestFrameSizeWithConstraints(setter.value, CFRangeMake(0, length),
        nullptr, CGSizeMake(width, CGFLOAT_MAX), nullptr);
    if (!std::isfinite(suggested.height)) return;
    CFHandle<CGPathRef> path(CGPathCreateWithRect(CGRectMake(0, 0, width, std::ceil(suggested.height + minHeight * 2)), nullptr));
    impl_->frame = CTFramesetterCreateFrame(setter.value, CFRangeMake(0, length), path.value, nullptr);
    if (!impl_->frame) return;
    AppleCapsLines capsLines(impl_->frame, impl_->attributed, impl_->caps, width, CGFLOAT_MAX / 4,
        wrapping, alignment, font, minHeight, false, impl_->defaultLineMetrics, uprightForms, verticalForms, verticalMetricSource.value);
    CFRange visible = capsLines.Visible();
    if (visible.location != 0 || visible.length != length) return;
    CFArrayRef lines = capsLines.Lines();
    CFIndex count = CFArrayGetCount(lines);
    float y = 0;
    for (CFIndex i = 0; i < count; ++i) {
        Impl::Row row;
        row.line = static_cast<CTLineRef>(CFArrayGetValueAtIndex(lines, i));
        // Typesetter lines are owned by AppleCapsLines, rather than the frame.
        // Metric-only rebuilding needs the same retained lifetime as casing.
        if (impl_->caps.Active() || capsLines.HasMetricOverrides() || capsLines.HasVerticalMetrics())
            impl_->retainedLines.push_back(static_cast<CTLineRef>(CFRetain(row.line)));
        CFRange range = CTLineGetStringRange(row.line);
        CFIndex end = range.location + range.length;
        while (end > range.location && IsBreak(CFStringGetCharacterAtIndex(string.value, end - 1))) --end;
        CGPoint origin{}; capsLines.Origins(CFRangeMake(i, 1), &origin);
        CGFloat ascent = 0, descent = 0, leading = 0;
        capsLines.Metrics(row.line, &ascent, &descent, &leading);
        impl_->BuildRow(row, range.location, end, y, origin.x, ascent, descent, leading, minHeight);
        y += row.metrics.line.height;
        CFArrayRef runs = CTLineGetGlyphRuns(row.line);
        if (verticalForms) {
            auto shifts=AppleVerticalMetricGlyphShifts(row.line,impl_->attributed);
            if (std::any_of(shifts.begin(),shifts.end(),[](double value){return std::abs(value)>.000001;})) {
                size_t at=0;
                for (CFIndex j=0;j<CFArrayGetCount(runs);++j) {
                    auto run=static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs,j));
                    const auto count=CTRunGetGlyphCount(run);
                    auto& values=row.metricGlyphShifts[run];auto& pieces=row.spacedGlyphs[run];
                    for (CFIndex glyph=0;glyph<count;++glyph) {
                        const auto shift=shifts[at++];values.push_back(shift);
                        if (!pieces.empty() && std::abs(pieces.back().shift-shift)<.0000001)++pieces.back().glyphs.length;
                        else pieces.push_back({run,CFRangeMake(glyph,1),shift});
                        row.minGlyphShift=std::min(row.minGlyphShift,static_cast<CGFloat>(shift));
                        row.maxGlyphShift=std::max(row.maxGlyphShift,static_cast<CGFloat>(shift));
                    }
                }
            }
        }
        for (CFIndex j = 0; j < CFArrayGetCount(runs); ++j) {
            auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, j));
            CFRange source = CTRunGetStringRange(run);
            CFIndex start = std::max(source.location, range.location);
            CFIndex last = std::min(source.location + source.length, end);
            if (last <= start) continue;
            auto number = static_cast<CFNumberRef>(CFDictionaryGetValue(CTRunGetAttributes(run), CFSTR("JaliumSpanIndex")));
            int32_t spanIndex = 0;
            if (number) {
                if (!CFNumberGetValue(number, kCFNumberSInt32Type, &spanIndex)) continue;
            } else if (!contextPaint) continue;
            if (impl_->upright.IsUnit(run)) {
                CFRange unit{}; double left = 0, right = 0;
                if (impl_->upright.LayoutCell(run, row.line, unit, left, right))
                    row.fragments.push_back({static_cast<uint32_t>(unit.location), static_cast<uint32_t>(unit.length),
                        static_cast<uint32_t>(spanIndex), static_cast<float>(origin.x + left), static_cast<float>(right - left)});
                continue;
            }
            CFIndex glyphCount = CTRunGetGlyphCount(run);
            if (!glyphCount) continue;
            std::vector<CGPoint> positions(static_cast<size_t>(glyphCount));
            std::vector<CGSize> advances(static_cast<size_t>(glyphCount));
            AppleLogicalRunPositions(run, positions);
            CTRunGetAdvances(run, CFRangeMake(0, 0), advances.data());
            double left = positions[0].x, right = left;
            for (CFIndex k = 0; k < glyphCount; ++k) {
                left = std::min(left, static_cast<double>(positions[k].x));
                right = std::max(right, static_cast<double>(positions[k].x + advances[k].width));
            }
            row.fragments.push_back({static_cast<uint32_t>(start), static_cast<uint32_t>(last - start),
                static_cast<uint32_t>(spanIndex), static_cast<float>(origin.x + left), static_cast<float>(right - left)});
        }
        impl_->rows.push_back(std::move(row));
    }
    if (length && IsBreak(CFStringGetCharacterAtIndex(string.value, length - 1))) {
        Impl::Row row;
        impl_->BuildRow(row, length, length, y, 0, impl_->defaultLineMetrics.ascent, impl_->defaultLineMetrics.descent, impl_->defaultLineMetrics.leading, minHeight);
        impl_->rows.push_back(std::move(row));
    }
#else
    (void)text; (void)defaultFont; (void)fonts; (void)spans; (void)spanCount;
    (void)width; (void)minHeight; (void)alignment; (void)direction; (void)wrapping;
#endif
}
MetalTextParagraph::~MetalTextParagraph() = default;
bool MetalTextParagraph::IsValid() const { return !impl_->rows.empty(); }
uint64_t MetalTextParagraph::CacheIdentity() const { return impl_->identity; }
uint32_t MetalTextParagraph::LineCount() const { return static_cast<uint32_t>(impl_->rows.size()); }

JaliumResult MetalTextParagraph::GetPositionModes(uint8_t* modes, uint32_t length) const
{
#ifdef __APPLE__
    if (length != impl_->SourceLength() || (length && !modes)) return JALIUM_ERROR_INVALID_ARGUMENT;
    if (length) std::copy(impl_->positionModes.begin(), impl_->positionModes.end(), modes);
    return JALIUM_OK;
#else
    (void)modes; (void)length; return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

JaliumResult MetalTextParagraph::GetCapsOperations(uint8_t* operations, uint32_t length) const
{
#ifdef __APPLE__
    if (length != impl_->SourceLength() || (length && !operations)) return JALIUM_ERROR_INVALID_ARGUMENT;
    if (impl_->caps.operations.empty()) { if (length) std::fill_n(operations, length, uint8_t(0)); }
    else std::copy(impl_->caps.operations.begin(), impl_->caps.operations.end(), operations);
    return JALIUM_OK;
#else
    (void)operations; (void)length; return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
JaliumResult MetalTextParagraph::NavigateWord(TextParagraph* const* paragraphs, const uint32_t* offsets,
    uint32_t count, const uint16_t* text, uint32_t length, uint32_t position,
    uint32_t start, uint32_t selectionLength, int32_t direction,
    bool backwardAffinity, JaliumParagraphCaret* result) const
{
#ifdef __APPLE__
    std::vector<MetalWordNavigationRow> rows;
    std::vector<uint64_t> identities;
    uint32_t previousEnd = 0;
    float y = 0;
    for (uint32_t i = 0; i < count; ++i) {
        auto paragraph = dynamic_cast<const MetalTextParagraph*>(paragraphs[i]);
        if (!paragraph || !paragraph->IsValid()) return JALIUM_ERROR_NOT_SUPPORTED;
        const auto& data = *paragraph->impl_;
        if (offsets[i] < previousEnd || data.SourceLength() > length - offsets[i]) return JALIUM_ERROR_INVALID_ARGUMENT;
        for (uint32_t j = previousEnd; j < offsets[i]; ++j)
            if (!IsBreak(text[j])) return JALIUM_ERROR_INVALID_ARGUMENT;
        for (uint32_t j = 0; j < data.SourceLength(); ++j)
            if (text[offsets[i] + j] != CFStringGetCharacterAtIndex(data.Source(), j)) return JALIUM_ERROR_INVALID_ARGUMENT;
        identities.push_back(data.identity); identities.push_back(offsets[i]);
        for (const auto& row : data.rows) {
            auto metrics = row.metrics; data.SourceLine(metrics); auto& line = metrics.line;
            MetalWordNavigationRow copy{offsets[i] + line.textPosition,
                offsets[i] + line.textPosition + line.length, y, line.height, data.direction, row.carets};
            for (auto& caret : copy.carets) { data.SourceCaret(caret); caret.textPosition += offsets[i]; }
            rows.push_back(std::move(copy)); y += line.height;
        }
        previousEnd = offsets[i] + data.SourceLength();
    }
    for (uint32_t j = previousEnd; j < length; ++j)
        if (!IsBreak(text[j])) return JALIUM_ERROR_INVALID_ARGUMENT;
    return NavigateMetalWords(text, length, identities, std::move(rows), position, start,
        selectionLength, direction, backwardAffinity, result);
#else
    (void)paragraphs; (void)offsets; (void)count; (void)text; (void)length; (void)position;
    (void)start; (void)selectionLength; (void)direction; (void)backwardAffinity; (void)result;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
bool MetalTextParagraph::ApplySpacing(const JaliumTextSpacingUnit* input, uint32_t count)
{
    if (!input || !count) return false;
    std::vector<JaliumTextAdvanceUnit> units;
    units.reserve(count);
    for (uint32_t i = 0; i < count; ++i)
        units.push_back({input[i].textPosition, input[i].length, input[i].spacing, 0, 0, input[i].flags});
    return ApplyAdvances(units.data(), count);
}

bool MetalTextParagraph::ApplyAdvances(const JaliumTextAdvanceUnit* input, uint32_t count)
{
#ifdef __APPLE__
    // Construction-only: keep the CTLine and its font resources untouched. A
    // tracking attribute on just the last character can split an ffi ligature.
    if (!input || !count || impl_->rows.empty()) return false;
    std::vector<JaliumTextAdvanceUnit> mapped;
    if (impl_->caps.Active()) {
        mapped.assign(input, input + count);
        for (auto& unit : mapped) { auto range = impl_->caps.PaintRange(CFRangeMake(unit.textPosition, unit.length)); unit.textPosition = range.location; unit.length = range.length; }
        input = mapped.data();
    }
    auto applyRow = [&](Impl::Row& row) -> bool {
        if (!row.metrics.line.length) return true;
        if (!row.line) return false;
        const uint32_t rowStart = row.metrics.line.textPosition;
        const uint32_t rowEnd = rowStart + row.metrics.line.length;
        struct Unit {
            JaliumTextAdvanceUnit source;
            double start, end, left, right, ownLeft = 0, ownRight = 0, leftPad = 0, rightPad = 0;
            double leftShift = 0, rightShift = 0, glyphShift = 0;
        };
        std::vector<Unit> units;
        std::vector<JaliumTextAdvanceUnit> ignored;
        auto originalEdge = [&](uint32_t position, bool backward) -> double {
            auto edge = std::lower_bound(row.logicalCarets.begin(), row.logicalCarets.end(),
                std::pair<uint32_t, int32_t>{position, backward ? 1 : 0}, [](const auto& a, const auto& b) {
                    return a.textPosition < b.first || (a.textPosition == b.first && a.backwardAffinity < b.second);
                });
            if (edge != row.logicalCarets.end() && edge->textPosition == position && bool(edge->backwardAffinity) == backward) return edge->x;
            return row.originX + CTLineGetOffsetForStringIndex(row.line, position, nullptr);
        };
        auto firstUnit = std::lower_bound(input, input + count, rowStart,
            [](const auto& unit, uint32_t position) { return unit.textPosition < position; });
        for (auto current = firstUnit; current != input + count && current->textPosition < rowEnd; ++current) {
            const auto& source = *current;
            if (source.length > rowEnd - source.textPosition) return false;
            if (source.flags & 1) { ignored.push_back(source); continue; }
            double start = originalEdge(source.textPosition, false);
            double end = originalEdge(source.textPosition + source.length, true);
            if (!std::isfinite(start) || !std::isfinite(end)) return false;
            units.push_back({source, start, end, std::min(start, end), std::max(start, end)});
            auto& unit = units.back();
            const double natural = unit.right - unit.left;
            if (!(source.flags & 4) || natural > .000001) {
                unit.ownLeft = end < start ? source.trailing : source.leading;
                unit.ownRight = end < start ? source.leading : source.trailing;
                const double requested = unit.ownLeft + unit.ownRight;
                if (requested < -natural) {
                    const double scale = -natural / requested;
                    unit.ownLeft *= scale; unit.ownRight *= scale;
                }
            }
        }
        if (units.empty()) {
            row.carets.clear(); row.logicalCarets.clear();
            for (const auto& source : ignored) {
                row.carets.push_back({source.textPosition, 0, 0});
                row.carets.push_back({source.textPosition + source.length, 1, 0});
            }
            row.logicalCarets = row.carets; // Input units are in logical order.
            std::stable_sort(row.logicalCarets.begin(), row.logicalCarets.end(), [](const auto& a, const auto& b) {
                return a.textPosition < b.textPosition || (a.textPosition == b.textPosition && a.backwardAffinity < b.backwardAffinity);
            });
            auto& metrics = row.metrics.line;
            metrics.x = metrics.width = 0; metrics.leftCaretPosition = rowStart; metrics.rightCaretPosition = rowEnd;
            metrics.leftBackwardAffinity = 0; metrics.rightBackwardAffinity = 1;
            for (auto& fragment : row.fragments) fragment.x = fragment.width = 0;
            return true;
        }
        std::stable_sort(units.begin(), units.end(), [](const auto& a, const auto& b) { return a.left < b.left; });
        for (size_t i = 1; i < units.size(); ++i) {
            auto& before = units[i - 1]; auto& after = units[i];
            // This implementation preserves cursive words rather than inventing
            // elongations. The same suppression applies to non-joining letter pairs.
            if ((before.source.flags & after.source.flags & 2) != 0) continue;
            double a = before.source.spacing / 2.0, b = after.source.spacing / 2.0;
            double requested = a + b;
            double limit = std::min(before.right - before.left + before.ownLeft + before.ownRight,
                after.right - after.left + after.ownLeft + after.ownRight);
            double actual = std::max(-limit, requested);
            double scale = requested < 0 && actual != requested ? actual / requested : 1;
            before.rightPad = a * scale; after.leftPad = b * scale;
        }
        double extra = 0;
        for (auto& unit : units) {
            unit.leftShift = extra;
            unit.glyphShift = extra + unit.ownLeft + unit.leftPad;
            extra += unit.ownLeft + unit.ownRight + unit.leftPad + unit.rightPad;
            unit.rightShift = extra;
            if (unit.right + unit.rightShift < unit.left + unit.leftShift - .001) return false;
        }
        std::vector<size_t> logical(units.size());
        for (size_t i = 0; i < logical.size(); ++i) logical[i] = i;
        std::sort(logical.begin(), logical.end(), [&](size_t a, size_t b) { return units[a].source.textPosition < units[b].source.textPosition; });
        auto firstAt = [&](uint32_t start) {
            return std::lower_bound(logical.begin(), logical.end(), start, [&](size_t i, uint32_t p) {
                return units[i].source.textPosition + units[i].source.length <= p;
            });
        };
        // Retained glyph clusters are translated as a whole. When low-level
        // settings force an optional ligature, its additional internal space stays
        // in its cell and the unchanged glyph is centered in that expanded cell.
        CFArrayRef runs = CTLineGetGlyphRuns(row.line);
        row.spacedGlyphs.clear();row.minGlyphShift=row.maxGlyphShift=0;
        for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
            auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i));
            CFIndex n = CTRunGetGlyphCount(run); if (!n) continue;
            std::vector<CFIndex> indices(n); CTRunGetStringIndices(run, CFRangeMake(0, 0), indices.data());
            auto boundaries = indices; auto span = CTRunGetStringRange(run);
            boundaries.push_back(span.location + span.length);
            std::sort(boundaries.begin(), boundaries.end());
            boundaries.erase(std::unique(boundaries.begin(), boundaries.end()), boundaries.end());
            for (CFIndex glyph = 0; glyph < n;) {
                CFIndex start = indices[glyph]; auto next = std::upper_bound(boundaries.begin(), boundaries.end(), start);
                CFIndex end = next == boundaries.end() ? start + 1 : *next;
                CFIndex last = glyph + 1; while (last < n && indices[last] == start) ++last;
                double shift = 0; uint32_t members = 0;
                if (start >= 0) for (auto member = firstAt(static_cast<uint32_t>(start)); member != logical.end(); ++member) {
                    const auto& unit = units[*member]; if (unit.source.textPosition >= end) break;
                    shift += unit.glyphShift; ++members;
                }
                if (members) shift /= members;
                if (!std::isfinite(shift)) return false;
                auto& chunks = row.spacedGlyphs[run];
                auto metric=row.metricGlyphShifts.find(run);
                for (CFIndex part=glyph;part<last;++part) {
                    const double combined=shift+(metric==row.metricGlyphShifts.end()?0:metric->second[part]);
                    if (!chunks.empty() && chunks.back().glyphs.location+chunks.back().glyphs.length==part &&
                        std::abs(chunks.back().shift-combined)<.0000001)++chunks.back().glyphs.length;
                    else chunks.push_back({run,CFRangeMake(part,1),combined});
                    row.minGlyphShift=std::min(row.minGlyphShift,static_cast<CGFloat>(combined));
                    row.maxGlyphShift=std::max(row.maxGlyphShift,static_cast<CGFloat>(combined));
                }
                glyph = last;
            }
        }
        row.carets.clear();
        for (const auto& unit : units) {
            const bool rtl = unit.end < unit.start;
            row.carets.push_back({unit.source.textPosition, 0, static_cast<float>(unit.start + (rtl ? unit.rightShift : unit.leftShift))});
            row.carets.push_back({unit.source.textPosition + unit.source.length, 1,
                static_cast<float>(unit.end + (rtl ? unit.leftShift : unit.rightShift))});
        }
        for (const auto& unit : ignored) {
            double original = row.originX + CTLineGetOffsetForStringIndex(row.line, unit.textPosition, nullptr);
            auto distance = [&](const auto& unit) { return std::min(std::abs(original - unit.left), std::abs(original - unit.right)); };
            auto nearest = std::lower_bound(units.begin(), units.end(), original,
                [](const auto& unit, double x) { return unit.left < x; });
            if (nearest == units.end()) nearest = std::prev(nearest);
            if (nearest != units.begin() && distance(*std::prev(nearest)) <= distance(*nearest)) --nearest;
            double mapped = std::abs(original - nearest->left) <= std::abs(original - nearest->right)
                ? nearest->left + nearest->leftShift : nearest->right + nearest->rightShift;
            row.carets.push_back({unit.textPosition, 0, static_cast<float>(mapped)});
            row.carets.push_back({unit.textPosition + unit.length, 1, static_cast<float>(mapped)});
        }
        row.logicalCarets = row.carets;
        std::stable_sort(row.logicalCarets.begin(), row.logicalCarets.end(), [](const auto& a, const auto& b) {
            return a.textPosition < b.textPosition || (a.textPosition == b.textPosition && a.backwardAffinity < b.backwardAffinity);
        });
        auto [left, right] = std::minmax_element(row.carets.begin(), row.carets.end(), [](const auto& a, const auto& b) { return a.x < b.x; });
        auto& metrics = row.metrics.line;
        metrics.x = left->x; metrics.width = right->x - left->x;
        metrics.leftCaretPosition = left->textPosition; metrics.rightCaretPosition = right->textPosition;
        metrics.leftBackwardAffinity = left->backwardAffinity; metrics.rightBackwardAffinity = right->backwardAffinity;
        for (auto& fragment : row.fragments) {
            double l = std::numeric_limits<double>::infinity(), r = -l;
            for (auto member = firstAt(fragment.textPosition); member != logical.end(); ++member) {
                const auto& unit = units[*member]; if (unit.source.textPosition >= fragment.textPosition + fragment.length) break;
                l = std::min(l, unit.left + unit.leftShift); r = std::max(r, unit.right + unit.rightShift);
            }
            if (std::isfinite(l)) { fragment.x = l; fragment.width = std::max(0.0, r - l); }
        }
        return true;
    };
    for (auto& row : impl_->rows) if (!applyRow(row)) return false;
    return true;
#else
    (void)input; (void)count; return false;
#endif
}

JaliumResult MetalTextParagraph::GetLine(uint32_t index, JaliumParagraphLineMetrics* result) const
{
    if (index >= impl_->rows.size()) return JALIUM_ERROR_INVALID_ARGUMENT;
    *result = impl_->rows[index].metrics; impl_->SourceLine(*result); return JALIUM_OK;
}
JaliumResult MetalTextParagraph::GetFragments(uint32_t line, JaliumTextFragmentMetrics* results,
    uint32_t capacity, uint32_t* count) const
{
    if (line >= impl_->rows.size()) return JALIUM_ERROR_INVALID_ARGUMENT;
    auto fragments = impl_->rows[line].fragments;
    for (auto& fragment : fragments) { auto range = impl_->caps.SourceRange(CFRangeMake(fragment.textPosition, fragment.length)); fragment.textPosition = range.location; fragment.length = range.length; }
    return CopyResults(fragments, results, capacity, count);
}
JaliumResult MetalTextParagraph::GetCaret(uint32_t line, uint32_t position, bool backward,
    JaliumParagraphCaret* result) const
{
    if (line >= impl_->rows.size()) return JALIUM_ERROR_INVALID_ARGUMENT;
    if (position > impl_->SourceLength()) return JALIUM_ERROR_INVALID_ARGUMENT;
    if (impl_->caps.Active() && !impl_->length) {
        *result = {position, backward ? 1 : 0, 0}; return JALIUM_OK;
    }
    position = static_cast<uint32_t>(impl_->caps.ToPaint(position));
    const auto& row = impl_->rows[line]; const auto& m = row.metrics.line;
    if (position < m.textPosition || position > m.textPosition + m.length) return JALIUM_ERROR_INVALID_ARGUMENT;
#ifdef __APPLE__
    if (position < impl_->length)
        position = static_cast<uint32_t>(impl_->caps.Cluster(impl_->String(), position).location);
#endif
    auto edge = std::lower_bound(row.logicalCarets.begin(), row.logicalCarets.end(),
        std::pair<uint32_t, int32_t>{position, backward ? 1 : 0}, [](const auto& value, const auto& key) {
            return value.textPosition < key.first ||
                (value.textPosition == key.first && value.backwardAffinity < key.second);
        });
    if (edge == row.logicalCarets.end() || edge->textPosition != position)
        edge = std::lower_bound(row.logicalCarets.begin(), row.logicalCarets.end(), position,
            [](const auto& value, uint32_t offset) { return value.textPosition < offset; });
    if (edge != row.logicalCarets.end() && edge->textPosition == position) {
        *result = *edge; impl_->SourceCaret(*result); return JALIUM_OK;
    }
    return JALIUM_ERROR_INVALID_ARGUMENT;
}
JaliumResult MetalTextParagraph::HitTest(uint32_t line, float x, JaliumParagraphCaret* result) const
{
    if (line >= impl_->rows.size()) return JALIUM_ERROR_INVALID_ARGUMENT;
    const auto& carets = impl_->rows[line].carets;
#ifdef __APPLE__
    const auto& shaped = impl_->rows[line];
    // Distinct logical offsets can share one visual caret at an isolate or
    // override boundary. A point inside a glyph belongs to that glyph's two
    // grapheme edges, rather than to an equally close edge of the other run.
    for (const auto& leading : carets) {
        if (leading.backwardAffinity || leading.textPosition >= impl_->length) continue;
        const auto cluster = impl_->caps.Cluster(impl_->String(), leading.textPosition);
        const uint32_t end = static_cast<uint32_t>(cluster.location + cluster.length);
        auto trailing = std::lower_bound(shaped.logicalCarets.begin(), shaped.logicalCarets.end(), std::pair{end, 1},
            [](const auto& edge, const auto& key) {
                return edge.textPosition < key.first || (edge.textPosition == key.first && edge.backwardAffinity < key.second);
            });
        if (trailing == shaped.logicalCarets.end() || trailing->textPosition != end || trailing->backwardAffinity != 1) continue;
        const float left = std::min(leading.x, trailing->x), right = std::max(leading.x, trailing->x);
        if (x > left && x < right) {
            *result = std::abs(x - leading.x) < std::abs(x - trailing->x) ? leading : *trailing;
            impl_->SourceCaret(*result); return JALIUM_OK;
        }
    }
#endif
    auto closest = std::min_element(carets.begin(), carets.end(), [&](const auto& a, const auto& b) {
        return std::abs(a.x - x) < std::abs(b.x - x);
    });
#ifdef __APPLE__
    // A bidi boundary can expose distinct logical offsets at the same visual
    // caret. Resolve that ambiguity using CoreText's hit for the shaped line,
    // while retaining the nearest grapheme edge and its affinity.
    const auto& row = impl_->rows[line];
    CFIndex hint = row.line ? CTLineGetStringIndexForPosition(row.line,
        CGPointMake(x - row.originX, 0)) : kCFNotFound;
    if (hint >= 0) {
        if (hint < impl_->length) {
            CFRange cluster = CFStringGetRangeOfComposedCharactersAtIndex(impl_->String(), hint);
            hint = cluster.location;
        }
        const float distance = std::abs(closest->x - x);
        auto preferred = std::find_if(carets.begin(), carets.end(), [&](const auto& edge) {
            return edge.textPosition == hint && std::abs(std::abs(edge.x - x) - distance) < 0.001f;
        });
        if (preferred != carets.end()) closest = preferred;
    }
#endif
    *result = *closest; impl_->SourceCaret(*result); return JALIUM_OK;
}
JaliumResult MetalTextParagraph::GetSelection(uint32_t line, uint32_t start, uint32_t length,
    JaliumTextRangeMetrics* results, uint32_t capacity, uint32_t* count) const
{
    if (line >= impl_->rows.size() || start > impl_->SourceLength() || length > impl_->SourceLength() - start)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    std::vector<JaliumTextRangeMetrics> rectangles;
#ifdef __APPLE__
    if (impl_->caps.Active() && !impl_->length) {
        const auto& m = impl_->rows[line].metrics.line;
        if (length) rectangles.push_back({start, length, 0, m.y, 0, m.height});
        return CopyResults(rectangles, results, capacity, count);
    }
    { auto range = impl_->caps.PaintRange(CFRangeMake(start, length)); start = range.location; length = range.length; }
    const auto& row = impl_->rows[line];
    const auto& m = row.metrics.line;
    std::map<CFIndex, std::pair<float, float>> edges;
    for (const auto& edge : row.carets) {
        CFIndex index = edge.textPosition;
        if (edge.backwardAffinity && index > 0) --index;
        if (index < m.textPosition || index >= m.textPosition + m.length) continue;
        CFRange cluster = impl_->caps.Cluster(impl_->String(), index);
        if (!length || cluster.location + cluster.length <= start || cluster.location >= start + length) continue;
        auto [it, added] = edges.try_emplace(cluster.location, edge.x, edge.x);
        if (!added) { it->second.first = std::min(it->second.first, edge.x); it->second.second = std::max(it->second.second, edge.x); }
    }
    for (auto [position, interval] : edges) {
        CFRange cluster = impl_->caps.Cluster(impl_->String(), position);
        if (interval.second > interval.first) rectangles.push_back({static_cast<uint32_t>(position),
            static_cast<uint32_t>(cluster.length), interval.first, m.y, interval.second - interval.first, m.height});
    }
    std::sort(rectangles.begin(), rectangles.end(), [](const auto& a, const auto& b) { return a.x < b.x; });
    std::vector<JaliumTextRangeMetrics> merged;
    for (const auto& rectangle : rectangles) {
        if (!merged.empty() && rectangle.x <= merged.back().x + merged.back().width + 0.01f) {
            auto& last = merged.back();
            last.width = std::max(last.x + last.width, rectangle.x + rectangle.width) - last.x;
            uint32_t end = std::max(last.textPosition + last.length, rectangle.textPosition + rectangle.length);
            last.textPosition = std::min(last.textPosition, rectangle.textPosition); last.length = end - last.textPosition;
        } else merged.push_back(rectangle);
    }
    rectangles = std::move(merged);
    for (auto& rectangle : rectangles) { auto range = impl_->caps.SourceRange(CFRangeMake(rectangle.textPosition, rectangle.length)); rectangle.textPosition = range.location; rectangle.length = range.length; }
#endif
    return CopyResults(rectangles, results, capacity, count);
}

bool MetalTextParagraph::UsesContextPaint() const { return impl_->contextPaint; }

JaliumResult MetalTextParagraph::GetSourceBounds(uint32_t line, uint32_t start, uint32_t length,
    JaliumTextRangeMetrics* result) const
{
    if (!result || line >= impl_->rows.size() || start > impl_->SourceLength() || length > impl_->SourceLength() - start)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    *result = {}; result->textPosition = start; result->length = length;
#ifdef __APPLE__
    if (!length) return JALIUM_OK;
    { auto range = impl_->caps.PaintRange(CFRangeMake(start, length)); start = range.location; length = range.length; }
    const auto& row = impl_->rows[line];
    CGRect bounds = CGRectNull;
    CFArrayRef runs = CTLineGetGlyphRuns(row.line);
    for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
        auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i));
        if (impl_->upright.IsUnit(run)) {
            auto include = [&](double shift) {
                auto ink = impl_->upright.Bounds(run, row.line, shift, start, length);
                if (!CGRectIsNull(ink)) { ink.origin.x += row.originX; bounds = CGRectUnion(bounds, ink); }
            };
            if (row.spacedGlyphs.empty()) include(0);
            else if (auto found = row.spacedGlyphs.find(run); found != row.spacedGlyphs.end())
                for (const auto& chunk : found->second) include(chunk.shift);
            continue;
        }
        if (IsFontRunInvisible(CTRunGetAttributes(run))) continue;
        CFIndex count = CTRunGetGlyphCount(run); if (!count) continue;
        std::vector<CFIndex> indices(count); CTRunGetStringIndices(run, CFRangeMake(0, 0), indices.data());
        auto boundaries = indices; auto span = CTRunGetStringRange(run);
        boundaries.push_back(span.location + span.length);
        std::sort(boundaries.begin(), boundaries.end());
        boundaries.erase(std::unique(boundaries.begin(), boundaries.end()), boundaries.end());
        auto include = [&](CFRange glyphs, double shift) {
            CFIndex first = -1, limit = glyphs.location + glyphs.length;
            for (CFIndex glyph = glyphs.location; glyph <= limit; ++glyph) {
                bool owned = false;
                if (glyph < limit) {
                    auto next = std::upper_bound(boundaries.begin(), boundaries.end(), indices[glyph]);
                    CFIndex end = next == boundaries.end() ? indices[glyph] + 1 : *next;
                    end = FontGlyphSourceEnd(impl_->String(), indices[glyph], end);
                    owned = end > start && uint64_t(end) <= uint64_t(start) + length;
                }
                if (owned && first < 0) first = glyph;
                if (!owned && first >= 0) {
                    CGRect ink = CTRunGetImageBounds(run, nullptr, CFRangeMake(first, glyph - first));
                    if (!CGRectIsNull(ink) && !CGRectIsInfinite(ink) && !CGRectIsEmpty(ink)) {
                        ink.origin.x += row.originX + shift; bounds = CGRectUnion(bounds, ink);
                    }
                    first = -1;
                }
            }
        };
        if (row.spacedGlyphs.empty()) include(CFRangeMake(0, count), 0);
        else if (auto found = row.spacedGlyphs.find(run); found != row.spacedGlyphs.end())
            for (const auto& chunk : found->second) include(chunk.glyphs, chunk.shift);
    }
    if (!CGRectIsNull(bounds) && !CGRectIsInfinite(bounds)) {
        result->x = CGRectGetMinX(bounds); result->y = row.metrics.baseline - CGRectGetMaxY(bounds);
        result->width = bounds.size.width; result->height = bounds.size.height;
    }
    return JALIUM_OK;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

bool MetalTextParagraph::RasterizeLine(uint32_t line, float x, float y, float opacity,
    const float* matrix, float clipWidth, float clipHeight, std::vector<uint8_t>& pixels,
    uint32_t& pixelWidth, uint32_t& pixelHeight, float& deviceX, float& deviceY,
    const uint32_t* ranges, uint32_t rangeCount, int32_t visibility, const float* paintColor,
    uint32_t ownerStart, uint32_t ownerLength) const
{
    pixels.clear(); pixelWidth = pixelHeight = 0; deviceX = deviceY = 0;
#ifdef __APPLE__
    if (line >= impl_->rows.size() || !matrix || !(clipWidth > 0) || !(clipHeight > 0)) return false;
    std::vector<uint32_t> mappedRanges;
    if (impl_->caps.Active()) {
        mappedRanges = impl_->caps.PaintRanges(ranges, rangeCount); ranges = mappedRanges.data(); rangeCount = mappedRanges.size() / 2;
        auto owner = impl_->caps.PaintOwnedRange(CFRangeMake(ownerStart, ownerLength)); ownerStart = owner.location; ownerLength = owner.length;
    }
    const auto& row = impl_->rows[line];
    if (!row.line || opacity <= 0) return false;
    for (int i = 0; i < 6; ++i) if (!std::isfinite(matrix[i])) return false;
    CGRect bounds = CTLineGetBoundsWithOptions(row.line, static_cast<CTLineBoundsOptions>(0));
    CGRect glyphBounds = CTLineGetBoundsWithOptions(row.line,
        static_cast<CTLineBoundsOptions>(kCTLineBoundsUseGlyphPathBounds | kCTLineBoundsIncludeLanguageExtents));
    if (!CGRectIsNull(glyphBounds) && !CGRectIsInfinite(glyphBounds)) bounds = CGRectUnion(bounds, glyphBounds);
    if (impl_->upright.HasUnits()) {
        auto runs = CTLineGetGlyphRuns(row.line);
        for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
            auto ink = impl_->upright.Bounds(static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i)), row.line, 0);
            if (!CGRectIsNull(ink)) bounds = CGRectUnion(bounds, ink);
        }
    }
    if (!row.spacedGlyphs.empty()) {
        bounds.origin.x += row.minGlyphShift;
        bounds.size.width += row.maxGlyphShift - row.minGlyphShift;
    }
    if (CGRectIsNull(bounds) || CGRectIsInfinite(bounds) || bounds.size.width <= 0 || bounds.size.height <= 0) return false;
    double baselineX = x + row.originX, baselineY = y + row.metrics.baseline;
    auto mapPoint = [&](double cx, double cy) {
        return CGPointMake(matrix[0] * (baselineX + cx) + matrix[2] * (baselineY - cy) + matrix[4],
            matrix[1] * (baselineX + cx) + matrix[3] * (baselineY - cy) + matrix[5]);
    };
    CGPoint corners[] = {mapPoint(CGRectGetMinX(bounds), CGRectGetMinY(bounds)),
        mapPoint(CGRectGetMaxX(bounds), CGRectGetMinY(bounds)), mapPoint(CGRectGetMinX(bounds), CGRectGetMaxY(bounds)),
        mapPoint(CGRectGetMaxX(bounds), CGRectGetMaxY(bounds))};
    double left = corners[0].x, right = left, top = corners[0].y, bottom = top;
    for (const auto& p : corners) { left = std::min(left, p.x); right = std::max(right, p.x); top = std::min(top, p.y); bottom = std::max(bottom, p.y); }
    if (!std::isfinite(left) || !std::isfinite(right) || !std::isfinite(top) || !std::isfinite(bottom)) return false;
    left = std::max(0.0, std::floor(left - 2)); top = std::max(0.0, std::floor(top - 2));
    right = std::min(static_cast<double>(clipWidth), std::ceil(right + 2));
    bottom = std::min(static_cast<double>(clipHeight), std::ceil(bottom + 2));
    if (right <= left || bottom <= top || right - left > 32768 || bottom - top > 32768) return false;
    pixelWidth = static_cast<uint32_t>(right - left); pixelHeight = static_cast<uint32_t>(bottom - top);
    size_t rowBytes = static_cast<size_t>(pixelWidth) * 4;
    if (pixelHeight > std::numeric_limits<size_t>::max() / rowBytes) return false;
    pixels.assign(rowBytes * pixelHeight, 0);
    CFHandle<CGColorSpaceRef> colorSpace(CGColorSpaceCreateWithName(kCGColorSpaceSRGB));
    CFHandle<CGContextRef> context(CGBitmapContextCreate(pixels.data(), pixelWidth, pixelHeight, 8,
        rowBytes, colorSpace.value, kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little));
    if (!context.value) { pixels.clear(); pixelWidth = pixelHeight = 0; return false; }
    CGAffineTransform transform = CGAffineTransformMake(matrix[0], -matrix[1], -matrix[2], matrix[3],
        matrix[0] * baselineX + matrix[2] * baselineY + matrix[4] - left,
        pixelHeight - (matrix[1] * baselineX + matrix[3] * baselineY + matrix[5] - top));
    CGContextConcatCTM(context.value, transform); CGContextSetTextMatrix(context.value, CGAffineTransformIdentity);
    CFHandle<CGColorRef> overrideColor(paintColor ? CGColorCreateGenericRGB(paintColor[0], paintColor[1], paintColor[2], 1) : nullptr);
    CFArrayRef runs = CTLineGetGlyphRuns(row.line);
    for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
        auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i));
        if (impl_->upright.IsUnit(run)) {
            CGContextSaveGState(context.value);
            if (overrideColor.value) CGContextSetFillColorWithColor(context.value, overrideColor.value);
            auto paint = [&](double shift) { impl_->upright.Draw(run, row.line, context.value, shift, opacity,
                ranges, rangeCount, visibility, paintColor, ownerStart, ownerLength, impl_->contextPaint); };
            if (row.spacedGlyphs.empty()) paint(0);
            else if (auto found = row.spacedGlyphs.find(run); found != row.spacedGlyphs.end())
                for (const auto& chunk : found->second) paint(chunk.shift);
            CGContextRestoreGState(context.value);
            continue;
        }
        if (IsFontRunInvisible(CTRunGetAttributes(run))) continue;
        auto number = static_cast<CFNumberRef>(CFDictionaryGetValue(CTRunGetAttributes(run), CFSTR("JaliumSpanOpacity")));
        float alpha = 1;
        if (number && !paintColor) CFNumberGetValue(number, kCFNumberFloat32Type, &alpha);
        CGContextSaveGState(context.value);
        ConfigureFontPositioning(context.value,
            CFDictionaryGetValue(CTRunGetAttributes(run), CFSTR("JaliumFractionalFontPositioning")) == kCFBooleanTrue);
        CGContextSetAlpha(context.value, std::clamp(opacity, 0.0f, 1.0f) * alpha * (paintColor ? std::clamp(paintColor[3], 0.0f, 1.0f) : 1));
        if (overrideColor.value) CGContextSetFillColorWithColor(context.value, overrideColor.value);
        auto owned = ownerLength == UINT32_MAX ? CFRangeMake(-1, 0) : CFRangeMake(ownerStart, ownerLength);
        if (row.spacedGlyphs.empty())
            DrawFontRunRanges(run, context.value, CGPointZero, impl_->String(), ranges, rangeCount, visibility, CFRangeMake(0, 0), owned, paintColor && !impl_->contextPaint);
        else if (auto found = row.spacedGlyphs.find(run); found != row.spacedGlyphs.end())
            for (const auto& cluster : found->second) DrawFontRunRanges(run, context.value, CGPointMake(cluster.shift, 0),
                impl_->String(), ranges, rangeCount, visibility, cluster.glyphs, owned, paintColor && !impl_->contextPaint);
        CGContextRestoreGState(context.value);
    }
    deviceX = static_cast<float>(left); deviceY = static_cast<float>(top); return true;
#else
    (void)line; (void)x; (void)y; (void)opacity; (void)matrix; (void)clipWidth; (void)clipHeight;
    return false;
#endif
}
}
