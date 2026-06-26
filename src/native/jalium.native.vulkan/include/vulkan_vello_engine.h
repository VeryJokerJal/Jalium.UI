#pragma once

#include "jalium_rendering_engine.h"
#include "jalium_impeller_shapes.h"
#include "jalium_impeller_stroke.h"
#include "jalium_gradient_sample.h"
#include "jalium_triangulate.h"
#include "jalium_vello_encode.h"      // VelloSceneEncoder / VelloScene — the GPU compute path
#include "vulkan_impeller_engine.h"   // for VkImpellerVertex / VkImpellerDrawBatch — Vello shares the same on-wire type
#include "vulkan_minimal.h"
#include <vector>
#include <cstdint>
#include <limits>
#include <list>
#include <unordered_map>
#include <memory>

namespace jalium {

// VelloVulkanEngine and ImpellerVulkanEngine emit binary-identical batches
// today — same vertex layout (pos+color, 24 bytes), same per-batch metadata.
// Aliasing the Vello types to the Impeller types lets vulkan_render_target.cpp
// consume both engines through one RenderEngineBatches function. When real
// Vello compute shaders ship the alias can be replaced with a distinct struct
// without touching either engine class's encoder side.
using VkVelloVertex = VkImpellerVertex;
using VkVelloDrawBatch = VkImpellerDrawBatch;

// ============================================================================
// VelloVulkanEngine — Vulkan Vello-engine adapter.
//
// **Status (2026-04-28):** runs on the same CPU-tessellation + scanline-AA
// pipeline as ImpellerVulkanEngine, NOT on the Vello GPU compute pipeline.
//
// Why: the original 5-stage SPIR-V compute pipeline (vulkan_vello_shaders.h:
// flatten/binAlloc/backdrop/coarse/fine) was wired into Execute() with
// missing descriptor bindings, no output image layout transition, and only
// 2 of the 5 storage buffers actually allocated in EnsureBuffers — so it
// could never produce visually correct output. Rewiring that requires the
// SPIR-V's std430 buffer layout to match the C++ structs, which the binary
// doesn't expose. Until proper Vello compute shaders land, this engine
// shares the Impeller path so Vello hot-switch at least produces visually
// correct frames (algorithmically identical to Impeller, just labelled
// VELLO so RenderingEngine.GetType()/the user-facing toggle works).
//
// The engine is intentionally a copy of the Impeller engine's structure
// (rather than a `using` alias) so that future work can swap in real
// compute-pipeline rendering without touching every Encode caller.
// ============================================================================

// VkVelloVertex / VkVelloDrawBatch are aliases above; struct definitions
// removed to avoid type duplication.

class VelloVulkanEngine : public IRenderingEngine {
public:
    /// `computeQueue` and `computeQueueFamily` are accepted to keep the
    /// constructor signature stable for the day a real GPU compute pipeline
    /// returns; today they are unused (the engine runs entirely on the CPU
    /// tessellation + scanline-AA path).
    VelloVulkanEngine(VkDevice device, VkPhysicalDevice physicalDevice,
                      VkQueue computeQueue, uint32_t computeQueueFamily);
    ~VelloVulkanEngine() override;

    JaliumRenderingEngine GetType() const override { return JALIUM_ENGINE_VELLO; }
    bool Initialize() override;

    void BeginFrame(uint32_t viewportWidth, uint32_t viewportHeight) override;
    void SetScissorRect(float left, float top, float right, float bottom) override;
    void ClearScissorRect() override;

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

    /// IRenderingEngine::Execute — no-op. Same contract as ImpellerVulkanEngine:
    /// vulkan_render_target.cpp consumes batches via GetBatches() once the
    /// consumer side is wired up.
    bool Execute(void* commandList, void* renderTarget, uint32_t width, uint32_t height) override;
    bool HasPendingWork() const override;
    uint32_t GetEncodedPathCount() const override;

    const std::vector<VkVelloDrawBatch>& GetBatches() const { return batches_; }
    void ClearBatches() { batches_.clear(); }

    // ── Real Vello GPU compute path ────────────────────────────────────────
    // When the render target's VelloComputePipeline is live (the device exposes
    // scalarBlockLayout), it calls SetComputeMode(true): Encode* then feed the
    // backend-agnostic VelloSceneEncoder (producing the GPU scene buffers) INSTEAD
    // of the CPU-tessellation batch path, and the render target consumes
    // GetScene() through the compute graph + composite. When compute is
    // unavailable the engine keeps the batch path (the Impeller-equivalent
    // fallback) so Vello still renders on devices without scalar block layout.
    void SetComputeMode(bool on) { computeMode_ = on; }
    bool IsComputeMode() const { return computeMode_; }
    // Closes any open scissor-clip and computes the draw monoids. Call once per
    // frame before reading GetScene().
    void FinalizeScene() { sceneEncoder_.Finalize(); }
    const VelloScene& GetScene() const { return sceneEncoder_.scene(); }

    /// Push a batch and snapshot scissor + coverage, then COALESCE it into the
    /// previous batch when both are solid-fill (pipelineType==0, no stencil
    /// contours) under the same scissor — identical to
    /// ImpellerVulkanEngine::PushBatch (folds the consecutive-batch merge of
    /// ImpellerD3D12Engine::PushBatchWithCoverage in). Gated by
    /// JALIUM_VK_BATCH_MERGE (default ON). NEVER merges stencil-then-cover
    /// (pipelineType==1) or differing-scissor batches; draw order preserved, so
    /// output is byte-identical to the unmerged sequence. Only the
    /// CPU-tessellation batch path uses this (computeMode_ feeds sceneEncoder_).
    void PushBatch(VkVelloDrawBatch&& batch) {
        batch.hasScissor = hasScissor_;
        if (hasScissor_) {
            batch.scissorL = scissorLeft_;
            batch.scissorT = scissorTop_;
            batch.scissorR = scissorRight_;
            batch.scissorB = scissorBottom_;
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
            if (last.pipelineType == 0 && last.stencilContours.empty() && scissorEq) {
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

    static void ComputeBatchCoverage(VkVelloDrawBatch& batch) {
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
        // Stencil batches carry their geometry in stencilContours (vertices is
        // empty until the consumer fan-triangulates), so fold those in too —
        // mirrors ImpellerVulkanEngine::ComputeBatchCoverage.
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

    bool ExpandStroke(const EngineBrushData& brush,
                      float strokeWidth,
                      ImpellerJoin join, float miterLimit,
                      ImpellerCap cap, bool closed,
                      std::vector<Contour>* collectContours = nullptr);

    VkDevice device_;
    VkPhysicalDevice physicalDevice_;
    VkQueue computeQueue_;            // accepted for ABI; unused today.
    uint32_t computeQueueFamily_;     // accepted for ABI; unused today.
    bool initialized_ = false;

    uint32_t viewportW_ = 0, viewportH_ = 0;

    float scissorLeft_ = 0, scissorTop_ = 0, scissorRight_ = 0, scissorBottom_ = 0;
    bool hasScissor_ = false;

    std::vector<float> flatPoints_;

    std::vector<VkVelloDrawBatch> batches_;
    uint32_t encodedPathCount_ = 0;

    // ── Transform-independent local-space stroke MESH cache ──────────────────
    // Mirrors ImpellerVulkanEngine's stroke cache (solid-only here — the Vello
    // batch path has no gradient stroke branch). Source-space mesh keyed on
    // geometry + stroke params + scaleBucket (transform EXCLUDED); per frame
    // EncodeStrokePath only transforms + recolors the cached vertices. Active
    // only on the CPU-tessellation fallback (NOT computeMode_). CPU-only data,
    // safe to persist across frames / device-lost. LRU-bounded.
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

    float flattenTolerance_ = 0.25f;

    TrigCache trigCache_;

    // The real Vello GPU-compute scene encoder (active when computeMode_).
    VelloSceneEncoder sceneEncoder_;
    bool computeMode_ = false;
};

} // namespace jalium
