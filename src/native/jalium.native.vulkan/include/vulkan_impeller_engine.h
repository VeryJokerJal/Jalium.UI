#pragma once

#include "jalium_rendering_engine.h"
#include "jalium_impeller_shapes.h"   // Trig / TrigCache / shape generators
#include "jalium_impeller_stroke.h"   // ImpellerCap / ImpellerJoin / ExpandStrokePath
#include "jalium_gradient_sample.h"   // SampleBrushGradient / FlattenGradientStops
#include "jalium_triangulate.h"       // Contour, TriangulatePolygon, FlattenPathToContours
#include "vulkan_minimal.h"
#include <vector>
#include <cstdint>
#include <cstdlib>
#include <algorithm>
#include <limits>
#include <list>
#include <unordered_map>
#include <memory>

namespace jalium {

// ============================================================================
// ImpellerVulkanEngine — CPU tessellation engine for the Vulkan backend.
//
// Architecture (mirrors ImpellerD3D12Engine):
//   • CPU-side: pixel-space flatten → triangulate (or convex fan, or
//     TrigCache shape generators) → ImpellerDrawBatch.
//   • Stroke: jalium::ExpandStrokePath<VkImpellerVertex> with full
//     caps/joins/dash/miter, sub-pixel hairline alpha fade.
//   • Gradient: per-vertex SampleBrushGradient (linear/radial/sweep) baked
//     into VkImpellerVertex.r/g/b/a (premultiplied).
//
// The engine itself does NOT own a render pass / pipeline / framebuffer /
// output image — it only encodes batches. vulkan_render_target.cpp consumes
// GetBatches() through its existing GPU path (the same way D3D12's
// directRenderer consumes ImpellerD3D12Engine batches). This keeps a single
// GPU pipeline and frame composite path on the Vulkan side, mirroring D3D12.
// ============================================================================

/// Vertex layout (matches ImpellerVertex on the D3D12 side).
struct VkImpellerVertex {
    float x, y;
    float r, g, b, a;
};
static_assert(sizeof(VkImpellerVertex) == 24, "VkImpellerVertex must be 24 bytes");

/// A draw batch produced by the Impeller engine. Field layout intentionally
/// matches ImpellerDrawBatch on the D3D12 side so the cross-backend stroke /
/// shape templates can target either type with the same interface.
struct VkImpellerDrawBatch {
    std::vector<VkImpellerVertex> vertices;
    std::vector<uint32_t> indices;
    uint32_t pipelineType = 0; // 0=solid fill (CPU-tessellated), 1=stencil-then-cover

    bool hasScissor = false;
    float scissorL = 0, scissorT = 0, scissorR = 0, scissorB = 0;

    // --- Per-batch rounded clip (snapshot at encode time, ALREADY-RESOLVED
    //     physical-pixel space). The engine has no transform/dpi context, so
    //     the render target resolves the live clipStack_'s innermost rounded
    //     clip to physical px and mirrors it in via SetRoundedClip (the Vulkan
    //     analogue of ImpellerD3D12Engine::SetRoundedClip — field set matches
    //     d3d12 ImpellerDrawBatch). Consumed by RenderEngineBatches, which
    //     forwards it into the engine-batch pipeline's push constants so Path/
    //     Polygon/stroke content no longer leaks through a rounded Border's
    //     corners (previously only the AABB scissor applied). ---
    bool hasRoundedClip = false;
    float roundedClipRect[4] = { 0, 0, 0, 0 };        // L, T, R, B (physical px)
    float roundedClipCornerRadii[4] = { 0, 0, 0, 0 }; // TL, TR, BR, BL (physical px)

    bool hasCoverage = false;
    float coverageL = 0, coverageT = 0, coverageR = 0, coverageB = 0;

    std::vector<Contour> stencilContours;
    FillRule stencilFillRule = FillRule::EvenOdd;
    float stencilR = 0, stencilG = 0, stencilB = 0, stencilA = 0;
};

// ── GPU stencil-then-cover gate ─────────────────────────────────────────────
// JALIUM_VK_STENCIL_PATH selects the GPU stencil-then-cover path for solid
// fills/strokes (pipelineType=1, MSAA, consumed by RenderEngineBatches) over
// the legacy CPU scanline path (RasterizePathToRects → pixel-rect quads,
// pipelineType=0). Gated + read once so the new GPU path can land incrementally
// and be A/B compared against the CPU path. Default ON (validated): mirrors
// D3D12, whose non-solid FillPath/FillPolygon default to GPU stencil-then-cover
// (8x MSAA). Set JALIUM_VK_STENCIL_PATH=0 to opt back into the legacy CPU
// scanline path.
// Both Vulkan engines (Impeller + Vello) consult this — it lives here because
// vulkan_vello_engine.h includes this header and aliases the batch type.
inline bool VkStencilPathEnabled()
{
    static const bool enabled = []() {
#if defined(_WIN32)
        // /sdl (SDLCheck=true in the .vcxproj) escalates C4996 on std::getenv to
        // a hard error, and this inline lives in a header compiled into TUs that
        // don't define _CRT_SECURE_NO_WARNINGS. Use the bounds-checked _dupenv_s,
        // matching vulkan_runtime.cpp's established env-read pattern.
        char* e = nullptr;
        size_t len = 0;
        if (_dupenv_s(&e, &len, "JALIUM_VK_STENCIL_PATH") != 0 || e == nullptr) {
            if (e) free(e);
            return true;  // default ON (validated): GPU stencil-then-cover, 8x MSAA
        }
        // Explicit opt-out only: leading 0/f/F/n/N disables; anything else enables.
        const bool off = (e[0] == '0' || e[0] == 'f' || e[0] == 'F' ||
                          e[0] == 'n' || e[0] == 'N');
        free(e);
        return !off;
#else
        const char* e = std::getenv("JALIUM_VK_STENCIL_PATH");
        if (!e || e[0] == '\0') return true;  // default ON (validated) — parity with D3D12
        return !(e[0] == '0' || e[0] == 'f' || e[0] == 'F' || e[0] == 'n' || e[0] == 'N');
#endif
    }();
    return enabled;
}

// ── Transform-independent stroke MESH cache gate ─────────────────────────────
// JALIUM_VK_STROKE_CACHE gates the local-space stroke mesh cache used by both
// Vulkan engines' EncodeStrokePath (Impeller + Vello, which aliases this
// header). Default ON: the cacheable common case (solid / linear-radial-sweep
// gradient, non-dashed, non-analytic, positive width) flattens + ExpandStroke
// ONCE in source space, caches the feathered triangle mesh keyed on
// geometry + stroke params + scaleBucket (transform EXCLUDED), and per frame
// only transforms the cached vertices (O(N)) + recolors — mirroring
// ImpellerD3D12Engine::EncodeStrokePath. This bypasses BOTH the per-frame CPU
// scanline (RasterizePathToRects) AND the GPU stencil-then-cover for that case,
// emitting a pipelineType=0 mesh exactly like the gradient branch already does.
// Set JALIUM_VK_STROKE_CACHE=0 to fall back to the legacy per-frame
// flatten → ExpandStroke → (scanline | stencil) path for A/B comparison.
inline bool StrokeCacheEnabled()
{
    static const bool enabled = []() {
#if defined(_WIN32)
        // /sdl escalates C4996 on std::getenv → use _dupenv_s (see VkStencilPathEnabled).
        char* e = nullptr;
        size_t len = 0;
        if (_dupenv_s(&e, &len, "JALIUM_VK_STROKE_CACHE") != 0 || e == nullptr) {
            if (e) free(e);
            return true;  // default ON: transform-independent stroke mesh cache
        }
        const bool off = (e[0] == '0' || e[0] == 'f' || e[0] == 'F' ||
                          e[0] == 'n' || e[0] == 'N');
        free(e);
        return !off;
#else
        const char* e = std::getenv("JALIUM_VK_STROKE_CACHE");
        if (!e || e[0] == '\0') return true;  // default ON
        return !(e[0] == '0' || e[0] == 'f' || e[0] == 'F' || e[0] == 'n' || e[0] == 'N');
#endif
    }();
    return enabled;
}

// ── Cross-frame fill CONTOUR cache gate ─────────────────────────────────────
// JALIUM_VK_FILL_CACHE gates EncodeFillPath's cross-frame contour cache on the
// default solid → stencil-then-cover route. Parity: D3D12's default fill route
// memoizes its flatten product in the core StencilPathCache
// (jalium_stencil_path.h; key = path bytes + scaleBucket; capacity 256) so a
// static icon costs only constant re-record CPU per frame — the Vulkan route
// used to rerun TransformCommandsToPixelSpace + FlattenPathToContours every
// frame for every path. Default ON. Set JALIUM_VK_FILL_CACHE=0 to fall back to
// the per-frame flatten for A/B comparison.
inline bool VkFillPathCacheEnabled()
{
    static const bool enabled = []() {
#if defined(_WIN32)
        // /sdl escalates C4996 on std::getenv → use _dupenv_s (see VkStencilPathEnabled).
        char* e = nullptr;
        size_t len = 0;
        if (_dupenv_s(&e, &len, "JALIUM_VK_FILL_CACHE") != 0 || e == nullptr) {
            if (e) free(e);
            return true;  // default ON: cross-frame fill contour cache
        }
        const bool off = (e[0] == '0' || e[0] == 'f' || e[0] == 'F' ||
                          e[0] == 'n' || e[0] == 'N');
        free(e);
        return !off;
#else
        const char* e = std::getenv("JALIUM_VK_FILL_CACHE");
        if (!e || e[0] == '\0') return true;  // default ON
        return !(e[0] == '0' || e[0] == 'f' || e[0] == 'F' || e[0] == 'n' || e[0] == 'N');
#endif
    }();
    return enabled;
}

// ── Pixel-space stroke CONTOUR cache gate (dash / explicit-Antialiased) ─────
// JALIUM_VK_STROKE_PXCACHE gates the transform-keyed pixel-space stroke
// contour cache in EncodeStrokePath's legacy body — the Vulkan analogue of
// ImpellerD3D12Engine::EncodeStrokePathPixelCached. The inputs the local-space
// mesh cache above declines (dash patterns, explicit Antialiased) used to
// re-run flatten + dash-walk + ExpandStrokePath every frame with zero caching;
// with this ON that pipeline runs once per distinct (path bytes, stroke
// params, dash params, transform with 1/8-px-quantized dx/dy) key and later
// frames only translate the cached contours by the integer pixel offset.
// Default ON. Set JALIUM_VK_STROKE_PXCACHE=0 for the legacy per-frame pipeline
// (byte-identical output) for A/B comparison.
inline bool VkStrokePixelCacheEnabled()
{
    static const bool enabled = []() {
#if defined(_WIN32)
        // /sdl escalates C4996 on std::getenv → use _dupenv_s (see VkStencilPathEnabled).
        char* e = nullptr;
        size_t len = 0;
        if (_dupenv_s(&e, &len, "JALIUM_VK_STROKE_PXCACHE") != 0 || e == nullptr) {
            if (e) free(e);
            return true;  // default ON: pixel-space stroke contour cache
        }
        const bool off = (e[0] == '0' || e[0] == 'f' || e[0] == 'F' ||
                          e[0] == 'n' || e[0] == 'N');
        free(e);
        return !off;
#else
        const char* e = std::getenv("JALIUM_VK_STROKE_PXCACHE");
        if (!e || e[0] == '\0') return true;  // default ON
        return !(e[0] == '0' || e[0] == 'f' || e[0] == 'F' || e[0] == 'n' || e[0] == 'N');
#endif
    }();
    return enabled;
}

// ── Consecutive solid-fill batch coalescing gate ────────────────────────────
// JALIUM_VK_BATCH_MERGE gates the in-place merge of consecutive, state-
// compatible pipelineType==0 batches (solid fill, cached stroke mesh, gradient
// mesh, FillPath/FillPolygon) inside PushBatch — collapsing N back-to-back
// vkCmdDrawIndexed calls (icon rows, ScrollBar arrows, Checkbox glyphs, the
// ~35 cached strokes DevTools shows) into ONE. Mirrors
// ImpellerD3D12Engine::PushBatchWithCoverage. The result is byte-identical to
// the unmerged sequence: draw order is preserved (a batch is only appended to
// the immediately-preceding batch), every pipelineType==0 batch shares one PSO
// + MVP in the consumer (RenderEngineBatches), and per-vertex premultiplied
// color carries solid/gradient shading so a merged buffer shades correctly.
// Stencil-then-cover (pipelineType==1) batches are NEVER merged (they need
// their own stencil/cover pass) and batches under a different scissor are NEVER
// merged (would clip wrong). Default ON; set JALIUM_VK_BATCH_MERGE=0 to emit
// one batch per primitive for A/B comparison / debugging.
// Both Vulkan engines (Impeller + Vello) consult this — it lives here because
// vulkan_vello_engine.h includes this header and aliases the batch type.
inline bool VkBatchMergeEnabled()
{
    static const bool enabled = []() {
#if defined(_WIN32)
        // /sdl escalates C4996 on std::getenv → use _dupenv_s (see VkStencilPathEnabled).
        char* e = nullptr;
        size_t len = 0;
        if (_dupenv_s(&e, &len, "JALIUM_VK_BATCH_MERGE") != 0 || e == nullptr) {
            if (e) free(e);
            return true;  // default ON: coalesce consecutive solid-fill batches
        }
        const bool off = (e[0] == '0' || e[0] == 'f' || e[0] == 'F' ||
                          e[0] == 'n' || e[0] == 'N');
        free(e);
        return !off;
#else
        const char* e = std::getenv("JALIUM_VK_BATCH_MERGE");
        if (!e || e[0] == '\0') return true;  // default ON
        return !(e[0] == '0' || e[0] == 'f' || e[0] == 'F' || e[0] == 'n' || e[0] == 'N');
#endif
    }();
    return enabled;
}

// Build a stencil-then-cover batch (pipelineType=1) from pixel-space contours
// (premultiplied color). Drops degenerate (<3 vertex) contours; returns false
// if nothing renderable remains. The consumer (RenderEngineBatches) fan-
// triangulates these contours for the stencil pass and emits a tight bounding-
// box cover quad — geometry stays in pixel space (same fixed ortho MVP as the
// solid-quad path), mirroring ImpellerD3D12Engine::StencilThenCoverFill.
inline bool BuildVkStencilBatch(std::vector<Contour>&& contours, FillRule rule,
                                float r, float g, float b, float a,
                                VkImpellerDrawBatch& batch)
{
    contours.erase(
        std::remove_if(contours.begin(), contours.end(),
            [](const Contour& c) { return c.VertexCount() < 3; }),
        contours.end());
    if (contours.empty()) return false;
    batch.stencilContours = std::move(contours);
    batch.stencilFillRule = rule;
    batch.stencilR = r;
    batch.stencilG = g;
    batch.stencilB = b;
    batch.stencilA = a;
    batch.pipelineType = 1;
    return true;
}

class ImpellerVulkanEngine : public IRenderingEngine {
public:
    ImpellerVulkanEngine(VkDevice device, VkPhysicalDevice physicalDevice);
    ~ImpellerVulkanEngine() override;

    JaliumRenderingEngine GetType() const override { return JALIUM_ENGINE_IMPELLER; }
    bool Initialize() override;

    void BeginFrame(uint32_t viewportWidth, uint32_t viewportHeight) override;
    void SetScissorRect(float left, float top, float right, float bottom) override;
    void ClearScissorRect() override;

    // Per-batch rounded-clip mirror (parallel to scissor; mirrors
    // ImpellerD3D12Engine::SetRoundedClip). The render target feeds
    // ALREADY-RESOLVED physical-pixel clip values here right before each
    // Encode* call so every emitted batch snapshots its draw-time clip.
    // Not part of IRenderingEngine — vulkan_render_target.cpp downcasts by
    // GetType() (the same way D3D12 calls its concrete engine directly).
    void SetRoundedClip(const float rect[4], const float radii[4]) {
        hasRoundedClip_ = true;
        for (int i = 0; i < 4; ++i) {
            roundedClipRect_[i] = rect[i];
            roundedClipRadii_[i] = radii[i];
        }
    }
    void ClearRoundedClip() { hasRoundedClip_ = false; }

    // E4: path-MSAA "analytic-only" mode, the Vulkan analogue of D3D12's
    // pathAnalyticOnly_ (set by SetPathMsaaSampleCount(0)). When on, solid
    // fill/stroke paths bypass the GPU stencil-then-cover route and take the
    // analytic-AA scanline rasterizer (RasterizePathToRects) instead — much
    // cheaper than N× stencil MSAA on weak GPUs, and Vulkan's scanline path is
    // already true analytic coverage AA. The render target downcasts and calls
    // this (mirroring SetRoundedClip); not part of IRenderingEngine.
    void SetPathAnalyticOnly(bool analyticOnly) { pathAnalyticOnly_ = analyticOnly; }

    bool EncodeFillPath(
        float startX, float startY,
        const float* commands, uint32_t commandLength,
        const EngineBrushData& brush,
        FillRule fillRule,
        const EngineTransform& transform,
        int32_t edgeMode = -1) override;

    bool EncodeStrokePath(
        float startX, float startY,
        const float* commands, uint32_t commandLength,
        const EngineBrushData& brush,
        float strokeWidth, bool closed,
        int32_t lineJoin, float miterLimit,
        int32_t lineCap,
        const float* dashPattern, uint32_t dashCount, float dashOffset,
        const EngineTransform& transform,
        int32_t edgeMode = -1) override;

    bool EncodeFillPolygon(
        const float* points, uint32_t pointCount,
        const EngineBrushData& brush,
        FillRule fillRule,
        const EngineTransform& transform) override;

    bool EncodeFillEllipse(
        float cx, float cy, float rx, float ry,
        const EngineBrushData& brush,
        const EngineTransform& transform) override;

    /// IRenderingEngine::Execute — no-op for Impeller-Vulkan: the GPU draw
    /// happens externally in vulkan_render_target.cpp via GetBatches(), the
    /// same way D3D12 routes Impeller through directRenderer. Returning
    /// true so callers don't think the engine errored.
    bool Execute(void* commandList, void* renderTarget, uint32_t width, uint32_t height) override;
    bool HasPendingWork() const override;
    uint32_t GetEncodedPathCount() const override;

    /// Batch consumption API used by vulkan_render_target.cpp.
    const std::vector<VkImpellerDrawBatch>& GetBatches() const { return batches_; }
    void ClearBatches() { batches_.clear(); }

    /// Push a batch and snapshot the current scissor + computed coverage AABB.
    /// Then try to COALESCE it into the previous batch when both are solid-fill
    /// (pipelineType==0, no stencil contours) under the same scissor — folding
    /// the consecutive-batch merge of ImpellerD3D12Engine::PushBatchWithCoverage
    /// into PushBatch (Vulkan's PushBatch already computes coverage, so there is
    /// no separate WithCoverage fast path). Gated by JALIUM_VK_BATCH_MERGE
    /// (default ON). NEVER merges stencil-then-cover (pipelineType==1) batches
    /// or batches under a different scissor; draw order is preserved (only the
    /// immediately-preceding batch is appended to), so blended output is
    /// byte-identical to the unmerged sequence.
    void PushBatch(VkImpellerDrawBatch&& batch) {
        batch.hasScissor = hasScissor_;
        if (hasScissor_) {
            batch.scissorL = scissorLeft_;
            batch.scissorT = scissorTop_;
            batch.scissorR = scissorRight_;
            batch.scissorB = scissorBottom_;
        }
        batch.hasRoundedClip = hasRoundedClip_;
        if (hasRoundedClip_) {
            for (int i = 0; i < 4; ++i) {
                batch.roundedClipRect[i] = roundedClipRect_[i];
                batch.roundedClipCornerRadii[i] = roundedClipRadii_[i];
            }
        }
        ComputeBatchCoverage(batch);

        if (VkBatchMergeEnabled()
            && batch.pipelineType == 0 && batch.stencilContours.empty()
            && !batches_.empty())
        {
            auto& last = batches_.back();
            const bool scissorEq = (last.hasScissor == batch.hasScissor) &&
                (!batch.hasScissor ||
                 (last.scissorL == batch.scissorL &&
                  last.scissorT == batch.scissorT &&
                  last.scissorR == batch.scissorR &&
                  last.scissorB == batch.scissorB));
            // Batches under a DIFFERENT rounded clip must never merge — the
            // clip rides in per-draw push constants, so a merged buffer could
            // only carry one of them (mirrors the D3D12 rule that batching
            // "only splits when the clip actually differs").
            const bool roundedClipEq = (last.hasRoundedClip == batch.hasRoundedClip) &&
                (!batch.hasRoundedClip ||
                 (last.roundedClipRect[0] == batch.roundedClipRect[0] &&
                  last.roundedClipRect[1] == batch.roundedClipRect[1] &&
                  last.roundedClipRect[2] == batch.roundedClipRect[2] &&
                  last.roundedClipRect[3] == batch.roundedClipRect[3] &&
                  last.roundedClipCornerRadii[0] == batch.roundedClipCornerRadii[0] &&
                  last.roundedClipCornerRadii[1] == batch.roundedClipCornerRadii[1] &&
                  last.roundedClipCornerRadii[2] == batch.roundedClipCornerRadii[2] &&
                  last.roundedClipCornerRadii[3] == batch.roundedClipCornerRadii[3]));
            if (last.pipelineType == 0 && last.stencilContours.empty() && scissorEq && roundedClipEq) {
                const uint32_t baseVertex = (uint32_t)last.vertices.size();
                const size_t oldIndexCount = last.indices.size();

                last.vertices.insert(last.vertices.end(),
                    batch.vertices.begin(), batch.vertices.end());

                last.indices.resize(oldIndexCount + batch.indices.size());
                uint32_t* dst = last.indices.data() + oldIndexCount;
                const uint32_t* src = batch.indices.data();
                const size_t n = batch.indices.size();
                for (size_t i = 0; i < n; ++i) dst[i] = src[i] + baseVertex;

                if (batch.hasCoverage) {
                    if (last.hasCoverage) {
                        if (batch.coverageL < last.coverageL) last.coverageL = batch.coverageL;
                        if (batch.coverageT < last.coverageT) last.coverageT = batch.coverageT;
                        if (batch.coverageR > last.coverageR) last.coverageR = batch.coverageR;
                        if (batch.coverageB > last.coverageB) last.coverageB = batch.coverageB;
                    } else {
                        last.coverageL = batch.coverageL;
                        last.coverageT = batch.coverageT;
                        last.coverageR = batch.coverageR;
                        last.coverageB = batch.coverageB;
                        last.hasCoverage = true;
                    }
                }
                return;
            }
        }

        batches_.push_back(std::move(batch));
    }

    static void ComputeBatchCoverage(VkImpellerDrawBatch& batch) {
        float minX =  std::numeric_limits<float>::infinity();
        float minY =  std::numeric_limits<float>::infinity();
        float maxX = -std::numeric_limits<float>::infinity();
        float maxY = -std::numeric_limits<float>::infinity();
        bool any = false;

        for (const auto& v : batch.vertices) {
            if (v.x < minX) minX = v.x;
            if (v.y < minY) minY = v.y;
            if (v.x > maxX) maxX = v.x;
            if (v.y > maxY) maxY = v.y;
            any = true;
        }
        if (batch.pipelineType == 1) {
            for (const auto& c : batch.stencilContours) {
                uint32_t n = c.VertexCount();
                for (uint32_t i = 0; i < n; ++i) {
                    float px = c.X(i);
                    float py = c.Y(i);
                    if (px < minX) minX = px;
                    if (py < minY) minY = py;
                    if (px > maxX) maxX = px;
                    if (py > maxY) maxY = py;
                    any = true;
                }
            }
        }
        if (!any || !(maxX >= minX) || !(maxY >= minY)) {
            batch.hasCoverage = false;
            return;
        }
        batch.hasCoverage = true;
        batch.coverageL = minX;
        batch.coverageT = minY;
        batch.coverageR = maxX;
        batch.coverageB = maxY;
    }

private:
    void TransformPoint(float& x, float& y, const EngineTransform& t) const {
        float tx = t.m11 * x + t.m21 * y + t.dx;
        float ty = t.m12 * x + t.m22 * y + t.dy;
        x = tx;
        y = ty;
    }

    /// Pixel-space stroke expansion driven by jalium::ExpandStrokePath.
    /// flatPoints_ must be populated by the caller (already in pixel space).
    bool ExpandStroke(const EngineBrushData& brush,
                      float strokeWidth,
                      ImpellerJoin join, float miterLimit,
                      ImpellerCap cap, bool closed,
                      std::vector<Contour>* collectContours = nullptr);

    VkDevice device_;
    VkPhysicalDevice physicalDevice_;
    bool initialized_ = false;

    uint32_t viewportW_ = 0, viewportH_ = 0;

    float scissorLeft_ = 0, scissorTop_ = 0, scissorRight_ = 0, scissorBottom_ = 0;
    bool hasScissor_ = false;

    // Live rounded-clip mirror (SetRoundedClip / ClearRoundedClip), snapshotted
    // into each batch by PushBatch — parallel to the scissor fields above.
    bool hasRoundedClip_ = false;
    float roundedClipRect_[4] = { 0, 0, 0, 0 };
    float roundedClipRadii_[4] = { 0, 0, 0, 0 };

    // Scratch buffer for pixel-space flat polylines used by EncodeStrokePath.
    std::vector<float> flatPoints_;

    std::vector<VkImpellerDrawBatch> batches_;
    uint32_t encodedPathCount_ = 0;

    // ── Transform-independent local-space stroke MESH cache ──────────────────
    // Mirrors ImpellerD3D12Engine's StrokeCache (d3d12_impeller_engine.h:521).
    // The cached feather mesh is stored in SOURCE space and keyed on
    // geometry + stroke params + scaleBucket (transform EXCLUDED); each frame
    // EncodeStrokePath only transforms the cached vertices + recolors them, so
    // repeated / animated / scrolled strokes skip flatten + ExpandStroke
    // entirely. Holds only CPU geometry (no GPU handles), so it is safe to
    // persist across frames and device-lost. LRU-bounded at kStrokeCacheCapacity.
    struct CachedStrokeRects {
        std::vector<float>    positions;   // 2 floats/vertex, source space
        std::vector<uint8_t>  coverage;    // 0..255 per-vertex feather coverage
        std::vector<uint32_t> indices;
        float bboxL = 0, bboxT = 0, bboxR = 0, bboxB = 0;
    };
    struct StrokeRectsNode {
        uint64_t key;
        std::shared_ptr<const CachedStrokeRects> entry;
    };
    std::list<StrokeRectsNode> strokeCacheList_;
    std::unordered_map<uint64_t, std::list<StrokeRectsNode>::iterator> strokeCacheMap_;
    static constexpr size_t kStrokeCacheCapacity = 512;
    std::shared_ptr<const CachedStrokeRects> StrokeCacheFind(uint64_t key);
    void StrokeCacheInsert(uint64_t key, std::shared_ptr<const CachedStrokeRects> entry);

    // ── Cross-frame fill CONTOUR cache ────────────────────────────────────────
    // Parity target: D3D12's default fill route caches its flatten product in
    // the core StencilPathCache (jalium_stencil_path.h; LRU 256; key =
    // HashStencilPathInput over path bytes + scaleBucket, translation/rotation
    // independent) and a hit costs only constant re-record CPU. The Vulkan
    // batch consumer (vulkan_render_target.cpp) requires PIXEL-space contours
    // in VkImpellerDrawBatch::stencilContours and has no per-draw transform,
    // so core's local-space StencilPathGeometry (pre-fanned triangles) cannot
    // be consumed directly; instead the engine caches the SOURCE-space flatten
    // product under the SAME key construction and a hit pays one O(N)
    // TransformPoint pass into the batch copy — flatten (the dominant per-path
    // CPU cost) is skipped, the consumer's per-frame fan + memcpy is
    // unchanged. dpi is captured via scaleBucket because the engine receives
    // the final device transform. Empty entries are negative caches. CPU-only
    // data → safe across frames and device-lost. Gated by JALIUM_VK_FILL_CACHE.
    struct CachedFillContours {
        std::vector<Contour> contours;   // source space; <3-vertex contours removed
    };
    struct FillContoursNode {
        uint64_t key;
        std::shared_ptr<const CachedFillContours> entry;
    };
    std::list<FillContoursNode> fillContourCacheList_;
    std::unordered_map<uint64_t, std::list<FillContoursNode>::iterator> fillContourCacheMap_;
    static constexpr size_t kFillContourCacheCapacity = 256;   // = D3D12 kStencilPathCacheCapacity
    std::shared_ptr<const CachedFillContours> FillContourCacheFind(uint64_t key);
    void FillContourCacheInsert(uint64_t key, std::shared_ptr<const CachedFillContours> entry);

    // ── Pixel-space transform-keyed stroke CONTOUR cache ─────────────────────
    // Vulkan analogue of ImpellerD3D12Engine::EncodeStrokePathPixelCached's
    // LRUs (d3d12_impeller_engine.h:521-558; capacity 512 each): the legacy
    // stroke body (dash / explicit-Antialiased — everything the local-space
    // mesh cache above declines) is memoized keyed on path bytes + stroke
    // params + dash params + the QUANTIZED transform (m11/m12/m21/m22 exact;
    // dx/dy reduced to their 1/8-px fractional bucket, integer pixels
    // re-applied at emit — port of the D3D12 dx/dy strip at
    // d3d12_impeller_engine.cpp:2824). Value is the expanded stroke outline in
    // origin-relative pixel space — the common input of BOTH the
    // stencil-then-cover branch and the scanline fallback, so neither consumer
    // changes shape. Gated by JALIUM_VK_STROKE_PXCACHE.
    struct CachedStrokePixelContours {
        std::vector<Contour> contours;   // origin-relative pixel space
    };
    struct StrokePixelContoursNode {
        uint64_t key;
        std::shared_ptr<const CachedStrokePixelContours> entry;
    };
    std::list<StrokePixelContoursNode> strokePxCacheList_;
    std::unordered_map<uint64_t, std::list<StrokePixelContoursNode>::iterator> strokePxCacheMap_;
    static constexpr size_t kStrokePixelCacheCapacity = 512;   // = D3D12 kStrokeCacheCapacity
    std::shared_ptr<const CachedStrokePixelContours> StrokePixelCacheFind(uint64_t key);
    void StrokePixelCacheInsert(uint64_t key, std::shared_ptr<const CachedStrokePixelContours> entry);

    float flattenTolerance_ = 0.25f;

    // E4: when true, SetPathMsaaSampleCount(0) selected analytic-only rendering
    // for solid paths; UseStencilPath() then reports false so every fill/stroke
    // takes the scanline (analytic-AA) route rather than GPU stencil-then-cover.
    bool pathAnalyticOnly_ = false;
    // Effective "use GPU stencil-then-cover" predicate for this engine: the
    // process-wide gate AND the not-analytic-only per-target override. Every
    // fill/stroke stencil-vs-scanline branch consults this instead of the bare
    // VkStencilPathEnabled() so the E4 knob's 0=analytic-only case is honoured.
    bool UseStencilPath() const { return VkStencilPathEnabled() && !pathAnalyticOnly_; }

    TrigCache trigCache_;
};

} // namespace jalium
