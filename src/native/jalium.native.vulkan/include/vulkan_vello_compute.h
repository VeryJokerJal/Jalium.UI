#pragma once

// ============================================================================
// VelloComputePipeline -- the Vello 0.10.0 GPU compute pipeline on Vulkan.
//
// Runs the Vello graph with a small-scene scan fast path
// (pathtag_reduce -> small scan OR reduce2/scan1/large scan ->
// bbox_clear -> flatten -> draw_reduce/draw_leaf -> clip_reduce/clip_leaf ->
// binning -> tile_alloc -> path_count_setup/path_count(indirect) -> backdrop
// -> coarse -> path_tiling_setup/path_tiling(indirect) -> fine) using the
// SPIR-V modules embedded in vulkan_vello_shaders.h (regenerated from the
// canonical D3D12 HLSL with -fvk-use-dx-layout so StructuredBuffer strides
// byte-match the shared C++ structs; requires VK_EXT_scalar_block_layout at
// runtime).
//
// The scene arrives as the packed u32 stream produced by VelloSceneEncoder
// (jalium_vello_encode.h, a port of vello_encoding): monoid scans, curve
// flattening, GPU stroke expansion and the REAL clip stack all run on the
// GPU now. Gradients sample a 512-wide premultiplied RGBA8 ramp image.
//
// The fine stage writes a viewport-sized RGBA8 storage image (premultiplied)
// which the render target composites onto the swap chain with SrcOver.
//
// Decoupled from VulkanRenderTarget::Impl: it loads its own device entry
// points from the supplied getDeviceProcAddr and resolves memory types from
// the cached VkPhysicalDeviceMemoryProperties.
// ============================================================================

#include "vulkan_minimal.h"
#include "jalium_vello_encode.h"

#include <cstdint>
#include <vector>

namespace jalium {

class VelloComputePipeline {
public:
    static constexpr uint32_t kFramesInFlight = 2;
    // Number of compiled compute shader permutations / pipelines. A record
    // executes either small-scan or the three-stage large-scan branch.
    static constexpr uint32_t kStageCount = 20;

    VelloComputePipeline() = default;
    ~VelloComputePipeline();

    VelloComputePipeline(const VelloComputePipeline&) = delete;
    VelloComputePipeline& operator=(const VelloComputePipeline&) = delete;

    // Loads procs, creates the 20 compute pipelines, the fixed scratch
    // buffers, the per-frame descriptor pools, the output sampler and the 1x1
    // dummy image (for the fine stage's image-atlas binding). Returns false on
    // any failure -- the caller then keeps the CPU fallback and never calls
    // Record().
    bool Initialize(VkDevice device,
                    VkPhysicalDevice physicalDevice,
                    PFN_vkGetDeviceProcAddr getDeviceProcAddr,
                    const VkPhysicalDeviceMemoryProperties& memoryProperties);

    bool IsReady() const { return ready_; }

    // Cap on Record() calls per prepared frame slot: each one allocates 20
    // descriptor sets from the per-frame pool (sized kMaxRecordsPerFrame *
    // kStageCount) and retires the previous sub-scene's host-visible inputs.
    // A frame with more sub-scenes than this drops the excess (logged by the
    // caller); raise together with the pool sizes in Initialize.
    static constexpr uint32_t kMaxRecordsPerFrame = 128;

    // Records the full compute graph for ONE cut sub-scene into `cmd` (which
    // MUST be outside any render pass). May be called several times per
    // prepared frame slot -- once per VelloSceneSpan replay command -- which
    // is what keeps painter order correct (each sub-scene composites at ITS
    // position in the stream). Grows the scratch buffers and the output image
    // as needed, uploads the scene + gradient ramps into FRESH host-visible
    // buffers (the previous sub-scene's inputs are retired, never overwritten
    // -- their GPU reads execute later), zeroes the bump allocator, dispatches
    // every stage with the required barriers, and leaves the output image in
    // SHADER_READ_ONLY_OPTIMAL ready for compositing. The output image is
    // reused serially across sub-scenes: the leading barrier orders the
    // previous composite's sample before this sub-scene's clear. Returns
    // false (records nothing) when the sub-scene is empty, the per-frame
    // record cap is exhausted, or a resource could not be allocated -- the
    // caller then skips the composite.
    bool Record(VkCommandBuffer cmd, const VelloSubScene& sub, uint32_t frameIdx);

    // Called only after the render target has observed this slot's submit
    // fence and successfully reset its external Vello-composite descriptor
    // pool. The compute pool is reset next; only after BOTH pools no longer
    // contain stale descriptors do we release scratch/output generations
    // retired to this slot. A failed reset leaves every retired object alive
    // and makes Record fail closed for this slot.
    bool PrepareFrameSlot(uint32_t frameIdx);

    // Valid after a successful Record(): the RGBA8 image the fine stage wrote
    // (premultiplied alpha), its view, a NEAREST sampler suitable for a 1:1
    // composite blit, and the image extent (== the scene viewport).
    VkImage     OutputImage()  const { return outputImage_; }
    VkImageView OutputView()   const { return outputView_; }
    VkSampler   OutputSampler() const { return outputSampler_; }
    VkExtent2D  OutputExtent() const { return { outputWidth_, outputHeight_ }; }

    // Releases every GPU object. MUST be called while the VkDevice is still
    // alive (the render target invokes this before Impl tears the device
    // down).
    void Destroy();

    // Fail-closed teardown for an indeterminate live device. Forgetting the
    // device makes Destroy a no-op; the Vulkan allocations intentionally stay
    // owned by the quarantined device generation until process exit, while the
    // C++ container itself can still be reclaimed safely.
    void AbandonDeviceResources() noexcept;

    // Logical resource roles bound across the compiled stages, and a single
    // (binding, type, role) tuple. Public so the file-scope per-stage binding
    // tables in the .cpp can name them.
    enum class Res : uint8_t {
        None = 0,
        Config,          // per-frame UBO (VelloConfig)
        Scene,           // per-frame packed scene u32 stream
        Bump,
        Reduced,
        Reduced2,
        ReducedScan,
        TagMonoids,
        PathBbox,
        LineSoup,
        DrawReduced,
        DrawMonoid,
        InfoBinData,     // combined draw-info + bin-data buffer
        ClipInp,
        ClipBic,
        ClipEl,
        ClipBbox,
        DrawBbox,
        BinHeader,
        VelloPath,
        VelloTile,
        SegCount,
        VelloSegment,
        Ptcl,
        BlendSpill,
        Indirect,
        RampImage,       // 512 x rows RGBA8 sampled image
        DummyImage,      // 1x1 image-atlas placeholder
        OutputImage,     // RGBA8 storage image
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
        VkDeviceSize   capacity = 0;       // allocated bytes
        void*          mapped   = nullptr; // non-null for HOST_VISIBLE buffers
    };

    struct RetiredImage {
        VkImage        image  = VK_NULL_HANDLE;
        VkDeviceMemory memory = VK_NULL_HANDLE;
        VkImageView    view   = VK_NULL_HANDLE;
    };

    static constexpr uint32_t kNoRetireSlot = UINT32_MAX;

    uint32_t FindMemoryType(uint32_t typeFilter, VkMemoryPropertyFlags props) const;
    bool CreateBuffer(VkDeviceSize size, VkBufferUsageFlags usage,
                      VkMemoryPropertyFlags memProps, GpuBuffer& out);
    bool EnsureBuffer(GpuBuffer& buf, VkDeviceSize bytes,
                      VkBufferUsageFlags usage, VkMemoryPropertyFlags memProps,
                      uint32_t retireSlot = kNoRetireSlot);
    void DestroyBuffer(GpuBuffer& buf);
    void DestroyRetiredImage(RetiredImage& image);

    bool CreatePipelines();
    bool CreateOutputSampler();
    bool CreateDummyImage();
    bool EnsureOutputImage(uint32_t width, uint32_t height, uint32_t retireSlot);
    bool EnsureRampImage(uint32_t rows, uint32_t retireSlot);
    bool EnsureScratch(const VelloRenderInfo& ri, uint32_t retireSlot);
    bool UploadInputs(const VelloSubScene& sub, uint32_t frameIdx);
    bool BuildDescriptorSets(uint32_t frameIdx, const VelloRenderInfo& ri,
                             uint32_t sceneWords);
    // Retire this slot's host-visible inputs (and the ramp image when the new
    // sub-scene uploads ramps) before reuse within the SAME frame: the
    // previous Record's GPU reads are still queued, so the buffers must not
    // be rewritten in place. No-op on the slot's first Record of the frame.
    void RetireInputsForReuse(uint32_t frameIdx, uint32_t retireSlot,
                              bool willUploadRamps);

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
    PFN_vkCmdCopyBufferToImage      cmdCopyBufferToImage_ = nullptr;

    // Per-stage GPU objects (indexed by Stage enum order in the .cpp).
    VkShaderModule        modules_[kStageCount] = {};
    VkDescriptorSetLayout setLayouts_[kStageCount] = {};
    VkPipelineLayout      pipelineLayouts_[kStageCount] = {};
    VkPipeline            pipelines_[kStageCount] = {};

    // Per-frame transient descriptor pool (reset once in PrepareFrameSlot;
    // each Record allocates a fresh 20-set group from it, up to
    // kMaxRecordsPerFrame groups per frame).
    VkDescriptorPool descriptorPools_[kFramesInFlight] = {};
    VkDescriptorSet  stageSets_[kStageCount] = {};   // valid only during a Record
    bool             frameSlotPrepared_[kFramesInFlight] = {};
    uint32_t         recordsThisFrame_[kFramesInFlight] = {};

    // Retire buckets, drained by PrepareFrameSlot after the slot's fence.
    std::vector<GpuBuffer> retiredBuffers_[kFramesInFlight];
    std::vector<RetiredImage> retiredImages_[kFramesInFlight];

    // Shared device-local scratch (cross-frame access serialized by the
    // leading barrier in Record).
    GpuBuffer bump_;
    GpuBuffer reduced_;
    GpuBuffer reduced2_;
    GpuBuffer reducedScan_;
    GpuBuffer tagMonoids_;
    GpuBuffer pathBbox_;
    GpuBuffer lineSoup_;
    GpuBuffer drawReduced_;
    GpuBuffer drawMonoid_;
    GpuBuffer infoBinData_;
    GpuBuffer clipInp_;
    GpuBuffer clipBic_;
    GpuBuffer clipEl_;
    GpuBuffer clipBbox_;
    GpuBuffer drawBbox_;
    GpuBuffer binHeader_;
    GpuBuffer velloPath_;
    GpuBuffer velloTile_;
    GpuBuffer segCount_;
    GpuBuffer velloSegment_;
    GpuBuffer ptcl_;
    GpuBuffer blendSpill_;
    GpuBuffer indirect_;

    // Per-frame host-visible CPU inputs.
    GpuBuffer config_[kFramesInFlight];
    GpuBuffer scene_[kFramesInFlight];
    GpuBuffer rampStaging_[kFramesInFlight];

    // Per-frame-slot host-visible upload arena: scene | config | ramp staging
    // slices for EVERY sub-scene of the frame bump-allocate from ONE buffer
    // instead of retiring + vkAllocateMemory-ing three buffers per Record
    // (2-3 device allocations x ~55 sub-scenes made EndDraw the frame's
    // bottleneck while scrolling). Reset in PrepareFrameSlot (after the slot's
    // fence); on overflow the old buffer is retired to this slot and a larger
    // one replaces it (earlier sub-scenes' descriptor sets keep the retired
    // buffer alive until the fence).
    struct FrameArena {
        GpuBuffer buf;
        VkDeviceSize cursor = 0;
    };
    static constexpr VkDeviceSize kArenaAlign = 256;  // covers minUniform/minStorage alignment
    FrameArena arena_[kFramesInFlight];
    // The current Record's slices (Record is single-threaded per frame).
    VkDeviceSize curSceneOffset_ = 0;
    VkDeviceSize curSceneRange_ = 0;
    VkDeviceSize curConfigOffset_ = 0;
    VkDeviceSize curRampOffset_ = 0;
    bool ArenaAlloc(uint32_t frameIdx, VkDeviceSize bytes, VkDeviceSize& outOffset);

    // Gradient ramp image (512 x rampRows_, RGBA8, sampled).
    VkImage        rampImage_    = VK_NULL_HANDLE;
    VkDeviceMemory rampMemory_   = VK_NULL_HANDLE;
    VkImageView    rampView_     = VK_NULL_HANDLE;
    uint32_t       rampRows_     = 0;
    bool           rampImageInitialized_ = false;

    // Single output storage image (RGBA8) + view, NEAREST sampler, dummy image.
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
