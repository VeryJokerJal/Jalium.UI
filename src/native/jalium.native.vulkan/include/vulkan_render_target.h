#pragma once

#include "jalium_backend.h"
#include "jalium_types.h"
#include "jalium_rendering_engine.h"
#include "vulkan_impeller_engine.h"
#include "vulkan_vello_engine.h"

#include <map>
#include <memory>
#include <string>
#include <tuple>
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

    ~VulkanRenderTarget() override;
    bool IsInitialized() const;
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

    /// Override: set rendering engine with hot-switch support.
    JaliumResult SetRenderingEngine(JaliumRenderingEngine engine) override {
        JaliumRenderingEngine resolved = ResolveRenderingEngine(engine, JALIUM_BACKEND_VULKAN);
        pendingEngine_ = resolved;
        if (!isDrawing_) {
            activeEngine_ = resolved;
        }
        return JALIUM_OK;
    }

    /// Returns true if the active engine is Impeller.
    bool IsImpellerActive() const { return activeEngine_ == JALIUM_ENGINE_IMPELLER; }

private:
    // Rendering engines (lazy-initialized)
    std::unique_ptr<ImpellerVulkanEngine> impellerEngine_;
    std::unique_ptr<VelloVulkanEngine> velloEngine_;

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
        // Either an owning pixel buffer (for one-shot bitmaps like the desktop
        // capture snapshot) OR a shared pointer into the text cache (hot path
        // for glyph rasterization — RenderText). Sharing skips a ~16 KB copy
        // per text primitive, which on Gallery's label-heavy pages was eating
        // ~70 ms of Render time per frame.
        std::vector<uint8_t> pixels;
        std::shared_ptr<const std::vector<uint8_t>> sharedPixels;
        const std::vector<uint8_t>& GetPixels() const {
            return sharedPixels ? *sharedPixels : pixels;
        }
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
    // Like TryGetSolidBrushColor but also accepts linear/radial gradient
    // brushes, collapsing them to their average stop color. The approximation
    // exists so the GPU replay pipeline doesn't have to bail out when a
    // gradient appears, which used to invalidate the entire frame's replay
    // path and force every other draw through the CPU upload fallback. Visual
    // fidelity is lost for gradients, but frame times drop by an order of
    // magnitude in UIs that sprinkle gradient accents on otherwise-solid
    // backgrounds (such as Gallery). A proper gradient shader would record a
    // dedicated gradient-rect command instead.
    bool TryGetApproximateBrushColor(Brush* brush, uint8_t& b, uint8_t& g, uint8_t& r, uint8_t& a) const;
    std::vector<uint8_t> BlurPixels(const std::vector<uint8_t>& source, int sourceWidth, int sourceHeight, int radius, float x, float y, float w, float h) const;
    void BlendBuffer(const std::vector<uint8_t>& source, int sourceWidth, int sourceHeight, float x, float y, float w, float h, float opacity);
    void PushTemporaryClip(float x, float y, float w, float h, float rx = 0.0f, float ry = 0.0f);
    void PopTemporaryClip();
    void ParseTintColor(const char* tint, float fallbackR, float fallbackG, float fallbackB, uint8_t& outB, uint8_t& outG, uint8_t& outR) const;
    void BlendOutsideRect(float x, float y, float w, float h, uint8_t b, uint8_t g, uint8_t r, uint8_t a);
    void StrokeRoundedRectApprox(float x, float y, float w, float h, float rx, float ry, float strokeWidth, uint8_t b, uint8_t g, uint8_t r, uint8_t a);
    void ResetGpuReplay();
    void InvalidateGpuReplay(const char* caller = nullptr);
    void ResetGpuSolidRectReplay() { ResetGpuReplay(); }
    void InvalidateGpuSolidRectReplay(const char* caller = nullptr) { InvalidateGpuReplay(caller); }
    void EnsureCpuRasterization();
    void ReplayCommandToCpu(const GpuReplayCommand& command);
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
    mutable bool cpuRasterNeeded_ = false;
    bool cpuRasterNeededLastFrame_ = false;

    // Rasterized-text cache. Windows RenderText used to call CreateDIBSection +
    // CreateFontW + DrawTextW on every frame, which dominated the Vulkan
    // backend's frame time (~150ms/frame in Gallery) because every static label
    // re-ran GDI. This cache keys on (text, font family, size, bitmap extents,
    // premultiplied BGRA color, draw flags) and stores the BGRA pixel payload
    // ready for DrawBitmap, so the GDI dance only runs the first time a given
    // string is drawn at a given size/color.
    //
    // Key members (ordered the same as operator< for std::tuple):
    //   [0] text as wstring
    //   [1] font family as wstring
    //   [2] fontHeight (rounded px, signed — matches GDI LOGFONT.lfHeight)
    //   [3] bitmapWidth
    //   [4] bitmapHeight
    //   [5] brush BGRA packed (b | g<<8 | r<<16 | a<<24)
    //   [6] drawFlags (alignment bits)
    using TextCacheKey = std::tuple<std::wstring, std::wstring, int, int, int, uint32_t, int>;
    struct TextCacheEntry {
        // Stored as shared_ptr so the bitmap command can alias it instead of
        // deep-copying 16 KB per text primitive. The cache owns a strong ref;
        // each recorded GpuReplayCommand holds a second strong ref until the
        // command list is cleared at the next frame boundary.
        std::shared_ptr<const std::vector<uint8_t>> pixels;
        int width = 0;
        int height = 0;
    };
    std::map<TextCacheKey, TextCacheEntry> textCache_;
    static constexpr size_t kMaxTextCacheEntries = 512;

    // Fast-path used by RenderText to emit a cached text bitmap straight into
    // the GPU replay command list, skipping both the VulkanBitmap wrapper
    // construction (which deep-copies the pixel vector) and the
    // TryRecordGpuPixelBufferCommand deep-copy. Owns a shared reference to
    // the text cache entry's pixel buffer so subsequent DrawReplayFrame reads
    // see the same bytes.
    void RecordCachedTextBitmap(std::shared_ptr<const std::vector<uint8_t>> pixels,
                                int width, int height, float x, float y);

    // Fallback used when a FillPath / FillPolygon / StrokePath / DrawPolygon
    // cannot be expressed as a GPU replay FilledPolygon command (for example:
    // self-intersecting paths, multiple subpaths, ear-clipping triangulation
    // failure, or non-axis-aligned transforms). The polygon/polyline gets
    // rasterized into a *local* BGRA buffer sized to its axis-aligned bbox,
    // then recorded as a GPU Bitmap command. Keeps the whole frame on the GPU
    // replay path (no InvalidateGpuReplay / CPU upload fallback) at the cost
    // of the rasterize step — for the typical PathIcon/IconElement this is a
    // few hundred pixels, well under 1 ms per primitive. The points are in
    // physical-pixel / world space; the helper does not re-apply the current
    // transform, because the caller already did when it built the point list.
    void RasterizePolygonToGpuBitmap(const std::vector<float>& worldPoints,
                                     int fillRule,
                                     uint8_t b, uint8_t g, uint8_t r, uint8_t a);
    void RasterizePolylineToGpuBitmap(const std::vector<float>& worldPoints,
                                      bool closed,
                                      float strokeWidth,
                                      uint8_t b, uint8_t g, uint8_t r, uint8_t a);

    std::unique_ptr<Impl> impl_;
};

} // namespace jalium
