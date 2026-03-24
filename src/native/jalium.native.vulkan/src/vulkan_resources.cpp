#include "vulkan_resources.h"

#include <algorithm>
#include <cstring>

namespace jalium {

VulkanSolidBrush::VulkanSolidBrush(float r, float g, float b, float a)
    : r_(r), g_(g), b_(b), a_(a)
{
}

VulkanLinearGradientBrush::VulkanLinearGradientBrush(
    float startX, float startY, float endX, float endY,
    const JaliumGradientStop* stops, uint32_t stopCount)
    : startX_(startX), startY_(startY), endX_(endX), endY_(endY)
{
    if (stops && stopCount > 0) {
        stops_.assign(stops, stops + stopCount);
    }
}

VulkanRadialGradientBrush::VulkanRadialGradientBrush(
    float centerX, float centerY, float radiusX, float radiusY,
    float originX, float originY,
    const JaliumGradientStop* stops, uint32_t stopCount)
    : centerX_(centerX)
    , centerY_(centerY)
    , radiusX_(radiusX)
    , radiusY_(radiusY)
    , originX_(originX)
    , originY_(originY)
{
    if (stops && stopCount > 0) {
        stops_.assign(stops, stops + stopCount);
    }
}

VulkanBitmap::VulkanBitmap(uint32_t width, uint32_t height, std::vector<uint8_t> pixelData)
    : width_(width > 0 ? width : 1)
    , height_(height > 0 ? height : 1)
    , pixelData_(std::move(pixelData))
{
    // Ensure pixel data matches expected size (4 bytes per RGBA pixel)
    size_t expected = static_cast<size_t>(width_) * height_ * 4;
    if (pixelData_.size() < expected) {
        pixelData_.resize(expected, 0);
    }
}

#ifdef _WIN32
VulkanTextFormat::VulkanTextFormat(
    IDWriteFactory* factory,
    const wchar_t* fontFamily,
    float fontSize,
    int32_t fontWeight,
    int32_t fontStyle)
    : fontFamily_(fontFamily ? fontFamily : L"Segoe UI")
    , fontSize_(fontSize)
{
    if (!factory) {
        return;
    }

    factory_ = factory;
    factory_->CreateTextFormat(
        fontFamily_.c_str(),
        nullptr,
        static_cast<DWRITE_FONT_WEIGHT>(fontWeight),
        static_cast<DWRITE_FONT_STYLE>(fontStyle),
        DWRITE_FONT_STRETCH_NORMAL,
        fontSize,
        L"",
        &format_);
}
#else
VulkanTextFormat::VulkanTextFormat(
    const wchar_t* fontFamily,
    float fontSize,
    int32_t fontWeight,
    int32_t fontStyle)
    : fontFamily_(fontFamily ? fontFamily : L"Sans")
    , fontSize_(fontSize)
{
    (void)fontWeight;
    (void)fontStyle;
}
#endif

void VulkanTextFormat::SetAlignment(int32_t alignment)
{
    alignment_ = alignment;
#ifdef _WIN32
    if (!format_) {
        return;
    }

    DWRITE_TEXT_ALIGNMENT textAlignment = DWRITE_TEXT_ALIGNMENT_LEADING;
    switch (alignment) {
        case JALIUM_TEXT_ALIGN_TRAILING:
            textAlignment = DWRITE_TEXT_ALIGNMENT_TRAILING;
            break;
        case JALIUM_TEXT_ALIGN_CENTER:
            textAlignment = DWRITE_TEXT_ALIGNMENT_CENTER;
            break;
        case JALIUM_TEXT_ALIGN_JUSTIFIED:
            textAlignment = DWRITE_TEXT_ALIGNMENT_JUSTIFIED;
            break;
    }

    format_->SetTextAlignment(textAlignment);
#endif
}

void VulkanTextFormat::SetParagraphAlignment(int32_t alignment)
{
    paragraphAlignment_ = alignment;
#ifdef _WIN32
    if (!format_) {
        return;
    }

    DWRITE_PARAGRAPH_ALIGNMENT paragraphAlignment = DWRITE_PARAGRAPH_ALIGNMENT_NEAR;
    switch (alignment) {
        case JALIUM_PARAGRAPH_ALIGN_FAR:
            paragraphAlignment = DWRITE_PARAGRAPH_ALIGNMENT_FAR;
            break;
        case JALIUM_PARAGRAPH_ALIGN_CENTER:
            paragraphAlignment = DWRITE_PARAGRAPH_ALIGNMENT_CENTER;
            break;
    }

    format_->SetParagraphAlignment(paragraphAlignment);
#endif
}

void VulkanTextFormat::SetTrimming(int32_t trimming)
{
    trimming_ = trimming;
#ifdef _WIN32
    if (!format_ || !factory_) {
        return;
    }

    DWRITE_TRIMMING trimmingOptions = {};
    ComPtr<IDWriteInlineObject> ellipsis;

    switch (trimming) {
        case JALIUM_TEXT_TRIMMING_CHARACTER_ELLIPSIS:
            trimmingOptions.granularity = DWRITE_TRIMMING_GRANULARITY_CHARACTER;
            factory_->CreateEllipsisTrimmingSign(format_.Get(), &ellipsis);
            break;
        case JALIUM_TEXT_TRIMMING_WORD_ELLIPSIS:
            trimmingOptions.granularity = DWRITE_TRIMMING_GRANULARITY_WORD;
            factory_->CreateEllipsisTrimmingSign(format_.Get(), &ellipsis);
            break;
        default:
            trimmingOptions.granularity = DWRITE_TRIMMING_GRANULARITY_NONE;
            break;
    }

    format_->SetTrimming(&trimmingOptions, ellipsis.Get());
#endif
}

void VulkanTextFormat::SetWordWrapping(int32_t wrapping)
{
#ifdef _WIN32
    if (format_) {
        DWRITE_WORD_WRAPPING dw;
        switch (wrapping) {
            case 1: dw = DWRITE_WORD_WRAPPING_NO_WRAP; break;
            case 2: dw = DWRITE_WORD_WRAPPING_CHARACTER; break;
            case 3: dw = DWRITE_WORD_WRAPPING_EMERGENCY_BREAK; break;
            default: dw = DWRITE_WORD_WRAPPING_WRAP; break;
        }
        format_->SetWordWrapping(dw);
    }
#endif
}

void VulkanTextFormat::SetLineSpacing(int32_t method, float spacing, float baseline)
{
#ifdef _WIN32
    if (format_) {
        DWRITE_LINE_SPACING_METHOD dwMethod;
        switch (method) {
            case 1: dwMethod = DWRITE_LINE_SPACING_METHOD_UNIFORM; break;
            case 2: dwMethod = DWRITE_LINE_SPACING_METHOD_PROPORTIONAL; break;
            default: dwMethod = DWRITE_LINE_SPACING_METHOD_DEFAULT; break;
        }
        format_->SetLineSpacing(dwMethod, spacing, baseline);
    }
#endif
}

void VulkanTextFormat::SetMaxLines(uint32_t) {}

JaliumResult VulkanTextFormat::HitTestPoint(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    float pointX, float pointY,
    JaliumTextHitTestResult* result)
{
    if (!result) return JALIUM_ERROR_INVALID_ARGUMENT;
    memset(result, 0, sizeof(*result));
#ifdef _WIN32
    if (!format_ || !factory_ || !text) return JALIUM_ERROR_INVALID_ARGUMENT;
    ComPtr<IDWriteTextLayout> layout;
    if (FAILED(factory_->CreateTextLayout(text, textLength, format_.Get(), maxWidth, maxHeight, &layout)))
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    BOOL trailing = FALSE, inside = FALSE;
    DWRITE_HIT_TEST_METRICS m = {};
    layout->HitTestPoint(pointX, pointY, &trailing, &inside, &m);
    result->textPosition = m.textPosition;
    result->isTrailingHit = trailing;
    result->isInside = inside;
    float cx, cy; DWRITE_HIT_TEST_METRICS cm = {};
    layout->HitTestTextPosition(m.textPosition, trailing, &cx, &cy, &cm);
    result->caretX = cx; result->caretY = cy; result->caretHeight = cm.height;
#endif
    return JALIUM_OK;
}

JaliumResult VulkanTextFormat::HitTestTextPosition(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    uint32_t textPosition, int32_t isTrailingHit,
    JaliumTextHitTestResult* result)
{
    if (!result) return JALIUM_ERROR_INVALID_ARGUMENT;
    memset(result, 0, sizeof(*result));
#ifdef _WIN32
    if (!format_ || !factory_ || !text) return JALIUM_ERROR_INVALID_ARGUMENT;
    ComPtr<IDWriteTextLayout> layout;
    if (FAILED(factory_->CreateTextLayout(text, textLength, format_.Get(), maxWidth, maxHeight, &layout)))
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    float cx, cy; DWRITE_HIT_TEST_METRICS m = {};
    layout->HitTestTextPosition(textPosition, isTrailingHit ? TRUE : FALSE, &cx, &cy, &m);
    result->textPosition = textPosition;
    result->isTrailingHit = isTrailingHit;
    result->isInside = 1;
    result->caretX = cx; result->caretY = cy; result->caretHeight = m.height;
#endif
    return JALIUM_OK;
}

JaliumResult VulkanTextFormat::MeasureText(
    const wchar_t* text,
    uint32_t textLength,
    float maxWidth,
    float maxHeight,
    JaliumTextMetrics* metrics)
{
    if (!text || !metrics) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    std::memset(metrics, 0, sizeof(JaliumTextMetrics));

#ifdef _WIN32
    if (factory_ && format_) {
        ComPtr<IDWriteTextLayout> layout;
        HRESULT hr = factory_->CreateTextLayout(text, textLength, format_.Get(), maxWidth, maxHeight, &layout);
        if (SUCCEEDED(hr) && layout) {
            DWRITE_TEXT_METRICS textMetrics{};
            if (SUCCEEDED(layout->GetMetrics(&textMetrics))) {
                metrics->width = textMetrics.widthIncludingTrailingWhitespace;
                metrics->height = textMetrics.height;
                metrics->lineCount = textMetrics.lineCount;
            }
        }
    }
#endif

    if (metrics->width == 0.0f && textLength > 0) {
        const float approxCharWidth = fontSize_ * 0.55f;
        metrics->width = std::min(maxWidth, approxCharWidth * static_cast<float>(textLength));
        metrics->height = std::min(maxHeight, fontSize_ * 1.2f);
        metrics->lineCount = 1;
    }

    metrics->ascent = fontSize_ * 0.8f;
    metrics->descent = fontSize_ * 0.2f;
    metrics->lineGap = fontSize_ * 0.2f;
    metrics->lineHeight = metrics->ascent + metrics->descent + metrics->lineGap;
    metrics->baseline = metrics->ascent;
    metrics->height = std::max(metrics->height, metrics->lineHeight);
    return JALIUM_OK;
}

JaliumResult VulkanTextFormat::GetFontMetrics(JaliumTextMetrics* metrics)
{
    if (!metrics) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    std::memset(metrics, 0, sizeof(JaliumTextMetrics));
    metrics->ascent = fontSize_ * 0.8f;
    metrics->descent = fontSize_ * 0.2f;
    metrics->lineGap = fontSize_ * 0.2f;
    metrics->lineHeight = metrics->ascent + metrics->descent + metrics->lineGap;
    metrics->baseline = metrics->ascent;
    return JALIUM_OK;
}

} // namespace jalium
