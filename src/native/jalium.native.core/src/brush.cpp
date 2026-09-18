#include "jalium_internal.h"
#include "jalium_abi_guard.h"
#include "jalium_string_util.h"

#include <cmath>
#include <new>

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
    uint32_t stopCount,
    uint32_t extendMode)
{
    if (!ctx || !stops || stopCount == 0 ||
        stopCount > jalium::kMaxGradientStopCount) {
        return nullptr;
    }
    try {
        auto backend = jalium::GetBackendFromContext(ctx);
        if (!backend) return nullptr;
        auto brush = backend->CreateLinearGradientBrush(
            startX, startY, endX, endY, stops, stopCount,
            extendMode <= 2 ? extendMode : 0);
        return reinterpret_cast<JaliumBrush*>(brush);
    } catch (...) {
        return nullptr;
    }
}

JALIUM_API JaliumBrush* jalium_brush_create_radial_gradient(
    JaliumContext* ctx,
    float centerX, float centerY, float radiusX, float radiusY,
    float originX, float originY,
    const JaliumGradientStop* stops,
    uint32_t stopCount,
    uint32_t extendMode)
{
    if (!ctx || !stops || stopCount == 0 ||
        stopCount > jalium::kMaxGradientStopCount) {
        return nullptr;
    }
    try {
        auto backend = jalium::GetBackendFromContext(ctx);
        if (!backend) return nullptr;
        auto brush = backend->CreateRadialGradientBrush(
            centerX, centerY, radiusX, radiusY, originX, originY,
            stops, stopCount,
            extendMode <= 2 ? extendMode : 0);
        return reinterpret_cast<JaliumBrush*>(brush);
    } catch (...) {
        return nullptr;
    }
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
    if (!ctx || !fontFamily || !std::isfinite(fontSize) ||
        fontSize <= jalium::kMinManagedFontSize ||
        fontSize > jalium::kMaxManagedFontSize) {
        return nullptr;
    }

    try {
        uint32_t familyLength = 0;
        if (!jalium::ManagedUtf16LengthBounded(
                fontFamily,
                jalium::kMaxManagedFontFamilyCodeUnits,
                &familyLength)) {
            return nullptr;
        }
        auto backend = jalium::GetBackendFromContext(ctx);
        if (!backend) return nullptr;
        auto wFamily = jalium::ManagedToWString(fontFamily, familyLength);
        auto format = backend->CreateTextFormat(
            wFamily.c_str(), fontSize, fontWeight, fontStyle);
        return reinterpret_cast<JaliumTextFormat*>(format);
    } catch (...) {
        // C++ exceptions must never unwind through the public C ABI.
        return nullptr;
    }
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

JALIUM_API void jalium_text_format_set_text_rendering_mode(JaliumTextFormat* format, int32_t mode) {
    if (!format) return;
    // Clamp invalid values to Auto so a corrupt managed-side cast can't make
    // ResolveEffectiveTextRenderingMode return a garbage AA mode that backends
    // would index off the end of their mode->rasterizer table.
    if (mode < 0 || mode > 3) mode = 0;
    reinterpret_cast<jalium::TextFormat*>(format)->SetTextRenderingMode(mode);
}

JALIUM_API void jalium_text_format_set_text_formatting_mode(JaliumTextFormat* format, int32_t mode) {
    if (!format) return;
    if (mode < 0 || mode > 1) mode = 0;  // 0=Ideal (WPF default), 1=Display
    reinterpret_cast<jalium::TextFormat*>(format)->SetTextFormattingMode(mode);
}

JALIUM_API void jalium_text_format_set_text_hinting_mode(JaliumTextFormat* format, int32_t mode) {
    if (!format) return;
    if (mode < 0 || mode > 2) mode = 0;  // 0=Auto, 1=Fixed, 2=Animated
    reinterpret_cast<jalium::TextFormat*>(format)->SetTextHintingMode(mode);
}

JALIUM_API void jalium_text_format_set_subpixel_positioning(JaliumTextFormat* format, int32_t enabled) {
    if (!format) return;
    reinterpret_cast<jalium::TextFormat*>(format)->SetSubpixelPositioning(enabled != 0);
}

JALIUM_API JaliumResult jalium_text_format_hit_test_point(
    JaliumTextFormat* format,
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    float pointX, float pointY,
    JaliumTextHitTestResult* result)
{
    if (!format || !text || !result ||
        textLength > jalium::kMaxManagedTextCodeUnits) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }
    *result = {};
    try {
#if defined(_WIN32)
        JaliumResult status =
            reinterpret_cast<jalium::TextFormat*>(format)->HitTestPoint(
                text, textLength, maxWidth, maxHeight,
                pointX, pointY, result);
#else
        auto wstr = jalium::ManagedToWString(text, textLength);
        JaliumResult status =
            reinterpret_cast<jalium::TextFormat*>(format)->HitTestPoint(
                wstr.c_str(), static_cast<uint32_t>(wstr.size()),
                maxWidth, maxHeight, pointX, pointY, result);
        if (status == JALIUM_OK) {
            result->textPosition = jalium::ManagedWStringIndexToUtf16Index(
                text, textLength, result->textPosition);
        }
#endif
        return status;
    } catch (const std::bad_alloc&) {
        *result = {};
        return JALIUM_ERROR_OUT_OF_MEMORY;
    } catch (...) {
        *result = {};
        return JALIUM_ERROR_UNKNOWN;
    }
}

JALIUM_API JaliumResult jalium_text_format_hit_test_text_position(
    JaliumTextFormat* format,
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    uint32_t textPosition, int32_t isTrailingHit,
    JaliumTextHitTestResult* result)
{
    if (!format || !text || !result ||
        textLength > jalium::kMaxManagedTextCodeUnits ||
        textPosition > textLength) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }
    *result = {};
    try {
#if defined(_WIN32)
        JaliumResult status =
            reinterpret_cast<jalium::TextFormat*>(format)->
                HitTestTextPosition(
                    text, textLength, maxWidth, maxHeight, textPosition,
                    isTrailingHit, result);
#else
        auto wstr = jalium::ManagedToWString(text, textLength);
        uint32_t wideTextPosition = jalium::ManagedUtf16IndexToWStringIndex(
            text, textLength, textPosition);
        JaliumResult status =
            reinterpret_cast<jalium::TextFormat*>(format)->HitTestTextPosition(
                wstr.c_str(), static_cast<uint32_t>(wstr.size()),
                maxWidth, maxHeight, wideTextPosition, isTrailingHit, result);
        if (status == JALIUM_OK) {
            result->textPosition = jalium::ManagedWStringIndexToUtf16Index(
                text, textLength, result->textPosition);
        }
#endif
        return status;
    } catch (const std::bad_alloc&) {
        *result = {};
        return JALIUM_ERROR_OUT_OF_MEMORY;
    } catch (...) {
        *result = {};
        return JALIUM_ERROR_UNKNOWN;
    }
}

JALIUM_API JaliumResult jalium_text_format_measure_text(
    JaliumTextFormat* format,
    const wchar_t* text,
    uint32_t textLength,
    float maxWidth,
    float maxHeight,
    JaliumTextMetrics* metrics)
{
    if (!format || !text || !metrics ||
        textLength > jalium::kMaxManagedTextCodeUnits) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    *metrics = {};
    try {
#if defined(_WIN32)
        return reinterpret_cast<jalium::TextFormat*>(format)->MeasureText(
            text, textLength, maxWidth, maxHeight, metrics);
#else
        auto wstr = jalium::ManagedToWString(text, textLength);
        return reinterpret_cast<jalium::TextFormat*>(format)->MeasureText(
            wstr.c_str(), static_cast<uint32_t>(wstr.size()),
            maxWidth, maxHeight, metrics);
#endif
    } catch (const std::bad_alloc&) {
        *metrics = {};
        return JALIUM_ERROR_OUT_OF_MEMORY;
    } catch (...) {
        *metrics = {};
        return JALIUM_ERROR_UNKNOWN;
    }
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

JALIUM_API JaliumResult jalium_text_format_get_font_unit_metrics(
    JaliumTextFormat* format, JaliumFontUnitMetrics* metrics)
{
    if (!format || !metrics || metrics->structSize < sizeof(JaliumFontUnitMetrics))
        return JALIUM_ERROR_INVALID_ARGUMENT;
    try {
        auto* provider = dynamic_cast<jalium::FontUnitMetricsProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? provider->GetFontUnitMetrics(metrics) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (...) {
        *metrics = {};
        metrics->structSize = sizeof(JaliumFontUnitMetrics);
        return JALIUM_ERROR_UNKNOWN;
    }
}

} // extern "C"
