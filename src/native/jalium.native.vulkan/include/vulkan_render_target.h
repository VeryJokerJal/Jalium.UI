#pragma once

#include "jalium_backend.h"
#include "jalium_types.h"
#include "jalium_rendering_engine.h"
#include "jalium_path_cache.h"
#include "vulkan_impeller_engine.h"
#include "vulkan_vello_engine.h"

#include <memory>
#include <string>
#include <unordered_map>
#include <vector>

namespace jalium {

class VulkanBackend;
class VulkanImportedVideoSurface;

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
    void FillPerCornerRoundedRectangle(float x, float y, float w, float h,
        float tl, float tr, float br, float bl, Brush* brush) override;
    void DrawPerCornerRoundedRectangle(float x, float y, float w, float h,
        float tl, float tr, float br, float bl, Brush* brush, float strokeWidth) override;
    void FillEllipse(float cx, float cy, float rx, float ry, Brush* brush) override;
    void DrawEllipse(float cx, float cy, float rx, float ry, Brush* brush, float strokeWidth) override;
    void FillEllipseBatch(const float* data, uint32_t count) override;
    void DrawLine(float x1, float y1, float x2, float y2, Brush* brush, float strokeWidth) override;
    void FillPolygon(const float* points, uint32_t pointCount, Brush* brush, int32_t fillRule) override;
    void DrawPolygon(const float* points, uint32_t pointCount, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin = 0, float miterLimit = 10.0f) override;
    void FillPath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule, int32_t edgeMode = -1) override;
    void StrokePath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin = 0, float miterLimit = 10.0f, int32_t lineCap = 0, const float* dashPattern = nullptr, uint32_t dashCount = 0, float dashOffset = 0.0f, int32_t edgeMode = -1) override;
    void DrawContentBorder(float x, float y, float w, float h, float blRadius, float brRadius, Brush* fillBrush, Brush* strokeBrush, float strokeWidth) override;
    void RenderText(const wchar_t* text, uint32_t textLength, TextFormat* format, float x, float y, float w, float h, Brush* brush) override;
    void PushTransform(const float* matrix) override;
    void PopTransform() override;
    void PushClip(float x, float y, float w, float h) override;
    void PushClipAliased(float x, float y, float w, float h) override;
    void PushRoundedRectClip(float x, float y, float w, float h, float rx, float ry) override;
    void PushPerCornerRoundedRectClip(float x, float y, float w, float h,
        float tl, float tr, float br, float bl) override;
    // TRUE inverse rounded clip (D3D12 PushRoundedClipExclude parity): keeps
    // the area OUTSIDE the rounded rect. Pushes a clipStack_ entry flagged
    // exclude — no scissor tightening — and the GPU replay path masks the
    // interior via clipFlags.x == 2 in the inverse-capable fragment shaders.
    void PushRoundedRectClipExclude(float x, float y, float w, float h,
        float rx, float ry) override;
    void PopClip() override;
    void PunchTransparentRect(float x, float y, float w, float h) override;
    void PushOpacity(float opacity) override;
    void PopOpacity() override;
    void SetShapeType(int type, float n) override;
    void SetVSyncEnabled(bool enabled) override;
    // Vulkan has no stencil-then-cover MSAA path renderer (paths tessellate to
    // the triangle-fill pipeline); the base contract lets such backends map
    // this knob differently. We treat it as a path-quality control: a higher
    // sample count raises the curve tessellation density (FillPath cache bucket
    // + StrokePath flatten tolerance), smoothing curved path edges at the cost
    // of more geometry — the same quality-vs-GPU-time tradeoff D3D12 exposes.
    void SetPathMsaaSampleCount(uint32_t sampleCount) override;
    void SetDpi(float dpiX, float dpiY) override;
    void AddDirtyRect(float x, float y, float w, float h) override;
    void SetFullInvalidation() override;
    bool SupportsPartialPresentation() const override;
    void DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity) override;
    void DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity, int scalingMode) override;
    void DrawVideoSurface(VideoSurface* surface, float x, float y, float w, float h,
                          float opacity, int scalingMode) override;
    // Composites a VulkanInkLayerBitmap onto the frame. The handle is the
    // backend-native pointer (the C ABI already unwrapped its wrapper). The ink
    // image is resident on the same device, so this samples it directly (no
    // per-frame upload) via the bitmap pipeline — matching D3D12 BlitInkLayer.
    void BlitInkLayer(void* inkLayerBitmap, float dstX, float dstY, float opacity) override;
    // Retained layers (damage-driven composited-animation fast path). Implemented
    // via the CPU-pixel capture model (mirrors transition capture): the subtree
    // rasterizes once into a sub-region pixel snapshot, then composites as a
    // transformed/opacity bitmap quad on subsequent frames. Env-gated default OFF
    // (JALIUM_VK_RETAINED_LAYERS) — opt-in until runtime-validated, exactly like
    // the GPU stencil path. When OFF, SupportsRetainedLayers() returns false and
    // the managed side falls back to full re-emission (correct, just no fast path).
    bool  SupportsRetainedLayers() const override;
    void* RealizeLayerBegin(void* existingLayer, float x, float y, float w, float h) override;
    void  RealizeLayerEnd(void* layer) override;
    void  CompositeLayer(void* layer, float x, float y, float w, float h, float opacity) override;
    // DestroyRetainedLayer frees OUR layers; a handle we don't own is FOREIGN
    // (another backend's layer drained here after a device-lost fallback) and is
    // left as a bounded, logged leak rather than freed through an unknown type.
    void DestroyRetainedLayer(void* layer) override;
    void DrawBackdropFilter(float x, float y, float w, float h, const char* backdropFilter, const char* material, const char* materialTint, float tintOpacity, float blurRadius, float cornerRadiusTL, float cornerRadiusTR, float cornerRadiusBR, float cornerRadiusBL) override;
    void DrawBackdropFilterEx(float x, float y, float w, float h, const char* backdropFilter, const char* material, const char* materialTint, float tintOpacity, float blurRadius, float noiseIntensity, float saturation, float luminosity, float cornerRadiusTL, float cornerRadiusTR, float cornerRadiusBR, float cornerRadiusBL) override;
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
    // Outer glow over the captured element content. D3D12 paints 7 concentric
    // expanding equal-alpha SDF rounded-rects then composites the capture; the
    // Vulkan port reaches the same soft-glow result through the proven
    // DrawDropShadowEffect path — a centred (zero-offset) blurred, tinted
    // silhouette of the capture spread outward by glowSize, with the crisp
    // content composited on top. Without this override the base no-op never even
    // composites the capture, so a glowing element renders NOTHING on Vulkan.
    void DrawOuterGlowEffect(float x, float y, float w, float h,
        float glowSize, float r, float g, float b, float a, float intensity,
        float uvOffsetX, float uvOffsetY,
        float cornerTL, float cornerTR, float cornerBR, float cornerBL) override;
    // Real per-pixel implementations over the captured element content (D3D12
    // currently stubs both — Vulkan ends up ahead here). The 5x4 color matrix
    // and the directional emboss run on the captured buffer, then composite
    // through the same GPU pixel-buffer + CPU BlendBuffer path the other
    // effect-capture effects use.
    void DrawColorMatrixEffect(float x, float y, float w, float h, const float* matrix) override;
    // Inner shadow: a soft dark frame hugging the INNER edge of the element
    // (the inverse of OuterGlow), drawn on top of the content and clipped to the
    // element bounds. GPU-path implementation via concentric clipped rounded-rect
    // strokes (no offscreen capture). Note: D3D12 currently no-ops this effect.
    void DrawInnerShadowEffect(float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY,
        float r, float g, float b, float a,
        float cornerTL, float cornerTR, float cornerBR, float cornerBL) override;
    void DrawEmbossEffect(float x, float y, float w, float h,
        float amount, float lightDirX, float lightDirY, float relief) override;
    void DrawShaderEffect(float x, float y, float w, float h,
        const uint8_t* shaderBytecode, uint32_t shaderBytecodeSize,
        const float* constants, uint32_t constantFloatCount) override;
    // HLSL-source custom shader: compiles the SM6 PS via DXC→SPIR-V at runtime
    // and runs it over the captured element content (Texture2D@t0 / Sampler@s0,
    // constants in cbuffer@b0, uv 0..1 element-local — matching the D3D12
    // convention). Falls back to drawing the captured content unmodified when
    // DXC / compilation is unavailable.
    void DrawShaderEffectFromSource(float x, float y, float w, float h,
        const char* hlslSource, const float* constants, uint32_t constantFloatCount) override;
    void DrawLiquidGlass(float x, float y, float w, float h, float cornerRadius, float blurRadius, float refractionAmount, float chromaticAberration, float tintR, float tintG, float tintB, float tintOpacity, float lightX, float lightY, float highlightBoost, int shapeType, float shapeExponent, int neighborCount, float fusionRadius, const float* neighborData) override;

    /// Override: set rendering engine with hot-switch support.
    JaliumResult SetRenderingEngine(JaliumRenderingEngine engine) override {
        JaliumRenderingEngine resolved = ResolveRenderingEngine(engine, JALIUM_BACKEND_VULKAN);
        pendingEngine_ = resolved;
        if (!isDrawing_) {
            activeEngine_ = resolved;
        }
        // Lazy-construct the engine that the user just opted into so the
        // first frame after a switch already has a valid encoder.
        if (resolved == JALIUM_ENGINE_IMPELLER) EnsureImpellerEngine();
        else if (resolved == JALIUM_ENGINE_VELLO) EnsureVelloEngine();
        return JALIUM_OK;
    }

    /// Override: report glyph atlas / path / texture usage for DevTools Perf tab.
    JaliumResult QueryGpuStats(JaliumGpuStats* out) const override;

    /// Override: hardware-timestamp GPU breakdown for the previous frame.
    /// Vulkan implementation issues vkCmdWriteTimestamp at category boundaries
    /// (one VkQueryPool slot per boundary), resolves them through
    /// vkGetQueryPoolResults once the per-frame fence is observed complete,
    /// and reports the decoded snapshot here. Returns JALIUM_OK with
    /// timingValid == 0 on the very first frame after init (the previous
    /// frame's data hasn't been collected yet) and when the physical device
    /// does not support timestamp queries on the graphics queue.
    JaliumResult QueryGpuTiming(JaliumGpuTimingStats* out) const override;

    /// Override: report current Vulkan swap chain configuration —
    /// mapped from VkPresentModeKHR / swap image count / composition mode
    /// so DevTools' "Present" status line shows "FIFO 3-buf composition"
    /// or "IMMEDIATE 2-buf" right next to the D3D12 backend's equivalent.
    /// Vulkan has no frame-latency-waitable equivalent, so those fields
    /// are always 0 / not-applicable in the returned struct.
    JaliumResult GetPresentInfo(JaliumPresentInfo* out) const override;

    /// Override: drop the host-side path-geometry and text-rasterization caches
    /// when the managed reclaimer signals idle. Both caches hold
    /// `std::shared_ptr` payloads, so any in-flight render command keeps its
    /// own entry alive past the eviction — the call is therefore safe to
    /// invoke between frames without `vkDeviceWaitIdle`. GPU-resident upload
    /// buffers / descriptor sets stay put: those live in the per-frame ring
    /// and are recycled by the swapchain anyway.
    JaliumResult ReclaimIdleResources() override;

    /// --- Two-phase back-buffer readback (parity verification; see base) ---
    /// Arms a one-shot capture: the NEXT EndDraw records a swapchain-image →
    /// host-visible-buffer copy (vkCmdCopyImageToBuffer) inside DrawFrame /
    /// DrawReplayFrame, right before the PRESENT-transition barrier, and tags
    /// it with that frame's slot fence. FetchReadback waits that fence and
    /// copies tightly-packed BGRA8 rows out (B8G8R8A8 swapchains copy
    /// through; R8G8B8A8 swizzles R↔B per pixel). Returns NOT_SUPPORTED when
    /// the surface lacks TRANSFER_SRC usage on its swapchain images.
    JaliumResult RequestReadback() override;
    JaliumResult FetchReadback(uint8_t* buf, uint32_t bufStride,
                               int32_t* outWidth, int32_t* outHeight) override;

    /// --- F8: TEST-ONLY device-lost injection (see RenderTarget base) ---
    /// Simulate VK_ERROR_DEVICE_LOST by tripping the sticky device-lost latch
    /// so the next BeginDraw/EndDraw drives the managed recovery chain.
    bool DebugRemoveDevice() override;
    /// orphaned is always 0 (Vulkan has no cross-generation retained-layer
    /// orphan path); graveyard is the cumulative fence-gated destroy count
    /// across the retained-layer / upload-image / buffer graveyards.
    bool DebugGetRetainedDestroyCounts(uint64_t* orphaned, uint64_t* graveyard) override;
    /// The live VkDevice handle as an integer (0 when torn down).
    uint64_t DebugDevicePointer() override;
    /// True while any offscreen/transition capture region is open.
    bool DebugInOffscreenCapture() override;
    // DebugForceLeakedCommandListResize / DebugForceVelloOutputOrphan are
    // D3D12-specific #921 injections with no Vulkan analogue (no open command
    // list spans a resize; Vello output is fence-gated like every resource),
    // so they are intentionally NOT overridden — the base returns -1.

    /// Returns true if the active engine is Impeller.
    bool IsImpellerActive() const { return activeEngine_ == JALIUM_ENGINE_IMPELLER; }
    /// Returns true if the active engine is Vello.
    bool IsVelloActive() const { return activeEngine_ == JALIUM_ENGINE_VELLO; }

    /// Lazily construct the Impeller engine. Returns true if the engine is
    /// alive after the call. Idempotent — safe to call every frame.
    bool EnsureImpellerEngine();
    /// Lazily construct the Vello engine. Returns true if the engine is alive
    /// after the call. Idempotent.
    bool EnsureVelloEngine();

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
        // Inverse clip (PushRoundedRectClipExclude): keep the area OUTSIDE the
        // (rounded) rect and mask its interior. Exclude entries contribute NO
        // axis-aligned scissor tightening (the excluded region's complement is
        // unbounded) — mirrors D3D12's PushRoundedClipExclude, which pushes no
        // scissor either.
        bool exclude = false;
        // PushClipAliased entry — managed uses it exclusively for the window
        // dirty-region clip. Effect captures skip aliased entries while
        // recording (see effectCaptureClipSuspendDepth_) so the isolated
        // offscreen content is never truncated by the damage rect; real
        // ancestor clips still apply.
        bool aliased = false;
        float x = 0.0f;
        float y = 0.0f;
        float w = 0.0f;
        float h = 0.0f;
        // Per-corner radii (TL, TR, BR, BL) — for the symmetric variant the
        // four are populated with the same value, so consumers don't need to
        // branch on rectangular vs per-corner.
        float radiusTL = 0.0f;
        float radiusTR = 0.0f;
        float radiusBR = 0.0f;
        float radiusBL = 0.0f;
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
        // BitmapScalingMode.NearestNeighbor (3): sample through the point
        // (VK_FILTER_NEAREST) sampler at replay so pixel art stays crisp.
        // Every other mode keeps the shared linear/anisotropic frameSampler
        // (Vulkan collapses D3D12's linear-vs-aniso split into that single
        // high-quality sampler, so HighQuality semantics are unchanged).
        bool useNearestSampler = false;
        // Linux self-hosted ClearType staging. Pixels carry B/G/R channel
        // coverage (A=max coverage), not ordinary image colour.
        bool lcdCoverage = false;
        float lcdTextR = 0.0f;
        float lcdTextG = 0.0f;
        float lcdTextB = 0.0f;
        float lcdTextA = 1.0f;
    };

    struct GpuFilledPolygonCommand {
        // Either an owning vertex buffer (rare — used by rasterize-fallback
        // paths that triangulate ad hoc) OR a shared_ptr into a cached path's
        // local-space triangle list (hot path: FillPath cache hits). The
        // latter avoids both the per-call heap allocation and the per-vertex
        // CPU transform — the CPU now records just (sharedTriangleVertices,
        // transform) and the GPU vertex shader applies the affine transform
        // when it samples each vertex.
        std::vector<float> triangleVertices;
        std::shared_ptr<const std::vector<float>> sharedTriangleVertices;
        const std::vector<float>& GetTriangleVertices() const {
            return sharedTriangleVertices ? *sharedTriangleVertices : triangleVertices;
        }
        // Affine transform applied by the vertex shader. Identity by default
        // (rasterize-fallback paths still pre-transform their vertices).
        float transformRow0[4] = { 1.0f, 0.0f, 0.0f, 0.0f }; // (m11, m12, dx, _)
        float transformRow1[4] = { 0.0f, 1.0f, 0.0f, 0.0f }; // (m21, m22, dy, _)
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
        // When true (element BlurEffect on the GPU replay path), `pixels` is empty
        // and the blur source is the LIVE composited frame: the replay blits the
        // element's just-rendered screen region (x,y,w,h, physical px) into the
        // shared upload image and the existing blur pipeline samples it. This is the
        // GPU compositor path — no per-element offscreen render target. Mirrors how
        // Backdrop/LiquidGlass already source from the live scene.
        bool captureLiveScene = false;
        // env JALIUM_VK_EFFECT_GPU_RT: like captureLiveScene, but the source is
        // the ISOLATED element content in the offscreen effect RT (the element's
        // [x,y,w,h] region blitted into the upload image at replay time). The
        // matching OffscreenEnd composite-back was suppressed, so the blurred
        // output REPLACES the element. Falls back to captureLiveScene semantics
        // when the offscreen degraded at replay (the element then rendered into
        // the main frame directly).
        bool captureOffscreen = false;
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
        int shapeType = 0;          // 0 = rounded-rect, 1 = super-ellipse
        float shapeExponent = 4.0f; // super-ellipse exponent
        int neighborCount = 0;      // 0..4 fused neighbours
        float fusionRadius = 0.0f;
        // Up to 4 neighbours, 5 floats each (x, y, w, h, cornerRadius).
        float neighborData[20] = {};
    };

    struct GpuBackdropCommand {
        uint32_t pixelWidth = 0;
        uint32_t pixelHeight = 0;
        std::vector<uint8_t> pixels;
        // Normal in-app backdrops sample the live GPU scene.  In that mode no
        // full-window CPU snapshot is carried by the command; replay captures
        // only sourceX/sourceY/sourceW/sourceH (including the blur apron) and
        // optionally downsamples it into the shared upload image.
        bool captureLiveScene = false;
        bool sampleScreenSpace = false;
        int32_t sourceX = 0;
        int32_t sourceY = 0;
        int32_t sourceW = 0;
        int32_t sourceH = 0;
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
        // Brightness multiplier applied after tint/saturation (MicaEffect raises
        // perceived brightness a few percent). 1 = unchanged. Field-for-field
        // parity with the D3D12 snapshot-backdrop shader's luminosity.
        float luminosity = 1.0f;
        // Source-UV remap: quad-local uv [0,1] over the panel -> source-texel uv.
        // The in-app path samples uploadImage, which holds the FULL captured frame
        // (CaptureLiveSceneToUpload blits [0,0,pixelWidth,pixelHeight]), so the
        // panel occupies only the sub-rect (x,y,w,h)/(pixelWidth,pixelHeight) of
        // it. Without this remap the shader stretched the whole frame across the
        // panel (only the frame-centred panel centre happened to align), which is
        // why the in-app backdrop diverged from D3D12's uvRemap path. The desktop
        // path passes identity (0,0,1,1) because its capture fills the quad 1:1.
        float uvOffsetX = 0.0f;
        float uvOffsetY = 0.0f;
        float uvScaleX = 1.0f;
        float uvScaleY = 1.0f;
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
        // env JALIUM_VK_EFFECT_GPU_RT (C6): when true the from/to content was captured
        // into transitionImages[0]/[1] via the GPU offscreen RT (no CPU pixels are carried
        // and fromPixels/toPixels stay empty). The replay skips the CPU-pixel upload and
        // samples the two persistent slot images directly. slotElemW/H are the element's
        // physical-pixel size, used to build uvScale = elemPx / transitionImage size (the
        // element sits at the top-left origin of each slot image, matching the D3D12
        // offscreenRT / uvScale convention exactly).
        bool useSlotImages = false;
        float slotElemW = 0.0f;
        float slotElemH = 0.0f;
        // env JALIUM_VK_EFFECT_GPU_RT (C6): a single-slot GPU-RT composite (the
        // DrawCapturedTransition particle path). progress is pre-set to fully select
        // singleSlotIndex (0 -> from, 1 -> to) and mode is the default crossfade, so
        // the shader collapses to that one slot's content. Only that slot needs to be
        // captured this frame; the other slot image is still sampled (weight 0) so the
        // capacity phase leaves both images SHADER_READ to keep the sample well-defined.
        bool singleSlot = false;
        int singleSlotIndex = 0;
    };

    // Custom pixel-shader effect (DrawShaderEffectFromSource). Holds the
    // cropped captured element content (BGRA), the rect, the FNV-1a hash of the
    // HLSL source (the compiled pipeline is cached in the Impl and looked up by
    // this hash at replay time, keeping Vulkan types out of this header), and
    // the user constant floats bound to cbuffer@b0.
    struct GpuCustomShaderCommand {
        std::vector<uint8_t> pixels;
        uint32_t pixelWidth  = 0;
        uint32_t pixelHeight = 0;
        float    x = 0.0f;
        float    y = 0.0f;
        float    w = 0.0f;
        float    h = 0.0f;
        uint64_t shaderHash = 0;
        std::vector<float> constants;
        // env JALIUM_VK_EFFECT_GPU_RT: `pixels` is EMPTY and the shader input is
        // the isolated element content in the offscreen effect RT (the [x,y,w,h]
        // rect blitted into uploadImage[0,0,w,h] at replay time, i.e. the same
        // destination the staging upload would fill). Used by user custom
        // shaders and by the built-in ColorMatrix / Emboss shaders.
        bool sampleOffscreen = false;
        // Replay patches constants[4..7] with (1/uploadW, 1/uploadH, uvScaleX,
        // uvScaleY) after the UBO memcpy: per-texel sampling info only the
        // replay knows (the upload image capacity is a per-frame decision).
        // The Emboss shader consumes it; requires constants.size() >= 8.
        bool patchUvInfo = false;
    };

    // Per-vertex-coloured triangle batch — the GPU analogue of D3D12's
    // AddTriangles(TriangleVertex*, count) path. Each vertex carries its
    // own RGBA (pre-multiplied), letting the pixel shader interpolate alpha
    // across the primitive. Used by DrawLine to render 3-strip manual AA
    // (core + 2 feather strips) so oblique strokes don't show GPU rasterizer
    // staircase aliasing. Vertex layout matches the vc shader: 6 floats per
    // vertex (x, y, r, g, b, a) — `vertices.size()` is `vertexCount * 6`.
    struct GpuVcTrianglesCommand {
        std::vector<float> vertices;   // interleaved [x, y, r, g, b, a, ...]
        uint32_t vertexCount = 0;
    };

    // Composites a resident ink-layer image (InkCanvas committed-ink layer)
    // onto the frame. Holds a non-owning pointer to the VulkanInkLayerBitmap
    // (kept alive for the frame by the managed InkCanvas); DrawReplayFrame reads
    // its VkImageView and samples it through the bitmap pipeline. Stored as
    // void* so the GpuReplayCommand structs stay free of Vulkan types.
    struct GpuInkLayerCommand {
        void*    inkLayer = nullptr;     // VulkanInkLayerBitmap* (non-owning)
        // Transformed axis-aligned draw rect (same convention as GpuBitmapCommand:
        // x/y = bbox min, w/h = bbox extent). The sampled UV always spans the
        // whole ink image (uvScale = 1), since the descriptor binds that image.
        float    x        = 0.0f;
        float    y        = 0.0f;
        float    w        = 0.0f;
        float    h        = 0.0f;
        float    opacity  = 1.0f;
    };

    struct GpuExternalVideoCommand {
        void* surface = nullptr; // VulkanImportedVideoSurface* (non-owning)
        float x = 0.0f;
        float y = 0.0f;
        float w = 0.0f;
        float h = 0.0f;
        float opacity = 1.0f;
    };

    // A shaped text run rendered through the dedicated text-glyph pipeline.
    // Holds the per-glyph instances as raw floats — 12 per glyph, matching the
    // 48-byte VkGlyphInstance layout (posX,posY, sizeX,sizeY, uvMinX,uvMinY,
    // uvMaxX,uvMaxY, colorR,colorG,colorB,colorA; colour PREMULTIPLIED, colorR<0
    // = colour-emoji sentinel). Stored as raw floats (not VkGlyphInstance) so this
    // header stays free of the Windows-only DirectWrite/atlas types, matching the
    // GpuVcTrianglesCommand convention. DrawReplayFrame uploads them into the glyph
    // SSBO and issues vkCmdDraw(6, glyphCount, ...) sampling the text atlas.
    struct GpuTextRunCommand {
        std::vector<float> glyphs;     // 12 floats per glyph instance
        uint32_t glyphCount = 0;
        bool clearType = false;        // select dual-source ClearType pipeline at replay
        bool smoothText = false;       // linear for Animated/rotated text; point for pixel-snapped display text
    };

    // Painter-order marker for the engine (Impeller / Vello CPU-tessellation)
    // batches: renders the batch sub-range [firstBatch, firstBatch + batchCount)
    // at this exact point of the replay stream, replacing the legacy frame-end
    // drain that painted every engine path on top of the whole frame. Indices
    // are stable: the engines' batch vectors only grow during a frame
    // (ClearBatches runs in EndDraw after consumption), so a span emitted early
    // keeps referring to the same batches at replay time.
    struct GpuEngineBatchSpanCommand {
        uint32_t firstBatch = 0;
        uint32_t batchCount = 0;
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
        Transition,
        VcTriangles,    // per-vertex-coloured triangle list (DrawLine 3-strip AA, etc.)
        InkLayer,       // composite a resident ink-layer image (InkCanvas)
        ExternalVideo,  // sample an imported Linux RGB dma-buf directly
        CustomShader,   // runtime-compiled custom pixel-shader effect
        TextRun,        // shaped text run via the dedicated text-glyph pipeline (Windows DirectWrite)
        OffscreenBegin, // env JALIUM_VK_EFFECT_GPU_RT: open the offscreen effect RT; subsequent replay
                        // commands render into it (isolated) until OffscreenEnd. Carries the element
                        // rect in solidRect.x/y/w/h for the sampling uvScale.
        OffscreenEnd,   // close the offscreen effect RT; its view becomes the effect sampling source.
        EngineBatchSpan, // drain the engine batches [firstBatch, firstBatch + batchCount) at THIS
                         // point of the stream — painter-order interleaving (B1). Payload in
                         // engineBatchSpan.
        TransitionCaptureBegin, // env JALIUM_VK_EFFECT_GPU_RT (C6): open an offscreen region whose
                                // isolated content becomes transition slot [transitionCaptureSlot].
                                // Reuses the effect offscreen RT redirect (CLEAR first draw / LOAD
                                // after); the child element draws stay engine/replay-encoded and are
                                // redirected into effectOffscreenImage exactly like an effect capture.
        TransitionCaptureEnd,   // close the region and blit effectOffscreenImage[element phys rect] to
                                // transitionImages[transitionCaptureSlot] at the (0,0) origin, then
                                // leave it SHADER_READ_ONLY. The element's phys rect rides in
                                // solidRect.x/y/w/h. Makes the slot a valid transition sampling source.
        RetainedLayerCaptureBegin, // C7 (env JALIUM_VK_RETAINED_LAYERS, default ON): open an offscreen
                                   // region whose isolated content becomes the persistent per-layer image
                                   // keyed by retainedLayerKey. Reuses the SAME effect-offscreen redirect
                                   // (CLEAR first draw / LOAD after) as TransitionCaptureBegin; the child
                                   // subtree's draws stay engine/replay-encoded and are redirected into
                                   // effectOffscreenImage exactly like an effect / transition capture.
        RetainedLayerCaptureEnd,   // close the region, ensure the element-sized per-layer image for
                                   // retainedLayerKey exists (create/resize), and blit
                                   // effectOffscreenImage[element phys rect] into it at the (0,0) origin,
                                   // leaving it SHADER_READ_ONLY. The element's PHYSICAL-pixel rect rides
                                   // in solidRect.x/y/w/h. Makes the layer a valid composite sampling
                                   // source that PERSISTS across frames (composite-only frames sample it
                                   // with no capture marker present).
    };

    struct GpuReplayCommand {
        GpuReplayCommandKind kind = GpuReplayCommandKind::SolidRect;
        // OffscreenEnd markers only (env JALIUM_VK_EFFECT_GPU_RT): skip the
        // automatic composite-back of the isolated element. Set retroactively by
        // TrySuppressPendingOffscreenComposite() when the effect that follows
        // REPLACES the element with its processed result (blur / color-matrix /
        // emboss / custom shader); drop shadow / outer glow keep the composite.
        bool offscreenSuppressComposite = false;
        // env JALIUM_VK_EFFECT_GPU_RT (C6): for TransitionCaptureBegin / TransitionCaptureEnd
        // markers, which transition slot (0 = old / 1 = new) this region targets. -1 for every
        // other command. The element's PHYSICAL-pixel rect for the End blit rides in solidRect.
        int transitionCaptureSlot = -1;
        // C7 (env JALIUM_VK_RETAINED_LAYERS): the retained-layer handle this command targets.
        // Non-null on RetainedLayerCaptureBegin / RetainedLayerCaptureEnd markers (whose element
        // PHYSICAL-pixel rect rides in solidRect) AND on the Bitmap COMPOSITE command emitted by
        // CompositeLayer — that Bitmap carries NO CPU pixels and instead samples the persistent
        // per-layer image bound to this key at replay. nullptr for every other command (which keep
        // the shared upload-image path). The key is the outer VulkanRetainedLayer* (stable for the
        // layer's lifetime); the replay side owns the matching GPU image in retainedLayerGpuImages_.
        const void* retainedLayerKey = nullptr;
        bool hasScissor = false;
        int32_t scissorLeft = 0;
        int32_t scissorTop = 0;
        int32_t scissorRight = 0;
        int32_t scissorBottom = 0;
        // SolidRect analytic-effect tag. Positive mode is soft drop-shadow:
        // render the rounded rect carried in
        // roundedClip{Left..RadiusY} with an ANALYTIC gaussian (erf) falloff of
        // the rect's own SDF instead of a hard rounded fill/clip — a single
        // over-blend replacing the N-layer concentric-rect halo (D3D12
        // DrawDropShadowEffect / sdf_rect shadowMode parity). The VS grows the
        // quad by 3*sigma+1px so the tail isn't clipped; the FS takes the erf
        // branch. Negative mode is a SuperEllipse fill: exact bounds ride in
        // innerRoundedClipRect and the parameter carries exponent n. Mode 0
        // (default) is a byte-identical no-op for every other SolidRect caller.
        float shadowMode = 0.0f;    // > 0.5 shadow, < -0.5 SuperEllipse
        float shadowSigma = 0.0f;   // shadow sigma or SuperEllipse exponent
        bool hasRoundedClip = false;
        // Inverse rounded clip (an ancestor PushRoundedRectClipExclude): the
        // shader keeps 1 - coverage, masking the rect's INTERIOR. Only the
        // pipelines with an inverse-capable clip section consume it
        // (solid_rect / bitmap_quad / text_glyph via clipFlags.x == 2);
        // replay branches for the other pipelines skip the rounded clip
        // entirely when this is set (drawing unmasked) instead of clipping
        // in the wrong direction.
        bool roundedClipInverse = false;
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
        // Per-corner radii in (TL, TR, BR, BL) order. When any of the X or Y
        // values are non-zero the SolidRect shader switches into
        // CoveragePerCornerRoundRect for the outer clip; uniform-radius
        // commands must leave these all zero (which is the default).
        float perCornerRadiusX[4] = { 0.0f, 0.0f, 0.0f, 0.0f };
        float perCornerRadiusY[4] = { 0.0f, 0.0f, 0.0f, 0.0f };
        // Per-corner radii for the INNER rounded clip (a per-corner border
        // stroke's inside edge), same (TL, TR, BR, BL) order. Non-zero entries
        // make the SolidRect shader use CoveragePerCornerRoundRect for the
        // inner subtraction so each corner's inner arc matches its outer arc;
        // uniform-radius / fill commands leave these zero (default) and keep
        // the uniform inner path.
        float innerPerCornerRadiusX[4] = { 0.0f, 0.0f, 0.0f, 0.0f };
        float innerPerCornerRadiusY[4] = { 0.0f, 0.0f, 0.0f, 0.0f };
        // Rotated / skewed rounded rect & fill. When set, the SolidRect replay
        // drives the shader's LOCAL-space rounded-coverage path (geometryFlags.y)
        // instead of the screen-space CoverageRoundRect clip. The transformed,
        // AA-expanded oriented quad rides in hasCustomQuad + quadPoint0..3; the
        // LOCAL (pre-transform, px) geometry below is forwarded into the FREE
        // inner rounded-clip push-constant slots so the push-constant block does
        // NOT grow. hasRoundedClip / hasInnerRoundedClip stay false on this path
        // (their screen-space clip writes would conflict). All radii are CIRCULAR
        // (one value per corner), in (TL, TR, BR, BL) order, matching the D3D12
        // sdRoundedBox SDF this path mirrors.
        bool hasLocalRoundedCoverage = false;
        float localOuterW = 0.0f;   // pre-transform OUTER rect width  (px)
        float localOuterH = 0.0f;   // pre-transform OUTER rect height (px)
        float localInnerW = 0.0f;   // pre-transform INNER rect width  (0 = fill, no inner subtraction)
        float localInnerH = 0.0f;   // pre-transform INNER rect height (0 = fill)
        float localExpandX = 0.0f;  // per-axis AA pad (px) applied to the quad in local-X
        float localExpandY = 0.0f;  // per-axis AA pad (px) applied to the quad in local-Y
        float localOuterPerCornerRadius[4] = { 0.0f, 0.0f, 0.0f, 0.0f }; // TL,TR,BR,BL OUTER
        float localInnerPerCornerRadius[4] = { 0.0f, 0.0f, 0.0f, 0.0f }; // TL,TR,BR,BL INNER
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
        GpuVcTrianglesCommand vcTriangles {};
        GpuInkLayerCommand inkLayer {};
        GpuExternalVideoCommand externalVideo {};
        GpuCustomShaderCommand customShader {};
        GpuTextRunCommand textRun {};
        GpuEngineBatchSpanCommand engineBatchSpan {};
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
    void BlendPixelSubpixel(int x, int y,
                           uint8_t textB, uint8_t textG, uint8_t textR,
                           uint8_t coverageB, uint8_t coverageG,
                           uint8_t coverageR, uint8_t coverageA);
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

    // Brush → EngineBrushData (solid + linear/radial gradient) so a gradient
    // stroke can be routed through the Impeller engine as a TRUE per-vertex
    // gradient. Stop storage is owned by the caller-supplied vector; bd.stops
    // aliases it, so it must outlive the EncodeXxx call. Opacity is folded into
    // the (straight-alpha) stops. Returns false for image/unsupported brushes.
    bool BuildEngineBrush(Brush* brush, float opacity, EngineBrushData& bd,
                          std::vector<EngineBrushData::GradientStop>& stopStore) const;
    std::vector<uint8_t> BlurPixels(const std::vector<uint8_t>& source, int sourceWidth, int sourceHeight, int radius, float x, float y, float w, float h) const;
    void BlendBuffer(const std::vector<uint8_t>& source, int sourceWidth, int sourceHeight, float x, float y, float w, float h, float opacity);
    void BlendLcdCoverageBuffer(const GpuBitmapCommand& bitmap);
    void PushTemporaryClip(float x, float y, float w, float h, float rx = 0.0f, float ry = 0.0f);
    void PopTemporaryClip();
    void ParseTintColor(const char* tint, float fallbackR, float fallbackG, float fallbackB, uint8_t& outB, uint8_t& outG, uint8_t& outR) const;
    void BlendOutsideRect(float x, float y, float w, float h, uint8_t b, uint8_t g, uint8_t r, uint8_t a);
    void StrokeRoundedRectApprox(float x, float y, float w, float h, float rx, float ry, float strokeWidth, uint8_t b, uint8_t g, uint8_t r, uint8_t a);
    void ResetGpuReplay();
    // Single funnel for appending a recorded GPU replay command. Every
    // primitive recorder pushes through here so the painter-order machinery
    // has one choke point: before appending, MaybeEmitEngineSpan() folds any
    // engine batches encoded since the last span into an EngineBatchSpan
    // command, keeping Impeller/Vello path work interleaved with the replay
    // stream in submission order (the Vulkan analogue of D3D12's
    // FlushVelloIfNeeded lazy flush). Two overloads preserve the exact
    // copy/move semantics of the raw push_back call sites they replaced.
    // Intentionally NOT funneled: the OffscreenBegin/OffscreenEnd marker
    // emits (paired markers whose position is managed by BeginEffectCapture /
    // EndEffectCapture themselves) and SpliceGlowBehindContent's content
    // re-insert (moves already-recorded commands).
    void RecordReplayCommand(const GpuReplayCommand& command);
    void RecordReplayCommand(GpuReplayCommand&& command);
    // If the active engine (Impeller, or Vello in CPU-tessellation mode) has
    // encoded batches beyond consumedEngineBatchCount_, emit an EngineBatchSpan
    // command covering [consumed, size) and advance the cursor. Called before
    // every RecordReplayCommand append, at EndDraw to fold the frame tail, and
    // around the effect-capture content stamps so the glow splice moves engine
    // work together with its element. Vello COMPUTE mode is exempt — its scene
    // is still consumed at frame end (sub-scene splitting is B2).
    void MaybeEmitEngineSpan();
    void InvalidateGpuReplay(const char* caller = nullptr);
    void ResetGpuSolidRectReplay() { ResetGpuReplay(); }
    void InvalidateGpuSolidRectReplay(const char* caller = nullptr) { InvalidateGpuReplay(caller); }
    void EnsureCpuRasterization();
    void ReplayCommandToCpu(const GpuReplayCommand& command);
    bool TryPopulateReplayClip(GpuReplayCommand& command) const;
    // Mirror the current clipStack_ AABB scissor into the active vector engine
    // (Impeller / Vello) so each engine batch snapshots the clip it was recorded
    // under — the Vulkan analogue of D3D12RenderTarget::SyncScissorToImpeller.
    // The consumer (Impl::RenderEngineBatches) already turns batch.hasScissor
    // into cmdSetScissor; this is the missing producer that feeds it. Reuses
    // TryPopulateReplayClip so the engine scissor is byte-identical to the
    // SolidRect GPU-replay path's scissor. MUST be called immediately before
    // every engine Encode* call: PushBatch snapshots hasScissor_ synchronously
    // inside Encode*, so a stale scissor from a previous primitive would
    // otherwise leak into the new batch.
    void SyncClipToEngine(IRenderingEngine* engine) const;
    bool TryRecordGpuClearRectCommand(float x, float y, float w, float h);
    bool TryRecordGpuPixelBufferCommand(const std::vector<uint8_t>& pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float opacity);
    // shared_ptr overload — used by VulkanBitmap-backed DrawBitmap so the
    // per-frame GpuReplayCommand reference-counts the source pixels rather
    // than vector-copying ~5 MB/RGBA bitmap on every call. The pointed-to
    // buffer must remain immutable for the lifetime of any in-flight
    // command that holds the shared_ptr (UpdatePackedPixels achieves this
    // via copy-on-write — it allocates a new buffer instead of mutating).
    // opacityAlreadyBaked: when true the caller has ALREADY multiplied the
    // opacity stack into the source pixels' alpha (e.g. the FreeType glyph
    // bitmap in RenderText, mirroring RasterizePolygonToGpuBitmap). The
    // recorder then stores the per-bitmap opacity verbatim instead of
    // multiplying by GetCurrentOpacity() a second time — otherwise text and
    // other pre-baked bitmaps fade as opacity² during animations.
    // scalingMode: JaliumBitmapScalingMode value (3 = NearestNeighbor selects
    // the point sampler at replay; every other mode keeps the shared
    // linear/anisotropic frameSampler — matches the D3D12 sampler mapping).
    bool TryRecordGpuPixelBufferCommandShared(std::shared_ptr<const std::vector<uint8_t>> pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float opacity, int scalingMode = 0, bool opacityAlreadyBaked = false, bool lcdCoverage = false, float lcdTextR = 0.0f, float lcdTextG = 0.0f, float lcdTextB = 0.0f, float lcdTextA = 1.0f);
    bool TryRecordGpuBlurCommand(const std::vector<uint8_t>& pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float radius, float opacity, bool alphaOnlyTint = false, float tintR = 0.0f, float tintG = 0.0f, float tintB = 0.0f, float tintA = 1.0f);
    // GPU compositor path for element BlurEffect: records a Blur command that
    // sources its pixels from the LIVE composited frame (the element's own screen
    // region, blitted 1:1 into the shared upload image at replay time) instead of
    // an uploaded CPU buffer. No CPU pixels and no per-element offscreen target.
    // Used on the GPU replay path, where BeginEffectCapture is a pass-through so
    // the element already rendered into the frame.
    bool TryRecordGpuLiveBlurCommand(float x, float y, float w, float h, float radius, float opacity, bool alphaOnlyTint = false, float tintR = 0.0f, float tintG = 0.0f, float tintB = 0.0f, float tintA = 1.0f, bool sourceOffscreen = false);
    bool TryRecordGpuBackdropCommand(const std::vector<uint8_t>& pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float blurRadius, float cornerRadiusTL, float cornerRadiusTR, float cornerRadiusBR, float cornerRadiusBL, float tintR, float tintG, float tintB, float tintOpacity, float saturation = 1.0f, float noiseIntensity = 0.0f, float luminosity = 1.0f, bool remapSourceUv = false);
    bool TryRecordGpuGlowCommand(float x, float y, float w, float h, float cornerRadius, float strokeWidth, float glowR, float glowG, float glowB, float glowA, float dimOpacity, float intensity);
    bool TryRecordGpuLiquidGlassCommand(const std::vector<uint8_t>& pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float cornerRadius, float blurRadius, float refractionAmount, float chromaticAberration, float tintR, float tintG, float tintB, float tintOpacity, float lightX, float lightY, float highlightBoost, int shapeType, float shapeExponent, int neighborCount, float fusionRadius, const float* neighborData);
    bool TryRecordGpuDimOutsideRectCommand(float x, float y, float w, float h, uint8_t b, uint8_t g, uint8_t r, uint8_t a);
    bool TryRecordGpuFilledPolygonCommand(const std::vector<float>& points, int32_t fillRule, Brush* brush);
    // Pre-triangulated variant of TryRecordGpuFilledPolygonCommand. Skips
    // the O(N³) ear-clip and uses the supplied (already triangulated, world-
    // space) vertex list directly. Used by rasterize-fallback paths that
    // build their vertices in world space.
    bool TryRecordPreTriangulatedFilledPolygon(std::vector<float>&& worldTriangles, Brush* brush);
    // Hot path: record a filled polygon whose vertices live in *local*
    // (pre-transform) space and are shared with the path geometry cache.
    // The supplied transform travels in the GPU command and is applied by
    // the vertex shader at draw time. This avoids both the per-vertex CPU
    // transform and the per-call heap allocation that the world-space
    // variant above pays.
    bool TryRecordSharedLocalFilledPolygon(std::shared_ptr<const std::vector<float>> sharedLocalTriangles,
                                           const CpuTransform& transform,
                                           Brush* brush);
    // Walk the FillPath/StrokePath command stream and emit (x, y) sample
    // points in *local* (pre-transform) space — bezier curves get sampled
    // into adaptive line segments (more samples for larger on-screen scale),
    // MoveTo/LineTo/Close are copied verbatim. The result is exactly what
    // the cached entry stores so subsequent draws of the same path skip
    // this work entirely.
    //
    // scaleFactor: the current transform's max scale (linear part). Used to
    // pick a per-curve segment count: at 1.0× we use the historic 16/8
    // cubic/quadratic samples; at 4.0× we proportionally raise it to keep
    // on-screen vertex density flat. Pass 1.0f when the caller has no
    // transform context.
    static void DecomposePathToLocalPoints(float startX, float startY,
                                           const float* commands,
                                           uint32_t commandLength,
                                           std::vector<float>& outLocalPoints,
                                           float scaleFactor = 1.0f);
    bool TryRecordGpuTransitionCommand(const std::vector<uint8_t>& fromPixels, const std::vector<uint8_t>& toPixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float progress, int mode);
    // env JALIUM_VK_EFFECT_GPU_RT (C6): record a Transition command that samples the two
    // persistent slot images (transitionImages[0]/[1]) captured via the GPU offscreen RT,
    // instead of uploading CPU pixel tiles. Carries no CPU pixels.
    bool TryRecordGpuTransitionSlotsCommand(float x, float y, float w, float h, float progress, int mode);
    // env JALIUM_VK_EFFECT_GPU_RT (C6): single-slot GPU-RT composite for
    // DrawCapturedTransition (the particle path). Samples one captured slot image.
    bool TryRecordGpuTransitionSingleSlotCommand(int slot, float x, float y, float w, float h, float opacity);
    bool TryRecordGpuSolidRectCommand(float x, float y, float w, float h, Brush* brush);
    bool TryRecordGpuRoundedRectFillCommand(float x, float y, float w, float h, float rx, float ry, Brush* brush);
    // Analytic erf gaussian drop-shadow rounded rect (single over-blend; D3D12
    // DrawDropShadowEffect parity). sigma = blurRadius/3; colour STRAIGHT.
    bool TryRecordGpuShadowRectCommand(float x, float y, float w, float h,
        float radius, float r, float g, float b, float a, float sigma);
    bool TryRecordGpuRoundedRectStrokeCommand(float x, float y, float w, float h, float rx, float ry, float strokeWidth, Brush* brush);
    // Rotated / skewed rounded-rect STROKE (strokeWidth > 0) or FILL
    // (strokeWidth <= 0), recorded as an AA-expanded oriented quad whose
    // fragment shader evaluates a per-corner rounded-box SDF in LOCAL space
    // (geometryFlags.y). This is the m12/m21 != 0 branch that the axis-only
    // CoverageRoundRect path cannot serve. Radii are CIRCULAR per corner
    // (TL, TR, BR, BL), in PRE-transform px; the centred model (outer =
    // corner+halfStroke, inner = corner-halfStroke) makes a rotated border land
    // at the same visible place as the unrotated one. Returns true on success
    // (command pushed or a benign no-op) and false to fall back to CPU.
    bool TryRecordGpuRotatedLocalRoundedRectCommand(const CpuTransform& transform,
        float x, float y, float w, float h,
        float tl, float tr, float br, float bl,
        float strokeWidth, Brush* brush);
    // Per-corner variants — same wire format as the uniform-radius helpers
    // but also write the 4 (rx, ry) pairs into the GpuReplayCommand so the
    // SolidRect shader picks each corner's radius separately at fragment
    // time (CoveragePerCornerRoundRect path). Used by Fill/Draw
    // PerCornerRoundedRectangle so Tab / Card-style "rounded top, square
    // bottom" shapes render correctly instead of degrading to a single
    // average radius.
    bool TryRecordGpuPerCornerRoundedRectFillCommand(float x, float y, float w, float h,
        float tl, float tr, float br, float bl, Brush* brush);
    bool TryRecordGpuPerCornerRoundedRectStrokeCommand(float x, float y, float w, float h,
        float tl, float tr, float br, float bl, float strokeWidth, Brush* brush);
    bool TryRecordGpuEllipseFillCommand(float cx, float cy, float rx, float ry, Brush* brush);
    bool TryRecordGpuEllipseStrokeCommand(float cx, float cy, float rx, float ry, float strokeWidth, Brush* brush);
    bool TryRecordGpuLineCommand(float x1, float y1, float x2, float y2, float strokeWidth, Brush* brush);
    // 3-strip manual AA variant — mirrors D3D12RenderTarget::DrawLine (see
    // [[project_d3d12_line_manual_aa]]). Builds 18 vertices (core + 2 feather
    // strips) with per-vertex alpha so the pixel shader interpolates a
    // 1-px AA ramp across the stroke edges. Emits one
    // GpuReplayCommandKind::VcTriangles command.
    bool TryRecordGpuLineAACommand(float x1, float y1, float x2, float y2, float strokeWidth, Brush* brush);
    bool TryRecordGpuPolylineCommand(const std::vector<float>& points, bool closed, float strokeWidth, Brush* brush);
    bool TryRecordGpuRectangleStrokeCommand(float x, float y, float w, float h, float strokeWidth, Brush* brush);
    bool TryRecordGpuBitmapCommand(Bitmap* bitmap, float x, float y, float w, float h,
                                   float opacity, int scalingMode,
                                   bool opacityAlreadyBaked = false,
                                   bool lcdCoverage = false,
                                   float lcdTextR = 0.0f,
                                   float lcdTextG = 0.0f,
                                   float lcdTextB = 0.0f,
                                   float lcdTextA = 1.0f);
    // Shared implementation behind both public DrawBitmap overrides. When
    // opacityAlreadyBaked is true the bitmap's alpha already carries the
    // opacity stack, so the GPU recorder must NOT re-apply GetCurrentOpacity()
    // (the CPU blit path likewise stays correct because the baked alpha is
    // intrinsic to the pixels). RenderText's FreeType path is the one caller
    // that passes true; image draws pass false and keep opacity*GetCurrentOpacity().
    void DrawBitmapInternal(Bitmap* bitmap, float x, float y, float w, float h,
                            float opacity, int scalingMode,
                            bool opacityAlreadyBaked,
                            bool lcdCoverage = false,
                            float lcdTextR = 0.0f,
                            float lcdTextG = 0.0f,
                            float lcdTextB = 0.0f,
                            float lcdTextA = 1.0f);
    // Records an InkLayer replay command sampling a resident VulkanInkLayerBitmap
    // image. Returns false (caller falls back to CPU readback-and-blend) when GPU
    // replay isn't active or the layer/extent is degenerate.
    bool TryRecordGpuInkLayerCommand(void* inkLayer, float x, float y, float opacity);
    bool TryRecordGpuExternalVideoCommand(
        VulkanImportedVideoSurface* surface,
        float x, float y, float w, float h, float opacity);
    // Readback cache for the foreign-but-healthy ink blit fallback: a layer
    // living on another window's VkDevice can't be GPU-sampled here, so its
    // pixels travel through host memory. Keyed by the layer's ContentVersion
    // so an unchanged layer costs zero readbacks per frame — the synchronous
    // GPU round trip happens once per stroke/clear/resize, not once per
    // frame. Entries are evicted wholesale past a small cap (a window blits
    // at most committed + preview = 2 foreign layers in practice).
    struct ForeignInkSnapshot {
        uint64_t version = 0;
        uint32_t width = 0;
        uint32_t height = 0;
        std::shared_ptr<const std::vector<uint8_t>> bgra;
    };
    std::unordered_map<const void*, ForeignInkSnapshot> foreignInkSnapshots_;
    // Records a CustomShader command (cropped captured content + source hash +
    // constants). Returns false when GPU replay isn't active or the captured
    // content is empty — caller then composites the captured content unmodified.
    bool TryRecordGpuCustomShaderCommand(float x, float y, float w, float h,
        uint64_t shaderHash, const float* constants, uint32_t constantFloatCount,
        bool sampleOffscreen = false, bool patchUvInfo = false);
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
    // env JALIUM_VK_EFFECT_GPU_RT (C6): GPU offscreen transition capture state. This path
    // does NOT touch activeTransitionSlot_ (kept -1) so the ~20 TryRecordGpu* guards and the
    // FillPolygon/DrawLine engine-bypass keep recording the captured subtree into
    // gpuReplayCommands_ / the engine (redirected into the offscreen RT at replay), instead
    // of forcing the whole capture onto the CPU raster path (which would lose FillPath engine
    // primitives). transitionCaptureGpuRtSlot_ >= 0 only between a GPU-RT BeginTransitionCapture
    // and its EndTransitionCapture. The per-slot physical-pixel element rect is stamped at Begin
    // and consumed by the End marker's blit + the DrawTransitionShader uvScale.
    int transitionCaptureGpuRtSlot_ = -1;
    float transitionCapturePhysRect_[2][4] = { { 0.0f, 0.0f, 0.0f, 0.0f }, { 0.0f, 0.0f, 0.0f, 0.0f } };
    bool transitionSlotGpuValid_[2] = { false, false };
    std::vector<CpuTransform> transformStack_;
    std::vector<float> opacityStack_;
    std::vector<ClipState> clipStack_;
    std::vector<EffectCaptureState> effectCaptureStack_;
    // Retained layers (env-gated JALIUM_VK_RETAINED_LAYERS, DEFAULT ON). C7 gives this
    // the real GPU offscreen-texture model at parity with D3D12: on the GPU replay path
    // RealizeLayerBegin/End emit RetainedLayerCaptureBegin/End markers that redirect the
    // subtree into the shared effect-offscreen RT (reusing the C6 machinery) and blit the
    // isolated element into a PERSISTENT per-layer image (owned by the Impl in
    // retainedLayerGpuImages_, keyed by the VulkanRetainedLayer* handle, element-sized);
    // CompositeLayer then samples that image as a transformed/opacity bitmap quad — no CPU
    // pixels, no full re-emission. The VulkanRetainedLayer object itself is the stable
    // cross-class key returned to managed as the opaque handle. Its `pixels` vector is kept
    // ONLY for the CPU-fallback path (a frame that could not stay on the GPU replay path,
    // e.g. an effect forced cpuRasterNeeded_); on the GPU path it stays empty and the GPU
    // image is the source. retainedCaptureStack_ saves frame state for the CPU capture
    // sub-frame (a stack so nested CPU realizes are safe).
    struct VulkanRetainedLayer {
        std::vector<uint8_t> pixels;  // BGRA8, w x h snapshot — CPU-fallback path ONLY (empty on GPU path)
        int32_t w = 0;
        int32_t h = 0;
        // Element PHYSICAL-pixel size recorded at the last GPU capture (RealizeLayerEnd's GPU
        // branch). CompositeLayer forwards it as the Bitmap command's pixelWidth/Height so the
        // replay's uvScale = elemPx / perLayerImageSize is exact. Zero until first GPU capture.
        int32_t physW = 0;
        int32_t physH = 0;
    };
    struct RetainedCaptureState {
        std::vector<uint8_t> savedPixels;
        std::vector<GpuReplayCommand> savedReplayCommands;
        // Ancestor clips belong to the final composite, not to the layer's
        // cached pixels. Keep them out of the capture exactly as D3D12 does;
        // clips pushed by the captured subtree itself remain active.
        std::vector<ClipState> savedClips;
        bool savedReplaySupported = false;
        bool savedReplayHasClear = false;
        // True when this capture opened the GPU offscreen region (RealizeLayerBegin's
        // GPU branch): RealizeLayerEnd then emits the RetainedLayerCaptureEnd marker and
        // does NOT restore CPU pixel state. False = the CPU snapshot fallback.
        bool gpuPath = false;
        VulkanRetainedLayer* layer = nullptr;
        int32_t physX = 0, physY = 0, physW = 0, physH = 0;
    };
    std::vector<std::unique_ptr<VulkanRetainedLayer>> retainedLayers_;
    // C7: the active retained capture rides on retainedCaptureStack_ — each RealizeLayerBegin
    // pushes a RetainedCaptureState (gpuPath=true on the GPU branch) carrying the layer + its
    // physical rect, which RealizeLayerEnd pops and turns into the End marker. The managed side
    // (RenderTargetDrawingContext.BeginLayerCapture) never nests a capture, and the GPU branch
    // guards on retainedCaptureStack_.empty(), so the stack holds at most one entry in practice
    // (it is a stack purely for symmetry with the transition/effect capture bookkeeping and to
    // stay robust if a caller ever nested). The GPU branch does NOT touch activeTransitionSlot_
    // / effectCaptureStack_ / cpuRasterNeeded_, so the subtree draws stay on the GPU replay path
    // (the ~20 TryRecordGpu* guards + FillPolygon/DrawLine engine bypass keep recording into the
    // shared offscreen RT).
    std::vector<RetainedCaptureState> retainedCaptureStack_;
    bool gpuReplaySupported_ = false;
    bool gpuReplayHasClear_ = false;
    std::vector<GpuReplayCommand> gpuReplayCommands_;
    // One intrusive retain per unique imported surface recorded this frame.
    // DrawReplayFrame transfers these references to its fence slot; abandoned
    // or CPU-fallback frames release them immediately.
    std::vector<VulkanImportedVideoSurface*> recordedExternalVideoSurfaces_;
    void RetainRecordedExternalVideoSurface(
        VulkanImportedVideoSurface* surface);
    void ReleaseRecordedExternalVideoSurfaces();
    // Number of engine batches already covered by an emitted EngineBatchSpan.
    // Monotonic within a frame (the engines' batch vectors only grow until
    // EndDraw's ClearBatches); reset to 0 in BeginDraw. Batches whose span was
    // dropped by a mid-frame command reset (Clear() / capture sub-frames)
    // simply never render — the D3D12 analogue drops eagerly-flushed work on
    // reset the same way.
    size_t consumedEngineBatchCount_ = 0;
    mutable bool cpuRasterNeeded_ = false;
    bool cpuRasterNeededLastFrame_ = false;

    // SuperEllipse shape state (SetShapeType). 0 = RoundedRect (default),
    // 1 = SuperEllipse. The exponent (Border.SuperEllipseN, default 4 = iOS
    // squircle) drives the Lamé-curve boundary. Consumed by FillRoundedRectangle
    // / DrawRoundedRectangle; the managed Border resets it to 0 after drawing and
    // BeginDraw clears it per frame as a safety net.
    int currentShapeType_ = 0;
    float currentShapeExponent_ = 4.0f;

    // Tessellates the SuperEllipse boundary for (x,y,w,h) at currentShapeExponent_
    // into a closed polygon (x,y interleaved, first/last vertex NOT repeated):
    // the full-rect Lamé curve |X/(w/2)|^n + |Y/(h/2)|^n = 1, matching D3D12's
    // sdSuperEllipseRect and the managed Border layout clip. The per-corner
    // radius parameters are ignored on purpose (the shared shape contract
    // derives curvature from the exponent alone — CornerRadius defaults to 0
    // for Shape=SuperEllipse, and an earlier per-corner-arc variant therefore
    // rendered square-cornered rectangles that only Vulkan displayed).
    void BuildSuperEllipsePolygon(float x, float y, float w, float h,
                                  float tl, float tr, float br, float bl,
                                  std::vector<float>& outPts) const;

    // Records a SuperEllipse fill as an IN-ORDER FilledPolygon replay command
    // (transformed to world space) instead of routing through FillPolygon, whose
    // preferred Impeller/Vello engine batch is drained AFTER every in-order
    // replay command — that late draw paints a SuperEllipse card's background
    // over its own text/bitmap content, leaving cards blank. Returns false when
    // an in-order record isn't possible (caller falls back to FillPolygon).
    bool TryFillSuperEllipseInOrder(float x, float y, float w, float h,
                                    float tl, float tr, float br, float bl, Brush* brush);

    // Records a gradient fill over an arbitrary convex local-space perimeter as an
    // IN-ORDER vertex-colour triangle fan (VcTriangles): each vertex bakes the
    // sampled gradient colour, so the gradient renders truly AND in draw order
    // (FillPolygon's gradient goes through the late-draining Impeller batch, which
    // would paint a card background over its own content). Returns false when the
    // brush isn't a gradient, or the fan can't be recorded (caller falls back to a
    // solid fill). cxLocal/cyLocal = fan centre in the same local space as perimeter.
    bool TryRecordGradientFanInOrder(const std::vector<float>& perimeterLocal,
                                     float cxLocal, float cyLocal, Brush* brush);

    // Tessellates a (per-corner) rounded rectangle outline into a closed local-space
    // polygon (x,y interleaved) for gradient-fan fills.
    void BuildRoundedRectPolygon(float x, float y, float w, float h,
                                 float tl, float tr, float br, float bl,
                                 std::vector<float>& outPts) const;

    // ── GPU-path effect glow/shadow (no offscreen capture) ───────────────────
    // On the GPU replay path the effect capture can't snapshot the element, so
    // OuterGlow / DropShadow approximate the halo with concentric rounded-rects.
    // The managed call order is BeginEffectCapture → element content →
    // EndEffectCapture → effect, so the content is already recorded by the time
    // the effect runs. BeginEffectCapture (GPU path) records where the content
    // began; EndEffectCapture stamps [pendingGlowStart_, pendingGlowEnd_); the
    // effect splices the glow BEHIND that content range (extract → glow → re-add).
    std::vector<int> effectContentStartStack_;
    int pendingGlowStart_ = -1;
    int pendingGlowEnd_ = -1;
    // Depth of open GPU-RT effect capture regions. While > 0, replay-clip
    // population skips ALIASED clip entries (the managed dirty-region scissor)
    // so the capture always holds the COMPLETE element silhouette — a partial
    // frame's damage rect must only bound the effect's OUTPUT, not its input
    // (D3D12 parity: BeginOffscreenCapture saves & clears the scissor stack).
    int effectCaptureClipSuspendDepth_ = 0;
    // env JALIUM_VK_EFFECT_GPU_RT: index of the OffscreenEnd marker recorded by
    // the outermost EndEffectCapture. The effect Draw* that follows (the managed
    // call order guarantees nothing records in between) uses it to retroactively
    // suppress the marker's composite-back when the processed result REPLACES
    // the element. -1 = none pending. Reset by ResetGpuReplay, consumed by the
    // suppress helper, and invalidated by the glow splice (indices shift).
    int lastOffscreenEndIndex_ = -1;
    // True while [lastOffscreenEndIndex_] is a valid OffscreenEnd marker still
    // sitting at the stream tail (nothing recorded since EndEffectCapture).
    // Non-mutating: the effect Draw* checks this BEFORE recording its own
    // sampling command.
    bool HasPendingOffscreenComposite() const;
    // Flag the pending OffscreenEnd as suppress-composite and consume the
    // pending index. requireTail=false is for callers that already recorded
    // their effect command (the marker is no longer the tail but is still the
    // correct target). Returns false (and consumes) when the marker is stale.
    bool TrySuppressPendingOffscreenComposite(bool requireTail = true);

    // Records the soft halo as a stack of concentric rounded-rects (outer faint →
    // inner stronger) expanding from (x,y,w,h) by `spread`, optionally offset
    // (drop shadow). Appends SolidRect replay commands at the current tail.
    void DrawGlowLayers(float x, float y, float w, float h, float spread,
                        float r, float g, float b, float a, float offX, float offY,
                        float cTL, float cTR, float cBR, float cBL);
    // Splices a glow drawn by DrawGlowLayers behind the just-recorded content
    // range. Returns true if it consumed the pending capture (effect should
    // return); false if there was no GPU-path capture (fall back to CPU path).
    bool SpliceGlowBehindContent(float x, float y, float w, float h, float spread,
                                 float r, float g, float b, float a, float offX, float offY,
                                 float cTL, float cTR, float cBR, float cBL);
    // Analytic erf gaussian drop shadow spliced behind content (D3D12
    // DrawDropShadowEffect parity; replaces the DrawGlowLayers 8-layer halo).
    bool SpliceErfShadowBehindContent(float x, float y, float w, float h,
        float blurRadius, float offsetX, float offsetY,
        float r, float g, float b, float a,
        float cTL, float cTR, float cBR, float cBL);

    // Path geometry cache. FillPath / StrokePath both decompose Bezier curves
    // into a dense local-space point list and (for FillPath) ear-clip into
    // triangles — the latter is O(N³) on the vertex count. Caching the local-
    // space output lets every subsequent draw of the same icon skip both the
    // bezier decompose and the triangulation; the only per-call work left is
    // applying the current frame's transform to the cached vertices, which is
    // O(N) and trivially cheap. See path_cache.h.
    std::unique_ptr<PathGeometryCache> pathCache_;
    static constexpr size_t            kMaxPathCacheEntries = 512;

    // E4 — Path MSAA knob (SetPathMsaaSampleCount). Now carries D3D12's real
    // semantics: 0 = analytic-only (route solid fills to the scanline analytic-AA
    // rasterizer, see pathAnalyticOnly_); 1/2/4/8 = the desired stencil-then-cover
    // MSAA sample count, intersected with device caps in EnsureStencilCover
    // Resources (a changed value rebuilds the stencil FB/PSOs at the frame
    // boundary via the size+sample key). Default 8 mirrors D3D12
    // (d3d12_direct_renderer.h pathMsaaSampleCount_ = 8) and is re-seeded from the
    // JALIUM_PATH_MSAA env at Initialize (also matching D3D12).
    uint32_t pathMsaaSampleCount_ = 8;
    // Analytic-only latch (SetPathMsaaSampleCount(0)); mirrors D3D12
    // pathAnalyticOnly_. Propagated to the engines' SetPathAnalyticOnly so their
    // fill/stroke encoders skip GPU stencil-then-cover for the scanline path.
    bool pathAnalyticOnly_ = false;
    // Curve-flattening scale is deliberately INDEPENDENT of the MSAA knob — D3D12
    // uses a fixed flattenTolerance (0.25) regardless of SampleDesc.Count, and
    // Vulkan at that fixed tessellation already matches D3D12 pixel-for-pixel
    // (path-fill parity 0.000%). The MSAA knob controls ONLY the stencil sample
    // count / analytic-only, never tessellation density. Kept as a hook (always
    // 1.0) so the two CPU-bitmap fallback helpers that reference it stay valid.
    float PathTessellationQualityScale() const { return 1.0f; }

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
