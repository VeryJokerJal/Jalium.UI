#include "vulkan_resources.h"
#include "vulkan_ink_layer.h"
#include "vulkan_capabilities.h"
#include "vulkan_video_format.h"
#include "vulkan_video_shaders.h"

#include <algorithm>
#include <cstring>
#include <new>

#if defined(__linux__)
#include <cerrno>
#include <fcntl.h>
#include <poll.h>
#if !defined(__ANDROID__)
#include <sys/ioctl.h>
#endif
#include <sys/stat.h>
#include <unistd.h>
#endif
#if defined(__ANDROID__)
#include <android/hardware_buffer.h>
#include <dlfcn.h>
#endif

namespace jalium {
namespace {
template <typename T>
T DeviceProc(const VulkanDeviceContext& context, const char* name)
{
    return context.getDeviceProcAddr
        ? reinterpret_cast<T>(context.getDeviceProcAddr(context.device, name)) : nullptr;
}
template <typename T>
T InstanceProc(const VulkanDeviceContext& context, const char* name)
{
    return context.getInstanceProcAddr
        ? reinterpret_cast<T>(context.getInstanceProcAddr(context.instance, name)) : nullptr;
}
using LifetimeCallback = void (*)(uint64_t);
LifetimeCallback Callback(uint64_t value)
{
    return reinterpret_cast<LifetimeCallback>(static_cast<uintptr_t>(value));
}
bool VkSuccess(VkResult result, VulkanDeviceGeneration& generation)
{
    if (result == VK_ERROR_DEVICE_LOST) generation.MarkLost();
    return result == VK_SUCCESS;
}
template <typename F> struct VideoCleanup {
    F action;
    ~VideoCleanup() { action(); }
};
template <typename F> VideoCleanup<F> Finally(F action) { return {std::move(action)}; }

#if defined(__linux__)
struct OwnedFd {
    int value = -1;
    ~OwnedFd() { if (value >= 0) close(value); }
};

#if !defined(__ANDROID__)
// dma-buf explicit synchronization uAPI. Keep the ABI local so older Linux
// headers still work; unsupported kernels fail this optional import cleanly.
struct ExportSyncFile { uint32_t flags; int32_t fd; };
constexpr unsigned long ExportSyncFileIoctl = _IOWR('b', 2, ExportSyncFile);
struct MergeSyncFiles { char name[32]; int32_t fd2; int32_t fence; uint32_t flags; uint32_t pad; };
constexpr unsigned long MergeSyncFilesIoctl = _IOWR('>', 3, MergeSyncFiles);
#endif

bool PrepareAcquireFence(VulkanDeviceGeneration& generation,
                         const JaliumVideoSurfaceDescriptor& descriptor,
                         VkSemaphore& semaphore)
{
    const auto& context = generation.ctx;
    OwnedFd fence;
    if (descriptor.acquire_fence_fd >= 0) {
        fence.value = fcntl(descriptor.acquire_fence_fd, F_DUPFD_CLOEXEC, 0);
        if (fence.value < 0) return false;
    } else if (!(descriptor.descriptor_flags & JALIUM_VS_FLAG_IMMUTABLE_READY)) {
#if defined(__ANDROID__)
        return false; // AImageReader supplies an explicit fence, never a dma-buf ioctl.
#else
        // Snapshot the producer's implicit write fences. A retained sample
        // prevents recycling, but does not by itself complete producer writes.
        // Merge every plane's fence into one GPU wait, including disjoint
        // allocations whose producer work can complete at different times.
        for (uint32_t i = 0; i < descriptor.plane_count; ++i) {
            ExportSyncFile sync{1u, -1}; // DMA_BUF_SYNC_READ
            if (ioctl(descriptor.planes[i].fd, ExportSyncFileIoctl, &sync) != 0)
                return false;
            OwnedFd planeFence;
            planeFence.value = sync.fd;
            if (i == 0) {
                fence.value = planeFence.value;
                planeFence.value = -1;
            } else {
                MergeSyncFiles merge{};
                merge.fd2 = planeFence.value;
                merge.fence = -1;
                if (ioctl(fence.value, MergeSyncFilesIoctl, &merge) != 0) return false;
                close(fence.value);
                fence.value = merge.fence;
            }
        }

#endif
    } else {
        return true;
    }

    // Polling a signalled fence avoids allocating a semaphore and also permits
    // import on devices without SYNC_FD semaphore support. Never block the UI.
    pollfd ready{fence.value, POLLIN, 0};
    int pollResult;
    do { pollResult = poll(&ready, 1, 0); } while (pollResult < 0 && errno == EINTR);
    if (pollResult < 0 || (ready.revents & (POLLERR | POLLNVAL | POLLHUP)) ||
        (pollResult && !(ready.revents & POLLIN))) return false;
    if (pollResult == 1) return true;
    if (!context.syncFdImportEnabled) return false;
    auto create = DeviceProc<PFN_vkCreateSemaphore>(context, "vkCreateSemaphore");
    auto destroy = DeviceProc<PFN_vkDestroySemaphore>(context, "vkDestroySemaphore");
    auto import = DeviceProc<PFN_vkImportSemaphoreFdKHR>(context, "vkImportSemaphoreFdKHR");
    if (!create || !destroy || !import) return false;
    VkSemaphoreCreateInfo info{VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO};
    if (!VkSuccess(create(context.device, &info, nullptr, &semaphore), generation)) return false;
    VkImportSemaphoreFdInfoKHR fdInfo{VK_STRUCTURE_TYPE_IMPORT_SEMAPHORE_FD_INFO_KHR};
    fdInfo.semaphore = semaphore;
    fdInfo.flags = VK_SEMAPHORE_IMPORT_TEMPORARY_BIT;
    fdInfo.handleType = VK_EXTERNAL_SEMAPHORE_HANDLE_TYPE_SYNC_FD_BIT;
    fdInfo.fd = fence.value;
    if (!VkSuccess(import(context.device, &fdInfo), generation)) {
        destroy(context.device, semaphore, nullptr);
        semaphore = VK_NULL_HANDLE;
        return false;
    }
    fence.value = -1; // Vulkan owns the imported fd only on success.
    return true;
}
#endif
} // namespace

std::shared_ptr<VulkanVideoSamplingResources> VulkanVideoSamplingResources::Create(
    std::shared_ptr<VulkanDeviceGeneration> generation, VkFormat format,
    uint32_t colorSpace, uint32_t flags, VkFormatFeatureFlags features,
    uint32_t descriptorCount,
    const VkSamplerYcbcrConversionCreateInfo* conversionOverride,
    uint64_t externalFormat)
{
    const bool ycbcr = conversionOverride || format == VK_FORMAT_G8_B8R8_2PLANE_420_UNORM ||
        format == VK_FORMAT_G10X6_B10X6R10X6_2PLANE_420_UNORM_3PACK16;
    if (!generation || (ycbcr && !generation->ctx.samplerYcbcrEnabled) || !generation->IsUsable() ||
        !descriptorCount || descriptorCount > 16) return {};
    std::lock_guard<std::recursive_mutex> lock(generation->driverCallMutex);
    if (!generation->IsUsable()) return {};
    VkSamplerYcbcrConversionCreateInfo conversionInfo{VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_CREATE_INFO};
    conversionInfo.format = format;
    conversionInfo.ycbcrModel = vulkan_video::Model(colorSpace);
    conversionInfo.ycbcrRange = flags & JALIUM_VS_FLAG_FULL_RANGE
        ? VK_SAMPLER_YCBCR_RANGE_ITU_FULL : VK_SAMPLER_YCBCR_RANGE_ITU_NARROW;
    conversionInfo.components = {VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY,
        VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY};
    conversionInfo.xChromaOffset = flags & JALIUM_VS_FLAG_CHROMA_COSITED_X
        ? VK_CHROMA_LOCATION_COSITED_EVEN : VK_CHROMA_LOCATION_MIDPOINT;
    conversionInfo.yChromaOffset = flags & JALIUM_VS_FLAG_CHROMA_COSITED_Y
        ? VK_CHROMA_LOCATION_COSITED_EVEN : VK_CHROMA_LOCATION_MIDPOINT;
    if (conversionOverride) conversionInfo = *conversionOverride;
    const bool cositedX = conversionInfo.xChromaOffset == VK_CHROMA_LOCATION_COSITED_EVEN;
    const bool cositedY = conversionInfo.yChromaOffset == VK_CHROMA_LOCATION_COSITED_EVEN;
    if (ycbcr && (((cositedX || cositedY) && !(features & VK_FORMAT_FEATURE_COSITED_CHROMA_SAMPLES_BIT)) ||
        ((!cositedX || !cositedY) && !(features & VK_FORMAT_FEATURE_MIDPOINT_CHROMA_SAMPLES_BIT)))) return {};
    const bool linear = (features & VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT) &&
        (!ycbcr || (features & VK_FORMAT_FEATURE_SAMPLED_IMAGE_YCBCR_CONVERSION_LINEAR_FILTER_BIT));
    // External format identifiers and every conversion field participate in the
    // key: two decoders can use one allocation format with different colorimetry.
    std::string key;
    for (uint64_t part : {uint64_t(format), externalFormat, uint64_t(ycbcr),
            uint64_t(conversionInfo.ycbcrModel), uint64_t(conversionInfo.ycbcrRange),
            uint64_t(conversionInfo.components.r), uint64_t(conversionInfo.components.g),
            uint64_t(conversionInfo.components.b), uint64_t(conversionInfo.components.a),
            uint64_t(conversionInfo.xChromaOffset), uint64_t(conversionInfo.yChromaOffset),
            uint64_t(linear), uint64_t(descriptorCount)}) {
        key += std::to_string(part); key += ':';
    }
    auto& cached = generation->videoSamplers[key];
    if (auto shared = cached.lock()) return shared;
    auto resources = std::make_shared<VulkanVideoSamplingResources>();
    resources->generation_ = generation;
    resources->descriptorCount = descriptorCount;
    const auto& context = generation->ctx;
    auto createConversion = DeviceProc<PFN_vkCreateSamplerYcbcrConversion>(context, "vkCreateSamplerYcbcrConversion");
    auto createSampler = DeviceProc<PFN_vkCreateSampler>(context, "vkCreateSampler");
    auto createLayout = DeviceProc<PFN_vkCreateDescriptorSetLayout>(context, "vkCreateDescriptorSetLayout");
    if ((ycbcr && !createConversion) || !createSampler || !createLayout) return {};
    conversionInfo.chromaFilter = linear ? VK_FILTER_LINEAR : VK_FILTER_NEAREST;
    if (ycbcr && !VkSuccess(createConversion(context.device, &conversionInfo, nullptr, &resources->conversion), *generation)) return {};
    VkSamplerYcbcrConversionInfo conversion{VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_INFO};
    conversion.conversion = resources->conversion;
    VkSamplerCreateInfo samplerInfo{VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO};
    samplerInfo.pNext = ycbcr ? &conversion : nullptr;
    samplerInfo.minFilter = samplerInfo.magFilter = conversionInfo.chromaFilter;
    samplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
    samplerInfo.addressModeU = samplerInfo.addressModeV = samplerInfo.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    if (!VkSuccess(createSampler(context.device, &samplerInfo, nullptr, &resources->sampler), *generation)) return {};
    VkDescriptorSetLayoutBinding binding{};
    binding.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    binding.descriptorCount = 1;
    binding.stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    binding.pImmutableSamplers = &resources->sampler;
    VkDescriptorSetLayoutCreateInfo layoutInfo{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};
    layoutInfo.bindingCount = 1;
    layoutInfo.pBindings = &binding;
    if (!VkSuccess(createLayout(context.device, &layoutInfo, nullptr, &resources->layout), *generation)) return {};
    // Purge expired entries so streams changing metadata do not grow the cache.
    for (auto it = generation->videoSamplers.begin(); it != generation->videoSamplers.end();) {
        if (it->first != key && it->second.expired()) it = generation->videoSamplers.erase(it);
        else ++it;
    }
    generation->videoSamplers[key] = resources;
    return resources;
}

VulkanVideoSamplingResources::~VulkanVideoSamplingResources()
{
    if (!generation_) return;
    std::lock_guard<std::recursive_mutex> lock(generation_->driverCallMutex);
    if (generation_->IsAbandoned()) return;
    const auto& context = generation_->ctx;
    auto destroyLayout = DeviceProc<PFN_vkDestroyDescriptorSetLayout>(context, "vkDestroyDescriptorSetLayout");
    auto destroySampler = DeviceProc<PFN_vkDestroySampler>(context, "vkDestroySampler");
    auto destroyConversion = DeviceProc<PFN_vkDestroySamplerYcbcrConversion>(context, "vkDestroySamplerYcbcrConversion");
    if (layout && destroyLayout) destroyLayout(context.device, layout, nullptr);
    if (sampler && destroySampler) destroySampler(context.device, sampler, nullptr);
    if (conversion && destroyConversion) destroyConversion(context.device, conversion, nullptr);
}

#if defined(_WIN32) || defined(JALIUM_VULKAN_TEST_WIN32_IMPORT)
VulkanImportedVideoSurface* VulkanImportedVideoSurface::CreateWin32(
    std::shared_ptr<VulkanDeviceGeneration> generation,
    const JaliumVideoSurfaceDescriptor& descriptor)
{
    if (!generation || !generation->ctx.valid || !generation->ctx.win32ImportEnabled ||
        descriptor.kind != JALIUM_VS_KIND_D3D11_SHARED || !descriptor.handle0 ||
        !descriptor.width || !descriptor.height ||
        !(descriptor.descriptor_flags & JALIUM_VS_FLAG_IMMUTABLE_READY) ||
        (descriptor.descriptor_flags & JALIUM_VS_FLAG_HDR_TRANSFER) ||
        (descriptor.handle1 && descriptor.handle1 != GetCurrentProcessId())) return nullptr;
    VkFormat format = VK_FORMAT_UNDEFINED;
    bool opaque = false, ycbcr = false;
    switch (descriptor.format_hint) {
    case JALIUM_VS_FORMAT_BGRX8: opaque = true; [[fallthrough]];
    case JALIUM_VS_FORMAT_BGRA8: format = VK_FORMAT_B8G8R8A8_UNORM; break;
    case JALIUM_VS_FORMAT_NV12: format = VK_FORMAT_G8_B8R8_2PLANE_420_UNORM; ycbcr = true; break;
    case JALIUM_VS_FORMAT_P010: format = VK_FORMAT_G10X6_B10X6R10X6_2PLANE_420_UNORM_3PACK16; ycbcr = true; break;
    default: return nullptr;
    }
    if (ycbcr && ((descriptor.width | descriptor.height) & 1u)) return nullptr;
    if (ycbcr && (descriptor.color_space < JALIUM_VS_COLOR_BT601 ||
                  descriptor.color_space > JALIUM_VS_COLOR_BT2020)) return nullptr;
    std::unique_lock<std::recursive_mutex> lock(generation->driverCallMutex);
    if (!generation->IsUsable() || (ycbcr && !generation->ctx.samplerYcbcrEnabled)) return nullptr;
    const auto& context = generation->ctx;
    auto getFormat = InstanceProc<PFN_vkGetPhysicalDeviceFormatProperties2>(context, "vkGetPhysicalDeviceFormatProperties2");
    auto getImageFormat = InstanceProc<PFN_vkGetPhysicalDeviceImageFormatProperties2>(context, "vkGetPhysicalDeviceImageFormatProperties2");
    auto getHandleProperties = DeviceProc<PFN_vkGetMemoryWin32HandlePropertiesKHR>(context, "vkGetMemoryWin32HandlePropertiesKHR");
    auto createImage = DeviceProc<PFN_vkCreateImage>(context, "vkCreateImage");
    auto getRequirements = DeviceProc<PFN_vkGetImageMemoryRequirements2>(context, "vkGetImageMemoryRequirements2");
    auto allocate = DeviceProc<PFN_vkAllocateMemory>(context, "vkAllocateMemory");
    auto bind = DeviceProc<PFN_vkBindImageMemory>(context, "vkBindImageMemory");
    if (!getFormat || !getImageFormat || !getHandleProperties || !createImage ||
        !getRequirements || !allocate || !bind) return nullptr;
    VkFormatProperties2 properties{VK_STRUCTURE_TYPE_FORMAT_PROPERTIES_2};
    getFormat(context.physicalDevice, format, &properties);
    const auto features = properties.formatProperties.optimalTilingFeatures;
    if (!(features & VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT)) return nullptr;
    VkPhysicalDeviceExternalImageFormatInfo externalQuery{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_EXTERNAL_IMAGE_FORMAT_INFO};
    externalQuery.handleType = VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D11_TEXTURE_BIT;
    VkPhysicalDeviceImageFormatInfo2 query{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGE_FORMAT_INFO_2};
    query.pNext = &externalQuery;
    query.format = format; query.type = VK_IMAGE_TYPE_2D;
    query.tiling = VK_IMAGE_TILING_OPTIMAL; query.usage = VK_IMAGE_USAGE_SAMPLED_BIT;
    VkSamplerYcbcrConversionImageFormatProperties conversionProperties{VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_IMAGE_FORMAT_PROPERTIES};
    VkExternalImageFormatProperties externalProperties{VK_STRUCTURE_TYPE_EXTERNAL_IMAGE_FORMAT_PROPERTIES};
    externalProperties.pNext = ycbcr ? &conversionProperties : nullptr;
    VkImageFormatProperties2 imageProperties{VK_STRUCTURE_TYPE_IMAGE_FORMAT_PROPERTIES_2};
    imageProperties.pNext = &externalProperties;
    if (!VkSuccess(getImageFormat(context.physicalDevice, &query, &imageProperties), *generation) ||
        !(externalProperties.externalMemoryProperties.externalMemoryFeatures & VK_EXTERNAL_MEMORY_FEATURE_IMPORTABLE_BIT) ||
        descriptor.width > imageProperties.imageFormatProperties.maxExtent.width ||
        descriptor.height > imageProperties.imageFormatProperties.maxExtent.height) return nullptr;
    // NT handles are not consumed by Vulkan. Duplicate the borrowed handle so
    // every early exit closes exactly our own reference, never the decoder's.
    HANDLE handle = nullptr;
    if (!DuplicateHandle(GetCurrentProcess(), reinterpret_cast<HANDLE>(static_cast<uintptr_t>(descriptor.handle0)),
            GetCurrentProcess(), &handle, 0, FALSE, DUPLICATE_SAME_ACCESS)) return nullptr;
    auto closeHandle = Finally([&] { CloseHandle(handle); });
    VkMemoryWin32HandlePropertiesKHR handleProperties{VK_STRUCTURE_TYPE_MEMORY_WIN32_HANDLE_PROPERTIES_KHR};
    if (!VkSuccess(getHandleProperties(context.device, VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D11_TEXTURE_BIT,
            handle, &handleProperties), *generation)) return nullptr;
    std::unique_ptr<VulkanImportedVideoSurface> surface(new (std::nothrow) VulkanImportedVideoSurface());
    if (!surface) return nullptr;
    surface->generation_ = generation; surface->kind_ = descriptor.kind;
    surface->width_ = descriptor.width; surface->height_ = descriptor.height;
    surface->sampling_ = VulkanVideoSamplingResources::Create(generation, format,
        descriptor.color_space, descriptor.descriptor_flags, features,
        ycbcr ? conversionProperties.combinedImageSamplerDescriptorCount : 1);
    if (!surface->sampling_) return nullptr;
    VkExternalMemoryImageCreateInfo external{VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO};
    external.handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D11_TEXTURE_BIT;
    VkImageCreateInfo image{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO};
    image.pNext = &external; image.imageType = VK_IMAGE_TYPE_2D; image.format = format;
    image.extent = {descriptor.width, descriptor.height, 1};
    image.mipLevels = image.arrayLayers = 1; image.samples = VK_SAMPLE_COUNT_1_BIT;
    image.tiling = VK_IMAGE_TILING_OPTIMAL; image.usage = VK_IMAGE_USAGE_SAMPLED_BIT;
    if (!VkSuccess(createImage(context.device, &image, nullptr, &surface->image_), *generation)) return nullptr;
    VkImageMemoryRequirementsInfo2 requirementsInfo{VK_STRUCTURE_TYPE_IMAGE_MEMORY_REQUIREMENTS_INFO_2};
    requirementsInfo.image = surface->image_;
    VkMemoryRequirements2 requirements{VK_STRUCTURE_TYPE_MEMORY_REQUIREMENTS_2};
    getRequirements(context.device, &requirementsInfo, &requirements);
    const uint32_t compatible = requirements.memoryRequirements.memoryTypeBits & handleProperties.memoryTypeBits;
    if (!compatible) return nullptr;
    uint32_t memoryType = 0;
    while (!(compatible & (1u << memoryType))) ++memoryType;
    VkMemoryDedicatedAllocateInfo dedicated{VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO};
    dedicated.image = surface->image_;
    VkImportMemoryWin32HandleInfoKHR import{VK_STRUCTURE_TYPE_IMPORT_MEMORY_WIN32_HANDLE_INFO_KHR};
    import.pNext = &dedicated; import.handleType = VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D11_TEXTURE_BIT;
    import.handle = handle;
    VkMemoryAllocateInfo allocation{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};
    allocation.pNext = &import; allocation.allocationSize = requirements.memoryRequirements.size;
    allocation.memoryTypeIndex = memoryType;
    if (!VkSuccess(allocate(context.device, &allocation, nullptr, &surface->memories_[0]), *generation) ||
        !VkSuccess(bind(context.device, surface->image_, surface->memories_[0], 0), *generation) ||
        !surface->CreateSamplingView(format, opaque)) return nullptr;
    lock.unlock();
    surface->RetainProducer(descriptor);
    return surface.release();
}
#endif

#if defined(__ANDROID__)
VulkanImportedVideoSurface* VulkanImportedVideoSurface::CreateAndroid(
    std::shared_ptr<VulkanDeviceGeneration> generation,
    const JaliumVideoSurfaceDescriptor& descriptor)
{
    if (!generation || !generation->ctx.valid || !generation->ctx.androidHardwareBufferEnabled ||
        descriptor.kind != JALIUM_VS_KIND_AHARDWAREBUFFER || !descriptor.handle0 ||
        !generation->ctx.foreignQueueEnabled || !descriptor.width || !descriptor.height ||
        (descriptor.descriptor_flags & JALIUM_VS_FLAG_HDR_TRANSFER)) return nullptr;
    if (!(descriptor.descriptor_flags & JALIUM_VS_FLAG_IMMUTABLE_READY) &&
        (!descriptor.lifetime_context || !descriptor.lifetime_retain_callback ||
         !descriptor.lifetime_release_callback)) return nullptr;
    // AHardwareBuffer functions arrived in API 26. Keep the API 24 payload
    // loadable; the decoder uses the same runtime gate for its ImageReader.
    auto describe = reinterpret_cast<void (*)(const AHardwareBuffer*, AHardwareBuffer_Desc*)>(dlsym(RTLD_DEFAULT, "AHardwareBuffer_describe"));
    if (!describe) return nullptr;
    auto* buffer = reinterpret_cast<AHardwareBuffer*>(static_cast<uintptr_t>(descriptor.handle0));
    AHardwareBuffer_Desc description{};
    describe(buffer, &description);
    if (description.width != descriptor.width || description.height != descriptor.height ||
        description.layers != 1 || !(description.usage & AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE) ||
        (description.usage & (AHARDWAREBUFFER_USAGE_PROTECTED_CONTENT | AHARDWAREBUFFER_USAGE_GPU_MIPMAP_COMPLETE))) return nullptr;
    std::unique_lock<std::recursive_mutex> lock(generation->driverCallMutex);
    if (!generation->IsUsable()) return nullptr;
    const auto& context = generation->ctx;
    auto getProperties = DeviceProc<PFN_vkGetAndroidHardwareBufferPropertiesANDROID>(context, "vkGetAndroidHardwareBufferPropertiesANDROID");
    auto getFormat = InstanceProc<PFN_vkGetPhysicalDeviceFormatProperties2>(context, "vkGetPhysicalDeviceFormatProperties2");
    auto getImageFormat = InstanceProc<PFN_vkGetPhysicalDeviceImageFormatProperties2>(context, "vkGetPhysicalDeviceImageFormatProperties2");
    auto createImage = DeviceProc<PFN_vkCreateImage>(context, "vkCreateImage");
    auto allocate = DeviceProc<PFN_vkAllocateMemory>(context, "vkAllocateMemory");
    auto bind = DeviceProc<PFN_vkBindImageMemory>(context, "vkBindImageMemory");
    if (!getProperties || !getFormat || !getImageFormat || !createImage || !allocate || !bind) return nullptr;
    VkAndroidHardwareBufferFormatPropertiesANDROID formatProperties{VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_FORMAT_PROPERTIES_ANDROID};
    VkAndroidHardwareBufferPropertiesANDROID properties{VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_PROPERTIES_ANDROID};
    properties.pNext = &formatProperties;
    if (!VkSuccess(getProperties(context.device, buffer, &properties), *generation) ||
        !properties.memoryTypeBits || !properties.allocationSize) return nullptr;
    const bool externalFormat = formatProperties.format == VK_FORMAT_UNDEFINED;
    if (externalFormat && !formatProperties.externalFormat) return nullptr;
    const bool ycbcr = externalFormat ||
        formatProperties.format == VK_FORMAT_G8_B8R8_2PLANE_420_UNORM ||
        formatProperties.format == VK_FORMAT_G8_B8_R8_3PLANE_420_UNORM ||
        formatProperties.format == VK_FORMAT_G10X6_B10X6R10X6_2PLANE_420_UNORM_3PACK16;
    if (ycbcr && !context.samplerYcbcrEnabled) return nullptr;
    if (ycbcr && !externalFormat && ((descriptor.width | descriptor.height) & 1u)) return nullptr;
    auto getPhysicalProperties = InstanceProc<PFN_vkGetPhysicalDeviceProperties>(context, "vkGetPhysicalDeviceProperties");
    if (!getPhysicalProperties) return nullptr;
    VkPhysicalDeviceProperties physicalProperties{};
    getPhysicalProperties(context.physicalDevice, &physicalProperties);
    if (descriptor.width > physicalProperties.limits.maxImageDimension2D ||
        descriptor.height > physicalProperties.limits.maxImageDimension2D) return nullptr;
    VkFormatFeatureFlags features = formatProperties.formatFeatures;
    uint32_t descriptorCount = externalFormat ? context.externalFormatDescriptorCount : 1;
    VkExternalMemoryImageCreateInfo external{VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO};
    external.handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID;
    VkExternalFormatANDROID androidFormat{VK_STRUCTURE_TYPE_EXTERNAL_FORMAT_ANDROID};
    androidFormat.externalFormat = externalFormat ? formatProperties.externalFormat : 0;
    external.pNext = externalFormat ? &androidFormat : nullptr;
    if (!externalFormat) {
        VkFormatProperties2 format{VK_STRUCTURE_TYPE_FORMAT_PROPERTIES_2};
        getFormat(context.physicalDevice, formatProperties.format, &format);
        features = format.formatProperties.optimalTilingFeatures;
        VkPhysicalDeviceExternalImageFormatInfo externalQuery{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_EXTERNAL_IMAGE_FORMAT_INFO};
        externalQuery.handleType = VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID;
        VkPhysicalDeviceImageFormatInfo2 query{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGE_FORMAT_INFO_2};
        query.pNext = &externalQuery; query.format = formatProperties.format;
        query.type = VK_IMAGE_TYPE_2D; query.tiling = VK_IMAGE_TILING_OPTIMAL; query.usage = VK_IMAGE_USAGE_SAMPLED_BIT;
        VkSamplerYcbcrConversionImageFormatProperties conversionProperties{VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_IMAGE_FORMAT_PROPERTIES};
        VkExternalImageFormatProperties externalProperties{VK_STRUCTURE_TYPE_EXTERNAL_IMAGE_FORMAT_PROPERTIES};
        externalProperties.pNext = ycbcr ? &conversionProperties : nullptr;
        VkImageFormatProperties2 imageProperties{VK_STRUCTURE_TYPE_IMAGE_FORMAT_PROPERTIES_2};
        imageProperties.pNext = &externalProperties;
        if (!VkSuccess(getImageFormat(context.physicalDevice, &query, &imageProperties), *generation) ||
            !(externalProperties.externalMemoryProperties.externalMemoryFeatures & VK_EXTERNAL_MEMORY_FEATURE_IMPORTABLE_BIT) ||
            descriptor.width > imageProperties.imageFormatProperties.maxExtent.width ||
            descriptor.height > imageProperties.imageFormatProperties.maxExtent.height) return nullptr;
        if (ycbcr) descriptorCount = conversionProperties.combinedImageSamplerDescriptorCount;
    }
    if (!(features & VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT)) return nullptr;
    VkSamplerYcbcrConversionCreateInfo conversion{VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_CREATE_INFO};
    conversion.pNext = externalFormat ? &androidFormat : nullptr;
    conversion.format = formatProperties.format;
    conversion.components = formatProperties.samplerYcbcrConversionComponents;
    conversion.ycbcrModel = formatProperties.suggestedYcbcrModel;
    conversion.ycbcrRange = formatProperties.suggestedYcbcrRange;
    conversion.xChromaOffset = formatProperties.suggestedXChromaOffset;
    conversion.yChromaOffset = formatProperties.suggestedYChromaOffset;
    // Codec metadata is authoritative; gralloc's suggestions are only defaults.
    if (descriptor.color_space) {
        if (descriptor.color_space > JALIUM_VS_COLOR_BT2020) return nullptr;
        conversion.ycbcrModel = vulkan_video::Model(descriptor.color_space);
        conversion.ycbcrRange = descriptor.descriptor_flags & JALIUM_VS_FLAG_FULL_RANGE
            ? VK_SAMPLER_YCBCR_RANGE_ITU_FULL : VK_SAMPLER_YCBCR_RANGE_ITU_NARROW;
    }
    if (descriptor.descriptor_flags & JALIUM_VS_FLAG_FULL_RANGE)
        conversion.ycbcrRange = VK_SAMPLER_YCBCR_RANGE_ITU_FULL;
    std::unique_ptr<VulkanImportedVideoSurface> surface(new (std::nothrow) VulkanImportedVideoSurface());
    if (!surface) return nullptr;
    surface->generation_ = generation; surface->kind_ = descriptor.kind;
    surface->width_ = descriptor.width; surface->height_ = descriptor.height;
    surface->externalQueueFamily_ = VK_QUEUE_FAMILY_FOREIGN_EXT;
    surface->sampling_ = VulkanVideoSamplingResources::Create(generation, formatProperties.format,
        descriptor.color_space, descriptor.descriptor_flags, features, descriptorCount,
        ycbcr ? &conversion : nullptr, androidFormat.externalFormat);
    if (!surface->sampling_ || !PrepareAcquireFence(*generation, descriptor, surface->acquireSemaphore_)) return nullptr;
    VkImageCreateInfo image{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO};
    image.pNext = &external; image.imageType = VK_IMAGE_TYPE_2D; image.format = formatProperties.format;
    image.extent = {descriptor.width, descriptor.height, 1};
    image.mipLevels = image.arrayLayers = 1; image.samples = VK_SAMPLE_COUNT_1_BIT;
    image.tiling = VK_IMAGE_TILING_OPTIMAL; image.usage = VK_IMAGE_USAGE_SAMPLED_BIT;
    if (!VkSuccess(createImage(context.device, &image, nullptr, &surface->image_), *generation)) return nullptr;
    // AHB requirements are queried from the buffer, not from an unbound image.
    // Android external images always use a dedicated allocation of that size.
    uint32_t memoryType = 0;
    while (!(properties.memoryTypeBits & (1u << memoryType))) ++memoryType;
    VkMemoryDedicatedAllocateInfo dedicated{VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO};
    dedicated.image = surface->image_;
    VkImportAndroidHardwareBufferInfoANDROID import{VK_STRUCTURE_TYPE_IMPORT_ANDROID_HARDWARE_BUFFER_INFO_ANDROID};
    import.pNext = &dedicated; import.buffer = buffer;
    VkMemoryAllocateInfo allocation{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};
    allocation.pNext = &import; allocation.allocationSize = properties.allocationSize;
    allocation.memoryTypeIndex = memoryType;
    if (!VkSuccess(allocate(context.device, &allocation, nullptr, &surface->memories_[0]), *generation) ||
        !VkSuccess(bind(context.device, surface->image_, surface->memories_[0], 0), *generation) ||
        !surface->CreateSamplingView(formatProperties.format, false)) return nullptr;
    lock.unlock();
    surface->RetainProducer(descriptor);
    return surface.release();
}
#endif

VulkanImportedVideoSurface* VulkanImportedVideoSurface::Create(
    std::shared_ptr<VulkanDeviceGeneration> generation,
    const JaliumVideoSurfaceDescriptor& descriptor)
{
#if defined(_WIN32) || defined(JALIUM_VULKAN_TEST_WIN32_IMPORT)
    return CreateWin32(std::move(generation), descriptor);
#elif defined(__ANDROID__)
    return CreateAndroid(std::move(generation), descriptor);
#elif defined(__linux__)
    vulkan_video::Format format;
    if (!generation || !generation->ctx.valid || !generation->ctx.dmaBufImportEnabled ||
        !vulkan_video::Describe(descriptor, format)) return nullptr;
    std::unique_lock<std::recursive_mutex> lock(generation->driverCallMutex);
    if (!generation->IsUsable()) return nullptr;
    const auto& context = generation->ctx;
    if (format.ycbcr && !context.samplerYcbcrEnabled) return nullptr;
    const bool modifierTiling = context.drmModifierEnabled;
    if (!modifierTiling && (descriptor.planes[0].modifier != 0 ||
                           descriptor.plane_count != format.planes)) return nullptr;

    // Duped descriptors may name the same allocation with different fd values.
    // Only distinct allocations require DISJOINT image memory binding.
    struct stat allocationStat[JALIUM_VIDEO_SURFACE_MAX_PLANES]{};
    bool disjoint = false;
    for (uint32_t i = 0; i < descriptor.plane_count; ++i) {
        if (fstat(descriptor.planes[i].fd, &allocationStat[i]) != 0) return nullptr;
        if (i && (allocationStat[i].st_dev != allocationStat[0].st_dev ||
                  allocationStat[i].st_ino != allocationStat[0].st_ino)) disjoint = true;
    }

    auto getFormatProperties = InstanceProc<PFN_vkGetPhysicalDeviceFormatProperties2>(context, "vkGetPhysicalDeviceFormatProperties2");
    auto getImageFormat = InstanceProc<PFN_vkGetPhysicalDeviceImageFormatProperties2>(context, "vkGetPhysicalDeviceImageFormatProperties2");
    auto createImage = DeviceProc<PFN_vkCreateImage>(context, "vkCreateImage");
    auto getRequirements = DeviceProc<PFN_vkGetImageMemoryRequirements2>(context, "vkGetImageMemoryRequirements2");
    auto getLayout = DeviceProc<PFN_vkGetImageSubresourceLayout>(context, "vkGetImageSubresourceLayout");
    auto getFdProperties = DeviceProc<PFN_vkGetMemoryFdPropertiesKHR>(context, "vkGetMemoryFdPropertiesKHR");
    auto getMemoryProperties = InstanceProc<PFN_vkGetPhysicalDeviceMemoryProperties>(context, "vkGetPhysicalDeviceMemoryProperties");
    auto allocate = DeviceProc<PFN_vkAllocateMemory>(context, "vkAllocateMemory");
    auto bind = DeviceProc<PFN_vkBindImageMemory2>(context, "vkBindImageMemory2");
    auto createView = DeviceProc<PFN_vkCreateImageView>(context, "vkCreateImageView");
    auto createPool = DeviceProc<PFN_vkCreateDescriptorPool>(context, "vkCreateDescriptorPool");
    auto allocateSets = DeviceProc<PFN_vkAllocateDescriptorSets>(context, "vkAllocateDescriptorSets");
    auto writeSets = DeviceProc<PFN_vkUpdateDescriptorSets>(context, "vkUpdateDescriptorSets");
    if (!getFormatProperties || !getImageFormat || !createImage || !getRequirements ||
        !getLayout || !getFdProperties || !getMemoryProperties || !allocate || !bind ||
        !createView || !createPool || !allocateSets || !writeSets) return nullptr;

    VkFormatProperties2 properties{VK_STRUCTURE_TYPE_FORMAT_PROPERTIES_2};
    VkFormatFeatureFlags features = 0;
    uint32_t memoryPlaneCount = format.planes;
    if (modifierTiling) {
        VkDrmFormatModifierPropertiesListEXT list{VK_STRUCTURE_TYPE_DRM_FORMAT_MODIFIER_PROPERTIES_LIST_EXT};
        properties.pNext = &list;
        std::vector<VkDrmFormatModifierPropertiesEXT> modifiers;
        if (vulkan_capabilities::Enumerate(modifiers,
            [&](uint32_t* count, VkDrmFormatModifierPropertiesEXT* values) {
                const uint32_t capacity = *count;
                list.drmFormatModifierCount = capacity;
                list.pDrmFormatModifierProperties = values;
                getFormatProperties(context.physicalDevice, format.vk, &properties);
                *count = list.drmFormatModifierCount;
                return values && *count > capacity ? VK_INCOMPLETE : VK_SUCCESS;
            }) != VK_SUCCESS) return nullptr;
        bool found = false;
        for (const auto& modifier : modifiers) {
            if (modifier.drmFormatModifier == descriptor.planes[0].modifier) {
                features = modifier.drmFormatModifierTilingFeatures;
                memoryPlaneCount = modifier.drmFormatModifierPlaneCount;
                found = true;
                break;
            }
        }
        if (!found || memoryPlaneCount != descriptor.plane_count) return nullptr;
    } else {
        getFormatProperties(context.physicalDevice, format.vk, &properties);
        features = properties.formatProperties.linearTilingFeatures;
    }
    if (!(features & VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT) ||
        (disjoint && !(features & VK_FORMAT_FEATURE_DISJOINT_BIT))) return nullptr;

    VkPhysicalDeviceImageDrmFormatModifierInfoEXT modifierQuery{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGE_DRM_FORMAT_MODIFIER_INFO_EXT};
    modifierQuery.drmFormatModifier = descriptor.planes[0].modifier;
    modifierQuery.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    VkPhysicalDeviceExternalImageFormatInfo externalQuery{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_EXTERNAL_IMAGE_FORMAT_INFO};
    externalQuery.handleType = VK_EXTERNAL_MEMORY_HANDLE_TYPE_DMA_BUF_BIT_EXT;
    externalQuery.pNext = modifierTiling ? &modifierQuery : nullptr;
    VkPhysicalDeviceImageFormatInfo2 query{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGE_FORMAT_INFO_2};
    query.pNext = &externalQuery;
    query.format = format.vk;
    query.type = VK_IMAGE_TYPE_2D;
    query.tiling = modifierTiling ? VK_IMAGE_TILING_DRM_FORMAT_MODIFIER_EXT : VK_IMAGE_TILING_LINEAR;
    query.usage = VK_IMAGE_USAGE_SAMPLED_BIT;
    query.flags = disjoint ? VK_IMAGE_CREATE_DISJOINT_BIT : 0;
    VkSamplerYcbcrConversionImageFormatProperties ycbcrProperties{VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_IMAGE_FORMAT_PROPERTIES};
    VkExternalImageFormatProperties externalProperties{VK_STRUCTURE_TYPE_EXTERNAL_IMAGE_FORMAT_PROPERTIES};
    externalProperties.pNext = format.ycbcr ? &ycbcrProperties : nullptr;
    VkImageFormatProperties2 imageProperties{VK_STRUCTURE_TYPE_IMAGE_FORMAT_PROPERTIES_2};
    imageProperties.pNext = &externalProperties;
    if (getImageFormat(context.physicalDevice, &query, &imageProperties) != VK_SUCCESS ||
        !(externalProperties.externalMemoryProperties.externalMemoryFeatures & VK_EXTERNAL_MEMORY_FEATURE_IMPORTABLE_BIT) ||
        descriptor.width > imageProperties.imageFormatProperties.maxExtent.width ||
        descriptor.height > imageProperties.imageFormatProperties.maxExtent.height) return nullptr;
    // Dedicated allocations cannot be bound one plane at a time.
    const bool dedicatedOnly = (externalProperties.externalMemoryProperties.externalMemoryFeatures &
        VK_EXTERNAL_MEMORY_FEATURE_DEDICATED_ONLY_BIT) != 0;
    if (disjoint && dedicatedOnly) return nullptr;

    std::unique_ptr<VulkanImportedVideoSurface> surface(new (std::nothrow) VulkanImportedVideoSurface());
    if (!surface) return nullptr;
    surface->generation_ = generation;
    surface->width_ = descriptor.width;
    surface->height_ = descriptor.height;
    surface->externalQueueFamily_ = context.foreignQueueEnabled
        ? VK_QUEUE_FAMILY_FOREIGN_EXT : VK_QUEUE_FAMILY_EXTERNAL;
    {
        surface->sampling_ = VulkanVideoSamplingResources::Create(generation, format.vk,
            descriptor.color_space, descriptor.descriptor_flags, features,
            format.ycbcr ? ycbcrProperties.combinedImageSamplerDescriptorCount : 1);
        if (!surface->sampling_) return nullptr;
    }
    if (!PrepareAcquireFence(*generation, descriptor, surface->acquireSemaphore_)) return nullptr;

    VkSubresourceLayout planeLayouts[JALIUM_VIDEO_SURFACE_MAX_PLANES]{};
    for (uint32_t i = 0; i < memoryPlaneCount; ++i) {
        planeLayouts[i].offset = descriptor.planes[i].offset_bytes;
        planeLayouts[i].rowPitch = descriptor.planes[i].stride_bytes;
        // size, arrayPitch and depthPitch must be zero for explicit modifiers.
    }
    VkImageDrmFormatModifierExplicitCreateInfoEXT modifierInfo{VK_STRUCTURE_TYPE_IMAGE_DRM_FORMAT_MODIFIER_EXPLICIT_CREATE_INFO_EXT};
    modifierInfo.drmFormatModifier = descriptor.planes[0].modifier;
    modifierInfo.drmFormatModifierPlaneCount = memoryPlaneCount;
    modifierInfo.pPlaneLayouts = planeLayouts;
    VkExternalMemoryImageCreateInfo externalInfo{VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO};
    externalInfo.pNext = modifierTiling ? &modifierInfo : nullptr;
    externalInfo.handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_DMA_BUF_BIT_EXT;
    VkImageCreateInfo imageInfo{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO};
    imageInfo.pNext = &externalInfo;
    imageInfo.flags = query.flags;
    imageInfo.imageType = VK_IMAGE_TYPE_2D;
    imageInfo.format = format.vk;
    imageInfo.extent = {descriptor.width, descriptor.height, 1};
    imageInfo.mipLevels = imageInfo.arrayLayers = 1;
    imageInfo.samples = VK_SAMPLE_COUNT_1_BIT;
    imageInfo.tiling = query.tiling;
    imageInfo.usage = VK_IMAGE_USAGE_SAMPLED_BIT;
    imageInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    imageInfo.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    if (!VkSuccess(createImage(context.device, &imageInfo, nullptr, &surface->image_), *generation)) return nullptr;

    if (!modifierTiling) {
        // LINEAR does not accept explicit pitches/offsets. Import only when
        // the producer layout exactly matches the implementation's layout.
        for (uint32_t i = 0; i < format.planes; ++i) {
            VkImageSubresource subresource{};
            subresource.aspectMask = format.ycbcr
                ? (VK_IMAGE_ASPECT_PLANE_0_BIT << i) : VK_IMAGE_ASPECT_COLOR_BIT;
            VkSubresourceLayout layout{};
            getLayout(context.device, surface->image_, &subresource, &layout);
            if (layout.offset != descriptor.planes[i].offset_bytes ||
                layout.rowPitch != descriptor.planes[i].stride_bytes) return nullptr;
        }
    }

    VkPhysicalDeviceMemoryProperties memoryProperties{};
    getMemoryProperties(context.physicalDevice, &memoryProperties);
    VkBindImageMemoryInfo bindings[JALIUM_VIDEO_SURFACE_MAX_PLANES]{};
    VkBindImagePlaneMemoryInfo planeBindings[JALIUM_VIDEO_SURFACE_MAX_PLANES]{};
    const uint32_t allocationCount = disjoint ? memoryPlaneCount : 1;
    for (uint32_t i = 0; i < allocationCount; ++i) {
        const auto planeAspect = static_cast<VkImageAspectFlagBits>(
            (modifierTiling ? VK_IMAGE_ASPECT_MEMORY_PLANE_0_BIT_EXT : VK_IMAGE_ASPECT_PLANE_0_BIT) << i);
        VkImagePlaneMemoryRequirementsInfo planeRequirements{VK_STRUCTURE_TYPE_IMAGE_PLANE_MEMORY_REQUIREMENTS_INFO};
        planeRequirements.planeAspect = planeAspect;
        VkImageMemoryRequirementsInfo2 requirementsInfo{VK_STRUCTURE_TYPE_IMAGE_MEMORY_REQUIREMENTS_INFO_2};
        requirementsInfo.image = surface->image_;
        requirementsInfo.pNext = disjoint ? &planeRequirements : nullptr;
        VkMemoryDedicatedRequirements dedicated{VK_STRUCTURE_TYPE_MEMORY_DEDICATED_REQUIREMENTS};
        VkMemoryRequirements2 requirements{VK_STRUCTURE_TYPE_MEMORY_REQUIREMENTS_2};
        requirements.pNext = &dedicated;
        getRequirements(context.device, &requirementsInfo, &requirements);
        if (disjoint && dedicated.requiresDedicatedAllocation) return nullptr;
        VkMemoryFdPropertiesKHR fdProperties{VK_STRUCTURE_TYPE_MEMORY_FD_PROPERTIES_KHR};
        const auto& plane = descriptor.planes[i];
        if (!VkSuccess(getFdProperties(context.device, VK_EXTERNAL_MEMORY_HANDLE_TYPE_DMA_BUF_BIT_EXT,
                            plane.fd, &fdProperties), *generation) ||
            (plane.size_bytes && plane.size_bytes < requirements.memoryRequirements.size)) return nullptr;
        const uint32_t compatible = requirements.memoryRequirements.memoryTypeBits & fdProperties.memoryTypeBits;
        uint32_t memoryType = UINT32_MAX;
        for (uint32_t index = 0; index < memoryProperties.memoryTypeCount; ++index) {
            if (!(compatible & (1u << index))) continue;
            if (memoryType == UINT32_MAX) memoryType = index;
            if (memoryProperties.memoryTypes[index].propertyFlags & VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT) {
                memoryType = index; break;
            }
        }
        if (memoryType == UINT32_MAX) return nullptr;
        OwnedFd fd;
        fd.value = fcntl(plane.fd, F_DUPFD_CLOEXEC, 0);
        if (fd.value < 0) return nullptr;
        VkMemoryDedicatedAllocateInfo dedicatedInfo{VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO};
        dedicatedInfo.image = surface->image_;
        VkImportMemoryFdInfoKHR import{VK_STRUCTURE_TYPE_IMPORT_MEMORY_FD_INFO_KHR};
        import.handleType = VK_EXTERNAL_MEMORY_HANDLE_TYPE_DMA_BUF_BIT_EXT;
        import.fd = fd.value;
        import.pNext = dedicatedOnly || dedicated.requiresDedicatedAllocation ? &dedicatedInfo : nullptr;
        VkMemoryAllocateInfo allocation{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};
        allocation.pNext = &import;
        allocation.allocationSize = requirements.memoryRequirements.size;
        allocation.memoryTypeIndex = memoryType;
        if (!VkSuccess(allocate(context.device, &allocation, nullptr, &surface->memories_[i]), *generation)) return nullptr;
        fd.value = -1;
        planeBindings[i].sType = VK_STRUCTURE_TYPE_BIND_IMAGE_PLANE_MEMORY_INFO;
        planeBindings[i].planeAspect = planeAspect;
        bindings[i].sType = VK_STRUCTURE_TYPE_BIND_IMAGE_MEMORY_INFO;
        bindings[i].pNext = disjoint ? &planeBindings[i] : nullptr;
        bindings[i].image = surface->image_;
        bindings[i].memory = surface->memories_[i];
    }
    if (!VkSuccess(bind(context.device, allocationCount, bindings), *generation)) return nullptr;

    if (!surface->CreateSamplingView(format.vk, format.opaque)) return nullptr;
    lock.unlock();
    surface->RetainProducer(descriptor);
    return surface.release();
#else
    (void)generation; (void)descriptor;
    return nullptr;
#endif
}

bool VulkanImportedVideoSurface::CreateSamplingView(VkFormat format, bool opaque)
{
    const auto& context = generation_->ctx;
    auto& generation = generation_;
    auto createView = DeviceProc<PFN_vkCreateImageView>(context, "vkCreateImageView");
    auto createPool = DeviceProc<PFN_vkCreateDescriptorPool>(context, "vkCreateDescriptorPool");
    auto allocateSets = DeviceProc<PFN_vkAllocateDescriptorSets>(context, "vkAllocateDescriptorSets");
    auto writeSets = DeviceProc<PFN_vkUpdateDescriptorSets>(context, "vkUpdateDescriptorSets");
    if (!sampling_ || !createView || !createPool || !allocateSets || !writeSets) return false;
    VkSamplerYcbcrConversionInfo conversion{VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_INFO};
    if (sampling_) conversion.conversion = sampling_->conversion;
    VkImageViewCreateInfo viewInfo{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO};
    viewInfo.pNext = sampling_ && sampling_->conversion ? &conversion : nullptr;
    viewInfo.image = image_;
    viewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
    viewInfo.format = format;
    viewInfo.components = {VK_COMPONENT_SWIZZLE_IDENTITY, VK_COMPONENT_SWIZZLE_IDENTITY,
        VK_COMPONENT_SWIZZLE_IDENTITY, opaque ? VK_COMPONENT_SWIZZLE_ONE : VK_COMPONENT_SWIZZLE_IDENTITY};
    viewInfo.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    if (!VkSuccess(createView(context.device, &viewInfo, nullptr, &view_), *generation)) return false;
    if (sampling_) {
        VkDescriptorPoolSize size{VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER, sampling_->descriptorCount};
        VkDescriptorPoolCreateInfo poolInfo{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};
        poolInfo.maxSets = poolInfo.poolSizeCount = 1;
        poolInfo.pPoolSizes = &size;
        if (!VkSuccess(createPool(context.device, &poolInfo, nullptr, &descriptorPool_), *generation)) return false;
        VkDescriptorSetAllocateInfo allocateInfo{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};
        allocateInfo.descriptorPool = descriptorPool_;
        allocateInfo.descriptorSetCount = 1;
        allocateInfo.pSetLayouts = &sampling_->layout;
        if (!VkSuccess(allocateSets(context.device, &allocateInfo, &descriptorSet_), *generation)) return false;
        VkDescriptorImageInfo image{sampling_->sampler, view_, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL};
        VkWriteDescriptorSet write{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};
        write.dstSet = descriptorSet_;
        write.descriptorCount = 1;
        write.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
        write.pImageInfo = &image;
        writeSets(context.device, 1, &write, 0, nullptr);
    }
    return true;
}

void VulkanImportedVideoSurface::RetainProducer(const JaliumVideoSurfaceDescriptor& descriptor)
{
    if (descriptor.lifetime_context && descriptor.lifetime_retain_callback && descriptor.lifetime_release_callback) {
        Callback(descriptor.lifetime_retain_callback)(descriptor.lifetime_context);
        lifetimeContext_ = descriptor.lifetime_context;
        lifetimeReleaseCallback_ = descriptor.lifetime_release_callback;
    }
}

VulkanImportedVideoSurface::~VulkanImportedVideoSurface()
{
    if (generation_) {
        std::lock_guard<std::recursive_mutex> lock(generation_->driverCallMutex);
        if (!generation_->IsAbandoned()) {
            const auto& context = generation_->ctx;
            auto destroyPool = DeviceProc<PFN_vkDestroyDescriptorPool>(context, "vkDestroyDescriptorPool");
            auto destroyView = DeviceProc<PFN_vkDestroyImageView>(context, "vkDestroyImageView");
            auto destroyImage = DeviceProc<PFN_vkDestroyImage>(context, "vkDestroyImage");
            auto freeMemory = DeviceProc<PFN_vkFreeMemory>(context, "vkFreeMemory");
            auto destroySemaphore = DeviceProc<PFN_vkDestroySemaphore>(context, "vkDestroySemaphore");
            if (descriptorPool_ && destroyPool) destroyPool(context.device, descriptorPool_, nullptr);
            if (view_ && destroyView) destroyView(context.device, view_, nullptr);
            if (image_ && destroyImage) destroyImage(context.device, image_, nullptr);
            for (auto memory : memories_) if (memory && freeMemory) freeMemory(context.device, memory, nullptr);
            if (acquireSemaphore_ && destroySemaphore) destroySemaphore(context.device, acquireSemaphore_, nullptr);
        }
    }
    sampling_.reset();
    // DEVICE_LOST permits recycling. An indeterminate live GPU does not:
    // quarantine the submitted producer reference alongside the leaked Vulkan
    // allocation so the decoder cannot overwrite memory still being sampled.
    // Unsubmitted imports never expose the producer allocation to GPU work.
    if ((!generation_ || !generation_->IsAbandoned() || !hasSubmitted_) &&
        lifetimeContext_ && lifetimeReleaseCallback_) Callback(lifetimeReleaseCallback_)(lifetimeContext_);
}

void VulkanImportedVideoSurface::Release()
{
    if (referenceCount_.fetch_sub(1, std::memory_order_acq_rel) == 1) delete this;
}
VkDevice VulkanImportedVideoSurface::DeviceHandle() const
{
    return generation_ ? generation_->ctx.device : VK_NULL_HANDLE;
}
bool VulkanImportedVideoSurface::DeviceLost() const
{
    return !generation_ || !generation_->IsUsable();
}

void VulkanImportedVideoSurface::RecordAcquire(VkCommandBuffer commandBuffer) const
{
    const auto& context = generation_->ctx;
    auto barrier = DeviceProc<PFN_vkCmdPipelineBarrier>(context, "vkCmdPipelineBarrier");
    VkImageMemoryBarrier info{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};
    // GENERAL preserves producer pixels. UNDEFINED would discard them.
    info.oldLayout = VK_IMAGE_LAYOUT_GENERAL;
    info.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    info.srcQueueFamilyIndex = externalQueueFamily_;
    info.dstQueueFamilyIndex = context.graphicsQueueFamily;
    info.image = image_;
    info.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    info.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
    barrier(commandBuffer, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,
        0, 0, nullptr, 0, nullptr, 1, &info);
}
void VulkanImportedVideoSurface::RecordRelease(VkCommandBuffer commandBuffer) const
{
    const auto& context = generation_->ctx;
    auto barrier = DeviceProc<PFN_vkCmdPipelineBarrier>(context, "vkCmdPipelineBarrier");
    VkImageMemoryBarrier info{VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};
    info.oldLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    info.newLayout = VK_IMAGE_LAYOUT_GENERAL;
    info.srcQueueFamilyIndex = context.graphicsQueueFamily;
    info.dstQueueFamilyIndex = externalQueueFamily_;
    info.image = image_;
    info.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    info.srcAccessMask = VK_ACCESS_SHADER_READ_BIT;
    barrier(commandBuffer, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
        0, 0, nullptr, 0, nullptr, 1, &info);
}

std::shared_ptr<std::vector<uint8_t>> VulkanImportedVideoSurface::ReadbackPixels()
{
    auto generation = generation_;
    if (!generation) return {};
    std::lock_guard<std::recursive_mutex> lock(generation->driverCallMutex);
    if (readbackPixels_) return readbackPixels_;
    if (!generation->IsUsable() || !sampling_ || !descriptorSet_) return {};
    PackedBgraLayout packed{};
    if (!TryComputeTightlyPackedBgraLayout(width_, height_, packed)) return {};
    const auto& context = generation->ctx;
#define LOAD(name) auto name = DeviceProc<PFN_vk##name>(context, "vk" #name); if (!name) return {}
    LOAD(CreateImage); LOAD(DestroyImage); LOAD(GetImageMemoryRequirements);
    LOAD(AllocateMemory); LOAD(FreeMemory); LOAD(BindImageMemory);
    LOAD(CreateImageView); LOAD(DestroyImageView); LOAD(CreateBuffer); LOAD(DestroyBuffer);
    LOAD(GetBufferMemoryRequirements); LOAD(BindBufferMemory); LOAD(MapMemory); LOAD(UnmapMemory);
    LOAD(CreateRenderPass); LOAD(DestroyRenderPass); LOAD(CreateFramebuffer); LOAD(DestroyFramebuffer);
    LOAD(CreatePipelineLayout); LOAD(DestroyPipelineLayout); LOAD(CreateShaderModule); LOAD(DestroyShaderModule);
    LOAD(CreateGraphicsPipelines); LOAD(DestroyPipeline); LOAD(CreateCommandPool); LOAD(DestroyCommandPool);
    LOAD(AllocateCommandBuffers); LOAD(BeginCommandBuffer); LOAD(EndCommandBuffer);
    LOAD(CmdBeginRenderPass); LOAD(CmdEndRenderPass); LOAD(CmdBindPipeline); LOAD(CmdBindDescriptorSets);
    LOAD(CmdDraw); LOAD(CmdCopyImageToBuffer); LOAD(CmdPipelineBarrier);
    LOAD(CreateFence); LOAD(DestroyFence); LOAD(WaitForFences); LOAD(QueueSubmit);
#undef LOAD
    auto memoryProperties = InstanceProc<PFN_vkGetPhysicalDeviceMemoryProperties>(context, "vkGetPhysicalDeviceMemoryProperties");
    if (!memoryProperties) return {};
    VkPhysicalDeviceMemoryProperties properties{};
    memoryProperties(context.physicalDevice, &properties);
    const auto memoryType = [&](uint32_t bits, VkMemoryPropertyFlags required) {
        for (uint32_t i = 0; i < properties.memoryTypeCount; ++i)
            if ((bits & (1u << i)) && (properties.memoryTypes[i].propertyFlags & required) == required) return i;
        return UINT32_MAX;
    };
    VkImage image = VK_NULL_HANDLE;
    VkDeviceMemory imageMemory = VK_NULL_HANDLE, bufferMemory = VK_NULL_HANDLE;
    VkImageView view = VK_NULL_HANDLE;
    VkBuffer buffer = VK_NULL_HANDLE;
    VkRenderPass pass = VK_NULL_HANDLE;
    VkFramebuffer framebuffer = VK_NULL_HANDLE;
    VkPipelineLayout layout = VK_NULL_HANDLE;
    VkPipeline pipeline = VK_NULL_HANDLE;
    VkShaderModule vertex = VK_NULL_HANDLE, fragment = VK_NULL_HANDLE;
    VkCommandPool pool = VK_NULL_HANDLE;
    VkFence fence = VK_NULL_HANDLE;
    void* mapped = nullptr;
    auto cleanup = Finally([&] {
        if (generation->IsAbandoned()) return;
        if (mapped) UnmapMemory(context.device, bufferMemory);
        if (fence) DestroyFence(context.device, fence, nullptr);
        if (pool) DestroyCommandPool(context.device, pool, nullptr);
        if (pipeline) DestroyPipeline(context.device, pipeline, nullptr);
        if (vertex) DestroyShaderModule(context.device, vertex, nullptr);
        if (fragment) DestroyShaderModule(context.device, fragment, nullptr);
        if (layout) DestroyPipelineLayout(context.device, layout, nullptr);
        if (framebuffer) DestroyFramebuffer(context.device, framebuffer, nullptr);
        if (pass) DestroyRenderPass(context.device, pass, nullptr);
        if (view) DestroyImageView(context.device, view, nullptr);
        if (image) DestroyImage(context.device, image, nullptr);
        if (imageMemory) FreeMemory(context.device, imageMemory, nullptr);
        if (buffer) DestroyBuffer(context.device, buffer, nullptr);
        if (bufferMemory) FreeMemory(context.device, bufferMemory, nullptr);
    });
    const auto ok = [&](VkResult result) { return VkSuccess(result, *generation); };
    VkImageCreateInfo imageInfo{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO};
    imageInfo.imageType = VK_IMAGE_TYPE_2D; imageInfo.format = VK_FORMAT_R8G8B8A8_UNORM;
    imageInfo.extent = {width_, height_, 1}; imageInfo.mipLevels = imageInfo.arrayLayers = 1;
    imageInfo.samples = VK_SAMPLE_COUNT_1_BIT; imageInfo.tiling = VK_IMAGE_TILING_OPTIMAL;
    imageInfo.usage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
    if (!ok(CreateImage(context.device, &imageInfo, nullptr, &image))) return {};
    VkMemoryRequirements requirements{};
    GetImageMemoryRequirements(context.device, image, &requirements);
    VkMemoryAllocateInfo allocation{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};
    allocation.allocationSize = requirements.size;
    allocation.memoryTypeIndex = memoryType(requirements.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    if (allocation.memoryTypeIndex == UINT32_MAX) allocation.memoryTypeIndex = memoryType(requirements.memoryTypeBits, 0);
    if (allocation.memoryTypeIndex == UINT32_MAX || !ok(AllocateMemory(context.device, &allocation, nullptr, &imageMemory)) ||
        !ok(BindImageMemory(context.device, image, imageMemory, 0))) return {};
    VkImageViewCreateInfo viewInfo{VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO};
    viewInfo.image = image; viewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D; viewInfo.format = imageInfo.format;
    viewInfo.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    if (!ok(CreateImageView(context.device, &viewInfo, nullptr, &view))) return {};
    VkBufferCreateInfo bufferInfo{VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO};
    bufferInfo.size = packed.packedBytes; bufferInfo.usage = VK_BUFFER_USAGE_TRANSFER_DST_BIT;
    if (!ok(CreateBuffer(context.device, &bufferInfo, nullptr, &buffer))) return {};
    GetBufferMemoryRequirements(context.device, buffer, &requirements);
    allocation.allocationSize = requirements.size;
    allocation.memoryTypeIndex = memoryType(requirements.memoryTypeBits, VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
    if (allocation.memoryTypeIndex == UINT32_MAX || !ok(AllocateMemory(context.device, &allocation, nullptr, &bufferMemory)) ||
        !ok(BindBufferMemory(context.device, buffer, bufferMemory, 0))) return {};
    VkAttachmentDescription attachment{};
    attachment.format = imageInfo.format; attachment.samples = VK_SAMPLE_COUNT_1_BIT;
    attachment.loadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE; attachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
    attachment.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE; attachment.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
    attachment.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED; attachment.finalLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
    VkAttachmentReference reference{0, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL};
    VkSubpassDescription subpass{};
    subpass.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
    subpass.colorAttachmentCount = 1; subpass.pColorAttachments = &reference;
    VkSubpassDependency dependency{};
    dependency.srcSubpass = 0; dependency.dstSubpass = VK_SUBPASS_EXTERNAL;
    dependency.srcStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
    dependency.dstStageMask = VK_PIPELINE_STAGE_TRANSFER_BIT;
    dependency.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
    dependency.dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
    VkRenderPassCreateInfo passInfo{VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO};
    passInfo.attachmentCount = 1; passInfo.pAttachments = &attachment;
    passInfo.subpassCount = 1; passInfo.pSubpasses = &subpass;
    passInfo.dependencyCount = 1; passInfo.pDependencies = &dependency;
    if (!ok(CreateRenderPass(context.device, &passInfo, nullptr, &pass))) return {};
    VkFramebufferCreateInfo framebufferInfo{VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO};
    framebufferInfo.renderPass = pass; framebufferInfo.attachmentCount = 1; framebufferInfo.pAttachments = &view;
    framebufferInfo.width = width_; framebufferInfo.height = height_; framebufferInfo.layers = 1;
    if (!ok(CreateFramebuffer(context.device, &framebufferInfo, nullptr, &framebuffer))) return {};
    VkPipelineLayoutCreateInfo layoutInfo{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};
    layoutInfo.setLayoutCount = 1; layoutInfo.pSetLayouts = &sampling_->layout;
    if (!ok(CreatePipelineLayout(context.device, &layoutInfo, nullptr, &layout))) return {};
    VkShaderModuleCreateInfo shaderInfo{VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO};
    shaderInfo.codeSize = kVideoReadbackVertSpvSize; shaderInfo.pCode = kVideoReadbackVertSpv;
    if (!ok(CreateShaderModule(context.device, &shaderInfo, nullptr, &vertex))) return {};
    shaderInfo.codeSize = kVideoReadbackFragSpvSize; shaderInfo.pCode = kVideoReadbackFragSpv;
    if (!ok(CreateShaderModule(context.device, &shaderInfo, nullptr, &fragment))) return {};
    VkPipelineShaderStageCreateInfo stages[2]{};
    for (int i = 0; i < 2; ++i) {
        stages[i].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        stages[i].stage = i ? VK_SHADER_STAGE_FRAGMENT_BIT : VK_SHADER_STAGE_VERTEX_BIT;
        stages[i].module = i ? fragment : vertex; stages[i].pName = "main";
    }
    VkPipelineVertexInputStateCreateInfo vertexInput{VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO};
    VkPipelineInputAssemblyStateCreateInfo assembly{VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO};
    assembly.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
    VkViewport viewport{0, 0, static_cast<float>(width_), static_cast<float>(height_), 0, 1};
    VkRect2D scissor{{0, 0}, {width_, height_}};
    VkPipelineViewportStateCreateInfo viewportInfo{VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO};
    viewportInfo.viewportCount = viewportInfo.scissorCount = 1; viewportInfo.pViewports = &viewport; viewportInfo.pScissors = &scissor;
    VkPipelineRasterizationStateCreateInfo raster{VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO};
    raster.polygonMode = VK_POLYGON_MODE_FILL; raster.cullMode = VK_CULL_MODE_NONE; raster.lineWidth = 1;
    VkPipelineMultisampleStateCreateInfo multisample{VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO};
    multisample.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;
    VkPipelineColorBlendAttachmentState blendAttachment{};
    blendAttachment.colorWriteMask = VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT | VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;
    VkPipelineColorBlendStateCreateInfo blend{VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO};
    blend.attachmentCount = 1; blend.pAttachments = &blendAttachment;
    VkGraphicsPipelineCreateInfo pipelineInfo{VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO};
    pipelineInfo.stageCount = 2; pipelineInfo.pStages = stages;
    pipelineInfo.pVertexInputState = &vertexInput; pipelineInfo.pInputAssemblyState = &assembly;
    pipelineInfo.pViewportState = &viewportInfo; pipelineInfo.pRasterizationState = &raster;
    pipelineInfo.pMultisampleState = &multisample; pipelineInfo.pColorBlendState = &blend;
    pipelineInfo.layout = layout; pipelineInfo.renderPass = pass;
    if (!ok(CreateGraphicsPipelines(context.device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &pipeline))) return {};
    VkCommandPoolCreateInfo poolInfo{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};
    poolInfo.flags = VK_COMMAND_POOL_CREATE_TRANSIENT_BIT; poolInfo.queueFamilyIndex = context.graphicsQueueFamily;
    if (!ok(CreateCommandPool(context.device, &poolInfo, nullptr, &pool))) return {};
    VkCommandBufferAllocateInfo commandInfo{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};
    commandInfo.commandPool = pool; commandInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY; commandInfo.commandBufferCount = 1;
    VkCommandBuffer command = VK_NULL_HANDLE;
    if (!ok(AllocateCommandBuffers(context.device, &commandInfo, &command))) return {};
    VkCommandBufferBeginInfo begin{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};
    begin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    if (!ok(BeginCommandBuffer(command, &begin))) return {};
    RecordAcquire(command);
    VkRenderPassBeginInfo render{VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO};
    render.renderPass = pass; render.framebuffer = framebuffer; render.renderArea = scissor;
    CmdBeginRenderPass(command, &render, VK_SUBPASS_CONTENTS_INLINE);
    CmdBindPipeline(command, VK_PIPELINE_BIND_POINT_GRAPHICS, pipeline);
    CmdBindDescriptorSets(command, VK_PIPELINE_BIND_POINT_GRAPHICS, layout, 0, 1, &descriptorSet_, 0, nullptr);
    CmdDraw(command, 6, 1, 0, 0);
    CmdEndRenderPass(command);
    RecordRelease(command);
    VkBufferImageCopy copy{};
    copy.imageSubresource = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1}; copy.imageExtent = {width_, height_, 1};
    CmdCopyImageToBuffer(command, image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, buffer, 1, &copy);
    VkBufferMemoryBarrier host{VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER};
    host.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT; host.dstAccessMask = VK_ACCESS_HOST_READ_BIT;
    host.srcQueueFamilyIndex = host.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    host.buffer = buffer; host.size = VK_WHOLE_SIZE;
    CmdPipelineBarrier(command, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_HOST_BIT, 0,
        0, nullptr, 1, &host, 0, nullptr);
    if (!ok(EndCommandBuffer(command))) return {};
    VkFenceCreateInfo fenceInfo{VK_STRUCTURE_TYPE_FENCE_CREATE_INFO};
    if (!ok(CreateFence(context.device, &fenceInfo, nullptr, &fence))) return {};
    const VkSemaphore acquire = AcquireSemaphore();
    const VkPipelineStageFlags waitStage = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
    VkSubmitInfo submit{VK_STRUCTURE_TYPE_SUBMIT_INFO};
    submit.waitSemaphoreCount = acquire ? 1 : 0; submit.pWaitSemaphores = &acquire; submit.pWaitDstStageMask = &waitStage;
    submit.commandBufferCount = 1; submit.pCommandBuffers = &command;
    {
        std::lock_guard<std::mutex> queueLock(generation->queueMutex);
        if (!ok(QueueSubmit(context.graphicsQueue, 1, &submit, fence))) return {};
    }
    MarkAcquireSubmitted();
    RetainForFrame();
    const VkResult wait = WaitForFences(context.device, 1, &fence, VK_TRUE, 2'000'000'000ull);
    if (!ok(wait)) {
        if (!generation->IsLost()) generation->MarkAbandoned();
        // An indeterminate submission keeps its source + scratch resources
        // quarantined. Device loss is authoritative and allows destruction.
        if (generation->IsLost()) ReleaseForFrame();
        return {};
    }
    ReleaseForFrame();
    auto pixels = std::make_shared<std::vector<uint8_t>>(packed.packedBytes);
    if (!ok(MapMemory(context.device, bufferMemory, 0, packed.packedBytes, 0, &mapped))) return {};
    const auto* rgba = static_cast<const uint8_t*>(mapped);
    for (size_t i = 0; i < packed.packedBytes; i += 4) {
        (*pixels)[i] = rgba[i + 2]; (*pixels)[i + 1] = rgba[i + 1];
        (*pixels)[i + 2] = rgba[i]; (*pixels)[i + 3] = rgba[i + 3];
    }
    readbackPixels_ = std::move(pixels);
    return readbackPixels_;
}
} // namespace jalium

