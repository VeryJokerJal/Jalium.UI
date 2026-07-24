#pragma once

#include "vulkan_minimal.h"      // <vulkan/vulkan.h> + platform defines
#include "vulkan_shader_compiler.h"

#include <atomic>
#include <cstdint>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

namespace jalium {

// ───────────────────────────────────────────────────────────────────────────
// Vulkan InkCanvas GPU pipeline — the parity counterpart of the D3D12 backend's
// d3d12_ink_layer.cpp / d3d12_brush_shader.cpp.
//
// Architecture mirror of D3D12, adapted to Vulkan's device model:
//
//   * D3D12Backend owns the device, so its ink layers + brush pipeline hang off
//     the backend directly. The Vulkan backend, by contrast, does NOT own a
//     device — each render target creates its own VkInstance/VkDevice (see
//     VulkanBackend doc). So the first render target publishes a
//     VulkanDeviceContext snapshot to the backend (RegisterDeviceContext), and
//     the ink-layer subsystem builds all its GPU objects on that shared device.
//     The InkCanvas blits its committed-ink layer onto the same window's render
//     target, which uses the same device, so direct GPU sampling works without
//     cross-device resource sharing.
//
//   * VulkanBrushPipeline is the shared piece (analogue of
//     D3D12BrushShaderPipeline): one fullscreen-triangle vertex shader, one
//     descriptor-set layout (BrushConstants b0, UserParams b1, StrokePoints t0),
//     one pipeline layout, and one render pass compatible with every ink-layer
//     bitmap (RGBA8 UNORM, LOAD/STORE). It compiles user brush HLSL → SPIR-V at
//     runtime via VulkanShaderCompiler (the DXC analogue of D3D12's D3DCompile).
//
//   * VulkanBrushShader holds the compiled fragment shader module + the
//     VkPipeline baked with the requested blend mode.
//
//   * VulkanInkLayerBitmap is the persistent RGBA8 image strokes are painted
//     into; it owns its own command pool / buffer / fence and runs brush
//     dispatches synchronously (matching D3D12InkLayerBitmap::ExecuteAndWait).
//     Between operations the image is left in SHADER_READ_ONLY_OPTIMAL so the
//     render target can sample it directly for compositing.
//
// All Vulkan entry points are loaded through the device context's
// vkGetInstanceProcAddr / vkGetDeviceProcAddr (the backend uses dynamic
// dispatch — there is no link-time vkXxx). DXC runtime compilation is only
// wired on Windows; elsewhere CreateBrushShader returns nullptr and the managed
// side falls back to CPU ink rasterization, exactly as today.
// ───────────────────────────────────────────────────────────────────────────

// Blend selector — values match the managed BrushBlendMode enum and the D3D12
// BrushBlendMode, so the managed → native pass-through is a direct cast.
enum class VulkanBrushBlendMode : int {
    SourceOver = 0,
    Additive   = 1,
    Erase      = 2,
};

// Shared, device-level handles published by the first render target so the
// backend-owned ink pipeline can build resources on the same device the window
// composites with.
struct VulkanDeviceContext {
    VkInstance                  instance            = VK_NULL_HANDLE;
    VkPhysicalDevice            physicalDevice      = VK_NULL_HANDLE;
    VkDevice                    device              = VK_NULL_HANDLE;
    VkQueue                     graphicsQueue       = VK_NULL_HANDLE;
    uint32_t                    graphicsQueueFamily = 0;
    PFN_vkGetInstanceProcAddr   getInstanceProcAddr = nullptr;
    PFN_vkGetDeviceProcAddr     getDeviceProcAddr   = nullptr;
    bool                        dmaBufImportEnabled = false;
    bool                        valid               = false;
};

// Shared-ownership wrapper around one render target's VkDevice — the Vulkan
// analogue of the ComPtr keep-alive the D3D12 retained layers use
// (d3d12_retained_layer.h). Vulkan handles are not reference-counted: once
// vkDestroyDevice runs, every child handle (images, buffers, fences, …) is
// garbage, and even *destroying* such a child afterwards is use-after-free
// inside the driver. Cross-frame resources that outlive their creating render
// target (ink layer bitmaps / brush shaders today; retained layers when Vulkan
// grows them) therefore hold a shared_ptr to this generation object, which
// owns the device + instance teardown:
//
//   * The creating Impl drops its reference in Impl::Destroy *after* tearing
//     down all RT-owned children. If ink objects still hold references, the
//     VkDevice/VkInstance stay alive until the last of them is destroyed, so
//     their destructors always talk to a live (possibly lost, never freed)
//     device. The last reference runs ~VulkanDeviceGeneration, which destroys
//     the device and then the instance — exactly what Impl::Destroy used to
//     do inline.
//
//   * `lost` is the "removed" discriminator (the analogue of D3D12's
//     GetDeviceRemovedReason, which Vulkan does not have): it is latched by
//     whichever call first observes VK_ERROR_DEVICE_LOST /
//     VK_ERROR_SURFACE_LOST_KHR. Consumers must fail fast on a lost
//     generation (skip dispatches/blits, skip vkDeviceWaitIdle — it would
//     just return VK_ERROR_DEVICE_LOST) but still destroy their child
//     handles: destruction on a lost-but-not-freed device is legal and is
//     the only way to release the driver-side bookkeeping.
//
// Pointer inequality between generations means "different device" — it does
// NOT imply the other generation is lost (a second window registers a second,
// perfectly healthy generation). Mirror the D3D12 Destroy rule: only the
// `lost` flag justifies skipping waits; a healthy foreign generation is used
// normally through its own handles.
struct VulkanDeviceGeneration {
    VulkanDeviceContext   ctx{};
    std::atomic<bool>     lost{false};
    // Fail-closed quarantine for an indeterminate live device. Unlike a
    // DEVICE_LOST generation, its child handles must not be destroyed because
    // the GPU may still reference them.
    std::atomic<bool>     abandoned{false};
    // High-level lifecycle barrier for every call into this device generation.
    // Callers acquire this before queueMutex and recheck IsUsable() while held.
    // Recursive locking is intentional: helpers classify failures through
    // MarkLost/MarkAbandoned and destructors call TryDrainForDestruction while
    // their complete Vulkan operation already owns the barrier.
    mutable std::recursive_mutex driverCallMutex;
    // Vulkan requires host access to a VkQueue, and device-wide idle waits,
    // to be externally synchronized across render targets and ink work that
    // share this generation.
    std::mutex            queueMutex;
    PFN_vkDeviceWaitIdle  deviceWaitIdle  = nullptr;
    PFN_vkDestroyDevice   destroyDevice   = nullptr;
    PFN_vkDestroyInstance destroyInstance = nullptr;

    VulkanDeviceGeneration() = default;
    VulkanDeviceGeneration(const VulkanDeviceGeneration&)            = delete;
    VulkanDeviceGeneration& operator=(const VulkanDeviceGeneration&) = delete;

    void MarkLost() {
        std::lock_guard<std::recursive_mutex> driverLock(driverCallMutex);
        lost.store(true, std::memory_order_release);
    }
    bool IsLost() const { return lost.load(std::memory_order_acquire); }
    void MarkAbandoned() {
        std::lock_guard<std::recursive_mutex> driverLock(driverCallMutex);
        abandoned.store(true, std::memory_order_release);
    }
    bool IsAbandoned() const {
        return abandoned.load(std::memory_order_acquire);
    }
    bool IsUsable() const { return !IsLost() && !IsAbandoned(); }

    // Prove child-handle destruction safe. DEVICE_LOST is authoritative and
    // permits teardown; a missing wait entry point or any other failure
    // quarantines the generation instead of guessing that the device is idle.
    bool TryDrainForDestruction()
    {
        std::lock_guard<std::recursive_mutex> driverLock(driverCallMutex);
        if (IsAbandoned()) return false;
        if (IsLost() || ctx.device == VK_NULL_HANDLE) return true;

        std::lock_guard<std::mutex> queueLock(queueMutex);
        if (IsAbandoned()) return false;
        if (IsLost()) return true;
        if (!deviceWaitIdle) {
            MarkAbandoned();
            return false;
        }

        const VkResult result = deviceWaitIdle(ctx.device);
        if (result == VK_SUCCESS) return true;
        if (result == VK_ERROR_DEVICE_LOST) {
            MarkLost();
            return true;
        }
        MarkAbandoned();
        return false;
    }

    ~VulkanDeviceGeneration()
    {
        std::lock_guard<std::recursive_mutex> driverLock(driverCallMutex);
        if (IsAbandoned()) return;
        if (ctx.device != VK_NULL_HANDLE) {
            // Ink work is always fence-waited before its objects die, and the
            // creating Impl::Destroy already drained the RT's own work, so the
            // queue is idle here in practice; the extra wait is a cheap belt
            // for exotic interleavings. On a lost device the wait would only
            // poke the dead driver, so skip it — vkDestroyDevice on a lost
            // device is legal and required.
            if (!destroyDevice || !TryDrainForDestruction()) {
                MarkAbandoned();
                return;
            }
            destroyDevice(ctx.device, nullptr);
        }
        if (IsAbandoned()) return;
        if (ctx.instance != VK_NULL_HANDLE && destroyInstance) {
            destroyInstance(ctx.instance, nullptr);
        }
    }
};

// Device entry points the ink-layer subsystem needs. Loaded once per device via
// VkInkFunctions::Load so the subsystem is self-contained and never reaches into
// the render target's private dispatch table.
struct VkInkFunctions;

class VulkanBrushShader;

// Shared brush pipeline (one per device generation). Owns the runtime shader
// compiler, the fullscreen VS, the descriptor-set + pipeline layouts, and the
// ink render pass. Always owned by shared_ptr (the backend holds one
// reference, every live ink bitmap / brush shader holds another), so a device
// swap in VulkanBackend::RegisterDeviceContext only drops the backend's
// reference — surviving ink objects keep both the pipeline and (through gen_)
// the VkDevice alive until they are destroyed themselves.
class VulkanBrushPipeline : public std::enable_shared_from_this<VulkanBrushPipeline> {
public:
    explicit VulkanBrushPipeline(std::shared_ptr<VulkanDeviceGeneration> generation);
    ~VulkanBrushPipeline();

    VulkanBrushPipeline(const VulkanBrushPipeline&)            = delete;
    VulkanBrushPipeline& operator=(const VulkanBrushPipeline&) = delete;

    // Loads the device function table, compiles the shared VS, and creates the
    // descriptor-set layout / pipeline layout / ink render pass. Idempotent.
    // Returns false if DXC is unavailable or any Vulkan object fails to create
    // (in which case the whole ink GPU path is disabled and callers fall back).
    bool Initialize();
    bool IsReady() const { return ready_; }

    // Compile a user brush HLSL body + bake its VkPipeline for the given blend
    // mode. Returns nullptr on compile / pipeline-creation failure.
    std::unique_ptr<VulkanBrushShader> CreateBrushShader(
        const char* shaderKey, const char* brushMainHlsl, VulkanBrushBlendMode blendMode);

    const VulkanDeviceContext& Context()          const { return ctx_; }
    const VkInkFunctions&      Fns()               const { return *fns_; }
    VkRenderPass               InkRenderPass()     const { return inkRenderPass_; }
    VkDescriptorSetLayout      DescriptorLayout()  const { return descriptorSetLayout_; }
    VkPipelineLayout           PipelineLayout()    const { return pipelineLayout_; }

    const std::shared_ptr<VulkanDeviceGeneration>& Generation() const { return gen_; }
    // "Removed" discriminator: true once the creating device generation
    // observed VK_ERROR_DEVICE_LOST. GPU work must fail fast; handle
    // destruction stays legal (the generation keeps the device alive).
    bool DeviceLost() const { return !gen_ || !gen_->IsUsable(); }
    bool DeviceAbandoned() const { return gen_ && gen_->IsAbandoned(); }

private:
    // gen_ must precede ctx_: the constructor initializes ctx_ from gen_->ctx
    // and member init runs in declaration order.
    std::shared_ptr<VulkanDeviceGeneration> gen_;
    VulkanDeviceContext             ctx_;
    std::unique_ptr<VkInkFunctions> fns_;
    VulkanShaderCompiler            compiler_;
    VkShaderModule                  vertexModule_        = VK_NULL_HANDLE;
    std::vector<uint32_t>           vertexSpirv_;
    VkDescriptorSetLayout           descriptorSetLayout_ = VK_NULL_HANDLE;
    VkPipelineLayout                pipelineLayout_      = VK_NULL_HANDLE;
    VkRenderPass                    inkRenderPass_       = VK_NULL_HANDLE;
    bool                            ready_               = false;
    bool                            attempted_           = false;
};

// One compiled brush. Holds a shared reference to the pipeline (and through it
// the device generation) so the managed BrushShaderHandle can outlive the
// creating render target without its destructor dereferencing a freed pipeline
// or a destroyed VkDevice; owns the fragment module + VkPipeline.
class VulkanBrushShader {
public:
    VulkanBrushShader(std::shared_ptr<VulkanBrushPipeline> owner,
                      VkShaderModule fragmentModule,
                      VkPipeline pipeline,
                      VulkanBrushBlendMode blendMode,
                      std::string shaderKey);
    ~VulkanBrushShader();

    VulkanBrushShader(const VulkanBrushShader&)            = delete;
    VulkanBrushShader& operator=(const VulkanBrushShader&) = delete;

    VkPipeline           Pipeline()  const { return pipeline_; }
    VulkanBrushBlendMode BlendMode() const { return blendMode_; }
    const std::string&   Key()       const { return shaderKey_; }
    // Identity of the pipeline (and so the device generation) this shader was
    // baked against. DispatchBrush refuses a shader whose owner differs from
    // the bitmap's pipeline — binding a VkPipeline from one VkDevice into a
    // command buffer of another is cross-device UB.
    const VulkanBrushPipeline* OwnerPipeline() const { return owner_.get(); }

private:
    std::shared_ptr<VulkanBrushPipeline> owner_;
    VkShaderModule       fragmentModule_ = VK_NULL_HANDLE;
    VkPipeline           pipeline_       = VK_NULL_HANDLE;
    VulkanBrushBlendMode blendMode_;
    std::string          shaderKey_;
};

// One immutable allocation generation of a persistent ink image. The image
// handles and extent never change after publication; only imageLayout_ changes
// while the owning bitmap performs a synchronous clear/brush/readback command.
//
// Replay commands retain this object when they are recorded and the submitting
// frame slot retains it until that slot's fence is observed. Resize can
// therefore publish a replacement immediately without vkDeviceWaitIdle, and
// destruction of the old VkImage cannot race an already-recorded or in-flight
// replay command.
class VulkanInkImageGeneration final {
public:
    ~VulkanInkImageGeneration();

    VulkanInkImageGeneration(const VulkanInkImageGeneration&) = delete;
    VulkanInkImageGeneration& operator=(const VulkanInkImageGeneration&) = delete;

    uint32_t    Width()        const { return width_; }
    uint32_t    Height()       const { return height_; }
    VkImage     Image()        const { return image_; }
    VkImageView ImageView()    const { return imageView_; }
    VkDevice    DeviceHandle() const {
        return pipeline_ ? pipeline_->Context().device : VK_NULL_HANDLE;
    }
    bool DeviceLost() const { return pipeline_ && pipeline_->DeviceLost(); }
    bool DeviceAbandoned() const {
        return pipeline_ && pipeline_->DeviceAbandoned();
    }

private:
    friend class VulkanInkLayerBitmap;

    explicit VulkanInkImageGeneration(
        std::shared_ptr<VulkanBrushPipeline> pipeline);

    std::shared_ptr<VulkanBrushPipeline> pipeline_;
    const VkInkFunctions* fns_ = nullptr;
    VkImage         image_       = VK_NULL_HANDLE;
    VkDeviceMemory  imageMemory_ = VK_NULL_HANDLE;
    VkImageView     imageView_   = VK_NULL_HANDLE;
    VkFramebuffer   framebuffer_ = VK_NULL_HANDLE;
    VkImageLayout   imageLayout_ = VK_IMAGE_LAYOUT_UNDEFINED;
    uint32_t        width_       = 0;
    uint32_t        height_      = 0;
    int64_t         gpuResidentBytesAccounted_ = 0;
};

// Persistent RGBA8 ink layer. Owns its image / view / framebuffer plus the
// scratch needed to dispatch a brush synchronously. Holds a shared reference
// to the pipeline (and through it the device generation): the managed
// InkLayerBitmap handle routinely outlives the render target that published
// the device (window close, device-lost recovery), and without the keep-alive
// its destructor would feed handles of an already-vkDestroyDevice'd device
// into the driver.
class VulkanInkLayerBitmap {
public:
    explicit VulkanInkLayerBitmap(std::shared_ptr<VulkanBrushPipeline> pipeline);
    ~VulkanInkLayerBitmap();

    VulkanInkLayerBitmap(const VulkanInkLayerBitmap&)            = delete;
    VulkanInkLayerBitmap& operator=(const VulkanInkLayerBitmap&) = delete;

    // Allocate / re-allocate the backing image. Contents reset to transparent
    // on any size change. Returns false on failure.
    bool Initialize(uint32_t width, uint32_t height);
    bool Resize(uint32_t width, uint32_t height);

    // Clear to a premultiplied RGBA color. Leaves the image SHADER_READ_ONLY.
    void Clear(float r, float g, float b, float a);

    // Dispatch a brush fragment shader over the layer. strokePoints points to
    // pointCount × 16 bytes (x, y, pressure, pad). managedConstants is the
    // 80-byte managed BrushConstantsNative; this method fills the trailing
    // ViewportSize/pad to reach the 96-byte cbuffer the shader expects.
    // extraParams (b1) is optional.
    //
    // Returns a JaliumInkDispatchResult (jalium_types.h). Failure classes:
    //   STALE_CONTEXT — device-lost latch, or the shader's pipeline and this
    //                   bitmap come from different device generations; the
    //                   caller must rebuild the whole ink resource chain.
    //   TRANSIENT     — momentary buffer/command failure; retry the same
    //                   handles next frame.
    //   INVALID_ARG / INVALID_STATE — malformed call / missing resources.
    int DispatchBrush(VulkanBrushShader* shader,
                      const void* strokePoints, uint32_t pointCount,
                      const void* managedConstants,
                      const void* extraParams, uint32_t extraParamsSize);

    uint32_t      Width()       const;
    uint32_t      Height()      const;
    VkImage       Image()       const;
    VkImageView   ImageView()   const;
    VkImageLayout ImageLayout() const;

    // Atomically captures the currently-published allocation generation.
    // Callers must carry this reference through command recording and, after
    // queue submission, until the corresponding frame fence completes.
    std::shared_ptr<const VulkanInkImageGeneration>
        AcquireImageGeneration() const;

    // Generation discrimination for consumers (BlitInkLayer): the device this
    // bitmap's image lives on, and whether that device generation is lost.
    // A render target may only GPU-sample the image when DeviceHandle()
    // matches its own VkDevice — Vulkan device-level handles never cross
    // devices — and must skip a lost generation entirely.
    VkDevice DeviceHandle() const { return pipeline_ ? pipeline_->Context().device : VK_NULL_HANDLE; }
    bool     DeviceLost()   const { return pipeline_ && pipeline_->DeviceLost(); }
    bool     DeviceAbandoned() const {
        return pipeline_ && pipeline_->DeviceAbandoned();
    }

    // Monotonic content revision, bumped after every successful mutation
    // (Clear / DispatchBrush / Resize). The foreign-device blit fallback keys
    // its readback cache on it so an unchanged layer costs zero GPU round
    // trips per frame instead of a synchronous readback every frame.
    uint64_t ContentVersion() const { return contentVersion_.load(std::memory_order_acquire); }

    // Copies the layer to a host BGRA8 buffer (width_*height_*4). Only used by
    // the render target's CPU-rasterization fallback (the rare frame where the
    // whole replay path is on CPU) so the committed ink still composites. The
    // normal path samples the resident image on the GPU and never reads back.
    bool ReadbackBgra(std::vector<uint8_t>& outBgra);

private:
    std::shared_ptr<VulkanInkImageGeneration>
        CreateResources(uint32_t width, uint32_t height);
    void BindOperationGeneration(
        const std::shared_ptr<VulkanInkImageGeneration>& generation);
    void ReleaseResources();
    bool ClearCurrent(float r, float g, float b, float a);
    bool EnsureBuffer(VkBuffer& buffer, VkDeviceMemory& memory, void*& mapped,
                      VkDeviceSize& capacity, VkDeviceSize required, VkBufferUsageFlags usage);
    void DestroyBuffer(VkBuffer& buffer, VkDeviceMemory& memory, void*& mapped, VkDeviceSize& capacity);
    bool BeginCommands();
    bool SubmitAndWait();
    void BarrierToLayout(VkImageLayout newLayout,
                         VkPipelineStageFlags srcStage, VkAccessFlags srcAccess,
                         VkPipelineStageFlags dstStage, VkAccessFlags dstAccess);
    // Classifies a failed driver result while holding driverCallMutex and then
    // queueMutex. DEVICE_LOST is latched; every other result quarantines the
    // generation so later operations and destructors fail closed.
    void NoteResult(VkResult result);

    std::shared_ptr<VulkanBrushPipeline> pipeline_;  // shared keep-alive (pipeline + device generation)
    const VkInkFunctions* fns_ = nullptr;            // borrowed from pipeline_; valid while pipeline_ held
    std::atomic<uint64_t> contentVersion_{1};        // see ContentVersion()
    void BumpContentVersion() { contentVersion_.fetch_add(1, std::memory_order_acq_rel); }

    // Serializes the bitmap-owned command buffer/fence and mapped scratch.
    // Backend calls already use VulkanBackend::inkMutex_; this second gate is
    // required for render-target readback, which can arrive independently.
    mutable std::mutex operationMutex_;
    // Resize holds this publication gate until the fresh image has been
    // cleared and returned to SHADER_READ_ONLY_OPTIMAL. A recorder therefore
    // observes either the complete old generation or the complete new one.
    mutable std::mutex imageGenerationMutex_;
    std::shared_ptr<VulkanInkImageGeneration> imageGeneration_;
    VulkanInkImageGeneration* operationGeneration_ = nullptr;

    // Non-owning aliases of operationGeneration_'s handles. During resize they
    // temporarily target an unpublished candidate while it is cleared; normal
    // bitmap operations target imageGeneration_. Asynchronous consumers retain
    // the shared generation returned by AcquireImageGeneration instead.
    VkImage         image_       = VK_NULL_HANDLE;
    VkDeviceMemory  imageMemory_ = VK_NULL_HANDLE;
    VkImageView     imageView_   = VK_NULL_HANDLE;
    VkFramebuffer   framebuffer_ = VK_NULL_HANDLE;
    VkImageLayout   imageLayout_ = VK_IMAGE_LAYOUT_UNDEFINED;
    uint32_t        width_       = 0;
    uint32_t        height_      = 0;
    // Synchronous dispatch scratch.
    VkCommandPool   cmdPool_   = VK_NULL_HANDLE;
    VkCommandBuffer cmdBuffer_ = VK_NULL_HANDLE;
    VkFence         fence_     = VK_NULL_HANDLE;

    // Reused upload buffers (host-visible coherent).
    VkBuffer       cbBuffer_   = VK_NULL_HANDLE;   // BrushConstants b0 (96 bytes)
    VkDeviceMemory cbMemory_   = VK_NULL_HANDLE;
    void*          cbMapped_   = nullptr;
    VkDeviceSize   cbCapacity_ = 0;

    VkBuffer       userBuffer_   = VK_NULL_HANDLE; // UserParams b1
    VkDeviceMemory userMemory_   = VK_NULL_HANDLE;
    void*          userMapped_   = nullptr;
    VkDeviceSize   userCapacity_ = 0;

    VkBuffer       strokeBuffer_   = VK_NULL_HANDLE; // StrokePoints t0 (SSBO)
    VkDeviceMemory strokeMemory_   = VK_NULL_HANDLE;
    void*          strokeMapped_   = nullptr;
    VkDeviceSize   strokeCapacity_ = 0;

    VkDescriptorPool descriptorPool_ = VK_NULL_HANDLE;
    VkDescriptorSet  descriptorSet_  = VK_NULL_HANDLE;

    // Lazily-created host-visible readback buffer for the CPU fallback path.
    VkBuffer       readbackBuffer_   = VK_NULL_HANDLE;
    VkDeviceMemory readbackMemory_   = VK_NULL_HANDLE;
    void*          readbackMapped_   = nullptr;
    VkDeviceSize   readbackCapacity_ = 0;
};

} // namespace jalium
