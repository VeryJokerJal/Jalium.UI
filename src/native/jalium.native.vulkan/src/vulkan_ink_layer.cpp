#include "vulkan_ink_layer.h"

#include <cstdio>
#include <cstring>

#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#define INK_LOG(fmt, ...) do { char _b[512]; snprintf(_b, sizeof(_b), "[Jalium Vulkan ink] " fmt "\n", ##__VA_ARGS__); OutputDebugStringA(_b); } while(0)
#else
#define INK_LOG(fmt, ...) fprintf(stderr, "[Jalium Vulkan ink] " fmt "\n", ##__VA_ARGS__)
#endif

namespace jalium {

// ───────────────────────────────────────────────────────────────────────────
// Device function table (dynamic dispatch — the backend never link-loads vkXxx)
// ───────────────────────────────────────────────────────────────────────────
struct VkInkFunctions {
    PFN_vkGetPhysicalDeviceMemoryProperties getPhysicalDeviceMemoryProperties = nullptr;

    PFN_vkCreateImage                createImage                = nullptr;
    PFN_vkDestroyImage               destroyImage               = nullptr;
    PFN_vkGetImageMemoryRequirements getImageMemoryRequirements = nullptr;
    PFN_vkBindImageMemory            bindImageMemory            = nullptr;
    PFN_vkCreateImageView            createImageView            = nullptr;
    PFN_vkDestroyImageView           destroyImageView           = nullptr;

    PFN_vkCreateBuffer                createBuffer                = nullptr;
    PFN_vkDestroyBuffer               destroyBuffer               = nullptr;
    PFN_vkGetBufferMemoryRequirements getBufferMemoryRequirements = nullptr;
    PFN_vkBindBufferMemory            bindBufferMemory            = nullptr;

    PFN_vkAllocateMemory  allocateMemory = nullptr;
    PFN_vkFreeMemory      freeMemory     = nullptr;
    PFN_vkMapMemory       mapMemory      = nullptr;
    PFN_vkUnmapMemory     unmapMemory    = nullptr;

    PFN_vkCreateFramebuffer  createFramebuffer  = nullptr;
    PFN_vkDestroyFramebuffer destroyFramebuffer = nullptr;
    PFN_vkCreateRenderPass   createRenderPass   = nullptr;
    PFN_vkDestroyRenderPass  destroyRenderPass  = nullptr;

    PFN_vkCreateShaderModule  createShaderModule  = nullptr;
    PFN_vkDestroyShaderModule destroyShaderModule = nullptr;

    PFN_vkCreateDescriptorSetLayout  createDescriptorSetLayout  = nullptr;
    PFN_vkDestroyDescriptorSetLayout destroyDescriptorSetLayout = nullptr;
    PFN_vkCreatePipelineLayout       createPipelineLayout       = nullptr;
    PFN_vkDestroyPipelineLayout      destroyPipelineLayout      = nullptr;
    PFN_vkCreateGraphicsPipelines    createGraphicsPipelines    = nullptr;
    PFN_vkDestroyPipeline            destroyPipeline            = nullptr;

    PFN_vkCreateDescriptorPool  createDescriptorPool  = nullptr;
    PFN_vkDestroyDescriptorPool destroyDescriptorPool = nullptr;
    PFN_vkAllocateDescriptorSets allocateDescriptorSets = nullptr;
    PFN_vkUpdateDescriptorSets   updateDescriptorSets   = nullptr;

    PFN_vkCreateCommandPool     createCommandPool     = nullptr;
    PFN_vkDestroyCommandPool    destroyCommandPool    = nullptr;
    PFN_vkAllocateCommandBuffers allocateCommandBuffers = nullptr;

    PFN_vkCreateFence    createFence    = nullptr;
    PFN_vkDestroyFence   destroyFence   = nullptr;
    PFN_vkWaitForFences  waitForFences  = nullptr;
    PFN_vkResetFences    resetFences    = nullptr;
    PFN_vkDeviceWaitIdle deviceWaitIdle = nullptr;

    PFN_vkResetCommandBuffer resetCommandBuffer = nullptr;
    PFN_vkBeginCommandBuffer beginCommandBuffer = nullptr;
    PFN_vkEndCommandBuffer   endCommandBuffer   = nullptr;

    PFN_vkCmdPipelineBarrier  cmdPipelineBarrier  = nullptr;
    PFN_vkCmdClearColorImage  cmdClearColorImage  = nullptr;
    PFN_vkCmdCopyImageToBuffer cmdCopyImageToBuffer = nullptr;
    PFN_vkCmdBeginRenderPass  cmdBeginRenderPass  = nullptr;
    PFN_vkCmdEndRenderPass    cmdEndRenderPass    = nullptr;
    PFN_vkCmdBindPipeline     cmdBindPipeline     = nullptr;
    PFN_vkCmdBindDescriptorSets cmdBindDescriptorSets = nullptr;
    PFN_vkCmdSetViewport      cmdSetViewport      = nullptr;
    PFN_vkCmdSetScissor       cmdSetScissor       = nullptr;
    PFN_vkCmdDraw             cmdDraw             = nullptr;
    PFN_vkQueueSubmit         queueSubmit         = nullptr;

    VkPhysicalDeviceMemoryProperties memProps {};

    bool Load(const VulkanDeviceContext& ctx);
    uint32_t FindMemoryType(uint32_t typeBits, VkMemoryPropertyFlags flags) const;
};

bool VkInkFunctions::Load(const VulkanDeviceContext& ctx) {
    if (!ctx.getInstanceProcAddr || !ctx.getDeviceProcAddr ||
        ctx.device == VK_NULL_HANDLE || ctx.instance == VK_NULL_HANDLE) {
        return false;
    }
    auto gip = ctx.getInstanceProcAddr;
    auto gdp = ctx.getDeviceProcAddr;
    VkInstance inst = ctx.instance;
    VkDevice   dev  = ctx.device;

    getPhysicalDeviceMemoryProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceMemoryProperties>(
        gip(inst, "vkGetPhysicalDeviceMemoryProperties"));

#define LOAD_DEV(name, fn) fn = reinterpret_cast<PFN_##name>(gdp(dev, #name))
    LOAD_DEV(vkCreateImage, createImage);
    LOAD_DEV(vkDestroyImage, destroyImage);
    LOAD_DEV(vkGetImageMemoryRequirements, getImageMemoryRequirements);
    LOAD_DEV(vkBindImageMemory, bindImageMemory);
    LOAD_DEV(vkCreateImageView, createImageView);
    LOAD_DEV(vkDestroyImageView, destroyImageView);
    LOAD_DEV(vkCreateBuffer, createBuffer);
    LOAD_DEV(vkDestroyBuffer, destroyBuffer);
    LOAD_DEV(vkGetBufferMemoryRequirements, getBufferMemoryRequirements);
    LOAD_DEV(vkBindBufferMemory, bindBufferMemory);
    LOAD_DEV(vkAllocateMemory, allocateMemory);
    LOAD_DEV(vkFreeMemory, freeMemory);
    LOAD_DEV(vkMapMemory, mapMemory);
    LOAD_DEV(vkUnmapMemory, unmapMemory);
    LOAD_DEV(vkCreateFramebuffer, createFramebuffer);
    LOAD_DEV(vkDestroyFramebuffer, destroyFramebuffer);
    LOAD_DEV(vkCreateRenderPass, createRenderPass);
    LOAD_DEV(vkDestroyRenderPass, destroyRenderPass);
    LOAD_DEV(vkCreateShaderModule, createShaderModule);
    LOAD_DEV(vkDestroyShaderModule, destroyShaderModule);
    LOAD_DEV(vkCreateDescriptorSetLayout, createDescriptorSetLayout);
    LOAD_DEV(vkDestroyDescriptorSetLayout, destroyDescriptorSetLayout);
    LOAD_DEV(vkCreatePipelineLayout, createPipelineLayout);
    LOAD_DEV(vkDestroyPipelineLayout, destroyPipelineLayout);
    LOAD_DEV(vkCreateGraphicsPipelines, createGraphicsPipelines);
    LOAD_DEV(vkDestroyPipeline, destroyPipeline);
    LOAD_DEV(vkCreateDescriptorPool, createDescriptorPool);
    LOAD_DEV(vkDestroyDescriptorPool, destroyDescriptorPool);
    LOAD_DEV(vkAllocateDescriptorSets, allocateDescriptorSets);
    LOAD_DEV(vkUpdateDescriptorSets, updateDescriptorSets);
    LOAD_DEV(vkCreateCommandPool, createCommandPool);
    LOAD_DEV(vkDestroyCommandPool, destroyCommandPool);
    LOAD_DEV(vkAllocateCommandBuffers, allocateCommandBuffers);
    LOAD_DEV(vkCreateFence, createFence);
    LOAD_DEV(vkDestroyFence, destroyFence);
    LOAD_DEV(vkWaitForFences, waitForFences);
    LOAD_DEV(vkResetFences, resetFences);
    LOAD_DEV(vkDeviceWaitIdle, deviceWaitIdle);
    LOAD_DEV(vkResetCommandBuffer, resetCommandBuffer);
    LOAD_DEV(vkBeginCommandBuffer, beginCommandBuffer);
    LOAD_DEV(vkEndCommandBuffer, endCommandBuffer);
    LOAD_DEV(vkCmdPipelineBarrier, cmdPipelineBarrier);
    LOAD_DEV(vkCmdClearColorImage, cmdClearColorImage);
    LOAD_DEV(vkCmdCopyImageToBuffer, cmdCopyImageToBuffer);
    LOAD_DEV(vkCmdBeginRenderPass, cmdBeginRenderPass);
    LOAD_DEV(vkCmdEndRenderPass, cmdEndRenderPass);
    LOAD_DEV(vkCmdBindPipeline, cmdBindPipeline);
    LOAD_DEV(vkCmdBindDescriptorSets, cmdBindDescriptorSets);
    LOAD_DEV(vkCmdSetViewport, cmdSetViewport);
    LOAD_DEV(vkCmdSetScissor, cmdSetScissor);
    LOAD_DEV(vkCmdDraw, cmdDraw);
    LOAD_DEV(vkQueueSubmit, queueSubmit);
#undef LOAD_DEV

    if (getPhysicalDeviceMemoryProperties && ctx.physicalDevice != VK_NULL_HANDLE) {
        getPhysicalDeviceMemoryProperties(ctx.physicalDevice, &memProps);
    }

    // Validate the must-have entry points are all present.
    return createImage && getImageMemoryRequirements && bindImageMemory &&
           createImageView && createBuffer && getBufferMemoryRequirements &&
           bindBufferMemory && allocateMemory && freeMemory && mapMemory &&
           createFramebuffer && createRenderPass && createShaderModule &&
           createDescriptorSetLayout && createPipelineLayout && createGraphicsPipelines &&
           createDescriptorPool && allocateDescriptorSets && updateDescriptorSets &&
           createCommandPool && allocateCommandBuffers && createFence && waitForFences &&
           resetFences && beginCommandBuffer && endCommandBuffer && cmdPipelineBarrier &&
           cmdClearColorImage && cmdBeginRenderPass && cmdEndRenderPass && cmdBindPipeline &&
           cmdBindDescriptorSets && cmdSetViewport && cmdSetScissor && cmdDraw && queueSubmit &&
           getPhysicalDeviceMemoryProperties && memProps.memoryTypeCount > 0;
}

uint32_t VkInkFunctions::FindMemoryType(uint32_t typeBits, VkMemoryPropertyFlags flags) const {
    for (uint32_t i = 0; i < memProps.memoryTypeCount; ++i) {
        if ((typeBits & (1u << i)) &&
            (memProps.memoryTypes[i].propertyFlags & flags) == flags) {
            return i;
        }
    }
    return UINT32_MAX;
}

namespace {

constexpr VkFormat kInkFormat = VK_FORMAT_R8G8B8A8_UNORM;

// ───────────────────────────────────────────────────────────────────────────
// Shared brush HLSL. The PS preamble + entry are byte-identical to the D3D12
// backend (d3d12_brush_shader.cpp) and the managed BrushShaderPreamble.hlsl —
// the user BrushMain body is concatenated between them. Only the vertex shader
// differs from D3D12: it maps the fullscreen triangle's NDC into Vulkan
// framebuffer pixel space (top-left origin, +y down) WITHOUT the D3D12 y-flip,
// because Vulkan's framebuffer Y already increases downward. The stroke
// coordinate frame the managed side fills (top-left origin) therefore matches
// pxPos exactly, just like on D3D12.
// ───────────────────────────────────────────────────────────────────────────
constexpr const char* kBrushVs = R"__HLSL__(
cbuffer BrushConstants : register(b0)
{
    float4 StrokeColor;
    float  StrokeWidth;
    float  StrokeHeight;
    float  TimeSeconds;
    uint   RandomSeed;
    float2 BBoxMin;
    float2 BBoxMax;
    uint   PointCount;
    uint   TaperMode;
    uint   IgnorePressure;
    uint   FitToCurve;
    float2 ViewportSize;
    float2 Pad;
};

struct PsIn
{
    float4 svPos : SV_Position;
    float2 pxPos : TEXCOORD0;
};

PsIn main(uint vid : SV_VertexID)
{
    float2 ndc = float2(
        (vid == 1) ?  3.0f : -1.0f,
        (vid == 2) ?  3.0f : -1.0f);

    PsIn o;
    o.svPos = float4(ndc, 0.0f, 1.0f);
    // Vulkan framebuffer Y increases downward: clip y=-1 → fb y=0 (top).
    o.pxPos = float2((ndc.x + 1.0f) * 0.5f * ViewportSize.x,
                     (ndc.y + 1.0f) * 0.5f * ViewportSize.y);
    return o;
}
)__HLSL__";

constexpr const char* kBrushPsPreamble = R"__HLSL__(
cbuffer BrushConstants : register(b0)
{
    float4 StrokeColor;
    float  StrokeWidth;
    float  StrokeHeight;
    float  TimeSeconds;
    uint   RandomSeed;
    float2 BBoxMin;
    float2 BBoxMax;
    uint   PointCount;
    uint   TaperMode;
    uint   IgnorePressure;
    uint   FitToCurve;
    float2 ViewportSize;
    float2 Pad;
};

struct StrokePoint
{
    float x;
    float y;
    float pressure;
    float pad;
};

StructuredBuffer<StrokePoint> StrokePoints : register(t0);

float Hash21(float2 p, uint extra)
{
    uint3 q = uint3(asuint(p.x), asuint(p.y), RandomSeed ^ extra);
    q = q * uint3(374761393u, 668265263u, 2246822519u);
    q = (q.x ^ q.y ^ q.z) * uint3(0x85ebca6bu, 0xc2b2ae35u, 0x27d4eb2fu);
    uint h = q.x ^ q.y ^ q.z;
    return (h & 0x00FFFFFFu) / float(0x01000000u);
}

float2 SdfSegment(float2 px, float2 a, float2 b)
{
    float2 pa = px - a;
    float2 ba = b - a;
    float lenSq = dot(ba, ba);
    float t = (lenSq > 1e-6) ? saturate(dot(pa, ba) / lenSq) : 0;
    return float2(length(pa - ba * t), t);
}

float TaperScale(float t)
{
    if (TaperMode == 1) return 1.0 - (1.0 - t) * (1.0 - t);
    if (TaperMode == 2) return 1.0 - t * t;
    return 1.0;
}

float2 SdfPolyline(float2 px)
{
    float totalLen = 0;
    [loop]
    for (uint i = 0; i + 1 < PointCount; ++i)
    {
        StrokePoint pa = StrokePoints[i];
        StrokePoint pb = StrokePoints[i + 1];
        totalLen += length(float2(pb.x - pa.x, pb.y - pa.y));
    }
    float invLen = (totalLen > 1e-6) ? (1.0 / totalLen) : 0.0;

    float bestDist = 1e20;
    float bestArc  = 0;
    float bestCov  = -1;
    float accum    = 0;

    [loop]
    for (uint j = 0; j + 1 < PointCount; ++j)
    {
        StrokePoint pa = StrokePoints[j];
        StrokePoint pb = StrokePoints[j + 1];
        float2 a   = float2(pa.x, pa.y);
        float2 b   = float2(pb.x, pb.y);
        float  len = length(b - a);
        float2 r   = SdfSegment(px, a, b);
        float  arc = saturate((accum + r.y * len) * invLen);

        float halfWEst = StrokeWidth * 0.5;
        if (IgnorePressure == 0)
        {
            float p = lerp(pa.pressure, pb.pressure, r.y);
            halfWEst *= p;
        }
        halfWEst *= TaperScale(arc);
        float covEst = saturate(halfWEst - r.x + 0.5);

        if (covEst > bestCov)
        {
            bestCov = covEst;
            bestArc = arc;
        }
        bestDist = min(bestDist, r.x);
        accum   += len;
    }

    return float2(bestDist, bestArc);
}

float HalfWidthAt(float t)
{
    float radius = StrokeWidth * 0.5;
    if (IgnorePressure == 0 && PointCount >= 2)
    {
        float idxF = saturate(t) * (PointCount - 1);
        uint  idx0 = (uint)floor(idxF);
        uint  idx1 = min(idx0 + 1, PointCount - 1);
        float frac = idxF - idx0;
        float p    = lerp(StrokePoints[idx0].pressure, StrokePoints[idx1].pressure, frac);
        radius *= p;
    }
    radius *= TaperScale(t);
    return max(radius, 0.0);
}

float StrokeCoverage(float sdf, float halfWidth)
{
    return saturate(halfWidth - sdf + 0.5);
}

struct PsIn
{
    float4 svPos : SV_Position;
    float2 pxPos : TEXCOORD0;
};
)__HLSL__";

constexpr const char* kBrushPsEntry = R"__HLSL__(
float4 BrushPsMain(PsIn input) : SV_Target
{
    return BrushMain(input.pxPos);
}
)__HLSL__";

VkShaderModule MakeShaderModule(const VkInkFunctions& fns, VkDevice device,
                                const std::vector<uint32_t>& spirv) {
    if (spirv.empty()) return VK_NULL_HANDLE;
    VkShaderModuleCreateInfo info{};
    info.sType    = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
    info.codeSize = spirv.size() * sizeof(uint32_t);
    info.pCode    = spirv.data();
    VkShaderModule mod = VK_NULL_HANDLE;
    if (fns.createShaderModule(device, &info, nullptr, &mod) != VK_SUCCESS) return VK_NULL_HANDLE;
    return mod;
}

void ConfigureBlend(VkPipelineColorBlendAttachmentState& a, VulkanBrushBlendMode mode) {
    a.colorWriteMask = VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT |
                       VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;
    a.blendEnable = VK_TRUE;
    a.colorBlendOp = VK_BLEND_OP_ADD;
    a.alphaBlendOp = VK_BLEND_OP_ADD;
    switch (mode) {
        case VulkanBrushBlendMode::SourceOver:
            a.srcColorBlendFactor = VK_BLEND_FACTOR_ONE;
            a.dstColorBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
            a.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
            a.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
            break;
        case VulkanBrushBlendMode::Additive:
            a.srcColorBlendFactor = VK_BLEND_FACTOR_ONE;
            a.dstColorBlendFactor = VK_BLEND_FACTOR_ONE;
            a.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
            a.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
            break;
        case VulkanBrushBlendMode::Erase:
            a.srcColorBlendFactor = VK_BLEND_FACTOR_ZERO;
            a.dstColorBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
            a.srcAlphaBlendFactor = VK_BLEND_FACTOR_ZERO;
            a.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
            break;
    }
}

} // namespace

// ───────────────────────────────────────────────────────────────────────────
// VulkanBrushPipeline
// ───────────────────────────────────────────────────────────────────────────
VulkanBrushPipeline::VulkanBrushPipeline(const VulkanDeviceContext& ctx)
    : ctx_(ctx), fns_(std::make_unique<VkInkFunctions>()) {}

VulkanBrushPipeline::~VulkanBrushPipeline() {
    if (!fns_) return;
    VkDevice device = ctx_.device;
    if (device == VK_NULL_HANDLE) return;
    if (fns_->deviceWaitIdle) fns_->deviceWaitIdle(device);
    if (inkRenderPass_ != VK_NULL_HANDLE) fns_->destroyRenderPass(device, inkRenderPass_, nullptr);
    if (pipelineLayout_ != VK_NULL_HANDLE) fns_->destroyPipelineLayout(device, pipelineLayout_, nullptr);
    if (descriptorSetLayout_ != VK_NULL_HANDLE) fns_->destroyDescriptorSetLayout(device, descriptorSetLayout_, nullptr);
    if (vertexModule_ != VK_NULL_HANDLE) fns_->destroyShaderModule(device, vertexModule_, nullptr);
}

bool VulkanBrushPipeline::Initialize() {
    if (ready_) return true;
    if (attempted_) return false;
    attempted_ = true;

    if (!ctx_.valid || ctx_.device == VK_NULL_HANDLE) return false;
    if (!fns_->Load(ctx_)) {
        INK_LOG("device function table load failed");
        return false;
    }
    if (!compiler_.Available()) {
        INK_LOG("DXC unavailable — GPU brush pipeline disabled (CPU fallback)");
        return false;
    }

    VkDevice device = ctx_.device;

    // Shared fullscreen-triangle VS.
    std::string err;
    vertexSpirv_ = compiler_.Compile(kBrushVs, "main", "vs_6_0", err);
    vertexModule_ = MakeShaderModule(*fns_, device, vertexSpirv_);
    if (vertexModule_ == VK_NULL_HANDLE) {
        INK_LOG("brush VS compile/create failed: %s", err.c_str());
        return false;
    }

    // Descriptor-set layout: b0 (UBO, VS+FS), b1 (UBO, FS), t0 (SSBO, FS).
    // Bindings follow the VulkanShaderCompiler register→binding shift scheme.
    VkDescriptorSetLayoutBinding bindings[3]{};
    bindings[0].binding = VulkanShaderCompiler::kBShift + 0;
    bindings[0].descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
    bindings[0].descriptorCount = 1;
    bindings[0].stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT;
    bindings[1].binding = VulkanShaderCompiler::kBShift + 1;
    bindings[1].descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
    bindings[1].descriptorCount = 1;
    bindings[1].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    bindings[2].binding = VulkanShaderCompiler::kTShift + 0;
    bindings[2].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
    bindings[2].descriptorCount = 1;
    bindings[2].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;

    VkDescriptorSetLayoutCreateInfo dslInfo{};
    dslInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
    dslInfo.bindingCount = 3;
    dslInfo.pBindings = bindings;
    if (fns_->createDescriptorSetLayout(device, &dslInfo, nullptr, &descriptorSetLayout_) != VK_SUCCESS) {
        INK_LOG("descriptor set layout failed");
        return false;
    }

    VkPipelineLayoutCreateInfo plInfo{};
    plInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    plInfo.setLayoutCount = 1;
    plInfo.pSetLayouts = &descriptorSetLayout_;
    if (fns_->createPipelineLayout(device, &plInfo, nullptr, &pipelineLayout_) != VK_SUCCESS) {
        INK_LOG("pipeline layout failed");
        return false;
    }

    // Ink render pass. Layout transitions are managed explicitly via barriers,
    // so the attachment stays COLOR_ATTACHMENT_OPTIMAL across the pass; LOAD
    // preserves previously-committed strokes.
    VkAttachmentDescription color{};
    color.format = kInkFormat;
    color.samples = VK_SAMPLE_COUNT_1_BIT;
    color.loadOp = VK_ATTACHMENT_LOAD_OP_LOAD;
    color.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
    color.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
    color.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    color.initialLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    color.finalLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

    VkAttachmentReference colorRef{};
    colorRef.attachment = 0;
    colorRef.layout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

    VkSubpassDescription subpass{};
    subpass.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
    subpass.colorAttachmentCount = 1;
    subpass.pColorAttachments = &colorRef;

    VkSubpassDependency deps[2]{};
    deps[0].srcSubpass = VK_SUBPASS_EXTERNAL;
    deps[0].dstSubpass = 0;
    deps[0].srcStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
    deps[0].dstStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
    deps[0].srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
    deps[0].dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_READ_BIT | VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
    deps[1].srcSubpass = 0;
    deps[1].dstSubpass = VK_SUBPASS_EXTERNAL;
    deps[1].srcStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
    deps[1].dstStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
    deps[1].srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
    deps[1].dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_READ_BIT | VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;

    VkRenderPassCreateInfo rpInfo{};
    rpInfo.sType = VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO;
    rpInfo.attachmentCount = 1;
    rpInfo.pAttachments = &color;
    rpInfo.subpassCount = 1;
    rpInfo.pSubpasses = &subpass;
    rpInfo.dependencyCount = 2;
    rpInfo.pDependencies = deps;
    if (fns_->createRenderPass(device, &rpInfo, nullptr, &inkRenderPass_) != VK_SUCCESS) {
        INK_LOG("ink render pass failed");
        return false;
    }

    ready_ = true;
    return true;
}

std::unique_ptr<VulkanBrushShader> VulkanBrushPipeline::CreateBrushShader(
    const char* shaderKey, const char* brushMainHlsl, VulkanBrushBlendMode blendMode) {
    if (!Initialize() || !brushMainHlsl) return nullptr;
    VkDevice device = ctx_.device;

    std::string psSource;
    psSource.reserve(std::strlen(kBrushPsPreamble) + std::strlen(brushMainHlsl) +
                     std::strlen(kBrushPsEntry) + 4);
    psSource.append(kBrushPsPreamble);
    psSource.append("\n");
    psSource.append(brushMainHlsl);
    psSource.append("\n");
    psSource.append(kBrushPsEntry);

    std::string err;
    std::vector<uint32_t> psSpirv = compiler_.Compile(psSource, "BrushPsMain", "ps_6_0", err);
    if (psSpirv.empty()) {
        INK_LOG("brush PS '%s' compile failed: %s", shaderKey ? shaderKey : "anon", err.c_str());
        return nullptr;
    }
    VkShaderModule fragModule = MakeShaderModule(*fns_, device, psSpirv);
    if (fragModule == VK_NULL_HANDLE) return nullptr;

    VkPipelineShaderStageCreateInfo stages[2]{};
    stages[0].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    stages[0].stage = VK_SHADER_STAGE_VERTEX_BIT;
    stages[0].module = vertexModule_;
    stages[0].pName = "main";
    stages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    stages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
    stages[1].module = fragModule;
    stages[1].pName = "BrushPsMain";

    VkPipelineVertexInputStateCreateInfo vi{};
    vi.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;

    VkPipelineInputAssemblyStateCreateInfo ia{};
    ia.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
    ia.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;

    VkPipelineViewportStateCreateInfo vp{};
    vp.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
    vp.viewportCount = 1;
    vp.scissorCount = 1;

    VkPipelineRasterizationStateCreateInfo rs{};
    rs.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
    rs.polygonMode = VK_POLYGON_MODE_FILL;
    rs.cullMode = VK_CULL_MODE_NONE;
    rs.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
    rs.lineWidth = 1.0f;

    VkPipelineMultisampleStateCreateInfo ms{};
    ms.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
    ms.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

    VkPipelineColorBlendAttachmentState blendAtt{};
    ConfigureBlend(blendAtt, blendMode);
    VkPipelineColorBlendStateCreateInfo cb{};
    cb.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
    cb.attachmentCount = 1;
    cb.pAttachments = &blendAtt;

    VkDynamicState dyn[2] = { VK_DYNAMIC_STATE_VIEWPORT, VK_DYNAMIC_STATE_SCISSOR };
    VkPipelineDynamicStateCreateInfo dynInfo{};
    dynInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
    dynInfo.dynamicStateCount = 2;
    dynInfo.pDynamicStates = dyn;

    VkGraphicsPipelineCreateInfo pInfo{};
    pInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
    pInfo.stageCount = 2;
    pInfo.pStages = stages;
    pInfo.pVertexInputState = &vi;
    pInfo.pInputAssemblyState = &ia;
    pInfo.pViewportState = &vp;
    pInfo.pRasterizationState = &rs;
    pInfo.pMultisampleState = &ms;
    pInfo.pColorBlendState = &cb;
    pInfo.pDynamicState = &dynInfo;
    pInfo.layout = pipelineLayout_;
    pInfo.renderPass = inkRenderPass_;
    pInfo.subpass = 0;

    VkPipeline pipeline = VK_NULL_HANDLE;
    VkResult vr = fns_->createGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pInfo, nullptr, &pipeline);
    if (vr != VK_SUCCESS || pipeline == VK_NULL_HANDLE) {
        INK_LOG("brush pipeline create failed (%d)", (int)vr);
        fns_->destroyShaderModule(device, fragModule, nullptr);
        return nullptr;
    }

    return std::make_unique<VulkanBrushShader>(
        this, fragModule, pipeline, blendMode, shaderKey ? shaderKey : "");
}

// ───────────────────────────────────────────────────────────────────────────
// VulkanBrushShader
// ───────────────────────────────────────────────────────────────────────────
VulkanBrushShader::VulkanBrushShader(VulkanBrushPipeline* owner,
                                     VkShaderModule fragmentModule,
                                     VkPipeline pipeline,
                                     VulkanBrushBlendMode blendMode,
                                     std::string shaderKey)
    : owner_(owner), fragmentModule_(fragmentModule), pipeline_(pipeline),
      blendMode_(blendMode), shaderKey_(std::move(shaderKey)) {}

VulkanBrushShader::~VulkanBrushShader() {
    if (!owner_) return;
    const VkInkFunctions& fns = owner_->Fns();
    VkDevice device = owner_->Context().device;
    if (device == VK_NULL_HANDLE) return;
    if (fns.deviceWaitIdle) fns.deviceWaitIdle(device);
    if (pipeline_ != VK_NULL_HANDLE) fns.destroyPipeline(device, pipeline_, nullptr);
    if (fragmentModule_ != VK_NULL_HANDLE) fns.destroyShaderModule(device, fragmentModule_, nullptr);
}

// ───────────────────────────────────────────────────────────────────────────
// VulkanInkLayerBitmap
// ───────────────────────────────────────────────────────────────────────────
VulkanInkLayerBitmap::VulkanInkLayerBitmap(VulkanBrushPipeline* pipeline)
    : pipeline_(pipeline), fns_(pipeline ? &pipeline->Fns() : nullptr) {}

VulkanInkLayerBitmap::~VulkanInkLayerBitmap() {
    if (fns_ && fns_->deviceWaitIdle && pipeline_->Context().device != VK_NULL_HANDLE) {
        fns_->deviceWaitIdle(pipeline_->Context().device);
    }
    ReleaseResources();
    VkDevice device = pipeline_ ? pipeline_->Context().device : VK_NULL_HANDLE;
    if (fns_ && device != VK_NULL_HANDLE) {
        DestroyBuffer(cbBuffer_, cbMemory_, cbMapped_, cbCapacity_);
        DestroyBuffer(userBuffer_, userMemory_, userMapped_, userCapacity_);
        DestroyBuffer(strokeBuffer_, strokeMemory_, strokeMapped_, strokeCapacity_);
        DestroyBuffer(readbackBuffer_, readbackMemory_, readbackMapped_, readbackCapacity_);
        if (descriptorPool_ != VK_NULL_HANDLE) fns_->destroyDescriptorPool(device, descriptorPool_, nullptr);
        if (fence_ != VK_NULL_HANDLE) fns_->destroyFence(device, fence_, nullptr);
        if (cmdPool_ != VK_NULL_HANDLE) fns_->destroyCommandPool(device, cmdPool_, nullptr);
    }
}

bool VulkanInkLayerBitmap::Initialize(uint32_t width, uint32_t height) {
    if (!pipeline_ || !pipeline_->IsReady() || !fns_) return false;
    VkDevice device = pipeline_->Context().device;

    // One-time scratch: command pool + buffer, fence, descriptor pool + set.
    if (cmdPool_ == VK_NULL_HANDLE) {
        VkCommandPoolCreateInfo cpInfo{};
        cpInfo.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
        cpInfo.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
        cpInfo.queueFamilyIndex = pipeline_->Context().graphicsQueueFamily;
        if (fns_->createCommandPool(device, &cpInfo, nullptr, &cmdPool_) != VK_SUCCESS) return false;

        VkCommandBufferAllocateInfo cbAlloc{};
        cbAlloc.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
        cbAlloc.commandPool = cmdPool_;
        cbAlloc.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
        cbAlloc.commandBufferCount = 1;
        if (fns_->allocateCommandBuffers(device, &cbAlloc, &cmdBuffer_) != VK_SUCCESS) return false;

        VkFenceCreateInfo fenceInfo{};
        fenceInfo.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
        if (fns_->createFence(device, &fenceInfo, nullptr, &fence_) != VK_SUCCESS) return false;

        VkDescriptorPoolSize sizes[2]{};
        sizes[0].type = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
        sizes[0].descriptorCount = 2;
        sizes[1].type = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
        sizes[1].descriptorCount = 1;
        VkDescriptorPoolCreateInfo dpInfo{};
        dpInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
        dpInfo.maxSets = 1;
        dpInfo.poolSizeCount = 2;
        dpInfo.pPoolSizes = sizes;
        if (fns_->createDescriptorPool(device, &dpInfo, nullptr, &descriptorPool_) != VK_SUCCESS) return false;

        VkDescriptorSetLayout layout = pipeline_->DescriptorLayout();
        VkDescriptorSetAllocateInfo dsAlloc{};
        dsAlloc.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
        dsAlloc.descriptorPool = descriptorPool_;
        dsAlloc.descriptorSetCount = 1;
        dsAlloc.pSetLayouts = &layout;
        if (fns_->allocateDescriptorSets(device, &dsAlloc, &descriptorSet_) != VK_SUCCESS) return false;
    }

    if (!CreateResources(width, height)) return false;
    Clear(0.0f, 0.0f, 0.0f, 0.0f);
    return true;
}

bool VulkanInkLayerBitmap::Resize(uint32_t width, uint32_t height) {
    if (width == width_ && height == height_) return true;
    if (!fns_) return false;
    VkDevice device = pipeline_->Context().device;
    if (fns_->deviceWaitIdle) fns_->deviceWaitIdle(device);
    ReleaseResources();
    if (!CreateResources(width, height)) return false;
    Clear(0.0f, 0.0f, 0.0f, 0.0f);
    return true;
}

bool VulkanInkLayerBitmap::CreateResources(uint32_t width, uint32_t height) {
    if (width == 0 || height == 0) return false;
    VkDevice device = pipeline_->Context().device;
    width_ = width;
    height_ = height;

    VkImageCreateInfo imgInfo{};
    imgInfo.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    imgInfo.imageType = VK_IMAGE_TYPE_2D;
    imgInfo.format = kInkFormat;
    imgInfo.extent = { width, height, 1 };
    imgInfo.mipLevels = 1;
    imgInfo.arrayLayers = 1;
    imgInfo.samples = VK_SAMPLE_COUNT_1_BIT;
    imgInfo.tiling = VK_IMAGE_TILING_OPTIMAL;
    imgInfo.usage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_SAMPLED_BIT |
                    VK_IMAGE_USAGE_TRANSFER_DST_BIT;
    imgInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    imgInfo.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    if (fns_->createImage(device, &imgInfo, nullptr, &image_) != VK_SUCCESS) return false;
    imageLayout_ = VK_IMAGE_LAYOUT_UNDEFINED;

    VkMemoryRequirements req{};
    fns_->getImageMemoryRequirements(device, image_, &req);
    uint32_t typeIndex = fns_->FindMemoryType(req.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    if (typeIndex == UINT32_MAX) return false;
    VkMemoryAllocateInfo alloc{};
    alloc.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    alloc.allocationSize = req.size;
    alloc.memoryTypeIndex = typeIndex;
    if (fns_->allocateMemory(device, &alloc, nullptr, &imageMemory_) != VK_SUCCESS) return false;
    if (fns_->bindImageMemory(device, image_, imageMemory_, 0) != VK_SUCCESS) return false;

    VkImageViewCreateInfo viewInfo{};
    viewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    viewInfo.image = image_;
    viewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
    viewInfo.format = kInkFormat;
    viewInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    viewInfo.subresourceRange.levelCount = 1;
    viewInfo.subresourceRange.layerCount = 1;
    if (fns_->createImageView(device, &viewInfo, nullptr, &imageView_) != VK_SUCCESS) return false;

    VkFramebufferCreateInfo fbInfo{};
    fbInfo.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
    fbInfo.renderPass = pipeline_->InkRenderPass();
    fbInfo.attachmentCount = 1;
    fbInfo.pAttachments = &imageView_;
    fbInfo.width = width;
    fbInfo.height = height;
    fbInfo.layers = 1;
    if (fns_->createFramebuffer(device, &fbInfo, nullptr, &framebuffer_) != VK_SUCCESS) return false;

    return true;
}

void VulkanInkLayerBitmap::ReleaseResources() {
    if (!fns_) return;
    VkDevice device = pipeline_->Context().device;
    if (device == VK_NULL_HANDLE) return;
    if (framebuffer_ != VK_NULL_HANDLE) { fns_->destroyFramebuffer(device, framebuffer_, nullptr); framebuffer_ = VK_NULL_HANDLE; }
    if (imageView_ != VK_NULL_HANDLE) { fns_->destroyImageView(device, imageView_, nullptr); imageView_ = VK_NULL_HANDLE; }
    if (image_ != VK_NULL_HANDLE) { fns_->destroyImage(device, image_, nullptr); image_ = VK_NULL_HANDLE; }
    if (imageMemory_ != VK_NULL_HANDLE) { fns_->freeMemory(device, imageMemory_, nullptr); imageMemory_ = VK_NULL_HANDLE; }
    imageLayout_ = VK_IMAGE_LAYOUT_UNDEFINED;
}

void VulkanInkLayerBitmap::DestroyBuffer(VkBuffer& buffer, VkDeviceMemory& memory,
                                         void*& mapped, VkDeviceSize& capacity) {
    if (!fns_) return;
    VkDevice device = pipeline_->Context().device;
    if (mapped && memory != VK_NULL_HANDLE) { fns_->unmapMemory(device, memory); mapped = nullptr; }
    if (buffer != VK_NULL_HANDLE) { fns_->destroyBuffer(device, buffer, nullptr); buffer = VK_NULL_HANDLE; }
    if (memory != VK_NULL_HANDLE) { fns_->freeMemory(device, memory, nullptr); memory = VK_NULL_HANDLE; }
    capacity = 0;
}

bool VulkanInkLayerBitmap::EnsureBuffer(VkBuffer& buffer, VkDeviceMemory& memory, void*& mapped,
                                        VkDeviceSize& capacity, VkDeviceSize required,
                                        VkBufferUsageFlags usage) {
    if (required <= capacity && buffer != VK_NULL_HANDLE) return true;
    VkDevice device = pipeline_->Context().device;
    DestroyBuffer(buffer, memory, mapped, capacity);

    VkDeviceSize size = required < 256 ? 256 : required;
    VkBufferCreateInfo bInfo{};
    bInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    bInfo.size = size;
    bInfo.usage = usage;
    bInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    if (fns_->createBuffer(device, &bInfo, nullptr, &buffer) != VK_SUCCESS) return false;

    VkMemoryRequirements req{};
    fns_->getBufferMemoryRequirements(device, buffer, &req);
    uint32_t typeIndex = fns_->FindMemoryType(
        req.memoryTypeBits,
        VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
    if (typeIndex == UINT32_MAX) return false;
    VkMemoryAllocateInfo alloc{};
    alloc.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    alloc.allocationSize = req.size;
    alloc.memoryTypeIndex = typeIndex;
    if (fns_->allocateMemory(device, &alloc, nullptr, &memory) != VK_SUCCESS) return false;
    if (fns_->bindBufferMemory(device, buffer, memory, 0) != VK_SUCCESS) return false;
    if (fns_->mapMemory(device, memory, 0, VK_WHOLE_SIZE, 0, &mapped) != VK_SUCCESS) return false;
    capacity = size;
    return true;
}

bool VulkanInkLayerBitmap::BeginCommands() {
    VkCommandBufferBeginInfo begin{};
    begin.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    begin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    if (fns_->resetCommandBuffer) fns_->resetCommandBuffer(cmdBuffer_, 0);
    return fns_->beginCommandBuffer(cmdBuffer_, &begin) == VK_SUCCESS;
}

bool VulkanInkLayerBitmap::SubmitAndWait() {
    if (fns_->endCommandBuffer(cmdBuffer_) != VK_SUCCESS) return false;
    VkDevice device = pipeline_->Context().device;
    fns_->resetFences(device, 1, &fence_);
    VkSubmitInfo submit{};
    submit.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    submit.commandBufferCount = 1;
    submit.pCommandBuffers = &cmdBuffer_;
    if (fns_->queueSubmit(pipeline_->Context().graphicsQueue, 1, &submit, fence_) != VK_SUCCESS) return false;
    fns_->waitForFences(device, 1, &fence_, VK_TRUE, UINT64_MAX);
    return true;
}

void VulkanInkLayerBitmap::BarrierToLayout(VkImageLayout newLayout,
                                           VkPipelineStageFlags srcStage, VkAccessFlags srcAccess,
                                           VkPipelineStageFlags dstStage, VkAccessFlags dstAccess) {
    VkImageMemoryBarrier barrier{};
    barrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    barrier.oldLayout = imageLayout_;
    barrier.newLayout = newLayout;
    barrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    barrier.image = image_;
    barrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    barrier.subresourceRange.levelCount = 1;
    barrier.subresourceRange.layerCount = 1;
    barrier.srcAccessMask = srcAccess;
    barrier.dstAccessMask = dstAccess;
    fns_->cmdPipelineBarrier(cmdBuffer_, srcStage, dstStage, 0, 0, nullptr, 0, nullptr, 1, &barrier);
    imageLayout_ = newLayout;
}

void VulkanInkLayerBitmap::Clear(float r, float g, float b, float a) {
    if (!fns_ || image_ == VK_NULL_HANDLE) return;
    if (!BeginCommands()) return;

    BarrierToLayout(VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                    VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, 0,
                    VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT);

    VkClearColorValue clear{};
    clear.float32[0] = r; clear.float32[1] = g; clear.float32[2] = b; clear.float32[3] = a;
    VkImageSubresourceRange range{};
    range.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    range.levelCount = 1;
    range.layerCount = 1;
    fns_->cmdClearColorImage(cmdBuffer_, image_, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, &clear, 1, &range);

    BarrierToLayout(VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
                    VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT,
                    VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, VK_ACCESS_SHADER_READ_BIT);

    SubmitAndWait();
}

int VulkanInkLayerBitmap::DispatchBrush(VulkanBrushShader* shader,
                                        const void* strokePoints, uint32_t pointCount,
                                        const void* managedConstants,
                                        const void* extraParams, uint32_t extraParamsSize) {
    if (!fns_ || !shader || !strokePoints || !managedConstants || pointCount < 2) return -1;
    if (image_ == VK_NULL_HANDLE || framebuffer_ == VK_NULL_HANDLE) return -2;
    VkDevice device = pipeline_->Context().device;

    constexpr VkDeviceSize kBrushConstantsBytes = 96; // 80 managed + 16 framework
    constexpr VkDeviceSize kManagedConstantsBytes = 80;
    const VkDeviceSize strokeBytes = static_cast<VkDeviceSize>(pointCount) * 16;
    const VkDeviceSize userBytes = (extraParams && extraParamsSize > 0) ? extraParamsSize : 16;

    if (!EnsureBuffer(cbBuffer_, cbMemory_, cbMapped_, cbCapacity_, kBrushConstantsBytes,
                      VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT)) return -3;
    if (!EnsureBuffer(userBuffer_, userMemory_, userMapped_, userCapacity_, userBytes,
                      VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT)) return -3;
    if (!EnsureBuffer(strokeBuffer_, strokeMemory_, strokeMapped_, strokeCapacity_, strokeBytes,
                      VK_BUFFER_USAGE_STORAGE_BUFFER_BIT)) return -3;

    // BrushConstants: copy 80 managed bytes, then fill ViewportSize (offset 64)
    // and zero the trailing 8-byte pad to reach the 96-byte cbuffer.
    std::memcpy(cbMapped_, managedConstants, kManagedConstantsBytes);
    float* viewport = reinterpret_cast<float*>(static_cast<uint8_t*>(cbMapped_) + 64);
    viewport[0] = static_cast<float>(width_);
    viewport[1] = static_cast<float>(height_);
    viewport[2] = 0.0f;
    viewport[3] = 0.0f;

    if (extraParams && extraParamsSize > 0) {
        std::memcpy(userMapped_, extraParams, extraParamsSize);
    } else {
        std::memset(userMapped_, 0, 16);
    }

    std::memcpy(strokeMapped_, strokePoints, static_cast<size_t>(strokeBytes));

    // Update descriptor set (b0 UBO, b1 UBO, t0 SSBO).
    VkDescriptorBufferInfo cbInfo{ cbBuffer_, 0, kBrushConstantsBytes };
    VkDescriptorBufferInfo userInfo{ userBuffer_, 0, VK_WHOLE_SIZE };
    VkDescriptorBufferInfo strokeInfo{ strokeBuffer_, 0, strokeBytes };
    VkWriteDescriptorSet writes[3]{};
    writes[0].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    writes[0].dstSet = descriptorSet_;
    writes[0].dstBinding = VulkanShaderCompiler::kBShift + 0;
    writes[0].descriptorCount = 1;
    writes[0].descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
    writes[0].pBufferInfo = &cbInfo;
    writes[1].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    writes[1].dstSet = descriptorSet_;
    writes[1].dstBinding = VulkanShaderCompiler::kBShift + 1;
    writes[1].descriptorCount = 1;
    writes[1].descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
    writes[1].pBufferInfo = &userInfo;
    writes[2].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    writes[2].dstSet = descriptorSet_;
    writes[2].dstBinding = VulkanShaderCompiler::kTShift + 0;
    writes[2].descriptorCount = 1;
    writes[2].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
    writes[2].pBufferInfo = &strokeInfo;
    fns_->updateDescriptorSets(device, 3, writes, 0, nullptr);

    // Stroke bbox → scissor (fast reject), clamped to the layer extent + margin.
    const float* mc = reinterpret_cast<const float*>(managedConstants);
    float bbMinX = mc[8], bbMinY = mc[9], bbMaxX = mc[10], bbMaxY = mc[11];
    VkRect2D scissor{ {0, 0}, { width_, height_ } };
    if (bbMaxX > bbMinX && bbMaxY > bbMinY) {
        const float margin = 2.0f;
        int left   = static_cast<int>(bbMinX - margin);
        int top    = static_cast<int>(bbMinY - margin);
        int right  = static_cast<int>(bbMaxX + margin + 1.0f);
        int bottom = static_cast<int>(bbMaxY + margin + 1.0f);
        left = left < 0 ? 0 : left;
        top  = top  < 0 ? 0 : top;
        if (right > (int)width_)  right = (int)width_;
        if (bottom > (int)height_) bottom = (int)height_;
        if (right > left && bottom > top) {
            scissor.offset = { left, top };
            scissor.extent = { (uint32_t)(right - left), (uint32_t)(bottom - top) };
        }
    }

    if (!BeginCommands()) return -4;

    BarrierToLayout(VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,
                    VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_TRANSFER_BIT,
                    VK_ACCESS_SHADER_READ_BIT,
                    VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
                    VK_ACCESS_COLOR_ATTACHMENT_READ_BIT | VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT);

    VkRenderPassBeginInfo rpBegin{};
    rpBegin.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
    rpBegin.renderPass = pipeline_->InkRenderPass();
    rpBegin.framebuffer = framebuffer_;
    rpBegin.renderArea.offset = { 0, 0 };
    rpBegin.renderArea.extent = { width_, height_ };
    fns_->cmdBeginRenderPass(cmdBuffer_, &rpBegin, VK_SUBPASS_CONTENTS_INLINE);

    VkViewport viewportRect{};
    viewportRect.x = 0.0f;
    viewportRect.y = 0.0f;
    viewportRect.width = static_cast<float>(width_);
    viewportRect.height = static_cast<float>(height_);
    viewportRect.minDepth = 0.0f;
    viewportRect.maxDepth = 1.0f;
    fns_->cmdSetViewport(cmdBuffer_, 0, 1, &viewportRect);
    fns_->cmdSetScissor(cmdBuffer_, 0, 1, &scissor);

    fns_->cmdBindPipeline(cmdBuffer_, VK_PIPELINE_BIND_POINT_GRAPHICS, shader->Pipeline());
    VkPipelineLayout layout = pipeline_->PipelineLayout();
    fns_->cmdBindDescriptorSets(cmdBuffer_, VK_PIPELINE_BIND_POINT_GRAPHICS, layout,
                                0, 1, &descriptorSet_, 0, nullptr);
    fns_->cmdDraw(cmdBuffer_, 3, 1, 0, 0);

    fns_->cmdEndRenderPass(cmdBuffer_);

    BarrierToLayout(VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
                    VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
                    VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT,
                    VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, VK_ACCESS_SHADER_READ_BIT);

    if (!SubmitAndWait()) return -5;
    return 0;
}

bool VulkanInkLayerBitmap::ReadbackBgra(std::vector<uint8_t>& outBgra) {
    if (!fns_ || !fns_->cmdCopyImageToBuffer || image_ == VK_NULL_HANDLE) return false;
    if (width_ == 0 || height_ == 0) return false;
    const VkDeviceSize bytes = static_cast<VkDeviceSize>(width_) * height_ * 4u;
    if (!EnsureBuffer(readbackBuffer_, readbackMemory_, readbackMapped_, readbackCapacity_,
                      bytes, VK_BUFFER_USAGE_TRANSFER_DST_BIT)) {
        return false;
    }

    if (!BeginCommands()) return false;
    BarrierToLayout(VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                    VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, VK_ACCESS_SHADER_READ_BIT,
                    VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_READ_BIT);

    VkBufferImageCopy copy{};
    copy.imageSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    copy.imageSubresource.layerCount = 1;
    copy.imageExtent = { width_, height_, 1 };
    fns_->cmdCopyImageToBuffer(cmdBuffer_, image_, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                               readbackBuffer_, 1, &copy);

    BarrierToLayout(VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
                    VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_READ_BIT,
                    VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, VK_ACCESS_SHADER_READ_BIT);
    if (!SubmitAndWait()) return false;

    // Image is R8G8B8A8 → convert to BGRA8 for the CPU pixelBuffer_ convention.
    outBgra.resize(static_cast<size_t>(bytes));
    const uint8_t* src = static_cast<const uint8_t*>(readbackMapped_);
    for (size_t i = 0; i + 3 < static_cast<size_t>(bytes); i += 4) {
        outBgra[i + 0] = src[i + 2]; // B
        outBgra[i + 1] = src[i + 1]; // G
        outBgra[i + 2] = src[i + 0]; // R
        outBgra[i + 3] = src[i + 3]; // A
    }
    return true;
}

} // namespace jalium
