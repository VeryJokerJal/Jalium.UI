#pragma once

#include "jalium_backend.h"
#include "d3d12_backend.h"
#include <dcomp.h>
#include <stack>
#include <vector>

namespace jalium {

/// D3D12 render target implementation.
/// Uses D2D1 DeviceContext for 2D rendering on a D3D12 swap chain.
/// When useComposition=true, uses CreateSwapChainForComposition + DirectComposition
/// for per-pixel alpha transparency (used by popup windows).
class D3D12RenderTarget : public RenderTarget {
public:
    D3D12RenderTarget(D3D12Backend* backend, void* hwnd, int32_t width, int32_t height, bool useComposition = false);
    ~D3D12RenderTarget() override;

    /// Initializes the render target.
    bool Initialize();

    // RenderTarget implementation
    JaliumResult Resize(int32_t width, int32_t height) override;
    JaliumResult BeginDraw() override;
    JaliumResult EndDraw() override;
    JaliumResult CreateWebViewVisual(void** visualOut) override;
    JaliumResult DestroyWebViewVisual(void* visual) override;
    void Clear(float r, float g, float b, float a) override;

    void FillRectangle(float x, float y, float w, float h, Brush* brush) override;
    void DrawRectangle(float x, float y, float w, float h, Brush* brush, float strokeWidth) override;
    void FillRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush) override;
    void DrawRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush, float strokeWidth) override;
    void FillEllipse(float cx, float cy, float rx, float ry, Brush* brush) override;
    void DrawEllipse(float cx, float cy, float rx, float ry, Brush* brush, float strokeWidth) override;
    void DrawLine(float x1, float y1, float x2, float y2, Brush* brush, float strokeWidth) override;
    void FillPolygon(const float* points, uint32_t pointCount, Brush* brush, int32_t fillRule) override;
    void DrawPolygon(const float* points, uint32_t pointCount, Brush* brush, float strokeWidth, bool closed) override;
    void FillPath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule) override;
    void StrokePath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed) override;
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
    void PushRoundedRectClip(float x, float y, float w, float h, float rx, float ry) override;
    void PopClip() override;
    void PushOpacity(float opacity) override;
    void PopOpacity() override;
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

    void DrawLiquidGlass(
        float x, float y, float w, float h,
        float cornerRadius,
        float blurRadius,
        float refractionAmount,
        float chromaticAberration,
        float tintR, float tintG, float tintB, float tintOpacity,
        float lightX, float lightY,
        float highlightBoost,
        int shapeType,
        float shapeExponent,
        int neighborCount,
        float fusionRadius,
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

    // Element effect capture & rendering
    void BeginEffectCapture(float x, float y, float w, float h) override;
    void EndEffectCapture() override;
    void DrawBlurEffect(float x, float y, float w, float h, float radius) override;
    void DrawDropShadowEffect(float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY,
        float r, float g, float b, float a) override;

private:
    static constexpr uint32_t FrameCount = 2;
    static constexpr uint32_t OffscreenResourceIdleFrames = 180;

    bool CreateSwapChain();
    bool CreateRenderTargetViews();
    bool CreateD2DRenderTarget();
    void WaitForGpu();
    void WaitForAllFrames();
    void MoveToNextFrame();

    // Helper to get D2D brush without using dynamic_cast (avoids cross-DLL RTTI issues)
    ID2D1Brush* GetD2DBrush(Brush* brush);

    D3D12Backend* backend_;
    HWND hwnd_;

    // Swap chain resources
    ComPtr<IDXGISwapChain3> swapChain_;
    ComPtr<ID3D12DescriptorHeap> rtvHeap_;
    uint32_t rtvDescriptorSize_ = 0;
    uint32_t frameIndex_ = 0;

    // Per-frame resources
    ComPtr<ID3D12Resource> renderTargets_[FrameCount];
    ComPtr<ID3D12CommandAllocator> commandAllocators_[FrameCount];
    ComPtr<ID3D11Resource> wrappedBackBuffers_[FrameCount];
    ComPtr<ID2D1Bitmap1> d2dRenderTargets_[FrameCount];

    // Command list
    ComPtr<ID3D12GraphicsCommandList> commandList_;

    // Synchronization
    ComPtr<ID3D12Fence> fence_;
    uint64_t fenceValues_[FrameCount] = {};
    HANDLE fenceEvent_ = nullptr;

    // D2D device context for drawing
    ComPtr<ID2D1DeviceContext2> d2dContext_;

    // Clip type for tracking push/pop pairs
    enum class ClipType { AxisAligned, RoundedRectLayer };

    // State stacks
    std::stack<D2D1_MATRIX_3X2_F> transformStack_;
    std::stack<float> opacityStack_;
    std::stack<ClipType> clipStack_;

    bool isDrawing_ = false;
    bool tearingSupported_ = false;
    bool isComposition_ = false;
    bool offscreenResourcesUsedThisFrame_ = false;
    uint32_t idleFramesWithoutOffscreenUse_ = 0;

    // DirectComposition resources (used when isComposition_ == true)
    ComPtr<IDCompositionDevice> dcompDevice_;
    ComPtr<IDCompositionTarget> dcompTarget_;
    ComPtr<IDCompositionVisual> dcompVisual_;          // Root container visual
    ComPtr<IDCompositionVisual> dcompSwapChainVisual_; // Visual that hosts the window swap chain content

    // Dirty rect tracking for partial rendering
    std::vector<D2D1_RECT_F> dirtyRects_;
    bool fullInvalidation_ = true;   // First frame is always full
    bool pushedDirtyClip_ = false;   // Whether BeginDraw pushed a clip for dirty region
    static constexpr size_t MaxDirtyRects = 8;
    static constexpr float DirtyRectMargin = 2.0f;  // AA margin

    // DPI settings - initialized from window
    float dpiX_ = 96.0f;
    float dpiY_ = 96.0f;

    // Backdrop blur resources - per-frame snapshot textures
    ComPtr<ID3D11Texture2D> snapshotTextures_[FrameCount];
    ComPtr<ID2D1Bitmap1> snapshotBitmaps_[FrameCount];
    bool snapshotValid_[FrameCount] = { false, false };

    // Helper to capture current render target for backdrop blur
    bool CaptureSnapshot();
    bool CreateSnapshotResources();
    void ReleaseSnapshotResources();
    void ReleaseOffscreenResources();
    void MarkOffscreenResourceUsed();
    void TrimOffscreenResourcesIfIdle();

    // Desktop capture resources for window backdrop
    ComPtr<ID2D1Bitmap1> desktopCaptureBitmap_;
    ComPtr<ID2D1Bitmap1> desktopBlurredBitmap_;  // Cached blurred result
    int32_t desktopCaptureWidth_ = 0;
    int32_t desktopCaptureHeight_ = 0;
    bool desktopCaptureValid_ = false;
    bool desktopBlurCacheValid_ = false;
    float cachedBlurRadius_ = 0.0f;

    // Cached liquid glass effects (avoid re-creating COM objects every frame)
    ComPtr<ID2D1Effect> cachedLgBlurEffect_;
    ComPtr<ID2D1Effect> cachedLgEffect_;

    // Pre-glass snapshot: when fused panels render, the first one captures
    // a clean snapshot (before any glass output). Subsequent fused panels
    // reuse it so they don't see each other's glass in their refraction.
    bool preGlassSnapshotCaptured_ = false;

    // Transition shader resources
    ComPtr<ID2D1Bitmap1> transitionBitmaps_[2];  // offscreen bitmaps for old/new content
    uint32_t transitionBmpW_ = 0, transitionBmpH_ = 0;
    ComPtr<ID2D1Image> savedTransitionTarget_;   // saved target during capture
    ComPtr<ID2D1Effect> cachedTransitionEffect_;
    bool CreateTransitionBitmaps(uint32_t pixelW, uint32_t pixelH);

    // Element effect resources (support nested parent/child captures).
    struct EffectBitmapSlot {
        ComPtr<ID2D1Bitmap1> bitmap;
        uint32_t pixelW = 0;
        uint32_t pixelH = 0;
    };

    struct ActiveEffectCapture {
        size_t slotIndex = 0;
        ComPtr<ID2D1Image> savedTarget;
        float captureX = 0;
        float captureY = 0;
        float captureW = 0;
        float captureH = 0;
    };

    std::vector<EffectBitmapSlot> effectBitmapSlots_;
    std::vector<ActiveEffectCapture> effectCaptureStack_;
    ComPtr<ID2D1Bitmap1> lastCapturedEffectBitmap_;
    float lastEffectCaptureX_ = 0;
    float lastEffectCaptureY_ = 0;
    float lastEffectCaptureW_ = 0;
    float lastEffectCaptureH_ = 0;
    ComPtr<ID2D1Effect> cachedBlurEffect_;       // reusable Gaussian blur effect
    ComPtr<ID2D1Effect> cachedShadowEffect_;     // reusable D2D1 shadow effect
    bool CreateEffectBitmap(EffectBitmapSlot& slot, uint32_t pixelW, uint32_t pixelH);
};

} // namespace jalium
