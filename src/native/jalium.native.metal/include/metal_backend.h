#pragma once

#include "jalium_backend.h"

#ifdef __APPLE__
#include <CoreFoundation/CoreFoundation.h>
#include <CoreGraphics/CoreGraphics.h>
#include <CoreText/CoreText.h>
#include <objc/objc.h>

// Forward declarations for Metal types (id<MTLDevice>, etc.)
// We use void* to avoid requiring Objective-C++ in the header.
typedef void* MTLDeviceRef;
typedef void* MTLCommandQueueRef;
typedef void* CAMetalLayerRef;
typedef void* MTLTextureRef;
#endif

#include <vector>
#include <stack>
#include <cstdint>
#include <cstring>
#include <cmath>

namespace jalium {

// ============================================================================
// Resource Classes
// ============================================================================

class MetalSolidBrush : public Brush {
public:
    float r, g, b, a;
    MetalSolidBrush(float r_, float g_, float b_, float a_)
        : r(r_), g(g_), b(b_), a(a_) {}
    JaliumBrushType GetType() const override { return JALIUM_BRUSH_SOLID; }
};

class MetalLinearGradientBrush : public Brush {
public:
    float startX, startY, endX, endY;
    std::vector<JaliumGradientStop> stops;
    MetalLinearGradientBrush(float sx, float sy, float ex, float ey,
                             const JaliumGradientStop* s, uint32_t count)
        : startX(sx), startY(sy), endX(ex), endY(ey), stops(s, s + count) {}
    JaliumBrushType GetType() const override { return JALIUM_BRUSH_LINEAR_GRADIENT; }
};

class MetalRadialGradientBrush : public Brush {
public:
    float centerX, centerY, radiusX, radiusY, originX, originY;
    std::vector<JaliumGradientStop> stops;
    MetalRadialGradientBrush(float cx, float cy, float rx, float ry,
                              float ox, float oy,
                              const JaliumGradientStop* s, uint32_t count)
        : centerX(cx), centerY(cy), radiusX(rx), radiusY(ry),
          originX(ox), originY(oy), stops(s, s + count) {}
    JaliumBrushType GetType() const override { return JALIUM_BRUSH_RADIAL_GRADIENT; }
};

class MetalTextFormat : public TextFormat {
public:
#ifdef __APPLE__
    CTFontRef font;
    float fontSize;
    int32_t alignment;
    int32_t paragraphAlignment;
    int32_t trimming;

    MetalTextFormat(CTFontRef f, float size)
        : font(f), fontSize(size), alignment(0), paragraphAlignment(0), trimming(0)
    {
        if (font) CFRetain(font);
    }

    ~MetalTextFormat() override {
        if (font) CFRelease(font);
    }

    void SetAlignment(int32_t a) override { alignment = a; }
    void SetParagraphAlignment(int32_t a) override { paragraphAlignment = a; }
    void SetTrimming(int32_t t) override { trimming = t; }
    void SetWordWrapping(int32_t) override {}
    void SetLineSpacing(int32_t, float, float) override {}
    void SetMaxLines(uint32_t) override {}

    JaliumResult MeasureText(
        const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight,
        JaliumTextMetrics* metrics) override;

    JaliumResult GetFontMetrics(JaliumTextMetrics* metrics) override;

    JaliumResult HitTestPoint(
        const wchar_t*, uint32_t, float, float, float, float,
        JaliumTextHitTestResult* result) override {
        if (result) memset(result, 0, sizeof(*result));
        return JALIUM_OK;
    }
    JaliumResult HitTestTextPosition(
        const wchar_t*, uint32_t, float, float, uint32_t, int32_t,
        JaliumTextHitTestResult* result) override {
        if (result) memset(result, 0, sizeof(*result));
        return JALIUM_OK;
    }
#else
    float fontSize_;
    int32_t alignment_, paragraphAlignment_, trimming_;

    MetalTextFormat(float size)
        : fontSize_(size), alignment_(0), paragraphAlignment_(0), trimming_(0) {}

    void SetAlignment(int32_t a) override { alignment_ = a; }
    void SetParagraphAlignment(int32_t a) override { paragraphAlignment_ = a; }
    void SetTrimming(int32_t t) override { trimming_ = t; }
    void SetWordWrapping(int32_t) override {}
    void SetLineSpacing(int32_t, float, float) override {}
    void SetMaxLines(uint32_t) override {}

    JaliumResult MeasureText(
        const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight,
        JaliumTextMetrics* metrics) override;

    JaliumResult GetFontMetrics(JaliumTextMetrics* metrics) override;

    JaliumResult HitTestPoint(
        const wchar_t*, uint32_t, float, float, float, float,
        JaliumTextHitTestResult* result) override {
        if (result) memset(result, 0, sizeof(*result));
        return JALIUM_OK;
    }
    JaliumResult HitTestTextPosition(
        const wchar_t*, uint32_t, float, float, uint32_t, int32_t,
        JaliumTextHitTestResult* result) override {
        if (result) memset(result, 0, sizeof(*result));
        return JALIUM_OK;
    }
#endif
};

class MetalBitmap : public Bitmap {
public:
    uint32_t width_, height_;
    std::vector<uint8_t> pixelData_;
#ifdef __APPLE__
    CGImageRef cgImage;

    MetalBitmap(uint32_t w, uint32_t h, std::vector<uint8_t>&& data, CGImageRef img)
        : width_(w), height_(h), pixelData_(std::move(data)), cgImage(img)
    {
        if (cgImage) CGImageRetain(cgImage);
    }

    ~MetalBitmap() override {
        if (cgImage) CGImageRelease(cgImage);
    }
#else
    MetalBitmap(uint32_t w, uint32_t h, std::vector<uint8_t>&& data)
        : width_(w), height_(h), pixelData_(std::move(data)) {}
#endif

    uint32_t GetWidth() const override { return width_; }
    uint32_t GetHeight() const override { return height_; }
};

// ============================================================================
// Render Target
// ============================================================================

struct MetalTransformState {
    float matrix[6]; // 3x2 column-major: m11,m12, m21,m22, m31,m32
};

class MetalRenderTarget : public RenderTarget {
public:
    MetalRenderTarget(int32_t width, int32_t height);
    ~MetalRenderTarget() override;

    bool Initialize(void* nsWindow);

    JaliumResult Resize(int32_t width, int32_t height) override;
    JaliumResult BeginDraw() override;
    JaliumResult EndDraw() override;

    void Clear(float r, float g, float b, float a) override;
    void FillRectangle(float x, float y, float w, float h, Brush* brush) override;
    void DrawRectangle(float x, float y, float w, float h, Brush* brush, float strokeWidth) override;
    void FillRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush) override;
    void DrawRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush, float strokeWidth) override;
    void FillEllipse(float cx, float cy, float rx, float ry, Brush* brush) override;
    void DrawEllipse(float cx, float cy, float rx, float ry, Brush* brush, float strokeWidth) override;
    void DrawLine(float x1, float y1, float x2, float y2, Brush* brush, float strokeWidth) override;
    void FillPolygon(const float* points, uint32_t pointCount, Brush* brush, int32_t fillRule) override;
    void DrawPolygon(const float* points, uint32_t pointCount, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin = 0, float miterLimit = 10.0f) override;
    void FillPath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule) override;
    void StrokePath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin = 0, float miterLimit = 10.0f, int32_t lineCap = 0) override;
    void DrawContentBorder(float x, float y, float w, float h,
        float blRadius, float brRadius,
        Brush* fillBrush, Brush* strokeBrush, float strokeWidth) override;
    void RenderText(
        const wchar_t* text, uint32_t textLength,
        TextFormat* format,
        float x, float y, float w, float h,
        Brush* brush) override;
    void PushTransform(const float* matrix) override;
    void PopTransform() override;
    void PushClip(float x, float y, float w, float h) override;
    void PopClip() override;
    void PushRoundedRectClip(float x, float y, float w, float h, float rx, float ry) override;
    void PunchTransparentRect(float x, float y, float w, float h) override;
    void PushOpacity(float opacity) override;
    void PopOpacity() override;
    void SetShapeType(int type, float n) override;
    void SetVSyncEnabled(bool enabled) override;
    void SetDpi(float dpiX, float dpiY) override;
    void AddDirtyRect(float x, float y, float w, float h) override;
    void SetFullInvalidation() override;
    void DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity) override;
    void DrawBackdropFilter(
        float x, float y, float w, float h,
        const char* backdropFilter,
        const char* material,
        const char* materialTint,
        float tintOpacity,
        float blurRadius,
        float cornerRadiusTL, float cornerRadiusTR,
        float cornerRadiusBR, float cornerRadiusBL) override;
    void DrawGlowingBorderHighlight(
        float x, float y, float w, float h,
        float animationPhase,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth,
        float trailLength,
        float dimOpacity,
        float screenWidth, float screenHeight) override;
    void DrawGlowingBorderTransition(
        float fromX, float fromY, float fromW, float fromH,
        float toX, float toY, float toW, float toH,
        float headProgress, float tailProgress,
        float animationPhase,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth,
        float trailLength,
        float dimOpacity,
        float screenWidth, float screenHeight) override;
    void DrawRippleEffect(
        float x, float y, float w, float h,
        float rippleProgress,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth,
        float dimOpacity,
        float screenWidth, float screenHeight) override;

private:
#ifdef __APPLE__
    void ApplyBrush(CGContextRef ctx, Brush* brush, bool forStroke);
    void ApplyGradientFill(CGContextRef ctx, Brush* brush);
    CGPathRef CreateRoundedRectPath(float x, float y, float w, float h, float rx, float ry);
    CGPathRef BuildCommandPath(float startX, float startY, const float* commands, uint32_t commandLength, bool closed);
#endif

    std::vector<uint8_t> framebuffer_;
    std::stack<MetalTransformState> transformStack_;
    std::stack<float> opacityStack_;
    int32_t clipDepth_ = 0;
    float currentOpacity_ = 1.0f;
    float dpiX_ = 96.0f;
    float dpiY_ = 96.0f;
    bool fullInvalidation_ = true;

#ifdef __APPLE__
    CGContextRef cgContext_ = nullptr;
    CAMetalLayerRef metalLayer_ = nullptr;
    MTLDeviceRef device_ = nullptr;
    MTLCommandQueueRef commandQueue_ = nullptr;
    void* nsView_ = nullptr; // NSView*
#endif
};

// ============================================================================
// Backend
// ============================================================================

/// Metal backend — full implementation using CoreGraphics + CoreText + Metal.
class MetalBackend : public IRenderBackend {
public:
    MetalBackend();
    ~MetalBackend() override;

    bool Initialize();

    JaliumBackend GetType() const override { return JALIUM_BACKEND_METAL; }
    const wchar_t* GetName() const override { return L"Metal"; }
    JaliumResult CheckDeviceStatus() override;

    RenderTarget* CreateRenderTarget(void* hwnd, int32_t width, int32_t height) override;
    RenderTarget* CreateRenderTargetForComposition(void* hwnd, int32_t width, int32_t height) override;
    Brush* CreateSolidBrush(float r, float g, float b, float a) override;
    Brush* CreateLinearGradientBrush(
        float startX, float startY, float endX, float endY,
        const JaliumGradientStop* stops, uint32_t stopCount) override;
    Brush* CreateRadialGradientBrush(
        float centerX, float centerY, float radiusX, float radiusY,
        float originX, float originY,
        const JaliumGradientStop* stops, uint32_t stopCount) override;
    TextFormat* CreateTextFormat(
        const wchar_t* fontFamily,
        float fontSize,
        int32_t fontWeight,
        int32_t fontStyle) override;
    Bitmap* CreateBitmapFromMemory(const uint8_t* data, uint32_t dataSize) override;
    Bitmap* CreateBitmapFromPixels(
        const uint8_t* pixels,
        uint32_t width,
        uint32_t height,
        uint32_t stride) override;

private:
    bool initialized_ = false;
#ifdef __APPLE__
    MTLDeviceRef device_ = nullptr;
#endif
};

IRenderBackend* CreateMetalBackend();

} // namespace jalium
