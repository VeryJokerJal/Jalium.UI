#pragma once

#include "jalium_backend.h"
#include "d3d12_backend.h"
#include "d3d12_direct_renderer.h"
#include <dcomp.h>
#include <stack>
#include <unordered_map>
#include <vector>
#include <memory>

namespace jalium {

/// D3D12 render target implementation.
/// Uses D3D12DirectRenderer for pure D3D12 instanced rendering (SDF rects,
/// glyph atlas text, bitmap quads, triangle fill).  No D2D/D3D11on12 bridge.
/// When useComposition=true, uses CreateSwapChainForComposition + DirectComposition
/// for per-pixel alpha transparency (used by popup windows).
class D3D12RenderTarget : public RenderTarget {
public:
    D3D12RenderTarget(D3D12Backend* backend, void* hwnd, int32_t width, int32_t height, bool useComposition = false);
    ~D3D12RenderTarget() override;

    /// Initializes the render target (swap chain, DirectRenderer, DComp if needed).
    bool Initialize();

    // RenderTarget implementation
    JaliumResult Resize(int32_t width, int32_t height) override;
    JaliumResult BeginDraw() override;
    JaliumResult EndDraw() override;
    JaliumResult CreateWebViewVisual(void** visualOut) override;
    JaliumResult DestroyWebViewVisual(void* visual) override;
    JaliumResult SetWebViewVisualPlacement(
        void* visual,
        int32_t x, int32_t y, int32_t width, int32_t height,
        int32_t contentOffsetX, int32_t contentOffsetY) override;
    void Clear(float r, float g, float b, float a) override;

    void FillRectangle(float x, float y, float w, float h, Brush* brush) override;
    void DrawRectangle(float x, float y, float w, float h, Brush* brush, float strokeWidth) override;
    void FillRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush) override;
    void DrawRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush, float strokeWidth) override;
    void FillEllipse(float cx, float cy, float rx, float ry, Brush* brush) override;
    void FillEllipseBatch(const float* data, uint32_t count) override;
    void DrawEllipse(float cx, float cy, float rx, float ry, Brush* brush, float strokeWidth) override;
    void DrawLine(float x1, float y1, float x2, float y2, Brush* brush, float strokeWidth) override;
    void FillPolygon(const float* points, uint32_t pointCount, Brush* brush, int32_t fillRule) override;
    void DrawPolygon(const float* points, uint32_t pointCount, Brush* brush, float strokeWidth, bool closed,
        int32_t lineJoin = 0, float miterLimit = 10.0f) override;
    void FillPath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule) override;
    void StrokePath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed,
        int32_t lineJoin = 0, float miterLimit = 10.0f, int32_t lineCap = 0) override;
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
    void PushClipAliased(float x, float y, float w, float h) override;
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
    void DrawBackdropFilter(
        float x, float y, float w, float h,
        const char* backdropFilter, const char* material, const char* materialTint,
        float tintOpacity, float blurRadius,
        float cornerRadiusTL, float cornerRadiusTR,
        float cornerRadiusBR, float cornerRadiusBL) override;

    void DrawGlowingBorderHighlight(
        float x, float y, float w, float h,
        float animationPhase,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth, float trailLength, float dimOpacity,
        float screenWidth, float screenHeight) override;

    void DrawGlowingBorderTransition(
        float fromX, float fromY, float fromW, float fromH,
        float toX, float toY, float toW, float toH,
        float headProgress, float tailProgress,
        float animationPhase,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth, float trailLength, float dimOpacity,
        float screenWidth, float screenHeight) override;

    void DrawRippleEffect(
        float x, float y, float w, float h,
        float rippleProgress,
        float glowColorR, float glowColorG, float glowColorB,
        float strokeWidth, float dimOpacity,
        float screenWidth, float screenHeight) override;

    void DrawLiquidGlass(
        float x, float y, float w, float h,
        float cornerRadius, float blurRadius,
        float refractionAmount, float chromaticAberration,
        float tintR, float tintG, float tintB, float tintOpacity,
        float lightX, float lightY, float highlightBoost,
        int shapeType, float shapeExponent,
        int neighborCount, float fusionRadius,
        const float* neighborData) override;

    void CaptureDesktopArea(int32_t screenX, int32_t screenY, int32_t width, int32_t height) override;
    void DrawDesktopBackdrop(
        float x, float y, float w, float h,
        float blurRadius,
        float tintR, float tintG, float tintB, float tintOpacity,
        float noiseIntensity, float saturation) override;

    void BeginTransitionCapture(int slot, float x, float y, float w, float h) override;
    void EndTransitionCapture(int slot) override;
    void DrawTransitionShader(float x, float y, float w, float h, float progress, int mode) override;
    void DrawCapturedTransition(int slot, float x, float y, float w, float h, float opacity) override;

    void BeginEffectCapture(float x, float y, float w, float h) override;
    void EndEffectCapture() override;
    void DrawBlurEffect(float x, float y, float w, float h, float radius,
        float uvOffsetX = 0, float uvOffsetY = 0) override;
    void DrawDropShadowEffect(float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY,
        float r, float g, float b, float a,
        float uvOffsetX = 0, float uvOffsetY = 0,
        float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0) override;
    void DrawColorMatrixEffect(float x, float y, float w, float h,
        const float* matrix) override;
    void DrawEmbossEffect(float x, float y, float w, float h,
        float amount, float lightDirX, float lightDirY, float relief) override;
    void DrawShaderEffect(float x, float y, float w, float h,
        const uint8_t* shaderBytecode, uint32_t shaderBytecodeSize,
        const float* constants, uint32_t constantFloatCount) override;

private:
    static constexpr uint32_t FrameCount = 3;

    bool CreateSwapChain();
    void WaitForAllFrames();

    // Brush → SdfRectInstance helpers
    bool FillBrushToInstance(Brush* brush, SdfRectInstance& inst);
    bool ExtractBrushColor(Brush* brush, float& r, float& g, float& b, float& a);

    D3D12Backend* backend_;
    HWND hwnd_;

    // Swap chain
    ComPtr<IDXGISwapChain3> swapChain_;
    uint32_t frameIndex_ = 0;

    // Synchronization (used only for Resize/Shutdown — per-frame sync is in DirectRenderer)
    ComPtr<ID3D12Fence> fence_;
    uint64_t fenceValues_[FrameCount] = {};
    HANDLE fenceEvent_ = nullptr;

    // Pure D3D12 direct renderer (owns command lists, RTVs, PSOs, etc.)
    std::unique_ptr<D3D12DirectRenderer> directRenderer_;

    bool isDrawing_ = false;
    bool lastEffectCaptureOk_ = false;  // tracks whether BeginEffectCapture succeeded
    bool tearingSupported_ = false;
    bool isComposition_ = false;
    bool vsyncEnabled_ = false;

    // Opacity stack (DirectRenderer only has SetOpacity, so we manage the stack here)
    std::stack<float> opacityStack_;

    // Actual swap chain creation flags (tracked for correct ResizeBuffers calls)
    UINT swapChainCreationFlags_ = 0;

    // DirectComposition resources (used when isComposition_ == true)
    ComPtr<IDCompositionDevice> dcompDevice_;
    ComPtr<IDCompositionTarget> dcompTarget_;
    ComPtr<IDCompositionVisual> dcompVisual_;
    ComPtr<IDCompositionVisual> dcompSwapChainVisual_;

    struct WebViewVisualEntry {
        ComPtr<IDCompositionVisual> containerVisual;
        ComPtr<IDCompositionVisual> targetVisual;
    };
    std::unordered_map<IDCompositionVisual*, WebViewVisualEntry> webViewVisuals_;

    // Dirty rect tracking
    struct DirtyRect { float x, y, w, h; };
    std::vector<DirtyRect> dirtyRects_;
    bool fullInvalidation_ = true;
    static constexpr size_t MaxDirtyRects = 8;
    static constexpr float DirtyRectMargin = 2.0f;

    // DPI
    float dpiX_ = 96.0f;
    float dpiY_ = 96.0f;

    // Clear color (latched in Clear(), applied in BeginDraw)
    float clearR_ = 0, clearG_ = 0, clearB_ = 0, clearA_ = 1;

    // Pre-glass snapshot flag for fused liquid glass panels
    bool preGlassSnapshotCaptured_ = false;

    // sRGB to linear conversion for Clear() color values
    static float SrgbToLinear(float s) {
        return (s <= 0.04045f) ? s / 12.92f : std::pow((s + 0.055f) / 1.055f, 2.4f);
    }
};

} // namespace jalium
