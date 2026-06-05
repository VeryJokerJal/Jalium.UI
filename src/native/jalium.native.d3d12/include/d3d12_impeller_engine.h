#pragma once

#include "jalium_rendering_engine.h"
#include "jalium_impeller_shapes.h"   // Trig / TrigCache / shape generators
#include "jalium_impeller_stroke.h"   // ImpellerCap / ImpellerJoin / ExpandStrokePath
#include "jalium_gradient_sample.h"   // SampleBrushGradient / FlattenGradientStops
#include "jalium_scanline_rasterizer.h"  // PixelRect — needed by stroke rasterized cache
#include "jalium_path_cache.h"        // PathGeometryCache (cross-backend source-space cache)
#include "d3d12_backend.h"
#include "d3d12_triangulate.h"
#include <vector>
#include <cstdint>
#include <cmath>
#include <array>
#include <limits>
#include <list>
#include <memory>
#include <unordered_map>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

namespace jalium {

// ============================================================================
// ImpellerD3D12Engine — Flutter Impeller-style tessellation engine on D3D12
//
// Architecture (matches Flutter Impeller):
//   1. Shape detection → optimized parametric generators (circle, ellipse, rrect)
//   2. Convex path detection → triangle fan (O(n), no ear-clipping)
//   3. Complex path → FlattenPathToContours + TriangulateCompoundPath
//   4. Stroke expansion with prevent-overdraw + sharp angle bridging
//   5. Gradient support (linear/radial) via vertex color interpolation
//   6. GPU rasterization via own PSO on back buffer
// ============================================================================

/// Vertex layout for Impeller solid fill pipeline.
struct ImpellerVertex {
    float x, y;         // Position
    float r, g, b, a;   // Color (premultiplied alpha)
};
static_assert(sizeof(ImpellerVertex) == 24, "ImpellerVertex must be 24 bytes");

/// A batch of triangles to draw with a single PSO.
/// pipelineType: 0=solid fill (CPU-tessellated), 1=stencil-then-cover (GPU, deferred)
struct ImpellerDrawBatch {
    std::vector<ImpellerVertex> vertices;
    std::vector<uint32_t> indices;
    uint32_t pipelineType = 0;

    // --- Per-batch scissor (snapshot at encode time) ---
    bool hasScissor = false;
    float scissorL = 0, scissorT = 0, scissorR = 0, scissorB = 0;

    // --- Per-batch rounded clip (snapshot at encode time, ALREADY-RESOLVED
    //     physical-pixel space). The engine has no transform/dpiScale, so the
    //     render target resolves the live roundedClipStack_ to physical px and
    //     mirrors it in via SetRoundedClip. Mirrors scissor so flush replays each
    //     batch's DRAW-TIME clip instead of the live stack (titlebar-glyph bug),
    //     and so batching only splits when the clip actually differs (no flush
    //     barrier → restores path batching across Borders/cards/buttons). ---
    bool hasRoundedClip = false;
    float roundedClipRect[4] = { 0, 0, 0, 0 };        // L, T, R, B (physical px)
    float roundedClipCornerRadii[4] = { 0, 0, 0, 0 }; // TL, TR, BR, BL (physical px)

    // --- Tile coverage (Flutter Impeller: Entity::GetCoverage) ---
    // Screen-space AABB of this batch. Used to cull batches outside the
    // viewport/scissor and to tighten the rasterizer scissor on submission,
    // mirroring Impeller's per-entity coverage tracking.
    bool hasCoverage = false;
    float coverageL = 0, coverageT = 0, coverageR = 0, coverageB = 0;

    // --- Stencil-then-cover data (pipelineType == 1) ---
    std::vector<Contour> stencilContours;   // original contours for stencil pass
    FillRule stencilFillRule = FillRule::EvenOdd;
    float stencilR = 0, stencilG = 0, stencilB = 0, stencilA = 0;
};

// ImpellerCap / ImpellerJoin enums and the Trig / TrigCache trig table now
// live in jalium_impeller_stroke.h and jalium_impeller_shapes.h (cross-backend).

// ============================================================================
// ImpellerD3D12Engine
// ============================================================================

class ImpellerD3D12Engine : public IRenderingEngine {
public:
    explicit ImpellerD3D12Engine(ID3D12Device* device, DXGI_FORMAT rtvFormat = DXGI_FORMAT_R8G8B8A8_UNORM);
    ~ImpellerD3D12Engine() override;

    JaliumRenderingEngine GetType() const override { return JALIUM_ENGINE_IMPELLER; }
    bool Initialize() override;

    void BeginFrame(uint32_t viewportWidth, uint32_t viewportHeight) override;
    void SetScissorRect(float left, float top, float right, float bottom) override;
    void ClearScissorRect() override;

    // Per-batch rounded-clip mirror (parallel to scissor). The render target
    // feeds ALREADY-RESOLVED physical-pixel clip values here at draw time so each
    // emitted batch snapshots its draw-time clip. Not part of IRenderingEngine.
    void SetRoundedClip(const float rect[4], const float radii[4]);
    void ClearRoundedClip();

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

    bool Execute(void* commandList, void* renderTarget, uint32_t width, uint32_t height) override;
    bool HasPendingWork() const override;
    uint32_t GetEncodedPathCount() const override;

    ID3D12Resource* GetOutputTexture() const { return outputTexture_.Get(); }
    const std::vector<ImpellerDrawBatch>& GetBatches() const { return batches_; }
    void ClearBatches() { batches_.clear(); }

    /// Push a batch with current scissor state automatically captured.
    /// Also computes the batch's screen-space tile coverage AABB from its
    /// vertices (or stencil contours), used by ExecuteOnCommandList to cull
    /// off-screen batches and tighten per-batch scissor rects.
    void PushBatch(ImpellerDrawBatch&& batch) {
        batch.hasScissor = hasScissor_;
        if (hasScissor_) {
            batch.scissorL = scissorLeft_;
            batch.scissorT = scissorTop_;
            batch.scissorR = scissorRight_;
            batch.scissorB = scissorBottom_;
        }
        batch.hasRoundedClip = hasRoundedClip_;
        if (hasRoundedClip_) {
            batch.roundedClipRect[0] = roundedClipRect_[0]; batch.roundedClipRect[1] = roundedClipRect_[1];
            batch.roundedClipRect[2] = roundedClipRect_[2]; batch.roundedClipRect[3] = roundedClipRect_[3];
            batch.roundedClipCornerRadii[0] = roundedClipCornerRadii_[0]; batch.roundedClipCornerRadii[1] = roundedClipCornerRadii_[1];
            batch.roundedClipCornerRadii[2] = roundedClipCornerRadii_[2]; batch.roundedClipCornerRadii[3] = roundedClipCornerRadii_[3];
        }
        ComputeBatchCoverage(batch);
        batches_.push_back(std::move(batch));
    }

    /// Fast-path PushBatch for callers that already know the batch's coverage
    /// AABB — typically the cache-hit branch in EncodeStrokePath / EncodeFillPath
    /// where we have the cached origin-relative bbox plus the per-call (intDx,
    /// intDy) offset. Skips the full vertex walk and additionally coalesces
    /// the batch into the previous batch when both share the same pipeline +
    /// scissor state, turning N consecutive solid-fill draws into a single GPU
    /// DrawIndexedInstanced. SaaSBackground.DrawWaveLines (28 strokes back-to-
    /// back, no scissor changes between them) used to issue 28 GPU draw calls;
    /// they now collapse to one.
    void PushBatchWithCoverage(ImpellerDrawBatch&& batch,
                               float coverL, float coverT,
                               float coverR, float coverB) {
        batch.hasScissor = hasScissor_;
        if (hasScissor_) {
            batch.scissorL = scissorLeft_;
            batch.scissorT = scissorTop_;
            batch.scissorR = scissorRight_;
            batch.scissorB = scissorBottom_;
        }
        batch.hasRoundedClip = hasRoundedClip_;
        if (hasRoundedClip_) {
            batch.roundedClipRect[0] = roundedClipRect_[0]; batch.roundedClipRect[1] = roundedClipRect_[1];
            batch.roundedClipRect[2] = roundedClipRect_[2]; batch.roundedClipRect[3] = roundedClipRect_[3];
            batch.roundedClipCornerRadii[0] = roundedClipCornerRadii_[0]; batch.roundedClipCornerRadii[1] = roundedClipCornerRadii_[1];
            batch.roundedClipCornerRadii[2] = roundedClipCornerRadii_[2]; batch.roundedClipCornerRadii[3] = roundedClipCornerRadii_[3];
        }
        bool batchHasCoverage = (coverR > coverL && coverB > coverT);
        if (batchHasCoverage) {
            batch.hasCoverage = true;
            batch.coverageL = coverL;
            batch.coverageT = coverT;
            batch.coverageR = coverR;
            batch.coverageB = coverB;
        } else {
            batch.hasCoverage = false;
        }

        // Try to coalesce into the previous batch when both share the same
        // pipeline + scissor state. We only coalesce solid-fill (pipelineType
        // 0) batches — stencil-then-cover (1) needs its own pass, and any
        // batch carrying user-supplied stencil contours can't be merged.
        if (batch.pipelineType == 0 && batch.stencilContours.empty()
            && !batches_.empty())
        {
            auto& last = batches_.back();
            bool scissorEq = (last.hasScissor == batch.hasScissor) &&
                (!batch.hasScissor ||
                 (last.scissorL == batch.scissorL &&
                  last.scissorT == batch.scissorT &&
                  last.scissorR == batch.scissorR &&
                  last.scissorB == batch.scissorB));
            bool roundedClipEq = (last.hasRoundedClip == batch.hasRoundedClip) &&
                (!batch.hasRoundedClip ||
                 (last.roundedClipRect[0] == batch.roundedClipRect[0] &&
                  last.roundedClipRect[1] == batch.roundedClipRect[1] &&
                  last.roundedClipRect[2] == batch.roundedClipRect[2] &&
                  last.roundedClipRect[3] == batch.roundedClipRect[3] &&
                  last.roundedClipCornerRadii[0] == batch.roundedClipCornerRadii[0] &&
                  last.roundedClipCornerRadii[1] == batch.roundedClipCornerRadii[1] &&
                  last.roundedClipCornerRadii[2] == batch.roundedClipCornerRadii[2] &&
                  last.roundedClipCornerRadii[3] == batch.roundedClipCornerRadii[3]));
            if (last.pipelineType == 0 && last.stencilContours.empty() && scissorEq && roundedClipEq)
            {
                uint32_t baseVertex = (uint32_t)last.vertices.size();
                size_t oldIndexCount = last.indices.size();

                last.vertices.insert(last.vertices.end(),
                    batch.vertices.begin(), batch.vertices.end());

                last.indices.resize(oldIndexCount + batch.indices.size());
                uint32_t* dst = last.indices.data() + oldIndexCount;
                const uint32_t* src = batch.indices.data();
                size_t n = batch.indices.size();
                for (size_t i = 0; i < n; i++) {
                    dst[i] = src[i] + baseVertex;
                }

                if (batchHasCoverage) {
                    if (last.hasCoverage) {
                        if (coverL < last.coverageL) last.coverageL = coverL;
                        if (coverT < last.coverageT) last.coverageT = coverT;
                        if (coverR > last.coverageR) last.coverageR = coverR;
                        if (coverB > last.coverageB) last.coverageB = coverB;
                    } else {
                        last.coverageL = coverL;
                        last.coverageT = coverT;
                        last.coverageR = coverR;
                        last.coverageB = coverB;
                        last.hasCoverage = true;
                    }
                }
                return;
            }
        }

        batches_.push_back(std::move(batch));
    }

    /// Compute screen-space AABB for a batch from its vertices or stencil
    /// contours. Sets hasCoverage = false when no geometry is available.
    static void ComputeBatchCoverage(ImpellerDrawBatch& batch) {
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

    bool ExecuteOnCommandList(
        ID3D12GraphicsCommandList* cmdList,
        D3D12_CPU_DESCRIPTOR_HANDLE rtvHandle,
        D3D12_RECT scissor,
        uint32_t viewportW, uint32_t viewportH);

private:
    // --- Fill: transform-independent geometry cache + legacy fallback ---

    /// Legacy pixel-space scanline fill. Preserved verbatim from the old
    /// EncodeFillPath; EncodeFillPath now delegates here for gradient brushes
    /// and for outlines its local-space triangulator can't handle.
    bool EncodeFillPathScanline(
        float startX, float startY,
        const float* commands, uint32_t commandLength,
        const EngineBrushData& brush,
        FillRule fillRule,
        const EngineTransform& transform,
        int32_t edgeMode);

    /// Legacy per-call pixel-space scanline polygon fill. EncodeFillPolygon
    /// delegates here for gradient brushes and polygons its local-space
    /// triangulator rejects.
    bool EncodeFillPolygonScanline(
        const float* points, uint32_t pointCount,
        const EngineBrushData& brush,
        FillRule fillRule,
        const EngineTransform& transform);

    /// Legacy pixel-space, transform-keyed stroke pipeline (analytic scanline
    /// or binary mesh). EncodeStrokePath delegates here for dashed strokes,
    /// explicit Antialiased mode, gradient brushes and the empty-command case.
    bool EncodeStrokePathPixelCached(
        float startX, float startY,
        const float* commands, uint32_t commandLength,
        const EngineBrushData& brush,
        float strokeWidth, bool closed,
        int32_t lineJoin, float miterLimit,
        int32_t lineCap,
        const float* dashPattern, uint32_t dashCount, float dashOffset,
        const EngineTransform& transform,
        int32_t edgeMode);

    /// Per-frame constant-width edge-AA feather ring built from the cached
    /// local-space boundary contours (our solid-fill target has no MSAA).
    void EmitContourFeather(
        const std::vector<Contour>& contours,
        const EngineTransform& transform,
        float r, float g, float b, float a);

    // --- Gradient Fill ---

    /// Encode a gradient-filled path (linear or radial).
    bool EncodeGradientFillPath(
        const std::vector<Contour>& contours,
        const EngineBrushData& brush,
        const EngineTransform& transform);

    // --- Legacy helpers ---

    void TransformPoint(float& x, float& y, const EngineTransform& t) const {
        float tx = t.m11 * x + t.m21 * y + t.dx;
        float ty = t.m12 * x + t.m22 * y + t.dy;
        x = tx;
        y = ty;
    }

    void FlattenPath(float startX, float startY,
                     const float* commands, uint32_t commandLength,
                     const EngineTransform& transform);

    void FlattenCubic(float x0, float y0, float x1, float y1,
                      float x2, float y2, float x3, float y3,
                      float tolerance);

    void FlattenQuadratic(float x0, float y0, float x1, float y1,
                          float x2, float y2, float tolerance);

    bool TessellateCurrentPath(const EngineBrushData& brush, FillRule fillRule);

    /// CPU stroke expansion. Thin wrapper around the cross-backend
    /// jalium::ExpandStrokePath template — keeps the same signature so the
    /// existing dash-walker in EncodeStrokePath can keep calling it.
    bool ExpandStroke(const EngineBrushData& brush,
                      float strokeWidth,
                      ImpellerJoin join, float miterLimit,
                      ImpellerCap cap, bool closed,
                      std::vector<Contour>* collectContours = nullptr);

    // GenerateRoundCap / GenerateRoundJoin / ComputeQuadrantDivisions /
    // ComputeStrokeAlphaCoverage now live in jalium_impeller_stroke.h /
    // jalium_impeller_shapes.h and are invoked through ExpandStrokePath.

    // --- Stencil-then-Cover (non-convex path fill) ---

    /// Fill a non-convex path using stencil buffer:
    ///  Pass 1: render all triangles (fan from origin) writing to stencil
    ///  Pass 2: cover bounding box, reading stencil (NonZero or EvenOdd)
    bool StencilThenCoverFill(
        const std::vector<Contour>& contours,
        FillRule fillRule,
        float r, float g, float b, float a,
        ID3D12GraphicsCommandList* cmdList,
        D3D12_CPU_DESCRIPTOR_HANDLE rtvHandle,
        uint32_t viewportW, uint32_t viewportH);

    bool EnsureStencilResources(uint32_t w, uint32_t h);

    // --- GPU Resources ---

    bool CreatePipelines();
    bool CreateRootSignature();
    bool EnsureOutputTexture(uint32_t w, uint32_t h);
    bool EnsureVertexBuffer(size_t requiredBytes);
    bool EnsureIndexBuffer(size_t requiredBytes);

    ID3D12Device* device_;
    DXGI_FORMAT rtvFormat_;
    bool initialized_ = false;

    uint32_t viewportW_ = 0, viewportH_ = 0;

    float scissorLeft_ = 0, scissorTop_ = 0, scissorRight_ = 0, scissorBottom_ = 0;
    bool hasScissor_ = false;

    // Rounded-clip mirror state (physical px, pre-resolved by the render target).
    bool hasRoundedClip_ = false;
    float roundedClipRect_[4] = { 0, 0, 0, 0 };
    float roundedClipCornerRadii_[4] = { 0, 0, 0, 0 };

    std::vector<float> flatPoints_;

    std::vector<ImpellerDrawBatch> batches_;
    uint32_t encodedPathCount_ = 0;

    float flattenTolerance_ = 0.25f;

    // Source-space PathGeometryCache (hoisted from jalium.native.vulkan to
    // jalium.native.core). EncodeFillPath caches the local-space flatten +
    // triangulation + boundary contours here, keyed by path data + fillRule +
    // scaleBucket (NOT the full transform). scaleBucket gives each scale
    // octave its own entry so source-space caching is safe across scales (the
    // under-subdivision concern that previously kept this unused). A moving /
    // scaled / rotated path now reuses the cached geometry and only pays an
    // O(N) per-frame vertex transform instead of re-rasterizing every frame.
    std::unique_ptr<PathGeometryCache> pathGeometryCache_;

    // Precomputed trig cache (shared across frames)
    TrigCache trigCache_;

    // --- D3D12 Resources ---

    ComPtr<ID3D12RootSignature> rootSignature_;
    ComPtr<ID3D12PipelineState> solidFillPSO_;

    ComPtr<ID3D12Resource> outputTexture_;
    uint32_t outputW_ = 0, outputH_ = 0;

    ComPtr<ID3D12Resource> vertexUploadBuffer_;
    ComPtr<ID3D12Resource> indexUploadBuffer_;
    size_t vertexUploadSize_ = 0;
    size_t indexUploadSize_ = 0;

    ComPtr<ID3D12Resource> vertexBuffer_;
    ComPtr<ID3D12Resource> indexBuffer_;
    size_t vertexBufferSize_ = 0;
    size_t indexBufferSize_ = 0;

    ComPtr<ID3D12DescriptorHeap> rtvHeap_;

    // Stencil-then-cover resources
    ComPtr<ID3D12PipelineState> stencilWritePSO_;   // writes to stencil, no color
    ComPtr<ID3D12PipelineState> stencilCoverNonZeroPSO_; // reads stencil != 0
    ComPtr<ID3D12PipelineState> stencilCoverEvenOddPSO_; // reads stencil bit 0
    ComPtr<ID3D12Resource> depthStencilBuffer_;
    ComPtr<ID3D12DescriptorHeap> dsvHeap_;
    uint32_t dsvW_ = 0, dsvH_ = 0;

    // Dedicated upload buffers for stencil pass (avoids overwriting solid batch data)
    ComPtr<ID3D12Resource> stencilVertexUploadBuffer_;
    ComPtr<ID3D12Resource> stencilIndexUploadBuffer_;
    size_t stencilVertexUploadSize_ = 0;
    size_t stencilIndexUploadSize_ = 0;

    bool EnsureStencilVertexBuffer(size_t requiredBytes);
    bool EnsureStencilIndexBuffer(size_t requiredBytes);

    // ─── Stroke rasterization cache ───────────────────────────────────────
    //
    // Why: ImpellerD3D12Engine::EncodeStrokePath runs the full CPU stroke
    // pipeline every frame for every path: pre-transform to pixel space →
    // FlattenPathToContours → optional dash expansion → ExpandStroke (mesh)
    // → RasterizePathToRects (analytic-AA scanline). Profiler attributes
    // the entire native cost back to IL_STUB_PInvoke (~80% self CPU); the
    // real work is the rasterizer (O(W × H × edges) on the bbox).
    //
    // What we cache: the final PixelRect list emitted by RasterizePathToRects.
    // This is the most-derived form available before the per-frame batch
    // build, so cache hits skip every CPU stage. PixelRect is plain data
    // (4 ints + alpha), so cache entries are dense and trivially copyable.
    //
    // Cache key: 64-bit FNV-1a hash over (commands raw bytes, startX/Y,
    // strokeWidth, closed, lineJoin, miterLimit, lineCap, dashPattern bytes,
    // dashCount, dashOffset, transform 6-float matrix). Transform is part
    // of the key because the entire pipeline runs in pixel space — a
    // different transform produces different rects. Static UI keeps the
    // same transform every frame, so hit rate is high in normal use;
    // scrolling/animating geometry naturally falls back to the slow path.
    // Cached stroke geometry: binary triangle mesh from ExpandStrokePath
    // (origin-relative pixel positions). Used when EdgeMode == Aliased (sharp,
    // no AA). ~200 verts per long bezier wave; the GPU rasterizer fills the
    // triangles directly. Coverage is implicit (binary 1× sampling).
    // For Antialiased (the default), strokes route through the analytic
    // cache below — PixelRect list with per-rect alpha, ~6000 rects per long
    // bezier wave but smooth edges and identical algorithm to fill.
    struct CachedStrokeRects {
        std::vector<float> positions;   // 2 floats per vertex, origin-relative
        std::vector<uint8_t> coverage;  // 0..255 per-vertex AA coverage mask
                                        //   (outer feather = 0, inner solid = 255)
                                        //   emit multiplies the premultiplied brush
                                        //   color by coverage/255 to reproduce the
                                        //   vertex-feather AA at draw time.
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

    // Analytic-coverage stroke cache: same shape as fill cache (PixelRect list),
    // populated by RasterizePathToRects on the expanded stroke contours. Used
    // when EdgeMode == Antialiased (default) so stroke edges get the same per-
    // rect alpha coverage that fill already gets — matches Vulkan stroke
    // exactly. HashStrokeInputs is shared with the binary cache; the cache key
    // partitions Antialiased vs Aliased entries via the trailing edgeMode byte.
    struct CachedStrokeAnalyticRects {
        std::vector<jalium::PixelRect> rects;
    };

    struct StrokeAnalyticRectsNode {
        uint64_t key;
        std::shared_ptr<const CachedStrokeAnalyticRects> entry;
    };

    std::list<StrokeAnalyticRectsNode> strokeAnalyticCacheList_;
    std::unordered_map<uint64_t, std::list<StrokeAnalyticRectsNode>::iterator> strokeAnalyticCacheMap_;
    static constexpr size_t kStrokeAnalyticCacheCapacity = 512;

    static uint64_t HashStrokeInputs(
        float startX, float startY,
        const float* commands, uint32_t commandLength,
        float strokeWidth, bool closed,
        int32_t lineJoin, float miterLimit, int32_t lineCap,
        const float* dashPattern, uint32_t dashCount, float dashOffset,
        const EngineTransform& transform,
        int32_t edgeMode) noexcept;

    std::shared_ptr<const CachedStrokeRects> StrokeCacheFind(uint64_t key);
    void StrokeCacheInsert(uint64_t key, std::shared_ptr<const CachedStrokeRects> entry);

    std::shared_ptr<const CachedStrokeAnalyticRects> StrokeAnalyticCacheFind(uint64_t key);
    void StrokeAnalyticCacheInsert(uint64_t key, std::shared_ptr<const CachedStrokeAnalyticRects> entry);

    // ─── Solid fill rasterization cache ───────────────────────────────────
    //
    // Mirrors StrokeCache for EncodeFillPath's solid-fill branch. Same
    // rationale: the RasterizePathToRects scanline pass dominates CPU
    // time and is purely a function of (commands, fillRule, transform).
    // A separate LRU keeps fill and stroke entries from evicting each
    // other under typical UI workloads where both populations are
    // sizeable. Gradient fills take the legacy source-space code path
    // and don't participate in this cache.
    // Fill cache stays on the PixelRect / scanline-rasterizer path because
    // RasterizePathToRects's analytic per-row coverage gives correct AA fills
    // for arbitrary concave / self-intersecting polygons (the kinds glyph and
    // SVG icon fills produce). Strokes were the only caller that hit the
    // 1500-rect-per-thin-curve pathology; fills typically produce dozens of
    // rects so the trade-off is fine.
    struct CachedFillRects {
        std::vector<jalium::PixelRect> rects;
    };

    struct FillRectsNode {
        uint64_t key;
        std::shared_ptr<const CachedFillRects> entry;
    };

    std::list<FillRectsNode> fillCacheList_;
    std::unordered_map<uint64_t, std::list<FillRectsNode>::iterator> fillCacheMap_;
    static constexpr size_t kFillCacheCapacity = 512;

    static uint64_t HashFillInputs(
        float startX, float startY,
        const float* commands, uint32_t commandLength,
        int32_t fillRule,
        const EngineTransform& transform) noexcept;

    std::shared_ptr<const CachedFillRects> FillCacheFind(uint64_t key);
    void FillCacheInsert(uint64_t key, std::shared_ptr<const CachedFillRects> entry);
};

} // namespace jalium
