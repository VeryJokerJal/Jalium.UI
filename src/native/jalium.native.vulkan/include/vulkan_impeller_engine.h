#pragma once

#include "jalium_rendering_engine.h"
#include "vulkan_minimal.h"
#include <vector>
#include <cstdint>

namespace jalium {

// ============================================================================
// ImpellerVulkanEngine — tessellation-based 2D rendering engine on Vulkan
//
// Same architecture as ImpellerD3D12Engine:
//   1. CPU path flattening (bezier → line segments)
//   2. CPU tessellation (polygon → triangles, via ear-clipping)
//   3. CPU stroke expansion (offset curves with caps/joins)
//   4. GPU rasterization (Vulkan graphics pipeline)
// ============================================================================

/// Vertex layout (matches ImpellerVertex in the D3D12 version).
struct VkImpellerVertex {
    float x, y;
    float r, g, b, a;
};
static_assert(sizeof(VkImpellerVertex) == 24, "VkImpellerVertex must be 24 bytes");

struct VkImpellerDrawBatch {
    std::vector<VkImpellerVertex> vertices;
    std::vector<uint32_t> indices;
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

    bool Execute(void* commandList, void* renderTarget, uint32_t width, uint32_t height) override;
    bool HasPendingWork() const override;
    uint32_t GetEncodedPathCount() const override;

    VkImage GetOutputImage() const { return outputImage_; }
    VkImageView GetOutputImageView() const { return outputImageView_; }

private:
    // --- Path Processing (same algorithm as D3D12 version) ---

    void FlattenPath(float startX, float startY,
                     const float* commands, uint32_t commandLength,
                     const EngineTransform& transform);
    void FlattenCubic(float x0, float y0, float x1, float y1,
                      float x2, float y2, float x3, float y3,
                      float tolerance);

    void TransformPoint(float& x, float& y, const EngineTransform& t) const {
        float tx = t.m11 * x + t.m21 * y + t.dx;
        float ty = t.m12 * x + t.m22 * y + t.dy;
        x = tx; y = ty;
    }

    bool TessellateCurrentPath(const EngineBrushData& brush, FillRule fillRule);
    bool ExpandStroke(const EngineBrushData& brush, float strokeWidth,
                      int32_t join, float miterLimit, int32_t cap, bool closed);

    // --- GPU Resources ---

    bool CreateGraphicsPipeline();
    bool EnsureOutputImage(uint32_t w, uint32_t h);
    uint32_t FindMemoryType(uint32_t typeFilter, VkMemoryPropertyFlags properties);

    VkDevice device_;
    VkPhysicalDevice physicalDevice_;
    bool initialized_ = false;

    // Viewport
    uint32_t viewportW_ = 0, viewportH_ = 0;

    // Scissor
    float scissorLeft_ = 0, scissorTop_ = 0, scissorRight_ = 0, scissorBottom_ = 0;
    bool hasScissor_ = false;

    // CPU staging
    std::vector<float> flatPoints_;
    std::vector<VkImpellerDrawBatch> batches_;
    uint32_t encodedPathCount_ = 0;
    float flattenTolerance_ = 0.25f;

    // --- Vulkan Resources ---

    VkRenderPass renderPass_ = VK_NULL_HANDLE;
    VkPipelineLayout pipelineLayout_ = VK_NULL_HANDLE;
    VkPipeline solidFillPipeline_ = VK_NULL_HANDLE;
    VkShaderModule vertModule_ = VK_NULL_HANDLE;
    VkShaderModule fragModule_ = VK_NULL_HANDLE;

    // Output image
    VkImage outputImage_ = VK_NULL_HANDLE;
    VkDeviceMemory outputMemory_ = VK_NULL_HANDLE;
    VkImageView outputImageView_ = VK_NULL_HANDLE;
    VkFramebuffer framebuffer_ = VK_NULL_HANDLE;
    uint32_t outputW_ = 0, outputH_ = 0;

    // Upload buffers (host-visible)
    VkBuffer vertexBuffer_ = VK_NULL_HANDLE;
    VkDeviceMemory vertexMemory_ = VK_NULL_HANDLE;
    VkBuffer indexBuffer_ = VK_NULL_HANDLE;
    VkDeviceMemory indexMemory_ = VK_NULL_HANDLE;
    size_t vertexBufferSize_ = 0;
    size_t indexBufferSize_ = 0;
};

} // namespace jalium
