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
#include <mutex>

namespace jalium {

// Forward declarations
class SoftwareBackend;
class SoftwareWorkerPool;
class SoftwareRetainedLayer;
class SoftwareEllipseMaskCache;
class SoftwareTextMaskCache;
class SoftwareTextCompositeCache;
class SoftwareBackdropCache;
class SoftwareRoundedRectMaskCache;
class SoftwareEllipticalRoundedRectMaskCache;
class SoftwarePathRasterCache;
class SoftwareLineRasterCache;
class SoftwareEffectResultCache;
#ifdef JALIUM_SOFTWARE_WAYLAND_PRESENT
class WaylandShmPresenter;
#endif
#ifdef JALIUM_SOFTWARE_X11_PRESENT
class X11SoftwarePresenter;
#endif

// ============================================================================
// Resource Classes
// ============================================================================

struct SoftwareGradientRaster {
    int32_t x = 0;
    int32_t y = 0;
    int32_t width = 0;
    int32_t height = 0;
    float left = 0.0f;
    float top = 0.0f;
    float right = 0.0f;
    float bottom = 0.0f;
    float opacity = 1.0f;
    bool opaque = false;
    std::vector<uint8_t> pixels;
};

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
    float deltaX = 0, deltaY = 0, inverseLengthSquared = 0;
    uint32_t spreadMethod; // 0=Pad, 1=Repeat, 2=Reflect
    // RGB channels are converted to linear light once at brush creation;
    // positions and alpha retain their wire values.
    std::vector<JaliumGradientStop> stops;
    std::vector<uint32_t> colorLut;
    std::vector<float> alphaLut;
    SoftwareLinearGradientBrush(float sx, float sy, float ex, float ey,
                                const JaliumGradientStop* s, uint32_t count,
                                uint32_t spread);
    ~SoftwareLinearGradientBrush() override;
    JaliumBrushType GetType() const override { return JALIUM_BRUSH_LINEAR_GRADIENT; }

    void SampleColor(float px, float py, float& outR, float& outG, float& outB, float& outA) const;
    void SampleColor8(float px, float py,
                      uint8_t& outR, uint8_t& outG, uint8_t& outB, float& outA) const;
    std::shared_ptr<const SoftwareGradientRaster> GetOrCreateRaster(
        float left, float top, float right, float bottom, float opacity) const;

private:
    mutable std::mutex rasterCacheMutex_;
    mutable std::vector<std::shared_ptr<SoftwareGradientRaster>> rasterCache_;
    mutable size_t rasterCacheBytes_ = 0;
};

class SoftwareRadialGradientBrush : public Brush {
public:
    float centerX, centerY, radiusX, radiusY, originX, originY;
    uint32_t spreadMethod; // 0=Pad, 1=Repeat, 2=Reflect
    std::vector<JaliumGradientStop> stops;
    std::vector<uint32_t> colorLut;
    std::vector<float> alphaLut;
    SoftwareRadialGradientBrush(float cx, float cy, float rx, float ry,
                                 float ox, float oy,
                                 const JaliumGradientStop* s, uint32_t count,
                                 uint32_t spread);
    ~SoftwareRadialGradientBrush() override;
    JaliumBrushType GetType() const override { return JALIUM_BRUSH_RADIAL_GRADIENT; }

    void SampleColor(float px, float py, float& outR, float& outG, float& outB, float& outA) const;
    void SampleColor8(float px, float py,
                      uint8_t& outR, uint8_t& outG, uint8_t& outB, float& outA) const;
    std::shared_ptr<const SoftwareGradientRaster> GetOrCreateRaster(
        float left, float top, float right, float bottom, float opacity) const;

private:
    mutable std::mutex rasterCacheMutex_;
    mutable std::vector<std::shared_ptr<SoftwareGradientRaster>> rasterCache_;
    mutable size_t rasterCacheBytes_ = 0;
};

class SoftwareTextFormat : public TextFormat, public FontUnitMetricsProvider {
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
    JaliumResult GetFontUnitMetrics(JaliumFontUnitMetrics* metrics) override;

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

struct SoftwareScaledBitmap {
    uint32_t width = 0;
    uint32_t height = 0;
    bool opaque = false;
    float destinationWidth = 0.0f;
    float destinationHeight = 0.0f;
    float phaseX = 0.0f;
    float phaseY = 0.0f;
    float opacity = 1.0f;
    std::vector<uint8_t> pixels;
};

class SoftwareBitmap : public Bitmap {
public:
    uint32_t width_, height_;
    std::vector<uint8_t> pixels_; // BGRA8
    bool dynamic_ = false;
    bool opaque_ = false;

    SoftwareBitmap(
        uint32_t w, uint32_t h, std::vector<uint8_t>&& data,
        bool dynamic = false,
        size_t scaledCacheBudgetBytes = 8u * 1024u * 1024u,
        bool trackGlobalCache = true)
        : width_(w), height_(h), pixels_(std::move(data)), dynamic_(dynamic),
          scaledCacheBudgetBytes_(scaledCacheBudgetBytes),
          trackGlobalCache_(trackGlobalCache)
    {
        if (!dynamic_ && pixels_.size() >= static_cast<size_t>(w) * h * 4u) {
            opaque_ = true;
            for (size_t offset = 3; offset < pixels_.size(); offset += 4) {
                if (pixels_[offset] != 255) { opaque_ = false; break; }
            }
        }
    }
    ~SoftwareBitmap() override;

    uint32_t GetWidth() const override { return width_; }
    uint32_t GetHeight() const override { return height_; }

    std::shared_ptr<const SoftwareScaledBitmap> GetOrCreateScaled(
        uint32_t width, uint32_t height,
        float destinationWidth, float destinationHeight,
        float phaseX, float phaseY, float opacity);
    size_t ScaledCacheBytes() const;
    void ClearScaledCache();

private:
    mutable std::mutex scaledCacheMutex_;
    std::vector<std::shared_ptr<SoftwareScaledBitmap>> scaledCache_;
    size_t scaledCacheBytes_ = 0;
    size_t scaledCacheBudgetBytes_ = 0;
    bool trackGlobalCache_ = true;
};

// Software video surface: backed by a SoftwareBitmap (the same BGRA8 vector
// the rest of the software backend already knows how to composite). Lock
// exposes the vector buffer directly; Unlock is a no-op because the
// composer reads `bitmap.pixels_` on the next draw call without any staging
// or texture-upload step. This is what removes the WriteableBitmap copy
// hop on the software path — managed video frames land straight in the
// vector the composer is going to read.
class SoftwareVideoSurface : public VideoSurface {
public:
    SoftwareBitmap bitmap;

    SoftwareVideoSurface(uint32_t w, uint32_t h)
        : bitmap(w, h, CreatePixelBuffer(w, h), true) {}

    uint32_t GetWidth()  const override { return bitmap.width_; }
    uint32_t GetHeight() const override { return bitmap.height_; }
    JaliumVideoSurfaceKind GetKind() const override { return JALIUM_VS_KIND_BGRA8_CPU; }

    bool Lock(uint8_t** outPtr, uint32_t* outStride) override
    {
        if (!outPtr || !outStride) return false;
        if (bitmap.pixels_.empty()) return false;
        *outPtr = bitmap.pixels_.data();
        *outStride = static_cast<uint32_t>(
            static_cast<uint64_t>(bitmap.width_) * 4u);
        return true;
    }

    bool Unlock(const JaliumVideoSurfaceDirtyRect* /*dirty*/) override
    {
        // Software composer reads pixels_ on the next composite pass.
        return true;
    }

private:
    static std::vector<uint8_t> CreatePixelBuffer(uint32_t w, uint32_t h)
    {
        PackedBgraLayout layout{};
        if (!TryComputeTightlyPackedBgraLayout(w, h, layout)) {
            return {};
        }
        return std::vector<uint8_t>(layout.packedBytes, 0);
    }
};

// ============================================================================
// Framebuffer
// ============================================================================

struct SoftwareFramebuffer {
    std::vector<uint8_t> pixels; // BGRA8, premultiplied alpha
    int32_t width = 0;
    int32_t height = 0;

    void Resize(int32_t w, int32_t h) {
        pixels.resize(static_cast<size_t>(w) * h * 4, 0);
        width = w;
        height = h;
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
    void BlendPixelUnchecked(int32_t x, int32_t y, uint8_t r, uint8_t g, uint8_t b, uint8_t a);
    void FillOpaqueSpan(int32_t y, int32_t x0, int32_t x1, uint32_t packedBgra);
    void BlendSolidSpan(int32_t y, int32_t x0, int32_t x1,
                        uint8_t r, uint8_t g, uint8_t b, uint8_t a);
    void BlendBgraSpan(int32_t y, int32_t x0, int32_t x1,
                       const uint8_t* sourceBgra);
    void BlendPixelSubpixel(int32_t x, int32_t y,
                            uint8_t r, uint8_t g, uint8_t b,
                            uint8_t coverageR, uint8_t coverageG, uint8_t coverageB);
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
    // Per-corner radii (TL, TR, BR, BL).  rx/ry below kept for backward
    // compatibility with code paths that still set the symmetric variant —
    // those paths populate all four per-corner values too.
    float radiusTL = 0, radiusTR = 0, radiusBR = 0, radiusBL = 0;
    float rx = 0, ry = 0;
    // Set on the clipStack_ entry when this level also pushed a rounded-corner
    // entry onto roundedClipStack_ (so PopClip pops both in lockstep).
    bool ownsRounded = false;
    bool Contains(float px, float py) const {
        if (px < x || px >= x + w || py < y || py >= y + h) return false;

        // Cap each corner radius to half the smaller side so a single corner
        // can't eat into the opposite edge.
        const float halfMin = std::min(w, h) * 0.5f;
        const float rTL = std::min(radiusTL, halfMin);
        const float rTR = std::min(radiusTR, halfMin);
        const float rBR = std::min(radiusBR, halfMin);
        const float rBL = std::min(radiusBL, halfMin);
        if (rTL <= 0 && rTR <= 0 && rBR <= 0 && rBL <= 0) return true;

        float lx = px - x, ly = py - y;
        if (lx < rTL && ly < rTL && rTL > 0) {
            float dx = (lx - rTL) / rTL, dy = (ly - rTL) / rTL;
            return (dx * dx + dy * dy) <= 1.0f;
        }
        if (lx > w - rTR && ly < rTR && rTR > 0) {
            float dx = (lx - (w - rTR)) / rTR, dy = (ly - rTR) / rTR;
            return (dx * dx + dy * dy) <= 1.0f;
        }
        if (lx < rBL && ly > h - rBL && rBL > 0) {
            float dx = (lx - rBL) / rBL, dy = (ly - (h - rBL)) / rBL;
            return (dx * dx + dy * dy) <= 1.0f;
        }
        if (lx > w - rBR && ly > h - rBR && rBR > 0) {
            float dx = (lx - (w - rBR)) / rBR, dy = (ly - (h - rBR)) / rBR;
            return (dx * dx + dy * dy) <= 1.0f;
        }
        return true;
    }
};

// ============================================================================
// Render Target
// ============================================================================

class SoftwareRenderTarget : public RenderTarget, public CpuFramebufferStorageProvider {
public:
    SoftwareRenderTarget(SoftwareBackend* backend, int32_t width, int32_t height);
    ~SoftwareRenderTarget() override;

    JaliumResult Resize(int32_t width, int32_t height) override;
    JaliumResult BeginDraw() override;
    JaliumResult EndDraw() override;
    JaliumResult RequestReadback() override;
    JaliumResult FetchReadback(uint8_t* buf, uint32_t bufStride,
        int32_t* outWidth, int32_t* outHeight) override;
    JaliumResult QueryGpuStats(JaliumGpuStats* out) const override;
    JaliumResult ReclaimIdleResources() override;
    JaliumResult CompactIdleFramebufferStorage() override;
    JaliumResult QueryMainFramebufferOwnedBytes(uint64_t* outBytes) const override;

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
    void FillPath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule, int32_t edgeMode = -1) override;
    void StrokePath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin = 0, float miterLimit = 10.0f, int32_t lineCap = 0, const float* dashPattern = nullptr, uint32_t dashCount = 0, float dashOffset = 0.0f, int32_t edgeMode = -1) override;
    // Legacy binary-coverage scanline fill kept for EdgeMode.Aliased.
    void FillPathAliased(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule);
    // Legacy outline-polygon stroke kept for EdgeMode.Aliased.
    void StrokePathAliased(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin, float miterLimit, int32_t lineCap, const float* dashPattern, uint32_t dashCount, float dashOffset);
    void DrawContentBorder(float x, float y, float w, float h,
        float blRadius, float brRadius,
        Brush* fillBrush, Brush* strokeBrush, float strokeWidth) override;

    bool SupportsRetainedLayers() const override { return true; }
    void* RealizeLayerBegin(void* existingLayer, float x, float y, float w, float h) override;
    void RealizeLayerEnd(void* layer) override;
    void CompositeLayer(void* layer, float x, float y, float w, float h, float opacity) override;
    void DestroyRetainedLayer(void* layer) override;
    void RenderText(
        const wchar_t* text, uint32_t textLength,
        TextFormat* format,
        float x, float y, float w, float h,
        Brush* brush) override;
#ifdef JALIUM_HAS_TEXT_ENGINE
    // Resampling blit for glyph runs under a rotated / skewed / anisotropic
    // matrix: quads are in physical-resolution local space, R maps them onto
    // the screen around (originX, originY). LCD coverage degrades to grayscale.
    void RenderTransformedGlyphQuads(
        const std::vector<TextGlyphQuad>& quads,
        float originX, float originY,
        float r00, float r01, float r10, float r11,
        uint8_t textR, uint8_t textG, uint8_t textB, float textAlpha);
#endif
    void PushTransform(const float* matrix) override;
    void PopTransform() override;
    void PushClip(float x, float y, float w, float h) override;
    void PopClip() override;
    void PushRoundedRectClip(float x, float y, float w, float h, float rx, float ry) override;
    void PushPerCornerRoundedRectClip(float x, float y, float w, float h,
        float tl, float tr, float br, float bl) override;
    void PunchTransparentRect(float x, float y, float w, float h) override;
    void PushOpacity(float opacity) override;
    void PopOpacity() override;
    void SetShapeType(int type, float n) override;
    void SetVSyncEnabled(bool enabled) override;
    void SetDpi(float dpiX, float dpiY) override;
    void AddDirtyRect(float x, float y, float w, float h) override;
    void SetFullInvalidation() override;
    void DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity) override;
    void DrawVideoSurface(VideoSurface* surface, float x, float y, float w, float h,
                          float opacity, int scalingMode) override;
    void DrawBackdropFilter(
        float x, float y, float w, float h,
        const char* backdropFilter,
        const char* material,
        const char* materialTint,
        float tintOpacity,
        float blurRadius,
        float cornerRadiusTL, float cornerRadiusTR,
        float cornerRadiusBR, float cornerRadiusBL) override;
    // Full in-app backdrop material on the CPU: apron box blur (3-pass,
    // sigma-matched to the GPU Gaussian), the shared colour pipeline, tint
    // with alpha, hash grain, opacity and per-corner rounding.
    void DrawBackdropMaterial(const JaliumBackdropMaterialDesc& desc) override;
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
        float uvOffsetX, float uvOffsetY,
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
    void DrawShaderEffectFromSource(float x, float y, float w, float h,
        const char* hlslSource, const float* constants,
        uint32_t constantFloatCount) override;

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
    struct CompactFramebufferStorage {
        std::vector<uint8_t> prefixPixels;
        uint8_t suffixBgra[4] = {};
        int32_t suffixStartRow = 0;
        bool active = false;
    };

    JaliumResult MaterializeMainFramebuffer();
    bool HasActiveFramebufferCapture() const;
    uint64_t MainFramebufferOwnedBytes() const;
    void CopyMainFramebufferBytes(uint8_t* destination, size_t byteCount) const;
    void ResetCompactFramebufferStorage();
    size_t RetainedCaptureBufferPoolBytes() const;
    void CacheRetainedCaptureBuffer(
        size_t depth, SoftwareFramebuffer&& framebuffer);
    void ReleaseRetainedCaptureBufferPool();

    enum class PreparedPaintKind : uint8_t {
        Invalid,
        Solid,
        LinearGradient,
        RadialGradient,
    };

    struct PreparedPaint {
        PreparedPaintKind kind = PreparedPaintKind::Invalid;
        const SoftwareLinearGradientBrush* linear = nullptr;
        const SoftwareRadialGradientBrush* radial = nullptr;
        uint8_t r = 0, g = 0, b = 0, a = 0;
        uint32_t packedBgra = 0;

        bool IsValid() const { return kind != PreparedPaintKind::Invalid; }
        bool IsSolid() const { return kind == PreparedPaintKind::Solid; }
    };

    PreparedPaint PreparePaint(Brush* brush) const;
    void SamplePaint(const PreparedPaint& paint, float px, float py,
                     uint8_t& r, uint8_t& g, uint8_t& b, uint8_t& a) const;
    void CompositeSpan(int32_t y, int32_t x0, int32_t x1,
                       const PreparedPaint& paint, uint8_t coverage = 255);
    bool TightenToRectClip(int32_t& x0, int32_t& y0,
                           int32_t& x1, int32_t& y1) const;
    bool TightenSpanToRoundedClips(int32_t y, int32_t& x0, int32_t& x1) const;
    // Text rendering sub-methods (dispatched from RenderText)
#ifdef JALIUM_HAS_TEXT_ENGINE
    void RenderTextWithGlyphAtlas(const wchar_t* text, uint32_t textLength,
        JaliumTextFormat* ftFormat, float x, float y, float w, float h,
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

    // Sub-pixel + 4x4-supersampled coverage rasterizer shared by the shape
    // primitives. The float device-space origin is fed through unchanged (no
    // (int) truncation), so an animated/fractional transform origin renders at
    // its true sub-pixel position instead of snapping to a whole pixel; the
    // fractional coverage doubles as edge anti-aliasing. The predicate receives
    // each sub-sample in shape-local coordinates (sample center minus origin).
    template <typename InsidePred>
    void RasterizeCoverageAA(float devOriginX, float devOriginY,
        float localMinX, float localMinY, float localMaxX, float localMaxY,
        Brush* brush, InsidePred inside);

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
    void BoxBlur(std::vector<uint8_t>& pixels, int32_t w, int32_t h, int32_t radius);

    // Helper: copy a region from framebuffer to a separate buffer
    void CopyRegion(const SoftwareFramebuffer& src, SoftwareFramebuffer& dst,
        int32_t srcX, int32_t srcY, int32_t w, int32_t h);

    // Helper: restore a saved region byte-for-byte (including transparent
    // pixels), without SrcOver blending.
    void RestoreRegion(const SoftwareFramebuffer& src, int32_t dstX, int32_t dstY);
    void RestoreRawRegion(const uint8_t* pixels, int32_t width, int32_t height,
        int32_t dstX, int32_t dstY);

    // Helper: blit a buffer onto framebuffer with alpha
    void BlitBuffer(const SoftwareFramebuffer& src, int32_t dstX, int32_t dstY, float opacity = 1.0f);
    void BlitRawBuffer(const uint8_t* pixels, int32_t width, int32_t height,
        int32_t dstX, int32_t dstY, float opacity = 1.0f);

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
    CompactFramebufferStorage compactFramebuffer_;
    bool isDrawing_ = false;
    std::stack<SoftwareTransform> transformStack_;
    std::stack<SoftwareClipRect> clipStack_;
    // Rounded-corner clip levels, in their own (untrimmed) rectangles. The
    // clipStack_ intersection cannot carry an ancestor's corner rounding, so
    // IsClipped tests every live rounded level here in addition to the top
    // rectangle intersection. Entries pair with the clipStack_ level whose
    // ownsRounded flag is set.
    std::vector<SoftwareClipRect> roundedClipStack_;
    std::stack<float> opacityStack_;
    SoftwareTransform currentTransform_;
    float currentOpacity_ = 1.0f;
    float dpiX_ = 96.0f;
    float dpiY_ = 96.0f;
    float scaleX_ = 1.0f;
    float scaleY_ = 1.0f;
    bool fullInvalidation_ = true;
    bool hasDirtyRect_ = false;
    int32_t dirtyLeft_ = 0;
    int32_t dirtyTop_ = 0;
    int32_t dirtyRight_ = 0;
    int32_t dirtyBottom_ = 0;

    // Effect capture state. Captures can nest when an effected element is
    // rendered inside another effected element, so each open scope owns its
    // framebuffer snapshot and capture bounds.
    struct EffectCaptureState {
        SoftwareFramebuffer savedRegion;
        float x = 0;
        float y = 0;
        float width = 0;
        float height = 0;
        int32_t pixelX = 0;
        int32_t pixelY = 0;
    };

    SoftwareFramebuffer effectCaptureFb_;
    std::vector<EffectCaptureState> effectCaptureStack_;
    float lastEffectCaptureX_ = 0, lastEffectCaptureY_ = 0;
    float lastEffectCaptureW_ = 0, lastEffectCaptureH_ = 0;
    bool effectCaptureReady_ = false;

    // Transition capture state
    SoftwareFramebuffer transitionCaptureFb_[2];
    float transitionX_[2] = {}, transitionY_[2] = {};
    float transitionW_[2] = {}, transitionH_[2] = {};
    bool transitionCaptureActive_[2] = {};

    // Desktop capture state
    SoftwareFramebuffer desktopCaptureFb_;

    // One-shot frame readback. RequestReadback arms the next EndDraw; EndDraw
    // snapshots the CPU framebuffer before presentation so FetchReadback has
    // exactly the same BGRA8/top-down contract as the GPU backends.
    SoftwareFramebuffer readbackFb_;
    bool readbackPending_ = false;
    bool readbackReady_ = false;

    struct RetainedCaptureState {
        SoftwareFramebuffer savedFramebuffer;
        std::stack<SoftwareClipRect> savedClips;
        std::vector<SoftwareClipRect> savedRoundedClips;
        std::vector<uint8_t> capturedPixels;
        SoftwareRetainedLayer* layer = nullptr;
        int32_t x = 0, y = 0, width = 0, height = 0;
    };
    std::vector<std::unique_ptr<SoftwareRetainedLayer>> retainedLayers_;
    std::vector<RetainedCaptureState> retainedCaptureStack_;
    std::vector<SoftwareFramebuffer> retainedCaptureBufferPool_;
    size_t retainedLayerBytes_ = 0;

    std::unique_ptr<SoftwareEllipseMaskCache> ellipseFillMaskCache_;
    std::unique_ptr<SoftwareEllipseMaskCache> ellipseStrokeMaskCache_;
    std::unique_ptr<SoftwareRoundedRectMaskCache> roundedRectMaskCache_;
    std::unique_ptr<SoftwareEllipticalRoundedRectMaskCache> ellipticalRoundedRectMaskCache_;
    std::unique_ptr<SoftwarePathRasterCache> pathRasterCache_;
    std::unique_ptr<SoftwareLineRasterCache> lineRasterCache_;
    std::unique_ptr<SoftwareTextMaskCache> textMaskCache_;
    std::unique_ptr<SoftwareTextCompositeCache> textCompositeCache_;
    std::unique_ptr<SoftwareBackdropCache> backdropCache_;
    std::unique_ptr<SoftwareEffectResultCache> gradientCompositeCache_;
    std::unique_ptr<SoftwareEffectResultCache> bitmapCompositeCache_;
    std::unique_ptr<SoftwareEffectResultCache> effectResultCache_;
    std::unique_ptr<SoftwareEffectResultCache> liquidGlassCache_;
    std::vector<uint8_t> blurScratch_;

    uint64_t frameStartNs_ = 0;
    uint64_t frameParallelStartNs_ = 0;
    uint64_t lastRasterNs_ = 0;
    uint64_t lastParallelNs_ = 0;
    mutable uint64_t framePixelsVisited_ = 0;
    mutable uint64_t framePixelsBlended_ = 0;
    mutable uint64_t frameAaSamples_ = 0;
    mutable uint64_t frameClipRejectedPixels_ = 0;

    // Platform-neutral surface descriptor for non-Windows present
    JaliumSurfaceDescriptor surfaceDescriptor_{};

#ifdef __ANDROID__
    // Android surface bring-up diagnostics: the first few EndDraw presents
    // log unconditionally, then go quiet. Per render target — a rebuilt RT is
    // a fresh instance, so every surface bring-up gets its own burst; the
    // former function-level static spent the budget once per process and left
    // later RTs (backend fallback, surface recreation) with no diagnostics.
    uint32_t presentLogFramesLeft_ = 5;
#endif

#ifdef JALIUM_SOFTWARE_WAYLAND_PRESENT
    std::unique_ptr<WaylandShmPresenter> waylandPresenter_;
#endif
#ifdef JALIUM_SOFTWARE_X11_PRESENT
    std::unique_ptr<X11SoftwarePresenter> x11Presenter_;
#endif

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
    VideoSurface* CreateVideoSurface(uint32_t width, uint32_t height,
                                     uint32_t formatHint) override;

    SoftwareWorkerPool* GetWorkerPool() const { return workerPool_.get(); }

#ifdef JALIUM_HAS_TEXT_ENGINE
    TextEngine* GetTextEngine() const { return textEngine_.get(); }
#else
    void* GetTextEngine() const { return nullptr; }
#endif

private:
    std::unique_ptr<SoftwareWorkerPool> workerPool_;
#ifdef JALIUM_HAS_TEXT_ENGINE
    std::unique_ptr<TextEngine> textEngine_;
#endif
};

IRenderBackend* CreateSoftwareBackend();

} // namespace jalium
