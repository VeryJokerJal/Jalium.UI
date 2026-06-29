#pragma once

#include "d3d12_backend.h"
#include "d3d12_glyph_atlas.h"
#include "jalium_stencil_path.h"
#include <vector>
#include <stack>
#include <memory>
#include <atomic>

namespace jalium {

class D3D12VelloRenderer;  // forward declaration
class D3D12RetainedLayer;  // forward declaration (retained GPU layer fast path)

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

    // --- render transform (32 bytes, two 16-byte-aligned float4s) ---
    // Full 2x3 affine applied to the quad corners IN THE VERTEX SHADER. This
    // replaces the old CPU "transform the top-left + sign-stripped sqrt() scale"
    // bake, which collapsed rotation / negative-diagonal (180 deg) / skew into a
    // mispositioned axis-aligned quad. posX/posY/sizeX/sizeY/corner radii/border
    // width/gradient geometry are kept in the caller's pre-transform space; the
    // VS builds the local quad then applies this matrix. For an un-rotated element
    // this is identity and the output is bit-identical to the old path.
    float xfM11, xfM12, xfM21, xfM22;   // linear part   (offset 192)
    float xfDx, xfDy, _xfPad0, _xfPad1; // translation (offset 208); _xfPad0/_xfPad1 are
                                        // reused as shadowMode/sigma on the DropShadow path
                                        // (= HLSL xform1.zw) — don't repurpose/clear blindly
};
static_assert(sizeof(SdfRectInstance) == 224, "SdfRectInstance must be 224 bytes");

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

    // --- render transform (32 bytes, two 16-byte-aligned float4s) ---
    // Full 2x3 affine applied to the quad corners IN THE VERTEX SHADER (see the
    // matching note on SdfRectInstance). Lets rotated / 180-deg-flipped / skewed
    // bitmaps, images, retained-layer composites and offscreen blits render
    // correctly instead of being mispositioned by the old sign-stripped scale.
    // The corner-indexed UV naturally produces the correct content flip under a
    // 180-deg negative-diagonal transform. Identity => bit-identical to before.
    float xfM11, xfM12, xfM21, xfM22;   // linear part   (offset 48)
    float xfDx, xfDy, _xfPad0, _xfPad1; // translation   (offset 64)
};
static_assert(sizeof(BitmapQuadInstance) == 80, "BitmapQuadInstance must be 80 bytes");

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
    bool  roundedClipInverse = false;                 // true → keep outside, mask the interior

    // Text-only: this batch is deformed (transform-scaled) text and must be drawn
    // with the bilinear text PSO (smooth sub-pixel positioning under animation, no
    // per-glyph integer snapping / jitter). Normal text keeps the point PSO (crisp
    // at 1:1). Batches with different smoothText must not coalesce (different PSO).
    bool  smoothText = false;
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
    // When true the clip keeps the area OUTSIDE the rounded rect (masks the
    // interior) instead of the inside — see PushRoundedClipExclude.
    bool inverse = false;
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
    /// `reportTransientPresentFailure` selects how a transient (non
    /// device-loss) Present failure is reported. Callers running under
    /// external present pacing MUST pass true: a failed Present never signals
    /// the frame-latency waitable, so swallowing the error (returning OK)
    /// would strand the present credit the managed scheduler consumed at
    /// BeginDraw and starve its event-driven loop. With true, the failure
    /// surfaces as JALIUM_ERROR_PRESENT_FAILED; with false the legacy
    /// dropped-frame contract holds (return OK, next frame retries).
    JaliumResult EndFrame(bool useDirtyRects, const std::vector<D3D12_RECT>& dirtyRects, UINT syncInterval, UINT presentFlags, bool reportTransientPresentFailure);

    /// Aborts the current frame without submitting GPU work.
    /// Closes the command list and resets internal state so BeginFrame can be called again.
    /// Used when the frame must be discarded (e.g. during window resize).
    void AbortFrame();

    /// Closes commandList_ exactly once iff it is currently recording, keyed on
    /// the atomic cmdListRecording_ (the single source of truth for the list's
    /// real open/closed state — inFrame_ lags both BeginFrame's open and
    /// EndFrame's close). Returns true iff this call performed the Close.
    /// optional outCloseHr receives the Close() HRESULT (S_OK if nothing was
    /// open or the device is gone) so EndFrame can keep its device-lost
    /// classification. Idempotent: a second caller sees recording==false and
    /// no-ops, preventing an invalid double Close on the shared list.
    bool CloseCommandListIfOpen(HRESULT* outCloseHr = nullptr);

    /// Returns false when the device was removed mid-frame (GPU switch, driver
    /// restart, TDR), latching frameDeviceLost_ so EndFrame aborts instead of
    /// recording. Recording through a torn-down user-mode driver does not fail
    /// cleanly — NVIDIA's UMD is known to AV — so every mid-frame flush gates
    /// on this before touching UploadInstances/RecordDrawCommands.
    bool CheckFrameDeviceAlive();

    // --- Draw commands (called between BeginFrame/EndFrame) ---
    void AddSdfRect(const SdfRectInstance& inst);
    /// Records a text draw. `aaMode` (JALIUM_TEXT_AA_*) and `hintingMode`
    /// (0=Auto, 1=Fixed, 2=Animated) come from the source TextFormat's
    /// per-element TextOptions; they are forwarded straight to
    /// D3D12GlyphAtlas::GenerateGlyphs so the glyph cache stores one
    /// rasterization per (glyph, aaMode, hintingMode) tuple. Both default
    /// to 0 (Auto) for legacy callers that haven't been retrofitted to
    /// plumb per-format modes yet — those fall back to the process-wide
    /// jalium_text_set_global_antialias_mode value.
    void AddText(IDWriteTextLayout* layout, float x, float y,
                 float r, float g, float b, float a,
                 uint64_t layoutKey = 0,
                 int32_t aaMode = 0,
                 int32_t hintingMode = 0);
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

    // Emits one radial soft-glow dot (bright centre fading to a transparent
    // edge) — the building block of the continuous, tapered glow ribbon that
    // outlines the highlighted element.
    void EmitSoftGlowDot(float cx, float cy, float diameter,
                         float r, float g, float b, float peakAlpha);

    // --- Offscreen render target (for transition capture) ---
    bool BeginOffscreenCapture(int slot, float x, float y, float w, float h);
    void EndOffscreenCapture(int slot);
    void DrawOffscreenBitmap(int slot, float x, float y, float w, float h, float opacity);
    // Draw a cropped sub-region of the offscreen texture.
    // (x,y,w,h) = destination rect in DIP; (uvOffsetX/Y) = DIP offset into the capture.
    void DrawOffscreenBitmapCropped(int slot, float x, float y, float w, float h,
        float uvOffsetX, float uvOffsetY, float opacity);
    bool IsInOffscreenCapture() const { return inOffscreenCapture_; }
    bool IsInRetainedCapture() const { return inRetainedCapture_; }

    // --- Retained GPU layers (damage-driven composited-animation fast path) ---
    // Realize a content-clean subtree into a PERSISTENT texture once, then
    // composite it as a transformed/opacity quad each frame. Unlike the 2-slot
    // transient offscreen pool above, each layer owns its own texture (see
    // D3D12RetainedLayer) so N layers can coexist across frames.
    bool SupportsRetainedLayers() const { return true; }
    // Begin/End mirror BeginOffscreenCapture/EndOffscreenCapture but target the
    // layer's own RTV. existing may be null (a new layer is allocated). Returns
    // the layer (== existing on reuse), or nullptr on failure (caller falls back).
    D3D12RetainedLayer* BeginRetainedLayerCapture(D3D12RetainedLayer* existing,
                                                  float x, float y, float w, float h);
    void EndRetainedLayerCapture(D3D12RetainedLayer* layer);
    // Composite a realized layer as a quad at (x,y,w,h) in current space; honors
    // the live transform stack (scale+translate) + ambient opacity via AddBitmap.
    void CompositeRetainedLayer(D3D12RetainedLayer* layer,
                                float x, float y, float w, float h, float opacity);
    // Retire the layer's texture (fence-gated) and delete it.
    void DestroyRetainedLayer(D3D12RetainedLayer* layer);

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
    UINT GetViewportWidth() const { return viewportWidth_; }
    UINT GetViewportHeight() const { return viewportHeight_; }

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
    // Inverse clip: keeps the area OUTSIDE the rounded rect and masks the
    // interior. Do NOT pair it with PushScissor (that would re-confine drawing
    // back inside the rect). Used for outer-glow effects hugging an element.
    void PushRoundedClipExclude(float x, float y, float w, float h, float rx, float ry);
    void PopRoundedClip();
    bool HasRoundedClip() const { return !roundedClipStack_.empty(); }

    // Forced rounded-clip override, used when replaying snapshotted Impeller
    // batches.  Each Impeller batch carries the rounded-clip state it was
    // captured under (already projected to physical px); since batches now
    // accumulate across clip boundaries instead of forcing a flush, the live
    // roundedClipStack_ no longer matches.  The render target sets the forced
    // payload per batch so ResolveRoundedClipForBatch uses it instead of the
    // live stack.  SetForcedRoundedClipNone marks "this batch had no clip";
    // ClearForcedRoundedClip restores normal live-stack resolution.
    void SetForcedRoundedClip(const float rect[4], const float radii[4]) {
        forcedRoundedClipActive_ = true;
        forcedRoundedClipPresent_ = true;
        forcedRoundedClipRect_[0] = rect[0]; forcedRoundedClipRect_[1] = rect[1];
        forcedRoundedClipRect_[2] = rect[2]; forcedRoundedClipRect_[3] = rect[3];
        forcedRoundedClipRadii_[0] = radii[0]; forcedRoundedClipRadii_[1] = radii[1];
        forcedRoundedClipRadii_[2] = radii[2]; forcedRoundedClipRadii_[3] = radii[3];
    }
    void SetForcedRoundedClipNone() {
        forcedRoundedClipActive_ = true;
        forcedRoundedClipPresent_ = false;
    }
    void ClearForcedRoundedClip() {
        forcedRoundedClipActive_ = false;
        forcedRoundedClipPresent_ = false;
    }

    // Resolves the innermost live rounded clip into physical-pixel rect/radii
    // (same projection ResolveRoundedClipForBatch applies to its own batches).
    // The render target mirrors this into the Impeller engine so its batches
    // snapshot the matching clip.  Returns false when no rounded clip is active.
    bool ResolveCurrentRoundedClip(float outRect[4], float outRadii[4]) const;
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

    // Shared offscreen-capture atlas dimensions (physical px). The capture occupies
    // only the top-left (0,0)-(capturePx) sub-rect; callers that sample it must scale
    // UVs by capturePx/offscreen (see DrawOuterGlowEffect). 0 until first capture.
    UINT GetOffscreenWidth() const { return offscreenW_; }
    UINT GetOffscreenHeight() const { return offscreenH_; }
    UINT GetOffscreenCaptureW(int slot) const { return (slot >= 0 && slot <= 1) ? (UINT)offscreenCaptureW_[slot] : 0; }
    UINT GetOffscreenCaptureH(int slot) const { return (slot >= 0 && slot <= 1) ? (UINT)offscreenCaptureH_[slot] : 0; }

    // --- Path MSAA quality (1/2/4/8) ---
    // Takes effect at the next BeginFrame. Values are clamped to {1,2,4,8}.
    void SetPathMsaaSampleCount(uint32_t sampleCount);
    uint32_t GetPathMsaaSampleCount() const { return pathMsaaSampleCount_; }

    // --- Format ---
    DXGI_FORMAT GetSwapChainFormat() const { return swapChainFormat_; }

    // --- Queries ---
    bool IsInitialized() const { return initialized_; }
    ID3D12Device* GetDevice() const { return device_; }
    ID3D12GraphicsCommandList* GetCommandList() const { return commandList_.Get(); }
    // [JALIUM-921 diagnostic] Real open/closed state of commandList_ (see members).
    bool IsCommandListRecording() const { return cmdListRecording_.load(std::memory_order_acquire); }
    unsigned long CommandListOwnerThread() const { return cmdListOwnerThread_.load(std::memory_order_acquire); }
    bool IsInFrame() const { return inFrame_; }
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
    // Returns false when the device was removed (frameDeviceLost_ latched):
    // callers MUST then skip every GPU call that would otherwise follow the
    // flush (barriers, copies, dispatches, SRV creation) — recording through
    // a torn-down UMD AVs instead of failing — while still performing their
    // CPU-side state restore (capture flags, scissor/constants restore).
    [[nodiscard]] bool FlushGraphicsForCompute();

    // --- Vello GPU path renderer ---
    D3D12VelloRenderer* GetVelloRenderer() const { return velloEnabled_ ? velloRenderer_.get() : nullptr; }
    bool HasVelloPaths() const;
    void FlushVelloPaths();
    void ApplyScissorToVello();
    void SetVelloEnabled(bool enabled) { velloEnabled_ = enabled; }

    // TEST-ONLY (#921 Vello-output regression self-check). Must be called with the
    // command list already open. Reproduces the 'JaliumVelloOutput' orphan and reports
    // via *outAlive whether the output texture survived the mid-frame
    // bitmapTextures_.clear() (1 = parked on the fence-gated retired list by the fix,
    // 0 = freed while still referenced). Returns 0 when staged, negative otherwise.
    int32_t DebugForceVelloOutputOrphan(int32_t* outAlive);

    // 懒创建 Vello 子系统(root sig + 计算 PSO + 描述符堆 + 缓冲 + 驱动 compute
    // 上下文)。仅当 velloEnabled_(active engine==Vello)且尚未创建时真正构造。
    // 默认引擎是 Impeller → velloEnabled_=false → 整条 Vello 永不创建,省下其
    // 全部 GPU/驱动开销且零性能代价(Vello 本就不用)。引擎热切到 Vello 时,
    // 下一帧 BeginFrame 会按需创建。返回 true 表示创建后可用。
    bool EnsureVelloRenderer();

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
    // Rebuilds only the stencil/cover PSOs against the current
    // pathMsaaSampleCount_ (shaders + root sig + heaps are reused). Called once
    // from CreateStencilPathResources and again whenever the sample count
    // changes at a frame boundary.
    bool RebuildStencilCoverPSOs();
    // Applies a pending path-MSAA sample-count change (rebuild PSOs + force the
    // scratch RT/depth to recreate). Invoked from BeginFrame after the fence
    // wait so the old MSAA resources are guaranteed idle.
    void ApplyPendingPathMsaaSampleCount();
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
    // The path MSAA color/depth/resolve textures are shared across swap-chain
    // frames rather than stored in FrameResources. Track the last submission
    // that touched them so another in-flight frame cannot clear/resolve the
    // same textures before the GPU has finished reading the prior contents.
    uint64_t pathScratchFenceValue_ = 0;

    // ── Frame-pacing instrumentation (DevTools Perf tab) ────────────────
    // The native side surfaces this through QueryGpuStats so DevTools can
    // tell apart "BeginDraw is slow because GPU is busy" from "BeginDraw is
    // slow because the recording itself is slow". accumulatingFrameWaitNs_
    // sums up the fence-wait time across every BeginFrame attempt the
    // managed Window made for one logical frame (the 50 ms timeout +
    // 1 ms retry loop on slow iGPUs can pile up multiple failed attempts).
    // Once the BeginFrame finally succeeds we flush the accumulator into
    // lastFrameGpuWaitNs_, which is what QueryGpuStats reports.
    uint64_t accumulatingFrameWaitNs_ = 0;
    uint64_t lastFrameGpuWaitNs_ = 0;
    // QPC tick at the moment EndFrame issues queue Signal(fence_, …) for the
    // *previous* frame.  Diff against the QPC tick when the next BeginFrame
    // observes that fence as completed → wall clock between Present submit
    // and the swap chain being ready to draw again. NOT pure GPU work —
    // includes DWM composition + DXGI queue latency. The hardware-timestamp
    // GPU breakdown is the canonical "what did the GPU actually do" number.
    uint64_t lastPresentSignalQpc_ = 0;
    uint64_t lastFramePresentToReadyNs_ = 0;
    // Wall time EndFrame spent blocked inside the Present/Present1 call for
    // the most recent frame. Under a slow compositor (occlusion throttling,
    // remote/virtual displays) a vsync-aligned Present stalls until the DWM
    // retires the previous buffer — this isolates that stall from genuine
    // CPU encode work in the EndDraw timing row.
    uint64_t lastFramePresentBlockNs_ = 0;

    // ── GPU work breakdown via hardware timestamp queries ────────────────
    // Categories track GPU time per work-class so DevTools Perf can answer
    // "what is consuming the GPU?" (typically backdrop blur on iGPU). One
    // ID3D12QueryHeap of TIMESTAMP queries is partitioned per frame; each
    // BeginFrame records start, MarkGpuTimingPoint(category) records the
    // boundary between two categories, EndFrame records end + resolves the
    // queries into the per-frame readback buffer. The *next* frame's
    // BeginFrame reads the readback after the fence wait, decodes spans,
    // and accumulates into lastGpuTimingSnapshot_.
public:
    enum class GpuTimingCategory : uint8_t {
        Other = 0,
        SdfRect = 1,
        Text = 2,
        Bitmap = 3,
        Path = 4,
        Backdrop = 5,
        LiquidGlass = 6,
        kCount = 7,
        kFrameEnd = 0xFF,  // sentinel — not a real category, marks the trailing timestamp
    };

    /// Marks the boundary in command-list recording where the previous
    /// span ends and a new span (of `category`) begins. Issues a TIMESTAMP
    /// EndQuery on the open command list and remembers the category so
    /// the readback decoder knows where to accumulate the delta. Safe
    /// no-op when timing is disabled or no slot is available.
    void MarkGpuTimingPoint(GpuTimingCategory category);

    struct GpuTimingSnapshot {
        uint64_t totalNs = 0;
        uint64_t categoryNs[static_cast<size_t>(GpuTimingCategory::kCount)] = {};
        uint32_t batchCount = 0;
        bool valid = false;
    };
    GpuTimingSnapshot GetGpuTimingSnapshot() const { return lastGpuTimingSnapshot_; }

    // Accessors used by D3D12RenderTarget::QueryGpuStats. Returned values
    // are last-completed-frame snapshots — point-in-time, not accumulators.
    uint64_t GetLastFrameGpuWaitNs() const { return lastFrameGpuWaitNs_; }
    uint64_t GetLastFramePresentToReadyNs() const { return lastFramePresentToReadyNs_; }
    uint64_t GetLastFramePresentBlockNs() const { return lastFramePresentBlockNs_; }
private:
    // Decode the previous frame's resolved timestamps and update
    // lastGpuTimingSnapshot_. Called from BeginFrame after fence wait
    // confirms the GPU resolved the queries.
    void DecodeGpuTimingForCompletedFrame(UINT frameIndex);

    static constexpr UINT kMaxTimingSlotsPerFrame = 512;
    ComPtr<ID3D12QueryHeap> timingQueryHeap_;
    bool timingSupported_ = false;
    uint64_t timestampFrequency_ = 0;
    struct PerFrameTiming {
        ComPtr<ID3D12Resource> readback;
        UINT nextSlot = 0;
        std::vector<GpuTimingCategory> spanCategories;
        uint32_t batchCountAtFinalize = 0;
        bool hasResolvedData = false;
    };
    PerFrameTiming timing_[kMaxFrames];
    GpuTimingSnapshot lastGpuTimingSnapshot_{};

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
    ComPtr<ID3D12PipelineState> textSmoothPSO_;   // deformed text: bilinear atlas sampling
    ComPtr<ID3D12PipelineState> bitmapPSO_;
    ComPtr<ID3D12PipelineState> copyBlendPSO_;  // SDF rect with copy blend (no alpha blending)
    ComPtr<ID3D12PipelineState> trianglePSO_;  // flat-shaded triangle fill

    // Compiled shaders (cached)
    ComPtr<ID3DBlob> sdfRectVS_;
    ComPtr<ID3DBlob> sdfRectPS_;
    ComPtr<ID3DBlob> textVS_;
    ComPtr<ID3DBlob> textPS_;
    ComPtr<ID3DBlob> textSmoothPS_;   // bilinear variant of the text PS (deformed text)
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
    // Grow-only: sized to the high-water-mark of every content extent requested
    // (normally the window viewport; larger when a path is rendered inside an
    // oversized element-effect capture). pathMsaaWidth_/Height_ are the allocated
    // texels; the resolve blit samples just the used sub-region via a uv-scale.
    ComPtr<ID3D12Resource>       pathMsaaColor_;
    ComPtr<ID3D12Resource>       pathMsaaDepth_;
    ComPtr<ID3D12Resource>       pathResolveTexture_;
    ComPtr<ID3D12DescriptorHeap> pathMsaaRtvHeap_;
    ComPtr<ID3D12DescriptorHeap> stencilDsvHeap_;  // 8× DSV for pathMsaaDepth_
    UINT  pathMsaaWidth_  = 0;
    UINT  pathMsaaHeight_ = 0;
    // Path stencil-then-cover MSAA sample count. Runtime-configurable (1/2/4/8)
    // so low-end GPUs / high-load scenes can trade path edge AA quality for
    // GPU time — 8× is the highest quality default; 4× roughly halves the
    // path GPU cost. `pending` is applied at the next BeginFrame boundary
    // (GPU idle after the fence wait), which rebuilds the stencil/cover PSOs
    // and forces the MSAA scratch RT + depth to be recreated at the new count.
    UINT  pathMsaaSampleCount_        = 8;
    UINT  pendingPathMsaaSampleCount_ = 8;
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

    // Forced rounded-clip override (see SetForcedRoundedClip).  When active,
    // ResolveRoundedClipForBatch short-circuits to this snapshot instead of
    // reading roundedClipStack_.back().
    bool forcedRoundedClipActive_ = false;
    bool forcedRoundedClipPresent_ = false;
    float forcedRoundedClipRect_[4] = {0, 0, 0, 0};
    float forcedRoundedClipRadii_[4] = {0, 0, 0, 0};

    // Resolves the active rounded-clip stack into the per-batch payload that
    // RecordDrawCommands writes to the b2 root constants.  Returns false when
    // no rounded clip is active or the resolution failed (e.g. inverse-singular
    // transform); in that case the caller leaves DrawBatch.hasRoundedClip=false.
    bool ResolveRoundedClipForBatch(DrawBatch& batch) const;
    // Shared live-stack resolution backing both ResolveRoundedClipForBatch and
    // ResolveCurrentRoundedClip.
    bool ResolveLiveRoundedClip(float outRect[4], float outRadii[4], bool* outInverse = nullptr) const;
    float currentOpacity_ = 1.0f;
    float currentShapeType_ = 0.0f;  // 0 = RoundedRect, 1 = SuperEllipse
    float currentShapeN_ = 4.0f;

    // Frame state
    UINT viewportWidth_ = 0;
    UINT viewportHeight_ = 0;
    bool inFrame_ = false;
    // [JALIUM-921 diagnostic] True while commandList_ is actually open (recording),
    // tracked independently of inFrame_. inFrame_ is set late in BeginFrame (after
    // the list is already open and has recorded back-buffer barriers) and cleared
    // early in EndFrame (before Close), so it does NOT reflect the real open/closed
    // window of the list. cmdListOwnerThread_ records which thread opened the list,
    // so a resize on the UI thread Resetting resources the render thread's still-open
    // list references (the #921 OBJECT_DELETED_WHILE_STILL_IN_USE setup) is caught.
    std::atomic<bool> cmdListRecording_{false};
    std::atomic<unsigned long> cmdListOwnerThread_{0};
    // Latched by CheckFrameDeviceAlive / UploadInstances when the device is
    // removed mid-frame; EndFrame then aborts the frame and reports
    // DEVICE_LOST instead of recording into the dead driver. Reset on the
    // next successful BeginFrame.
    bool frameDeviceLost_ = false;

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
    // drawOrder_ snapshot taken right after the last CaptureSnapshot() copy.
    // If the caller invokes CaptureSnapshot a second time without any draw
    // having fired in between, the back buffer is still bit-identical and we
    // can skip the (expensive) CopyResource + barrier dance entirely. Reset
    // to a sentinel at BeginFrame so a stale value from the prior frame
    // never matches the new frame's drawOrder_=0.
    float snapshotCaptureDrawOrder_ = -1.0f;

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
    float offscreenCaptureW_[2] = {};   // recorded capture extent, physical px (pw) — sole UV divisor authority (closes F1)
    float offscreenCaptureH_[2] = {};
    bool offscreenCaptureValid_[2] = {};
    // Saved state during offscreen capture
    UINT savedFrameIndex_ = 0;
    bool inOffscreenCapture_ = false;
    bool offscreenResourcesUsedThisFrame_ = false;
    std::stack<D3D12_RECT> savedScissorStack_;  // scissor stack saved during offscreen capture

    // --- Retained layer capture (shares the RT-redirect machinery; mutually
    //     exclusive with offscreen capture to avoid nested RT redirects) ---
    bool inRetainedCapture_ = false;
    class D3D12RetainedLayer* activeRealizeLayer_ = nullptr;

    // --- Active capture target (offscreen OR retained) ---
    // Stashed by BeginOffscreenCapture / BeginRetainedLayerCapture and consumed
    // by the stencil-path arm (exitPathMode in RecordDrawCommands) so the path
    // resolve/blit composites into the capture texture instead of leaking onto
    // the swap-chain back buffer. captureViewportW_/H_ are the capture rect in
    // physical px (pw/ph) — the viewport restored for in-capture batches that
    // follow a path run. Only read while inOffscreenCapture_ || inRetainedCapture_
    // is true (both reset by BeginFrame/AbortFrame), so a stale handle after an
    // aborted capture is never dereferenced. Cleared by End*Capture for hygiene.
    D3D12_CPU_DESCRIPTOR_HANDLE captureRtv_ = {};
    UINT captureViewportW_ = 0;
    UINT captureViewportH_ = 0;

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
    // [#921] Set once the path-stencil MSAA scratch (pathMsaaColor_/Depth_/
    // pathResolveTexture_) has been bound into the open command list this frame;
    // blocks EnsureStencilDepthBuffer from regrowing (Reset+recreate) the scratch
    // mid-frame, which would free a resource the still-open list references →
    // #921 OBJECT_DELETED_WHILE_STILL_IN_USE at Close. Reset each BeginFrame.
    bool pathMsaaUsedThisFrame_ = false;

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
