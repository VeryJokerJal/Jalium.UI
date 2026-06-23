#pragma once

#include "jalium_backend.h"
#include "jalium_types.h"
#include "jalium_rendering_engine.h"
#include "jalium_path_cache.h"
#include "text_cache.h"
#include "vulkan_impeller_engine.h"
#include "vulkan_vello_engine.h"

#include <memory>
#include <string>
#include <unordered_map>
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
    void DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity) override;
    void DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity, int scalingMode) override;
    void DrawVideoSurface(VideoSurface* surface, float x, float y, float w, float h,
                          float opacity, int scalingMode) override;
    // Composites a VulkanInkLayerBitmap onto the frame. The handle is the
    // backend-native pointer (the C ABI already unwrapped its wrapper). The ink
    // image is resident on the same device, so this samples it directly (no
    // per-frame upload) via the bitmap pipeline — matching D3D12 BlitInkLayer.
    void BlitInkLayer(void* inkLayerBitmap, float dstX, float dstY, float opacity) override;
    // Vulkan has no retained-layer implementation yet (SupportsRetainedLayers()
    // stays false), so a non-null handle reaching this target is necessarily
    // FOREIGN — created by another backend's render target (e.g. a D3D12 layer
    // drained through the replacement target after a device-lost backend
    // fallback). Deleting through an unknown concrete type would be undefined
    // behavior, so this override keeps the base class's no-op semantics but
    // makes the bounded leak explicit and observable in debug output instead
    // of silently inheriting it. When Vulkan grows retained layers, give them
    // a VulkanDeviceGeneration keep-alive (vulkan_ink_layer.h) and mirror the
    // D3D12 destroy rule: orphan only when the creating generation is actually
    // lost — pointer inequality alone means "different", not "removed".
    void DestroyRetainedLayer(void* layer) override;
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
        CustomShader,   // runtime-compiled custom pixel-shader effect
        TextRun,        // shaped text run via the dedicated text-glyph pipeline (Windows DirectWrite)
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
        // Per-corner radii in (TL, TR, BR, BL) order. When any of the X or Y
        // values are non-zero the SolidRect shader switches into
        // CoveragePerCornerRoundRect for the outer clip; uniform-radius
        // commands must leave these all zero (which is the default).
        float perCornerRadiusX[4] = { 0.0f, 0.0f, 0.0f, 0.0f };
        float perCornerRadiusY[4] = { 0.0f, 0.0f, 0.0f, 0.0f };
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
        GpuCustomShaderCommand customShader {};
        GpuTextRunCommand textRun {};
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

    // Brush → EngineBrushData (solid + linear/radial gradient) so a gradient
    // stroke can be routed through the Impeller engine as a TRUE per-vertex
    // gradient. Stop storage is owned by the caller-supplied vector; bd.stops
    // aliases it, so it must outlive the EncodeXxx call. Opacity is folded into
    // the (straight-alpha) stops. Returns false for image/unsupported brushes.
    bool BuildEngineBrush(Brush* brush, float opacity, EngineBrushData& bd,
                          std::vector<EngineBrushData::GradientStop>& stopStore) const;
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
    bool TryRecordGpuPixelBufferCommandShared(std::shared_ptr<const std::vector<uint8_t>> pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float opacity, bool opacityAlreadyBaked = false);
    bool TryRecordGpuBlurCommand(const std::vector<uint8_t>& pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float radius, float opacity, bool alphaOnlyTint = false, float tintR = 0.0f, float tintG = 0.0f, float tintB = 0.0f, float tintA = 1.0f);
    bool TryRecordGpuBackdropCommand(const std::vector<uint8_t>& pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float blurRadius, float cornerRadiusTL, float cornerRadiusTR, float cornerRadiusBR, float cornerRadiusBL, float tintR, float tintG, float tintB, float tintOpacity, float saturation = 1.0f, float noiseIntensity = 0.0f);
    bool TryRecordGpuGlowCommand(float x, float y, float w, float h, float cornerRadius, float strokeWidth, float glowR, float glowG, float glowB, float glowA, float dimOpacity, float intensity);
    bool TryRecordGpuLiquidGlassCommand(const std::vector<uint8_t>& pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float cornerRadius, float blurRadius, float refractionAmount, float chromaticAberration, float tintR, float tintG, float tintB, float tintOpacity, float lightX, float lightY, float highlightBoost);
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
    bool TryRecordGpuSolidRectCommand(float x, float y, float w, float h, Brush* brush);
    bool TryRecordGpuRoundedRectFillCommand(float x, float y, float w, float h, float rx, float ry, Brush* brush);
    bool TryRecordGpuRoundedRectStrokeCommand(float x, float y, float w, float h, float rx, float ry, float strokeWidth, Brush* brush);
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
    bool TryRecordGpuBitmapCommand(Bitmap* bitmap, float x, float y, float w, float h, float opacity, bool opacityAlreadyBaked = false);
    // Shared implementation behind both public DrawBitmap overrides. When
    // opacityAlreadyBaked is true the bitmap's alpha already carries the
    // opacity stack, so the GPU recorder must NOT re-apply GetCurrentOpacity()
    // (the CPU blit path likewise stays correct because the baked alpha is
    // intrinsic to the pixels). RenderText's FreeType path is the one caller
    // that passes true; image draws pass false and keep opacity*GetCurrentOpacity().
    void DrawBitmapInternal(Bitmap* bitmap, float x, float y, float w, float h, float opacity, int scalingMode, bool opacityAlreadyBaked);
    // Records an InkLayer replay command sampling a resident VulkanInkLayerBitmap
    // image. Returns false (caller falls back to CPU readback-and-blend) when GPU
    // replay isn't active or the layer/extent is degenerate.
    bool TryRecordGpuInkLayerCommand(void* inkLayer, float x, float y, float opacity);
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
        uint64_t shaderHash, const float* constants, uint32_t constantFloatCount);
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

    // SuperEllipse shape state (SetShapeType). 0 = RoundedRect (default),
    // 1 = SuperEllipse. The exponent (Border.SuperEllipseN, default 4 = iOS
    // squircle) drives the Lamé-curve boundary. Consumed by FillRoundedRectangle
    // / DrawRoundedRectangle; the managed Border resets it to 0 after drawing and
    // BeginDraw clears it per frame as a safety net.
    int currentShapeType_ = 0;
    float currentShapeExponent_ = 4.0f;

    // Tessellates the SuperEllipse boundary for (x,y,w,h) at currentShapeExponent_
    // into a closed CCW polygon (x,y interleaved) approximating D3D12's
    // sdSuperEllipseRect, so it can be filled / stroked through the Impeller path.
    // Tessellates an iOS-style squircle: STRAIGHT edges joined by continuous-
    // curvature (Lamé, exponent currentShapeExponent_) CORNERS of the given
    // per-corner radii — NOT a full-rect superellipse (which collapses a wide /
    // short element into a flattened oval). Radii clamp to min(w,h)/2.
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

    // Rasterized-text cache. Windows RenderText used to call CreateDIBSection +
    // CreateFontW + DrawTextW on every frame, which dominated the Vulkan
    // backend's frame time (~150ms/frame in Gallery) because every static label
    // re-ran GDI. This cache stores the rasterized BGRA pixel payload keyed
    // by (text, font family id, size, bitmap extents, premultiplied BGRA
    // color, draw flags, weight, style) so the GDI dance only runs the first
    // time a given string is drawn at a given size/color.
    //
    // Implementation: TextLruCache (text_cache.h) — std::unordered_map with
    // C++20 transparent lookup, a doubly linked list for O(1) LRU touch,
    // and per-insert eviction (no more "clear-the-world when full"). The
    // hot path uses a wstring_view-based key view so a cache hit allocates
    // nothing.
    std::unique_ptr<TextLruCache> textCache_;
    FontFamilyInterner            familyInterner_;
    static constexpr size_t       kMaxTextCacheEntries = 512;

    // Path geometry cache. FillPath / StrokePath both decompose Bezier curves
    // into a dense local-space point list and (for FillPath) ear-clip into
    // triangles — the latter is O(N³) on the vertex count. Caching the local-
    // space output lets every subsequent draw of the same icon skip both the
    // bezier decompose and the triangulation; the only per-call work left is
    // applying the current frame's transform to the cached vertices, which is
    // O(N) and trivially cheap. See path_cache.h.
    std::unique_ptr<PathGeometryCache> pathCache_;
    static constexpr size_t            kMaxPathCacheEntries = 512;

    // Path-quality knob set by SetPathMsaaSampleCount (1/2/4/8). Folded into the
    // tessellation scale so higher values yield denser curve subdivision (the
    // Vulkan analogue of D3D12's path MSAA sample count). See
    // PathTessellationQualityScale().
    uint32_t pathMsaaSampleCount_ = 1;
    float PathTessellationQualityScale() const {
        switch (pathMsaaSampleCount_) {
            case 2:  return 1.25f;
            case 4:  return 1.5f;
            case 8:  return 1.75f;
            default: return 1.0f;
        }
    }

    // Fast-path used by RenderText to emit a cached text bitmap straight into
    // the GPU replay command list, skipping both the VulkanBitmap wrapper
    // construction (which deep-copies the pixel vector) and the
    // TryRecordGpuPixelBufferCommand deep-copy. Owns a shared reference to
    // the text cache entry's pixel buffer so subsequent DrawReplayFrame reads
    // see the same bytes.
    // destScale maps the high-resolution source bitmap (width x height pixels)
    // down to its base-DIP footprint: the local quad is sized (width*destScale,
    // height*destScale) while the texture keeps its full pixel resolution. When
    // text is rasterized at a magnified em (so a scaled draw stays crisp), pass
    // destScale = 1/rasterScale so the current transform re-magnifies the
    // base-DIP quad to the correct on-screen size. Default 1.0 = 1:1 (no scale).
    void RecordCachedTextBitmap(std::shared_ptr<const std::vector<uint8_t>> pixels,
                                int width, int height, float x, float y,
                                float destScale = 1.0f);

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
