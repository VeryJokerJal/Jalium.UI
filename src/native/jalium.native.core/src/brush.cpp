#include "jalium_internal.h"

// ============================================================================
// C API Implementation
// ============================================================================

extern "C" {

JALIUM_API JaliumBrush* jalium_brush_create_solid(JaliumContext* ctx, float r, float g, float b, float a) {
    if (!ctx) return nullptr;

    auto backend = jalium::GetBackendFromContext(ctx);
    if (!backend) return nullptr;

    auto brush = backend->CreateSolidBrush(r, g, b, a);
    return reinterpret_cast<JaliumBrush*>(brush);
}

JALIUM_API JaliumBrush* jalium_brush_create_linear_gradient(
    JaliumContext* ctx,
    float startX, float startY, float endX, float endY,
    const JaliumGradientStop* stops,
    uint32_t stopCount)
{
    if (!ctx || !stops || stopCount == 0) return nullptr;

    auto backend = jalium::GetBackendFromContext(ctx);
    if (!backend) return nullptr;

    auto brush = backend->CreateLinearGradientBrush(startX, startY, endX, endY, stops, stopCount);
    return reinterpret_cast<JaliumBrush*>(brush);
}

JALIUM_API JaliumBrush* jalium_brush_create_radial_gradient(
    JaliumContext* ctx,
    float centerX, float centerY, float radiusX, float radiusY,
    float originX, float originY,
    const JaliumGradientStop* stops,
    uint32_t stopCount)
{
    if (!ctx || !stops || stopCount == 0) return nullptr;

    auto backend = jalium::GetBackendFromContext(ctx);
    if (!backend) return nullptr;

    auto brush = backend->CreateRadialGradientBrush(centerX, centerY, radiusX, radiusY, originX, originY, stops, stopCount);
    return reinterpret_cast<JaliumBrush*>(brush);
}

JALIUM_API void jalium_brush_destroy(JaliumBrush* brush) {
    if (brush) {
        delete reinterpret_cast<jalium::Brush*>(brush);
    }
}

JALIUM_API JaliumTextFormat* jalium_text_format_create(
    JaliumContext* ctx,
    const wchar_t* fontFamily,
    float fontSize,
    int32_t fontWeight,
    int32_t fontStyle)
{
    if (!ctx || !fontFamily || fontSize <= 0) return nullptr;

    auto backend = jalium::GetBackendFromContext(ctx);
    if (!backend) return nullptr;

    auto format = backend->CreateTextFormat(fontFamily, fontSize, fontWeight, fontStyle);
    return reinterpret_cast<JaliumTextFormat*>(format);
}

JALIUM_API void jalium_text_format_destroy(JaliumTextFormat* format) {
    if (format) {
        delete reinterpret_cast<jalium::TextFormat*>(format);
    }
}

JALIUM_API void jalium_text_format_set_alignment(JaliumTextFormat* format, int32_t alignment) {
    if (format) {
        reinterpret_cast<jalium::TextFormat*>(format)->SetAlignment(alignment);
    }
}

JALIUM_API void jalium_text_format_set_paragraph_alignment(JaliumTextFormat* format, int32_t alignment) {
    if (format) {
        reinterpret_cast<jalium::TextFormat*>(format)->SetParagraphAlignment(alignment);
    }
}

JALIUM_API void jalium_text_format_set_trimming(JaliumTextFormat* format, int32_t trimming) {
    if (format) {
        reinterpret_cast<jalium::TextFormat*>(format)->SetTrimming(trimming);
    }
}

JALIUM_API void jalium_text_format_set_word_wrapping(JaliumTextFormat* format, int32_t wrapping) {
    if (format) {
        reinterpret_cast<jalium::TextFormat*>(format)->SetWordWrapping(wrapping);
    }
}

JALIUM_API void jalium_text_format_set_line_spacing(JaliumTextFormat* format, int32_t method, float spacing, float baseline) {
    if (format) {
        reinterpret_cast<jalium::TextFormat*>(format)->SetLineSpacing(method, spacing, baseline);
    }
}

JALIUM_API void jalium_text_format_set_max_lines(JaliumTextFormat* format, uint32_t maxLines) {
    if (format) {
        reinterpret_cast<jalium::TextFormat*>(format)->SetMaxLines(maxLines);
    }
}

JALIUM_API JaliumResult jalium_text_format_hit_test_point(
    JaliumTextFormat* format,
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    float pointX, float pointY,
    JaliumTextHitTestResult* result)
{
    if (!format || !text || !result) return JALIUM_ERROR_INVALID_ARGUMENT;
    return reinterpret_cast<jalium::TextFormat*>(format)->HitTestPoint(
        text, textLength, maxWidth, maxHeight, pointX, pointY, result);
}

JALIUM_API JaliumResult jalium_text_format_hit_test_text_position(
    JaliumTextFormat* format,
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    uint32_t textPosition, int32_t isTrailingHit,
    JaliumTextHitTestResult* result)
{
    if (!format || !text || !result) return JALIUM_ERROR_INVALID_ARGUMENT;
    return reinterpret_cast<jalium::TextFormat*>(format)->HitTestTextPosition(
        text, textLength, maxWidth, maxHeight, textPosition, isTrailingHit, result);
}

JALIUM_API JaliumResult jalium_text_format_measure_text(
    JaliumTextFormat* format,
    const wchar_t* text,
    uint32_t textLength,
    float maxWidth,
    float maxHeight,
    JaliumTextMetrics* metrics)
{
    if (!format || !text || !metrics) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    return reinterpret_cast<jalium::TextFormat*>(format)->MeasureText(
        text, textLength, maxWidth, maxHeight, metrics);
}

JALIUM_API JaliumResult jalium_text_format_get_font_metrics(
    JaliumTextFormat* format,
    JaliumTextMetrics* metrics)
{
    if (!format || !metrics) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    return reinterpret_cast<jalium::TextFormat*>(format)->GetFontMetrics(metrics);
}

} // extern "C"
