#include "vulkan_vello_compute.h"
#include "vulkan_vello_shaders.h"   // kVello<Stage>Spv / ...SpvSize

#include <vector>
#include <cstring>
#include <algorithm>

namespace jalium {

namespace {

// GPU-internal struct strides (mirror d3d12_vello.h; these structs are written
// and read entirely on-GPU so only the byte stride matters here). Compiled with
// -fvk-use-dx-layout, so these match the SPIR-V StructuredBuffer ArrayStrides
// exactly (verified: LineSoup 24, VelloPath 20, VelloTile 8, VelloSegment 20,
// SegmentCount 8, BinHeader 8, PathBbox 24, ClipBic 8, ClipEl 20).
constexpr uint32_t kLineSoupStride     = 24;
constexpr uint32_t kVelloPathStride    = 20;
constexpr uint32_t kVelloTileStride    = 8;
constexpr uint32_t kVelloSegmentStride = 20;
constexpr uint32_t kSegCountStride     = 8;
constexpr uint32_t kBinHeaderStride    = 8;
constexpr uint32_t kPathBboxStride     = 24;
constexpr uint32_t kClipBicStride      = 8;
constexpr uint32_t kClipElStride       = 20;
constexpr uint32_t kFloat4Stride       = 16;   // intersected_bbox / clip_bbox
constexpr uint32_t kBumpBytes          = 32;   // BumpAllocators

// Fixed power-of-two capacities (element counts) — bump-allocator-indexed
// over-allocations, NOT growable. Match D3D12 EnsureGPUBuffers nominal sizes.
constexpr uint32_t kBinDataCap   = 1u << 18;   // uint
constexpr uint32_t kTilesCap     = 1u << 21;   // VelloTile
constexpr uint32_t kSegCountsCap = 1u << 21;   // VelloSegmentCount
constexpr uint32_t kSegmentsCap  = 1u << 21;   // VelloSegment
constexpr uint32_t kPtclCap      = 1u << 23;   // uint
constexpr uint32_t kBlendCap     = 1u << 20;   // reported in config; no buffer (fine stripped it)

// GPU pipeline config (96 bytes, matches HLSL cbuffer VelloConfig / d3d12_vello.h).
struct VelloConfig {
    uint32_t width_in_tiles;
    uint32_t height_in_tiles;
    uint32_t target_width;
    uint32_t target_height;
    uint32_t base_color;
    uint32_t n_drawobj;
    uint32_t n_path;
    uint32_t n_clip;
    uint32_t bin_data_start;
    uint32_t lines_size;
    uint32_t binning_size;
    uint32_t tiles_size;
    uint32_t seg_counts_size;
    uint32_t segments_size;
    uint32_t blend_size;
    uint32_t ptcl_size;
    uint32_t num_segments;
    uint32_t pad_[7];
};
static_assert(sizeof(VelloConfig) == 96, "VelloConfig must be 96 bytes");

using Res = VelloComputePipeline::Res;
using StageBinding = VelloComputePipeline::StageBinding;

constexpr VkDescriptorType UBO = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
constexpr VkDescriptorType SSB = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
constexpr VkDescriptorType SIMG = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
constexpr VkDescriptorType SMPL = VK_DESCRIPTOR_TYPE_SAMPLER;
constexpr VkDescriptorType STIMG = VK_DESCRIPTOR_TYPE_STORAGE_IMAGE;

// Per-stage binding tables. Binding numbers are the register-shifted values
// emitted by dxc (-fvk-{t,s,u}-shift {16,32,48}); only the bindings that survive
// -O3 are listed (verified by spirv-dis). The descriptor-set layout for each
// stage is built EXACTLY from its list, so it matches the loaded SPIR-V.
const StageBinding kBboxClear[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::PathInfo}, {48, SSB, Res::PathBbox},
};
const StageBinding kFlatten[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::PathSegment},
    {48, SSB, Res::LineSoup}, {49, SSB, Res::Bump}, {50, SSB, Res::PathBbox},
};
const StageBinding kClipReduce[] = {
    {16, SSB, Res::ClipInp}, {17, SSB, Res::PathBbox},
    {48, SSB, Res::ClipBic}, {49, SSB, Res::ClipEl},
};
const StageBinding kClipLeaf[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::ClipInp}, {17, SSB, Res::PathBbox},
    {19, SSB, Res::ClipEl}, {49, SSB, Res::ClipBbox},
};
const StageBinding kBinning[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::DrawMonoid}, {17, SSB, Res::PathBbox},
    {18, SSB, Res::ClipBbox}, {48, SSB, Res::Bump}, {49, SSB, Res::IntersectedBbox},
    {50, SSB, Res::BinData}, {51, SSB, Res::BinHeader},
};
const StageBinding kTileAlloc[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::IntersectedBbox}, {17, SSB, Res::DrawTag},
    {48, SSB, Res::Bump}, {49, SSB, Res::VelloPath}, {50, SSB, Res::VelloTile},
};
const StageBinding kPathCountSetup[] = {
    {48, SSB, Res::Bump}, {49, SSB, Res::Indirect1},
};
const StageBinding kPathCount[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::LineSoup}, {17, SSB, Res::VelloPath},
    {48, SSB, Res::Bump}, {49, SSB, Res::VelloTile}, {50, SSB, Res::SegCount},
};
const StageBinding kPathTilingSetup[] = {
    {48, SSB, Res::Bump}, {49, SSB, Res::Indirect2},
};
const StageBinding kPathTiling[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::SegCount}, {17, SSB, Res::LineSoup},
    {18, SSB, Res::VelloPath}, {19, SSB, Res::VelloTile},
    {48, SSB, Res::Bump}, {49, SSB, Res::VelloSegment},
};
const StageBinding kBackdrop[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::VelloPath}, {48, SSB, Res::VelloTile},
};
const StageBinding kCoarse[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::DrawTag}, {17, SSB, Res::DrawMonoid},
    {18, SSB, Res::PathDraw}, {19, SSB, Res::BinHeader}, {20, SSB, Res::BinData},
    {21, SSB, Res::VelloPath}, {22, SSB, Res::PathBbox},
    {48, SSB, Res::VelloTile}, {49, SSB, Res::Bump}, {50, SSB, Res::Ptcl},
};
const StageBinding kFine[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::VelloSegment}, {17, SSB, Res::Ptcl},
    {18, SSB, Res::GradientRamp}, {20, SIMG, Res::DummyImage},
    {32, SMPL, Res::Sampler}, {48, STIMG, Res::OutputImage},
};

// Stage index order — also the dispatch order for the non-indirect stages.
enum StageIdx : uint32_t {
    S_BboxClear = 0, S_Flatten, S_ClipReduce, S_ClipLeaf, S_Binning, S_TileAlloc,
    S_PathCountSetup, S_PathCount, S_Backdrop, S_Coarse, S_PathTilingSetup,
    S_PathTiling, S_Fine,
};

struct StageDef {
    const uint32_t*     spirv;
    size_t              spirvSize;
    const StageBinding* bindings;
    uint32_t            bindingCount;
};

#define VVC_STAGE(spv, tbl) { spv, spv##Size, tbl, (uint32_t)(sizeof(tbl) / sizeof(tbl[0])) }

const StageDef kStages[VelloComputePipeline::kStageCount] = {
    VVC_STAGE(kVelloBboxClearSpv,      kBboxClear),
    VVC_STAGE(kVelloFlattenSpv,        kFlatten),
    VVC_STAGE(kVelloClipReduceSpv,     kClipReduce),
    VVC_STAGE(kVelloClipLeafSpv,       kClipLeaf),
    VVC_STAGE(kVelloBinningSpv,        kBinning),
    VVC_STAGE(kVelloTileAllocSpv,      kTileAlloc),
    VVC_STAGE(kVelloPathCountSetupSpv, kPathCountSetup),
    VVC_STAGE(kVelloPathCountSpv,      kPathCount),
    VVC_STAGE(kVelloBackdropSpv,       kBackdrop),
    VVC_STAGE(kVelloCoarseSpv,         kCoarse),
    VVC_STAGE(kVelloPathTilingSetupSpv,kPathTilingSetup),
    VVC_STAGE(kVelloPathTilingSpv,     kPathTiling),
    VVC_STAGE(kVelloFineSpv,           kFine),
};
#undef VVC_STAGE

constexpr uint32_t kTileSize = 16;
inline uint32_t DivCeil(uint32_t a, uint32_t b) { return (a + b - 1) / b; }
inline uint32_t Wg256(uint32_t n) { return (n + 255) / 256; }

// A full COMPUTE->COMPUTE global memory barrier (the analogue of D3D12's UAV
// barrier between dependent compute stages).
void ComputeBarrier(PFN_vkCmdPipelineBarrier fn, VkCommandBuffer cmd) {
    VkMemoryBarrier mb{};
    mb.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
    mb.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
    mb.dstAccessMask = VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT;
    fn(cmd, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
       0, 1, &mb, 0, nullptr, 0, nullptr);
}

} // namespace

VelloComputePipeline::~VelloComputePipeline() { Destroy(); }

uint32_t VelloComputePipeline::FindMemoryType(uint32_t typeFilter,
                                              VkMemoryPropertyFlags props) const {
    for (uint32_t i = 0; i < memoryProperties_.memoryTypeCount; ++i) {
        if ((typeFilter & (1u << i)) &&
            (memoryProperties_.memoryTypes[i].propertyFlags & props) == props) {
            return i;
        }
    }
    return UINT32_MAX;
}

bool VelloComputePipeline::CreateBuffer(VkDeviceSize size, VkBufferUsageFlags usage,
                                        VkMemoryPropertyFlags memProps, GpuBuffer& out) {
    if (size == 0) size = 256;
    VkBufferCreateInfo bi{};
    bi.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    bi.size = size;
    bi.usage = usage;
    bi.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    if (createBuffer_(device_, &bi, nullptr, &out.buffer) != VK_SUCCESS) return false;

    VkMemoryRequirements req{};
    getBufferMemoryRequirements_(device_, out.buffer, &req);
    uint32_t typeIdx = FindMemoryType(req.memoryTypeBits, memProps);
    if (typeIdx == UINT32_MAX) return false;

    VkMemoryAllocateInfo ai{};
    ai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    ai.allocationSize = req.size;
    ai.memoryTypeIndex = typeIdx;
    if (allocateMemory_(device_, &ai, nullptr, &out.memory) != VK_SUCCESS) return false;
    if (bindBufferMemory_(device_, out.buffer, out.memory, 0) != VK_SUCCESS) return false;

    out.capacity = size;
    if (memProps & VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT) {
        if (mapMemory_(device_, out.memory, 0, VK_WHOLE_SIZE, 0, &out.mapped) != VK_SUCCESS) {
            out.mapped = nullptr;
            return false;
        }
    }
    return true;
}

void VelloComputePipeline::DestroyBuffer(GpuBuffer& buf) {
    if (buf.mapped && buf.memory) { unmapMemory_(device_, buf.memory); buf.mapped = nullptr; }
    if (buf.buffer) { destroyBuffer_(device_, buf.buffer, nullptr); buf.buffer = VK_NULL_HANDLE; }
    if (buf.memory) { freeMemory_(device_, buf.memory, nullptr); buf.memory = VK_NULL_HANDLE; }
    buf.capacity = 0;
}

bool VelloComputePipeline::EnsureBuffer(GpuBuffer& buf, VkDeviceSize bytes,
                                        VkBufferUsageFlags usage,
                                        VkMemoryPropertyFlags memProps) {
    if (bytes == 0) bytes = 256;
    if (buf.buffer && buf.capacity >= bytes) return true;
    DestroyBuffer(buf);
    return CreateBuffer(bytes, usage, memProps, buf);
}

bool VelloComputePipeline::Initialize(VkDevice device, VkPhysicalDevice physicalDevice,
                                      PFN_vkGetDeviceProcAddr getDeviceProcAddr,
                                      const VkPhysicalDeviceMemoryProperties& memoryProperties) {
    device_ = device;
    physicalDevice_ = physicalDevice;
    memoryProperties_ = memoryProperties;

    auto load = [&](const char* name) -> PFN_vkVoidFunction {
        return getDeviceProcAddr(device, name);
    };
    bool ok = true;
    auto need = [&](PFN_vkVoidFunction fp) { if (!fp) ok = false; return fp; };

    createShaderModule_         = (PFN_vkCreateShaderModule)        need(load("vkCreateShaderModule"));
    destroyShaderModule_        = (PFN_vkDestroyShaderModule)       need(load("vkDestroyShaderModule"));
    createDescriptorSetLayout_  = (PFN_vkCreateDescriptorSetLayout) need(load("vkCreateDescriptorSetLayout"));
    destroyDescriptorSetLayout_ = (PFN_vkDestroyDescriptorSetLayout)need(load("vkDestroyDescriptorSetLayout"));
    createPipelineLayout_       = (PFN_vkCreatePipelineLayout)      need(load("vkCreatePipelineLayout"));
    destroyPipelineLayout_      = (PFN_vkDestroyPipelineLayout)     need(load("vkDestroyPipelineLayout"));
    createComputePipelines_     = (PFN_vkCreateComputePipelines)    need(load("vkCreateComputePipelines"));
    destroyPipeline_            = (PFN_vkDestroyPipeline)           need(load("vkDestroyPipeline"));
    createDescriptorPool_       = (PFN_vkCreateDescriptorPool)      need(load("vkCreateDescriptorPool"));
    destroyDescriptorPool_      = (PFN_vkDestroyDescriptorPool)     need(load("vkDestroyDescriptorPool"));
    resetDescriptorPool_        = (PFN_vkResetDescriptorPool)       need(load("vkResetDescriptorPool"));
    allocateDescriptorSets_     = (PFN_vkAllocateDescriptorSets)    need(load("vkAllocateDescriptorSets"));
    updateDescriptorSets_       = (PFN_vkUpdateDescriptorSets)      need(load("vkUpdateDescriptorSets"));
    createBuffer_               = (PFN_vkCreateBuffer)              need(load("vkCreateBuffer"));
    destroyBuffer_              = (PFN_vkDestroyBuffer)             need(load("vkDestroyBuffer"));
    getBufferMemoryRequirements_= (PFN_vkGetBufferMemoryRequirements)need(load("vkGetBufferMemoryRequirements"));
    allocateMemory_             = (PFN_vkAllocateMemory)            need(load("vkAllocateMemory"));
    freeMemory_                 = (PFN_vkFreeMemory)                need(load("vkFreeMemory"));
    bindBufferMemory_           = (PFN_vkBindBufferMemory)          need(load("vkBindBufferMemory"));
    mapMemory_                  = (PFN_vkMapMemory)                 need(load("vkMapMemory"));
    unmapMemory_                = (PFN_vkUnmapMemory)               need(load("vkUnmapMemory"));
    createImage_                = (PFN_vkCreateImage)               need(load("vkCreateImage"));
    destroyImage_               = (PFN_vkDestroyImage)              need(load("vkDestroyImage"));
    getImageMemoryRequirements_ = (PFN_vkGetImageMemoryRequirements)need(load("vkGetImageMemoryRequirements"));
    bindImageMemory_            = (PFN_vkBindImageMemory)           need(load("vkBindImageMemory"));
    createImageView_            = (PFN_vkCreateImageView)           need(load("vkCreateImageView"));
    destroyImageView_           = (PFN_vkDestroyImageView)          need(load("vkDestroyImageView"));
    createSampler_              = (PFN_vkCreateSampler)             need(load("vkCreateSampler"));
    destroySampler_             = (PFN_vkDestroySampler)            need(load("vkDestroySampler"));
    cmdBindPipeline_            = (PFN_vkCmdBindPipeline)           need(load("vkCmdBindPipeline"));
    cmdBindDescriptorSets_      = (PFN_vkCmdBindDescriptorSets)     need(load("vkCmdBindDescriptorSets"));
    cmdDispatch_                = (PFN_vkCmdDispatch)               need(load("vkCmdDispatch"));
    cmdDispatchIndirect_        = (PFN_vkCmdDispatchIndirect)       need(load("vkCmdDispatchIndirect"));
    cmdPipelineBarrier_         = (PFN_vkCmdPipelineBarrier)        need(load("vkCmdPipelineBarrier"));
    cmdFillBuffer_              = (PFN_vkCmdFillBuffer)             need(load("vkCmdFillBuffer"));
    cmdClearColorImage_         = (PFN_vkCmdClearColorImage)        need(load("vkCmdClearColorImage"));
    if (!ok) return false;

    if (!CreatePipelines()) return false;
    if (!CreateOutputSampler()) return false;
    if (!CreateDummyImage()) return false;

    // Per-frame transient descriptor pools.
    VkDescriptorPoolSize poolSizes[] = {
        { VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 80 },
        { VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, 16 },
        { VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,   2 },
        { VK_DESCRIPTOR_TYPE_SAMPLER,         2 },
        { VK_DESCRIPTOR_TYPE_STORAGE_IMAGE,   2 },
    };
    for (uint32_t f = 0; f < kFramesInFlight; ++f) {
        VkDescriptorPoolCreateInfo pi{};
        pi.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
        pi.maxSets = kStageCount;
        pi.poolSizeCount = (uint32_t)(sizeof(poolSizes) / sizeof(poolSizes[0]));
        pi.pPoolSizes = poolSizes;
        if (createDescriptorPool_(device_, &pi, nullptr, &descriptorPools_[f]) != VK_SUCCESS) {
            return false;
        }
    }

    // Fixed device-local scratch buffers that never change size.
    const VkBufferUsageFlags storageDst =
        VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT;
    const VkMemoryPropertyFlags devLocal = VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT;
    if (!CreateBuffer(kBumpBytes, storageDst, devLocal, bump_)) return false;
    if (!CreateBuffer((VkDeviceSize)kBinDataCap * 4, storageDst, devLocal, binData_)) return false;
    if (!CreateBuffer((VkDeviceSize)kTilesCap * kVelloTileStride, storageDst, devLocal, velloTile_)) return false;
    if (!CreateBuffer((VkDeviceSize)kSegCountsCap * kSegCountStride, storageDst, devLocal, segCount_)) return false;
    if (!CreateBuffer((VkDeviceSize)kSegmentsCap * kVelloSegmentStride, storageDst, devLocal, velloSegment_)) return false;
    if (!CreateBuffer((VkDeviceSize)kPtclCap * 4, storageDst, devLocal, ptcl_)) return false;
    const VkBufferUsageFlags indirectUsage = storageDst | VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT;
    if (!CreateBuffer(64, indirectUsage, devLocal, indirect1_)) return false;
    if (!CreateBuffer(64, indirectUsage, devLocal, indirect2_)) return false;

    ready_ = true;
    return true;
}

bool VelloComputePipeline::CreatePipelines() {
    for (uint32_t s = 0; s < kStageCount; ++s) {
        const StageDef& def = kStages[s];

        VkShaderModuleCreateInfo smi{};
        smi.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        smi.codeSize = def.spirvSize;
        smi.pCode = def.spirv;
        if (createShaderModule_(device_, &smi, nullptr, &modules_[s]) != VK_SUCCESS) return false;

        std::vector<VkDescriptorSetLayoutBinding> binds(def.bindingCount);
        for (uint32_t i = 0; i < def.bindingCount; ++i) {
            binds[i] = {};
            binds[i].binding = def.bindings[i].binding;
            binds[i].descriptorType = def.bindings[i].type;
            binds[i].descriptorCount = 1;
            binds[i].stageFlags = VK_SHADER_STAGE_COMPUTE_BIT;
        }
        VkDescriptorSetLayoutCreateInfo li{};
        li.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
        li.bindingCount = (uint32_t)binds.size();
        li.pBindings = binds.data();
        if (createDescriptorSetLayout_(device_, &li, nullptr, &setLayouts_[s]) != VK_SUCCESS) return false;

        VkPipelineLayoutCreateInfo pli{};
        pli.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
        pli.setLayoutCount = 1;
        pli.pSetLayouts = &setLayouts_[s];
        if (createPipelineLayout_(device_, &pli, nullptr, &pipelineLayouts_[s]) != VK_SUCCESS) return false;

        VkComputePipelineCreateInfo ci{};
        ci.sType = VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO;
        ci.stage.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        ci.stage.stage = VK_SHADER_STAGE_COMPUTE_BIT;
        ci.stage.module = modules_[s];
        ci.stage.pName = "main";
        ci.layout = pipelineLayouts_[s];
        if (createComputePipelines_(device_, VK_NULL_HANDLE, 1, &ci, nullptr, &pipelines_[s]) != VK_SUCCESS) {
            return false;
        }
    }
    return true;
}

bool VelloComputePipeline::CreateOutputSampler() {
    VkSamplerCreateInfo si{};
    si.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
    si.magFilter = VK_FILTER_NEAREST;
    si.minFilter = VK_FILTER_NEAREST;
    si.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
    si.addressModeU = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    si.addressModeV = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    si.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    si.maxLod = VK_LOD_CLAMP_NONE;
    if (createSampler_(device_, &si, nullptr, &outputSampler_) != VK_SUCCESS) return false;
    // A second sampler bound to the fine stage's image-atlas slot (unused in v1).
    if (createSampler_(device_, &si, nullptr, &dummySampler_) != VK_SUCCESS) return false;
    return true;
}

bool VelloComputePipeline::CreateDummyImage() {
    VkImageCreateInfo ii{};
    ii.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    ii.imageType = VK_IMAGE_TYPE_2D;
    ii.format = VK_FORMAT_R8G8B8A8_UNORM;
    ii.extent = { 1, 1, 1 };
    ii.mipLevels = 1;
    ii.arrayLayers = 1;
    ii.samples = VK_SAMPLE_COUNT_1_BIT;
    ii.tiling = VK_IMAGE_TILING_OPTIMAL;
    ii.usage = VK_IMAGE_USAGE_SAMPLED_BIT;
    ii.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    if (createImage_(device_, &ii, nullptr, &dummyImage_) != VK_SUCCESS) return false;

    VkMemoryRequirements req{};
    getImageMemoryRequirements_(device_, dummyImage_, &req);
    uint32_t typeIdx = FindMemoryType(req.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    if (typeIdx == UINT32_MAX) return false;
    VkMemoryAllocateInfo ai{};
    ai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    ai.allocationSize = req.size;
    ai.memoryTypeIndex = typeIdx;
    if (allocateMemory_(device_, &ai, nullptr, &dummyMemory_) != VK_SUCCESS) return false;
    if (bindImageMemory_(device_, dummyImage_, dummyMemory_, 0) != VK_SUCCESS) return false;

    VkImageViewCreateInfo vi{};
    vi.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    vi.image = dummyImage_;
    vi.viewType = VK_IMAGE_VIEW_TYPE_2D;
    vi.format = VK_FORMAT_R8G8B8A8_UNORM;
    vi.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
    if (createImageView_(device_, &vi, nullptr, &dummyView_) != VK_SUCCESS) return false;
    return true;
}

bool VelloComputePipeline::EnsureOutputImage(uint32_t width, uint32_t height) {
    if (width == 0) width = 1;
    if (height == 0) height = 1;
    if (outputImage_ && outputWidth_ == width && outputHeight_ == height) return true;

    if (outputView_)  { destroyImageView_(device_, outputView_, nullptr); outputView_ = VK_NULL_HANDLE; }
    if (outputImage_) { destroyImage_(device_, outputImage_, nullptr); outputImage_ = VK_NULL_HANDLE; }
    if (outputMemory_){ freeMemory_(device_, outputMemory_, nullptr); outputMemory_ = VK_NULL_HANDLE; }
    outputLayout_ = VK_IMAGE_LAYOUT_UNDEFINED;

    VkImageCreateInfo ii{};
    ii.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    ii.imageType = VK_IMAGE_TYPE_2D;
    ii.format = VK_FORMAT_R32G32B32A32_SFLOAT;   // fine writes RWTexture2D<float4> (Rgba32f)
    ii.extent = { width, height, 1 };
    ii.mipLevels = 1;
    ii.arrayLayers = 1;
    ii.samples = VK_SAMPLE_COUNT_1_BIT;
    ii.tiling = VK_IMAGE_TILING_OPTIMAL;
    // STORAGE: fine stage writes it; SAMPLED: composite reads it; TRANSFER_DST:
    // the per-frame vkCmdClearColorImage in Record().
    ii.usage = VK_IMAGE_USAGE_STORAGE_BIT | VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT;
    ii.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    if (createImage_(device_, &ii, nullptr, &outputImage_) != VK_SUCCESS) return false;

    VkMemoryRequirements req{};
    getImageMemoryRequirements_(device_, outputImage_, &req);
    uint32_t typeIdx = FindMemoryType(req.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    if (typeIdx == UINT32_MAX) return false;
    VkMemoryAllocateInfo ai{};
    ai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    ai.allocationSize = req.size;
    ai.memoryTypeIndex = typeIdx;
    if (allocateMemory_(device_, &ai, nullptr, &outputMemory_) != VK_SUCCESS) return false;
    if (bindImageMemory_(device_, outputImage_, outputMemory_, 0) != VK_SUCCESS) return false;

    VkImageViewCreateInfo vi{};
    vi.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    vi.image = outputImage_;
    vi.viewType = VK_IMAGE_VIEW_TYPE_2D;
    vi.format = VK_FORMAT_R32G32B32A32_SFLOAT;
    vi.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
    if (createImageView_(device_, &vi, nullptr, &outputView_) != VK_SUCCESS) return false;

    outputWidth_ = width;
    outputHeight_ = height;
    return true;
}

bool VelloComputePipeline::EnsureScratch(const VelloScene& scene) {
    const VkBufferUsageFlags storageDst =
        VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT;
    const VkMemoryPropertyFlags devLocal = VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT;

    const uint32_t numPaths     = std::max<uint32_t>(scene.numPaths, 1);
    const uint32_t numSegs      = std::max<uint32_t>((uint32_t)scene.segments.size(), 1);
    const uint32_t numDrawObjs  = std::max<uint32_t>(scene.numDrawObjs, 1);
    const uint32_t numClipOps   = std::max<uint32_t>(scene.numClipOps, 1);
    const uint32_t tilesX       = DivCeil(scene.viewportW, kTileSize);
    const uint32_t tilesY       = DivCeil(scene.viewportH, kTileSize);
    const uint32_t numBins      = std::max<uint32_t>(DivCeil(tilesX, 16) * DivCeil(tilesY, 16), 1);
    const uint32_t clipWgs      = std::max<uint32_t>(Wg256(numClipOps), 1);

    bool ok = true;
    ok &= EnsureBuffer(pathBbox_,        (VkDeviceSize)numPaths * kPathBboxStride,         storageDst, devLocal);
    ok &= EnsureBuffer(lineSoup_,        (VkDeviceSize)numSegs * 64 * kLineSoupStride,     storageDst, devLocal);
    ok &= EnsureBuffer(intersectedBbox_, (VkDeviceSize)numDrawObjs * kFloat4Stride,        storageDst, devLocal);
    ok &= EnsureBuffer(clipBic_,         (VkDeviceSize)clipWgs * kClipBicStride,           storageDst, devLocal);
    ok &= EnsureBuffer(clipEl_,          (VkDeviceSize)numClipOps * kClipElStride,         storageDst, devLocal);
    ok &= EnsureBuffer(clipBbox_,        (VkDeviceSize)numClipOps * kFloat4Stride,         storageDst, devLocal);
    ok &= EnsureBuffer(binHeader_,       (VkDeviceSize)numBins * 256 * kBinHeaderStride,   storageDst, devLocal);
    ok &= EnsureBuffer(velloPath_,       (VkDeviceSize)numDrawObjs * kVelloPathStride,     storageDst, devLocal);
    return ok;
}

bool VelloComputePipeline::UploadInputs(const VelloScene& scene, uint32_t frameIdx) {
    const VkBufferUsageFlags hostStorage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT;
    const VkBufferUsageFlags hostUbo     = VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT;
    const VkMemoryPropertyFlags hostVis =
        VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT;

    auto upload = [&](GpuBuffer& buf, const void* data, size_t bytes, VkBufferUsageFlags usage) -> bool {
        if (!EnsureBuffer(buf, std::max<size_t>(bytes, 256), usage, hostVis)) return false;
        if (bytes && data) std::memcpy(buf.mapped, data, bytes);
        return true;
    };

    // VelloConfig — built here (capacities are a container concern, not the encoder's).
    const uint32_t tilesX = DivCeil(scene.viewportW, kTileSize);
    const uint32_t tilesY = DivCeil(scene.viewportH, kTileSize);
    const uint32_t numSegs = (uint32_t)scene.segments.size();
    const uint32_t lineSoupCap = std::max<uint32_t>(numSegs, 1) * 64;

    VelloConfig cfg{};
    cfg.width_in_tiles  = tilesX;
    cfg.height_in_tiles = tilesY;
    cfg.target_width    = scene.viewportW;
    cfg.target_height   = scene.viewportH;
    cfg.base_color      = 0;                       // transparent
    cfg.n_drawobj       = scene.numDrawObjs;
    cfg.n_path          = scene.numPaths;
    cfg.n_clip          = scene.numClipOps;
    cfg.bin_data_start  = 0;
    cfg.lines_size      = lineSoupCap;
    cfg.binning_size    = kBinDataCap;
    cfg.tiles_size      = kTilesCap;
    cfg.seg_counts_size = kSegCountsCap;
    cfg.segments_size   = kSegmentsCap;
    cfg.blend_size      = kBlendCap;
    cfg.ptcl_size       = kPtclCap;
    cfg.num_segments    = numSegs;

    bool ok = true;
    ok &= upload(config_[frameIdx],      &cfg, sizeof(cfg), hostUbo);
    ok &= upload(pathSegment_[frameIdx], scene.segments.data(),  scene.segments.size()  * sizeof(PathSegment),     hostStorage);
    ok &= upload(pathInfo_[frameIdx],    scene.pathInfos.data(), scene.pathInfos.size() * sizeof(PathInfo),        hostStorage);
    ok &= upload(pathDraw_[frameIdx],    scene.pathDraws.data(), scene.pathDraws.size() * sizeof(PathDraw),        hostStorage);
    ok &= upload(drawTag_[frameIdx],     scene.drawTags.data(),  scene.drawTags.size()  * sizeof(DrawTag),         hostStorage);
    ok &= upload(drawMonoid_[frameIdx],  scene.drawMonoids.data(), scene.drawMonoids.size() * sizeof(VelloDrawMonoid), hostStorage);
    ok &= upload(clipInp_[frameIdx],     scene.clipInps.data(),  scene.clipInps.size()  * sizeof(VelloClipInp),    hostStorage);
    ok &= upload(gradientRamp_[frameIdx],scene.gradientRamps.data(), scene.gradientRamps.size() * sizeof(uint32_t),hostStorage);
    return ok;
}

VkBuffer VelloComputePipeline::BufferForRes(Res res, uint32_t f) const {
    switch (res) {
        case Res::Config:          return config_[f].buffer;
        case Res::PathSegment:     return pathSegment_[f].buffer;
        case Res::PathInfo:        return pathInfo_[f].buffer;
        case Res::PathDraw:        return pathDraw_[f].buffer;
        case Res::DrawTag:         return drawTag_[f].buffer;
        case Res::DrawMonoid:      return drawMonoid_[f].buffer;
        case Res::ClipInp:         return clipInp_[f].buffer;
        case Res::GradientRamp:    return gradientRamp_[f].buffer;
        case Res::Bump:            return bump_.buffer;
        case Res::PathBbox:        return pathBbox_.buffer;
        case Res::LineSoup:        return lineSoup_.buffer;
        case Res::IntersectedBbox: return intersectedBbox_.buffer;
        case Res::ClipBic:         return clipBic_.buffer;
        case Res::ClipEl:          return clipEl_.buffer;
        case Res::ClipBbox:        return clipBbox_.buffer;
        case Res::BinHeader:       return binHeader_.buffer;
        case Res::BinData:         return binData_.buffer;
        case Res::VelloPath:       return velloPath_.buffer;
        case Res::VelloTile:       return velloTile_.buffer;
        case Res::SegCount:        return segCount_.buffer;
        case Res::VelloSegment:    return velloSegment_.buffer;
        case Res::Ptcl:            return ptcl_.buffer;
        case Res::Indirect1:       return indirect1_.buffer;
        case Res::Indirect2:       return indirect2_.buffer;
        default:                   return VK_NULL_HANDLE;
    }
}

bool VelloComputePipeline::BuildDescriptorSets(uint32_t frameIdx, const VelloScene& scene) {
    if (resetDescriptorPool_(device_, descriptorPools_[frameIdx], 0) != VK_SUCCESS) return false;

    VkDescriptorSetLayout layouts[kStageCount];
    for (uint32_t s = 0; s < kStageCount; ++s) layouts[s] = setLayouts_[s];
    VkDescriptorSetAllocateInfo ai{};
    ai.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
    ai.descriptorPool = descriptorPools_[frameIdx];
    ai.descriptorSetCount = kStageCount;
    ai.pSetLayouts = layouts;
    if (allocateDescriptorSets_(device_, &ai, stageSets_) != VK_SUCCESS) return false;

    const bool hasClips = scene.numClipOps > 0;

    // Reused storage for the descriptor writes (stable addresses required until
    // the updateDescriptorSets call).
    std::vector<VkDescriptorBufferInfo> bufInfos;
    std::vector<VkDescriptorImageInfo>  imgInfos;
    std::vector<VkWriteDescriptorSet>   writes;
    bufInfos.reserve(96); imgInfos.reserve(8); writes.reserve(96);

    for (uint32_t s = 0; s < kStageCount; ++s) {
        const StageDef& def = kStages[s];
        for (uint32_t i = 0; i < def.bindingCount; ++i) {
            const StageBinding& b = def.bindings[i];
            VkWriteDescriptorSet w{};
            w.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
            w.dstSet = stageSets_[s];
            w.dstBinding = b.binding;
            w.descriptorCount = 1;
            w.descriptorType = b.type;

            if (b.type == SIMG || b.type == STIMG || b.type == SMPL) {
                VkDescriptorImageInfo ii{};
                if (b.res == Res::OutputImage) {
                    ii.imageView = outputView_;
                    ii.imageLayout = VK_IMAGE_LAYOUT_GENERAL;
                } else if (b.res == Res::DummyImage) {
                    ii.imageView = dummyView_;
                    ii.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
                } else { // Sampler
                    ii.sampler = dummySampler_;
                }
                imgInfos.push_back(ii);
                w.pImageInfo = &imgInfos.back();
            } else {
                Res res = b.res;
                // Mirror D3D12: when there are no clips, binning reads a dummy
                // for clip_bbox (clip_leaf never wrote it).
                if (res == Res::ClipBbox && !hasClips) res = Res::IntersectedBbox;
                VkDescriptorBufferInfo bi{};
                bi.buffer = BufferForRes(res, frameIdx);
                bi.offset = 0;
                bi.range = VK_WHOLE_SIZE;
                bufInfos.push_back(bi);
                w.pBufferInfo = &bufInfos.back();
            }
            writes.push_back(w);
        }
    }
    updateDescriptorSets_(device_, (uint32_t)writes.size(), writes.data(), 0, nullptr);
    return true;
}

bool VelloComputePipeline::Record(VkCommandBuffer cmd, const VelloScene& scene, uint32_t frameIdx) {
    if (!ready_ || scene.drawTags.empty()) return false;
    frameIdx %= kFramesInFlight;

    if (!EnsureOutputImage(scene.viewportW, scene.viewportH)) return false;
    if (!EnsureScratch(scene)) return false;
    if (!UploadInputs(scene, frameIdx)) return false;
    if (!BuildDescriptorSets(frameIdx, scene)) return false;

    const uint32_t numPaths    = scene.numPaths;
    const uint32_t numSegs     = (uint32_t)scene.segments.size();
    const uint32_t numDrawObjs = scene.numDrawObjs;
    const uint32_t numClipOps  = scene.numClipOps;
    const uint32_t tilesX = DivCeil(scene.viewportW, kTileSize);
    const uint32_t tilesY = DivCeil(scene.viewportH, kTileSize);
    const uint32_t widthInBins  = DivCeil(tilesX, 16);
    const uint32_t heightInBins = DivCeil(tilesY, 16);

    // ── Leading serialization barriers ──
    // (1) global compute->compute: serialize this frame's scratch writes after
    //     the previous frame's compute (single shared scratch buffers); and
    //     transition the output image to GENERAL (waiting on the previous
    //     frame's composite fragment read) + the dummy image to SHADER_READ.
    {
        VkMemoryBarrier mb{};
        mb.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
        // INDIRECT_COMMAND_READ: indirect1_/indirect2_ are single shared buffers;
        // the previous frame's last touch was vkCmdDispatchIndirect (a
        // DRAW_INDIRECT read), so serialize this frame's path_count_setup write
        // against it explicitly (mirrors D3D12's INDIRECT_ARGUMENT<->UAV round-trip).
        mb.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT | VK_ACCESS_INDIRECT_COMMAND_READ_BIT;
        mb.dstAccessMask = VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT | VK_ACCESS_TRANSFER_WRITE_BIT;

        VkImageMemoryBarrier imgs[2]{};
        // output image: prior composite fragment-read -> this frame's writes (clear+fine), -> GENERAL.
        imgs[0].sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        imgs[0].srcAccessMask = VK_ACCESS_SHADER_READ_BIT;
        imgs[0].dstAccessMask = VK_ACCESS_SHADER_WRITE_BIT | VK_ACCESS_TRANSFER_WRITE_BIT;
        imgs[0].oldLayout = outputLayout_;
        imgs[0].newLayout = VK_IMAGE_LAYOUT_GENERAL;
        imgs[0].srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        imgs[0].dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        imgs[0].image = outputImage_;
        imgs[0].subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
        // dummy image: (re)assert SHADER_READ for fine's unused image-atlas binding
        // EVERY frame (UNDEFINED oldLayout is always valid; robust to a failed first submit).
        imgs[1].sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        imgs[1].srcAccessMask = 0;
        imgs[1].dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
        imgs[1].oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        imgs[1].newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        imgs[1].srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        imgs[1].dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        imgs[1].image = dummyImage_;
        imgs[1].subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
        cmdPipelineBarrier_(cmd,
            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT | VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT,
            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT | VK_PIPELINE_STAGE_TRANSFER_BIT,
            0, 1, &mb, 0, nullptr, 2, imgs);
        outputLayout_ = VK_IMAGE_LAYOUT_GENERAL;
    }

    // Clear the output image to transparent (D3D12 parity / defensive — the fine
    // stage writes every in-bounds pixel, but the clear future-proofs any partial
    // write) and zero the bump allocator. Both are TRANSFER writes made visible to
    // the first compute stage (and to fine's image write) by the barrier below.
    {
        VkClearColorValue clear{};
        VkImageSubresourceRange range{ VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
        cmdClearColorImage_(cmd, outputImage_, VK_IMAGE_LAYOUT_GENERAL, &clear, 1, &range);
    }
    cmdFillBuffer_(cmd, bump_.buffer, 0, VK_WHOLE_SIZE, 0u);
    {
        VkMemoryBarrier mb{};
        mb.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
        mb.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
        mb.dstAccessMask = VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT;
        cmdPipelineBarrier_(cmd, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                            0, 1, &mb, 0, nullptr, 0, nullptr);
    }

    auto dispatch = [&](uint32_t s, uint32_t gx, uint32_t gy, uint32_t gz) {
        cmdBindPipeline_(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipelines_[s]);
        cmdBindDescriptorSets_(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipelineLayouts_[s],
                               0, 1, &stageSets_[s], 0, nullptr);
        cmdDispatch_(cmd, gx, gy, gz);
    };
    auto barrier = [&]() { ComputeBarrier(cmdPipelineBarrier_, cmd); };

    // Compute<-write -> indirect-read dependency around the two indirect setups.
    auto indirectBarrier = [&]() {
        VkMemoryBarrier mb{};
        mb.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
        mb.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
        mb.dstAccessMask = VK_ACCESS_INDIRECT_COMMAND_READ_BIT;
        cmdPipelineBarrier_(cmd, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                            VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT, 0, 1, &mb, 0, nullptr, 0, nullptr);
    };

    // ── Stage 1: bbox_clear ──
    dispatch(S_BboxClear, Wg256(numPaths), 1, 1);
    barrier();
    // ── Stage 2: flatten ──
    dispatch(S_Flatten, Wg256(numSegs), 1, 1);
    barrier();
    // ── Stage 3a/3b: clip_reduce -> clip_leaf (only when there are clips) ──
    if (numClipOps > 0) {
        dispatch(S_ClipReduce, Wg256(numClipOps), 1, 1);
        barrier();
        dispatch(S_ClipLeaf, Wg256(numClipOps), 1, 1);
        barrier();
    }
    // ── Stage 4: binning ──
    dispatch(S_Binning, Wg256(numDrawObjs), 1, 1);
    barrier();
    // ── Stage 5: tile_alloc ──
    dispatch(S_TileAlloc, Wg256(numDrawObjs), 1, 1);
    barrier();
    // ── Stage 6: path_count_setup (writes indirect1) ──
    dispatch(S_PathCountSetup, 1, 1, 1);
    indirectBarrier();
    // ── Stage 7: path_count (indirect) ──
    cmdBindPipeline_(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipelines_[S_PathCount]);
    cmdBindDescriptorSets_(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipelineLayouts_[S_PathCount],
                           0, 1, &stageSets_[S_PathCount], 0, nullptr);
    cmdDispatchIndirect_(cmd, indirect1_.buffer, 0);
    barrier();
    // ── Stage 8: backdrop ──
    dispatch(S_Backdrop, numDrawObjs, tilesY, 1);
    barrier();
    // ── Stage 9: coarse ──
    dispatch(S_Coarse, widthInBins, heightInBins, 1);
    barrier();
    // ── Stage 10: path_tiling_setup (writes indirect2) ──
    dispatch(S_PathTilingSetup, 1, 1, 1);
    indirectBarrier();
    // ── Stage 11: path_tiling (indirect) ──
    cmdBindPipeline_(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipelines_[S_PathTiling]);
    cmdBindDescriptorSets_(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipelineLayouts_[S_PathTiling],
                           0, 1, &stageSets_[S_PathTiling], 0, nullptr);
    cmdDispatchIndirect_(cmd, indirect2_.buffer, 0);
    barrier();
    // ── Stage 12: fine (writes the output storage image) ──
    dispatch(S_Fine, tilesX, tilesY, 1);

    // Transition the output image GENERAL -> SHADER_READ for the composite blit.
    {
        VkImageMemoryBarrier ob{};
        ob.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        ob.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
        ob.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
        ob.oldLayout = VK_IMAGE_LAYOUT_GENERAL;
        ob.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        ob.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        ob.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        ob.image = outputImage_;
        ob.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
        cmdPipelineBarrier_(cmd, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                            VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, 0, 0, nullptr, 0, nullptr, 1, &ob);
        outputLayout_ = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    }
    return true;
}

void VelloComputePipeline::Destroy() {
    if (!device_) return;

    for (uint32_t s = 0; s < kStageCount; ++s) {
        if (pipelines_[s])       destroyPipeline_(device_, pipelines_[s], nullptr);
        if (pipelineLayouts_[s]) destroyPipelineLayout_(device_, pipelineLayouts_[s], nullptr);
        if (setLayouts_[s])      destroyDescriptorSetLayout_(device_, setLayouts_[s], nullptr);
        if (modules_[s])         destroyShaderModule_(device_, modules_[s], nullptr);
        pipelines_[s] = VK_NULL_HANDLE; pipelineLayouts_[s] = VK_NULL_HANDLE;
        setLayouts_[s] = VK_NULL_HANDLE; modules_[s] = VK_NULL_HANDLE;
    }
    for (uint32_t f = 0; f < kFramesInFlight; ++f) {
        if (descriptorPools_[f]) destroyDescriptorPool_(device_, descriptorPools_[f], nullptr);
        descriptorPools_[f] = VK_NULL_HANDLE;
        DestroyBuffer(config_[f]); DestroyBuffer(pathSegment_[f]); DestroyBuffer(pathInfo_[f]);
        DestroyBuffer(pathDraw_[f]); DestroyBuffer(drawTag_[f]); DestroyBuffer(drawMonoid_[f]);
        DestroyBuffer(clipInp_[f]); DestroyBuffer(gradientRamp_[f]);
    }
    DestroyBuffer(bump_); DestroyBuffer(pathBbox_); DestroyBuffer(lineSoup_);
    DestroyBuffer(intersectedBbox_); DestroyBuffer(clipBic_); DestroyBuffer(clipEl_);
    DestroyBuffer(clipBbox_); DestroyBuffer(binHeader_); DestroyBuffer(binData_);
    DestroyBuffer(velloPath_); DestroyBuffer(velloTile_); DestroyBuffer(segCount_);
    DestroyBuffer(velloSegment_); DestroyBuffer(ptcl_); DestroyBuffer(indirect1_);
    DestroyBuffer(indirect2_);

    if (outputView_)   destroyImageView_(device_, outputView_, nullptr);
    if (outputImage_)  destroyImage_(device_, outputImage_, nullptr);
    if (outputMemory_) freeMemory_(device_, outputMemory_, nullptr);
    if (outputSampler_)destroySampler_(device_, outputSampler_, nullptr);
    if (dummyView_)    destroyImageView_(device_, dummyView_, nullptr);
    if (dummyImage_)   destroyImage_(device_, dummyImage_, nullptr);
    if (dummyMemory_)  freeMemory_(device_, dummyMemory_, nullptr);
    if (dummySampler_) destroySampler_(device_, dummySampler_, nullptr);
    outputView_ = VK_NULL_HANDLE; outputImage_ = VK_NULL_HANDLE; outputMemory_ = VK_NULL_HANDLE;
    outputSampler_ = VK_NULL_HANDLE; dummyView_ = VK_NULL_HANDLE; dummyImage_ = VK_NULL_HANDLE;
    dummyMemory_ = VK_NULL_HANDLE; dummySampler_ = VK_NULL_HANDLE;

    ready_ = false;
    device_ = VK_NULL_HANDLE;
}

} // namespace jalium
