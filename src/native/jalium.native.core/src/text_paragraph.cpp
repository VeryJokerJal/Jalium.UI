#include "jalium_backend.h"
#include "jalium_abi_guard.h"
#include "jalium_string_util.h"
#include <cmath>
#include <new>

namespace {
template<class Action> JaliumResult Guard(Action action) noexcept
{
    try { return action(); }
    catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_UNKNOWN; }
}
jalium::TextParagraph* Layout(JaliumTextParagraph* value)
{ return reinterpret_cast<jalium::TextParagraph*>(value); }
}

extern "C" {
JALIUM_API JaliumTextParagraph* jalium_text_paragraph_create(JaliumTextFormat* format,
    const uint16_t* text, uint32_t length, const JaliumTextSpan* spans, uint32_t count,
    float width, float minHeight, int32_t alignment, int32_t direction)
{
    if (!format || !text || length > jalium::kMaxManagedTextCodeUnits ||
        count > length || (count && !spans) || !std::isfinite(width) || width <= 0 ||
        !std::isfinite(minHeight) || minHeight < 0 || alignment < 0 || alignment > 3 ||
        direction < -1 || direction > 1) return nullptr;
    uint32_t end = 0;
    for (uint32_t i = 0; i < count; ++i) {
        const auto& span = spans[i];
        if (!span.format || span.textPosition != end || !span.length ||
            span.length > length - end || !std::isfinite(span.r) || !std::isfinite(span.g) ||
            !std::isfinite(span.b) || !std::isfinite(span.a)) return nullptr;
        end += span.length;
    }
    if (end != length) return nullptr;
    try {
        auto provider = dynamic_cast<jalium::TextParagraphProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? reinterpret_cast<JaliumTextParagraph*>(provider->CreateParagraph(
            text, length, spans, count, width, minHeight, alignment, direction)) : nullptr;
    } catch (...) { return nullptr; }
}
static JaliumTextParagraph* CreateWrappedParagraph(JaliumTextFormat* format,
    const uint16_t* text, uint32_t length, const JaliumTextSpan* spans, uint32_t count,
    float width, float minHeight, int32_t alignment, int32_t direction, int32_t wrapping, bool vertical, bool upright = false)
{
    if (!format || !text || length > jalium::kMaxManagedTextCodeUnits ||
        count > length || (count && !spans) || !std::isfinite(width) || width <= 0 ||
        !std::isfinite(minHeight) || minHeight < 0 || alignment < 0 || alignment > 3 ||
        direction < -1 || direction > 1 || wrapping < 0 || wrapping > 2) return nullptr;
    uint32_t end = 0;
    for (uint32_t i = 0; i < count; ++i) {
        const auto& span = spans[i];
        if (!span.format || span.textPosition != end || !span.length ||
            span.length > length - end || !std::isfinite(span.r) || !std::isfinite(span.g) ||
            !std::isfinite(span.b) || !std::isfinite(span.a)) return nullptr;
        end += span.length;
    }
    if (end != length) return nullptr;
    try {
        if (upright) {
            auto provider = dynamic_cast<jalium::TextParagraphUprightProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
            return provider ? reinterpret_cast<JaliumTextParagraph*>(provider->CreateUprightParagraph(
                text, length, spans, count, width, minHeight, alignment, direction, wrapping)) : nullptr;
        }
        if (vertical) {
            auto provider = dynamic_cast<jalium::TextParagraphVerticalProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
            return provider ? reinterpret_cast<JaliumTextParagraph*>(provider->CreateVerticalParagraph(
                text, length, spans, count, width, minHeight, alignment, direction, wrapping)) : nullptr;
        }
        auto provider = dynamic_cast<jalium::TextParagraphWrappingProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? reinterpret_cast<JaliumTextParagraph*>(provider->CreateParagraphWithWrapping(
            text, length, spans, count, width, minHeight, alignment, direction, wrapping)) : nullptr;
    } catch (...) { return nullptr; }
}
JALIUM_API JaliumTextParagraph* jalium_text_paragraph_create_with_wrapping(JaliumTextFormat* format,
    const uint16_t* text, uint32_t length, const JaliumTextSpan* spans, uint32_t count,
    float width, float minHeight, int32_t alignment, int32_t direction, int32_t wrapping)
{ return CreateWrappedParagraph(format, text, length, spans, count, width, minHeight, alignment, direction, wrapping, false); }
JALIUM_API JaliumTextParagraph* jalium_text_paragraph_create_vertical(JaliumTextFormat* format,
    const uint16_t* text, uint32_t length, const JaliumTextSpan* spans, uint32_t count,
    float width, float minHeight, int32_t alignment, int32_t direction, int32_t wrapping)
{ return CreateWrappedParagraph(format, text, length, spans, count, width, minHeight, alignment, direction, wrapping, true); }
JALIUM_API JaliumTextParagraph* jalium_text_paragraph_create_upright(JaliumTextFormat* format,
    const uint16_t* text, uint32_t length, const JaliumTextSpan* spans, uint32_t count,
    float width, float minHeight, int32_t alignment, int32_t direction, int32_t wrapping)
{ return CreateWrappedParagraph(format, text, length, spans, count, width, minHeight, alignment, direction, wrapping, true, true); }
static JaliumTextParagraph* CreateSpacedParagraph(bool contextPaint, JaliumTextFormat* format,
    const uint16_t* text, uint32_t length, const JaliumTextSpan* spans, uint32_t count,
    const JaliumTextSpacingUnit* units, uint32_t unitCount,
    float width, float minHeight, int32_t direction)
{
    if (!format || !text || !length || length > jalium::kMaxManagedTextCodeUnits ||
        !count || count > length || !spans || !unitCount || unitCount > length || !units ||
        !std::isfinite(width) || width <= 0 || !std::isfinite(minHeight) || minHeight < 0 ||
        direction < -1 || direction > 1) return nullptr;
    uint32_t end = 0;
    for (uint32_t i = 0; i < count; ++i) {
        const auto& span = spans[i];
        if (!span.format || span.textPosition != end || !span.length || span.length > length - end ||
            !std::isfinite(span.r) || !std::isfinite(span.g) || !std::isfinite(span.b) || !std::isfinite(span.a)) return nullptr;
        end += span.length;
    }
    if (end != length) return nullptr;
    end = 0;
    for (uint32_t i = 0; i < unitCount; ++i) {
        const auto& unit = units[i];
        if (unit.textPosition != end || !unit.length || unit.length > length - end ||
            !std::isfinite(unit.spacing) || std::abs(unit.spacing) > 1000000 || (unit.flags & ~3u)) return nullptr;
        end += unit.length;
    }
    if (end != length) return nullptr;
    try {
        if (contextPaint) {
            auto provider = dynamic_cast<jalium::TextParagraphContextProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
            return provider ? reinterpret_cast<JaliumTextParagraph*>(provider->CreateContextParagraph(
                text, length, spans, count, units, unitCount, width, minHeight, direction)) : nullptr;
        }
        auto provider = dynamic_cast<jalium::TextParagraphSpacingProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? reinterpret_cast<JaliumTextParagraph*>(provider->CreateSpacedParagraph(
            text, length, spans, count, units, unitCount, width, minHeight, direction)) : nullptr;
    } catch (...) { return nullptr; }
}
JALIUM_API JaliumTextParagraph* jalium_text_paragraph_create_spaced(JaliumTextFormat* format,
    const uint16_t* text, uint32_t length, const JaliumTextSpan* spans, uint32_t count,
    const JaliumTextSpacingUnit* units, uint32_t unitCount, float width, float minHeight, int32_t direction)
{ return CreateSpacedParagraph(false, format, text, length, spans, count, units, unitCount, width, minHeight, direction); }
JALIUM_API JaliumTextParagraph* jalium_text_paragraph_create_context_spaced(JaliumTextFormat* format,
    const uint16_t* text, uint32_t length, const JaliumTextSpan* spans, uint32_t count,
    const JaliumTextSpacingUnit* units, uint32_t unitCount, float width, float minHeight, int32_t direction)
{ return CreateSpacedParagraph(true, format, text, length, spans, count, units, unitCount, width, minHeight, direction); }
static JaliumTextParagraph* CreateAdvancedParagraph(bool contextPaint, JaliumTextFormat* format,
    const uint16_t* text, uint32_t length, const JaliumTextSpan* spans, uint32_t count,
    const JaliumTextAdvanceUnit* units, uint32_t unitCount,
    float width, float minHeight, int32_t direction, bool vertical = false, bool upright = false)
{
    if (!format || !text || !length || length > jalium::kMaxManagedTextCodeUnits ||
        !count || count > length || !spans || !unitCount || unitCount > length || !units ||
        !std::isfinite(width) || width <= 0 || !std::isfinite(minHeight) || minHeight < 0 ||
        direction < -1 || direction > 1) return nullptr;
    uint32_t end = 0;
    for (uint32_t i = 0; i < count; ++i) {
        const auto& span = spans[i];
        if (!span.format || span.textPosition != end || !span.length || span.length > length - end ||
            !std::isfinite(span.r) || !std::isfinite(span.g) || !std::isfinite(span.b) || !std::isfinite(span.a)) return nullptr;
        end += span.length;
    }
    if (end != length) return nullptr;
    end = 0;
    for (uint32_t i = 0; i < unitCount; ++i) {
        const auto& unit = units[i];
        if (unit.textPosition != end || !unit.length || unit.length > length - end ||
            !std::isfinite(unit.spacing) || std::abs(unit.spacing) > 1000000 ||
            !std::isfinite(unit.leading) || std::abs(unit.leading) > 1000000 ||
            !std::isfinite(unit.trailing) || std::abs(unit.trailing) > 1000000 || (unit.flags & ~7u)) return nullptr;
        end += unit.length;
    }
    if (end != length) return nullptr;
    try {
        if (upright) {
            auto provider = dynamic_cast<jalium::TextParagraphUprightProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
            return provider ? reinterpret_cast<JaliumTextParagraph*>(provider->CreateAdvancedUprightParagraph(
                text, length, spans, count, units, unitCount, width, minHeight, direction, contextPaint)) : nullptr;
        }
        if (vertical) {
            auto provider = dynamic_cast<jalium::TextParagraphVerticalAdvanceProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
            return provider ? reinterpret_cast<JaliumTextParagraph*>(provider->CreateAdvancedVerticalParagraph(
                text, length, spans, count, units, unitCount, width, minHeight, direction, contextPaint)) : nullptr;
        }
        auto provider = dynamic_cast<jalium::TextParagraphAdvanceProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? reinterpret_cast<JaliumTextParagraph*>(provider->CreateAdvancedParagraph(
            text, length, spans, count, units, unitCount, width, minHeight, direction, contextPaint)) : nullptr;
    } catch (...) { return nullptr; }
}
JALIUM_API JaliumTextParagraph* jalium_text_paragraph_create_advanced(JaliumTextFormat* format,
    const uint16_t* text, uint32_t length, const JaliumTextSpan* spans, uint32_t count,
    const JaliumTextAdvanceUnit* units, uint32_t unitCount, float width, float minHeight, int32_t direction)
{ return CreateAdvancedParagraph(false, format, text, length, spans, count, units, unitCount, width, minHeight, direction); }
JALIUM_API JaliumTextParagraph* jalium_text_paragraph_create_context_advanced(JaliumTextFormat* format,
    const uint16_t* text, uint32_t length, const JaliumTextSpan* spans, uint32_t count,
    const JaliumTextAdvanceUnit* units, uint32_t unitCount, float width, float minHeight, int32_t direction)
{ return CreateAdvancedParagraph(true, format, text, length, spans, count, units, unitCount, width, minHeight, direction); }
JALIUM_API JaliumTextParagraph* jalium_text_paragraph_create_vertical_advanced(JaliumTextFormat* format,
    const uint16_t* text, uint32_t length, const JaliumTextSpan* spans, uint32_t count,
    const JaliumTextAdvanceUnit* units, uint32_t unitCount, float width, float minHeight,
    int32_t direction, int32_t contextPaint)
{
    if (contextPaint < 0 || contextPaint > 1) return nullptr;
    return CreateAdvancedParagraph(contextPaint != 0, format, text, length, spans, count,
        units, unitCount, width, minHeight, direction, true);
}
JALIUM_API JaliumTextParagraph* jalium_text_paragraph_create_upright_advanced(JaliumTextFormat* format,
    const uint16_t* text, uint32_t length, const JaliumTextSpan* spans, uint32_t count,
    const JaliumTextAdvanceUnit* units, uint32_t unitCount, float width, float minHeight,
    int32_t direction, int32_t contextPaint)
{
    if (contextPaint < 0 || contextPaint > 1) return nullptr;
    return CreateAdvancedParagraph(contextPaint != 0, format, text, length, spans, count,
        units, unitCount, width, minHeight, direction, true, true);
}
JALIUM_API JaliumTextParagraph* jalium_text_paragraph_create_combined(JaliumTextFormat* format,
    const uint16_t* text, uint32_t length, const JaliumTextSpan* spans, uint32_t count,
    const JaliumTextCombineUnit* combined, uint32_t combinedCount,
    const JaliumTextAdvanceUnit* advances, uint32_t advanceCount,
    float width, float minHeight, int32_t alignment, int32_t direction,
    int32_t wrapping, int32_t orientation, int32_t contextPaint)
{
    if (!format || !text || !length || length > jalium::kMaxManagedTextCodeUnits ||
        !spans || !count || count > length || !combined || !combinedCount || combinedCount > length ||
        advanceCount > length || (advanceCount && !advances) ||
        !std::isfinite(width) || width <= 0 || !std::isfinite(minHeight) || minHeight < 0 ||
        alignment < 0 || alignment > 3 || direction < -1 || direction > 1 ||
        wrapping < 0 || wrapping > 2 || orientation < 0 || orientation > 2 ||
        contextPaint < 0 || contextPaint > 1) return nullptr;
    uint32_t end = 0;
    for (uint32_t i = 0; i < count; ++i) {
        const auto& span = spans[i];
        if (!span.format || span.textPosition != end || !span.length || span.length > length - end ||
            !std::isfinite(span.r) || !std::isfinite(span.g) || !std::isfinite(span.b) || !std::isfinite(span.a)) return nullptr;
        end += span.length;
    }
    if (end != length) return nullptr;
    end = 0;
    for (uint32_t i = 0; i < combinedCount; ++i) {
        const auto& unit = combined[i];
        if (unit.textPosition < end || unit.textPosition > length || !unit.length ||
            unit.length > length - unit.textPosition || !std::isfinite(unit.em) || unit.em <= 0 ||
            unit.em > 1000000 || unit.direction < -1 || unit.direction > 1) return nullptr;
        end = unit.textPosition + unit.length;
    }
    end = 0; uint32_t group = 0;
    for (uint32_t i = 0; i < advanceCount; ++i) {
        const auto& unit = advances[i];
        if (unit.textPosition != end || !unit.length || unit.length > length - end ||
            !std::isfinite(unit.spacing) || std::abs(unit.spacing) > 1000000 ||
            !std::isfinite(unit.leading) || std::abs(unit.leading) > 1000000 ||
            !std::isfinite(unit.trailing) || std::abs(unit.trailing) > 1000000 || (unit.flags & ~7u)) return nullptr;
        end += unit.length;
        while (group < combinedCount && combined[group].textPosition + combined[group].length <= end) ++group;
        if (group < combinedCount && combined[group].textPosition < end) return nullptr;
    }
    if (advanceCount && end != length) return nullptr;
    try {
        auto provider = dynamic_cast<jalium::TextParagraphCombineProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? reinterpret_cast<JaliumTextParagraph*>(provider->CreateCombinedParagraph(
            text, length, spans, count, combined, combinedCount, advances, advanceCount,
            width, minHeight, alignment, direction, wrapping, orientation, contextPaint != 0)) : nullptr;
    } catch (...) { return nullptr; }
}
JALIUM_API void jalium_text_paragraph_destroy(JaliumTextParagraph* value) { delete Layout(value); }
JALIUM_API uint32_t jalium_text_paragraph_line_count(JaliumTextParagraph* value)
{ return value ? Layout(value)->LineCount() : 0; }
JALIUM_API JaliumResult jalium_text_paragraph_get_line(JaliumTextParagraph* value,
    uint32_t line, JaliumParagraphLineMetrics* result)
{
    if (result) *result = {};
    if (!value || !result) return JALIUM_ERROR_INVALID_ARGUMENT;
    return Guard([&] { return Layout(value)->GetLine(line, result); });
}
JALIUM_API JaliumResult jalium_text_paragraph_get_fragments(JaliumTextParagraph* value,
    uint32_t line, JaliumTextFragmentMetrics* results, uint32_t capacity, uint32_t* count)
{
    if (count) *count = 0;
    if (!value || !count || (capacity && !results)) return JALIUM_ERROR_INVALID_ARGUMENT;
    return Guard([&] { return Layout(value)->GetFragments(line, results, capacity, count); });
}
JALIUM_API JaliumResult jalium_text_paragraph_get_caret(JaliumTextParagraph* value,
    uint32_t line, uint32_t position, int32_t backward, JaliumParagraphCaret* result)
{
    if (result) *result = {};
    if (!value || !result) return JALIUM_ERROR_INVALID_ARGUMENT;
    return Guard([&] { return Layout(value)->GetCaret(line, position, backward != 0, result); });
}
JALIUM_API JaliumResult jalium_text_paragraph_hit_test(JaliumTextParagraph* value,
    uint32_t line, float x, JaliumParagraphCaret* result)
{
    if (result) *result = {};
    if (!value || !result || !std::isfinite(x)) return JALIUM_ERROR_INVALID_ARGUMENT;
    return Guard([&] { return Layout(value)->HitTest(line, x, result); });
}
JALIUM_API JaliumResult jalium_text_paragraph_get_selection(JaliumTextParagraph* value,
    uint32_t line, uint32_t start, uint32_t length, JaliumTextRangeMetrics* results,
    uint32_t capacity, uint32_t* count)
{
    if (count) *count = 0;
    if (!value || !count || (capacity && !results)) return JALIUM_ERROR_INVALID_ARGUMENT;
    return Guard([&] { return Layout(value)->GetSelection(line, start, length, results, capacity, count); });
}
JALIUM_API JaliumResult jalium_render_target_draw_paragraph_line(JaliumRenderTarget* target,
    JaliumTextParagraph* value, uint32_t line, float x, float y, float opacity)
{
    if (!target || !value || line >= Layout(value)->LineCount() || !std::isfinite(x) ||
        !std::isfinite(y) || !std::isfinite(opacity)) return JALIUM_ERROR_INVALID_ARGUMENT;
    return Guard([&] {
        auto provider = dynamic_cast<jalium::TextParagraphRenderProvider*>(reinterpret_cast<jalium::RenderTarget*>(target));
        if (!provider) return JALIUM_ERROR_NOT_SUPPORTED;
        provider->RenderParagraphLine(Layout(value), line, x, y, opacity);
        return JALIUM_OK;
    });
}
JALIUM_API JaliumResult jalium_text_paragraph_get_position_modes(JaliumTextParagraph* value,
    uint8_t* modes, uint32_t length)
{
    if (!value || (length && !modes)) return JALIUM_ERROR_INVALID_ARGUMENT;
    return Guard([&] {
        auto provider = dynamic_cast<jalium::TextParagraphPositionProvider*>(Layout(value));
        return provider ? provider->GetPositionModes(modes, length) : JALIUM_ERROR_NOT_SUPPORTED;
    });
}

JALIUM_API JaliumResult jalium_text_paragraph_get_caps_operations(JaliumTextParagraph* value,
    uint8_t* operations, uint32_t length)
{
    if (!value || (length && !operations)) return JALIUM_ERROR_INVALID_ARGUMENT;
    return Guard([&] {
        auto provider = dynamic_cast<jalium::TextParagraphCapsOperationsProvider*>(Layout(value));
        return provider ? provider->GetCapsOperations(operations, length) : JALIUM_ERROR_NOT_SUPPORTED;
    });
}

JALIUM_API JaliumResult jalium_text_paragraph_get_source_bounds(JaliumTextParagraph* value,
    uint32_t line, uint32_t start, uint32_t length, JaliumTextRangeMetrics* result)
{
    if (result) *result = {};
    if (!value || !result) return JALIUM_ERROR_INVALID_ARGUMENT;
    return Guard([&] {
        JaliumParagraphLineMetrics metrics{};
        if (Layout(value)->GetLine(line, &metrics) != JALIUM_OK || start < metrics.line.textPosition ||
            start - metrics.line.textPosition > metrics.line.length || length > metrics.line.length - (start - metrics.line.textPosition))
            return JALIUM_ERROR_INVALID_ARGUMENT;
        auto provider = dynamic_cast<jalium::TextParagraphSourceBoundsProvider*>(Layout(value));
        return provider ? provider->GetSourceBounds(line, start, length, result) : JALIUM_ERROR_NOT_SUPPORTED;
    });
}
JALIUM_API JaliumResult jalium_render_target_draw_paragraph_slice(JaliumRenderTarget* target,
    JaliumTextParagraph* value, uint32_t line, float x, float y,
    uint32_t start, uint32_t length, float r, float g, float b, float a)
{
    if (!target || !value || !std::isfinite(x) || !std::isfinite(y) ||
        !std::isfinite(r) || !std::isfinite(g) || !std::isfinite(b) || !std::isfinite(a)) return JALIUM_ERROR_INVALID_ARGUMENT;
    return Guard([&] {
        JaliumParagraphLineMetrics metrics{};
        if (Layout(value)->GetLine(line, &metrics) != JALIUM_OK || start < metrics.line.textPosition ||
            length > metrics.line.length || start - metrics.line.textPosition > metrics.line.length - length)
            return JALIUM_ERROR_INVALID_ARGUMENT;
        auto provider = dynamic_cast<jalium::TextParagraphSliceRenderProvider*>(reinterpret_cast<jalium::RenderTarget*>(target));
        float color[]{r, g, b, a};
        return provider ? provider->RenderParagraphSlice(Layout(value), line, x, y, start, length, color) : JALIUM_ERROR_NOT_SUPPORTED;
    });
}
JALIUM_API JaliumResult jalium_render_target_draw_paragraph_brush(JaliumRenderTarget* target,
    JaliumTextParagraph* value, uint32_t line, float x, float y,
    uint32_t start, uint32_t length, JaliumBrush* brush)
{
    if (!target || !value || !brush || !std::isfinite(x) || !std::isfinite(y)) return JALIUM_ERROR_INVALID_ARGUMENT;
    return Guard([&] {
        JaliumParagraphLineMetrics metrics{};
        if (Layout(value)->GetLine(line, &metrics) != JALIUM_OK || start < metrics.line.textPosition ||
            length > metrics.line.length || start - metrics.line.textPosition > metrics.line.length - length)
            return JALIUM_ERROR_INVALID_ARGUMENT;
        auto provider = dynamic_cast<jalium::TextParagraphBrushRenderProvider*>(reinterpret_cast<jalium::RenderTarget*>(target));
        return provider ? provider->RenderParagraphBrush(Layout(value), line, x, y, start, length,
            reinterpret_cast<jalium::Brush*>(brush)) : JALIUM_ERROR_NOT_SUPPORTED;
    });
}
JALIUM_API JaliumResult jalium_text_paragraph_navigate_word(
    JaliumTextParagraph* const* paragraphs, const uint32_t* offsets, uint32_t count,
    const uint16_t* text, uint32_t length, uint32_t position,
    uint32_t start, uint32_t selectionLength, int32_t direction,
    int32_t backward, JaliumParagraphCaret* result)
{
    if (result) *result = {};
    if (!result || !paragraphs || !offsets || !count || !text || count > static_cast<uint64_t>(length) + 1 ||
        length > jalium::kMaxManagedTextCodeUnits || position > length || start > length ||
        selectionLength > length - start || direction < 0 || direction > 3 ||
        (backward != 0 && backward != 1)) return JALIUM_ERROR_INVALID_ARGUMENT;
    for (uint32_t i = 0; i < count; ++i)
        if (!paragraphs[i] || offsets[i] > length || (i && offsets[i] < offsets[i - 1]))
            return JALIUM_ERROR_INVALID_ARGUMENT;
    return Guard([&] {
        auto provider = dynamic_cast<jalium::TextParagraphNavigationProvider*>(Layout(paragraphs[0]));
        if (!provider) return JALIUM_ERROR_NOT_SUPPORTED;
        return provider->NavigateWord(reinterpret_cast<jalium::TextParagraph* const*>(paragraphs),
            offsets, count, text, length, position, start, selectionLength, direction, backward != 0, result);
    });
}
}
