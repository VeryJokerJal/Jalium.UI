#pragma once

#include "jalium_backend.h"

#ifdef _WIN32
#include <dwrite.h>
#include <wincodec.h>
#include <wrl/client.h>
#endif

#include <string>
#include <vector>

namespace jalium {

#ifdef _WIN32
using Microsoft::WRL::ComPtr;
#endif

class VulkanSolidBrush : public Brush {
public:
    VulkanSolidBrush(float r, float g, float b, float a);

    JaliumBrushType GetType() const override { return JALIUM_BRUSH_SOLID; }

    float r_ = 0.0f;
    float g_ = 0.0f;
    float b_ = 0.0f;
    float a_ = 1.0f;
};

class VulkanLinearGradientBrush : public Brush {
public:
    VulkanLinearGradientBrush(
        float startX, float startY, float endX, float endY,
        const JaliumGradientStop* stops, uint32_t stopCount);

    JaliumBrushType GetType() const override { return JALIUM_BRUSH_LINEAR_GRADIENT; }

    float startX_ = 0.0f;
    float startY_ = 0.0f;
    float endX_ = 0.0f;
    float endY_ = 0.0f;
    std::vector<JaliumGradientStop> stops_;
};

class VulkanRadialGradientBrush : public Brush {
public:
    VulkanRadialGradientBrush(
        float centerX, float centerY, float radiusX, float radiusY,
        float originX, float originY,
        const JaliumGradientStop* stops, uint32_t stopCount);

    JaliumBrushType GetType() const override { return JALIUM_BRUSH_RADIAL_GRADIENT; }

    float centerX_ = 0.0f;
    float centerY_ = 0.0f;
    float radiusX_ = 0.0f;
    float radiusY_ = 0.0f;
    float originX_ = 0.0f;
    float originY_ = 0.0f;
    std::vector<JaliumGradientStop> stops_;
};

class VulkanBitmap : public Bitmap {
public:
    VulkanBitmap(uint32_t width, uint32_t height, std::vector<uint8_t> pixelData);

    uint32_t GetWidth() const override { return width_; }
    uint32_t GetHeight() const override { return height_; }

    const std::vector<uint8_t>& GetPixels() const { return pixelData_; }

private:
    uint32_t width_ = 0;
    uint32_t height_ = 0;
    std::vector<uint8_t> pixelData_;
};

class VulkanTextFormat : public TextFormat {
public:
#ifdef _WIN32
    VulkanTextFormat(
        IDWriteFactory* factory,
        const wchar_t* fontFamily,
        float fontSize,
        int32_t fontWeight,
        int32_t fontStyle);
#else
    VulkanTextFormat(
        const wchar_t* fontFamily,
        float fontSize,
        int32_t fontWeight,
        int32_t fontStyle);
#endif

    void SetAlignment(int32_t alignment) override;
    void SetParagraphAlignment(int32_t alignment) override;
    void SetTrimming(int32_t trimming) override;

    JaliumResult MeasureText(
        const wchar_t* text,
        uint32_t textLength,
        float maxWidth,
        float maxHeight,
        JaliumTextMetrics* metrics) override;

    JaliumResult GetFontMetrics(JaliumTextMetrics* metrics) override;

    const std::wstring& GetFontFamily() const { return fontFamily_; }
    float GetFontSize() const { return fontSize_; }
    int32_t GetAlignment() const { return alignment_; }
    int32_t GetParagraphAlignment() const { return paragraphAlignment_; }

private:
    std::wstring fontFamily_;
    float fontSize_ = 12.0f;
    int32_t alignment_ = JALIUM_TEXT_ALIGN_LEADING;
    int32_t paragraphAlignment_ = JALIUM_PARAGRAPH_ALIGN_NEAR;
    int32_t trimming_ = JALIUM_TEXT_TRIMMING_NONE;

#ifdef _WIN32
    ComPtr<IDWriteFactory> factory_;
    ComPtr<IDWriteTextFormat> format_;
#endif
};

} // namespace jalium
