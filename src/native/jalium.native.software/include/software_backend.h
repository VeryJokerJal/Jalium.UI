#pragma once

#include "jalium_backend.h"

#ifdef JALIUM_HAS_TEXT_ENGINE
#include "text_engine.h"
#include "text_layout.h"
#include "glyph_atlas.h"
#endif

#include <vector>
#include <stack>
#include <string>
#include <cstdint>
#include <cstring>
#include <cmath>
#include <algorithm>
#include <memory>

namespace jalium {

// Forward declarations
class SoftwareBackend;

// ============================================================================
// Resource Classes
// ============================================================================

class SoftwareSolidBrush : public Brush {
public:
    float r, g, b, a;
    SoftwareSolidBrush(float r_, float g_, float b_, float a_)
        : r(r_), g(g_), b(b_), a(a_) {}
    JaliumBrushType GetType() const override { return JALIUM_BRUSH_SOLID; }
};

class SoftwareLinearGradientBrush : public Brush {
public:
    float startX, startY, endX, endY;
    std::vector<JaliumGradientStop> stops;
    SoftwareLinearGradientBrush(float sx, float sy, float ex, float ey,
                                const JaliumGradientStop* s, uint32_t count)
        : startX(sx), startY(sy), endX(ex), endY(ey), stops(s, s + count) {}
    JaliumBrushType GetType() const override { return JALIUM_BRUSH_LINEAR_GRADIENT; }

    void SampleColor(float px, float py, float& outR, float& outG, float& outB, float& outA) const;
};

class SoftwareRadialGradientBrush : public Brush {
public:
    float centerX, centerY, radiusX, radiusY, originX, originY;
    std::vector<JaliumGradientStop> stops;
    SoftwareRadialGradientBrush(float cx, float cy, float rx, float ry,
                                 float ox, float oy,
                                 const JaliumGradientStop* s, uint32_t count)
        : centerX(cx), centerY(cy), radiusX(rx), radiusY(ry),
          originX(ox), originY(oy), stops(s, s + count) {}
    JaliumBrushType GetType() const override { return JALIUM_BRUSH_RADIAL_GRADIENT; }

    void SampleColor(float px, float py, float& outR, float& outG, float& outB, float& outA) const;
};

class SoftwareTextFormat : public TextFormat {
public:
    std::wstring fontFamily;
    float fontSize;
    int32_t fontWeight;
    int32_t fontStyle;
    int32_t alignment = 0;
    int32_t paragraphAlignment = 0;
    int32_t trimming = 0;

    SoftwareTextFormat(const wchar_t* family, float size, int32_t weight, int32_t style)
        : fontFamily(family ? family : L"sans-serif"), fontSize(size),
          fontWeight(weight), fontStyle(style) {}

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
};

class SoftwareBitmap : public Bitmap {
public:
    uint32_t width_, height_;
    std::vector<uint8_t> pixels_; // BGRA8

    SoftwareBitmap(uint32_t w, uint32_t h, std::vector<uint8_t>&& data)
        : width_(w), height_(h), pixels_(std::move(data)) {}

    uint32_t GetWidth() const override { return width_; }
    uint32_t GetHeight() const override { return height_; }
};

// ============================================================================
// Framebuffer
// ============================================================================

struct SoftwareFramebuffer {
    std::vector<uint8_t> pixels; // BGRA8, premultiplied alpha
    int32_t width = 0;
    int32_t height = 0;

    void Resize(int32_t w, int32_t h) {
        width = w;
        height = h;
        pixels.resize(static_cast<size_t>(w) * h * 4, 0);
    }

    void Clear(uint8_t r, uint8_t g, uint8_t b, uint8_t a) {
        for (size_t i = 0; i < pixels.size(); i += 4) {
            pixels[i + 0] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = a;
        }
    }

    void BlendPixel(int32_t x, int32_t y, uint8_t r, uint8_t g, uint8_t b, uint8_t a);
    void SetPixel(int32_t x, int32_t y, uint8_t r, uint8_t g, uint8_t b, uint8_t a);
};

// ============================================================================
// Transform / Clip State
// ============================================================================

struct SoftwareTransform {
    float m[6]; // 3x2 column-major: m11,m12, m21,m22, tx,ty

    void Apply(float inX, float inY, float& outX, float& outY) const {
        outX = inX * m[0] + inY * m[2] + m[4];
        outY = inX * m[1] + inY * m[3] + m[5];
    }

    static SoftwareTransform Identity() {
        return {{1, 0, 0, 1, 0, 0}};
    }

    SoftwareTransform Multiply(const SoftwareTransform& b) const {
        SoftwareTransform r;
        r.m[0] = m[0] * b.m[0] + m[1] * b.m[2];
        r.m[1] = m[0] * b.m[1] + m[1] * b.m[3];
        r.m[2] = m[2] * b.m[0] + m[3] * b.m[2];
        r.m[3] = m[2] * b.m[1] + m[3] * b.m[3];
        r.m[4] = m[4] * b.m[0] + m[5] * b.m[2] + b.m[4];
        r.m[5] = m[4] * b.m[1] + m[5] * b.m[3] + b.m[5];
        return r;
    }
};

struct SoftwareClipRect {
    float x, y, w, h;
    float rx = 0, ry = 0; // corner radii (0 = rectangular)
    bool Contains(float px, float py) const {
        if (px < x || px >= x + w || py < y || py >= y + h) return false;
        if (rx <= 0 || ry <= 0) return true;
        // Check rounded corners
        float lx = px - x, ly = py - y;
        if (lx < rx && ly < ry) {
            float dx = (lx - rx) / rx, dy = (ly - ry) / ry;
            return (dx * dx + dy * dy) <= 1.0f;
        }
        if (lx > w - rx && ly < ry) {
            float dx = (lx - (w - rx)) / rx, dy = (ly - ry) / ry;
            return (dx * dx + dy * dy) <= 1.0f;
        }
        if (lx < rx && ly > h - ry) {
            float dx = (lx - rx) / rx, dy = (ly - (h - ry)) / ry;
            return (dx * dx + dy * dy) <= 1.0f;
        }
        if (lx > w - rx && ly > h - ry) {
            float dx = (lx - (w - rx)) / rx, dy = (ly - (h - ry)) / ry;
            return (dx * dx + dy * dy) <= 1.0f;
        }
        return true;
    }
};

// ============================================================================
// Render Target
// ============================================================================

class SoftwareRenderTarget : public RenderTarget {
public:
    SoftwareRenderTarget(SoftwareBackend* backend, int32_t width, int32_t height);
    ~SoftwareRenderTarget() override;

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
    void StrokePath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin = 0, float miterLimit = 10.0f, int32_t lineCap = 0, const float* dashPattern = nullptr, uint32_t dashCount = 0, float dashOffset = 0.0f) override;
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

    // Per-corner rounded rectangles
    void FillPerCornerRoundedRectangle(float x, float y, float w, float h,
        float tl, float tr, float br, float bl, Brush* brush) override;
    void DrawPerCornerRoundedRectangle(float x, float y, float w, float h,
        float tl, float tr, float br, float bl, Brush* brush, float strokeWidth) override;

    // Batch ellipse
    void FillEllipseBatch(const float* data, uint32_t count) override;

    // Aliased clip
    void PushClipAliased(float x, float y, float w, float h) override;

    // Desktop capture
    void CaptureDesktopArea(int32_t screenX, int32_t screenY, int32_t width, int32_t height) override;
    void DrawDesktopBackdrop(
        float x, float y, float w, float h,
        float blurRadius,
        float tintR, float tintG, float tintB, float tintOpacity,
        float noiseIntensity, float saturation) override;

    // Transition capture
    void BeginTransitionCapture(int slot, float x, float y, float w, float h) override;
    void EndTransitionCapture(int slot) override;
    void DrawTransitionShader(float x, float y, float w, float h, float progress, int mode) override;
    void DrawCapturedTransition(int slot, float x, float y, float w, float h, float opacity) override;

    // Effect capture
    void BeginEffectCapture(float x, float y, float w, float h) override;
    void EndEffectCapture() override;
    void DrawBlurEffect(float x, float y, float w, float h, float radius,
        float uvOffsetX = 0, float uvOffsetY = 0) override;
    void DrawDropShadowEffect(float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY,
        float r, float g, float b, float a,
        float uvOffsetX = 0, float uvOffsetY = 0,
        float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0) override;
    void DrawOuterGlowEffect(float x, float y, float w, float h,
        float glowSize, float r, float g, float b, float a, float intensity,
        float cornerTL, float cornerTR, float cornerBR, float cornerBL) override;
    void DrawInnerShadowEffect(float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY,
        float r, float g, float b, float a,
        float cornerTL, float cornerTR, float cornerBR, float cornerBL) override;
    void DrawColorMatrixEffect(float x, float y, float w, float h,
        const float* matrix) override;
    void DrawEmbossEffect(float x, float y, float w, float h,
        float amount, float lightDirX, float lightDirY, float relief) override;
    void DrawShaderEffect(float x, float y, float w, float h,
        const uint8_t* shaderBytecode, uint32_t shaderBytecodeSize,
        const float* constants, uint32_t constantFloatCount) override;

    // Liquid glass approximation
    void DrawLiquidGlass(
        float x, float y, float w, float h,
        float cornerRadius,
        float blurRadius,
        float refractionAmount,
        float chromaticAberration,
        float tintR, float tintG, float tintB, float tintOpacity,
        float lightX, float lightY,
        float highlightBoost = 0.0f,
        int shapeType = 0,
        float shapeExponent = 4.0f,
        int neighborCount = 0,
        float fusionRadius = 30.0f,
        const float* neighborData = nullptr) override;

    const SoftwareFramebuffer& GetFramebuffer() const { return fb_; }

    friend class SoftwareBackend;

private:
    // Text rendering sub-methods (dispatched from RenderText)
#ifdef JALIUM_HAS_TEXT_ENGINE
    void RenderTextWithGlyphAtlas(const wchar_t* text, uint32_t textLength,
        FreeTypeTextFormat* ftFormat, float x, float y, float w, float h,
        SoftwareSolidBrush* brush);
#endif
#ifdef _WIN32
    void RenderTextWithGDI(const wchar_t* text, uint32_t textLength,
        SoftwareTextFormat* stf, float x, float y, float w, float h,
        SoftwareSolidBrush* brush);
#endif
    void RenderTextPlaceholder(const wchar_t* text, uint32_t textLength,
        TextFormat* format, float x, float y, float w, float h,
        SoftwareSolidBrush* brush);

    void FillScanlineRect(float x, float y, float w, float h, Brush* brush);
    void StrokeScanlineRect(float x, float y, float w, float h, Brush* brush, float strokeWidth);
    void DrawHLine(int32_t x0, int32_t x1, int32_t y, uint8_t r, uint8_t g, uint8_t b, uint8_t a);
    void DrawBresenhamLine(float x1, float y1, float x2, float y2, uint8_t r, uint8_t g, uint8_t b, uint8_t a, float strokeWidth);
    void GetBrushColor(Brush* brush, float px, float py, uint8_t& r, uint8_t& g, uint8_t& b, uint8_t& a);
    bool IsClipped(float px, float py) const;

    // Helper: test if point is inside a per-corner rounded rect (local coords)
    static bool IsInsidePerCornerRoundedRect(float px, float py, float w, float h,
        float tl, float tr, float br, float bl);

    // Helper: box blur (separable, in-place on BGRA8 buffer)
    static void BoxBlur(std::vector<uint8_t>& pixels, int32_t w, int32_t h, int32_t radius);

    // Helper: copy a region from framebuffer to a separate buffer
    void CopyRegion(const SoftwareFramebuffer& src, SoftwareFramebuffer& dst,
        int32_t srcX, int32_t srcY, int32_t w, int32_t h);

    // Helper: blit a buffer onto framebuffer with alpha
    void BlitBuffer(const SoftwareFramebuffer& src, int32_t dstX, int32_t dstY, float opacity = 1.0f);

public:
    // Helper: adaptive bezier flattening (public for use by path parser)
    static void FlattenCubicBezier(std::vector<float>& pts,
        float x0, float y0, float cp1x, float cp1y,
        float cp2x, float cp2y, float x1, float y1, float tolerance);
    static void FlattenQuadBezier(std::vector<float>& pts,
        float x0, float y0, float cpx, float cpy,
        float x1, float y1, float tolerance);
private:

    // Helper: generate stroke outline with line joins and caps.
    // For open paths: outputs 1 contour (single closed polygon).
    // For closed paths: outputs 2 contours (outer + inner rings with opposite winding).
    void GenerateStrokeOutline(const std::vector<float>& pts, uint32_t ptCount,
        float strokeWidth, bool closed, int32_t lineJoin, float miterLimit,
        int32_t lineCap, std::vector<std::vector<float>>& outContours);

    // Helper: fill multiple contours using scanline (NonZero winding rule)
    void FillMultiContour(const std::vector<std::vector<float>>& contours, Brush* brush);

    // Helper: apply dash pattern to a polyline
    static void ApplyDashPattern(const std::vector<float>& pts, uint32_t ptCount,
        const float* dashPattern, uint32_t dashCount, float dashOffset,
        std::vector<std::vector<float>>& segments);

    SoftwareBackend* backend_ = nullptr;  // non-owning back-pointer for text engine access

    SoftwareFramebuffer fb_;
    std::stack<SoftwareTransform> transformStack_;
    std::stack<SoftwareClipRect> clipStack_;
    std::stack<float> opacityStack_;
    SoftwareTransform currentTransform_;
    float currentOpacity_ = 1.0f;
    float dpiX_ = 96.0f;
    float dpiY_ = 96.0f;
    float scaleX_ = 1.0f;
    float scaleY_ = 1.0f;
    bool fullInvalidation_ = true;

    // Effect capture state
    SoftwareFramebuffer effectCaptureFb_;
    SoftwareFramebuffer savedFb_;
    float effectCaptureX_ = 0, effectCaptureY_ = 0;
    float effectCaptureW_ = 0, effectCaptureH_ = 0;
    bool effectCaptureActive_ = false;

    // Transition capture state
    SoftwareFramebuffer transitionCaptureFb_[2];
    float transitionX_[2] = {}, transitionY_[2] = {};
    float transitionW_[2] = {}, transitionH_[2] = {};
    bool transitionCaptureActive_[2] = {};

    // Desktop capture state
    SoftwareFramebuffer desktopCaptureFb_;

    // Platform-neutral surface descriptor for non-Windows present
    JaliumSurfaceDescriptor surfaceDescriptor_{};

#ifdef _WIN32
    void* hwnd_ = nullptr;
    void* cachedTextDC_ = nullptr; // HDC cached for text rendering
#endif
};

// ============================================================================
// Backend
// ============================================================================

class SoftwareBackend : public IRenderBackend {
public:
    SoftwareBackend();
    ~SoftwareBackend() override;

    JaliumBackend GetType() const override { return JALIUM_BACKEND_SOFTWARE; }
    const wchar_t* GetName() const override { return L"Software"; }

    RenderTarget* CreateRenderTarget(void* hwnd, int32_t width, int32_t height) override;
    RenderTarget* CreateRenderTargetForComposition(void* hwnd, int32_t width, int32_t height) override;
    RenderTarget* CreateRenderTargetForSurface(
        const JaliumSurfaceDescriptor* surface, int32_t width, int32_t height) override;
    Brush* CreateSolidBrush(float r, float g, float b, float a) override;
    Brush* CreateLinearGradientBrush(
        float startX, float startY, float endX, float endY,
        const JaliumGradientStop* stops, uint32_t stopCount,
        uint32_t spreadMethod = 0) override;
    Brush* CreateRadialGradientBrush(
        float centerX, float centerY, float radiusX, float radiusY,
        float originX, float originY,
        const JaliumGradientStop* stops, uint32_t stopCount,
        uint32_t spreadMethod = 0) override;
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

#ifdef JALIUM_HAS_TEXT_ENGINE
    TextEngine* GetTextEngine() const { return textEngine_.get(); }
#else
    void* GetTextEngine() const { return nullptr; }
#endif

private:
#ifdef JALIUM_HAS_TEXT_ENGINE
    std::unique_ptr<TextEngine> textEngine_;
#endif
};

IRenderBackend* CreateSoftwareBackend();

} // namespace jalium
