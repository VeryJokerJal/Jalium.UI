#include <chrono>
#include "vulkan_vello_compute.h"
#include "vulkan_vello_shaders.h"   // kVello<Stage>Spv / ...SpvSize

#include <vector>
#include <cstring>
#include <algorithm>
#include <cstdlib>

#include "vulkan_environment.h"

namespace jalium {

namespace {

using Res = VelloComputePipeline::Res;
using StageBinding = VelloComputePipeline::StageBinding;

constexpr VkDescriptorType UBO = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
constexpr VkDescriptorType SSB = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
constexpr VkDescriptorType SIMG = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
constexpr VkDescriptorType STIMG = VK_DESCRIPTOR_TYPE_STORAGE_IMAGE;

bool VelloSmallScanEnabled()
{
    static const bool enabled = [] {
        const char* value = std::getenv("JALIUM_VELLO_SMALL_SCAN");
        return value == nullptr || value[0] != '0';
    }();
    return enabled;
}

// Per-stage binding tables. Binding numbers are the register-shifted values
// emitted by dxc (-fvk-{t,s,u}-shift {16,32,48}); only the bindings that
// survive -O3 are listed (verified by spirv-dis on the regenerated 0.10.0
// SPIR-V). The descriptor-set layout for each stage is built EXACTLY from its
// list, so it matches the loaded module.
const StageBinding kPathtagReduceB[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::Scene}, {48, SSB, Res::Reduced},
};
const StageBinding kPathtagReduce2B[] = {
    {16, SSB, Res::Reduced}, {48, SSB, Res::Reduced2},
};
const StageBinding kPathtagScan1B[] = {
    {16, SSB, Res::Reduced}, {17, SSB, Res::Reduced2}, {48, SSB, Res::ReducedScan},
};
const StageBinding kPathtagScanB[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::Scene}, {17, SSB, Res::ReducedScan},
    {48, SSB, Res::TagMonoids},
};
const StageBinding kPathtagScanSmallB[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::Scene}, {17, SSB, Res::Reduced},
    {48, SSB, Res::TagMonoids},
};
const StageBinding kBboxClearB[] = {
    {0, UBO, Res::Config}, {48, SSB, Res::PathBbox},
};
const StageBinding kFlattenB[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::Scene}, {17, SSB, Res::TagMonoids},
    {48, SSB, Res::PathBbox}, {49, SSB, Res::Bump}, {50, SSB, Res::LineSoup},
};
const StageBinding kDrawReduceB[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::Scene}, {48, SSB, Res::DrawReduced},
};
const StageBinding kDrawLeafB[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::Scene}, {17, SSB, Res::DrawReduced},
    {18, SSB, Res::PathBbox}, {48, SSB, Res::DrawMonoid}, {49, SSB, Res::InfoBinData},
    {50, SSB, Res::ClipInp},
};
const StageBinding kClipReduceB[] = {
    {16, SSB, Res::ClipInp}, {17, SSB, Res::PathBbox},
    {48, SSB, Res::ClipBic}, {49, SSB, Res::ClipEl},
};
const StageBinding kClipLeafB[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::ClipInp}, {17, SSB, Res::PathBbox},
    {18, SSB, Res::ClipBic}, {19, SSB, Res::ClipEl},
    {48, SSB, Res::DrawMonoid}, {49, SSB, Res::ClipBbox},
};
const StageBinding kBinningB[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::DrawMonoid}, {17, SSB, Res::PathBbox},
    {18, SSB, Res::ClipBbox}, {48, SSB, Res::DrawBbox}, {49, SSB, Res::Bump},
    {50, SSB, Res::InfoBinData}, {51, SSB, Res::BinHeader},
};
const StageBinding kTileAllocB[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::Scene}, {17, SSB, Res::DrawBbox},
    {48, SSB, Res::Bump}, {49, SSB, Res::VelloPath}, {50, SSB, Res::VelloTile},
};
const StageBinding kPathCountSetupB[] = {
    {48, SSB, Res::Bump}, {49, SSB, Res::Indirect},
};
const StageBinding kPathCountB[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::LineSoup}, {17, SSB, Res::VelloPath},
    {48, SSB, Res::Bump}, {49, SSB, Res::VelloTile}, {50, SSB, Res::SegCount},
};
const StageBinding kBackdropB[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::VelloPath},
    {48, SSB, Res::Bump}, {49, SSB, Res::VelloTile},
};
const StageBinding kCoarseB[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::Scene}, {17, SSB, Res::DrawMonoid},
    {18, SSB, Res::BinHeader}, {19, SSB, Res::InfoBinData}, {20, SSB, Res::VelloPath},
    {48, SSB, Res::VelloTile}, {49, SSB, Res::Bump}, {50, SSB, Res::Ptcl},
};
const StageBinding kPathTilingSetupB[] = {
    {48, SSB, Res::Bump}, {49, SSB, Res::Indirect}, {50, SSB, Res::Ptcl},
};
const StageBinding kPathTilingB[] = {
    {16, SSB, Res::SegCount}, {17, SSB, Res::LineSoup}, {18, SSB, Res::VelloPath},
    {19, SSB, Res::VelloTile}, {48, SSB, Res::Bump}, {49, SSB, Res::VelloSegment},
};
const StageBinding kFineB[] = {
    {0, UBO, Res::Config}, {16, SSB, Res::VelloSegment}, {17, SSB, Res::Ptcl},
    {18, SSB, Res::InfoBinData}, {19, SIMG, Res::RampImage}, {20, SIMG, Res::DummyImage},
    {48, SSB, Res::BlendSpill}, {49, STIMG, Res::OutputImage},
};

// Stage index order == dispatch order.
enum StageIdx : uint32_t {
    S_PathtagReduce = 0, S_PathtagReduce2, S_PathtagScan1, S_PathtagScan,
    S_PathtagScanSmall,
    S_BboxClear, S_Flatten, S_DrawReduce, S_DrawLeaf, S_ClipReduce, S_ClipLeaf,
    S_Binning, S_TileAlloc, S_PathCountSetup, S_PathCount, S_Backdrop, S_Coarse,
    S_PathTilingSetup, S_PathTiling, S_Fine,
};

struct StageDef {
    const uint32_t*     spirv;
    size_t              spirvSize;
    const StageBinding* bindings;
    uint32_t            bindingCount;
};

#define VVC_STAGE(spv, tbl) { spv, spv##Size, tbl, (uint32_t)(sizeof(tbl) / sizeof(tbl[0])) }

const StageDef kStages[VelloComputePipeline::kStageCount] = {
    VVC_STAGE(kVelloPathtagReduceSpv,  kPathtagReduceB),
    VVC_STAGE(kVelloPathtagReduce2Spv, kPathtagReduce2B),
    VVC_STAGE(kVelloPathtagScan1Spv,   kPathtagScan1B),
    VVC_STAGE(kVelloPathtagScanSpv,    kPathtagScanB),
    VVC_STAGE(kVelloPathtagScanSmallSpv, kPathtagScanSmallB),
    VVC_STAGE(kVelloBboxClearSpv,      kBboxClearB),
    VVC_STAGE(kVelloFlattenSpv,        kFlattenB),
    VVC_STAGE(kVelloDrawReduceSpv,     kDrawReduceB),
    VVC_STAGE(kVelloDrawLeafSpv,       kDrawLeafB),
    VVC_STAGE(kVelloClipReduceSpv,     kClipReduceB),
    VVC_STAGE(kVelloClipLeafSpv,       kClipLeafB),
    VVC_STAGE(kVelloBinningSpv,        kBinningB),
    VVC_STAGE(kVelloTileAllocSpv,      kTileAllocB),
    VVC_STAGE(kVelloPathCountSetupSpv, kPathCountSetupB),
    VVC_STAGE(kVelloPathCountSpv,      kPathCountB),
    VVC_STAGE(kVelloBackdropSpv,       kBackdropB),
    VVC_STAGE(kVelloCoarseSpv,         kCoarseB),
    VVC_STAGE(kVelloPathTilingSetupSpv,kPathTilingSetupB),
    VVC_STAGE(kVelloPathTilingSpv,     kPathTilingB),
    VVC_STAGE(kVelloFineSpv,           kFineB),
};
#undef VVC_STAGE

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

namespace {
uint64_t g_vkRecMicros = 0;
uint64_t g_vkRecCount = 0;
struct VkRecTimer {
    std::chrono::steady_clock::time_point t0 = std::chrono::steady_clock::now();
    ~VkRecTimer() {
        g_vkRecMicros += (uint64_t)std::chrono::duration_cast<std::chrono::microseconds>(
                             std::chrono::steady_clock::now() - t0)
                             .count();
        g_vkRecCount++;
    }
};
}  // namespace

bool VelloComputePipeline::CreateBuffer(VkDeviceSize size, VkBufferUsageFlags usage,
                                        VkMemoryPropertyFlags memProps, GpuBuffer& out) {
    if (size == 0) size = 256;
    VkBufferCreateInfo bi{};
    bi.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    bi.size = size;
    bi.usage = usage;
    bi.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    if (createBuffer_(device_, &bi, nullptr, &out.buffer) != VK_SUCCESS) {
        DestroyBuffer(out);
        return false;
    }

    VkMemoryRequirements req{};
    getBufferMemoryRequirements_(device_, out.buffer, &req);
    uint32_t typeIdx = FindMemoryType(req.memoryTypeBits, memProps);
    if (typeIdx == UINT32_MAX) {
        DestroyBuffer(out);
        return false;
    }

    VkMemoryAllocateInfo ai{};
    ai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    ai.allocationSize = req.size;
    ai.memoryTypeIndex = typeIdx;
    if (allocateMemory_(device_, &ai, nullptr, &out.memory) != VK_SUCCESS) {
        DestroyBuffer(out);
        return false;
    }
    if (bindBufferMemory_(device_, out.buffer, out.memory, 0) != VK_SUCCESS) {
        DestroyBuffer(out);
        return false;
    }

    out.capacity = size;
    if (memProps & VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT) {
        if (mapMemory_(device_, out.memory, 0, VK_WHOLE_SIZE, 0, &out.mapped) != VK_SUCCESS) {
            out.mapped = nullptr;
            DestroyBuffer(out);
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
                                        VkMemoryPropertyFlags memProps,
                                        uint32_t retireSlot) {
    if (bytes == 0) bytes = 256;
    if (buf.buffer && buf.capacity >= bytes) return true;

    // Build first: an allocation/bind/map failure must leave the live
    // generation completely untouched.
    GpuBuffer candidate{};
    if (!CreateBuffer(bytes, usage, memProps, candidate)) {
        DestroyBuffer(candidate);
        return false;
    }

    if (buf.buffer || buf.memory) {
        if (retireSlot < kFramesInFlight) {
            try {
                retiredBuffers_[retireSlot].push_back(buf);
            } catch (...) {
                DestroyBuffer(candidate);
                return false;
            }
        } else {
            // Per-frame input buffers are replaced only after their own slot
            // fence and descriptor-pool reset, so they need no cross-slot hold.
            DestroyBuffer(buf);
        }
    }
    buf = candidate;
    return true;
}

void VelloComputePipeline::DestroyRetiredImage(RetiredImage& image) {
    if (image.view)   destroyImageView_(device_, image.view, nullptr);
    if (image.image)  destroyImage_(device_, image.image, nullptr);
    if (image.memory) freeMemory_(device_, image.memory, nullptr);
    image = {};
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
    cmdCopyBufferToImage_       = (PFN_vkCmdCopyBufferToImage)      need(load("vkCmdCopyBufferToImage"));
    if (!ok) return false;

    if (!CreatePipelines()) return false;
    if (!CreateOutputSampler()) return false;
    if (!CreateDummyImage()) return false;

    // Per-frame transient descriptor pools. Each Record allocates a fresh
    // 20-set group (the pool is reset once per frame in PrepareFrameSlot, so
    // groups from earlier sub-scenes of the SAME frame stay valid while their
    // GPU reads are still queued); size everything for kMaxRecordsPerFrame
    // groups.
    VkDescriptorPoolSize poolSizes[] = {
        { VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, 128 * kMaxRecordsPerFrame },
        { VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER,  24 * kMaxRecordsPerFrame },
        { VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE,    4 * kMaxRecordsPerFrame },
        { VK_DESCRIPTOR_TYPE_STORAGE_IMAGE,    4 * kMaxRecordsPerFrame },
    };
    for (uint32_t f = 0; f < kFramesInFlight; ++f) {
        VkDescriptorPoolCreateInfo pi{};
        pi.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
        pi.maxSets = kStageCount * kMaxRecordsPerFrame;
        pi.poolSizeCount = (uint32_t)(sizeof(poolSizes) / sizeof(poolSizes[0]));
        pi.pPoolSizes = poolSizes;
        if (createDescriptorPool_(device_, &pi, nullptr, &descriptorPools_[f]) != VK_SUCCESS) {
            return false;
        }
    }

    const VkBufferUsageFlags storageDst =
        VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT;
    const VkMemoryPropertyFlags devLocal = VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT;
    if (!CreateBuffer(sizeof(VelloBumpAllocators), storageDst, devLocal, bump_)) return false;
    const VkBufferUsageFlags indirectUsage = storageDst | VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT;
    if (!CreateBuffer(64, indirectUsage, devLocal, indirect_)) return false;

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

bool VelloComputePipeline::EnsureOutputImage(uint32_t width, uint32_t height,
                                             uint32_t retireSlot) {
    if (width == 0) width = 1;
    if (height == 0) height = 1;
    if (outputImage_ && outputWidth_ == width && outputHeight_ == height) return true;

    // Transactional replacement: keep the current output (and every descriptor
    // that references it) valid unless the complete image+memory+view candidate
    // succeeds.
    RetiredImage candidate{};

    VkImageCreateInfo ii{};
    ii.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    ii.imageType = VK_IMAGE_TYPE_2D;
    ii.format = VK_FORMAT_R8G8B8A8_UNORM;   // fine writes premultiplied RGBA8
    ii.extent = { width, height, 1 };
    ii.mipLevels = 1;
    ii.arrayLayers = 1;
    ii.samples = VK_SAMPLE_COUNT_1_BIT;
    ii.tiling = VK_IMAGE_TILING_OPTIMAL;
    // STORAGE: fine stage writes it; SAMPLED: composite reads it; TRANSFER_DST:
    // the per-frame vkCmdClearColorImage in Record().
    ii.usage = VK_IMAGE_USAGE_STORAGE_BIT | VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT;
    ii.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    if (createImage_(device_, &ii, nullptr, &candidate.image) != VK_SUCCESS) {
        DestroyRetiredImage(candidate);
        return false;
    }

    VkMemoryRequirements req{};
    getImageMemoryRequirements_(device_, candidate.image, &req);
    uint32_t typeIdx = FindMemoryType(req.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    if (typeIdx == UINT32_MAX) {
        DestroyRetiredImage(candidate);
        return false;
    }
    VkMemoryAllocateInfo ai{};
    ai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    ai.allocationSize = req.size;
    ai.memoryTypeIndex = typeIdx;
    if (allocateMemory_(device_, &ai, nullptr, &candidate.memory) != VK_SUCCESS) {
        DestroyRetiredImage(candidate);
        return false;
    }
    if (bindImageMemory_(device_, candidate.image, candidate.memory, 0) != VK_SUCCESS) {
        DestroyRetiredImage(candidate);
        return false;
    }

    VkImageViewCreateInfo vi{};
    vi.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    vi.image = candidate.image;
    vi.viewType = VK_IMAGE_VIEW_TYPE_2D;
    vi.format = VK_FORMAT_R8G8B8A8_UNORM;
    vi.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
    if (createImageView_(device_, &vi, nullptr, &candidate.view) != VK_SUCCESS) {
        DestroyRetiredImage(candidate);
        return false;
    }

    if (outputImage_ || outputMemory_ || outputView_) {
        RetiredImage old{ outputImage_, outputMemory_, outputView_ };
        if (retireSlot < kFramesInFlight) {
            try {
                retiredImages_[retireSlot].push_back(old);
            } catch (...) {
                DestroyRetiredImage(candidate);
                return false;
            }
        } else {
            DestroyRetiredImage(old);
        }
    }

    outputImage_ = candidate.image;
    outputMemory_ = candidate.memory;
    outputView_ = candidate.view;
    outputLayout_ = VK_IMAGE_LAYOUT_UNDEFINED;
    outputWidth_ = width;
    outputHeight_ = height;
    return true;
}

bool VelloComputePipeline::EnsureRampImage(uint32_t rows, uint32_t retireSlot) {
    if (rows == 0) rows = 1;
    if (rampImage_ && rampRows_ >= rows) return true;

    uint32_t newRows = std::max(rows, std::max(rampRows_ * 2, 4u));

    RetiredImage candidate{};
    VkImageCreateInfo ii{};
    ii.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    ii.imageType = VK_IMAGE_TYPE_2D;
    ii.format = VK_FORMAT_R8G8B8A8_UNORM;
    ii.extent = { kVelloRampWidth, newRows, 1 };
    ii.mipLevels = 1;
    ii.arrayLayers = 1;
    ii.samples = VK_SAMPLE_COUNT_1_BIT;
    ii.tiling = VK_IMAGE_TILING_OPTIMAL;
    ii.usage = VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT;
    ii.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    if (createImage_(device_, &ii, nullptr, &candidate.image) != VK_SUCCESS) {
        DestroyRetiredImage(candidate);
        return false;
    }
    VkMemoryRequirements req{};
    getImageMemoryRequirements_(device_, candidate.image, &req);
    uint32_t typeIdx = FindMemoryType(req.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    if (typeIdx == UINT32_MAX) {
        DestroyRetiredImage(candidate);
        return false;
    }
    VkMemoryAllocateInfo ai{};
    ai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    ai.allocationSize = req.size;
    ai.memoryTypeIndex = typeIdx;
    if (allocateMemory_(device_, &ai, nullptr, &candidate.memory) != VK_SUCCESS) {
        DestroyRetiredImage(candidate);
        return false;
    }
    if (bindImageMemory_(device_, candidate.image, candidate.memory, 0) != VK_SUCCESS) {
        DestroyRetiredImage(candidate);
        return false;
    }
    VkImageViewCreateInfo vi{};
    vi.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    vi.image = candidate.image;
    vi.viewType = VK_IMAGE_VIEW_TYPE_2D;
    vi.format = VK_FORMAT_R8G8B8A8_UNORM;
    vi.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
    if (createImageView_(device_, &vi, nullptr, &candidate.view) != VK_SUCCESS) {
        DestroyRetiredImage(candidate);
        return false;
    }

    if (rampImage_ || rampMemory_ || rampView_) {
        RetiredImage old{ rampImage_, rampMemory_, rampView_ };
        if (retireSlot < kFramesInFlight) {
            try {
                retiredImages_[retireSlot].push_back(old);
            } catch (...) {
                DestroyRetiredImage(candidate);
                return false;
            }
        } else {
            DestroyRetiredImage(old);
        }
    }

    rampImage_ = candidate.image;
    rampMemory_ = candidate.memory;
    rampView_ = candidate.view;
    rampRows_ = newRows;
    rampImageInitialized_ = false;
    return true;
}

bool VelloComputePipeline::EnsureScratch(const VelloRenderInfo& ri, uint32_t retireSlot) {
    const VkBufferUsageFlags storage = VK_BUFFER_USAGE_STORAGE_BUFFER_BIT;
    const VkMemoryPropertyFlags devLocal = VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT;
    bool ok = true;
    auto ensure = [&](GpuBuffer& b, uint64_t bytes) {
        ok = ok && EnsureBuffer(b, bytes, storage, devLocal, retireSlot);
    };
    ensure(reduced_, (uint64_t)ri.reducedSize * kVelloStrideTagMonoid);
    ensure(reduced2_, (uint64_t)ri.reduced2Size * kVelloStrideTagMonoid);
    ensure(reducedScan_, (uint64_t)ri.reducedScanSize * kVelloStrideTagMonoid);
    ensure(tagMonoids_, (uint64_t)ri.tagMonoidsSize * kVelloStrideTagMonoid);
    ensure(pathBbox_, (uint64_t)ri.pathBboxSize * kVelloStridePathBbox);
    ensure(lineSoup_, (uint64_t)ri.lineSoupSize * kVelloStrideLineSoup);
    ensure(drawReduced_, (uint64_t)ri.drawReducedSize * kVelloStrideDrawMonoid);
    ensure(drawMonoid_, (uint64_t)ri.drawMonoidSize * kVelloStrideDrawMonoid);
    ensure(infoBinData_, (uint64_t)ri.infoBinDataSize * 4);
    ensure(clipInp_, (uint64_t)ri.clipInpSize * kVelloStrideClipInp);
    ensure(clipBic_, (uint64_t)ri.clipBicSize * kVelloStrideClipBic);
    ensure(clipEl_, (uint64_t)ri.clipElSize * kVelloStrideClipEl);
    ensure(clipBbox_, (uint64_t)ri.clipBboxSize * kVelloStrideClipBbox);
    ensure(drawBbox_, (uint64_t)ri.drawBboxSize * kVelloStrideDrawBbox);
    ensure(binHeader_, (uint64_t)ri.binHeaderSize * kVelloStrideBinHeader);
    ensure(velloPath_, (uint64_t)ri.pathSize * kVelloStridePath);
    ensure(velloTile_, (uint64_t)ri.tileSize * kVelloStrideTile);
    ensure(segCount_, (uint64_t)ri.segCountSize * kVelloStrideSegCount);
    ensure(velloSegment_, (uint64_t)ri.segmentSize * kVelloStrideSegment);
    ensure(ptcl_, (uint64_t)ri.ptclSize * 4);
    ensure(blendSpill_, (uint64_t)ri.blendSpillSize * 4);
    return ok;
}

void VelloComputePipeline::RetireInputsForReuse(uint32_t frameIdx, uint32_t retireSlot,
                                                bool willUploadRamps) {
    if (frameIdx >= kFramesInFlight || recordsThisFrame_[frameIdx] == 0) return;
    auto retire = [&](GpuBuffer& b) {
        if (b.buffer == VK_NULL_HANDLE && b.memory == VK_NULL_HANDLE) return;
        if (retireSlot < kFramesInFlight) {
            try {
                retiredBuffers_[retireSlot].push_back(b);
                b = GpuBuffer{};
                return;
            } catch (...) {
                // fall through to immediate destroy — never leave b dangling
            }
        }
        DestroyBuffer(b);
    };
    // scene/config/ramp staging now live in the per-slot arena (fresh slices
    // per Record) -- nothing to retire here; the arena itself is reset per
    // frame in PrepareFrameSlot and retired wholesale on growth.
    (void)retire;
    // The ramp IMAGE is also rewritten in place by the upload copy: retiring
    // it forces EnsureRampImage to build a fresh one, so the earlier
    // sub-scene's queued fine reads keep their texels. Skip when this
    // sub-scene has no ramps — with no gradient draws it never samples the
    // image, and the stale binding is harmless.
    if (willUploadRamps && rampImage_ != VK_NULL_HANDLE) {
        RetiredImage old{ rampImage_, rampMemory_, rampView_ };
        if (retireSlot < kFramesInFlight) {
            try {
                retiredImages_[retireSlot].push_back(old);
                old = RetiredImage{};
            } catch (...) {
            }
        }
        if (old.image != VK_NULL_HANDLE || old.memory != VK_NULL_HANDLE ||
            old.view != VK_NULL_HANDLE) {
            DestroyRetiredImage(old);
        }
        rampImage_ = VK_NULL_HANDLE;
        rampMemory_ = VK_NULL_HANDLE;
        rampView_ = VK_NULL_HANDLE;
        rampRows_ = 0;
        rampImageInitialized_ = false;
    }
}

bool VelloComputePipeline::ArenaAlloc(uint32_t frameIdx, VkDeviceSize bytes,
                                      VkDeviceSize& outOffset) {
    FrameArena& a = arena_[frameIdx % kFramesInFlight];
    VkDeviceSize off = ((a.cursor + kArenaAlign - 1) / kArenaAlign) * kArenaAlign;
    if (a.buf.buffer == VK_NULL_HANDLE || off + bytes > a.buf.capacity) {
        const VkDeviceSize kMinArena = 1048576;
        VkDeviceSize want = off + bytes;
        if (want < kMinArena) want = kMinArena;
        VkDeviceSize newCap = a.buf.capacity ? a.buf.capacity : kMinArena;
        while (newCap < want) newCap = newCap * 2;
        // Retire the old arena into THIS slot: earlier sub-scenes of this
        // frame recorded descriptor sets / copies against it.
        if (!EnsureBuffer(a.buf, newCap,
                          VK_BUFFER_USAGE_STORAGE_BUFFER_BIT |
                              VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT |
                              VK_BUFFER_USAGE_TRANSFER_SRC_BIT,
                          VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT |
                              VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
                          frameIdx % kFramesInFlight)) {
            return false;
        }
        off = 0;
        a.cursor = 0;
    }
    outOffset = off;
    a.cursor = off + bytes;
    return true;
}

bool VelloComputePipeline::UploadInputs(const VelloSubScene& sub, uint32_t frameIdx) {
    // One bump allocation per input from the per-slot arena -- no per-Record
    // buffer creation (2-3 vkAllocateMemory x ~55 sub-scenes made EndDraw the
    // scrolling bottleneck). The slices' offsets feed the descriptor writes
    // (scene / config) and the ramp copy (bufferOffset).
    const uint64_t sceneBytes4 = (uint64_t)sub.packed.data.size() * 4;
    const uint64_t sceneBytes = sceneBytes4 > 256 ? sceneBytes4 : 256;
    const uint64_t rampBytes = (uint64_t)sub.rampData.size() * 4;
    if (!ArenaAlloc(frameIdx, sceneBytes, curSceneOffset_)) return false;
    if (!ArenaAlloc(frameIdx, 256, curConfigOffset_)) return false;
    if (rampBytes > 0) {
        if (!ArenaAlloc(frameIdx, rampBytes, curRampOffset_)) return false;
    } else {
        curRampOffset_ = 0;
    }
    curSceneRange_ = sceneBytes;

    FrameArena& a = arena_[frameIdx % kFramesInFlight];
    if (!a.buf.mapped) return false;
    uint8_t* base = static_cast<uint8_t*>(a.buf.mapped);
    std::memcpy(base + curSceneOffset_, sub.packed.data.data(), (size_t)sceneBytes4);
    std::memset(base + curConfigOffset_, 0, 256);
    std::memcpy(base + curConfigOffset_, &sub.ri.config, sizeof(VelloConfig));
    if (rampBytes > 0) {
        std::memcpy(base + curRampOffset_, sub.rampData.data(), (size_t)rampBytes);
    }
    return true;
}

VkBuffer VelloComputePipeline::BufferForRes(Res res, uint32_t frameIdx) const {
    switch (res) {
        case Res::Config:       return config_[frameIdx].buffer;
        case Res::Scene:        return scene_[frameIdx].buffer;
        case Res::Bump:         return bump_.buffer;
        case Res::Reduced:      return reduced_.buffer;
        case Res::Reduced2:     return reduced2_.buffer;
        case Res::ReducedScan:  return reducedScan_.buffer;
        case Res::TagMonoids:   return tagMonoids_.buffer;
        case Res::PathBbox:     return pathBbox_.buffer;
        case Res::LineSoup:     return lineSoup_.buffer;
        case Res::DrawReduced:  return drawReduced_.buffer;
        case Res::DrawMonoid:   return drawMonoid_.buffer;
        case Res::InfoBinData:  return infoBinData_.buffer;
        case Res::ClipInp:      return clipInp_.buffer;
        case Res::ClipBic:      return clipBic_.buffer;
        case Res::ClipEl:       return clipEl_.buffer;
        case Res::ClipBbox:     return clipBbox_.buffer;
        case Res::DrawBbox:     return drawBbox_.buffer;
        case Res::BinHeader:    return binHeader_.buffer;
        case Res::VelloPath:    return velloPath_.buffer;
        case Res::VelloTile:    return velloTile_.buffer;
        case Res::SegCount:     return segCount_.buffer;
        case Res::VelloSegment: return velloSegment_.buffer;
        case Res::Ptcl:         return ptcl_.buffer;
        case Res::BlendSpill:   return blendSpill_.buffer;
        case Res::Indirect:     return indirect_.buffer;
        default:                return VK_NULL_HANDLE;
    }
}

bool VelloComputePipeline::BuildDescriptorSets(uint32_t frameIdx, const VelloRenderInfo& ri,
                                               uint32_t sceneWords) {
    (void)ri;
    (void)sceneWords;
    // NO pool reset here: the pool is reset once per frame in PrepareFrameSlot.
    // Resetting per Record would invalidate the descriptor groups of earlier
    // sub-scenes in the SAME frame whose GPU reads are still queued in this
    // command buffer. Each call just allocates a fresh 20-set group.

    VkDescriptorSetLayout layouts[kStageCount];
    for (uint32_t s = 0; s < kStageCount; ++s) layouts[s] = setLayouts_[s];

    VkDescriptorSetAllocateInfo ai{};
    ai.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
    ai.descriptorPool = descriptorPools_[frameIdx];
    ai.descriptorSetCount = kStageCount;
    ai.pSetLayouts = layouts;
    if (allocateDescriptorSets_(device_, &ai, stageSets_) != VK_SUCCESS) {
        return false;
    }

    std::vector<VkWriteDescriptorSet> writes;
    std::vector<VkDescriptorBufferInfo> bufInfos;
    std::vector<VkDescriptorImageInfo> imgInfos;
    writes.reserve(kStageCount * 10);
    bufInfos.reserve(kStageCount * 10);
    imgInfos.reserve(8);

    for (uint32_t s = 0; s < kStageCount; ++s) {
        const StageDef& def = kStages[s];
        for (uint32_t i = 0; i < def.bindingCount; ++i) {
            const StageBinding& sb = def.bindings[i];
            VkWriteDescriptorSet w{};
            w.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
            w.dstSet = stageSets_[s];
            w.dstBinding = sb.binding;
            w.descriptorCount = 1;
            w.descriptorType = sb.type;
            if (sb.type == SIMG || sb.type == STIMG) {
                VkDescriptorImageInfo ii{};
                if (sb.res == Res::RampImage) {
                    ii.imageView = rampView_;
                    ii.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
                } else if (sb.res == Res::DummyImage) {
                    ii.imageView = dummyView_;
                    ii.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
                } else {  // OutputImage
                    ii.imageView = outputView_;
                    ii.imageLayout = VK_IMAGE_LAYOUT_GENERAL;
                }
                imgInfos.push_back(ii);
                w.pImageInfo = &imgInfos.back();
            } else {
                VkDescriptorBufferInfo bi{};
                if (sb.res == Res::Scene) {
                    bi.buffer = arena_[frameIdx % kFramesInFlight].buf.buffer;
                    bi.offset = curSceneOffset_;
                    bi.range = curSceneRange_;
                } else if (sb.res == Res::Config) {
                    bi.buffer = arena_[frameIdx % kFramesInFlight].buf.buffer;
                    bi.offset = curConfigOffset_;
                    bi.range = 256;
                } else {
                    bi.buffer = BufferForRes(sb.res, frameIdx);
                    bi.offset = 0;
                    bi.range = VK_WHOLE_SIZE;
                }
                if (bi.buffer == VK_NULL_HANDLE) return false;
                bufInfos.push_back(bi);
                w.pBufferInfo = &bufInfos.back();
            }
            writes.push_back(w);
        }
    }
    updateDescriptorSets_(device_, (uint32_t)writes.size(), writes.data(), 0, nullptr);
    return true;
}

bool VelloComputePipeline::PrepareFrameSlot(uint32_t frameIdx) {
    {
        static int s_perf = -1;
        if (s_perf < 0) {
            char buf[8];
            s_perf = (ReadVulkanEnvironment("JALIUM_VELLO_PERF", buf) && buf[0] != '0') ? 1 : 0;
        }
        if (frameIdx < kFramesInFlight) arena_[frameIdx].cursor = 0;
        if (s_perf && frameIdx < kFramesInFlight && recordsThisFrame_[frameIdx] > 0) {
            static uint32_t s_frames = 0, s_records = 0;
            s_frames++;
            s_records += recordsThisFrame_[frameIdx];
            if (s_frames >= 120) {
                std::fprintf(stderr, "[VkVello] subscenes/frame=%.1f (cap %u) rec=%.0fus", 
                             (double)s_records / (double)s_frames, kMaxRecordsPerFrame,
                             g_vkRecCount ? (double)g_vkRecMicros / (double)g_vkRecCount : 0.0);
                g_vkRecMicros = 0;
                g_vkRecCount = 0;
                std::fputc(10, stderr);
                std::fflush(stderr);
                s_frames = s_records = 0;
            }
        }
    }
    if (!ready_ || frameIdx >= kFramesInFlight) return false;
    if (resetDescriptorPool_(device_, descriptorPools_[frameIdx], 0) != VK_SUCCESS) {
        frameSlotPrepared_[frameIdx] = false;
        return false;
    }
    for (auto& b : retiredBuffers_[frameIdx]) DestroyBuffer(b);
    retiredBuffers_[frameIdx].clear();
    for (auto& im : retiredImages_[frameIdx]) DestroyRetiredImage(im);
    retiredImages_[frameIdx].clear();
    frameSlotPrepared_[frameIdx] = true;
    recordsThisFrame_[frameIdx] = 0;
    return true;
}

bool VelloComputePipeline::Record(VkCommandBuffer cmd, const VelloSubScene& sub,
                                  uint32_t frameIdx) {
    if (!ready_ || frameIdx >= kFramesInFlight) return false;
    if (!frameSlotPrepared_[frameIdx]) return false;
    if (recordsThisFrame_[frameIdx] >= kMaxRecordsPerFrame) return false;

    if (sub.packed.data.empty()) return false;
    if (sub.viewportW == 0 || sub.viewportH == 0) return false;
    const VelloRenderInfo& ri = sub.ri;

    // Retire into THIS slot's bucket, not the other in-flight one. A frame now
    // issues many Records (one per sub-scene), so a resource replaced by the
    // Nth sub-scene may still be referenced by commands the 1st..N-1th recorded
    // into this frame's still-unsubmitted command buffer. Only this slot's own
    // fence proves those have executed, and that is exactly what gates
    // PrepareFrameSlot(frameIdx) on the next cycle.
    const uint32_t retireSlot = frameIdx;
    VkRecTimer recTimer_;
    // Second and later sub-scenes of this frame: never rewrite the previous
    // sub-scene's host-visible inputs (its GPU reads are queued in this very
    // command buffer) — retire them and allocate fresh ones.
    RetireInputsForReuse(frameIdx, retireSlot, sub.rampCount > 0);
    // Render only the sub-scene's region: its packed transforms are already
    // rebased by -region.origin, so the output image is region-sized and the
    // render target composites it at that origin.
    const VelloRenderRegion& region = sub.packed.region;
    if (region.Empty()) return false;
    // Grow-only: the sub-scene writes region.width x region.height texels into
    // the image's top-left corner. Resizing per sub-scene would retire an image
    // that descriptor sets from earlier sub-scenes in THIS frame still
    // reference, so the image only ever grows and the composite maps just the
    // region's texels (see CompositeVelloOutput's viewport trick).
    if (!EnsureOutputImage(std::max(region.width, outputWidth_),
                           std::max(region.height, outputHeight_), retireSlot)) {
        return false;
    }
    if (!EnsureRampImage(std::max(sub.rampCount, 1u), retireSlot)) return false;
    if (!EnsureScratch(ri, retireSlot)) return false;
    if (!UploadInputs(sub, frameIdx)) return false;
    if (!BuildDescriptorSets(frameIdx, ri, (uint32_t)sub.packed.data.size())) return false;
    ++recordsThisFrame_[frameIdx];

    // ------------------------------------------------------------------
    // Leading barrier: serialize against the previous frame's compute reads/
    // writes, the composite's fragment reads and the indirect dispatch reads
    // (the scratch and indirect buffers are shared across frames).
    // ------------------------------------------------------------------
    {
        VkMemoryBarrier mb{};
        mb.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
        mb.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT | VK_ACCESS_INDIRECT_COMMAND_READ_BIT;
        mb.dstAccessMask = VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT |
                           VK_ACCESS_TRANSFER_WRITE_BIT;

        VkImageMemoryBarrier imgs[3]{};
        uint32_t imgCount = 0;

        // Output: whatever the previous composite left -> GENERAL for fine.
        VkImageMemoryBarrier& ob = imgs[imgCount++];
        ob.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        ob.srcAccessMask = VK_ACCESS_SHADER_READ_BIT;
        ob.dstAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
        ob.oldLayout = outputLayout_;
        ob.newLayout = VK_IMAGE_LAYOUT_GENERAL;
        ob.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        ob.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        ob.image = outputImage_;
        ob.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };

        // Dummy atlas: (re-)assert SHADER_READ_ONLY (contents are irrelevant).
        VkImageMemoryBarrier& db = imgs[imgCount++];
        db.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        db.srcAccessMask = 0;
        db.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
        db.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        db.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        db.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        db.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        db.image = dummyImage_;
        db.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };

        // Ramp image: to TRANSFER_DST when uploading this frame, else make
        // sure it is SHADER_READ_ONLY at least once.
        VkImageMemoryBarrier& rb = imgs[imgCount++];
        rb.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        rb.srcAccessMask = rampImageInitialized_ ? VK_ACCESS_SHADER_READ_BIT : 0;
        rb.oldLayout = rampImageInitialized_ ? VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
                                             : VK_IMAGE_LAYOUT_UNDEFINED;
        if (sub.rampCount > 0) {
            rb.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
            rb.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
        } else {
            rb.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
            rb.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        }
        rb.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        rb.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        rb.image = rampImage_;
        rb.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };

        cmdPipelineBarrier_(cmd,
                            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT |
                                VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT |
                                VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT,
                            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT |
                                VK_PIPELINE_STAGE_TRANSFER_BIT,
                            0, 1, &mb, 0, nullptr, imgCount, imgs);
    }

    // Fine writes the complete region, including empty tiles and allocator
    // failure. Do not clear the (possibly much larger) reusable output image.
    cmdFillBuffer_(cmd, bump_.buffer, 0, VK_WHOLE_SIZE, 0);

    // Upload gradient ramps.
    if (sub.rampCount > 0) {
        VkBufferImageCopy region{};
        region.bufferOffset = curRampOffset_;
        region.bufferRowLength = kVelloRampWidth;
        region.bufferImageHeight = sub.rampCount;
        region.imageSubresource = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1 };
        region.imageOffset = { 0, 0, 0 };
        region.imageExtent = { kVelloRampWidth, sub.rampCount, 1 };
        cmdCopyBufferToImage_(cmd, arena_[frameIdx % kFramesInFlight].buf.buffer, rampImage_,
                              VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &region);

        VkImageMemoryBarrier rb{};
        rb.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        rb.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
        rb.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
        rb.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
        rb.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        rb.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        rb.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        rb.image = rampImage_;
        rb.subresourceRange = { VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1 };
        cmdPipelineBarrier_(cmd, VK_PIPELINE_STAGE_TRANSFER_BIT,
                            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, 0, nullptr, 0, nullptr,
                            1, &rb);
    }
    rampImageInitialized_ = true;

    // Transfer writes (bump zero) -> compute.
    {
        VkMemoryBarrier mb{};
        mb.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
        mb.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
        mb.dstAccessMask = VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_SHADER_WRITE_BIT;
        cmdPipelineBarrier_(cmd, VK_PIPELINE_STAGE_TRANSFER_BIT,
                            VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, 1, &mb, 0, nullptr, 0,
                            nullptr);
    }

    // ------------------------------------------------------------------
    // The dispatch graph
    // ------------------------------------------------------------------
    auto bindAndDispatch = [&](uint32_t stage, uint32_t x, uint32_t y, uint32_t z) {
        cmdBindPipeline_(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipelines_[stage]);
        cmdBindDescriptorSets_(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipelineLayouts_[stage],
                               0, 1, &stageSets_[stage], 0, nullptr);
        cmdDispatch_(cmd, x, y, z);
        ComputeBarrier(cmdPipelineBarrier_, cmd);
    };

    const bool useSmallPathScan = !ri.useLargePathScan && VelloSmallScanEnabled();
    // Small scan's first workgroup has an identity parent; only subsequent
    // groups read reduced[]. The large permutation still needs its chain.
    if (!useSmallPathScan || ri.pathtagReduceWgs > 1) {
        bindAndDispatch(S_PathtagReduce, ri.pathtagReduceWgs, 1, 1);
    }
    if (!useSmallPathScan) {
        bindAndDispatch(S_PathtagReduce2, ri.pathtagReduce2Wgs, 1, 1);
        bindAndDispatch(S_PathtagScan1, ri.pathtagScan1Wgs, 1, 1);
        bindAndDispatch(S_PathtagScan, ri.pathtagScanWgs, 1, 1);
    } else {
        bindAndDispatch(S_PathtagScanSmall, ri.pathtagScanWgs, 1, 1);
    }
    bindAndDispatch(S_BboxClear, ri.bboxClearWgs, 1, 1);
    bindAndDispatch(S_Flatten, ri.flattenWgs, 1, 1);
    // draw_leaf likewise only reads reductions for preceding workgroups.
    if (ri.drawReduceWgs > 1) {
        bindAndDispatch(S_DrawReduce, ri.drawReduceWgs, 1, 1);
    }
    bindAndDispatch(S_DrawLeaf, ri.drawReduceWgs, 1, 1);
    if (ri.clipReduceWgs > 0) {
        bindAndDispatch(S_ClipReduce, ri.clipReduceWgs, 1, 1);
    }
    if (ri.clipLeafWgs > 0) {
        bindAndDispatch(S_ClipLeaf, ri.clipLeafWgs, 1, 1);
    }
    bindAndDispatch(S_Binning, ri.binningWgs, 1, 1);
    bindAndDispatch(S_TileAlloc, ri.tileAllocWgs, 1, 1);

    auto indirectBarrier = [&]() {
        VkMemoryBarrier mb{};
        mb.sType = VK_STRUCTURE_TYPE_MEMORY_BARRIER;
        mb.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT;
        mb.dstAccessMask = VK_ACCESS_INDIRECT_COMMAND_READ_BIT;
        cmdPipelineBarrier_(cmd, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                            VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT, 0, 1, &mb, 0, nullptr, 0,
                            nullptr);
    };

    // path_count_setup -> path_count (indirect)
    bindAndDispatch(S_PathCountSetup, 1, 1, 1);
    indirectBarrier();
    cmdBindPipeline_(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipelines_[S_PathCount]);
    cmdBindDescriptorSets_(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipelineLayouts_[S_PathCount],
                           0, 1, &stageSets_[S_PathCount], 0, nullptr);
    cmdDispatchIndirect_(cmd, indirect_.buffer, 0);
    ComputeBarrier(cmdPipelineBarrier_, cmd);

    bindAndDispatch(S_Backdrop, ri.backdropWgs, 1, 1);
    bindAndDispatch(S_Coarse, ri.widthInBins, ri.heightInBins, 1);

    // path_tiling_setup -> path_tiling (indirect)
    bindAndDispatch(S_PathTilingSetup, 1, 1, 1);
    indirectBarrier();
    cmdBindPipeline_(cmd, VK_PIPELINE_BIND_POINT_COMPUTE, pipelines_[S_PathTiling]);
    cmdBindDescriptorSets_(cmd, VK_PIPELINE_BIND_POINT_COMPUTE,
                           pipelineLayouts_[S_PathTiling], 0, 1, &stageSets_[S_PathTiling], 0,
                           nullptr);
    cmdDispatchIndirect_(cmd, indirect_.buffer, 0);
    ComputeBarrier(cmdPipelineBarrier_, cmd);

    bindAndDispatch(S_Fine, ri.config.width_in_tiles, ri.config.height_in_tiles, 1);

    // Output -> SHADER_READ_ONLY for the composite (carries the memory
    // dependency for fine's writes).
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
                            VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, 0, 0, nullptr, 0, nullptr,
                            1, &ob);
    }
    outputLayout_ = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

    return true;
}

void VelloComputePipeline::Destroy() {
    if (!device_) return;

    for (uint32_t s = 0; s < kStageCount; ++s) {
        if (pipelines_[s]) { destroyPipeline_(device_, pipelines_[s], nullptr); pipelines_[s] = VK_NULL_HANDLE; }
        if (pipelineLayouts_[s]) { destroyPipelineLayout_(device_, pipelineLayouts_[s], nullptr); pipelineLayouts_[s] = VK_NULL_HANDLE; }
        if (setLayouts_[s]) { destroyDescriptorSetLayout_(device_, setLayouts_[s], nullptr); setLayouts_[s] = VK_NULL_HANDLE; }
        if (modules_[s]) { destroyShaderModule_(device_, modules_[s], nullptr); modules_[s] = VK_NULL_HANDLE; }
    }
    for (uint32_t f = 0; f < kFramesInFlight; ++f) {
        if (descriptorPools_[f]) { destroyDescriptorPool_(device_, descriptorPools_[f], nullptr); descriptorPools_[f] = VK_NULL_HANDLE; }
        for (auto& b : retiredBuffers_[f]) DestroyBuffer(b);
        retiredBuffers_[f].clear();
        for (auto& im : retiredImages_[f]) DestroyRetiredImage(im);
        retiredImages_[f].clear();
        DestroyBuffer(config_[f]);
        DestroyBuffer(scene_[f]);
        DestroyBuffer(rampStaging_[f]);
        DestroyBuffer(arena_[f].buf);
        arena_[f].cursor = 0;
        frameSlotPrepared_[f] = false;
    }

    DestroyBuffer(bump_);
    DestroyBuffer(reduced_);
    DestroyBuffer(reduced2_);
    DestroyBuffer(reducedScan_);
    DestroyBuffer(tagMonoids_);
    DestroyBuffer(pathBbox_);
    DestroyBuffer(lineSoup_);
    DestroyBuffer(drawReduced_);
    DestroyBuffer(drawMonoid_);
    DestroyBuffer(infoBinData_);
    DestroyBuffer(clipInp_);
    DestroyBuffer(clipBic_);
    DestroyBuffer(clipEl_);
    DestroyBuffer(clipBbox_);
    DestroyBuffer(drawBbox_);
    DestroyBuffer(binHeader_);
    DestroyBuffer(velloPath_);
    DestroyBuffer(velloTile_);
    DestroyBuffer(segCount_);
    DestroyBuffer(velloSegment_);
    DestroyBuffer(ptcl_);
    DestroyBuffer(blendSpill_);
    DestroyBuffer(indirect_);

    if (rampView_) { destroyImageView_(device_, rampView_, nullptr); rampView_ = VK_NULL_HANDLE; }
    if (rampImage_) { destroyImage_(device_, rampImage_, nullptr); rampImage_ = VK_NULL_HANDLE; }
    if (rampMemory_) { freeMemory_(device_, rampMemory_, nullptr); rampMemory_ = VK_NULL_HANDLE; }
    rampRows_ = 0;
    rampImageInitialized_ = false;

    if (outputView_) { destroyImageView_(device_, outputView_, nullptr); outputView_ = VK_NULL_HANDLE; }
    if (outputImage_) { destroyImage_(device_, outputImage_, nullptr); outputImage_ = VK_NULL_HANDLE; }
    if (outputMemory_) { freeMemory_(device_, outputMemory_, nullptr); outputMemory_ = VK_NULL_HANDLE; }
    outputLayout_ = VK_IMAGE_LAYOUT_UNDEFINED;
    outputWidth_ = outputHeight_ = 0;

    if (outputSampler_) { destroySampler_(device_, outputSampler_, nullptr); outputSampler_ = VK_NULL_HANDLE; }
    if (dummySampler_) { destroySampler_(device_, dummySampler_, nullptr); dummySampler_ = VK_NULL_HANDLE; }
    if (dummyView_) { destroyImageView_(device_, dummyView_, nullptr); dummyView_ = VK_NULL_HANDLE; }
    if (dummyImage_) { destroyImage_(device_, dummyImage_, nullptr); dummyImage_ = VK_NULL_HANDLE; }
    if (dummyMemory_) { freeMemory_(device_, dummyMemory_, nullptr); dummyMemory_ = VK_NULL_HANDLE; }

    ready_ = false;
    device_ = VK_NULL_HANDLE;
}

void VelloComputePipeline::AbandonDeviceResources() noexcept {
    // Zero every handle without any Vulkan call: the allocations stay owned by
    // the quarantined device generation until process exit.
    for (uint32_t s = 0; s < kStageCount; ++s) {
        pipelines_[s] = VK_NULL_HANDLE;
        pipelineLayouts_[s] = VK_NULL_HANDLE;
        setLayouts_[s] = VK_NULL_HANDLE;
        modules_[s] = VK_NULL_HANDLE;
    }
    for (uint32_t f = 0; f < kFramesInFlight; ++f) {
        descriptorPools_[f] = VK_NULL_HANDLE;
        retiredBuffers_[f].clear();
        retiredImages_[f].clear();
        config_[f] = {};
        scene_[f] = {};
        rampStaging_[f] = {};
        arena_[f] = {};
        frameSlotPrepared_[f] = false;
    }
    bump_ = {};
    reduced_ = {};
    reduced2_ = {};
    reducedScan_ = {};
    tagMonoids_ = {};
    pathBbox_ = {};
    lineSoup_ = {};
    drawReduced_ = {};
    drawMonoid_ = {};
    infoBinData_ = {};
    clipInp_ = {};
    clipBic_ = {};
    clipEl_ = {};
    clipBbox_ = {};
    drawBbox_ = {};
    binHeader_ = {};
    velloPath_ = {};
    velloTile_ = {};
    segCount_ = {};
    velloSegment_ = {};
    ptcl_ = {};
    blendSpill_ = {};
    indirect_ = {};
    rampImage_ = VK_NULL_HANDLE;
    rampMemory_ = VK_NULL_HANDLE;
    rampView_ = VK_NULL_HANDLE;
    rampRows_ = 0;
    rampImageInitialized_ = false;
    outputImage_ = VK_NULL_HANDLE;
    outputMemory_ = VK_NULL_HANDLE;
    outputView_ = VK_NULL_HANDLE;
    outputLayout_ = VK_IMAGE_LAYOUT_UNDEFINED;
    outputWidth_ = outputHeight_ = 0;
    outputSampler_ = VK_NULL_HANDLE;
    dummyImage_ = VK_NULL_HANDLE;
    dummyMemory_ = VK_NULL_HANDLE;
    dummyView_ = VK_NULL_HANDLE;
    dummySampler_ = VK_NULL_HANDLE;
    ready_ = false;
    device_ = VK_NULL_HANDLE;
}

} // namespace jalium
