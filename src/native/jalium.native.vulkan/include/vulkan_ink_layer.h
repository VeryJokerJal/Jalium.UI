#pragma once

#include "vulkan_minimal.h"      // <vulkan/vulkan.h> + platform defines
#include "vulkan_shader_compiler.h"

#include <cstdint>
#include <memory>
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
    bool                        valid               = false;
};

// Device entry points the ink-layer subsystem needs. Loaded once per device via
// VkInkFunctions::Load so the subsystem is self-contained and never reaches into
// the render target's private dispatch table.
struct VkInkFunctions;

class VulkanBrushShader;

// Shared brush pipeline (one per device). Owns the runtime shader compiler, the
// fullscreen VS, the descriptor-set + pipeline layouts, and the ink render pass.
class VulkanBrushPipeline {
public:
    explicit VulkanBrushPipeline(const VulkanDeviceContext& ctx);
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

private:
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

// One compiled brush. Non-owning back-pointer to the shared pipeline for the VS
// / layout; owns the fragment module + VkPipeline.
class VulkanBrushShader {
public:
    VulkanBrushShader(VulkanBrushPipeline* owner,
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

private:
    VulkanBrushPipeline* owner_;
    VkShaderModule       fragmentModule_ = VK_NULL_HANDLE;
    VkPipeline           pipeline_       = VK_NULL_HANDLE;
    VulkanBrushBlendMode blendMode_;
    std::string          shaderKey_;
};

// Persistent RGBA8 ink layer. Owns its image / view / framebuffer plus the
// scratch needed to dispatch a brush synchronously.
class VulkanInkLayerBitmap {
public:
    explicit VulkanInkLayerBitmap(VulkanBrushPipeline* pipeline);
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
    // extraParams (b1) is optional. Returns 0 on success, non-zero on failure.
    int DispatchBrush(VulkanBrushShader* shader,
                      const void* strokePoints, uint32_t pointCount,
                      const void* managedConstants,
                      const void* extraParams, uint32_t extraParamsSize);

    uint32_t      Width()       const { return width_; }
    uint32_t      Height()      const { return height_; }
    VkImage       Image()       const { return image_; }
    VkImageView   ImageView()   const { return imageView_; }
    VkImageLayout ImageLayout() const { return imageLayout_; }

    // Copies the layer to a host BGRA8 buffer (width_*height_*4). Only used by
    // the render target's CPU-rasterization fallback (the rare frame where the
    // whole replay path is on CPU) so the committed ink still composites. The
    // normal path samples the resident image on the GPU and never reads back.
    bool ReadbackBgra(std::vector<uint8_t>& outBgra);

private:
    bool CreateResources(uint32_t width, uint32_t height);
    void ReleaseResources();
    bool EnsureBuffer(VkBuffer& buffer, VkDeviceMemory& memory, void*& mapped,
                      VkDeviceSize& capacity, VkDeviceSize required, VkBufferUsageFlags usage);
    void DestroyBuffer(VkBuffer& buffer, VkDeviceMemory& memory, void*& mapped, VkDeviceSize& capacity);
    bool BeginCommands();
    bool SubmitAndWait();
    void BarrierToLayout(VkImageLayout newLayout,
                         VkPipelineStageFlags srcStage, VkAccessFlags srcAccess,
                         VkPipelineStageFlags dstStage, VkAccessFlags dstAccess);

    VulkanBrushPipeline* pipeline_;     // shared (non-owning)
    const VkInkFunctions* fns_ = nullptr;

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
