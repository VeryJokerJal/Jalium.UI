#pragma once

#include "jalium_backend.h"

#include <cstdint>
#include <memory>
#include <vector>

namespace jalium {

// Metal objects stay behind PImpl boundaries so this header is valid C++ on
// every host. Apple translation units compile as Objective-C++ and own all
// retain/release rules.

class MetalSolidBrush final : public Brush {
public:
    float r, g, b, a;
    MetalSolidBrush(float red, float green, float blue, float alpha)
        : r(red), g(green), b(blue), a(alpha) {}
    JaliumBrushType GetType() const override { return JALIUM_BRUSH_SOLID; }
};

class MetalLinearGradientBrush final : public Brush {
public:
    float startX, startY, endX, endY;
    uint32_t spreadMethod;
    std::vector<JaliumGradientStop> stops;

    MetalLinearGradientBrush(float sx, float sy, float ex, float ey,
        const JaliumGradientStop* values, uint32_t count, uint32_t spread);
    JaliumBrushType GetType() const override { return JALIUM_BRUSH_LINEAR_GRADIENT; }
};

class MetalRadialGradientBrush final : public Brush {
public:
    float centerX, centerY, radiusX, radiusY, originX, originY;
    uint32_t spreadMethod;
    std::vector<JaliumGradientStop> stops;

    MetalRadialGradientBrush(float cx, float cy, float rx, float ry,
        float ox, float oy, const JaliumGradientStop* values, uint32_t count,
        uint32_t spread);
    JaliumBrushType GetType() const override { return JALIUM_BRUSH_RADIAL_GRADIENT; }
};

class MetalTextFormat final : public TextFormat {
public:
    MetalTextFormat(const wchar_t* family, float size, int32_t weight, int32_t style);
    ~MetalTextFormat() override;

    bool IsValid() const;
    void SetAlignment(int32_t alignment) override;
    void SetParagraphAlignment(int32_t alignment) override;
    void SetTrimming(int32_t trimming) override;
    void SetWordWrapping(int32_t wrapping) override;
    void SetLineSpacing(int32_t method, float spacing, float baseline) override;
    void SetMaxLines(uint32_t maxLines) override;
    JaliumResult MeasureText(const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight, JaliumTextMetrics* metrics) override;
    JaliumResult GetFontMetrics(JaliumTextMetrics* metrics) override;
    JaliumResult HitTestPoint(const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight, float pointX, float pointY,
        JaliumTextHitTestResult* result) override;
    JaliumResult HitTestTextPosition(const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight, uint32_t textPosition,
        int32_t isTrailingHit, JaliumTextHitTestResult* result) override;

private:
    bool Rasterize(const wchar_t* text, uint32_t textLength,
        float x, float y, float width, float height,
        const float* deviceTransform,
        float clipX, float clipY, float clipWidth, float clipHeight,
        float r, float g, float b, float a,
        std::vector<uint8_t>& pixels, uint32_t& pixelWidth,
        uint32_t& pixelHeight, float& deviceX, float& deviceY) const;
    struct Impl;
    std::unique_ptr<Impl> impl_;
    friend class MetalRenderTarget;
};

class MetalBitmap final : public Bitmap {
public:
    MetalBitmap(uint32_t width, uint32_t height, std::vector<uint8_t>&& bgraPixels);
    ~MetalBitmap() override;
    uint32_t GetWidth() const override;
    uint32_t GetHeight() const override;

private:
    void* EnsureTexture(void* deviceHandle);
    struct Impl;
    std::unique_ptr<Impl> impl_;
    friend class MetalRenderTarget;
};

class MetalVideoSurface final : public VideoSurface {
public:
    MetalVideoSurface(void* device, uint32_t width, uint32_t height, uint32_t formatHint);
    MetalVideoSurface(void* device, const JaliumVideoSurfaceDescriptor& descriptor);
    ~MetalVideoSurface() override;

    bool IsValid() const;
    uint32_t GetWidth() const override;
    uint32_t GetHeight() const override;
    JaliumVideoSurfaceKind GetKind() const override;
    bool Lock(uint8_t** outPtr, uint32_t* outStride) override;
    bool Unlock(const JaliumVideoSurfaceDirtyRect* dirty) override;

private:
    void* TextureHandle(uint32_t plane = 0) const;
    struct Impl;
    std::unique_ptr<Impl> impl_;
    friend class MetalRenderTarget;
};

class MetalBackend;

class MetalRenderTarget final : public RenderTarget {
public:
    MetalRenderTarget(MetalBackend* backend, int32_t width, int32_t height,
        bool composition);
    ~MetalRenderTarget() override;

    bool Initialize(const JaliumSurfaceDescriptor* surface);
    bool Initialize(void* nativeHandle);

    JaliumResult Resize(int32_t width, int32_t height) override;
    JaliumResult BeginDraw() override;
    JaliumResult EndDraw() override;
    JaliumResult CreateWebViewVisual(void** visualOut) override;
    JaliumResult DestroyWebViewVisual(void* visual) override;
    JaliumResult SetWebViewVisualPlacement(void* visual, int32_t x, int32_t y,
        int32_t width, int32_t height, int32_t contentOffsetX,
        int32_t contentOffsetY) override;
    JaliumResult CreateAnimProbe(int32_t x, int32_t y, int32_t width,
        int32_t height, float travelPx, float periodSec, uint32_t colorArgb,
        int32_t vertical, void** visualOut) override;
    JaliumResult DestroyAnimProbe(void* visual) override;

    void Clear(float r, float g, float b, float a) override;
    void FillRectangle(float x, float y, float w, float h, Brush* brush) override;
    void DrawRectangle(float x, float y, float w, float h, Brush* brush,
        float strokeWidth) override;
    void FillRoundedRectangle(float x, float y, float w, float h, float rx,
        float ry, Brush* brush) override;
    void DrawRoundedRectangle(float x, float y, float w, float h, float rx,
        float ry, Brush* brush, float strokeWidth) override;
    void FillPerCornerRoundedRectangle(float x, float y, float w, float h,
        float tl, float tr, float br, float bl, Brush* brush) override;
    void DrawPerCornerRoundedRectangle(float x, float y, float w, float h,
        float tl, float tr, float br, float bl, Brush* brush,
        float strokeWidth) override;
    void FillEllipse(float cx, float cy, float rx, float ry, Brush* brush) override;
    void FillEllipseBatch(const float* data, uint32_t count) override;
    void DrawEllipse(float cx, float cy, float rx, float ry, Brush* brush,
        float strokeWidth) override;
    void DrawLine(float x1, float y1, float x2, float y2, Brush* brush,
        float strokeWidth) override;
    void FillPolygon(const float* points, uint32_t pointCount, Brush* brush,
        int32_t fillRule) override;
    void DrawPolygon(const float* points, uint32_t pointCount, Brush* brush,
        float strokeWidth, bool closed, int32_t lineJoin = 0,
        float miterLimit = 10.0f) override;
    void FillPath(float startX, float startY, const float* commands,
        uint32_t commandLength, Brush* brush, int32_t fillRule,
        int32_t edgeMode = -1) override;
    void StrokePath(float startX, float startY, const float* commands,
        uint32_t commandLength, Brush* brush, float strokeWidth, bool closed,
        int32_t lineJoin = 0, float miterLimit = 10.0f, int32_t lineCap = 0,
        const float* dashPattern = nullptr, uint32_t dashCount = 0,
        float dashOffset = 0.0f, int32_t edgeMode = -1) override;
    void DrawContentBorder(float x, float y, float w, float h, float blRadius,
        float brRadius, Brush* fillBrush, Brush* strokeBrush,
        float strokeWidth) override;
    void RenderText(const wchar_t* text, uint32_t textLength, TextFormat* format,
        float x, float y, float w, float h, Brush* brush) override;

    void PushTransform(const float* matrix) override;
    void PopTransform() override;
    void PushClip(float x, float y, float w, float h) override;
    void PushClipAliased(float x, float y, float w, float h) override;
    void PushRoundedRectClip(float x, float y, float w, float h, float rx,
        float ry) override;
    void PushPerCornerRoundedRectClip(float x, float y, float w, float h,
        float tl, float tr, float br, float bl) override;
    void PushRoundedRectClipExclude(float x, float y, float w, float h,
        float rx, float ry) override;
    void PopClip() override;
    void PunchTransparentRect(float x, float y, float w, float h) override;
    void PushOpacity(float opacity) override;
    void PopOpacity() override;
    void SetShapeType(int type, float n) override;
    void SetVSyncEnabled(bool enabled) override;
    void SetExternalPresentPacing(bool enabled) override;
    void SetPathMsaaSampleCount(uint32_t sampleCount) override;
    void SetDpi(float dpiX, float dpiY) override;
    void AddDirtyRect(float x, float y, float w, float h) override;
    void SetFullInvalidation() override;
    bool SupportsPartialPresentation() const override { return true; }
    void DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h,
        float opacity) override;
    void DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h,
        float opacity, int scalingMode) override;
    void DrawVideoSurface(VideoSurface* surface, float x, float y, float w,
        float h, float opacity, int scalingMode) override;
    void BlitInkLayer(void* inkLayerBitmap, float dstX, float dstY,
        float opacity) override;

    bool SupportsRetainedLayers() const override;
    void* RealizeLayerBegin(void* existingLayer, float x, float y, float w,
        float h) override;
    void RealizeLayerEnd(void* layer) override;
    void CompositeLayer(void* layer, float x, float y, float w, float h,
        float opacity) override;
    void DestroyRetainedLayer(void* layer) override;

    void DrawBackdropFilter(float x, float y, float w, float h,
        const char* backdropFilter, const char* material,
        const char* materialTint, float tintOpacity, float blurRadius,
        float cornerRadiusTL, float cornerRadiusTR, float cornerRadiusBR,
        float cornerRadiusBL) override;
    void DrawBackdropFilterEx(float x, float y, float w, float h,
        const char* backdropFilter, const char* material,
        const char* materialTint, float tintOpacity, float blurRadius,
        float noiseIntensity, float saturation, float luminosity,
        float cornerRadiusTL, float cornerRadiusTR, float cornerRadiusBR,
        float cornerRadiusBL) override;
    void DrawBackdropMaterial(const JaliumBackdropMaterialDesc& desc) override;
    void DrawGlowingBorderHighlight(float x, float y, float w, float h,
        float animationPhase, float glowColorR, float glowColorG,
        float glowColorB, float strokeWidth, float trailLength,
        float dimOpacity, float screenWidth, float screenHeight) override;
    void DrawGlowingBorderTransition(float fromX, float fromY, float fromW,
        float fromH, float toX, float toY, float toW, float toH,
        float headProgress, float tailProgress, float animationPhase,
        float glowColorR, float glowColorG, float glowColorB, float strokeWidth,
        float trailLength, float dimOpacity, float screenWidth,
        float screenHeight) override;
    void DrawRippleEffect(float x, float y, float w, float h,
        float rippleProgress, float glowColorR, float glowColorG,
        float glowColorB, float strokeWidth, float dimOpacity,
        float screenWidth, float screenHeight) override;
    void CaptureDesktopArea(int32_t screenX, int32_t screenY, int32_t width,
        int32_t height) override;
    void DrawDesktopBackdrop(float x, float y, float w, float h,
        float blurRadius, float tintR, float tintG, float tintB,
        float tintOpacity, float noiseIntensity, float saturation) override;
    void BeginTransitionCapture(int slot, float x, float y, float w,
        float h) override;
    void EndTransitionCapture(int slot) override;
    void DrawTransitionShader(float x, float y, float w, float h,
        float progress, int mode, float cornerRadius) override;
    void DrawCapturedTransition(int slot, float x, float y, float w, float h,
        float opacity) override;
    void BeginEffectCapture(float x, float y, float w, float h) override;
    void EndEffectCapture() override;
    void DrawBlurEffect(float x, float y, float w, float h, float radius,
        float uvOffsetX = 0, float uvOffsetY = 0) override;
    void DrawDropShadowEffect(float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY, float r, float g,
        float b, float a, float uvOffsetX = 0, float uvOffsetY = 0,
        float cornerTL = 0, float cornerTR = 0, float cornerBR = 0,
        float cornerBL = 0) override;
    void DrawOuterGlowEffect(float x, float y, float w, float h,
        float glowSize, float r, float g, float b, float a, float intensity,
        float uvOffsetX, float uvOffsetY, float cornerTL, float cornerTR,
        float cornerBR, float cornerBL) override;
    void DrawInnerShadowEffect(float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY, float r, float g,
        float b, float a, float uvOffsetX, float uvOffsetY, float cornerTL,
        float cornerTR, float cornerBR, float cornerBL) override;
    void DrawColorMatrixEffect(float x, float y, float w, float h,
        const float* matrix) override;
    void DrawEmbossEffect(float x, float y, float w, float h, float amount,
        float lightDirX, float lightDirY, float relief) override;
    void DrawShaderEffect(float x, float y, float w, float h,
        const uint8_t* shaderBytecode, uint32_t shaderBytecodeSize,
        const float* constants, uint32_t constantFloatCount) override;
    void DrawShaderEffectFromSource(float x, float y, float w, float h,
        const char* hlslSource, const float* constants,
        uint32_t constantFloatCount) override;
    void DrawLiquidGlass(float x, float y, float w, float h,
        float cornerRadius, float blurRadius, float refractionAmount,
        float chromaticAberration, float tintR, float tintG, float tintB,
        float tintOpacity, float lightX, float lightY, float highlightBoost,
        int shapeType, float shapeExponent, int neighborCount,
        float fusionRadius, const float* neighborData) override;

    JaliumResult QueryGpuStats(JaliumGpuStats* out) const override;
    JaliumResult QueryGpuTiming(JaliumGpuTimingStats* out) const override;
    JaliumResult GetPresentInfo(JaliumPresentInfo* out) const override;
    JaliumResult SetRenderingEngine(JaliumRenderingEngine engine) override;
    JaliumResult ReclaimIdleResources() override;
    JaliumResult RequestReadback() override;
    JaliumResult FetchReadback(uint8_t* buf, uint32_t bufStride,
        int32_t* outWidth, int32_t* outHeight) override;
    bool DebugRemoveDevice() override;
    bool DebugGetRetainedDestroyCounts(uint64_t* orphaned,
        uint64_t* graveyard) override;
    uint64_t DebugDevicePointer() override;
    bool DebugInOffscreenCapture() override;

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

class MetalBackend final : public IRenderBackend {
public:
    MetalBackend();
    ~MetalBackend() override;
    bool Initialize();

    JaliumBackend GetType() const override { return JALIUM_BACKEND_METAL; }
    const wchar_t* GetName() const override { return L"Metal"; }
    JaliumResult CheckDeviceStatus() override;
    JaliumResult SetGpuPreference(JaliumGpuPreference preference) override;
    JaliumResult GetAdapterInfo(JaliumAdapterInfo* out) const override;
    RenderTarget* CreateRenderTarget(void* nativeHandle, int32_t width,
        int32_t height) override;
    RenderTarget* CreateRenderTargetForComposition(void* nativeHandle,
        int32_t width, int32_t height) override;
    RenderTarget* CreateRenderTargetForSurface(const JaliumSurfaceDescriptor* surface,
        int32_t width, int32_t height) override;
    RenderTarget* CreateRenderTargetForCompositionSurface(
        const JaliumSurfaceDescriptor* surface, int32_t width,
        int32_t height) override;
    Brush* CreateSolidBrush(float r, float g, float b, float a) override;
    Brush* CreateLinearGradientBrush(float startX, float startY, float endX,
        float endY, const JaliumGradientStop* stops, uint32_t stopCount,
        uint32_t spreadMethod = 0) override;
    Brush* CreateRadialGradientBrush(float centerX, float centerY,
        float radiusX, float radiusY, float originX, float originY,
        const JaliumGradientStop* stops, uint32_t stopCount,
        uint32_t spreadMethod = 0) override;
    TextFormat* CreateTextFormat(const wchar_t* fontFamily, float fontSize,
        int32_t fontWeight, int32_t fontStyle) override;
    Bitmap* CreateBitmapFromMemory(const uint8_t* data,
        uint32_t dataSize) override;
    Bitmap* CreateBitmapFromPixels(const uint8_t* pixels, uint32_t width,
        uint32_t height, uint32_t stride) override;
    VideoSurface* CreateVideoSurface(uint32_t width, uint32_t height,
        uint32_t formatHint) override;
    VideoSurface* WrapExternalVideoSurface(
        const JaliumVideoSurfaceDescriptor* descriptor) override;
    void* CreateInkLayerBitmap(uint32_t width, uint32_t height) override;
    void DestroyInkLayerBitmap(void* bitmap) override;
    int32_t ResizeInkLayerBitmap(void* bitmap, uint32_t width,
        uint32_t height) override;
    void ClearInkLayerBitmap(void* bitmap, float r, float g, float b,
        float a) override;
    void* CreateBrushShader(const char* shaderKey, const char* brushMainHlsl,
        int32_t blendMode) override;
    void DestroyBrushShader(void* shader) override;
    int32_t DispatchBrush(void* bitmap, void* shader, const void* strokePoints,
        uint32_t pointCount, const void* constants, const void* extraParams,
        uint32_t extraParamsSize) override;

    void* DeviceHandle() const;
    void* CommandQueueHandle() const;
    void NoteDeviceError(int64_t errorCode);

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
    friend class MetalRenderTarget;
};

IRenderBackend* CreateMetalBackend();

} // namespace jalium
