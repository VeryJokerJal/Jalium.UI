#include "vulkan_vello_engine.h"
#include "vulkan_vello_shaders.h"
#include <cstring>
#include <cmath>
#include <algorithm>

namespace jalium {

// ============================================================================
// VelloVulkanEngine — Vello compute pipeline on Vulkan
//
// This implements the same algorithmic pipeline as VelloD3D12Engine but uses
// Vulkan compute shaders (SPIR-V) instead of HLSL.
//
// The CPU-side path encoding is identical.  The GPU-side pipeline stages
// (Flatten, Bin, Backdrop, Coarse, Fine) use the same algorithm translated
// from HLSL to GLSL→SPIR-V.
// ============================================================================

VelloVulkanEngine::VelloVulkanEngine(
    VkDevice device, VkPhysicalDevice physicalDevice,
    VkQueue computeQueue, uint32_t computeQueueFamily)
    : device_(device)
    , physicalDevice_(physicalDevice)
    , computeQueue_(computeQueue)
    , computeQueueFamily_(computeQueueFamily)
{
}

VelloVulkanEngine::~VelloVulkanEngine() {
    if (device_ == VK_NULL_HANDLE) return;

    vkDeviceWaitIdle(device_);

    // Destroy pipelines
    if (flattenPipeline_) vkDestroyPipeline(device_, flattenPipeline_, nullptr);
    if (binAllocPipeline_) vkDestroyPipeline(device_, binAllocPipeline_, nullptr);
    if (backdropPipeline_) vkDestroyPipeline(device_, backdropPipeline_, nullptr);
    if (coarsePipeline_) vkDestroyPipeline(device_, coarsePipeline_, nullptr);
    if (finePipeline_) vkDestroyPipeline(device_, finePipeline_, nullptr);
    if (pipelineLayout_) vkDestroyPipelineLayout(device_, pipelineLayout_, nullptr);
    if (descriptorSetLayout_) vkDestroyDescriptorSetLayout(device_, descriptorSetLayout_, nullptr);
    if (descriptorPool_) vkDestroyDescriptorPool(device_, descriptorPool_, nullptr);

    // Destroy buffers
    auto destroyBuffer = [&](VkBuffer& buf, VkDeviceMemory& mem) {
        if (buf) { vkDestroyBuffer(device_, buf, nullptr); buf = VK_NULL_HANDLE; }
        if (mem) { vkFreeMemory(device_, mem, nullptr); mem = VK_NULL_HANDLE; }
    };

    destroyBuffer(segmentBuffer_, segmentMemory_);
    destroyBuffer(pathInfoBuffer_, pathInfoMemory_);
    destroyBuffer(lineSegBuffer_, lineSegMemory_);
    destroyBuffer(tileBuffer_, tileMemory_);
    destroyBuffer(ptclBuffer_, ptclMemory_);
    destroyBuffer(stagingBuffer_, stagingMemory_);

    // Destroy output image
    if (outputImageView_) vkDestroyImageView(device_, outputImageView_, nullptr);
    if (outputImage_) vkDestroyImage(device_, outputImage_, nullptr);
    if (outputMemory_) vkFreeMemory(device_, outputMemory_, nullptr);

    if (commandPool_) vkDestroyCommandPool(device_, commandPool_, nullptr);
}

// ============================================================================
// Initialization
// ============================================================================

bool VelloVulkanEngine::Initialize() {
    if (initialized_) return true;

    // Create command pool
    VkCommandPoolCreateInfo poolInfo = {};
    poolInfo.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
    poolInfo.queueFamilyIndex = computeQueueFamily_;
    poolInfo.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;

    if (vkCreateCommandPool(device_, &poolInfo, nullptr, &commandPool_) != VK_SUCCESS) {
        return false;
    }

    if (!CreateDescriptorSetLayouts()) return false;
    if (!CreateComputePipelines()) return false;

    initialized_ = true;
    return true;
}

bool VelloVulkanEngine::CreateDescriptorSetLayouts() {
    // Vello compute shaders bind multiple storage buffers:
    //   binding 0: segments (SSBO)
    //   binding 1: pathInfo (SSBO)
    //   binding 2: lineSegs (SSBO)
    //   binding 3: tiles (SSBO)
    //   binding 4: PTCL (SSBO)
    //   binding 5: output image (storage image)
    //   binding 6: config (UBO)

    VkDescriptorSetLayoutBinding bindings[7] = {};
    for (int i = 0; i < 6; ++i) {
        bindings[i].binding = i;
        bindings[i].descriptorType = (i == 5)
            ? VK_DESCRIPTOR_TYPE_STORAGE_IMAGE
            : VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
        bindings[i].descriptorCount = 1;
        bindings[i].stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;
    }
    bindings[6].binding = 6;
    bindings[6].descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
    bindings[6].descriptorCount = 1;
    bindings[6].stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;

    VkDescriptorSetLayoutCreateInfo layoutInfo = {};
    layoutInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
    layoutInfo.bindingCount = 7;
    layoutInfo.pBindings = bindings;

    if (vkCreateDescriptorSetLayout(device_, &layoutInfo, nullptr, &descriptorSetLayout_) != VK_SUCCESS) {
        return false;
    }

    // Pipeline layout
    VkPipelineLayoutCreateInfo pipelineLayoutInfo = {};
    pipelineLayoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    pipelineLayoutInfo.setLayoutCount = 1;
    pipelineLayoutInfo.pSetLayouts = &descriptorSetLayout_;

    // Push constants for per-dispatch config
    VkPushConstantRange pushConstant = {};
    pushConstant.stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;
    pushConstant.offset = 0;
    pushConstant.size = 32; // viewportW, viewportH, tilesX, tilesY, pathCount, segCount, pad, pad
    pipelineLayoutInfo.pushConstantRangeCount = 1;
    pipelineLayoutInfo.pPushConstantRanges = &pushConstant;

    if (vkCreatePipelineLayout(device_, &pipelineLayoutInfo, nullptr, &pipelineLayout_) != VK_SUCCESS) {
        return false;
    }

    // Descriptor pool
    VkDescriptorPoolSize poolSizes[] = {
        { VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 30 },
        { VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 5 },
        { VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 5 },
    };

    VkDescriptorPoolCreateInfo dpInfo = {};
    dpInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
    dpInfo.maxSets = 10;
    dpInfo.poolSizeCount = 3;
    dpInfo.pPoolSizes = poolSizes;

    return vkCreateDescriptorPool(device_, &dpInfo, nullptr, &descriptorPool_) == VK_SUCCESS;
}

bool VelloVulkanEngine::CreateComputePipelines() {
    // Helper: create a compute pipeline from embedded SPIR-V
    auto createPipeline = [&](const uint32_t* spirvData, size_t spirvSize, VkPipeline& outPipeline) -> bool {
        VkShaderModuleCreateInfo moduleInfo = {};
        moduleInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        moduleInfo.codeSize = spirvSize;
        moduleInfo.pCode = spirvData;

        VkShaderModule shaderModule = VK_NULL_HANDLE;
        if (vkCreateShaderModule(device_, &moduleInfo, nullptr, &shaderModule) != VK_SUCCESS) {
            return false;
        }

        VkComputePipelineCreateInfo pipelineInfo = {};
        pipelineInfo.sType = VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO;
        pipelineInfo.stage.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        pipelineInfo.stage.stage = VK_SHADER_STAGE_COMPUTE_BIT;
        pipelineInfo.stage.module = shaderModule;
        pipelineInfo.stage.pName = "main";
        pipelineInfo.layout = pipelineLayout_;

        VkResult result = vkCreateComputePipelines(device_, VK_NULL_HANDLE, 1, &pipelineInfo,
                                                    nullptr, &outPipeline);
        vkDestroyShaderModule(device_, shaderModule, nullptr);
        return result == VK_SUCCESS;
    };

    // Create pipeline for each Vello compute stage
    if (!createPipeline(kVelloFlattenSpv, kVelloFlattenSpvSize, flattenPipeline_)) return false;
    if (!createPipeline(kVelloBinningSpv, kVelloBinningSpvSize, binAllocPipeline_)) return false;
    if (!createPipeline(kVelloBackdropSpv, kVelloBackdropSpvSize, backdropPipeline_)) return false;
    if (!createPipeline(kVelloCoarseSpv, kVelloCoarseSpvSize, coarsePipeline_)) return false;
    if (!createPipeline(kVelloFineSpv, kVelloFineSpvSize, finePipeline_)) return false;

    return true;
}

// ============================================================================
// Helper: Vulkan buffer creation
// ============================================================================

uint32_t VelloVulkanEngine::FindMemoryType(uint32_t typeFilter, VkMemoryPropertyFlags properties) {
    VkPhysicalDeviceMemoryProperties memProps;
    vkGetPhysicalDeviceMemoryProperties(physicalDevice_, &memProps);
    for (uint32_t i = 0; i < memProps.memoryTypeCount; ++i) {
        if ((typeFilter & (1 << i)) &&
            (memProps.memoryTypes[i].propertyFlags & properties) == properties) {
            return i;
        }
    }
    return UINT32_MAX;
}

VkBuffer VelloVulkanEngine::CreateBuffer(
    VkDeviceSize size, VkBufferUsageFlags usage,
    VkMemoryPropertyFlags properties, VkDeviceMemory& memory)
{
    VkBufferCreateInfo bufInfo = {};
    bufInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    bufInfo.size = size;
    bufInfo.usage = usage;
    bufInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;

    VkBuffer buffer = VK_NULL_HANDLE;
    if (vkCreateBuffer(device_, &bufInfo, nullptr, &buffer) != VK_SUCCESS) {
        return VK_NULL_HANDLE;
    }

    VkMemoryRequirements memReqs;
    vkGetBufferMemoryRequirements(device_, buffer, &memReqs);

    VkMemoryAllocateInfo allocInfo = {};
    allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocInfo.allocationSize = memReqs.size;
    allocInfo.memoryTypeIndex = FindMemoryType(memReqs.memoryTypeBits, properties);

    if (allocInfo.memoryTypeIndex == UINT32_MAX ||
        vkAllocateMemory(device_, &allocInfo, nullptr, &memory) != VK_SUCCESS) {
        vkDestroyBuffer(device_, buffer, nullptr);
        return VK_NULL_HANDLE;
    }

    vkBindBufferMemory(device_, buffer, memory, 0);
    return buffer;
}

bool VelloVulkanEngine::EnsureOutputImage(uint32_t w, uint32_t h) {
    if (outputImage_ && outputW_ == w && outputH_ == h) return true;

    // Clean up old
    if (outputImageView_) { vkDestroyImageView(device_, outputImageView_, nullptr); outputImageView_ = VK_NULL_HANDLE; }
    if (outputImage_) { vkDestroyImage(device_, outputImage_, nullptr); outputImage_ = VK_NULL_HANDLE; }
    if (outputMemory_) { vkFreeMemory(device_, outputMemory_, nullptr); outputMemory_ = VK_NULL_HANDLE; }

    VkImageCreateInfo imgInfo = {};
    imgInfo.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    imgInfo.imageType = VK_IMAGE_TYPE_2D;
    imgInfo.format = VK_FORMAT_R8G8B8A8_UNORM;
    imgInfo.extent = { w, h, 1 };
    imgInfo.mipLevels = 1;
    imgInfo.arrayLayers = 1;
    imgInfo.samples = VK_SAMPLE_COUNT_1_BIT;
    imgInfo.tiling = VK_IMAGE_TILING_OPTIMAL;
    imgInfo.usage = VK_IMAGE_USAGE_STORAGE_BIT | VK_IMAGE_USAGE_SAMPLED_BIT |
                    VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT;
    imgInfo.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;

    if (vkCreateImage(device_, &imgInfo, nullptr, &outputImage_) != VK_SUCCESS) return false;

    VkMemoryRequirements memReqs;
    vkGetImageMemoryRequirements(device_, outputImage_, &memReqs);

    VkMemoryAllocateInfo allocInfo = {};
    allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocInfo.allocationSize = memReqs.size;
    allocInfo.memoryTypeIndex = FindMemoryType(memReqs.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);

    if (vkAllocateMemory(device_, &allocInfo, nullptr, &outputMemory_) != VK_SUCCESS) return false;
    vkBindImageMemory(device_, outputImage_, outputMemory_, 0);

    VkImageViewCreateInfo viewInfo = {};
    viewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    viewInfo.image = outputImage_;
    viewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
    viewInfo.format = VK_FORMAT_R8G8B8A8_UNORM;
    viewInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    viewInfo.subresourceRange.levelCount = 1;
    viewInfo.subresourceRange.layerCount = 1;

    if (vkCreateImageView(device_, &viewInfo, nullptr, &outputImageView_) != VK_SUCCESS) return false;

    outputW_ = w;
    outputH_ = h;
    return true;
}

bool VelloVulkanEngine::EnsureBuffers(size_t segmentBytes, size_t pathInfoBytes) {
    // For now, recreate if needed (a real implementation would use suballocation)
    auto destroyBuffer = [&](VkBuffer& buf, VkDeviceMemory& mem) {
        if (buf) { vkDestroyBuffer(device_, buf, nullptr); buf = VK_NULL_HANDLE; }
        if (mem) { vkFreeMemory(device_, mem, nullptr); mem = VK_NULL_HANDLE; }
    };

    destroyBuffer(segmentBuffer_, segmentMemory_);
    destroyBuffer(pathInfoBuffer_, pathInfoMemory_);

    if (segmentBytes > 0) {
        segmentBuffer_ = CreateBuffer(segmentBytes,
            VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT,
            VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT, segmentMemory_);
        if (!segmentBuffer_) return false;
    }

    if (pathInfoBytes > 0) {
        pathInfoBuffer_ = CreateBuffer(pathInfoBytes,
            VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT,
            VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT, pathInfoMemory_);
        if (!pathInfoBuffer_) return false;
    }

    return true;
}

// ============================================================================
// Per-Frame Lifecycle
// ============================================================================

void VelloVulkanEngine::BeginFrame(uint32_t viewportWidth, uint32_t viewportHeight) {
    viewportW_ = viewportWidth;
    viewportH_ = viewportHeight;
    tilesX_ = (viewportWidth + kVkTileWidth - 1) / kVkTileWidth;
    tilesY_ = (viewportHeight + kVkTileHeight - 1) / kVkTileHeight;
    segments_.clear();
    pathInfos_.clear();
}

void VelloVulkanEngine::SetScissorRect(float left, float top, float right, float bottom) {
    scissorLeft_ = left; scissorTop_ = top;
    scissorRight_ = right; scissorBottom_ = bottom;
    hasScissor_ = true;
}

void VelloVulkanEngine::ClearScissorRect() {
    hasScissor_ = false;
}

// ============================================================================
// Path Encoding (CPU side — identical algorithm to D3D12 version)
// ============================================================================

void VelloVulkanEngine::FlattenBezier(
    float x0, float y0, float x1, float y1,
    float x2, float y2, float x3, float y3,
    float tolerance)
{
    // Wang's formula for adaptive subdivision
    float dx1 = x2 - 2.0f * x1 + x0;
    float dy1 = y2 - 2.0f * y1 + y0;
    float dx2 = x3 - 2.0f * x2 + x1;
    float dy2 = y3 - 2.0f * y2 + y1;

    float mx = std::max(std::abs(dx1), std::abs(dx2));
    float my = std::max(std::abs(dy1), std::abs(dy2));
    float maxDev = std::sqrt(mx * mx + my * my);

    if (maxDev <= tolerance) {
        // Flat enough — add as a line segment
        VkPathSegment seg = {};
        seg.p0x = x0; seg.p0y = y0;
        seg.p1x = x3; seg.p1y = y3;
        seg.tag = 0; // line
        seg.pathIndex = (uint32_t)pathInfos_.size();
        segments_.push_back(seg);
        return;
    }

    uint32_t n = (uint32_t)std::ceil(std::sqrt(3.0f / (4.0f * tolerance) * maxDev));
    n = std::min(n, 256u);

    float prevX = x0, prevY = y0;
    float dt = 1.0f / (float)n;

    for (uint32_t i = 1; i <= n; ++i) {
        float t = dt * i;
        float mt = 1.0f - t;
        float mt2 = mt * mt;
        float mt3 = mt2 * mt;
        float t2 = t * t;
        float t3 = t2 * t;

        float px = mt3 * x0 + 3.0f * mt2 * t * x1 + 3.0f * mt * t2 * x2 + t3 * x3;
        float py = mt3 * y0 + 3.0f * mt2 * t * y1 + 3.0f * mt * t2 * y2 + t3 * y3;

        VkPathSegment seg = {};
        seg.p0x = prevX; seg.p0y = prevY;
        seg.p1x = px; seg.p1y = py;
        seg.tag = 0; // line
        seg.pathIndex = (uint32_t)pathInfos_.size();
        segments_.push_back(seg);

        prevX = px; prevY = py;
    }
}

bool VelloVulkanEngine::EncodeFillPath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    const EngineBrushData& brush,
    FillRule fillRule,
    const EngineTransform& transform)
{
    uint32_t pathIndex = (uint32_t)pathInfos_.size();
    uint32_t segStart = (uint32_t)segments_.size();

    float curX = startX, curY = startY;
    TransformPoint(curX, curY, transform);
    float prevX = curX, prevY = curY;

    uint32_t i = 0;
    while (i < commandLength) {
        float tag = commands[i];
        if (tag == 0.0f && i + 2 < commandLength) {
            // LineTo
            float x = commands[i + 1], y = commands[i + 2];
            TransformPoint(x, y, transform);

            VkPathSegment seg = {};
            seg.p0x = prevX; seg.p0y = prevY;
            seg.p1x = x; seg.p1y = y;
            seg.tag = 0;
            seg.pathIndex = pathIndex;
            segments_.push_back(seg);

            prevX = x; prevY = y;
            i += 3;
        } else if (tag == 1.0f && i + 6 < commandLength) {
            // BezierTo (cubic)
            float cp1x = commands[i + 1], cp1y = commands[i + 2];
            float cp2x = commands[i + 3], cp2y = commands[i + 4];
            float ex = commands[i + 5], ey = commands[i + 6];

            TransformPoint(cp1x, cp1y, transform);
            TransformPoint(cp2x, cp2y, transform);
            TransformPoint(ex, ey, transform);

            FlattenBezier(prevX, prevY, cp1x, cp1y, cp2x, cp2y, ex, ey, 0.25f);

            prevX = ex; prevY = ey;
            i += 7;
        } else {
            i++;
        }
    }

    // Close path
    if (std::abs(prevX - curX) > 1e-5f || std::abs(prevY - curY) > 1e-5f) {
        VkPathSegment seg = {};
        seg.p0x = prevX; seg.p0y = prevY;
        seg.p1x = curX; seg.p1y = curY;
        seg.tag = 0;
        seg.pathIndex = pathIndex;
        segments_.push_back(seg);
    }

    uint32_t segCount = (uint32_t)segments_.size() - segStart;
    if (segCount == 0) return false;

    // Compute bounding box
    float bboxMinX = 1e9f, bboxMinY = 1e9f, bboxMaxX = -1e9f, bboxMaxY = -1e9f;
    for (uint32_t s = segStart; s < segStart + segCount; ++s) {
        auto& seg = segments_[s];
        bboxMinX = std::min({ bboxMinX, seg.p0x, seg.p1x });
        bboxMinY = std::min({ bboxMinY, seg.p0y, seg.p1y });
        bboxMaxX = std::max({ bboxMaxX, seg.p0x, seg.p1x });
        bboxMaxY = std::max({ bboxMaxY, seg.p0y, seg.p1y });
    }

    VkPathInfo info = {};
    info.bboxMinX = bboxMinX; info.bboxMinY = bboxMinY;
    info.bboxMaxX = bboxMaxX; info.bboxMaxY = bboxMaxY;
    info.r = brush.r * brush.a;  // premultiply
    info.g = brush.g * brush.a;
    info.b = brush.b * brush.a;
    info.a = brush.a;
    info.segmentOffset = segStart;
    info.segmentCount = segCount;
    info.fillRule = (fillRule == FillRule::NonZero) ? kVkFillRuleNonZero : kVkFillRuleEvenOdd;
    info.brushType = brush.type;
    pathInfos_.push_back(info);

    return true;
}

bool VelloVulkanEngine::EncodeStrokePath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    const EngineBrushData& brush,
    float strokeWidth, bool closed,
    int32_t lineJoin, float miterLimit,
    int32_t lineCap,
    const float* dashPattern, uint32_t dashCount, float dashOffset,
    const EngineTransform& transform)
{
    // For strokes, expand on CPU to fill path, then encode as fill.
    // This matches Impeller's approach of CPU stroke expansion.
    // TODO: Implement GPU-accelerated stroke rendering.

    // For now, delegate to fill path with expanded stroke geometry.
    // A complete implementation would use the same stroke expansion as ImpellerD3D12Engine.
    return EncodeFillPath(startX, startY, commands, commandLength, brush,
                          FillRule::NonZero, transform);
}

bool VelloVulkanEngine::EncodeFillPolygon(
    const float* points, uint32_t pointCount,
    const EngineBrushData& brush,
    FillRule fillRule,
    const EngineTransform& transform)
{
    if (pointCount < 3) return false;

    // Convert polygon to path commands
    std::vector<float> commands;
    commands.reserve((pointCount - 1) * 3);
    for (uint32_t i = 1; i < pointCount; ++i) {
        commands.push_back(0.0f); // LineTo
        commands.push_back(points[i * 2]);
        commands.push_back(points[i * 2 + 1]);
    }
    // Close
    commands.push_back(0.0f);
    commands.push_back(points[0]);
    commands.push_back(points[1]);

    return EncodeFillPath(points[0], points[1],
                          commands.data(), (uint32_t)commands.size(),
                          brush, fillRule, transform);
}

bool VelloVulkanEngine::EncodeFillEllipse(
    float cx, float cy, float rx, float ry,
    const EngineBrushData& brush,
    const EngineTransform& transform)
{
    // Approximate with 4 cubic bezier curves
    constexpr float kappa = 0.5522847498f;
    float kx = rx * kappa;
    float ky = ry * kappa;

    float startX = cx, startY = cy - ry;

    float commands[] = {
        1, cx + kx, cy - ry,  cx + rx, cy - ky,  cx + rx, cy,
        1, cx + rx, cy + ky,  cx + kx, cy + ry,  cx, cy + ry,
        1, cx - kx, cy + ry,  cx - rx, cy + ky,  cx - rx, cy,
        1, cx - rx, cy - ky,  cx - kx, cy - ry,  cx, cy - ry,
    };

    return EncodeFillPath(startX, startY,
                          commands, sizeof(commands) / sizeof(float),
                          brush, FillRule::NonZero, transform);
}

// ============================================================================
// GPU Execution
// ============================================================================

bool VelloVulkanEngine::Execute(void* commandList, void* renderTarget, uint32_t width, uint32_t height) {
    if (pathInfos_.empty()) return true;

    auto cmdBuf = static_cast<VkCommandBuffer>(commandList);

    if (!EnsureOutputImage(width, height)) return false;

    size_t segBytes = segments_.size() * sizeof(VkPathSegment);
    size_t pathBytes = pathInfos_.size() * sizeof(VkPathInfo);

    if (!EnsureBuffers(segBytes, pathBytes)) return false;

    // Upload CPU data to GPU staging buffer
    size_t totalUpload = segBytes + pathBytes;
    if (totalUpload > stagingSize_) {
        if (stagingBuffer_) {
            vkDestroyBuffer(device_, stagingBuffer_, nullptr);
            vkFreeMemory(device_, stagingMemory_, nullptr);
            stagingBuffer_ = VK_NULL_HANDLE;
            stagingMemory_ = VK_NULL_HANDLE;
        }
        stagingBuffer_ = CreateBuffer(totalUpload,
            VK_BUFFER_USAGE_TRANSFER_SRC_BIT,
            VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
            stagingMemory_);
        if (!stagingBuffer_) return false;
        stagingSize_ = totalUpload;
    }

    // Map and copy data
    void* mapped = nullptr;
    vkMapMemory(device_, stagingMemory_, 0, totalUpload, 0, &mapped);
    memcpy(mapped, segments_.data(), segBytes);
    memcpy((uint8_t*)mapped + segBytes, pathInfos_.data(), pathBytes);
    vkUnmapMemory(device_, stagingMemory_);

    // Copy staging → GPU buffers
    VkBufferCopy segCopy = {};
    segCopy.size = segBytes;
    vkCmdCopyBuffer(cmdBuf, stagingBuffer_, segmentBuffer_, 1, &segCopy);

    VkBufferCopy pathCopy = {};
    pathCopy.srcOffset = segBytes;
    pathCopy.size = pathBytes;
    vkCmdCopyBuffer(cmdBuf, stagingBuffer_, pathInfoBuffer_, 1, &pathCopy);

    // Memory barrier: transfer → compute read
    VkMemoryBarrier barrier = {};
    barrier.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
    barrier.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
    vkCmdPipelineBarrier(cmdBuf,
        VK_PIPELINE_STAGE_TRANSFER_BIT,
        VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
        0, 1, &barrier, 0, nullptr, 0, nullptr);

    // Dispatch Vello compute pipeline stages
    uint32_t segCount = (uint32_t)segments_.size();
    uint32_t pathCount = (uint32_t)pathInfos_.size();

    // Push constants: viewportW, viewportH, tilesX, tilesY, pathCount, segCount, pad, pad
    struct PushConst {
        uint32_t viewportW, viewportH, tilesX, tilesY;
        uint32_t pathCount, segCount, pad0, pad1;
    } pc = { viewportW_, viewportH_, tilesX_, tilesY_, pathCount, segCount, 0, 0 };

    // Stage 1: Flatten — bezier curves → line segments
    vkCmdBindPipeline(cmdBuf, VK_PIPELINE_BIND_POINT_COMPUTE, flattenPipeline_);
    vkCmdPushConstants(cmdBuf, pipelineLayout_, VK_SHADER_STAGE_COMPUTE_BIT, 0, sizeof(pc), &pc);
    vkCmdDispatch(cmdBuf, (segCount + 255) / 256, 1, 1);

    // Barrier: flatten → binning
    barrier.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
    barrier.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
    vkCmdPipelineBarrier(cmdBuf, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
        VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, 1, &barrier, 0, nullptr, 0, nullptr);

    // Stage 2: Binning — assign segments to tiles
    vkCmdBindPipeline(cmdBuf, VK_PIPELINE_BIND_POINT_COMPUTE, binAllocPipeline_);
    vkCmdDispatch(cmdBuf, (tilesX_ * tilesY_ + 255) / 256, 1, 1);

    vkCmdPipelineBarrier(cmdBuf, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
        VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, 1, &barrier, 0, nullptr, 0, nullptr);

    // Stage 3: Backdrop — prefix-sum winding numbers
    vkCmdBindPipeline(cmdBuf, VK_PIPELINE_BIND_POINT_COMPUTE, backdropPipeline_);
    vkCmdDispatch(cmdBuf, tilesX_, 1, 1);

    vkCmdPipelineBarrier(cmdBuf, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
        VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, 1, &barrier, 0, nullptr, 0, nullptr);

    // Stage 4: Coarse — generate per-tile command lists (PTCL)
    vkCmdBindPipeline(cmdBuf, VK_PIPELINE_BIND_POINT_COMPUTE, coarsePipeline_);
    vkCmdDispatch(cmdBuf, (tilesX_ * tilesY_ + 255) / 256, 1, 1);

    vkCmdPipelineBarrier(cmdBuf, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
        VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, 1, &barrier, 0, nullptr, 0, nullptr);

    // Stage 5: Fine — render final pixels with analytical AA
    vkCmdBindPipeline(cmdBuf, VK_PIPELINE_BIND_POINT_COMPUTE, finePipeline_);
    vkCmdDispatch(cmdBuf, tilesX_, tilesY_, 1);

    return true;
}

bool VelloVulkanEngine::HasPendingWork() const {
    return !pathInfos_.empty();
}

uint32_t VelloVulkanEngine::GetEncodedPathCount() const {
    return (uint32_t)pathInfos_.size();
}

} // namespace jalium
