#pragma once

// ============================================================================
// VelloComputePipeline — the REAL Vello GPU compute pipeline on Vulkan.
//
// Mirrors the D3D12 D3D12VelloRenderer::DispatchGPU prefix-sum tiling graph
// (bbox_clear -> flatten -> binning -> tile_alloc -> path_count_setup ->
// path_count(indirect) -> backdrop -> coarse -> path_tiling_setup ->
// path_tiling(indirect) -> fine) using the 13 SPIR-V modules embedded in
// vulkan_vello_shaders.h (regenerated from the canonical D3D12 HLSL with
// -fvk-use-dx-layout so StructuredBuffer strides byte-match the C++ structs;
// requires VK_EXT_scalar_block_layout at runtime).
//
// Clip bboxes are CPU stack-replayed and uploaded per frame (D3D12 parity);
// the clip_reduce/clip_leaf PSOs are still created but never dispatched —
// their simplified EndClip branch wrote `self ∩ parent` instead of Vello's
// "revert to parent context" (see Record()).
//
// The CPU scene (PathSegment / PathInfo / PathDraw / DrawTag / DrawMonoid /
// ClipInp / gradient ramps) is produced by VelloSceneEncoder (jalium_vello_encode.h)
// and uploaded here; the fine stage writes a viewport-sized RGBA32F storage
// image which the render target then composites onto the swap chain.
//
// Decoupled from VulkanRenderTarget::Impl: it loads its own device entry points
// from the supplied getDeviceProcAddr and resolves memory types from the cached
// VkPhysicalDeviceMemoryProperties, so it touches no Impl private state.
// ============================================================================

#include "vulkan_minimal.h"
#include "jalium_vello_encode.h"

#include <cstdint>
#include <vector>

namespace jalium {

class VelloComputePipeline {
public:
    static constexpr uint32_t kFramesInFlight = 2;
    // Number of compute stages (shader modules / pipelines).
    static constexpr uint32_t kStageCount = 13;

    VelloComputePipeline() = default;
    ~VelloComputePipeline();

    VelloComputePipeline(const VelloComputePipeline&) = delete;
    VelloComputePipeline& operator=(const VelloComputePipeline&) = delete;

    // Loads procs, creates the 13 compute pipelines, the fixed scratch buffers,
    // the per-frame descriptor pools, the output sampler and the 1x1 dummy image
    // (for the fine stage's unused image-atlas binding). Returns false on any
    // failure — the caller then keeps the CPU fallback and never calls Record().
    bool Initialize(VkDevice device,
                    VkPhysicalDevice physicalDevice,
                    PFN_vkGetDeviceProcAddr getDeviceProcAddr,
                    const VkPhysicalDeviceMemoryProperties& memoryProperties);

    bool IsReady() const { return ready_; }

    // Records the full compute graph for `scene` into `cmd` (which MUST be
    // outside any render pass). Grows the scratch / per-frame input buffers and
    // the output image as needed, uploads the scene into the per-frame host-
    // visible input buffers, zeroes the bump allocator, dispatches every stage
    // with the required COMPUTE<->COMPUTE / COMPUTE->INDIRECT barriers, and
    // leaves the output image in SHADER_READ_ONLY_OPTIMAL ready for compositing.
    // Returns false (records nothing) when the scene is empty or a resource
    // could not be allocated — the caller then skips the composite.
    bool Record(VkCommandBuffer cmd, const VelloScene& scene, uint32_t frameIdx);

    // Called only after the render target has observed this slot's submit fence
    // and successfully reset its external Vello-composite descriptor pool. The
    // compute pool is reset next; only after BOTH pools no longer contain stale
    // descriptors do we release scratch/output generations retired to this slot.
    // A failed reset leaves every retired object alive and makes Record fail
    // closed for this slot.
    bool PrepareFrameSlot(uint32_t frameIdx);

    // Valid after a successful Record(): the RGBA32F image the fine stage wrote,
    // its view, a NEAREST sampler suitable for a 1:1 composite blit, and the
    // image extent (== the scene viewport).
    VkImage     OutputImage()  const { return outputImage_; }
    VkImageView OutputView()   const { return outputView_; }
    VkSampler   OutputSampler() const { return outputSampler_; }
    VkExtent2D  OutputExtent() const { return { outputWidth_, outputHeight_ }; }

    // Releases every GPU object. MUST be called while the VkDevice is still
    // alive (the render target invokes this before Impl tears the device down).
    void Destroy();

    // Fail-closed teardown for an indeterminate live device. Forgetting the
    // device makes Destroy a no-op; the Vulkan allocations intentionally stay
    // owned by the quarantined device generation until process exit, while the
    // C++ container itself can still be reclaimed safely.
    void AbandonDeviceResources() noexcept;

    // Logical resource roles bound across the 13 stages, and a single (binding,
    // type, role) tuple. Public so the file-scope per-stage binding tables in
    // the .cpp can name them; the descriptor-write path maps a role to the
    // concrete VkBuffer / image.
    enum class Res : uint8_t {
        None = 0,
        Config, Bump, PathBbox, LineSoup, DrawMonoid, IntersectedBbox,
        ClipInp, ClipBic, ClipEl, ClipBbox, BinHeader, BinData,
        VelloPath, VelloTile, SegCount, VelloSegment, Ptcl,
        Indirect1, Indirect2,
        PathSegment, PathInfo, PathDraw, DrawTag, GradientRamp,
        OutputImage, DummyImage, Sampler,
    };

    struct StageBinding {
        uint32_t         binding;
        VkDescriptorType type;
        Res              res;
    };

private:
    struct GpuBuffer {
        VkBuffer       buffer   = VK_NULL_HANDLE;
        VkDeviceMemory memory   = VK_NULL_HANDLE;
        VkDeviceSize   capacity = 0;     // allocated bytes
        void*          mapped   = nullptr; // non-null for HOST_VISIBLE buffers
    };

    struct RetiredOutputImage {
        VkImage        image  = VK_NULL_HANDLE;
        VkDeviceMemory memory = VK_NULL_HANDLE;
        VkImageView    view   = VK_NULL_HANDLE;
    };

    static constexpr uint32_t kNoRetireSlot = UINT32_MAX;

    uint32_t FindMemoryType(uint32_t typeFilter, VkMemoryPropertyFlags props) const;
    bool CreateBuffer(VkDeviceSize size, VkBufferUsageFlags usage,
                      VkMemoryPropertyFlags memProps, GpuBuffer& out);
    // Grows `buf` to at least `bytes` (keeping >= existing capacity). Host-visible
    // buffers stay mapped. Returns false on failure.
    bool EnsureBuffer(GpuBuffer& buf, VkDeviceSize bytes,
                      VkBufferUsageFlags usage, VkMemoryPropertyFlags memProps,
                      uint32_t retireSlot = kNoRetireSlot);
    void DestroyBuffer(GpuBuffer& buf);
    void DestroyOutputImage(RetiredOutputImage& image);

    bool CreatePipelines();
    bool CreateOutputSampler();
    bool CreateDummyImage();
    bool EnsureOutputImage(uint32_t width, uint32_t height, uint32_t retireSlot);
    bool EnsureScratch(const VelloScene& scene, uint32_t retireSlot);
    bool UploadInputs(const VelloScene& scene, uint32_t frameIdx);
    // Allocates 13 fresh descriptor sets from this frame's (reset) pool and
    // writes every binding from the stage tables. Returns false on alloc failure.
    bool BuildDescriptorSets(uint32_t frameIdx, const VelloScene& scene);

    VkBuffer BufferForRes(Res res, uint32_t frameIdx) const;

    bool ready_ = false;

    VkDevice         device_ = VK_NULL_HANDLE;
    VkPhysicalDevice physicalDevice_ = VK_NULL_HANDLE;
    VkPhysicalDeviceMemoryProperties memoryProperties_{};

    // Device entry points (loaded in Initialize).
    PFN_vkCreateShaderModule        createShaderModule_ = nullptr;
    PFN_vkDestroyShaderModule       destroyShaderModule_ = nullptr;
    PFN_vkCreateDescriptorSetLayout createDescriptorSetLayout_ = nullptr;
    PFN_vkDestroyDescriptorSetLayout destroyDescriptorSetLayout_ = nullptr;
    PFN_vkCreatePipelineLayout      createPipelineLayout_ = nullptr;
    PFN_vkDestroyPipelineLayout     destroyPipelineLayout_ = nullptr;
    PFN_vkCreateComputePipelines    createComputePipelines_ = nullptr;
    PFN_vkDestroyPipeline           destroyPipeline_ = nullptr;
    PFN_vkCreateDescriptorPool      createDescriptorPool_ = nullptr;
    PFN_vkDestroyDescriptorPool     destroyDescriptorPool_ = nullptr;
    PFN_vkResetDescriptorPool       resetDescriptorPool_ = nullptr;
    PFN_vkAllocateDescriptorSets    allocateDescriptorSets_ = nullptr;
    PFN_vkUpdateDescriptorSets      updateDescriptorSets_ = nullptr;
    PFN_vkCreateBuffer              createBuffer_ = nullptr;
    PFN_vkDestroyBuffer             destroyBuffer_ = nullptr;
    PFN_vkGetBufferMemoryRequirements getBufferMemoryRequirements_ = nullptr;
    PFN_vkAllocateMemory            allocateMemory_ = nullptr;
    PFN_vkFreeMemory                freeMemory_ = nullptr;
    PFN_vkBindBufferMemory          bindBufferMemory_ = nullptr;
    PFN_vkMapMemory                 mapMemory_ = nullptr;
    PFN_vkUnmapMemory               unmapMemory_ = nullptr;
    PFN_vkCreateImage               createImage_ = nullptr;
    PFN_vkDestroyImage              destroyImage_ = nullptr;
    PFN_vkGetImageMemoryRequirements getImageMemoryRequirements_ = nullptr;
    PFN_vkBindImageMemory           bindImageMemory_ = nullptr;
    PFN_vkCreateImageView           createImageView_ = nullptr;
    PFN_vkDestroyImageView          destroyImageView_ = nullptr;
    PFN_vkCreateSampler             createSampler_ = nullptr;
    PFN_vkDestroySampler            destroySampler_ = nullptr;
    PFN_vkCmdBindPipeline           cmdBindPipeline_ = nullptr;
    PFN_vkCmdBindDescriptorSets     cmdBindDescriptorSets_ = nullptr;
    PFN_vkCmdDispatch               cmdDispatch_ = nullptr;
    PFN_vkCmdDispatchIndirect       cmdDispatchIndirect_ = nullptr;
    PFN_vkCmdPipelineBarrier        cmdPipelineBarrier_ = nullptr;
    PFN_vkCmdFillBuffer             cmdFillBuffer_ = nullptr;
    PFN_vkCmdClearColorImage        cmdClearColorImage_ = nullptr;

    // Per-stage GPU objects (indexed by Stage enum order in the .cpp).
    VkShaderModule        modules_[kStageCount] = {};
    VkDescriptorSetLayout setLayouts_[kStageCount] = {};
    VkPipelineLayout      pipelineLayouts_[kStageCount] = {};
    VkPipeline            pipelines_[kStageCount] = {};

    // Per-frame transient descriptor pool (reset + 13 sets reallocated each
    // Record). One per in-flight frame so a reset never touches sets still
    // referenced by the other frame's command buffer.
    VkDescriptorPool descriptorPools_[kFramesInFlight] = {};
    VkDescriptorSet  stageSets_[kStageCount] = {};   // valid only during a Record
    bool              frameSlotPrepared_[kFramesInFlight] = {};

    // A Record on slot N runs only after N's fence, so the sole possible user
    // of the replaced shared generation is the other in-flight slot. Park the
    // old handles in that slot's bucket; PrepareFrameSlot destroys the bucket
    // after its fence and both descriptor-pool resets have succeeded.
    std::vector<GpuBuffer> retiredBuffers_[kFramesInFlight];
    std::vector<RetiredOutputImage> retiredOutputImages_[kFramesInFlight];

    // Single shared device-local scratch buffers (cross-frame access serialized
    // by the leading barrier in Record). clipBic_/clipEl_ only back the retained
    // (never-dispatched) clip_reduce/clip_leaf descriptor sets.
    GpuBuffer bump_;
    GpuBuffer pathBbox_;
    GpuBuffer lineSoup_;
    GpuBuffer intersectedBbox_;
    GpuBuffer clipBic_;
    GpuBuffer clipEl_;
    GpuBuffer binHeader_;
    GpuBuffer binData_;
    GpuBuffer velloPath_;
    GpuBuffer velloTile_;
    GpuBuffer segCount_;
    GpuBuffer velloSegment_;
    GpuBuffer ptcl_;
    GpuBuffer indirect1_;
    GpuBuffer indirect2_;

    // Per-frame host-visible CPU-input buffers (×2). clipBbox_ carries the
    // CPU stack-replayed clip bboxes (full Vello Begin/End semantics) read by
    // binning — it replaces the former clip_reduce/clip_leaf GPU output.
    GpuBuffer config_[kFramesInFlight];
    GpuBuffer pathSegment_[kFramesInFlight];
    GpuBuffer pathInfo_[kFramesInFlight];
    GpuBuffer pathDraw_[kFramesInFlight];
    GpuBuffer drawTag_[kFramesInFlight];
    GpuBuffer drawMonoid_[kFramesInFlight];
    GpuBuffer clipInp_[kFramesInFlight];
    GpuBuffer clipBbox_[kFramesInFlight];
    GpuBuffer gradientRamp_[kFramesInFlight];

    // Single output storage image (RGBA32F) + view, NEAREST sampler, dummy image.
    VkImage        outputImage_   = VK_NULL_HANDLE;
    VkDeviceMemory outputMemory_  = VK_NULL_HANDLE;
    VkImageView    outputView_    = VK_NULL_HANDLE;
    VkImageLayout  outputLayout_  = VK_IMAGE_LAYOUT_UNDEFINED;
    uint32_t       outputWidth_   = 0;
    uint32_t       outputHeight_  = 0;
    VkSampler      outputSampler_ = VK_NULL_HANDLE;

    VkImage        dummyImage_  = VK_NULL_HANDLE;
    VkDeviceMemory dummyMemory_ = VK_NULL_HANDLE;
    VkImageView    dummyView_   = VK_NULL_HANDLE;
    VkSampler      dummySampler_ = VK_NULL_HANDLE;
};

} // namespace jalium
