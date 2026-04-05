#pragma once

#include "jalium_backend.h"
#include "jalium_types.h"

#include <memory>
#include <vector>

namespace jalium {

class VulkanBackend;

class VulkanRenderTarget : public RenderTarget {
public:
    VulkanRenderTarget(
        VulkanBackend* backend,
        const JaliumSurfaceDescriptor& surface,
        int32_t width,
        int32_t height,
        bool useComposition);

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
    void DrawContentBorder(float x, float y, float w, float h, float blRadius, float brRadius, Brush* fillBrush, Brush* strokeBrush, float strokeWidth) override;
    void RenderText(const wchar_t* text, uint32_t textLength, TextFormat* format, float x, float y, float w, float h, Brush* brush) override;
    void PushTransform(const float* matrix) override;
    void PopTransform() override;
    void PushClip(float x, float y, float w, float h) override;
    void PushRoundedRectClip(float x, float y, float w, float h, float rx, float ry) override;
    void PopClip() override;
    void PunchTransparentRect(float x, float y, float w, float h) override;
    void PushOpacity(float opacity) override;
    void PopOpacity() override;
    void SetShapeType(int type, float n) override;
    void SetVSyncEnabled(bool enabled) override;
    void SetDpi(float dpiX, float dpiY) override;
    void AddDirtyRect(float x, float y, float w, float h) override;
    void SetFullInvalidation() override;
    void DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity) override;
    void DrawBackdropFilter(float x, float y, float w, float h, const char* backdropFilter, const char* material, const char* materialTint, float tintOpacity, float blurRadius, float cornerRadiusTL, float cornerRadiusTR, float cornerRadiusBR, float cornerRadiusBL) override;
    void DrawGlowingBorderHighlight(float x, float y, float w, float h, float animationPhase, float glowColorR, float glowColorG, float glowColorB, float strokeWidth, float trailLength, float dimOpacity, float screenWidth, float screenHeight) override;
    void DrawGlowingBorderTransition(float fromX, float fromY, float fromW, float fromH, float toX, float toY, float toW, float toH, float headProgress, float tailProgress, float animationPhase, float glowColorR, float glowColorG, float glowColorB, float strokeWidth, float trailLength, float dimOpacity, float screenWidth, float screenHeight) override;
    void DrawRippleEffect(float x, float y, float w, float h, float rippleProgress, float glowColorR, float glowColorG, float glowColorB, float strokeWidth, float dimOpacity, float screenWidth, float screenHeight) override;
    void CaptureDesktopArea(int32_t screenX, int32_t screenY, int32_t width, int32_t height) override;
    void DrawDesktopBackdrop(float x, float y, float w, float h, float blurRadius, float tintR, float tintG, float tintB, float tintOpacity, float noiseIntensity, float saturation) override;
    void BeginTransitionCapture(int slot, float x, float y, float w, float h) override;
    void EndTransitionCapture(int slot) override;
    void DrawTransitionShader(float x, float y, float w, float h, float progress, int mode) override;
    void DrawCapturedTransition(int slot, float x, float y, float w, float h, float opacity) override;
    void BeginEffectCapture(float x, float y, float w, float h) override;
    void EndEffectCapture() override;
    void DrawBlurEffect(float x, float y, float w, float h, float radius, float uvOffsetX = 0, float uvOffsetY = 0) override;
    void DrawDropShadowEffect(float x, float y, float w, float h, float blurRadius, float offsetX, float offsetY, float r, float g, float b, float a, float uvOffsetX = 0, float uvOffsetY = 0, float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0) override;
    void DrawShaderEffect(float x, float y, float w, float h,
        const uint8_t* shaderBytecode, uint32_t shaderBytecodeSize,
        const float* constants, uint32_t constantFloatCount) override;
    void DrawLiquidGlass(float x, float y, float w, float h, float cornerRadius, float blurRadius, float refractionAmount, float chromaticAberration, float tintR, float tintG, float tintB, float tintOpacity, float lightX, float lightY, float highlightBoost, int shapeType, float shapeExponent, int neighborCount, float fusionRadius, const float* neighborData) override;

private:
    struct CpuTransform {
        float m11 = 1.0f;
        float m12 = 0.0f;
        float m21 = 0.0f;
        float m22 = 1.0f;
        float dx = 0.0f;
        float dy = 0.0f;
    };

    struct ClipState {
        bool rounded = false;
        float x = 0.0f;
        float y = 0.0f;
        float w = 0.0f;
        float h = 0.0f;
        float rx = 0.0f;
        float ry = 0.0f;
        CpuTransform transform {};
        CpuTransform inverseTransform {};
        bool hasInverse = true;
    };

    struct GpuSolidRectCommand {
        float x = 0.0f;
        float y = 0.0f;
        float w = 0.0f;
        float h = 0.0f;
        float r = 0.0f;
        float g = 0.0f;
        float b = 0.0f;
        float a = 1.0f;
    };

    struct GpuBitmapCommand {
        uint32_t pixelWidth = 0;
        uint32_t pixelHeight = 0;
        std::vector<uint8_t> pixels;
        float x = 0.0f;
        float y = 0.0f;
        float w = 0.0f;
        float h = 0.0f;
        float opacity = 1.0f;
    };

    struct GpuFilledPolygonCommand {
        std::vector<float> triangleVertices;
        float r = 0.0f;
        float g = 0.0f;
        float b = 0.0f;
        float a = 1.0f;
    };

    struct GpuBlurCommand {
        uint32_t pixelWidth = 0;
        uint32_t pixelHeight = 0;
        std::vector<uint8_t> pixels;
        float x = 0.0f;
        float y = 0.0f;
        float w = 0.0f;
        float h = 0.0f;
        float radius = 0.0f;
        float opacity = 1.0f;
        bool alphaOnlyTint = false;
        float tintR = 0.0f;
        float tintG = 0.0f;
        float tintB = 0.0f;
        float tintA = 1.0f;
    };

    struct GpuLiquidGlassCommand {
        uint32_t pixelWidth = 0;
        uint32_t pixelHeight = 0;
        std::vector<uint8_t> pixels;
        float x = 0.0f;
        float y = 0.0f;
        float w = 0.0f;
        float h = 0.0f;
        float cornerRadius = 0.0f;
        float blurRadius = 0.0f;
        float refractionAmount = 0.0f;
        float chromaticAberration = 0.0f;
        float tintR = 0.0f;
        float tintG = 0.0f;
        float tintB = 0.0f;
        float tintOpacity = 0.0f;
        float lightX = 0.0f;
        float lightY = 0.0f;
        float highlightBoost = 0.0f;
    };

    struct GpuBackdropCommand {
        uint32_t pixelWidth = 0;
        uint32_t pixelHeight = 0;
        std::vector<uint8_t> pixels;
        float x = 0.0f;
        float y = 0.0f;
        float w = 0.0f;
        float h = 0.0f;
        float blurRadius = 0.0f;
        float cornerRadiusTL = 0.0f;
        float cornerRadiusTR = 0.0f;
        float cornerRadiusBR = 0.0f;
        float cornerRadiusBL = 0.0f;
        float tintR = 0.0f;
        float tintG = 0.0f;
        float tintB = 0.0f;
        float tintOpacity = 0.0f;
        float saturation = 1.0f;
        float noiseIntensity = 0.0f;
    };

    struct GpuGlowCommand {
        float x = 0.0f;
        float y = 0.0f;
        float w = 0.0f;
        float h = 0.0f;
        float cornerRadius = 0.0f;
        float strokeWidth = 0.0f;
        float glowR = 0.0f;
        float glowG = 0.0f;
        float glowB = 0.0f;
        float glowA = 1.0f;
        float dimOpacity = 0.0f;
        float intensity = 1.0f;
    };

    struct GpuTransitionCommand {
        uint32_t pixelWidth = 0;
        uint32_t pixelHeight = 0;
        std::vector<uint8_t> fromPixels;
        std::vector<uint8_t> toPixels;
        float x = 0.0f;
        float y = 0.0f;
        float w = 0.0f;
        float h = 0.0f;
        float progress = 0.0f;
        float opacity = 1.0f;
        int mode = 0;
    };

    enum class GpuReplayCommandKind {
        SolidRect,
        ClearRect,
        Bitmap,
        FilledPolygon,
        Blur,
        Backdrop,
        Glow,
        LiquidGlass,
        Transition
    };

    struct GpuReplayCommand {
        GpuReplayCommandKind kind = GpuReplayCommandKind::SolidRect;
        bool hasScissor = false;
        int32_t scissorLeft = 0;
        int32_t scissorTop = 0;
        int32_t scissorRight = 0;
        int32_t scissorBottom = 0;
        bool hasRoundedClip = false;
        float roundedClipLeft = 0.0f;
        float roundedClipTop = 0.0f;
        float roundedClipRight = 0.0f;
        float roundedClipBottom = 0.0f;
        float roundedClipRadiusX = 0.0f;
        float roundedClipRadiusY = 0.0f;
        bool hasInnerRoundedClip = false;
        float innerRoundedClipLeft = 0.0f;
        float innerRoundedClipTop = 0.0f;
        float innerRoundedClipRight = 0.0f;
        float innerRoundedClipBottom = 0.0f;
        float innerRoundedClipRadiusX = 0.0f;
        float innerRoundedClipRadiusY = 0.0f;
        bool hasCustomQuad = false;
        float quadPoint0X = 0.0f;
        float quadPoint0Y = 0.0f;
        float quadPoint1X = 0.0f;
        float quadPoint1Y = 0.0f;
        float quadPoint2X = 0.0f;
        float quadPoint2Y = 0.0f;
        float quadPoint3X = 0.0f;
        float quadPoint3Y = 0.0f;
        GpuSolidRectCommand solidRect {};
        GpuBitmapCommand bitmap {};
        GpuFilledPolygonCommand filledPolygon {};
        GpuBlurCommand blur {};
        GpuBackdropCommand backdrop {};
        GpuGlowCommand glow {};
        GpuLiquidGlassCommand liquidGlass {};
        GpuTransitionCommand transition {};
    };

    struct EffectCaptureState {
        std::vector<uint8_t> savedPixels;
        std::vector<GpuReplayCommand> savedReplayCommands;
        bool savedReplaySupported = false;
        bool savedReplayHasClear = false;
        float x = 0.0f;
        float y = 0.0f;
        float w = 0.0f;
        float h = 0.0f;
    };

    struct TransitionCaptureState {
        std::vector<uint8_t> pixels;
        bool valid = false;
    };

    class Impl;
    CpuTransform GetCurrentTransform() const;
    float GetCurrentOpacity() const;
    static CpuTransform MultiplyTransforms(const CpuTransform& left, const CpuTransform& right);
    static bool TryInvertTransform(const CpuTransform& transform, CpuTransform& inverse);
    static void ApplyTransform(const CpuTransform& transform, float x, float y, float& outX, float& outY);
    bool IsInsideClip(float x, float y) const;
    void RasterizePolygon(const std::vector<float>& points, int fillRule, uint8_t b, uint8_t g, uint8_t r, uint8_t a);
    void StrokePolyline(const std::vector<float>& points, bool closed, float strokeWidth, uint8_t b, uint8_t g, uint8_t r, uint8_t a);
    void ResizeCpuCanvas();
    void ClearCpuCanvas(uint8_t b, uint8_t g, uint8_t r, uint8_t a);
    void FillSolidRect(int left, int top, int right, int bottom, uint8_t b, uint8_t g, uint8_t r, uint8_t a);
    void BlendPixel(int x, int y, uint8_t b, uint8_t g, uint8_t r, uint8_t a);
    bool TryGetSolidBrushColor(Brush* brush, uint8_t& b, uint8_t& g, uint8_t& r, uint8_t& a) const;
    std::vector<uint8_t> BlurPixels(const std::vector<uint8_t>& source, int sourceWidth, int sourceHeight, int radius, float x, float y, float w, float h) const;
    void BlendBuffer(const std::vector<uint8_t>& source, int sourceWidth, int sourceHeight, float x, float y, float w, float h, float opacity);
    void PushTemporaryClip(float x, float y, float w, float h, float rx = 0.0f, float ry = 0.0f);
    void PopTemporaryClip();
    void ParseTintColor(const char* tint, float fallbackR, float fallbackG, float fallbackB, uint8_t& outB, uint8_t& outG, uint8_t& outR) const;
    void BlendOutsideRect(float x, float y, float w, float h, uint8_t b, uint8_t g, uint8_t r, uint8_t a);
    void StrokeRoundedRectApprox(float x, float y, float w, float h, float rx, float ry, float strokeWidth, uint8_t b, uint8_t g, uint8_t r, uint8_t a);
    void ResetGpuReplay();
    void InvalidateGpuReplay();
    void ResetGpuSolidRectReplay() { ResetGpuReplay(); }
    void InvalidateGpuSolidRectReplay() { InvalidateGpuReplay(); }
    bool TryPopulateReplayClip(GpuReplayCommand& command) const;
    bool TryRecordGpuClearRectCommand(float x, float y, float w, float h);
    bool TryRecordGpuPixelBufferCommand(const std::vector<uint8_t>& pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float opacity);
    bool TryRecordGpuBlurCommand(const std::vector<uint8_t>& pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float radius, float opacity, bool alphaOnlyTint = false, float tintR = 0.0f, float tintG = 0.0f, float tintB = 0.0f, float tintA = 1.0f);
    bool TryRecordGpuBackdropCommand(const std::vector<uint8_t>& pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float blurRadius, float cornerRadiusTL, float cornerRadiusTR, float cornerRadiusBR, float cornerRadiusBL, float tintR, float tintG, float tintB, float tintOpacity, float saturation = 1.0f, float noiseIntensity = 0.0f);
    bool TryRecordGpuGlowCommand(float x, float y, float w, float h, float cornerRadius, float strokeWidth, float glowR, float glowG, float glowB, float glowA, float dimOpacity, float intensity);
    bool TryRecordGpuLiquidGlassCommand(const std::vector<uint8_t>& pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float cornerRadius, float blurRadius, float refractionAmount, float chromaticAberration, float tintR, float tintG, float tintB, float tintOpacity, float lightX, float lightY, float highlightBoost);
    bool TryRecordGpuDimOutsideRectCommand(float x, float y, float w, float h, uint8_t b, uint8_t g, uint8_t r, uint8_t a);
    bool TryRecordGpuFilledPolygonCommand(const std::vector<float>& points, int32_t fillRule, Brush* brush);
    bool TryRecordGpuTransitionCommand(const std::vector<uint8_t>& fromPixels, const std::vector<uint8_t>& toPixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float progress, int mode);
    bool TryRecordGpuSolidRectCommand(float x, float y, float w, float h, Brush* brush);
    bool TryRecordGpuRoundedRectFillCommand(float x, float y, float w, float h, float rx, float ry, Brush* brush);
    bool TryRecordGpuRoundedRectStrokeCommand(float x, float y, float w, float h, float rx, float ry, float strokeWidth, Brush* brush);
    bool TryRecordGpuEllipseFillCommand(float cx, float cy, float rx, float ry, Brush* brush);
    bool TryRecordGpuEllipseStrokeCommand(float cx, float cy, float rx, float ry, float strokeWidth, Brush* brush);
    bool TryRecordGpuLineCommand(float x1, float y1, float x2, float y2, float strokeWidth, Brush* brush);
    bool TryRecordGpuPolylineCommand(const std::vector<float>& points, bool closed, float strokeWidth, Brush* brush);
    bool TryRecordGpuRectangleStrokeCommand(float x, float y, float w, float h, float strokeWidth, Brush* brush);
    bool TryRecordGpuBitmapCommand(Bitmap* bitmap, float x, float y, float w, float h, float opacity);
    void TouchFrame() const;

    VulkanBackend* backend_ = nullptr;
    JaliumSurfaceDescriptor surface_{};
    bool isComposition_ = false;
    bool isDrawing_ = false;
    bool fullInvalidation_ = true;
    float clearColor_[4] = { 0.0f, 0.0f, 0.0f, 0.0f };
    float dpiX_ = 96.0f;
    float dpiY_ = 96.0f;
    std::vector<JaliumRect> dirtyRects_;
    std::vector<uint8_t> pixelBuffer_;
    std::vector<uint8_t> desktopCapturePixels_;
    int32_t desktopCaptureWidth_ = 0;
    int32_t desktopCaptureHeight_ = 0;
    bool desktopCaptureValid_ = false;
    std::vector<uint8_t> lastCapturedPixels_;
    float lastCaptureX_ = 0.0f;
    float lastCaptureY_ = 0.0f;
    float lastCaptureW_ = 0.0f;
    float lastCaptureH_ = 0.0f;
    std::vector<uint8_t> transitionSavedPixels_;
    std::vector<GpuReplayCommand> transitionSavedReplayCommands_;
    bool transitionSavedReplaySupported_ = false;
    bool transitionSavedReplayHasClear_ = false;
    int activeTransitionSlot_ = -1;
    TransitionCaptureState transitionSlots_[2];
    std::vector<CpuTransform> transformStack_;
    std::vector<float> opacityStack_;
    std::vector<ClipState> clipStack_;
    std::vector<EffectCaptureState> effectCaptureStack_;
    bool gpuReplaySupported_ = false;
    bool gpuReplayHasClear_ = false;
    std::vector<GpuReplayCommand> gpuReplayCommands_;
    std::unique_ptr<Impl> impl_;
};

} // namespace jalium
