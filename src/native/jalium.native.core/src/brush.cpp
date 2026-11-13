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

JALIUM_API JaliumResult jalium_brush_set_color_interpolation_mode(JaliumBrush* brush, uint32_t mode) {
    if (!brush || mode > 1) return JALIUM_ERROR_INVALID_ARGUMENT;
    auto provider = dynamic_cast<jalium::GradientColorInterpolationProvider*>(reinterpret_cast<jalium::Brush*>(brush));
    if (!provider) return JALIUM_ERROR_NOT_SUPPORTED;
    try { return provider->SetColorInterpolationMode(mode); }
    catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_RESOURCE_CREATION_FAILED; }
}

JALIUM_API JaliumResult jalium_brush_set_paint_transform(JaliumBrush* brush, const JaliumMatrix* transform) {
    if (!brush || !transform) return JALIUM_ERROR_INVALID_ARGUMENT;
    auto provider = dynamic_cast<jalium::GradientPaintTransformProvider*>(reinterpret_cast<jalium::Brush*>(brush));
    if (!provider) return JALIUM_ERROR_NOT_SUPPORTED;
    try { return provider->SetPaintTransform(*transform); }
    catch (...) { return JALIUM_ERROR_RESOURCE_CREATION_FAILED; }
}

JALIUM_API JaliumResult jalium_brush_set_radial_geometry(JaliumBrush* brush, float radius, uint32_t flags) {
    if (!brush || !std::isfinite(radius) || radius < 0 ||
        (flags != 0 && flags != 1 && flags != 3 && flags != 5) ||
        ((flags == 0 || flags == 3) && radius != 0))
        return JALIUM_ERROR_INVALID_ARGUMENT;
    auto* provider = dynamic_cast<jalium::RadialGradientGeometryProvider*>(reinterpret_cast<jalium::Brush*>(brush));
    if (!provider) return JALIUM_ERROR_NOT_SUPPORTED;
    try { return provider->SetRadialGeometry(radius, flags); }
    catch (...) { return JALIUM_ERROR_RESOURCE_CREATION_FAILED; }
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

JALIUM_API JaliumTextFormat* jalium_text_format_create_with_width(JaliumContext* ctx,
    const wchar_t* fontFamily, float fontSize, int32_t fontWeight, int32_t fontStyle, float widthPercentage)
{
    if (!std::isfinite(widthPercentage) || widthPercentage < 0) return nullptr;
    if (!ctx || !fontFamily || !std::isfinite(fontSize) || fontSize <= jalium::kMinManagedFontSize ||
        fontSize > jalium::kMaxManagedFontSize) return nullptr;
    try {
        uint32_t length = 0;
        if (!jalium::ManagedUtf16LengthBounded(fontFamily, jalium::kMaxManagedFontFamilyCodeUnits, &length)) return nullptr;
        auto factory = dynamic_cast<jalium::TextFormatWidthFactory*>(jalium::GetBackendFromContext(ctx));
        if (!factory) return widthPercentage == 100 ? jalium_text_format_create(ctx, fontFamily, fontSize, fontWeight, fontStyle) : nullptr;
        auto family = jalium::ManagedToWString(fontFamily, length);
        return reinterpret_cast<JaliumTextFormat*>(factory->CreateTextFormatWithWidth(
            family.c_str(), fontSize, fontWeight, fontStyle, widthPercentage));
    } catch (...) { return nullptr; }
}

JALIUM_API void jalium_text_format_destroy(JaliumTextFormat* format) {
    if (format) {
        delete reinterpret_cast<jalium::TextFormat*>(format);
    }
}

JALIUM_API JaliumResult jalium_text_format_set_font_fallbacks(JaliumTextFormat* format,
    JaliumTextFormat* const* fallbackFormats, uint32_t count)
{
    if (!format || count > jalium::kMaxFontFallbackCount || (count && !fallbackFormats))
        return JALIUM_ERROR_INVALID_ARGUMENT;
    for (uint32_t i = 0; i < count; ++i)
        if (!fallbackFormats[i]) return JALIUM_ERROR_INVALID_ARGUMENT;
    try {
        auto provider = dynamic_cast<jalium::TextFontFallbackProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? provider->SetFontFallbacks(
            reinterpret_cast<jalium::TextFormat* const*>(fallbackFormats), count) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_UNKNOWN; }
}

namespace {
bool ValidUnicodeRanges(const JaliumUnicodeRange* ranges, uint32_t count)
{
    if (count > jalium::kMaxFontUnicodeRangeCount || (count && !ranges)) return false;
    for (uint32_t i = 0; i < count; ++i)
        if (ranges[i].first > ranges[i].last || ranges[i].last > 0x10ffffu) return false;
    return true;
}
}

JALIUM_API JaliumResult jalium_text_format_set_font_face_metrics(JaliumTextFormat* format,
    const JaliumFontFaceMetrics* metrics, float* usedSize)
{
    if (!format || !metrics || !usedSize || metrics->structSize != sizeof(JaliumFontFaceMetrics) ||
        !std::isfinite(metrics->sizeAdjust) || metrics->sizeAdjust < 0) return JALIUM_ERROR_INVALID_ARGUMENT;
    const double ratios[] = {metrics->ascent, metrics->descent, metrics->lineGap,
        metrics->verticalAscent, metrics->verticalDescent, metrics->verticalLineGap};
    for (double value : ratios) if (!std::isfinite(value) || (value < 0 && value != -1)) return JALIUM_ERROR_INVALID_ARGUMENT;
    try {
        auto provider = dynamic_cast<jalium::TextFontFaceMetricsProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? provider->SetFontFaceMetrics(metrics, usedSize) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_UNKNOWN; }
}

JALIUM_API JaliumResult jalium_text_format_get_font_ink_bounds(JaliumTextFormat* format,
    const wchar_t* text,uint32_t length,JaliumTextRangeMetrics* bounds)
{
    if(!format||!text||!bounds||length>jalium::kMaxManagedTextCodeUnits)return JALIUM_ERROR_INVALID_ARGUMENT;
    *bounds={};
    try {
        auto provider=dynamic_cast<jalium::TextFontFaceMetricsProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        if(!provider)return JALIUM_ERROR_NOT_SUPPORTED;
#if defined(_WIN32)
        return provider->GetFontInkBounds(text,length,bounds);
#else
        auto converted=jalium::ManagedToWString(text,length);
        auto status=provider->GetFontInkBounds(converted.c_str(),static_cast<uint32_t>(converted.size()),bounds);
        if(status==JALIUM_OK){bounds->textPosition=0;bounds->length=length;}
        return status;
#endif
    } catch(const std::bad_alloc&){return JALIUM_ERROR_OUT_OF_MEMORY;} catch(...){return JALIUM_ERROR_UNKNOWN;}
}

JALIUM_API JaliumResult jalium_text_format_get_font_aspect_metric(JaliumTextFormat* format, uint32_t metric, double* aspect)
{
    if(!format || !aspect || metric<1 || metric>5) return JALIUM_ERROR_INVALID_ARGUMENT;
    *aspect=0;
    try { auto p=dynamic_cast<jalium::TextFontSizeAdjustProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return p?p->GetFontAspectMetric(metric,aspect):JALIUM_ERROR_NOT_SUPPORTED; }
    catch(const std::bad_alloc&){return JALIUM_ERROR_OUT_OF_MEMORY;} catch(...){return JALIUM_ERROR_UNKNOWN;}
}
JALIUM_API JaliumResult jalium_text_format_set_font_size_adjust(JaliumTextFormat* format, uint32_t metric,
    double aspect, uint32_t opticalMode, const JaliumFontVariation* axes, uint32_t count, float* usedSize)
{
    if(!format || !usedSize || metric>5 || opticalMode>2 || !std::isfinite(aspect) || aspect<0 || count>16384 || (count && !axes))return JALIUM_ERROR_INVALID_ARGUMENT;
    for(uint32_t i=0;i<count;++i){if(!std::isfinite(axes[i].value))return JALIUM_ERROR_INVALID_ARGUMENT;
        for(unsigned shift=0;shift<32;shift+=8){auto ch=(axes[i].tag>>shift)&255u;if(ch<32||ch>126)return JALIUM_ERROR_INVALID_ARGUMENT;}}
    try { auto p=dynamic_cast<jalium::TextFontSizeAdjustProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return p?p->SetFontSizeAdjust(metric,aspect,opticalMode,axes,count,usedSize):JALIUM_ERROR_NOT_SUPPORTED; }
    catch(const std::bad_alloc&){return JALIUM_ERROR_OUT_OF_MEMORY;} catch(...){return JALIUM_ERROR_UNKNOWN;}
}

JALIUM_API JaliumResult jalium_text_format_set_font_optical_variations(JaliumTextFormat* format,
    const JaliumFontVariation* axes, uint32_t count, uint32_t mode)
{
    if (!format || mode > 2 || count > 16384 || (count && !axes)) return JALIUM_ERROR_INVALID_ARGUMENT;
    for (uint32_t i = 0; i < count; ++i) {
        if (!std::isfinite(axes[i].value)) return JALIUM_ERROR_INVALID_ARGUMENT;
        for (unsigned shift = 0; shift < 32; shift += 8) {
            auto ch = (axes[i].tag >> shift) & 255u;
            if (ch < 32 || ch > 126) return JALIUM_ERROR_INVALID_ARGUMENT;
        }
    }
    try {
        auto provider = dynamic_cast<jalium::TextFontOpticalVariationProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? provider->SetFontOpticalVariations(axes, count, mode) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_UNKNOWN; }
}

JALIUM_API JaliumResult jalium_text_format_set_font_variations(JaliumTextFormat* format,
    const JaliumFontVariation* axes, uint32_t count)
{
    if (!format || count > 16384 || (count && !axes)) return JALIUM_ERROR_INVALID_ARGUMENT;
    for (uint32_t i = 0; i < count; ++i) {
        if (!std::isfinite(axes[i].value)) return JALIUM_ERROR_INVALID_ARGUMENT;
        for (unsigned shift = 0; shift < 32; shift += 8) {
            auto ch = (axes[i].tag >> shift) & 255u;
            if (ch < 32 || ch > 126) return JALIUM_ERROR_INVALID_ARGUMENT;
        }
    }
    try {
        auto provider = dynamic_cast<jalium::TextFontVariationProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? provider->SetFontVariations(axes, count) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_UNKNOWN; }
}

JALIUM_API JaliumResult jalium_text_format_set_direction(JaliumTextFormat* format, int32_t direction)
{
    if (!format || direction < 0 || direction > 2) return JALIUM_ERROR_INVALID_ARGUMENT;
    try {
        auto provider = dynamic_cast<jalium::TextDirectionProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? provider->SetTextDirection(direction) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_UNKNOWN; }
}

JALIUM_API JaliumResult jalium_text_format_set_font_features(JaliumTextFormat* format,
    const JaliumFontFeature* features, uint32_t count)
{
    if (!format || count > 16384 || (count && !features)) return JALIUM_ERROR_INVALID_ARGUMENT;
    for (uint32_t i = 0; i < count; ++i)
        for (unsigned shift = 0; shift < 32; shift += 8) {
            auto ch = (features[i].tag >> shift) & 255u;
            if (ch < 32 || ch > 126) return JALIUM_ERROR_INVALID_ARGUMENT;
        }
    try {
        auto provider = dynamic_cast<jalium::TextFontFeatureProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? provider->SetFontFeatures(features, count) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_UNKNOWN; }
}

JALIUM_API JaliumResult jalium_text_format_get_font_feature_support(JaliumTextFormat* format,
    uint32_t tables, const uint32_t* tags, uint32_t count, uint8_t* supported)
{
    if (!format || tables < 1 || tables > 3 || count > 16384 || (count && (!tags || !supported))) return JALIUM_ERROR_INVALID_ARGUMENT;
    for (uint32_t i = 0; i < count; ++i)
        for (unsigned shift = 0; shift < 32; shift += 8) {
            auto ch = (tags[i] >> shift) & 255u;
            if (ch < 32 || ch > 126) return JALIUM_ERROR_INVALID_ARGUMENT;
        }
    try {
        auto provider = dynamic_cast<const jalium::TextFontFeatureQueryProvider*>(reinterpret_cast<const jalium::TextFormat*>(format));
        return provider ? provider->GetFontFeatureSupport(tables, tags, count, supported) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_UNKNOWN; }
}

JALIUM_API JaliumResult jalium_text_format_set_unicode_ranges(JaliumTextFormat* format,
    const JaliumUnicodeRange* ranges, uint32_t count, int32_t enabled)
{
    if (!format || (enabled != 0 && enabled != 1) || !ValidUnicodeRanges(ranges, count))
        return JALIUM_ERROR_INVALID_ARGUMENT;
    try {
        auto provider = dynamic_cast<jalium::TextFontCharacterProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? provider->SetUnicodeRanges(ranges, count, enabled != 0) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_UNKNOWN; }
}

JALIUM_API JaliumResult jalium_text_format_set_font_caps(JaliumTextFormat* format,
    uint32_t variant, int32_t flags, const char* language, uint32_t languageLength)
{
    if (!format || variant > 6 || flags < 0 || flags > 7 || ((flags & 4) && !(flags & 2)) ||
        languageLength > 128 || (languageLength && !language)) return JALIUM_ERROR_INVALID_ARGUMENT;
    for (uint32_t i = 0; i < languageLength; ++i) {
        const auto c = static_cast<unsigned char>(language[i]);
        if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
            (c >= '0' && c <= '9') || c == '-')) return JALIUM_ERROR_INVALID_ARGUMENT;
    }
    try {
        auto provider = dynamic_cast<jalium::TextFontCapsProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? provider->SetFontCaps(variant, static_cast<uint32_t>(flags), language, languageLength) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_UNKNOWN; }
}

JALIUM_API JaliumResult jalium_text_format_get_character_coverage(JaliumTextFormat* format,
    const uint32_t* characters, uint32_t count, uint8_t* supported)
{
    if (!format || count > jalium::kMaxFontCharacterCount || (count && (!characters || !supported)))
        return JALIUM_ERROR_INVALID_ARGUMENT;
    for (uint32_t i = 0; i < count; ++i)
        if (characters[i] > 0x10ffffu || (characters[i] >= 0xd800u && characters[i] <= 0xdfffu))
            return JALIUM_ERROR_INVALID_ARGUMENT;
    try {
        auto provider = dynamic_cast<jalium::TextFontCharacterProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? provider->GetCharacterCoverage(characters, count, supported) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_UNKNOWN; }
}

JALIUM_API JaliumResult jalium_text_format_set_orientation(JaliumTextFormat* format, int32_t orientation, int32_t baseline)
{
    if (!format || orientation < 0 || orientation > 2 || baseline < 0 || baseline > 1) return JALIUM_ERROR_INVALID_ARGUMENT;
    try {
        auto provider = dynamic_cast<jalium::TextOrientationProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? provider->SetTextOrientation(orientation, baseline) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_UNKNOWN; }
}

JALIUM_API JaliumResult jalium_text_format_set_font_emoji(JaliumTextFormat* format,
    uint32_t variant, uint32_t after)
{
    if (!format || variant > 3 || after > 2) return JALIUM_ERROR_INVALID_ARGUMENT;
    try {
        auto provider = dynamic_cast<jalium::TextFontEmojiProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? provider->SetFontEmoji(variant, after) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_UNKNOWN; }
}

JALIUM_API JaliumResult jalium_text_format_set_font_position(JaliumTextFormat* format,
    uint32_t variant, uint32_t flags, uint32_t runMode)
{
    if (!format || variant > 2 || flags > 7 || ((flags & 4) && !(flags & 2)) || runMode > 3 || (!variant && runMode))
        return JALIUM_ERROR_INVALID_ARGUMENT;
    try {
        auto provider = dynamic_cast<jalium::TextFontPositionProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? provider->SetFontPosition(variant, flags, runMode) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_UNKNOWN; }
}

JALIUM_API JaliumResult jalium_text_format_set_font_caps_continuation(JaliumTextFormat* format,
    uint32_t operation)
{
    if (!format || operation > 2) return JALIUM_ERROR_INVALID_ARGUMENT;
    try {
        auto provider = dynamic_cast<jalium::TextFontCapsContinuationProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? provider->SetFontCapsContinuation(operation) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_UNKNOWN; }
}

JALIUM_API JaliumResult jalium_text_format_set_font_caps_context(JaliumTextFormat* format,
    uint32_t boundaries)
{
    if (!format || (boundaries & ~63u)) return JALIUM_ERROR_INVALID_ARGUMENT;
    try {
        auto provider = dynamic_cast<jalium::TextFontCapsContextProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? provider->SetFontCapsContext(boundaries) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_UNKNOWN; }
}

JALIUM_API JaliumResult jalium_text_format_set_font_display(JaliumTextFormat* format,
    const JaliumFontDisplayEntry* entries, uint32_t count)
{
    if (!format || count > jalium::kMaxFontFallbackCount || (count && !entries))
        return JALIUM_ERROR_INVALID_ARGUMENT;
    uint64_t total = 0;
    for (uint32_t i = 0; i < count; ++i) {
        const auto& entry = entries[i];
        total += entry.rangeCount;
        if (!ValidUnicodeRanges(entry.ranges, entry.rangeCount) || total > jalium::kMaxFontUnicodeRangeCount ||
            entry.flags > 3 || entry.flags == 2 || ((entry.flags & 1) != 0) == (entry.format != nullptr))
            return JALIUM_ERROR_INVALID_ARGUMENT;
    }
    try {
        auto provider = dynamic_cast<jalium::TextFontCharacterProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? provider->SetFontDisplay(entries, count) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (const std::bad_alloc&) { return JALIUM_ERROR_OUT_OF_MEMORY; }
    catch (...) { return JALIUM_ERROR_UNKNOWN; }
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

JALIUM_API JaliumResult jalium_text_format_hit_test_text_range(
    JaliumTextFormat* format, const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight, uint32_t textPosition, uint32_t length,
    JaliumTextRangeMetrics* result)
{
    if (result) *result = {};
    if (!format || !text || !result || textLength > jalium::kMaxManagedTextCodeUnits ||
        textPosition > textLength || length > textLength - textPosition ||
        !std::isfinite(maxWidth) || !std::isfinite(maxHeight))
        return JALIUM_ERROR_INVALID_ARGUMENT;
    try {
        auto provider = dynamic_cast<jalium::TextRangeMetricsProvider*>(
            reinterpret_cast<jalium::TextFormat*>(format));
        if (!provider) return JALIUM_ERROR_NOT_SUPPORTED;
#if defined(_WIN32)
        return provider->HitTestTextRange(text, textLength, maxWidth, maxHeight,
            textPosition, length, result);
#else
        auto wide = jalium::ManagedToWString(text, textLength);
        uint32_t start = jalium::ManagedUtf16IndexToWStringIndex(text, textLength, textPosition);
        uint32_t end = jalium::ManagedUtf16IndexToWStringIndex(text, textLength, textPosition + length);
        if (length && jalium::ManagedWStringIndexToUtf16Index(text, textLength, end) < textPosition + length)
            ++end;
        auto status = provider->HitTestTextRange(wide.c_str(), static_cast<uint32_t>(wide.size()),
            maxWidth, maxHeight, start, length ? end - start : 0, result);
        if (status == JALIUM_OK) {
            uint32_t resultEnd = jalium::ManagedWStringIndexToUtf16Index(text, textLength,
                result->textPosition + result->length);
            result->textPosition = jalium::ManagedWStringIndexToUtf16Index(text, textLength, result->textPosition);
            result->length = resultEnd - result->textPosition;
        }
        return status;
#endif
    } catch (const std::bad_alloc&) {
        *result = {};
        return JALIUM_ERROR_OUT_OF_MEMORY;
    } catch (...) {
        *result = {};
        return JALIUM_ERROR_UNKNOWN;
    }
}

JALIUM_API JaliumResult jalium_text_format_get_line_metrics(
    JaliumTextFormat* format, const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight, uint32_t textPosition, int32_t backwardAffinity,
    JaliumTextLineMetrics* result)
{
    if (result) *result = {};
    if (!format || !text || !result || textLength > jalium::kMaxManagedTextCodeUnits ||
        textPosition > textLength || !std::isfinite(maxWidth) || !std::isfinite(maxHeight))
        return JALIUM_ERROR_INVALID_ARGUMENT;
    try {
        auto provider = dynamic_cast<jalium::TextLineMetricsProvider*>(
            reinterpret_cast<jalium::TextFormat*>(format));
        if (!provider) return JALIUM_ERROR_NOT_SUPPORTED;
#if defined(_WIN32)
        return provider->GetLineMetrics(text, textLength, maxWidth, maxHeight,
            textPosition, backwardAffinity, result);
#else
        auto wide = jalium::ManagedToWString(text, textLength);
        auto status = provider->GetLineMetrics(wide.c_str(), static_cast<uint32_t>(wide.size()),
            maxWidth, maxHeight, jalium::ManagedUtf16IndexToWStringIndex(text, textLength, textPosition),
            backwardAffinity, result);
        if (status == JALIUM_OK) {
            uint32_t end = jalium::ManagedWStringIndexToUtf16Index(text, textLength,
                result->textPosition + result->length);
            result->textPosition = jalium::ManagedWStringIndexToUtf16Index(text, textLength, result->textPosition);
            result->length = end - result->textPosition;
            result->leftCaretPosition = jalium::ManagedWStringIndexToUtf16Index(text, textLength, result->leftCaretPosition);
            result->rightCaretPosition = jalium::ManagedWStringIndexToUtf16Index(text, textLength, result->rightCaretPosition);
        }
        return status;
#endif
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

JALIUM_API JaliumResult jalium_text_format_get_font_math_constants(
    JaliumTextFormat* format, JaliumFontMathConstants* constants)
{
    if (!format || !constants || constants->structSize < sizeof(JaliumFontMathConstants))
        return JALIUM_ERROR_INVALID_ARGUMENT;
    try {
        auto* provider = dynamic_cast<jalium::FontMathConstantsProvider*>(reinterpret_cast<jalium::TextFormat*>(format));
        return provider ? provider->GetFontMathConstants(constants) : JALIUM_ERROR_NOT_SUPPORTED;
    } catch (...) {
        *constants = {};
        constants->structSize = sizeof(JaliumFontMathConstants);
        return JALIUM_ERROR_UNKNOWN;
    }
}

} // extern "C"
