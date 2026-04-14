#pragma once

#include "jalium_rendering_engine.h"
#include "vulkan_minimal.h"
#include <vector>
#include <cstdint>

namespace jalium {

// ============================================================================
// VelloVulkanEngine — Vello GPU compute pipeline on Vulkan
//
// Translates the Vello rendering architecture to Vulkan compute shaders.
// Pipeline stages (matching the D3D12 implementation):
//   1. Path Encode  (CPU) — encode path segments into GPU buffers
//   2. Flatten      (CS)  — bezier curves → line segments (Wang's formula)
//   3. Bin + Alloc  (CS)  — assign segments to 16×16 tiles, allocate storage
//   4. Backdrop     (CS)  — prefix-sum winding number propagation
//   5. Coarse       (CS)  — generate per-tile command lists (PTCL)
//   6. Fine         (CS)  — render final pixels with analytical AA coverage
//
// Uses SPIR-V compute shaders compiled from the same algorithm as HLSL.
// ============================================================================

// Tile dimensions (must match compute shaders)
static constexpr uint32_t kVkTileWidth  = 16;
static constexpr uint32_t kVkTileHeight = 16;

// Fill rules
static constexpr uint32_t kVkFillRuleEvenOdd = 0;
static constexpr uint32_t kVkFillRuleNonZero = 1;

// GPU-side data structures (must match SPIR-V shaders)
struct VkPathSegment {
    float p0x, p0y;
    float p1x, p1y;
    float p2x, p2y;
    float p3x, p3y;
    uint32_t tag;
    uint32_t pathIndex;
    uint32_t pad0, pad1;
};
static_assert(sizeof(VkPathSegment) == 48, "VkPathSegment must be 48 bytes");

struct VkLineSeg {
    float p0x, p0y;
    float p1x, p1y;
    uint32_t pathIndex;
    uint32_t pad;
};
static_assert(sizeof(VkLineSeg) == 24, "VkLineSeg must be 24 bytes");

// Per-path metadata
struct VkPathInfo {
    float bboxMinX, bboxMinY, bboxMaxX, bboxMaxY;
    float r, g, b, a;       // Solid color (premultiplied)
    uint32_t segmentOffset;
    uint32_t segmentCount;
    uint32_t fillRule;
    uint32_t brushType;      // 0=solid, 1=linearGrad, etc.
};

class VelloVulkanEngine : public IRenderingEngine {
public:
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

    bool Execute(void* commandList, void* renderTarget, uint32_t width, uint32_t height) override;
    bool HasPendingWork() const override;
    uint32_t GetEncodedPathCount() const override;

    /// Get the output image for compositing.
    VkImage GetOutputImage() const { return outputImage_; }
    VkImageView GetOutputImageView() const { return outputImageView_; }

private:
    // --- Path encoding helpers (CPU, same algorithm as D3D12 version) ---

    void FlattenBezier(float x0, float y0, float x1, float y1,
                       float x2, float y2, float x3, float y3,
                       float tolerance);

    void TransformPoint(float& x, float& y, const EngineTransform& t) const {
        float tx = t.m11 * x + t.m21 * y + t.dx;
        float ty = t.m12 * x + t.m22 * y + t.dy;
        x = tx; y = ty;
    }

    // --- GPU Resource Management ---

    bool CreateComputePipelines();
    bool CreateDescriptorSetLayouts();
    bool EnsureBuffers(size_t segmentBytes, size_t pathInfoBytes);
    bool EnsureOutputImage(uint32_t w, uint32_t h);
    uint32_t FindMemoryType(uint32_t typeFilter, VkMemoryPropertyFlags properties);

    VkBuffer CreateBuffer(VkDeviceSize size, VkBufferUsageFlags usage,
                          VkMemoryPropertyFlags properties, VkDeviceMemory& memory);

    VkDevice device_;
    VkPhysicalDevice physicalDevice_;
    VkQueue computeQueue_;
    uint32_t computeQueueFamily_;
    bool initialized_ = false;

    // Viewport
    uint32_t viewportW_ = 0, viewportH_ = 0;
    uint32_t tilesX_ = 0, tilesY_ = 0;

    // Scissor
    float scissorLeft_ = 0, scissorTop_ = 0, scissorRight_ = 0, scissorBottom_ = 0;
    bool hasScissor_ = false;

    // CPU staging
    std::vector<VkPathSegment> segments_;
    std::vector<VkPathInfo> pathInfos_;

    // --- Vulkan Resources ---

    // Compute pipelines for each Vello stage
    VkPipelineLayout pipelineLayout_ = VK_NULL_HANDLE;
    VkPipeline flattenPipeline_ = VK_NULL_HANDLE;
    VkPipeline binAllocPipeline_ = VK_NULL_HANDLE;
    VkPipeline backdropPipeline_ = VK_NULL_HANDLE;
    VkPipeline coarsePipeline_ = VK_NULL_HANDLE;
    VkPipeline finePipeline_ = VK_NULL_HANDLE;

    VkDescriptorSetLayout descriptorSetLayout_ = VK_NULL_HANDLE;
    VkDescriptorPool descriptorPool_ = VK_NULL_HANDLE;

    // GPU buffers
    VkBuffer segmentBuffer_ = VK_NULL_HANDLE;
    VkDeviceMemory segmentMemory_ = VK_NULL_HANDLE;
    VkBuffer pathInfoBuffer_ = VK_NULL_HANDLE;
    VkDeviceMemory pathInfoMemory_ = VK_NULL_HANDLE;
    VkBuffer lineSegBuffer_ = VK_NULL_HANDLE;
    VkDeviceMemory lineSegMemory_ = VK_NULL_HANDLE;
    VkBuffer tileBuffer_ = VK_NULL_HANDLE;
    VkDeviceMemory tileMemory_ = VK_NULL_HANDLE;
    VkBuffer ptclBuffer_ = VK_NULL_HANDLE;
    VkDeviceMemory ptclMemory_ = VK_NULL_HANDLE;

    // Staging buffer (host-visible)
    VkBuffer stagingBuffer_ = VK_NULL_HANDLE;
    VkDeviceMemory stagingMemory_ = VK_NULL_HANDLE;
    size_t stagingSize_ = 0;

    // Output image
    VkImage outputImage_ = VK_NULL_HANDLE;
    VkDeviceMemory outputMemory_ = VK_NULL_HANDLE;
    VkImageView outputImageView_ = VK_NULL_HANDLE;
    uint32_t outputW_ = 0, outputH_ = 0;

    // Command pool for compute
    VkCommandPool commandPool_ = VK_NULL_HANDLE;
};

} // namespace jalium
