#pragma once

#include "jalium_backend.h"
#include "d3d12_backend.h"
#include <vector>

namespace jalium {

/// Simple color representation (no D2D dependency).
struct ColorF { float r, g, b, a; };

/// Gradient stop with position and color (no D2D dependency).
struct GradStop { float position; ColorF color; };

class D3D12SolidBrush : public Brush {
public:
    D3D12SolidBrush(float r, float g, float b, float a);
    ~D3D12SolidBrush() override = default;

    JaliumBrushType GetType() const override { return JALIUM_BRUSH_SOLID; }

    float r_, g_, b_, a_;
};

class D3D12LinearGradientBrush : public Brush {
public:
    D3D12LinearGradientBrush(
        float startX, float startY, float endX, float endY,
        const JaliumGradientStop* stops, uint32_t stopCount);
    ~D3D12LinearGradientBrush() override = default;

    JaliumBrushType GetType() const override { return JALIUM_BRUSH_LINEAR_GRADIENT; }

    float startX_, startY_, endX_, endY_;
    std::vector<GradStop> stops_;
    uint32_t spreadMethod_ = 0;  // 0=Pad, 1=Repeat, 2=Reflect
};

class D3D12RadialGradientBrush : public Brush {
public:
    D3D12RadialGradientBrush(
        float centerX, float centerY, float radiusX, float radiusY,
        float originX, float originY,
        const JaliumGradientStop* stops, uint32_t stopCount);
    ~D3D12RadialGradientBrush() override = default;

    JaliumBrushType GetType() const override { return JALIUM_BRUSH_RADIAL_GRADIENT; }

    float centerX_, centerY_, radiusX_, radiusY_, originX_, originY_;
    std::vector<GradStop> stops_;
    uint32_t spreadMethod_ = 0;  // 0=Pad, 1=Repeat, 2=Reflect
};

/// D3D12 bitmap wrapper.
class D3D12Bitmap : public Bitmap {
public:
    D3D12Bitmap(uint32_t width, uint32_t height);
    ~D3D12Bitmap() override = default;

    uint32_t GetWidth() const override { return width_; }
    uint32_t GetHeight() const override { return height_; }

    /// Gets or creates a D3D12 texture resource for direct rendering.
    ID3D12Resource* GetOrCreateD3D12Texture(ID3D12Device* device, ID3D12GraphicsCommandList* cmdList);

    /// Sets the bitmap data from WIC.
    void SetBitmapData(const uint8_t* data, uint32_t dataSize);

    /// Releases old resources that are no longer needed by the GPU.
    /// Call after a fence wait confirms the GPU finished using old textures.
    void ReleasePendingResources();

    uint32_t width_, height_;
    std::vector<uint8_t> pixelData_;

private:
    // D3D12 texture for direct renderer path
    ComPtr<ID3D12Resource> d3d12Texture_;
    ComPtr<ID3D12Resource> d3d12UploadBuffer_;
    bool d3d12TextureValid_ = false;
    // Deferred release: old resources kept alive until GPU finishes using them
    std::vector<ComPtr<ID3D12Resource>> pendingRelease_;
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
    void SetWordWrapping(int32_t wrapping) override;
    void SetLineSpacing(int32_t method, float spacing, float baseline) override;
    void SetMaxLines(uint32_t maxLines) override;

    JaliumResult MeasureText(
        const wchar_t* text,
        uint32_t textLength,
        float maxWidth,
        float maxHeight,
        JaliumTextMetrics* metrics) override;

    JaliumResult GetFontMetrics(JaliumTextMetrics* metrics) override;

    JaliumResult HitTestPoint(
        const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight,
        float pointX, float pointY,
        JaliumTextHitTestResult* result) override;

    JaliumResult HitTestTextPosition(
        const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight,
        uint32_t textPosition, int32_t isTrailingHit,
        JaliumTextHitTestResult* result) override;

    IDWriteTextFormat* GetFormat() const { return format_.Get(); }
    IDWriteFactory* GetFactory() const { return factory_; }

    /// Creates an IDWriteTextLayout with the current format settings (including maxLines).
    HRESULT CreateLayout(const wchar_t* text, uint32_t textLength,
                         float maxWidth, float maxHeight,
                         IDWriteTextLayout** layout);

private:

    ComPtr<IDWriteTextFormat> format_;
    IDWriteFactory* factory_ = nullptr;
    float fontSize_ = 0.0f;
    uint32_t maxLines_ = 0;  // 0 = unlimited
};

} // namespace jalium
