#pragma once

#include "jalium_rendering_engine.h"
#include "jalium_impeller_shapes.h"   // Trig / TrigCache / shape generators
#include "jalium_impeller_stroke.h"   // ImpellerCap / ImpellerJoin / ExpandStrokePath
#include "jalium_gradient_sample.h"   // SampleBrushGradient / FlattenGradientStops
#include "jalium_triangulate.h"       // Contour, TriangulatePolygon, FlattenPathToContours
#include "vulkan_minimal.h"
#include <vector>
#include <cstdint>
#include <limits>

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

    bool hasCoverage = false;
    float coverageL = 0, coverageT = 0, coverageR = 0, coverageB = 0;

    std::vector<Contour> stencilContours;
    FillRule stencilFillRule = FillRule::EvenOdd;
    float stencilR = 0, stencilG = 0, stencilB = 0, stencilA = 0;
};

class ImpellerVulkanEngine : public IRenderingEngine {
public:
    ImpellerVulkanEngine(VkDevice device, VkPhysicalDevice physicalDevice);
    ~ImpellerVulkanEngine() override;

    JaliumRenderingEngine GetType() const override { return JALIUM_ENGINE_IMPELLER; }
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

    /// Push a batch and snapshot the current scissor + computed coverage AABB
    /// (mirrors ImpellerD3D12Engine::PushBatch).
    void PushBatch(VkImpellerDrawBatch&& batch) {
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

    // Scratch buffer for pixel-space flat polylines used by EncodeStrokePath.
    std::vector<float> flatPoints_;

    std::vector<VkImpellerDrawBatch> batches_;
    uint32_t encodedPathCount_ = 0;

    float flattenTolerance_ = 0.25f;

    TrigCache trigCache_;
};

} // namespace jalium
