#pragma once

#include "jalium_backend.h"
#include "jalium_internal.h"
#include "d3d12_backend.h"
#include "d3d12_direct_renderer.h"
#include "d3d12_impeller_engine.h"
#include "d3d12_ink_layer.h"
#include <dcomp.h>
#include <dcompanimation.h>   // IDCompositionAnimation — off-thread indicator-visual probe
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

    /// Override: hardware-timestamp GPU breakdown for the previous frame.
    JaliumResult QueryGpuTiming(JaliumGpuTimingStats* out) const override;

    /// Override: return the swap-chain frame-latency waitable HANDLE as
    /// intptr_t. Returns 0 when the swap chain was created without the
    /// FRAME_LATENCY_WAITABLE_OBJECT flag (older runtimes).
    intptr_t GetFrameLatencyWaitable() const override {
        return reinterpret_cast<intptr_t>(frameLatencyWaitable_);
    }

    /// Override: report current swap chain present configuration (SwapEffect /
    /// tearing / frame-latency waitable / max latency / composition). Lets the
    /// host show "D3D12 FLIP-tearing-1f" in status bars and immediately notice
    /// if driver stripped optional flags during swap chain creation.
    JaliumResult GetPresentInfo(JaliumPresentInfo* out) const override;

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

    // --- Off-thread animation probe (Increment 1 — architecture hard gate) ---
    // Creates a child DComp visual whose content is a solid-color mini composition
    // swap chain, binds an autonomous IDCompositionAnimation to its X (or Y) offset,
    // and Commits ONCE. Thereafter DWM drives the offset at vblank with NO app-side
    // Present / Commit — the WPF independent-animation model. Used only to PROVE the
    // primitive self-drives on real iGPU hardware (PresentMon: app present ≈ 0 while
    // the block visibly slides). Env-gated by the managed layer; not a production path.
    JaliumResult CreateAnimProbe(
        int32_t x, int32_t y, int32_t width, int32_t height,
        float travelPx, float periodSec, uint32_t colorArgb, int32_t vertical,
        void** visualOut) override;
    JaliumResult DestroyAnimProbe(void* visual) override;
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
    void FillPath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule, int32_t edgeMode = -1) override;
    void StrokePath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed,
        int32_t lineJoin = 0, float miterLimit = 10.0f, int32_t lineCap = 0,
        const float* dashPattern = nullptr, uint32_t dashCount = 0, float dashOffset = 0.0f, int32_t edgeMode = -1) override;
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
    void PushPerCornerRoundedRectClip(float x, float y, float w, float h,
        float tl, float tr, float br, float bl) override;
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
    void DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity) override;
    void DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity, int scalingMode) override;
    void DrawVideoSurface(VideoSurface* surface, float x, float y, float w, float h,
                          float opacity, int scalingMode) override;

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

    /// --- Retained GPU layers (forward to the direct renderer) ---
    bool  SupportsRetainedLayers() const override;
    void* RealizeLayerBegin(void* existingLayer, float x, float y, float w, float h) override;
    void  RealizeLayerEnd(void* layer) override;
    void  CompositeLayer(void* layer, float x, float y, float w, float h, float opacity) override;
    void  DestroyRetainedLayer(void* layer) override;

    /// --- TEST-ONLY device-removal injection (see RenderTarget base) ---
    bool DebugRemoveDevice() override;
    bool DebugGetRetainedDestroyCounts(uint64_t* orphaned, uint64_t* graveyard) override;
    uint64_t DebugDevicePointer() override;
    bool DebugInOffscreenCapture() override;
    int32_t DebugForceLeakedCommandListResize(int32_t width, int32_t height, int32_t* outListClosed) override;
    int32_t DebugForceVelloOutputOrphan(int32_t* outAlive) override;

    /// --- Two-phase back-buffer readback (parity verification; see base) ---
    /// Forwards to D3D12DirectRenderer, which records the copy inside its
    /// EndFrame right before the PRESENT transition and fence-tags it.
    JaliumResult RequestReadback() override;
    JaliumResult FetchReadback(uint8_t* buf, uint32_t bufStride,
                               int32_t* outWidth, int32_t* outHeight) override;

    void DrawBackdropFilter(
        float x, float y, float w, float h,
        const char* backdropFilter, const char* material, const char* materialTint,
        float tintOpacity, float blurRadius,
        float cornerRadiusTL, float cornerRadiusTR,
        float cornerRadiusBR, float cornerRadiusBL) override;

    void DrawBackdropFilterEx(
        float x, float y, float w, float h,
        const char* backdropFilter, const char* material, const char* materialTint,
        float tintOpacity, float blurRadius,
        float noiseIntensity, float saturation, float luminosity,
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
    void DrawTransitionShader(float x, float y, float w, float h, float progress, int mode,
        float cornerRadius) override;
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
    void DrawOuterGlowEffect(float x, float y, float w, float h,
        float glowSize, float r, float g, float b, float a, float intensity,
        float uvOffsetX, float uvOffsetY,
        float cornerTL, float cornerTR, float cornerBR, float cornerBL) override;
    void DrawInnerShadowEffect(float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY,
        float r, float g, float b, float a,
        float uvOffsetX, float uvOffsetY,
        float cornerTL, float cornerTR, float cornerBR, float cornerBL) override;
    void DrawColorMatrixEffect(float x, float y, float w, float h,
        const float* matrix) override;
    void DrawEmbossEffect(float x, float y, float w, float h,
        float amount, float lightDirX, float lightDirY, float relief) override;
    void DrawShaderEffect(float x, float y, float w, float h,
        const uint8_t* shaderBytecode, uint32_t shaderBytecodeSize,
        const float* constants, uint32_t constantFloatCount) override;
    // HLSL-source custom shader: compile the source to DXBC at runtime
    // (D3DCompile, cached by source hash) and reuse the DXBC DrawShaderEffect
    // path. Mirrors the Vulkan backend's DXC path so one authored SM6 HLSL drives
    // both backends.
    void DrawShaderEffectFromSource(float x, float y, float w, float h,
        const char* hlslSource, const float* constants, uint32_t constantFloatCount) override;

private:
    // 编译期上限：仅用于定长数组(fenceValues_)与不变循环边界。运行期实际
    // swapchain 后台缓冲数是 swapBufferCount_(<= FrameCount)。
    static constexpr uint32_t FrameCount = 3;

    // 运行期 swapchain 后台缓冲数。默认 2(双缓冲)——相比 3 少一整张窗口
    // 尺寸后台缓冲及其 WDDM 驱动镜像/压缩元数据。
    // 【撤回记录 2026-06-10】曾随"令牌桶"实验改默认 3+MFL3：合成探针数据好看
    // （hover 158→95ms），但真实应用 idle 攒满 3 credit 后突发连画 3 帧，
    // FLIP_SEQUENTIAL 严格按序展示，慢合成(9fps)下清空队列 ~340ms，交互帧排
    // 队尾反而更糟（"快 ack≈discard"推断不成立，ack 只是收件回执）。
    // 环境变量 JALIUM_SWAPCHAIN_BUFFERS=2|3 可覆盖(钳到 [2, FrameCount])。
    // 在 CreateSwapChain() 里解析一次后固定。
    static constexpr uint32_t kDefaultSwapBufferCount = 2;
    uint32_t swapBufferCount_ = kDefaultSwapBufferCount;

    // SetMaximumFrameLatency 的值。核显独显一律 1（最低延迟）。历史上核显曾放宽到
    // 2+3buffer，实测只多攒一帧输入延迟、对 DWM 的 buffer 释放节奏毫无影响，已撤回
    // （见 CreateSwapChain 内注释）。
    uint32_t maxFrameLatency_ = 1;

    // 是否核显(UMA)。仅用于诊断/标签。
    bool isIntegratedAdapter_ = false;

    bool CreateSwapChain();
    void WaitForAllFrames();

    // Flush pending Vello paths before non-Vello draws to maintain correct Z-order.
    // Also commits any deferred clip / transform pushes so the upcoming draw
    // observes the correct scissor and transform stacks. Call this before any
    // non-path draw (FillRect, DrawText, DrawBitmap, etc.).
    void FlushVelloIfNeeded();

    // Flush Impeller tessellated batches into DirectRenderer's triangle pipeline.
    // Called after each Impeller path encode to maintain correct Z-order.
    void FlushImpellerBatches();

    // ── Deferred clip + transform state ──────────────────────────────────
    // PushClip / PushTransform are extremely common (thousands per frame in
    // tree-traversal rendering) and most of them are followed by a Pop with
    // no actual draw in between (control templates that conditionally route
    // through an empty branch, transform-then-untransform helpers, etc.).
    // Eagerly pushing each one onto DirectRenderer's scissor / transform
    // stack burns CPU on scissor-rect rebuilds and forces SyncScissorToImpeller
    // to copy the scissor across engines for nothing.
    //
    // Instead we record each push into pendingStateOps_ and only commit it
    // to DirectRenderer at the first real draw (CommitDeferredState, invoked
    // from FlushVelloIfNeeded and from each path emit). A Pop that matches
    // the most recent pending push is collapsed in place — peephole
    // elimination of empty push/pop pairs costs zero state-machine churn.
    enum class DeferredOpKind : uint8_t {
        Transform,                // 6-float matrix
        ClipAxisAligned,          // (x, y, w, h)
        ClipAxisAlignedAliased,   // (x, y, w, h)
        ClipRoundedRect,          // (x, y, w, h, rx, ry)
        ClipPerCornerRounded,     // (x, y, w, h, tl, tr, br, bl)
        Opacity,                  // single float
    };
    struct DeferredStateOp {
        DeferredOpKind kind;
        float data[8];            // discriminated payload
    };
    std::vector<DeferredStateOp> pendingStateOps_;
    bool IdentityMatrixSkip(const float* m);
    void CommitDeferredState();
    void EmitDeferredOp(const DeferredStateOp& op);

    // Brush → SdfRectInstance helpers
    bool FillBrushToInstance(Brush* brush, SdfRectInstance& inst);
    bool ExtractBrushColor(Brush* brush, float& r, float& g, float& b, float& a);

    // Brush → EngineBrushData (solid + linear/radial gradients), for the Impeller
    // engine path/stroke/polygon encoders. Stop storage is owned by the caller-
    // supplied vector; bd.stops aliases stopStore.data(), so the vector MUST
    // outlive the EncodeXxx call that consumes bd. For gradient brushes bd carries
    // the gradient geometry + stops AND a flat fallback color (first stop) in
    // bd.r/g/b/a, used by the engine routes that have no gradient sampler (strokes,
    // polygon fills) so a gradient stroke degrades to a representative solid
    // instead of vanishing. Opacity is folded into the (straight-alpha) stops.
    bool BrushToEngineBrush(Brush* brush, float opacity, EngineBrushData& bd,
                            std::vector<EngineBrushData::GradientStop>& stopStore);

    // Like ExtractBrushColor but also accepts gradient brushes, returning a
    // representative solid (first stop, straight alpha — callers apply opacity
    // separately via SdfRectInstance.opacity). Used by the SDF stroke primitives
    // (DrawRectangle / DrawRoundedRectangle / DrawPerCornerRoundedRectangle /
    // DrawEllipse / DrawContentBorder) whose border is a solid color with no
    // per-pixel gradient support, so a gradient outline degrades to a solid
    // instead of being dropped.
    bool ExtractStrokeColor(Brush* brush, float& r, float& g, float& b, float& a);

    // Representative solid for any colour-bearing brush: solid → exact colour,
    // linear/radial gradient → EQUAL-WEIGHT AVERAGE of all stops (straight
    // alpha, opacity NOT folded in). Field-for-field the same rule as
    // VulkanRenderTarget::TryGetApproximateBrushColor so single-colour sinks
    // (RenderText) resolve the SAME representative colour on both backends.
    // Distinct from ExtractStrokeColor, which keeps its first-stop semantics
    // for the SDF stroke primitives that bake it into their visual baseline.
    bool TryGetApproximateBrushColor(Brush* brush, float& r, float& g, float& b, float& a);

    // Routes a gradient-brush outline (line/rect/rounded-rect/ellipse), supplied
    // as a path command buffer, through the Impeller stroke engine so it renders
    // a TRUE per-pixel gradient stroke. Returns false (no-op) for solid brushes,
    // non-Impeller engines, or on encode failure — the caller then falls back to
    // its solid SDF border path. cmds use tag 0=LineTo[0,x,y], 1=CubicTo
    // [1,c1x,c1y,c2x,c2y,ex,ey], 5=ClosePath.
    bool TryStrokeGradientPath(Brush* brush, float strokeWidth,
                               float startX, float startY,
                               const std::vector<float>& cmds, bool closed,
                               int32_t lineJoin, float miterLimit, int32_t lineCap);

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
    // External present pacing (managed scheduler owns the frame-latency
    // waitable): BeginDraw skips the waitable wait and Present uses sync
    // interval 0 so the UI thread never blocks on the compositor. See
    // RenderTarget::SetExternalPresentPacing.
    bool externalPresentPacing_ = false;

    // Opacity stack (DirectRenderer only has SetOpacity, so we manage the stack here)
    std::stack<float> opacityStack_;

    // Tracks whether each PushClip/PushClipAliased/PushRoundedRectClip frame was
    // a rounded clip, so the matching PopClip can pop both the scissor and the
    // rounded-clip stack on the underlying DirectRenderer.
    std::vector<bool> clipFrameIsRounded_;

    // Actual swap chain creation flags (tracked for correct ResizeBuffers calls)
    UINT swapChainCreationFlags_ = 0;

    // Frame-latency waitable handle. Owned by the swap chain; we use it to
    // block in BeginFrame until the back buffer is genuinely ready, instead
    // of polling the fence with a 50 ms timeout. Set during CreateSwapChain
    // when FRAME_LATENCY_WAITABLE_OBJECT was requested; reset on Resize and
    // closed in the destructor. nullptr means "feature unavailable" — the
    // BeginFrame path still works through the fence wait fallback.
    HANDLE frameLatencyWaitable_ = nullptr;

    // Frame-pacing: waitable wait time, accumulated across every BeginDraw
    // attempt for one logical frame and flushed when the first successful
    // BeginDraw of that frame returns. Mirrors the fence-wait accumulator
    // on D3D12DirectRenderer but lives here because the waitable wait
    // happens in D3D12RenderTarget::BeginDraw, before the renderer is
    // invoked. Cleared on Resize.
    mutable uint64_t accumulatingWaitableWaitNs_ = 0;
    mutable uint64_t lastFrameWaitableWaitNs_ = 0;
    // Note: the public IRenderBackend override GetFrameLatencyWaitable() returns
    // the same HANDLE cast to intptr_t at the top of the class. No additional
    // typed accessor needed — D3D12DirectRenderer reads frameLatencyWaitable_
    // directly through friendship-equivalent (lives in the same translation unit).

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

    // Off-thread animation probe (Increment 1 hard gate). All GPU resources are
    // ComPtr members so teardown is automatic on DestroyAnimProbe / RT destruction.
    struct AnimProbeEntry {
        ComPtr<IDCompositionVisual>          visual;          // child visual (above swap-chain visual)
        ComPtr<IDXGISwapChain1>              contentSwapChain; // static solid-color content
        ComPtr<IDCompositionAnimation>       anim;            // autonomous back-and-forth curve
        ComPtr<IDCompositionRectangleClip>   clip;            // track-rect clamp
    };
    std::unordered_map<IDCompositionVisual*, AnimProbeEntry> animProbes_;

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

    // Runtime-compiled custom shader effect bytecode, keyed by FNV-1a hash of the
    // HLSL source so a repeated DrawShaderEffectFromSource skips re-compilation.
    std::unordered_map<uint64_t, ComPtr<ID3DBlob>> customShaderHlslCache_;

    // sRGB to linear conversion for Clear() color values
    static float SrgbToLinear(float s) {
        return (s <= 0.04045f) ? s / 12.92f : std::pow((s + 0.055f) / 1.055f, 2.4f);
    }
};

} // namespace jalium
