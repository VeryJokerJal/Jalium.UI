#include "vulkan_impeller_engine.h"
#include "vulkan_impeller_shaders.h"
#include "jalium_triangulate.h"
#include <cstring>
#include <cmath>
#include <algorithm>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

namespace jalium {

// ============================================================================
// ImpellerVulkanEngine — CPU tessellation + Vulkan rasterization
//
// The CPU-side tessellation code is identical to ImpellerD3D12Engine.
// Only the GPU-side differs: Vulkan graphics pipeline instead of D3D12.
// ============================================================================

// Embedded GLSL→SPIR-V placeholder.
// In production, these would be pre-compiled SPIR-V bytecode.
// The shader logic is identical to the D3D12 HLSL version:
//   VS: position = mvp * vec4(pos, 0, 1);  passthrough color
//   FS: output = vertex_color (premultiplied alpha)

ImpellerVulkanEngine::ImpellerVulkanEngine(VkDevice device, VkPhysicalDevice physicalDevice)
    : device_(device)
    , physicalDevice_(physicalDevice)
{
}

ImpellerVulkanEngine::~ImpellerVulkanEngine() {
    if (device_ == VK_NULL_HANDLE) return;

    vkDeviceWaitIdle(device_);

    if (solidFillPipeline_) vkDestroyPipeline(device_, solidFillPipeline_, nullptr);
    if (pipelineLayout_) vkDestroyPipelineLayout(device_, pipelineLayout_, nullptr);
    if (renderPass_) vkDestroyRenderPass(device_, renderPass_, nullptr);
    if (vertModule_) vkDestroyShaderModule(device_, vertModule_, nullptr);
    if (fragModule_) vkDestroyShaderModule(device_, fragModule_, nullptr);
    if (framebuffer_) vkDestroyFramebuffer(device_, framebuffer_, nullptr);
    if (outputImageView_) vkDestroyImageView(device_, outputImageView_, nullptr);
    if (outputImage_) vkDestroyImage(device_, outputImage_, nullptr);
    if (outputMemory_) vkFreeMemory(device_, outputMemory_, nullptr);
    if (vertexBuffer_) vkDestroyBuffer(device_, vertexBuffer_, nullptr);
    if (vertexMemory_) vkFreeMemory(device_, vertexMemory_, nullptr);
    if (indexBuffer_) vkDestroyBuffer(device_, indexBuffer_, nullptr);
    if (indexMemory_) vkFreeMemory(device_, indexMemory_, nullptr);
}

bool ImpellerVulkanEngine::Initialize() {
    if (initialized_) return true;

    if (!CreateGraphicsPipeline()) return false;

    initialized_ = true;
    return true;
}

uint32_t ImpellerVulkanEngine::FindMemoryType(uint32_t typeFilter, VkMemoryPropertyFlags properties) {
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

bool ImpellerVulkanEngine::CreateGraphicsPipeline() {
    // Create render pass
    VkAttachmentDescription colorAttachment = {};
    colorAttachment.format = VK_FORMAT_R8G8B8A8_UNORM;
    colorAttachment.samples = VK_SAMPLE_COUNT_1_BIT;
    colorAttachment.loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR;
    colorAttachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
    colorAttachment.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
    colorAttachment.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    colorAttachment.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    colorAttachment.finalLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

    VkAttachmentReference colorRef = {};
    colorRef.attachment = 0;
    colorRef.layout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

    VkSubpassDescription subpass = {};
    subpass.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
    subpass.colorAttachmentCount = 1;
    subpass.pColorAttachments = &colorRef;

    VkRenderPassCreateInfo rpInfo = {};
    rpInfo.sType = VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO;
    rpInfo.attachmentCount = 1;
    rpInfo.pAttachments = &colorAttachment;
    rpInfo.subpassCount = 1;
    rpInfo.pSubpasses = &subpass;

    if (vkCreateRenderPass(device_, &rpInfo, nullptr, &renderPass_) != VK_SUCCESS) {
        return false;
    }

    // Pipeline layout with push constants (4x4 MVP matrix)
    VkPushConstantRange pushConstant = {};
    pushConstant.stageFlags = VK_SHADER_STAGE_VERTEX_BIT;
    pushConstant.offset = 0;
    pushConstant.size = 64; // 4x4 float matrix

    VkPipelineLayoutCreateInfo layoutInfo = {};
    layoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    layoutInfo.pushConstantRangeCount = 1;
    layoutInfo.pPushConstantRanges = &pushConstant;

    if (vkCreatePipelineLayout(device_, &layoutInfo, nullptr, &pipelineLayout_) != VK_SUCCESS) {
        return false;
    }

    // Create shader modules from pre-compiled SPIR-V
    VkShaderModuleCreateInfo vertModuleInfo = {};
    vertModuleInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
    vertModuleInfo.codeSize = kImpellerSolidFillVertShaderSpvSize;
    vertModuleInfo.pCode = kImpellerSolidFillVertShaderSpv;

    if (vkCreateShaderModule(device_, &vertModuleInfo, nullptr, &vertModule_) != VK_SUCCESS) {
        return false;
    }

    VkShaderModuleCreateInfo fragModuleInfo = {};
    fragModuleInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
    fragModuleInfo.codeSize = kImpellerSolidFillFragShaderSpvSize;
    fragModuleInfo.pCode = kImpellerSolidFillFragShaderSpv;

    if (vkCreateShaderModule(device_, &fragModuleInfo, nullptr, &fragModule_) != VK_SUCCESS) {
        return false;
    }

    // Shader stages
    VkPipelineShaderStageCreateInfo shaderStages[2] = {};
    shaderStages[0].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    shaderStages[0].stage = VK_SHADER_STAGE_VERTEX_BIT;
    shaderStages[0].module = vertModule_;
    shaderStages[0].pName = "main";
    shaderStages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    shaderStages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
    shaderStages[1].module = fragModule_;
    shaderStages[1].pName = "main";

    // Vertex input: POSITION (float2) + COLOR (float4)
    VkVertexInputBindingDescription bindingDesc = {};
    bindingDesc.binding = 0;
    bindingDesc.stride = sizeof(VkImpellerVertex);
    bindingDesc.inputRate = VK_VERTEX_INPUT_RATE_VERTEX;

    VkVertexInputAttributeDescription attrDescs[2] = {};
    attrDescs[0].binding = 0;
    attrDescs[0].location = 0;
    attrDescs[0].format = VK_FORMAT_R32G32_SFLOAT;
    attrDescs[0].offset = 0;
    attrDescs[1].binding = 0;
    attrDescs[1].location = 1;
    attrDescs[1].format = VK_FORMAT_R32G32B32A32_SFLOAT;
    attrDescs[1].offset = 8;

    VkPipelineVertexInputStateCreateInfo vertexInput = {};
    vertexInput.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
    vertexInput.vertexBindingDescriptionCount = 1;
    vertexInput.pVertexBindingDescriptions = &bindingDesc;
    vertexInput.vertexAttributeDescriptionCount = 2;
    vertexInput.pVertexAttributeDescriptions = attrDescs;

    VkPipelineInputAssemblyStateCreateInfo inputAssembly = {};
    inputAssembly.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
    inputAssembly.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;

    VkPipelineViewportStateCreateInfo viewportState = {};
    viewportState.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
    viewportState.viewportCount = 1;
    viewportState.scissorCount = 1;

    VkPipelineRasterizationStateCreateInfo rasterizer = {};
    rasterizer.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
    rasterizer.polygonMode = VK_POLYGON_MODE_FILL;
    rasterizer.lineWidth = 1.0f;
    rasterizer.cullMode = VK_CULL_MODE_NONE;
    rasterizer.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;

    VkPipelineMultisampleStateCreateInfo multisampling = {};
    multisampling.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
    multisampling.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

    // Premultiplied alpha blending: src=One, dst=InvSrcAlpha
    VkPipelineColorBlendAttachmentState blendAttachment = {};
    blendAttachment.colorWriteMask = VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT |
                                     VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;
    blendAttachment.blendEnable = VK_TRUE;
    blendAttachment.srcColorBlendFactor = VK_BLEND_FACTOR_ONE;
    blendAttachment.dstColorBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
    blendAttachment.colorBlendOp = VK_BLEND_OP_ADD;
    blendAttachment.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
    blendAttachment.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
    blendAttachment.alphaBlendOp = VK_BLEND_OP_ADD;

    VkPipelineColorBlendStateCreateInfo colorBlending = {};
    colorBlending.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
    colorBlending.attachmentCount = 1;
    colorBlending.pAttachments = &blendAttachment;

    VkDynamicState dynamicStates[] = { VK_DYNAMIC_STATE_VIEWPORT, VK_DYNAMIC_STATE_SCISSOR };
    VkPipelineDynamicStateCreateInfo dynamicState = {};
    dynamicState.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
    dynamicState.dynamicStateCount = 2;
    dynamicState.pDynamicStates = dynamicStates;

    VkGraphicsPipelineCreateInfo pipelineInfo = {};
    pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
    pipelineInfo.stageCount = 2;
    pipelineInfo.pStages = shaderStages;
    pipelineInfo.pVertexInputState = &vertexInput;
    pipelineInfo.pInputAssemblyState = &inputAssembly;
    pipelineInfo.pViewportState = &viewportState;
    pipelineInfo.pRasterizationState = &rasterizer;
    pipelineInfo.pMultisampleState = &multisampling;
    pipelineInfo.pColorBlendState = &colorBlending;
    pipelineInfo.pDynamicState = &dynamicState;
    pipelineInfo.layout = pipelineLayout_;
    pipelineInfo.renderPass = renderPass_;
    pipelineInfo.subpass = 0;

    if (vkCreateGraphicsPipelines(device_, VK_NULL_HANDLE, 1, &pipelineInfo,
                                   nullptr, &solidFillPipeline_) != VK_SUCCESS) {
        return false;
    }

    return true;
}

bool ImpellerVulkanEngine::EnsureOutputImage(uint32_t w, uint32_t h) {
    if (outputImage_ && outputW_ == w && outputH_ == h) return true;

    // Clean up old
    if (framebuffer_) { vkDestroyFramebuffer(device_, framebuffer_, nullptr); framebuffer_ = VK_NULL_HANDLE; }
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
    imgInfo.usage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_SAMPLED_BIT |
                    VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
    imgInfo.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;

    if (vkCreateImage(device_, &imgInfo, nullptr, &outputImage_) != VK_SUCCESS) return false;

    VkMemoryRequirements memReqs;
    vkGetImageMemoryRequirements(device_, outputImage_, &memReqs);

    VkMemoryAllocateInfo allocInfo = {};
    allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocInfo.allocationSize = memReqs.size;
    allocInfo.memoryTypeIndex = FindMemoryType(memReqs.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    if (allocInfo.memoryTypeIndex == UINT32_MAX) return false;

    if (vkAllocateMemory(device_, &allocInfo, nullptr, &outputMemory_) != VK_SUCCESS) return false;
    vkBindImageMemory(device_, outputImage_, outputMemory_, 0);

    VkImageViewCreateInfo viewInfo = {};
    viewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    viewInfo.image = outputImage_;
    viewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
    viewInfo.format = VK_FORMAT_R8G8B8A8_UNORM;
    viewInfo.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };

    if (vkCreateImageView(device_, &viewInfo, nullptr, &outputImageView_) != VK_SUCCESS) return false;

    // Create framebuffer
    VkFramebufferCreateInfo fbInfo = {};
    fbInfo.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
    fbInfo.renderPass = renderPass_;
    fbInfo.attachmentCount = 1;
    fbInfo.pAttachments = &outputImageView_;
    fbInfo.width = w;
    fbInfo.height = h;
    fbInfo.layers = 1;

    if (vkCreateFramebuffer(device_, &fbInfo, nullptr, &framebuffer_) != VK_SUCCESS) return false;

    outputW_ = w;
    outputH_ = h;
    return true;
}

// ============================================================================
// Per-Frame Lifecycle
// ============================================================================

void ImpellerVulkanEngine::BeginFrame(uint32_t viewportWidth, uint32_t viewportHeight) {
    viewportW_ = viewportWidth;
    viewportH_ = viewportHeight;
    batches_.clear();
    encodedPathCount_ = 0;
    flatPoints_.clear();
}

void ImpellerVulkanEngine::SetScissorRect(float left, float top, float right, float bottom) {
    scissorLeft_ = left; scissorTop_ = top;
    scissorRight_ = right; scissorBottom_ = bottom;
    hasScissor_ = true;
}

void ImpellerVulkanEngine::ClearScissorRect() {
    hasScissor_ = false;
}

// ============================================================================
// Path Flattening (CPU — identical to ImpellerD3D12Engine)
// ============================================================================

void ImpellerVulkanEngine::FlattenPath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    const EngineTransform& transform)
{
    flatPoints_.clear();

    float sx = startX, sy = startY;
    TransformPoint(sx, sy, transform);
    flatPoints_.push_back(sx);
    flatPoints_.push_back(sy);

    float curX = startX, curY = startY;
    uint32_t i = 0;

    while (i < commandLength) {
        float tag = commands[i];
        if (tag == 0.0f && i + 2 < commandLength) {
            float x = commands[i + 1], y = commands[i + 2];
            TransformPoint(x, y, transform);
            flatPoints_.push_back(x);
            flatPoints_.push_back(y);
            curX = commands[i + 1]; curY = commands[i + 2];
            i += 3;
        } else if (tag == 1.0f && i + 6 < commandLength) {
            float cp1x = commands[i + 1], cp1y = commands[i + 2];
            float cp2x = commands[i + 3], cp2y = commands[i + 4];
            float ex = commands[i + 5], ey = commands[i + 6];

            float tcx = curX, tcy = curY;
            TransformPoint(tcx, tcy, transform);
            TransformPoint(cp1x, cp1y, transform);
            TransformPoint(cp2x, cp2y, transform);
            TransformPoint(ex, ey, transform);

            FlattenCubic(tcx, tcy, cp1x, cp1y, cp2x, cp2y, ex, ey, flattenTolerance_);

            curX = commands[i + 5]; curY = commands[i + 6];
            i += 7;
        } else {
            i++;
        }
    }
}

void ImpellerVulkanEngine::FlattenCubic(
    float x0, float y0, float x1, float y1,
    float x2, float y2, float x3, float y3,
    float tolerance)
{
    float dx1 = x2 - 2.0f * x1 + x0;
    float dy1 = y2 - 2.0f * y1 + y0;
    float dx2 = x3 - 2.0f * x2 + x1;
    float dy2 = y3 - 2.0f * y2 + y1;

    float mx = std::max(std::abs(dx1), std::abs(dx2));
    float my = std::max(std::abs(dy1), std::abs(dy2));
    float maxDev = std::sqrt(mx * mx + my * my);

    if (maxDev <= tolerance) {
        flatPoints_.push_back(x3);
        flatPoints_.push_back(y3);
        return;
    }

    uint32_t n = (uint32_t)std::ceil(std::sqrt(3.0f / (4.0f * tolerance) * maxDev));
    n = std::min(n, 256u);

    float dt = 1.0f / (float)n;
    for (uint32_t i = 1; i <= n; ++i) {
        float t = dt * i;
        float mt = 1.0f - t;
        float mt2 = mt * mt, mt3 = mt2 * mt;
        float t2 = t * t, t3 = t2 * t;

        float px = mt3 * x0 + 3.0f * mt2 * t * x1 + 3.0f * mt * t2 * x2 + t3 * x3;
        float py = mt3 * y0 + 3.0f * mt2 * t * y1 + 3.0f * mt * t2 * y2 + t3 * y3;

        flatPoints_.push_back(px);
        flatPoints_.push_back(py);
    }
}

// ============================================================================
// Tessellation (CPU — identical to ImpellerD3D12Engine)
// ============================================================================

bool ImpellerVulkanEngine::TessellateCurrentPath(const EngineBrushData& brush, FillRule fillRule) {
    uint32_t pointCount = (uint32_t)(flatPoints_.size() / 2);
    if (pointCount < 3) return false;

    std::vector<uint32_t> indices;
    if (!TriangulatePolygon(flatPoints_.data(), pointCount, indices)) return false;
    if (indices.empty()) return false;

    float r = brush.r * brush.a;
    float g = brush.g * brush.a;
    float b = brush.b * brush.a;
    float a = brush.a;

    VkImpellerDrawBatch batch;
    batch.vertices.reserve(pointCount);
    for (uint32_t i = 0; i < pointCount; ++i) {
        VkImpellerVertex v;
        v.x = flatPoints_[i * 2];
        v.y = flatPoints_[i * 2 + 1];
        v.r = r; v.g = g; v.b = b; v.a = a;
        batch.vertices.push_back(v);
    }
    batch.indices = std::move(indices);
    batches_.push_back(std::move(batch));
    return true;
}

bool ImpellerVulkanEngine::ExpandStroke(
    const EngineBrushData& brush, float strokeWidth,
    int32_t join, float miterLimit, int32_t cap, bool closed)
{
    uint32_t pointCount = (uint32_t)(flatPoints_.size() / 2);
    if (pointCount < 2) return false;

    float halfWidth = strokeWidth * 0.5f;
    float r = brush.r * brush.a;
    float g = brush.g * brush.a;
    float b = brush.b * brush.a;
    float a = brush.a;

    VkImpellerDrawBatch batch;
    auto& verts = batch.vertices;
    auto& indices = batch.indices;

    auto getX = [&](uint32_t i) { return flatPoints_[i * 2]; };
    auto getY = [&](uint32_t i) { return flatPoints_[i * 2 + 1]; };

    for (uint32_t i = 0; i + 1 < pointCount; ++i) {
        float dx = getX(i + 1) - getX(i);
        float dy = getY(i + 1) - getY(i);
        float len = std::sqrt(dx * dx + dy * dy);
        if (len < 1e-6f) continue;

        float nx = -dy / len * halfWidth;
        float ny = dx / len * halfWidth;

        uint32_t base = (uint32_t)verts.size();
        verts.push_back({ getX(i) + nx, getY(i) + ny, r, g, b, a });
        verts.push_back({ getX(i) - nx, getY(i) - ny, r, g, b, a });
        verts.push_back({ getX(i+1) + nx, getY(i+1) + ny, r, g, b, a });
        verts.push_back({ getX(i+1) - nx, getY(i+1) - ny, r, g, b, a });

        indices.push_back(base);
        indices.push_back(base + 1);
        indices.push_back(base + 2);
        indices.push_back(base + 1);
        indices.push_back(base + 3);
        indices.push_back(base + 2);
    }

    if (!verts.empty()) {
        batches_.push_back(std::move(batch));
    }
    return true;
}

// ============================================================================
// Path Encoding Entry Points
// ============================================================================

bool ImpellerVulkanEngine::EncodeFillPath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    const EngineBrushData& brush,
    FillRule fillRule,
    const EngineTransform& transform)
{
    FlattenPath(startX, startY, commands, commandLength, transform);
    bool ok = TessellateCurrentPath(brush, fillRule);
    if (ok) encodedPathCount_++;
    return ok;
}

bool ImpellerVulkanEngine::EncodeStrokePath(
    float startX, float startY,
    const float* commands, uint32_t commandLength,
    const EngineBrushData& brush,
    float strokeWidth, bool closed,
    int32_t lineJoin, float miterLimit,
    int32_t lineCap,
    const float* dashPattern, uint32_t dashCount, float dashOffset,
    const EngineTransform& transform)
{
    FlattenPath(startX, startY, commands, commandLength, transform);
    bool ok = ExpandStroke(brush, strokeWidth, lineJoin, miterLimit, lineCap, closed);
    if (ok) encodedPathCount_++;
    return ok;
}

bool ImpellerVulkanEngine::EncodeFillPolygon(
    const float* points, uint32_t pointCount,
    const EngineBrushData& brush,
    FillRule fillRule,
    const EngineTransform& transform)
{
    if (pointCount < 3) return false;

    flatPoints_.clear();
    flatPoints_.reserve(pointCount * 2);
    for (uint32_t i = 0; i < pointCount; ++i) {
        float x = points[i * 2], y = points[i * 2 + 1];
        TransformPoint(x, y, transform);
        flatPoints_.push_back(x);
        flatPoints_.push_back(y);
    }

    bool ok = TessellateCurrentPath(brush, fillRule);
    if (ok) encodedPathCount_++;
    return ok;
}

bool ImpellerVulkanEngine::EncodeFillEllipse(
    float cx, float cy, float rx, float ry,
    const EngineBrushData& brush,
    const EngineTransform& transform)
{
    float maxRadius = std::max(rx, ry);
    constexpr float kCircleTolerance = 0.1f;
    uint32_t quadDivisions = 1;
    if (maxRadius > 0.0f) {
        float k = kCircleTolerance / maxRadius;
        if (k < 1.0f) {
            quadDivisions = std::max(1u, std::min((uint32_t)std::ceil(
                (float)M_PI / 4.0f / std::acos(1.0f - k)), 64u));
        }
    }
    uint32_t totalVerts = quadDivisions * 4;

    flatPoints_.clear();
    flatPoints_.reserve(totalVerts * 2);
    for (uint32_t i = 0; i < totalVerts; ++i) {
        float angle = 2.0f * (float)M_PI * (float)i / (float)totalVerts;
        float x = cx + rx * std::cos(angle);
        float y = cy + ry * std::sin(angle);
        TransformPoint(x, y, transform);
        flatPoints_.push_back(x);
        flatPoints_.push_back(y);
    }

    bool ok = TessellateCurrentPath(brush, FillRule::NonZero);
    if (ok) encodedPathCount_++;
    return ok;
}

// ============================================================================
// GPU Execution
// ============================================================================

bool ImpellerVulkanEngine::Execute(void* commandList, void* renderTarget, uint32_t width, uint32_t height) {
    if (batches_.empty()) return true;

    auto cmdBuf = static_cast<VkCommandBuffer>(commandList);

    if (!EnsureOutputImage(width, height)) return false;

    // Calculate totals
    size_t totalVertBytes = 0, totalIdxBytes = 0;
    for (auto& batch : batches_) {
        totalVertBytes += batch.vertices.size() * sizeof(VkImpellerVertex);
        totalIdxBytes += batch.indices.size() * sizeof(uint32_t);
    }

    // Ensure buffers (host-visible for simplicity; production would use staging)
    auto ensureBuffer = [&](VkBuffer& buf, VkDeviceMemory& mem, size_t& currentSize,
                            size_t needed, VkBufferUsageFlags usage) {
        if (currentSize >= needed) return true;
        if (buf) { vkDestroyBuffer(device_, buf, nullptr); buf = VK_NULL_HANDLE; }
        if (mem) { vkFreeMemory(device_, mem, nullptr); mem = VK_NULL_HANDLE; }

        VkBufferCreateInfo bufInfo = {};
        bufInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
        bufInfo.size = needed;
        bufInfo.usage = usage;
        if (vkCreateBuffer(device_, &bufInfo, nullptr, &buf) != VK_SUCCESS) return false;

        VkMemoryRequirements reqs;
        vkGetBufferMemoryRequirements(device_, buf, &reqs);

        VkMemoryAllocateInfo alloc = {};
        alloc.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        alloc.allocationSize = reqs.size;
        alloc.memoryTypeIndex = FindMemoryType(reqs.memoryTypeBits,
            VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
        if (alloc.memoryTypeIndex == UINT32_MAX) return false;
        if (vkAllocateMemory(device_, &alloc, nullptr, &mem) != VK_SUCCESS) return false;
        vkBindBufferMemory(device_, buf, mem, 0);
        currentSize = needed;
        return true;
    };

    if (!ensureBuffer(vertexBuffer_, vertexMemory_, vertexBufferSize_,
                      totalVertBytes, VK_BUFFER_USAGE_VERTEX_BUFFER_BIT)) return false;
    if (!ensureBuffer(indexBuffer_, indexMemory_, indexBufferSize_,
                      totalIdxBytes, VK_BUFFER_USAGE_INDEX_BUFFER_BIT)) return false;

    // Upload vertex data
    void* mapped = nullptr;
    vkMapMemory(device_, vertexMemory_, 0, totalVertBytes, 0, &mapped);
    size_t offset = 0;
    for (auto& batch : batches_) {
        size_t bytes = batch.vertices.size() * sizeof(VkImpellerVertex);
        memcpy((uint8_t*)mapped + offset, batch.vertices.data(), bytes);
        offset += bytes;
    }
    vkUnmapMemory(device_, vertexMemory_);

    // Upload index data
    vkMapMemory(device_, indexMemory_, 0, totalIdxBytes, 0, &mapped);
    offset = 0;
    for (auto& batch : batches_) {
        size_t bytes = batch.indices.size() * sizeof(uint32_t);
        memcpy((uint8_t*)mapped + offset, batch.indices.data(), bytes);
        offset += bytes;
    }
    vkUnmapMemory(device_, indexMemory_);

    // Begin render pass
    VkRenderPassBeginInfo rpBegin = {};
    rpBegin.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
    rpBegin.renderPass = renderPass_;
    rpBegin.framebuffer = framebuffer_;
    rpBegin.renderArea = { { 0, 0 }, { width, height } };

    VkClearValue clearValue = {};
    clearValue.color = { { 0, 0, 0, 0 } }; // transparent
    rpBegin.clearValueCount = 1;
    rpBegin.pClearValues = &clearValue;

    vkCmdBeginRenderPass(cmdBuf, &rpBegin, VK_SUBPASS_CONTENTS_INLINE);

    if (solidFillPipeline_) {
        vkCmdBindPipeline(cmdBuf, VK_PIPELINE_BIND_POINT_GRAPHICS, solidFillPipeline_);

        // Set viewport
        VkViewport viewport = {};
        viewport.width = (float)width;
        viewport.height = (float)height;
        viewport.maxDepth = 1.0f;
        vkCmdSetViewport(cmdBuf, 0, 1, &viewport);

        // Set scissor
        VkRect2D scissor = {};
        if (hasScissor_) {
            scissor.offset = { (int32_t)scissorLeft_, (int32_t)scissorTop_ };
            scissor.extent = { (uint32_t)(scissorRight_ - scissorLeft_),
                               (uint32_t)(scissorBottom_ - scissorTop_) };
        } else {
            scissor.extent = { width, height };
        }
        vkCmdSetScissor(cmdBuf, 0, 1, &scissor);

        // Push MVP matrix
        float mvp[16] = {
            2.0f / width,  0,               0, 0,
            0,            -2.0f / height,    0, 0,
            0,             0,               1, 0,
            -1.0f,         1.0f,            0, 1
        };
        vkCmdPushConstants(cmdBuf, pipelineLayout_, VK_SHADER_STAGE_VERTEX_BIT, 0, 64, mvp);

        // Draw all batches
        VkDeviceSize vbOffset = 0, ibOffset = 0;
        for (auto& batch : batches_) {
            VkDeviceSize vbOff = vbOffset;
            vkCmdBindVertexBuffers(cmdBuf, 0, 1, &vertexBuffer_, &vbOff);
            vkCmdBindIndexBuffer(cmdBuf, indexBuffer_, ibOffset, VK_INDEX_TYPE_UINT32);

            vkCmdDrawIndexed(cmdBuf, (uint32_t)batch.indices.size(), 1, 0, 0, 0);

            vbOffset += batch.vertices.size() * sizeof(VkImpellerVertex);
            ibOffset += batch.indices.size() * sizeof(uint32_t);
        }
    }

    vkCmdEndRenderPass(cmdBuf);

    return true;
}

bool ImpellerVulkanEngine::HasPendingWork() const {
    return !batches_.empty();
}

uint32_t ImpellerVulkanEngine::GetEncodedPathCount() const {
    return encodedPathCount_;
}

} // namespace jalium
