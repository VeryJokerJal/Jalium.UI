#include "vulkan_resources.h"

#ifndef _WIN32
#include "text_engine.h"
#include "text_layout.h"
#endif

#ifdef _WIN32
#include <Windows.h>
#endif

#include <algorithm>
#include <cmath>
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

bool VulkanBitmap::UpdatePackedPixels(const uint8_t* pixels, uint32_t width, uint32_t height, uint32_t stride)
{
    if (!pixels || width == 0 || height == 0 || stride < width * 4u) {
        return false;
    }
    if (width != width_ || height != height_) {
        return false;  // Size change requires recreation.
    }

    const size_t rowBytes = static_cast<size_t>(width) * 4u;
    const size_t requiredSize = rowBytes * height;
    if (pixelData_.size() != requiredSize) {
        pixelData_.resize(requiredSize);
    }

    if (stride == rowBytes) {
        std::memcpy(pixelData_.data(), pixels, requiredSize);
    } else {
        for (uint32_t row = 0; row < height; ++row) {
            std::memcpy(pixelData_.data() + row * rowBytes,
                        pixels + static_cast<size_t>(row) * stride,
                        rowBytes);
        }
    }
    return true;
}

#ifdef _WIN32

namespace {

// Helper: create a GDI font matching the text format properties.
HFONT CreateGdiFont(const std::wstring& fontFamily, float fontSize, int32_t fontWeight, int32_t fontStyle)
{
    const int fontHeight = -static_cast<int>(std::round(fontSize));
    return CreateFontW(
        fontHeight, 0, 0, 0,
        fontWeight,
        (fontStyle == 1 || fontStyle == 2) ? TRUE : FALSE,
        FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS,
        CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_DONTCARE,
        fontFamily.c_str());
}

// Helper: clamp a float dimension to a safe LONG range for GDI RECT.
// UI frameworks frequently pass FLT_MAX / infinity for "unconstrained";
// static_cast<LONG> on those is undefined behavior on MSVC.
LONG SafeLong(float v)
{
    if (v <= 0 || !(v == v)) return 0;  // negative, zero, or NaN
    if (v > 100000.0f) return 100000;
    return static_cast<LONG>(v);
}

// Helper: build DrawTextW flags from text format properties.
UINT BuildDrawTextFlags(int32_t alignment, int32_t wordWrapping, int32_t trimming)
{
    UINT flags = DT_NOPREFIX | DT_TOP;
    switch (alignment) {
        case JALIUM_TEXT_ALIGN_CENTER:   flags |= DT_CENTER; break;
        case JALIUM_TEXT_ALIGN_TRAILING: flags |= DT_RIGHT;  break;
        default:                         flags |= DT_LEFT;   break;
    }
    if (wordWrapping == 1) {
        flags |= DT_SINGLELINE;
    } else {
        flags |= DT_WORDBREAK;
    }
    switch (trimming) {
        case JALIUM_TEXT_TRIMMING_CHARACTER_ELLIPSIS: flags |= DT_END_ELLIPSIS;  break;
        case JALIUM_TEXT_TRIMMING_WORD_ELLIPSIS:      flags |= DT_WORD_ELLIPSIS; break;
    }
    return flags;
}

} // namespace

VulkanTextFormat::VulkanTextFormat(
    const wchar_t* fontFamily,
    float fontSize,
    int32_t fontWeight,
    int32_t fontStyle)
    : fontFamily_(fontFamily ? fontFamily : L"Segoe UI")
    , fontSize_(fontSize)
    , fontWeight_(fontWeight)
    , fontStyle_(fontStyle)
{
}
#else
VulkanTextFormat::VulkanTextFormat(
    TextEngine* textEngine,
    const wchar_t* fontFamily,
    float fontSize,
    int32_t fontWeight,
    int32_t fontStyle)
    : fontFamily_(fontFamily ? fontFamily : L"Sans")
    , fontSize_(fontSize)
    , fontWeight_(fontWeight)
    , fontStyle_(fontStyle)
{
    // Create a FreeTypeTextFormat if text engine is available
    if (textEngine) {
        auto* ftFormat = textEngine->CreateTextFormat(fontFamily, fontSize, fontWeight, fontStyle);
        if (ftFormat) {
            ftTextFormat_.reset(static_cast<FreeTypeTextFormat*>(ftFormat));
        }
    }
}

#endif

VulkanTextFormat::~VulkanTextFormat() = default;

void VulkanTextFormat::SetAlignment(int32_t alignment)
{
    alignment_ = alignment;
#ifndef _WIN32
    if (ftTextFormat_) ftTextFormat_->SetAlignment(alignment);
#endif
}

void VulkanTextFormat::SetParagraphAlignment(int32_t alignment)
{
    paragraphAlignment_ = alignment;
#ifndef _WIN32
    if (ftTextFormat_) ftTextFormat_->SetParagraphAlignment(alignment);
#endif
}

void VulkanTextFormat::SetTrimming(int32_t trimming)
{
    trimming_ = trimming;
#ifndef _WIN32
    if (ftTextFormat_) ftTextFormat_->SetTrimming(trimming);
#endif
}

void VulkanTextFormat::SetWordWrapping(int32_t wrapping)
{
    wordWrapping_ = wrapping;
#ifndef _WIN32
    if (ftTextFormat_) ftTextFormat_->SetWordWrapping(wrapping);
#endif
}

void VulkanTextFormat::SetLineSpacing(int32_t method, float spacing, float baseline)
{
    lineSpacingMethod_ = method;
    lineSpacingMultiplier_ = spacing;
    lineSpacingBaseline_ = baseline;
#ifndef _WIN32
    if (ftTextFormat_) ftTextFormat_->SetLineSpacing(method, spacing, baseline);
#endif
}

void VulkanTextFormat::SetMaxLines(uint32_t maxLines) {
    maxLines_ = maxLines;
#ifndef _WIN32
    if (ftTextFormat_) ftTextFormat_->SetMaxLines(maxLines);
#endif
}

JaliumResult VulkanTextFormat::HitTestPoint(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float /*maxHeight*/,
    float pointX, float pointY,
    JaliumTextHitTestResult* result)
{
    if (!result) return JALIUM_ERROR_INVALID_ARGUMENT;
    memset(result, 0, sizeof(*result));
#ifdef _WIN32
    if (!text || textLength == 0) return JALIUM_OK;

    HDC hdc = CreateCompatibleDC(nullptr);
    if (!hdc) return JALIUM_OK;

    HFONT hFont = CreateGdiFont(fontFamily_, fontSize_, fontWeight_, fontStyle_);
    HGDIOBJ oldFont = SelectObject(hdc, hFont);

    TEXTMETRICW tm {};
    GetTextMetricsW(hdc, &tm);
    float lineH = static_cast<float>(tm.tmHeight);

    int fitCount = 0;
    SIZE totalSize {};
    std::vector<INT> widths(textLength);
    GetTextExtentExPointW(hdc, text, static_cast<int>(textLength),
        SafeLong(maxWidth), &fitCount, widths.data(), &totalSize);

    uint32_t pos = 0;
    float prevWidth = 0;
    for (uint32_t i = 0; i < static_cast<uint32_t>(fitCount) && i < textLength; ++i) {
        float charRight = static_cast<float>(widths[i]);
        float charMid = (prevWidth + charRight) / 2.0f;
        if (pointX <= charMid) { pos = i; break; }
        pos = i + 1;
        prevWidth = charRight;
    }

    result->textPosition = pos;
    result->isTrailingHit = 0;
    result->isInside = (pointX >= 0 && pointX <= totalSize.cx && pointY >= 0 && pointY <= totalSize.cy) ? 1 : 0;
    result->caretX = (pos > 0 && pos <= textLength) ? static_cast<float>(widths[pos - 1]) : 0;
    result->caretY = 0;
    result->caretHeight = lineH;

    SelectObject(hdc, oldFont);
    DeleteObject(hFont);
    DeleteDC(hdc);
#else
    if (ftTextFormat_)
        return ftTextFormat_->HitTestPoint(text, textLength, maxWidth, 0, pointX, pointY, result);
#endif
    return JALIUM_OK;
}

JaliumResult VulkanTextFormat::HitTestTextPosition(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float /*maxHeight*/,
    uint32_t textPosition, int32_t isTrailingHit,
    JaliumTextHitTestResult* result)
{
    if (!result) return JALIUM_ERROR_INVALID_ARGUMENT;
    memset(result, 0, sizeof(*result));
#ifdef _WIN32
    if (!text || textLength == 0) return JALIUM_OK;

    HDC hdc = CreateCompatibleDC(nullptr);
    if (!hdc) return JALIUM_OK;

    HFONT hFont = CreateGdiFont(fontFamily_, fontSize_, fontWeight_, fontStyle_);
    HGDIOBJ oldFont = SelectObject(hdc, hFont);

    TEXTMETRICW tm {};
    GetTextMetricsW(hdc, &tm);
    float lineH = static_cast<float>(tm.tmHeight);

    int fitCount = 0;
    SIZE totalSize {};
    std::vector<INT> widths(textLength);
    GetTextExtentExPointW(hdc, text, static_cast<int>(textLength),
        SafeLong(maxWidth), &fitCount, widths.data(), &totalSize);

    float cx = 0;
    if (textPosition > 0 && textPosition <= textLength) {
        cx = static_cast<float>(widths[textPosition - 1]);
    }
    if (isTrailingHit && textPosition < textLength) {
        cx = static_cast<float>(widths[textPosition]);
    }

    result->textPosition = textPosition;
    result->isTrailingHit = isTrailingHit;
    result->isInside = 1;
    result->caretX = cx;
    result->caretY = 0;
    result->caretHeight = lineH;

    SelectObject(hdc, oldFont);
    DeleteObject(hFont);
    DeleteDC(hdc);
#else
    if (ftTextFormat_)
        return ftTextFormat_->HitTestTextPosition(text, textLength, maxWidth, 0, textPosition, isTrailingHit, result);
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
    HDC hdc = CreateCompatibleDC(nullptr);
    if (hdc) {
        HFONT hFont = CreateGdiFont(fontFamily_, fontSize_, fontWeight_, fontStyle_);
        HGDIOBJ oldFont = SelectObject(hdc, hFont);

        RECT rc = { 0, 0, SafeLong(maxWidth), SafeLong(maxHeight) };
        UINT dtFlags = BuildDrawTextFlags(alignment_, wordWrapping_, JALIUM_TEXT_TRIMMING_NONE) | DT_CALCRECT;
        DrawTextW(hdc, text, static_cast<int>(textLength), &rc, dtFlags);

        TEXTMETRICW tm {};
        GetTextMetricsW(hdc, &tm);

        metrics->width = static_cast<float>(rc.right - rc.left);
        metrics->height = static_cast<float>(rc.bottom - rc.top);
        metrics->lineHeight = static_cast<float>(tm.tmHeight);
        metrics->baseline = static_cast<float>(tm.tmAscent);
        metrics->ascent = static_cast<float>(tm.tmAscent);
        metrics->descent = static_cast<float>(tm.tmDescent);
        metrics->lineGap = static_cast<float>(tm.tmExternalLeading);
        metrics->lineCount = (tm.tmHeight > 0)
            ? static_cast<uint32_t>(metrics->height / static_cast<float>(tm.tmHeight))
            : 1;
        if (metrics->lineCount == 0) metrics->lineCount = 1;

        SelectObject(hdc, oldFont);
        DeleteObject(hFont);
        DeleteDC(hdc);
        return JALIUM_OK;
    }
#else
    // Cross-platform: delegate to FreeType text engine
    if (ftTextFormat_) {
        return ftTextFormat_->MeasureText(text, textLength, maxWidth, maxHeight, metrics);
    }
#endif

    // Fallback: approximate metrics if no text engine available
    if (metrics->width == 0.0f && textLength > 0) {
        const float approxCharWidth = fontSize_ * 0.55f;
        metrics->width = std::min(maxWidth, approxCharWidth * static_cast<float>(textLength));
        metrics->height = std::min(maxHeight, fontSize_ * 1.2f);
        metrics->lineCount = 1;
    }

    if (metrics->ascent == 0.0f) {
        metrics->ascent = fontSize_ * 0.8f;
        metrics->descent = fontSize_ * 0.2f;
        metrics->lineGap = fontSize_ * 0.2f;
        metrics->lineHeight = metrics->ascent + metrics->descent + metrics->lineGap;
        metrics->baseline = metrics->ascent;
        metrics->height = std::max(metrics->height, metrics->lineHeight);
    }
    return JALIUM_OK;
}

JaliumResult VulkanTextFormat::GetFontMetrics(JaliumTextMetrics* metrics)
{
    if (!metrics) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    std::memset(metrics, 0, sizeof(JaliumTextMetrics));

#ifdef _WIN32
    HDC hdc = CreateCompatibleDC(nullptr);
    if (hdc) {
        HFONT hFont = CreateGdiFont(fontFamily_, fontSize_, fontWeight_, fontStyle_);
        HGDIOBJ oldFont = SelectObject(hdc, hFont);

        TEXTMETRICW tm {};
        GetTextMetricsW(hdc, &tm);

        metrics->ascent = static_cast<float>(tm.tmAscent);
        metrics->descent = static_cast<float>(tm.tmDescent);
        metrics->lineGap = static_cast<float>(tm.tmExternalLeading);
        metrics->lineHeight = static_cast<float>(tm.tmHeight);
        metrics->baseline = static_cast<float>(tm.tmAscent);

        SelectObject(hdc, oldFont);
        DeleteObject(hFont);
        DeleteDC(hdc);
        return JALIUM_OK;
    }
#else
    if (ftTextFormat_) {
        return ftTextFormat_->GetFontMetrics(metrics);
    }
#endif

    // Fallback
    metrics->ascent = fontSize_ * 0.8f;
    metrics->descent = fontSize_ * 0.2f;
    metrics->lineGap = fontSize_ * 0.2f;
    metrics->lineHeight = metrics->ascent + metrics->descent + metrics->lineGap;
    metrics->baseline = metrics->ascent;
    return JALIUM_OK;
}

} // namespace jalium
