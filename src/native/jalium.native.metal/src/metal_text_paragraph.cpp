#include "metal_text_paragraph.h"
#include "metal_word_navigation.h"
#include "metal_font_visibility.h"
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
    struct Row {
        JaliumParagraphLineMetrics metrics{};
        std::vector<JaliumTextFragmentMetrics> fragments;
        std::vector<JaliumParagraphCaret> carets;
        std::vector<JaliumParagraphCaret> logicalCarets;
#ifdef __APPLE__
        CTLineRef line = nullptr; // retained by frame
        CGFloat originX = 0;
#endif
    };
    std::vector<Row> rows;
#ifdef __APPLE__
    CFMutableAttributedStringRef attributed = nullptr;
    CTFrameRef frame = nullptr;
    ~Impl() { if (frame) CFRelease(frame); if (attributed) CFRelease(attributed); }

    CFStringRef String() const { return CFAttributedStringGetString(attributed); }
    void BuildRow(Row& row, CFIndex start, CFIndex end, float y,
        CGFloat originX, CGFloat ascent, CGFloat descent, CGFloat leading, float minHeight)
    {
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
            CFRange cluster = CFStringGetRangeOfComposedCharactersAtIndex(string, index);
            // Combining marks and ZWJ glyphs must not expose interior carets.
            if ((leadingEdge && index != cluster.location) ||
                (!leadingEdge && index + 1 != cluster.location + cluster.length)) return;
            target->carets.push_back({static_cast<uint32_t>(cluster.location +
                (leadingEdge ? 0 : cluster.length)), leadingEdge ? 0 : 1,
                static_cast<float>(originX + offset)});
        });
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
    const std::vector<void*>& visibility, const std::vector<std::vector<FontCascadeEntry>>& canonical)
    : impl_(std::make_unique<Impl>())
{
    impl_->length = length;
    impl_->direction = direction;
#ifdef __APPLE__
    CFHandle<CFStringRef> string(CFStringCreateWithCharacters(kCFAllocatorDefault, text, length));
    if (!string.value) return;
    impl_->attributed = CFAttributedStringCreateMutable(kCFAllocatorDefault, 0);
    if (!impl_->attributed) return;
    CFAttributedStringReplaceString(impl_->attributed, CFRangeMake(0, 0), string.value);
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
        // Color-font glyphs ignore the foreground color's alpha. Store the
        // opacity separately and apply it to each already shaped CTRun.
        CFHandle<CGColorRef> color(CGColorCreateGenericRGB(span.r, span.g, span.b, 1));
        if (!color.value) return;
        CFAttributedStringSetAttribute(impl_->attributed, range, kCTForegroundColorAttributeName, color.value);
        int32_t id = static_cast<int32_t>(i);
        CFHandle<CFNumberRef> number(CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt32Type, &id));
        if (!number.value) return;
        CFAttributedStringSetAttribute(impl_->attributed, range, CFSTR("JaliumSpanIndex"), number.value);
        float alpha = std::clamp(span.a, 0.0f, 1.0f);
        CFHandle<CFNumberRef> opacity(CFNumberCreate(kCFAllocatorDefault, kCFNumberFloat32Type, &alpha));
        if (!opacity.value) return;
        CFAttributedStringSetAttribute(impl_->attributed, range, CFSTR("JaliumSpanOpacity"), opacity.value);
        if (i < canonical.size()) ApplyCanonicalFontFallbacks(impl_->attributed, range, canonical[i]);
        if (i < visibility.size()) ApplyFontVisibility(impl_->attributed, range, static_cast<CFCharacterSetRef>(visibility[i]));
    }
    auto font = static_cast<CTFontRef>(defaultFont);
    if (!length) {
        Impl::Row row;
        impl_->BuildRow(row, 0, 0, 0, 0, CTFontGetAscent(font), CTFontGetDescent(font), CTFontGetLeading(font), minHeight);
        impl_->rows.push_back(std::move(row));
        return;
    }
    CFHandle<CTFramesetterRef> setter(CTFramesetterCreateWithAttributedString(impl_->attributed));
    if (!setter.value) return;
    CGSize suggested = CTFramesetterSuggestFrameSizeWithConstraints(setter.value, CFRangeMake(0, length),
        nullptr, CGSizeMake(width, CGFLOAT_MAX), nullptr);
    if (!std::isfinite(suggested.height)) return;
    CFHandle<CGPathRef> path(CGPathCreateWithRect(CGRectMake(0, 0, width, std::ceil(suggested.height + minHeight * 2)), nullptr));
    impl_->frame = CTFramesetterCreateFrame(setter.value, CFRangeMake(0, length), path.value, nullptr);
    if (!impl_->frame) return;
    CFRange visible = CTFrameGetVisibleStringRange(impl_->frame);
    if (visible.location != 0 || visible.length != length) return;
    CFArrayRef lines = CTFrameGetLines(impl_->frame);
    CFIndex count = CFArrayGetCount(lines);
    float y = 0;
    for (CFIndex i = 0; i < count; ++i) {
        Impl::Row row;
        row.line = static_cast<CTLineRef>(CFArrayGetValueAtIndex(lines, i));
        CFRange range = CTLineGetStringRange(row.line);
        CFIndex end = range.location + range.length;
        while (end > range.location && IsBreak(CFStringGetCharacterAtIndex(string.value, end - 1))) --end;
        CGPoint origin{}; CTFrameGetLineOrigins(impl_->frame, CFRangeMake(i, 1), &origin);
        CGFloat ascent = 0, descent = 0, leading = 0;
        CTLineGetTypographicBounds(row.line, &ascent, &descent, &leading);
        impl_->BuildRow(row, range.location, end, y, origin.x, ascent, descent, leading, minHeight);
        y += row.metrics.line.height;
        CFArrayRef runs = CTLineGetGlyphRuns(row.line);
        for (CFIndex j = 0; j < CFArrayGetCount(runs); ++j) {
            auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, j));
            CFRange source = CTRunGetStringRange(run);
            CFIndex start = std::max(source.location, range.location);
            CFIndex last = std::min(source.location + source.length, end);
            if (last <= start) continue;
            auto number = static_cast<CFNumberRef>(CFDictionaryGetValue(CTRunGetAttributes(run), CFSTR("JaliumSpanIndex")));
            int32_t spanIndex = 0;
            if (!number || !CFNumberGetValue(number, kCFNumberSInt32Type, &spanIndex)) continue;
            CFIndex glyphCount = CTRunGetGlyphCount(run);
            if (!glyphCount) continue;
            std::vector<CGPoint> positions(static_cast<size_t>(glyphCount));
            std::vector<CGSize> advances(static_cast<size_t>(glyphCount));
            CTRunGetPositions(run, CFRangeMake(0, 0), positions.data());
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
        impl_->BuildRow(row, length, length, y, 0, CTFontGetAscent(font), CTFontGetDescent(font), CTFontGetLeading(font), minHeight);
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
        if (offsets[i] < previousEnd || data.length > length - offsets[i]) return JALIUM_ERROR_INVALID_ARGUMENT;
        for (uint32_t j = previousEnd; j < offsets[i]; ++j)
            if (!IsBreak(text[j])) return JALIUM_ERROR_INVALID_ARGUMENT;
        for (uint32_t j = 0; j < data.length; ++j)
            if (text[offsets[i] + j] != CFStringGetCharacterAtIndex(data.String(), j)) return JALIUM_ERROR_INVALID_ARGUMENT;
        identities.push_back(data.identity); identities.push_back(offsets[i]);
        for (const auto& row : data.rows) {
            auto& line = row.metrics.line;
            MetalWordNavigationRow copy{offsets[i] + line.textPosition,
                offsets[i] + line.textPosition + line.length, y, line.height, data.direction, row.carets};
            for (auto& caret : copy.carets) caret.textPosition += offsets[i];
            rows.push_back(std::move(copy)); y += line.height;
        }
        previousEnd = offsets[i] + data.length;
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
JaliumResult MetalTextParagraph::GetLine(uint32_t index, JaliumParagraphLineMetrics* result) const
{
    if (index >= impl_->rows.size()) return JALIUM_ERROR_INVALID_ARGUMENT;
    *result = impl_->rows[index].metrics; return JALIUM_OK;
}
JaliumResult MetalTextParagraph::GetFragments(uint32_t line, JaliumTextFragmentMetrics* results,
    uint32_t capacity, uint32_t* count) const
{
    if (line >= impl_->rows.size()) return JALIUM_ERROR_INVALID_ARGUMENT;
    return CopyResults(impl_->rows[line].fragments, results, capacity, count);
}
JaliumResult MetalTextParagraph::GetCaret(uint32_t line, uint32_t position, bool backward,
    JaliumParagraphCaret* result) const
{
    if (line >= impl_->rows.size()) return JALIUM_ERROR_INVALID_ARGUMENT;
    const auto& row = impl_->rows[line]; const auto& m = row.metrics.line;
    if (position < m.textPosition || position > m.textPosition + m.length) return JALIUM_ERROR_INVALID_ARGUMENT;
#ifdef __APPLE__
    if (position < impl_->length)
        position = static_cast<uint32_t>(CFStringGetRangeOfComposedCharactersAtIndex(impl_->String(), position).location);
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
        *result = *edge; return JALIUM_OK;
    }
    return JALIUM_ERROR_INVALID_ARGUMENT;
}
JaliumResult MetalTextParagraph::HitTest(uint32_t line, float x, JaliumParagraphCaret* result) const
{
    if (line >= impl_->rows.size()) return JALIUM_ERROR_INVALID_ARGUMENT;
    const auto& carets = impl_->rows[line].carets;
    auto closest = std::min_element(carets.begin(), carets.end(), [&](const auto& a, const auto& b) {
        return std::abs(a.x - x) < std::abs(b.x - x);
    });
    *result = *closest; return JALIUM_OK;
}
JaliumResult MetalTextParagraph::GetSelection(uint32_t line, uint32_t start, uint32_t length,
    JaliumTextRangeMetrics* results, uint32_t capacity, uint32_t* count) const
{
    if (line >= impl_->rows.size() || start > impl_->length || length > impl_->length - start)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    std::vector<JaliumTextRangeMetrics> rectangles;
#ifdef __APPLE__
    const auto& row = impl_->rows[line];
    const auto& m = row.metrics.line;
    std::map<CFIndex, std::pair<float, float>> edges;
    for (const auto& edge : row.carets) {
        CFIndex index = edge.textPosition;
        if (edge.backwardAffinity && index > 0) --index;
        if (index < m.textPosition || index >= m.textPosition + m.length) continue;
        CFRange cluster = CFStringGetRangeOfComposedCharactersAtIndex(impl_->String(), index);
        if (!length || cluster.location + cluster.length <= start || cluster.location >= start + length) continue;
        auto [it, added] = edges.try_emplace(cluster.location, edge.x, edge.x);
        if (!added) { it->second.first = std::min(it->second.first, edge.x); it->second.second = std::max(it->second.second, edge.x); }
    }
    for (auto [position, interval] : edges) {
        CFRange cluster = CFStringGetRangeOfComposedCharactersAtIndex(impl_->String(), position);
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
#endif
    return CopyResults(rectangles, results, capacity, count);
}

bool MetalTextParagraph::RasterizeLine(uint32_t line, float x, float y, float opacity,
    const float* matrix, float clipWidth, float clipHeight, std::vector<uint8_t>& pixels,
    uint32_t& pixelWidth, uint32_t& pixelHeight, float& deviceX, float& deviceY) const
{
    pixels.clear(); pixelWidth = pixelHeight = 0; deviceX = deviceY = 0;
#ifdef __APPLE__
    if (line >= impl_->rows.size() || !matrix || !(clipWidth > 0) || !(clipHeight > 0)) return false;
    const auto& row = impl_->rows[line];
    if (!row.line || opacity <= 0) return false;
    for (int i = 0; i < 6; ++i) if (!std::isfinite(matrix[i])) return false;
    CGRect bounds = CTLineGetBoundsWithOptions(row.line, static_cast<CTLineBoundsOptions>(0));
    CGRect glyphBounds = CTLineGetBoundsWithOptions(row.line,
        static_cast<CTLineBoundsOptions>(kCTLineBoundsUseGlyphPathBounds | kCTLineBoundsIncludeLanguageExtents));
    if (!CGRectIsNull(glyphBounds) && !CGRectIsInfinite(glyphBounds)) bounds = CGRectUnion(bounds, glyphBounds);
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
    CFArrayRef runs = CTLineGetGlyphRuns(row.line);
    for (CFIndex i = 0; i < CFArrayGetCount(runs); ++i) {
        auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, i));
        if (IsFontRunInvisible(CTRunGetAttributes(run))) continue;
        auto number = static_cast<CFNumberRef>(CFDictionaryGetValue(CTRunGetAttributes(run), CFSTR("JaliumSpanOpacity")));
        float alpha = 1;
        if (number) CFNumberGetValue(number, kCFNumberFloat32Type, &alpha);
        CGContextSaveGState(context.value);
        CGContextSetAlpha(context.value, std::clamp(opacity, 0.0f, 1.0f) * alpha);
        DrawFontRun(run, context.value, CGPointZero);
        CGContextRestoreGState(context.value);
    }
    deviceX = static_cast<float>(left); deviceY = static_cast<float>(top); return true;
#else
    (void)line; (void)x; (void)y; (void)opacity; (void)matrix; (void)clipWidth; (void)clipHeight;
    return false;
#endif
}
}
