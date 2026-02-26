#pragma once

#include "jalium_backend.h"
#include "d3d12_backend.h"
#include <vector>

namespace jalium {

/// D2D solid color brush wrapper.
class D3D12SolidBrush : public Brush {
public:
    D3D12SolidBrush(float r, float g, float b, float a);
    ~D3D12SolidBrush() override = default;

    JaliumBrushType GetType() const override { return JALIUM_BRUSH_SOLID; }

    /// Creates the actual D2D brush for a device context.
    ID2D1SolidColorBrush* GetOrCreateBrush(ID2D1DeviceContext* context);

    float r_, g_, b_, a_;

private:
    ComPtr<ID2D1SolidColorBrush> brush_;
    ID2D1DeviceContext* lastContext_ = nullptr;
};

/// D2D linear gradient brush wrapper.
class D3D12LinearGradientBrush : public Brush {
public:
    D3D12LinearGradientBrush(
        float startX, float startY, float endX, float endY,
        const JaliumGradientStop* stops, uint32_t stopCount);
    ~D3D12LinearGradientBrush() override = default;

    JaliumBrushType GetType() const override { return JALIUM_BRUSH_LINEAR_GRADIENT; }

    /// Creates the actual D2D brush for a device context.
    ID2D1LinearGradientBrush* GetOrCreateBrush(ID2D1DeviceContext* context);

    float startX_, startY_, endX_, endY_;
    std::vector<D2D1_GRADIENT_STOP> stops_;

private:
    ComPtr<ID2D1GradientStopCollection> stopCollection_;
    ComPtr<ID2D1LinearGradientBrush> brush_;
    ID2D1DeviceContext* lastContext_ = nullptr;
};

/// D2D radial gradient brush wrapper.
class D3D12RadialGradientBrush : public Brush {
public:
    D3D12RadialGradientBrush(
        float centerX, float centerY, float radiusX, float radiusY,
        float originX, float originY,
        const JaliumGradientStop* stops, uint32_t stopCount);
    ~D3D12RadialGradientBrush() override = default;

    JaliumBrushType GetType() const override { return JALIUM_BRUSH_RADIAL_GRADIENT; }

    /// Creates the actual D2D brush for a device context.
    ID2D1RadialGradientBrush* GetOrCreateBrush(ID2D1DeviceContext* context);

    float centerX_, centerY_, radiusX_, radiusY_, originX_, originY_;
    std::vector<D2D1_GRADIENT_STOP> stops_;

private:
    ComPtr<ID2D1GradientStopCollection> stopCollection_;
    ComPtr<ID2D1RadialGradientBrush> brush_;
    ID2D1DeviceContext* lastContext_ = nullptr;
};

/// D2D bitmap wrapper.
class D3D12Bitmap : public Bitmap {
public:
    D3D12Bitmap(uint32_t width, uint32_t height);
    ~D3D12Bitmap() override = default;

    uint32_t GetWidth() const override { return width_; }
    uint32_t GetHeight() const override { return height_; }

    /// Gets or creates the D2D bitmap for a device context.
    ID2D1Bitmap* GetOrCreateBitmap(ID2D1DeviceContext* context);

    /// Sets the bitmap data from WIC.
    void SetBitmapData(const uint8_t* data, uint32_t dataSize);

    uint32_t width_, height_;
    std::vector<uint8_t> pixelData_;

private:
    ComPtr<ID2D1Bitmap> bitmap_;
    ID2D1DeviceContext* lastContext_ = nullptr;
};

/// DirectWrite text format wrapper.
class D3D12TextFormat : public TextFormat {
public:
    D3D12TextFormat(IDWriteFactory* factory,
                    const wchar_t* fontFamily,
                    float fontSize,
                    int32_t fontWeight,
                    int32_t fontStyle);
    ~D3D12TextFormat() override = default;

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

    IDWriteTextFormat* GetFormat() const { return format_.Get(); }
    IDWriteFactory* GetFactory() const { return factory_; }

private:
    ComPtr<IDWriteTextFormat> format_;
    IDWriteFactory* factory_ = nullptr;
    float fontSize_ = 0.0f;
};

} // namespace jalium
