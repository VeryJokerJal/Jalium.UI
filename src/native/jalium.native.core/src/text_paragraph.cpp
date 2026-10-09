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
        !std::isfinite(minHeight) || minHeight <= 0 || alignment < 0 || alignment > 3 ||
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
JALIUM_API JaliumTextParagraph* jalium_text_paragraph_create_with_wrapping(JaliumTextFormat* format,
    const uint16_t* text, uint32_t length, const JaliumTextSpan* spans, uint32_t count,
    float width, float minHeight, int32_t alignment, int32_t direction, int32_t wrapping)
{
    if (!format || !text || length > jalium::kMaxManagedTextCodeUnits ||
        count > length || (count && !spans) || !std::isfinite(width) || width <= 0 ||
        !std::isfinite(minHeight) || minHeight <= 0 || alignment < 0 || alignment > 3 ||
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
        auto provider = dynamic_cast<jalium::TextParagraphWrappingProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? reinterpret_cast<JaliumTextParagraph*>(provider->CreateParagraphWithWrapping(
            text, length, spans, count, width, minHeight, alignment, direction, wrapping)) : nullptr;
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
