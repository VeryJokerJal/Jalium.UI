#pragma once

#include "jalium_backend.h"
#include "jalium_internal.h"
#include "d3d12_backend.h"
#include "d3d12_direct_renderer.h"
#include "d3d12_impeller_engine.h"
#include "d3d12_ink_layer.h"
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

    /// Override: set rendering engine with hot-switch support.
    JaliumResult SetRenderingEngine(JaliumRenderingEngine engine) override;

    /// Override: report glyph atlas / path cache / texture usage for DevTools.
    JaliumResult QueryGpuStats(JaliumGpuStats* out) const override;

    /// Override: drop the D3D12 glyph atlas at the next BeginFrame boundary.
    /// We deliberately do NOT call `D3D12GlyphAtlas::Reset()` directly here —
    /// glyph entries already emitted earlier in the frame carry baked UV
    /// coordinates that point into the existing atlas, and a mid-frame reset
    /// would shift every cached glyph's UV under their feet (memory entry
    /// `project_d3d12_glyph_atlas_no_midframe_reset.md`). Instead we set the
    /// atlas's `needsReset_` flag through the public RequestResetAtFrameBoundary
    /// helper; D3D12DirectRenderer::BeginFrame already calls
    /// `glyphAtlas_->ApplyPendingGrowthOrReset()` after the frame fence wait,
    /// which honors the flag and recreates the atlas exactly once on the
    /// safe boundary.
    JaliumResult ReclaimIdleResources() override;

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
    void FillPerCornerRoundedRectangle(float x, float y, float w, float h, float tl, float tr, float br, float bl, Brush* brush) override;
    void DrawPerCornerRoundedRectangle(float x, float y, float w, float h, float tl, float tr, float br, float bl, Brush* brush, float strokeWidth) override;
    void FillEllipse(float cx, float cy, float rx, float ry, Brush* brush) override;
    void FillEllipseBatch(const float* data, uint32_t count) override;
    void DrawEllipse(float cx, float cy, float rx, float ry, Brush* brush, float strokeWidth) override;
    void DrawLine(float x1, float y1, float x2, float y2, Brush* brush, float strokeWidth) override;
    void FillPolygon(const float* points, uint32_t pointCount, Brush* brush, int32_t fillRule) override;
    void DrawPolygon(const float* points, uint32_t pointCount, Brush* brush, float strokeWidth, bool closed,
        int32_t lineJoin = 0, float miterLimit = 10.0f) override;
    void FillPath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule) override;
    void StrokePath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed,
        int32_t lineJoin = 0, float miterLimit = 10.0f, int32_t lineCap = 0,
        const float* dashPattern = nullptr, uint32_t dashCount = 0, float dashOffset = 0.0f) override;
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
    void DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity, int scalingMode) override;

    /// Blits the contents of <paramref name="inkBitmap"/> onto the swap
    /// chain at (dstX, dstY). Used by InkCanvas to composite its
    /// committed-ink layer each frame after brush dispatches. The ink
    /// bitmap's texture is expected to be in PIXEL_SHADER_RESOURCE state
    /// (DispatchBrush / Clear leave it there).
    void BlitInkLayer(D3D12InkLayerBitmap* inkBitmap,
                      float dstX, float dstY, float opacity);

    /// Base-class virtual dispatch for the C API — forwards to the D3D12
    /// typed overload after casting the opaque handle.
    void BlitInkLayer(void* inkLayerBitmap,
                      float dstX, float dstY, float opacity) override
    {
        BlitInkLayer(reinterpret_cast<D3D12InkLayerBitmap*>(inkLayerBitmap),
                     dstX, dstY, opacity);
    }
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

    // Flush pending Vello paths before non-Vello draws to maintain correct Z-order.
    // Call this before any non-path draw (FillRect, DrawText, DrawBitmap, etc.).
    void FlushVelloIfNeeded();

    // Flush Impeller tessellated batches into DirectRenderer's triangle pipeline.
    // Called after each Impeller path encode to maintain correct Z-order.
    void FlushImpellerBatches();

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

    // Impeller engine (lazy-initialized on first use when engine == IMPELLER)
    std::unique_ptr<ImpellerD3D12Engine> impellerEngine_;

    /// Returns true if the active engine is Impeller.
    bool IsImpellerActive() const {
        return activeEngine_ == JALIUM_ENGINE_IMPELLER;
    }

    /// Ensure the Impeller engine is initialized.
    bool EnsureImpellerEngine();

    /// Sync DirectRenderer scissor state to Impeller engine.
    void SyncScissorToImpeller();

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

    // Dirty rect tracking with aggregation (containment / intersection /
    // near-adjacency merging). When the rect count would exceed MaxDirtyRects
    // we perform a "minimum-waste" merge of the closest pair instead of
    // surrendering to a full-window redraw.
public:
    struct DirtyRect { float x, y, w, h; };
private:
    std::vector<DirtyRect> dirtyRects_;
    bool fullInvalidation_ = true;
    // Raised from 8 → 32. DXGI Present1 accepts any count — the old value
    // existed purely because the previous implementation had no merge logic
    // and needed a hard cap to avoid unbounded growth.
    static constexpr size_t MaxDirtyRects = 32;
    static constexpr float DirtyRectMargin = 2.0f;
    // Two rects whose edges are within this distance (in DIPs) are treated as
    // touching — prevents scattered AA fringes from staying fragmented.
    static constexpr float DirtyRectAdjacencyEpsilon = 1.0f;
    // How much bounding-area waste is tolerable when speculatively merging two
    // disjoint rects, relative to the larger rect's area. 0.3 = merge as long
    // as waste is ≤ 30 % of the larger input.
    static constexpr float DirtyRectMergeWasteRatio = 0.3f;

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
