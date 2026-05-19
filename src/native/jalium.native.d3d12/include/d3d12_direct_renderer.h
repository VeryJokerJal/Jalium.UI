#pragma once

#include "d3d12_backend.h"
#include "d3d12_glyph_atlas.h"
#include "jalium_stencil_path.h"
#include <vector>
#include <stack>
#include <memory>

namespace jalium {

class D3D12VelloRenderer;  // forward declaration

// ============================================================================
// 3x2 affine transform (column-major)
// ============================================================================

struct Transform2D {
    float m11, m12, m21, m22, dx, dy;

    static Transform2D Identity() { return { 1, 0, 0, 1, 0, 0 }; }

    // Multiply: *this * rhs  (apply *this first, then rhs)
    Transform2D operator*(const Transform2D& rhs) const {
        return {
            m11 * rhs.m11 + m12 * rhs.m21,
            m11 * rhs.m12 + m12 * rhs.m22,
            m21 * rhs.m11 + m22 * rhs.m21,
            m21 * rhs.m12 + m22 * rhs.m22,
            dx * rhs.m11 + dy * rhs.m21 + rhs.dx,
            dx * rhs.m12 + dy * rhs.m22 + rhs.dy
        };
    }
};

// ============================================================================
// Instance data layout for SDF rect shader (192 bytes, 16-byte aligned)
//
// Supports solid fills and linear/radial gradient fills.
// When gradientType == 0, fillR/G/B/A is used as a flat premultiplied color.
// When gradientType != 0, gradient stops are sampled in the pixel shader.
// ============================================================================

struct SdfRectInstance {
    // --- geometry (16 bytes) ---
    float posX, posY;           // top-left corner (pixels)
    float sizeX, sizeY;         // width, height

    // --- solid fill color (16 bytes, premultiplied RGBA) ---
    float fillR, fillG, fillB, fillA;

    // --- border color (16 bytes, premultiplied RGBA) ---
    float borderR, borderG, borderB, borderA;

    // --- corner radii (16 bytes) ---
    float cornerTL, cornerTR, cornerBR, cornerBL;

    // --- misc (16 bytes) ---
    float borderWidth;
    float opacity;
    uint32_t gradientType;      // 0=solid, 1=linear, 2=radial
    uint32_t stopCount;         // number of gradient stops (0-4)

    // --- gradient geometry (16 bytes) ---
    // linear: startX, startY, endX, endY (in rect-local pixels)
    // radial: centerX, centerY, radiusX, radiusY
    float gradGeom0, gradGeom1, gradGeom2, gradGeom3;

    // --- gradient stops (4 × 20 bytes = 80 bytes) ---
    // Each stop: position, R, G, B, A (linear premultiplied)
    float stop0Pos, stop0R, stop0G, stop0B, stop0A;
    float stop1Pos, stop1R, stop1G, stop1B, stop1A;
    float stop2Pos, stop2R, stop2G, stop2B, stop2A;
    float stop3Pos, stop3R, stop3G, stop3B, stop3A;

    // --- shape type (16 bytes) ---
    float shapeType;            // 0 = RoundedRect (default), 1 = SuperEllipse
    float shapeN;               // SuperEllipse exponent (e.g. 4.0 for squircle)
    float _pad2, _pad3;
};
static_assert(sizeof(SdfRectInstance) == 192, "SdfRectInstance must be 192 bytes");

// ============================================================================
// Instance data layout for bitmap quad shader (48 bytes, 16-byte aligned)
// ============================================================================

struct BitmapQuadInstance {
    float posX, posY;           // top-left corner (pixels)       offset 0
    float sizeX, sizeY;         // width, height                  offset 8
    float uvMinX, uvMinY;       // texture UV top-left [0,1]      offset 16
    float uvMaxX, uvMaxY;       // texture UV bottom-right [0,1]  offset 24
    float opacity;              // overall opacity [0,1]           offset 32
    float samplerIdx;           // 0=linear,1=point,2=anisotropic  offset 36
    float _pad1, _pad2;         //                                offset 40 (pad to 48)
};
static_assert(sizeof(BitmapQuadInstance) == 48, "BitmapQuadInstance must be 48 bytes");

// ============================================================================
// Frame constants CBV (16 bytes)
// ============================================================================

struct DirectFrameConstants {
    float screenWidth;
    float screenHeight;
    float invScreenWidth;
    float invScreenHeight;
};

// ============================================================================
// Triangle vertex for path/polygon fill (24 bytes)
// ============================================================================

struct TriangleVertex {
    float x, y;             // screen-space position (pixels)
    float r, g, b, a;       // premultiplied linear RGBA
};
static_assert(sizeof(TriangleVertex) == 24, "TriangleVertex must be 24 bytes");

// ============================================================================
// Draw batch — a range of instances sharing the same PSO
// ============================================================================

enum class DrawBatchType : uint8_t {
    SdfRect,
    Text,
    Bitmap,
    Ellipse,
    Line,
    PunchRect,      // copy-blend (writes RGBA directly, no alpha blending)
    SnapshotBlit,   // draw captured snapshot region as textured quad
    Triangle,       // flat-shaded triangles for path/polygon fill
    StencilPath,    // stencil-then-cover SVG path fill (see d3d12_path_pipeline)
    LiquidGlass,    // full liquid glass effect (SDF refraction, highlight, shadow)
};

struct DrawBatch {
    DrawBatchType type;
    uint32_t instanceOffset;
    uint32_t instanceCount;
    float sortOrder;       // painter's order (ascending)
    D3D12_RECT scissor;    // active scissor at submission time
    bool hasScissor;       // whether a custom scissor is active

    // Rounded-rect clip (post-DPI physical-pixel coordinates, matching SV_Position).
    // When hasRoundedClip is true, the pixel shader discards fragments outside the
    // per-corner rounded-rect SDF defined by (roundedClipRect, roundedClipCornerRadii).
    bool  hasRoundedClip = false;
    float roundedClipRect[4]        = { 0, 0, 0, 0 }; // left, top, right, bottom
    float roundedClipCornerRadii[4] = { 0, 0, 0, 0 }; // TL, TR, BR, BL (in physical pixels)
};

// ============================================================================
// Rounded-rect clip stack entry (DIP-space, before DPI / transform).
// Used by D3D12RenderTarget::PushRoundedRectClip — when present, every batch
// recorded while the entry is on the stack carries the rounded-clip data and
// every PSO discards fragments outside the SDF in the pixel shader.
// ============================================================================

struct RoundedClipState {
    // DIP-space rectangle (top-left + size) and per-corner radii at push time.
    // Layout matches CornerRadius: TopLeft, TopRight, BottomRight, BottomLeft.
    float x, y, w, h;
    float radiusTL = 0, radiusTR = 0, radiusBR = 0, radiusBL = 0;
    // Captured transform at push time so the clip can be projected to physical
    // pixels regardless of subsequent transform pushes/pops.
    Transform2D transform;
};

// ============================================================================
// D3D12 Direct Renderer
//
// Replaces D2D immediate-mode rendering with batched D3D12 instanced draws.
// Usage per frame:
//   BeginFrame()          — reset buffers, set RTV
//   AddRect/AddText/...   — collect instances
//   EndFrame()            — upload, sort, draw, present
// ============================================================================

class D3D12DirectRenderer {
public:
    explicit D3D12DirectRenderer(D3D12Backend* backend);
    ~D3D12DirectRenderer();

    bool Initialize(IDXGISwapChain3* swapChain, UINT frameCount);
    void Shutdown();

    // Releases back buffer references so DXGI ResizeBuffers can succeed.
    // Must be called before the swap chain is resized.
    void ReleaseBackBufferReferences();

    // Called when the swap chain is resized. Acquires new back buffer references,
    // recreates RTVs, and invalidates cached blur temp textures.
    bool OnResize(UINT newWidth, UINT newHeight);

    // --- Per-frame lifecycle ---
    bool BeginFrame(UINT frameIndex, UINT width, UINT height, bool clear, float clearR, float clearG, float clearB, float clearA);
    JaliumResult EndFrame(bool useDirtyRects, const std::vector<D3D12_RECT>& dirtyRects, UINT syncInterval, UINT presentFlags);

    /// Aborts the current frame without submitting GPU work.
    /// Closes the command list and resets internal state so BeginFrame can be called again.
    /// Used when the frame must be discarded (e.g. during window resize).
    void AbortFrame();

    // --- Draw commands (called between BeginFrame/EndFrame) ---
    void AddSdfRect(const SdfRectInstance& inst);
    void AddText(IDWriteTextLayout* layout, float x, float y,
                 float r, float g, float b, float a,
                 uint64_t layoutKey = 0);
    void AddBitmap(float x, float y, float w, float h, float opacity,
                   ID3D12Resource* textureResource, DXGI_FORMAT format,
                   float uvMaxX = 1.0f, float uvMaxY = 1.0f,
                   int scalingMode = 0 /* JALIUM_BITMAP_SCALING_UNSPECIFIED */,
                   ID3D12Resource* uploadBuffer = nullptr);

    // --- Triangle path fill (flat-shaded triangulated polygon) ---
    void AddTriangles(const TriangleVertex* vertices, uint32_t vertexCount);

    /// Add pre-transformed triangles without applying the current transform or opacity.
    /// Used by Impeller engine which produces vertices already in pixel-space with
    /// opacity baked into vertex colors.
    void AddTrianglesPreTransformed(const TriangleVertex* vertices, uint32_t vertexCount);

    /// Stencil-then-cover path fill (solid color brush). Uses the current
    /// transform stack + dpiScale to project local-space verts into pixels
    /// inside the vertex shader. Works for any contour topology (concave,
    /// self-intersecting, multi-figure). Falls back gracefully when the
    /// stencil pipeline isn't ready: the caller (D3D12RenderTarget::FillPath)
    /// then routes to the Impeller engine.
    /// fillRule: 0 = EvenOdd, 1 = NonZero.
    /// Returns false when the stencil renderer is unavailable.
    bool AddStencilPath(std::shared_ptr<const StencilPathGeometry> geom,
                        float r, float g, float b, float a,
                        int32_t fillRule);

    /// Look up or build a cached StencilPathGeometry for the given source
    /// path commands. Caller passes the same buffer it would feed to
    /// FillPath; the cache key includes the current transform's scale
    /// bucket so vertex density tracks on-screen size. Always returns a
    /// non-null geometry (possibly empty when the path is degenerate).
    std::shared_ptr<const StencilPathGeometry>
    GetOrBuildStencilPathGeometry(float startX, float startY,
                                  const float* commands, uint32_t commandLength);

    // --- Punch transparent rect (copy blend, writes 0,0,0,0 directly) ---
    void PunchTransparentRect(float x, float y, float w, float h);

    // --- Blur / effect commands ---
    // Blurs a rectangular region of the current render target in-place.
    // Uses a two-pass separable Gaussian blur via compute shader.
    void BlurRegion(float x, float y, float w, float h, float radius);

    // --- Snapshot-based effects ---
    // Pre-glass snapshot: first fused panel captures before any glass output.
    // Reset to false each frame in BeginFrame.
    bool preGlassSnapshotCaptured_ = false;

    // Captures the current back buffer content to an internal snapshot texture.
    bool CaptureSnapshot();

    // Draws a blurred + tinted region from the snapshot.
    // Used by DrawBackdropFilter and simplified DrawLiquidGlass.
    void DrawSnapshotBlurred(float x, float y, float w, float h,
                             float blurRadius,
                             float tintR, float tintG, float tintB, float tintOpacity,
                             float cornerRadius);

    // --- Full Liquid Glass rendering ---
    // Renders complete liquid glass with SDF refraction, chromatic aberration,
    // edge highlights, inner shadow, tint/vibrancy, and neighbor fusion.
    void DrawLiquidGlass(float x, float y, float w, float h,
                         float cornerRadius, float blurRadius,
                         float refractionAmount, float chromaticAberration,
                         float tintR, float tintG, float tintB, float tintOpacity,
                         float lightX, float lightY, float highlightBoost,
                         int shapeType, float shapeExponent,
                         int neighborCount, float fusionRadius,
                         const float* neighborData);

    // --- Desktop backdrop ---
    // Captures desktop area via GDI and uploads to a D3D12 texture.
    void CaptureDesktopArea(int32_t screenX, int32_t screenY, int32_t width, int32_t height);
    // Draws captured desktop with blur and tint.
    void DrawDesktopBackdrop(float x, float y, float w, float h,
                             float blurRadius,
                             float tintR, float tintG, float tintB, float tintOpacity);

    // --- Glow effects (approximated with SDF rects) ---
    void DrawGlowingBorderHighlight(
        float x, float y, float w, float h,
        float animationPhase, float glowR, float glowG, float glowB,
        float strokeWidth, float trailLength, float dimOpacity,
        float screenWidth, float screenHeight);

    void DrawGlowingBorderTransition(
        float fromX, float fromY, float fromW, float fromH,
        float toX, float toY, float toW, float toH,
        float headProgress, float tailProgress,
        float animationPhase, float glowR, float glowG, float glowB,
        float strokeWidth, float trailLength, float dimOpacity,
        float screenWidth, float screenHeight);

    void DrawRippleEffect(
        float x, float y, float w, float h,
        float rippleProgress, float glowR, float glowG, float glowB,
        float strokeWidth, float dimOpacity,
        float screenWidth, float screenHeight);

    // --- Offscreen render target (for transition capture) ---
    bool BeginOffscreenCapture(int slot, float x, float y, float w, float h);
    void EndOffscreenCapture(int slot);
    void DrawOffscreenBitmap(int slot, float x, float y, float w, float h, float opacity);
    // Draw a cropped sub-region of the offscreen texture.
    // (x,y,w,h) = destination rect in DIP; (uvOffsetX/Y) = DIP offset into the capture.
    void DrawOffscreenBitmapCropped(int slot, float x, float y, float w, float h,
        float uvOffsetX, float uvOffsetY, float opacity);
    bool IsInOffscreenCapture() const { return inOffscreenCapture_; }
    void DrawCustomShaderEffect(int slot,
        float x, float y, float w, float h,
        const uint8_t* shaderBytecode, uint32_t shaderBytecodeSize,
        const float* constants, uint32_t constantFloatCount);

    // Blurs the offscreen texture at the given slot in-place using two-pass Gaussian.
    // The offscreen must have been captured (EndOffscreenCapture called) before this.
    // Returns true on success, false if blur resources aren't ready or slot is invalid.
    bool BlurOffscreenSlot(int slot, float radius);

    // --- State stacks ---
    void PushScissor(float x, float y, float w, float h);
    void PushScissorRaw(const D3D12_RECT& rect) { scissorStack_.push(rect); }
    void PopScissor();
    bool HasScissor() const { return !scissorStack_.empty(); }
    D3D12_RECT GetCurrentScissor() const { return scissorStack_.empty() ? D3D12_RECT{0,0,0,0} : scissorStack_.top(); }

    // Rounded-rect clip — pushed on top of the regular scissor.  All batches
    // recorded while a rounded clip is active receive the SDF clip data and
    // their pixel shader discards fragments outside the rounded-rect.
    // The matching scissor for fast hardware culling is still the caller's
    // responsibility (PushScissor) — rounded clip is purely the SDF mask.
    // Per-corner version is the canonical entry point; the symmetric variant
    // forwards by populating all four corners with (rx).
    void PushRoundedClip(float x, float y, float w, float h, float rx, float ry);
    void PushPerCornerRoundedClip(float x, float y, float w, float h,
        float tl, float tr, float br, float bl);
    void PopRoundedClip();
    bool HasRoundedClip() const { return !roundedClipStack_.empty(); }
    void SetOpacity(float opacity) { currentOpacity_ = opacity; }
    float GetOpacity() const { return currentOpacity_; }
    void SetShapeType(float type, float n) { currentShapeType_ = type; currentShapeN_ = n; }
    float GetShapeType() const { return currentShapeType_; }
    float GetShapeN() const { return currentShapeN_; }

    // --- Transform stack ---
    void PushTransform(float m11, float m12, float m21, float m22, float dx, float dy);
    void PopTransform();
    Transform2D GetCurrentTransform() const;

    // --- DPI ---
    void SetDpiScale(float dpiScale);
    float GetDpiScale() const { return dpiScale_; }

    // --- Format ---
    DXGI_FORMAT GetSwapChainFormat() const { return swapChainFormat_; }

    // --- Queries ---
    bool IsInitialized() const { return initialized_; }
    ID3D12Device* GetDevice() const { return device_; }
    ID3D12GraphicsCommandList* GetCommandList() const { return commandList_.Get(); }
    D3D12_CPU_DESCRIPTOR_HANDLE GetRtvHandle() const {
        // Phase 2 MSAA was rolled back — render directly into the swap-chain
        // back buffer. MSAA infrastructure remains behind GetMsaaRtvHandle /
        // EnsureMsaaColorBuffer for future re-enablement when bitmap path
        // can be taught to bypass the 4× target.
        return GetSwapChainRtvHandle();
    }
    D3D12_CPU_DESCRIPTOR_HANDLE GetSwapChainRtvHandle() const {
        auto h = rtvHeap_->GetCPUDescriptorHandleForHeapStart();
        h.ptr += currentFrame_ * rtvDescriptorSize_;
        return h;
    }

    // Diagnostic: query fence values for debug logging
    uint64_t GetFenceCompletedValue() const { return fence_ ? fence_->GetCompletedValue() : 0; }
    uint64_t GetFrameFenceValue(UINT frameIndex) const { return frameIndex < frameCount_ ? frames_[frameIndex].fenceValue : 0; }

    // Flush pending graphics draws so compute or external code can safely read
    // the current render target contents.  Called by D3D12RenderTarget before
    // effect capture / blur that need rasterised content on the back buffer.
    void FlushGraphicsForCompute();

    // --- Vello GPU path renderer ---
    D3D12VelloRenderer* GetVelloRenderer() const { return velloEnabled_ ? velloRenderer_.get() : nullptr; }
    bool HasVelloPaths() const;
    void FlushVelloPaths();
    void ApplyScissorToVello();
    void SetVelloEnabled(bool enabled) { velloEnabled_ = enabled; }

    // --- Diagnostics accessors (Perf tab) ---
    D3D12GlyphAtlas* GetGlyphAtlas() const { return glyphAtlas_.get(); }
    int32_t GetBitmapBatchTextureCount() const { return static_cast<int32_t>(bitmapTextures_.size()); }

    /// Drops every cached custom-shader pipeline state. The PSO ComPtrs
    /// auto-release; any GPU work that referenced them earlier in the frame
    /// keeps an implicit ref through the open command list until the frame's
    /// fence completes (D3D12 ID3D12PipelineState lifetime is fence-bound).
    /// Called from D3D12RenderTarget::ReclaimIdleResources when the
    /// idle-resource reclaimer signals the application has been quiet long
    /// enough that holding the cache is no longer worth the memory; rebuilt
    /// lazily by GetOrCreateCustomShaderPSO on the next draw that needs a
    /// custom shader.
    void ClearCustomShaderCache() { customShaderCache_.clear(); }
    int64_t GetBitmapBatchTextureBytes() const {
        int64_t total = 0;
        for (const auto& tx : bitmapTextures_) {
            if (!tx.textureResource) continue;
            auto desc = tx.textureResource->GetDesc();
            // DXGI_FORMAT width × height × bytes/pixel is a reasonable approximation.
            int64_t bpp = 4;
            switch (desc.Format) {
                case DXGI_FORMAT_R8_UNORM: bpp = 1; break;
                case DXGI_FORMAT_R8G8_UNORM: bpp = 2; break;
                case DXGI_FORMAT_R16G16B16A16_FLOAT: bpp = 8; break;
                default: bpp = 4; break;
            }
            total += static_cast<int64_t>(desc.Width) * desc.Height * bpp;
        }
        return total;
    }

private:
    bool CreatePSOs();
    bool CreateRootSignature();
    bool CreateFrameResources();
    bool CreateBlurResources();
    bool CreateStencilPathResources();
    bool EnsureStencilDepthBuffer(UINT width, UINT height);
    void WaitForGpuIdle();
    bool EnsureSnapshotTexture();
    bool EnsureBlurTemps(UINT requiredWidth, UINT requiredHeight);
    bool EnsureOffscreenTargets(UINT requiredWidth, UINT requiredHeight);
    ID3D12PipelineState* GetOrCreateCustomShaderPSO(const uint8_t* shaderBytecode, uint32_t shaderBytecodeSize);
    void UploadInstances();
    void RecordDrawCommands();

    D3D12Backend* backend_ = nullptr;
    ID3D12Device* device_ = nullptr;
    IDXGISwapChain3* swapChain_ = nullptr;

    // Per-frame resources (double-buffered)
    static constexpr UINT kMaxFrames = 3;
    struct FrameResources {
        ComPtr<ID3D12CommandAllocator> commandAllocator;
        ComPtr<ID3D12Resource> instanceUploadBuffer;  // upload heap, persistently mapped — lazy-allocated, grow ×2
        void* instanceMappedPtr = nullptr;
        size_t instanceCapacity = 0;                  // current size in bytes of instanceUploadBuffer (0 = not yet allocated)
        ComPtr<ID3D12Resource> constantsBuffer;       // ring-buffer for per-flush constants
        void* constantsMappedPtr = nullptr;
        UINT constantsRingOffset = 0;                 // next free 256-byte slot in ring buffer
        uint64_t fenceValue = 0;

        // Instance upload buffers retired by mid-frame growth.  When EnsureFrameInstanceCapacity
        // replaces instanceUploadBuffer, earlier draws on the open command list still hold
        // descriptor-table references to the old buffer's resource.  We park the old ComPtr
        // here to keep its refcount > 0 until the GPU finishes this frame; BeginFrame clears
        // the list after the per-frame fence wait succeeds.
        // Also receives the per-Dispatch upload / config / scratch resources Vello
        // retires from its DrainRetired API (mid-frame multi-Dispatch z-order
        // path), since the same fence-gated lifetime applies.
        std::vector<ComPtr<ID3D12Resource>> retiredInstanceBuffers;
        // Per-Dispatch shader-visible descriptor heaps that Vello allocates fresh
        // each Dispatch (so mid-frame multi-Dispatch doesn't overwrite each other's
        // descriptors before the GPU reads them). Lifetime gated by the same
        // fence as retiredInstanceBuffers.
        std::vector<ComPtr<ID3D12DescriptorHeap>> retiredDescriptorHeaps;
    };

    // Grow the persistently-mapped UPLOAD heap that backs both per-frame instance
    // SRVs and mid-frame constant buffers.  Safe to call mid-frame: any old buffer
    // is parked on FrameResources::retiredInstanceBuffers so the descriptors of
    // already-recorded draws stay valid until BeginFrame's fence wait clears them.
    bool EnsureFrameInstanceCapacity(FrameResources& fr, size_t requiredBytes);
    static constexpr UINT kConstantsRingSize = 256 * 64;  // 64 flush slots per frame
    FrameResources frames_[kMaxFrames];
    UINT frameCount_ = 0;
    UINT currentFrame_ = 0;

    // Command list (shared across frames, reset per frame)
    ComPtr<ID3D12GraphicsCommandList> commandList_;

    // Fence
    ComPtr<ID3D12Fence> fence_;
    HANDLE fenceEvent_ = nullptr;
    uint64_t nextFenceValue_ = 1;

    // RTV descriptor heap (for swap chain back buffers + offscreen RTs)
    ComPtr<ID3D12DescriptorHeap> rtvHeap_;
    UINT rtvDescriptorSize_ = 0;
    ComPtr<ID3D12Resource> renderTargets_[kMaxFrames];

    // ── 4× MSAA color buffer (Phase 2 of stroke-quality fix) ────────────
    // We render into a 4-sample MSAA texture rather than the swap-chain
    // back buffer directly; the back buffer gets the result via
    // ResolveSubresource at the end of the frame. This re-introduces
    // edge AA for the stroke triangle pipeline (which was switched from
    // CPU scanline rasterization to direct GPU triangulation in Phase 1)
    // without paying the per-pixel rect cost the analytic-AA path was
    // imposing. RTV slot for the MSAA target follows the per-frame back-
    // buffer RTV slots; we re-create on resize alongside renderTargets_.
    ComPtr<ID3D12Resource> msaaColorBuffer_;
    uint32_t msaaWidth_ = 0;
    uint32_t msaaHeight_ = 0;
    static constexpr UINT kMsaaSampleCount = 4;
    bool EnsureMsaaColorBuffer(uint32_t width, uint32_t height);
    D3D12_CPU_DESCRIPTOR_HANDLE GetMsaaRtvHandle() const {
        auto h = rtvHeap_->GetCPUDescriptorHandleForHeapStart();
        // RTV heap layout: [0..frameCount_-1] back buffers, [frameCount_..frameCount_+1]
        // offscreen RT slots, [frameCount_+2] MSAA.
        h.ptr += (frameCount_ + 2) * rtvDescriptorSize_;
        return h;
    }

    // SRV descriptor heap (for StructuredBuffer<Instance>)
    ComPtr<ID3D12DescriptorHeap> srvHeap_;
    UINT srvDescriptorSize_ = 0;

    // Frame constants (legacy — kept for blur passes that run outside per-frame lifecycle)
    ComPtr<ID3D12Resource> frameConstantsBuffer_;
    void* frameConstantsMapped_ = nullptr;

    // Per-frame SRV region size (avoids cross-frame descriptor race)
    UINT frameSrvRegionSize_ = 0;

    // Pipeline state
    ComPtr<ID3D12RootSignature> rootSignature_;
    ComPtr<ID3D12PipelineState> sdfRectPSO_;
    ComPtr<ID3D12PipelineState> textPSO_;
    ComPtr<ID3D12PipelineState> bitmapPSO_;
    ComPtr<ID3D12PipelineState> copyBlendPSO_;  // SDF rect with copy blend (no alpha blending)
    ComPtr<ID3D12PipelineState> trianglePSO_;  // flat-shaded triangle fill

    // Compiled shaders (cached)
    ComPtr<ID3DBlob> sdfRectVS_;
    ComPtr<ID3DBlob> sdfRectPS_;
    ComPtr<ID3DBlob> textVS_;
    ComPtr<ID3DBlob> textPS_;
    ComPtr<ID3DBlob> bitmapVS_;
    ComPtr<ID3DBlob> bitmapPS_;
    ComPtr<ID3DBlob> triangleVS_;
    ComPtr<ID3DBlob> trianglePS_;
    ComPtr<ID3DBlob> customEffectVS_;

    // ── Stencil-then-cover path renderer ────────────────────────────────
    // Architecture mirrors docs/reference/pure_d3d12_path_renderer.h, with
    // an 8× MSAA scratch buffer added so SVG edges land with the same
    // sample density as Direct2D/Skia at high quality:
    //   • CPU once: flatten path commands → local-space fan triangles +
    //     local-space cover quad (jalium_stencil_path.h, cached LRU).
    //   • Per frame, per path:
    //       0) (Lazy on first stencil-path batch in this frame) bind the
    //          per-renderer 8× MSAA scratch color + 8× MSAA stencil.
    //       1) Stencil PSO (NonZero or EvenOdd) writes stencil with the fan
    //          triangles (RenderTargetWriteMask = 0, no color).
    //       2) Cover PSO with stencil-NotEqual-zero writes the path color
    //          (premultiplied) into the MSAA scratch and resets stencil
    //          via STENCIL_OP_REPLACE so the next path starts clean.
    //   • At any path → non-path transition (or end of frame):
    //       3) ResolveSubresource MSAA color → 1× scratch (sample average
    //          gives correct premult-alpha edge coverage).
    //       4) A fullscreen-triangle "path resolve" PSO blits the 1×
    //          scratch onto the back buffer with premult alpha blend.
    //
    // Vertex shader applies the per-draw transform supplied via root
    // constants (transform 6 floats + color 4 + screen 4 + pad 2 = 16).
    ComPtr<ID3D12RootSignature> stencilPathRootSig_;
    ComPtr<ID3D12PipelineState> psoStencilFillNonZero_;
    ComPtr<ID3D12PipelineState> psoStencilFillEvenOdd_;
    ComPtr<ID3D12PipelineState> psoStencilCover_;
    ComPtr<ID3DBlob>            stencilPathVS_;
    ComPtr<ID3DBlob>            stencilPathPS_;

    // Resolve pipeline: fullscreen triangle samples the 1× resolved
    // path scratch and premult-alpha-blends it onto the back buffer.
    ComPtr<ID3D12RootSignature> pathResolveRootSig_;
    ComPtr<ID3D12PipelineState> psoPathResolve_;
    ComPtr<ID3DBlob>            pathResolveVS_;
    ComPtr<ID3DBlob>            pathResolvePS_;

    // 8× MSAA scratch resources (color + stencil) and 1× resolve target.
    // Sized to viewport on first use and on resize.
    ComPtr<ID3D12Resource>       pathMsaaColor_;
    ComPtr<ID3D12Resource>       pathMsaaDepth_;
    ComPtr<ID3D12Resource>       pathResolveTexture_;
    ComPtr<ID3D12DescriptorHeap> pathMsaaRtvHeap_;
    ComPtr<ID3D12DescriptorHeap> stencilDsvHeap_;  // 8× DSV for pathMsaaDepth_
    UINT  pathMsaaWidth_  = 0;
    UINT  pathMsaaHeight_ = 0;
    static constexpr UINT kPathMsaaSampleCount = 8;
    D3D12_RESOURCE_STATES pathMsaaColorState_   = D3D12_RESOURCE_STATE_RENDER_TARGET;
    D3D12_RESOURCE_STATES pathResolveTexState_  = D3D12_RESOURCE_STATE_COMMON;
    bool  stencilPathReady_   = false;

    // Per-frame queue of stencil-path draws. DrawBatch references entries by
    // index (DrawBatch::instanceOffset). Cleared each BeginFrame.
    struct StencilPathDraw {
        std::shared_ptr<const StencilPathGeometry> geom;
        // Pixel-space transform (current transform * dpi at record time).
        float m11, m12, m21, m22, dx, dy;
        // Premultiplied color * opacity.
        float r, g, b, a;
        // Stencil pipeline (0 = EvenOdd, 1 = NonZero).
        int32_t fillRule;
        // GPU vertex buffer offsets into the per-frame upload heap, filled
        // at UploadInstances time.
        size_t fillVbOffsetBytes  = 0;
        UINT   fillVertexCount    = 0;
        size_t coverVbOffsetBytes = 0;
        UINT   coverVertexCount   = 0;
    };
    std::vector<StencilPathDraw> stencilPathDraws_;
    // Source-space LRU keyed on (start, commands, scaleBucket). Capacity
    // 256 is enough for typical UI working sets (Gallery, DesktopDemo).
    std::unique_ptr<StencilPathCache> stencilPathCache_;
    static constexpr size_t kStencilPathCacheCapacity = 256;
    // Total bytes of stencil-path vertex data this frame; placed in the
    // shared per-frame UPLOAD heap after triangle vertices.
    size_t stencilPathBufferByteOffset_ = 0;

    // Glyph atlas for text rendering
    std::unique_ptr<D3D12GlyphAtlas> glyphAtlas_;

    // Instance collection (CPU side, per frame)
    std::vector<SdfRectInstance> rectInstances_;
    std::vector<GlyphQuadInstance> textInstances_;
    std::vector<BitmapQuadInstance> bitmapInstances_;
    std::vector<TriangleVertex> triangleVertices_;
    std::vector<DrawBatch> batches_;
    float drawOrder_ = 0.0f;

    // Per-frame bitmap texture binding (one texture per bitmap batch)
    // Uses ComPtr to prevent the resource from being freed before the GPU executes the draw.
    // uploadBuffer is the *most recent* upload buffer of the source D3D12Bitmap
    // at the time this batch was recorded — keeping a ref here ensures the
    // CopyTextureRegion src (recorded into the same command list) survives
    // until the next BeginFrame's fence-wait drains it, even if the bitmap is
    // Disposed mid-frame from a worker thread (cache eviction) or by the GC
    // finalizer. Without this, the upload buffer's only owner is the bitmap
    // itself; destroying the bitmap drops it and triggers
    // OBJECT_DELETED_WHILE_STILL_IN_USE on Close.
    struct BitmapBatchTexture {
        uint32_t batchIndex;
        ComPtr<ID3D12Resource> textureResource;
        ComPtr<ID3D12Resource> uploadBuffer;  // optional: nullptr for non-D3D12Bitmap sources (offscreen RT, desktop dup)
        DXGI_FORMAT format;
    };
    std::vector<BitmapBatchTexture> bitmapTextures_;

    struct CustomShaderCacheEntry {
        uint64_t hash = 0;
        std::vector<uint8_t> bytecode;
        ComPtr<ID3D12PipelineState> pso;
    };
    std::vector<CustomShaderCacheEntry> customShaderCache_;

    // State stacks
    std::stack<D3D12_RECT> scissorStack_;
    std::stack<Transform2D> transformStack_;
    std::vector<RoundedClipState> roundedClipStack_;

    // Resolves the active rounded-clip stack into the per-batch payload that
    // RecordDrawCommands writes to the b2 root constants.  Returns false when
    // no rounded clip is active or the resolution failed (e.g. inverse-singular
    // transform); in that case the caller leaves DrawBatch.hasRoundedClip=false.
    bool ResolveRoundedClipForBatch(DrawBatch& batch) const;
    float currentOpacity_ = 1.0f;
    float currentShapeType_ = 0.0f;  // 0 = RoundedRect, 1 = SuperEllipse
    float currentShapeN_ = 4.0f;

    // Frame state
    UINT viewportWidth_ = 0;
    UINT viewportHeight_ = 0;
    bool inFrame_ = false;

    // Current frame constants — cached so RecordDrawCommands can embed them
    // via SetGraphicsRoot32BitConstants (avoiding the CBV race condition).
    DirectFrameConstants currentFrameConstants_ = {};
    bool initialized_ = false;
    size_t textBufferByteOffset_ = 0;    // byte offset of text instances in upload buffer
    size_t bitmapBufferByteOffset_ = 0;  // byte offset of bitmap instances in upload buffer
    size_t triBufferByteOffset_ = 0;     // byte offset of triangle vertices in upload buffer
    size_t uploadBufferOffset_ = 0;      // ring-buffer offset into upload buffer for data versioning
    UINT srvAllocOffset_ = 0;            // ring-buffer offset into SRV heap for descriptor versioning
    UINT lastFlushSrvBase_ = 0;          // base SRV slot of the most recent flush (used by RecordDrawCommands)
    UINT lastFlushSlotsPerFlush_ = 8;    // total slots allocated for the most recent flush
    // Stencil-path resolve SRV region — placed AFTER the bitmap region so
    // bitmap descriptor pairs stay stride-2 aligned even when path runs
    // interleave with bitmap batches. UploadInstances writes both fields;
    // RecordDrawCommands' resolve allocator reads them.
    UINT lastFlushPathResolveBase_ = 0;  // offset (in slots) from lastFlushSrvBase_ to first resolve slot
    UINT lastFlushPathResolveCount_ = 0; // number of resolve slots reserved this flush

    // Helper to count snapshot blit batches for descriptor slot calculation
    UINT CountSnapshotBlitBatches() const {
        UINT count = 0;
        for (auto& b : batches_)
            if (b.type == DrawBatchType::SnapshotBlit) ++count;
        return count;
    }

    // DPI scale factor (1.0 = 96 DPI / 100%)
    float dpiScale_ = 1.0f;

    // Vello GPU path renderer
    std::unique_ptr<D3D12VelloRenderer> velloRenderer_;
    bool velloEnabled_ = true;

    // Swap chain format (queried at init, used for PSO creation)
    DXGI_FORMAT swapChainFormat_ = DXGI_FORMAT_R8G8B8A8_UNORM;

    // Per-frame instance/constant UPLOAD heap is grown on demand.  Starts at the
    // initial capacity below, doubles whenever UploadInstances or a mid-frame
    // constant write needs more space.  This keeps a quiescent app (e.g. the
    // single-button AOT demo) at a few hundred KB instead of 48 MB × kMaxFrames.
    static constexpr size_t kInitialInstanceCapacityBytes = 256 * 1024;       // 256 KB
    // Reserved tail (mid-frame CBs for backdrop blur, liquid glass, custom shader
    // effects).  UploadInstances pads the requested capacity by this amount so a
    // mid-frame write rarely needs to fall back even on the very first frame.
    static constexpr size_t kMidFrameReserveBytes = 64 * 1024;                // 64 KB
    // Sanity cap on instance / glyph / bitmap counts per frame — purely a safety
    // valve to refuse pathologically broken pages.  No longer drives buffer size.
    static constexpr size_t kMaxInstancesPerFrame = 262144;
    // Complex pages can flush graphics many times per frame (snapshot/blur/offscreen/liquid glass).
    // Give each frame a shader-visible descriptor region big enough to handle
    // a few hundred FlushGraphicsForCompute calls (8 SRV slots each) without
    // wrapping the ring and overwriting tables still referenced by earlier
    // draws on the same command list.  1024 × 32 B = 32 KB total — this is
    // shader-visible memory, kept tight on purpose now that we serve real
    // overhead reductions elsewhere.
    static constexpr UINT kMaxSrvDescriptors = 1024;

    // --- Snapshot resources (for backdrop filter, liquid glass) ---
    ComPtr<ID3D12Resource> snapshotTexture_;
    UINT snapshotW_ = 0, snapshotH_ = 0;
    bool snapshotValid_ = false;   // content validity for the current frame
    bool snapshotUsedThisFrame_ = false;
    D3D12_RESOURCE_STATES snapshotState_ = D3D12_RESOURCE_STATE_COMMON;

    // --- Desktop capture resources ---
    ComPtr<ID3D12Resource> desktopTexture_;
    ComPtr<ID3D12Resource> desktopUploadBuffer_;
    UINT desktopCaptureW_ = 0, desktopCaptureH_ = 0;
    bool desktopCaptureValid_ = false;
    D3D12_RESOURCE_STATES desktopTextureState_ = D3D12_RESOURCE_STATE_COMMON;

    // --- Offscreen render targets (for transition capture, 2 slots) ---
    ComPtr<ID3D12Resource> offscreenRT_[2];
    D3D12_RESOURCE_STATES offscreenRTState_[2] = { D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_COMMON };
    UINT offscreenW_ = 0, offscreenH_ = 0;
    float offscreenCaptureX_[2] = {};
    float offscreenCaptureY_[2] = {};
    bool offscreenCaptureValid_[2] = {};
    // Saved state during offscreen capture
    UINT savedFrameIndex_ = 0;
    bool inOffscreenCapture_ = false;
    bool offscreenResourcesUsedThisFrame_ = false;
    std::stack<D3D12_RECT> savedScissorStack_;  // scissor stack saved during offscreen capture

    // --- Gaussian blur compute resources ---
    ComPtr<ID3D12RootSignature> blurRootSignature_;
    ComPtr<ID3D12PipelineState> blurPSO_;
    ComPtr<ID3DBlob> blurCS_;

    // Non-shader-visible descriptor heap for blur UAV/SRV (CPU staging for CopyDescriptors)
    ComPtr<ID3D12DescriptorHeap> blurCpuHeap_;

    // Temporary textures for two-pass blur (lazily created / cached)
    ComPtr<ID3D12Resource> blurTempA_;  // copy of region + horizontal blur output
    ComPtr<ID3D12Resource> blurTempB_;  // vertical blur output
    UINT blurTempW_ = 0;
    UINT blurTempH_ = 0;
    D3D12_RESOURCE_STATES blurTempAState_ = D3D12_RESOURCE_STATE_COMMON;
    D3D12_RESOURCE_STATES blurTempBState_ = D3D12_RESOURCE_STATE_COMMON;
    bool blurResourcesReady_ = false;
    bool blurTempsUsedThisFrame_ = false;

    // Blur constants layout (must match cbuffer in shader)
    struct BlurConstants {
        uint32_t direction;   // 0 = horizontal, 1 = vertical
        float    radius;      // blur radius in pixels
        uint32_t texWidth;
        uint32_t texHeight;
    };

    // --- Liquid glass resources ---
    ComPtr<ID3D12RootSignature> lgRootSignature_;
    ComPtr<ID3D12PipelineState> lgPSO_;
    ComPtr<ID3DBlob> lgVS_;
    ComPtr<ID3DBlob> lgPS_;
    ComPtr<ID3D12Resource> lgConstantsBuffer_;
    void* lgConstantsMapped_ = nullptr;
    bool lgResourcesReady_ = false;

    bool CreateLiquidGlassResources();

    // Liquid glass constants layout (must match cbuffer in shader: 192 bytes = 12 float4)
    struct LiquidGlassConstants {
        // Register 0: glass rect
        float glassX, glassY, glassW, glassH;
        // Register 1: refraction params
        float cornerRadius, refractionHeight, refractionAmount, chromaticAberration;
        // Register 2: tint / vibrancy
        float vibrancy, tintR, tintG, tintB;
        // Register 3: tint opacity, highlight, light position (screen-space, -1 = no mouse)
        float tintOpacity, highlightOpacity, lightPosX, lightPosY;
        // Register 4: shadow
        float shadowOffset, shadowRadius, shadowOpacity, blurTexW;
        // Register 5: screen size + shape
        float scrW, scrH, shapeType, shapeN;
        // Register 6: fusion
        float neighborCount, fusionRadius, blurTexH, _pad2;
        // Registers 7-10: neighbor rects
        float n0x, n0y, n0w, n0h;
        float n1x, n1y, n1w, n1h;
        float n2x, n2y, n2w, n2h;
        float n3x, n3y, n3w, n3h;
        // Register 11: neighbor corner radii
        float n0r, n1r, n2r, n3r;
    };
    static_assert(sizeof(LiquidGlassConstants) == 192, "LiquidGlassConstants must be 192 bytes");

    // Blur the full snapshot for liquid glass refraction.
    // Result is in blurTempA_ (PIXEL_SHADER_RESOURCE state).
    bool BlurSnapshotForGlass(float blurRadius);

    // Geometry constants for liquid glass VS (16 bytes)
    struct LiquidGlassGeom {
        float x, y, w, h;
    };
};

} // namespace jalium
