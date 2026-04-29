#pragma once

#include "jalium_rendering_engine.h"
#include "jalium_impeller_shapes.h"
#include "jalium_impeller_stroke.h"
#include "jalium_gradient_sample.h"
#include "jalium_triangulate.h"
#include "vulkan_impeller_engine.h"   // for VkImpellerVertex / VkImpellerDrawBatch — Vello shares the same on-wire type
#include "vulkan_minimal.h"
#include <vector>
#include <cstdint>
#include <limits>

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
        const EngineTransform& transform) override;

    bool EncodeStrokePath(
        float startX, float startY,
        const float* commands, uint32_t commandLength,
        const EngineBrushData& brush,
        float strokeWidth, bool closed,
        int32_t lineJoin, float miterLimit,
        int32_t lineCap,
        const float* dashPattern, uint32_t dashCount, float dashOffset,
        const EngineTransform& transform) override;

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

    void PushBatch(VkVelloDrawBatch&& batch) {
        batch.hasScissor = hasScissor_;
        if (hasScissor_) {
            batch.scissorL = scissorLeft_;
            batch.scissorT = scissorTop_;
            batch.scissorR = scissorRight_;
            batch.scissorB = scissorBottom_;
        }
        ComputeBatchCoverage(batch);
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

    float flattenTolerance_ = 0.25f;

    TrigCache trigCache_;
};

} // namespace jalium
