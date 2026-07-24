// VulkanVideoSurface — stage 2 BGRA8 video staging on Vulkan. Mirrors the
// D3D12 path's role (wraps the existing bitmap upload machinery + exposes a
// Lock/Unlock pair) but routes through VulkanBitmap.UpdatePackedPixels so
// the shared_ptr<vector> COW invariant the framework relies on for safe
// GpuReplayCommand replay stays intact — any in-flight command holding the
// previous frame's pixels keeps that buffer alive on its own.
//
// Stage 5 will replace this with VK_KHR_external_memory_win32 / dma-buf /
// AHardwareBuffer imports to skip the CPU staging hop entirely.

#include "vulkan_resources.h"
#include "vulkan_backend.h"
#include "vulkan_render_target.h"
#include "vulkan_ink_layer.h"

#include <cstring>

#if defined(__linux__) && !defined(__ANDROID__)
#include <unistd.h>
#endif

namespace jalium {

namespace {

constexpr uint32_t DrmFourcc(char a, char b, char c, char d)
{
    return static_cast<uint32_t>(static_cast<uint8_t>(a)) |
           (static_cast<uint32_t>(static_cast<uint8_t>(b)) << 8) |
           (static_cast<uint32_t>(static_cast<uint8_t>(c)) << 16) |
           (static_cast<uint32_t>(static_cast<uint8_t>(d)) << 24);
}

using VideoLifetimeCallback = void (*)(uint64_t);

VideoLifetimeCallback DecodeLifetimeCallback(uint64_t callback)
{
    return callback == 0 ? nullptr : reinterpret_cast<VideoLifetimeCallback>(
        static_cast<uintptr_t>(callback));
}

template <typename T>
T LoadVideoDeviceProc(
    const VulkanDeviceContext& context,
    const char* name)
{
    return context.getDeviceProcAddr
        ? reinterpret_cast<T>(context.getDeviceProcAddr(context.device, name))
        : nullptr;
}

template <typename T>
T LoadVideoInstanceProc(
    const VulkanDeviceContext& context,
    const char* name)
{
    return context.getInstanceProcAddr
        ? reinterpret_cast<T>(context.getInstanceProcAddr(context.instance, name))
        : nullptr;
}

} // namespace

VulkanVideoSurface::VulkanVideoSurface(uint32_t width, uint32_t height)
    : bitmap(width, height, std::vector<uint8_t>(static_cast<size_t>(width) * height * 4u, 0))
    , staging(static_cast<size_t>(width) * height * 4u, 0)
{
}

bool VulkanVideoSurface::Lock(uint8_t** outPtr, uint32_t* outStride)
{
    if (!outPtr || !outStride) return false;
    if (staging.empty()) return false;
    *outPtr    = staging.data();
    *outStride = bitmap.GetWidth() * 4u;
    return true;
}

bool VulkanVideoSurface::Unlock(const JaliumVideoSurfaceDirtyRect* /*dirty*/)
{
    // Hand the staging buffer to the bitmap. UpdatePackedPixels allocates
    // a fresh shared_ptr<vector> inside, so any in-flight GpuReplayCommand
    // holding the previous frame's pixels stays valid until that command
    // completes and drops its ref — see [[project_vulkan_bitmap_shared_ptr_cow]].
    bitmap.UpdatePackedPixels(staging.data(),
                              bitmap.GetWidth(), bitmap.GetHeight(),
                              bitmap.GetWidth() * 4u);
    return true;
}

VulkanImportedVideoSurface* VulkanImportedVideoSurface::Create(
    std::shared_ptr<VulkanDeviceGeneration> generation,
    const JaliumVideoSurfaceDescriptor& descriptor)
{
#if defined(__linux__) && !defined(__ANDROID__)
    if (!generation || !generation->IsUsable() || !generation->ctx.valid ||
        !generation->ctx.dmaBufImportEnabled || descriptor.plane_count != 1 ||
        descriptor.planes[0].fd < 0 || descriptor.width == 0 ||
        descriptor.height == 0) {
        return nullptr;
    }
    std::unique_lock<std::recursive_mutex> driverLock(
        generation->driverCallMutex);
    if (!generation->IsUsable()) return nullptr;

    VkFormat vkFormat = VK_FORMAT_UNDEFINED;
    bool forceOpaqueAlpha = false;
    switch (descriptor.drm_fourcc) {
    case DrmFourcc('A', 'R', '2', '4'):
        vkFormat = VK_FORMAT_B8G8R8A8_UNORM;
        break;
    case DrmFourcc('X', 'R', '2', '4'):
        vkFormat = VK_FORMAT_B8G8R8A8_UNORM;
        forceOpaqueAlpha = true;
        break;
    case DrmFourcc('A', 'B', '2', '4'):
        vkFormat = VK_FORMAT_R8G8B8A8_UNORM;
        break;
    case DrmFourcc('X', 'B', '2', '4'):
        vkFormat = VK_FORMAT_R8G8B8A8_UNORM;
        forceOpaqueAlpha = true;
        break;
    default:
        // The descriptor remains fully multi-plane, but NV12/P010 needs a
        // YCbCr immutable-sampler pipeline. Rejecting here activates the
        // decoder's precise-PTS CPU fallback instead of importing illegally.
        return nullptr;
    }

    const auto& context = generation->ctx;
    auto createImage = LoadVideoDeviceProc<PFN_vkCreateImage>(context, "vkCreateImage");
    auto destroyImage = LoadVideoDeviceProc<PFN_vkDestroyImage>(context, "vkDestroyImage");
    auto getRequirements = LoadVideoDeviceProc<PFN_vkGetImageMemoryRequirements>(
        context, "vkGetImageMemoryRequirements");
    auto getFdProperties = LoadVideoDeviceProc<PFN_vkGetMemoryFdPropertiesKHR>(
        context, "vkGetMemoryFdPropertiesKHR");
    auto allocateMemory = LoadVideoDeviceProc<PFN_vkAllocateMemory>(context, "vkAllocateMemory");
    auto freeMemory = LoadVideoDeviceProc<PFN_vkFreeMemory>(context, "vkFreeMemory");
    auto bindImageMemory = LoadVideoDeviceProc<PFN_vkBindImageMemory>(context, "vkBindImageMemory");
    auto createImageView = LoadVideoDeviceProc<PFN_vkCreateImageView>(context, "vkCreateImageView");
    auto destroyImageView = LoadVideoDeviceProc<PFN_vkDestroyImageView>(context, "vkDestroyImageView");
    auto getMemoryProperties = LoadVideoInstanceProc<PFN_vkGetPhysicalDeviceMemoryProperties>(
        context, "vkGetPhysicalDeviceMemoryProperties");
    if (!createImage || !destroyImage || !getRequirements || !getFdProperties ||
        !allocateMemory || !freeMemory || !bindImageMemory || !createImageView ||
        !destroyImageView || !getMemoryProperties) {
        return nullptr;
    }

    VkSubresourceLayout planeLayout{};
    planeLayout.offset = descriptor.planes[0].offset_bytes;
    planeLayout.rowPitch = descriptor.planes[0].stride_bytes;
    planeLayout.size = descriptor.planes[0].size_bytes;
    VkImageDrmFormatModifierExplicitCreateInfoEXT modifierInfo{};
    modifierInfo.sType =
        VK_STRUCTURE_TYPE_IMAGE_DRM_FORMAT_MODIFIER_EXPLICIT_CREATE_INFO_EXT;
    modifierInfo.drmFormatModifier = descriptor.planes[0].modifier;
    modifierInfo.drmFormatModifierPlaneCount = 1;
    modifierInfo.pPlaneLayouts = &planeLayout;
    VkExternalMemoryImageCreateInfo externalInfo{};
    externalInfo.sType = VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO;
    externalInfo.pNext = &modifierInfo;
    externalInfo.handleTypes =
        VK_EXTERNAL_MEMORY_HANDLE_TYPE_DMA_BUF_BIT_EXT;
    VkImageCreateInfo imageInfo{};
    imageInfo.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    imageInfo.pNext = &externalInfo;
    imageInfo.imageType = VK_IMAGE_TYPE_2D;
    imageInfo.format = vkFormat;
    imageInfo.extent = {descriptor.width, descriptor.height, 1};
    imageInfo.mipLevels = 1;
    imageInfo.arrayLayers = 1;
    imageInfo.samples = VK_SAMPLE_COUNT_1_BIT;
    imageInfo.tiling = VK_IMAGE_TILING_DRM_FORMAT_MODIFIER_EXT;
    imageInfo.usage = VK_IMAGE_USAGE_SAMPLED_BIT;
    imageInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    imageInfo.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;

    VkImage image = VK_NULL_HANDLE;
    if (createImage(context.device, &imageInfo, nullptr, &image) != VK_SUCCESS ||
        image == VK_NULL_HANDLE) {
        return nullptr;
    }

    VkMemoryRequirements requirements{};
    getRequirements(context.device, image, &requirements);
    VkMemoryFdPropertiesKHR fdProperties{};
    fdProperties.sType = VK_STRUCTURE_TYPE_MEMORY_FD_PROPERTIES_KHR;
    if (getFdProperties(context.device,
                        VK_EXTERNAL_MEMORY_HANDLE_TYPE_DMA_BUF_BIT_EXT,
                        descriptor.planes[0].fd, &fdProperties) != VK_SUCCESS) {
        destroyImage(context.device, image, nullptr);
        return nullptr;
    }
    VkPhysicalDeviceMemoryProperties memoryProperties{};
    getMemoryProperties(context.physicalDevice, &memoryProperties);
    const uint32_t compatible =
        requirements.memoryTypeBits & fdProperties.memoryTypeBits;
    uint32_t memoryType = UINT32_MAX;
    for (uint32_t i = 0; i < memoryProperties.memoryTypeCount; ++i) {
        if ((compatible & (1u << i)) == 0) continue;
        if (memoryType == UINT32_MAX) memoryType = i;
        if ((memoryProperties.memoryTypes[i].propertyFlags &
             VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT) != 0) {
            memoryType = i;
            break;
        }
    }
    if (memoryType == UINT32_MAX) {
        destroyImage(context.device, image, nullptr);
        return nullptr;
    }

    const int importFd = dup(descriptor.planes[0].fd);
    if (importFd < 0) {
        destroyImage(context.device, image, nullptr);
        return nullptr;
    }
    VkImportMemoryFdInfoKHR importInfo{};
    importInfo.sType = VK_STRUCTURE_TYPE_IMPORT_MEMORY_FD_INFO_KHR;
    importInfo.handleType = VK_EXTERNAL_MEMORY_HANDLE_TYPE_DMA_BUF_BIT_EXT;
    importInfo.fd = importFd;
    VkMemoryAllocateInfo allocation{};
    allocation.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocation.pNext = &importInfo;
    allocation.allocationSize = requirements.size;
    allocation.memoryTypeIndex = memoryType;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    if (allocateMemory(context.device, &allocation, nullptr, &memory) !=
            VK_SUCCESS ||
        memory == VK_NULL_HANDLE) {
        close(importFd); // ownership transfers only on successful import
        destroyImage(context.device, image, nullptr);
        return nullptr;
    }
    if (bindImageMemory(context.device, image, memory, 0) != VK_SUCCESS) {
        freeMemory(context.device, memory, nullptr);
        destroyImage(context.device, image, nullptr);
        return nullptr;
    }

    VkImageViewCreateInfo viewInfo{};
    viewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    viewInfo.image = image;
    viewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
    viewInfo.format = vkFormat;
    viewInfo.components = {
        VK_COMPONENT_SWIZZLE_IDENTITY,
        VK_COMPONENT_SWIZZLE_IDENTITY,
        VK_COMPONENT_SWIZZLE_IDENTITY,
        forceOpaqueAlpha ? VK_COMPONENT_SWIZZLE_ONE
                         : VK_COMPONENT_SWIZZLE_IDENTITY};
    viewInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    viewInfo.subresourceRange.levelCount = 1;
    viewInfo.subresourceRange.layerCount = 1;
    VkImageView view = VK_NULL_HANDLE;
    if (createImageView(context.device, &viewInfo, nullptr, &view) != VK_SUCCESS ||
        view == VK_NULL_HANDLE) {
        freeMemory(context.device, memory, nullptr);
        destroyImage(context.device, image, nullptr);
        return nullptr;
    }

    auto* surface = new (std::nothrow) VulkanImportedVideoSurface();
    if (!surface) {
        destroyImageView(context.device, view, nullptr);
        freeMemory(context.device, memory, nullptr);
        destroyImage(context.device, image, nullptr);
        return nullptr;
    }
    surface->generation_ = generation;
    surface->width_ = descriptor.width;
    surface->height_ = descriptor.height;
    surface->image_ = image;
    surface->view_ = view;
    surface->memory_ = memory;
    // The Vulkan import is complete. Do not hold the driver barrier while
    // invoking producer-owned lifetime callbacks.
    driverLock.unlock();
    if (descriptor.lifetime_context != 0 &&
        descriptor.lifetime_retain_callback != 0 &&
        descriptor.lifetime_release_callback != 0) {
        auto retain = DecodeLifetimeCallback(
            descriptor.lifetime_retain_callback);
        if (retain) {
            retain(descriptor.lifetime_context);
            surface->lifetimeContext_ = descriptor.lifetime_context;
            surface->lifetimeReleaseCallback_ =
                descriptor.lifetime_release_callback;
        }
    }
    return surface;
#else
    (void)generation;
    (void)descriptor;
    return nullptr;
#endif
}

VulkanImportedVideoSurface::~VulkanImportedVideoSurface()
{
    if (generation_) {
        std::lock_guard<std::recursive_mutex> driverLock(
            generation_->driverCallMutex);
        if (!generation_->IsAbandoned() &&
            generation_->ctx.device != VK_NULL_HANDLE) {
            const auto& context = generation_->ctx;
            auto destroyImageView = LoadVideoDeviceProc<PFN_vkDestroyImageView>(
                context, "vkDestroyImageView");
            auto destroyImage = LoadVideoDeviceProc<PFN_vkDestroyImage>(
                context, "vkDestroyImage");
            auto freeMemory = LoadVideoDeviceProc<PFN_vkFreeMemory>(
                context, "vkFreeMemory");
            if (view_ != VK_NULL_HANDLE && destroyImageView)
                destroyImageView(context.device, view_, nullptr);
            if (image_ != VK_NULL_HANDLE && destroyImage)
                destroyImage(context.device, image_, nullptr);
            if (memory_ != VK_NULL_HANDLE && freeMemory)
                freeMemory(context.device, memory_, nullptr);
        }
    }
    // The producer lifetime is independent from Vulkan device availability.
    // Producer ownership is independent of Vulkan quarantine. Even when GPU
    // handles are intentionally leaked for an abandoned generation, return
    // the held GstSample to the decoder pool.
    if (lifetimeContext_ != 0 && lifetimeReleaseCallback_ != 0) {
        auto release = DecodeLifetimeCallback(lifetimeReleaseCallback_);
        if (release) release(lifetimeContext_);
    }
}

void VulkanImportedVideoSurface::Release()
{
    if (referenceCount_.fetch_sub(1, std::memory_order_acq_rel) == 1) {
        delete this;
    }
}

VkDevice VulkanImportedVideoSurface::DeviceHandle() const
{
    return generation_ ? generation_->ctx.device : VK_NULL_HANDLE;
}

bool VulkanImportedVideoSurface::DeviceLost() const
{
    return !generation_ || !generation_->IsUsable();
}

// ─── Backend factory ──────────────────────────────────────────────────────

VideoSurface* VulkanBackend::CreateVideoSurface(uint32_t width, uint32_t height,
                                                 uint32_t /*formatHint*/)
{
    if (width == 0 || height == 0) return nullptr;
    return new VulkanVideoSurface(width, height);
}

VideoSurface* VulkanBackend::WrapExternalVideoSurface(
    const JaliumVideoSurfaceDescriptor* descriptor)
{
    if (!descriptor) return nullptr;

    if (descriptor->kind == JALIUM_VS_KIND_AHARDWAREBUFFER) {
        // Stage 4 真填占位:Android MediaCodec output → AHardwareBuffer →
        // Vulkan VK_ANDROID_external_memory_android_hardware_buffer:
        //   1. vkGetAndroidHardwareBufferPropertiesANDROID(buffer) 拿 memory
        //      requirements + external format
        //   2. vkCreateImage with VkExternalMemoryImageCreateInfo + (optional)
        //      VkExternalFormatANDROID for non-RGB hardware buffer formats
        //   3. vkAllocateMemory with VkImportAndroidHardwareBufferInfoANDROID
        //      pointing at descriptor->handle0 = AHardwareBuffer*
        //   4. vkBindImageMemory + create VkImageView with the YUV sampler
        //      conversion (VkSamplerYcbcrConversion) when external format
        //   5. wrap in ImportedVulkanVideoSurface : VideoSurface,DrawVideoSurface
        //      uses a YUV-aware sampler in the bitmap shader
        //
        // 当前返 nullptr → externalImportFails 计数 +1 → fallback 到 stage 2
        // VulkanVideoSurface BGRA staging(SIMD YUV→BGRA 已在 and_yuv_*.cpp)。
        return nullptr;
    }

    if (descriptor->kind == JALIUM_VS_KIND_VK_IMAGE) {
        // Stage 5 占位:VK_KHR_external_memory_win32 (Windows D3D11 shared) /
        //                VK_EXT_external_memory_dma_buf (Linux VAAPI)。
        // 跟 AHARDWAREBUFFER 模式类似,只是 import handle 类型不同。
        return nullptr;
    }

    if (descriptor->kind == JALIUM_VS_KIND_LINUX_DMABUF) {
        auto generation = AcquireExternalImportGeneration();
        if (!generation) return nullptr;
        return VulkanImportedVideoSurface::Create(
            std::move(generation), *descriptor);
    }

    // 其它 kind:BGRA8_CPU 走 CreateVideoSurface;D3D11_SHARED / IOSurface /
    // Metal / CVPixelBuffer 是 D3D12 / Apple 后端专用。
    return nullptr;
}

// ─── Render-target draw routing ──────────────────────────────────────────

void VulkanRenderTarget::DrawVideoSurface(VideoSurface* surface,
                                           float x, float y, float w, float h,
                                           float opacity, int scalingMode)
{
    if (!surface) return;
    auto* vs = dynamic_cast<VulkanVideoSurface*>(surface);
    if (!vs) {
        auto* imported = dynamic_cast<VulkanImportedVideoSurface*>(surface);
        if (imported) {
            TouchFrame();
            (void)TryRecordGpuExternalVideoCommand(
                imported, x, y, w, h, opacity);
        }
        return;
    }
    // The wrapped bitmap already received the new pixels via Unlock; the
    // existing scaling-aware DrawBitmap path handles staging-buffer →
    // VkImage upload + sampler binding + composite.
    DrawBitmap(&vs->bitmap, x, y, w, h, opacity, scalingMode);
}

} // namespace jalium
