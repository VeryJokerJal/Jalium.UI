#include "vulkan_resources.h"
#include "vulkan_ink_layer.h"
#include "vulkan_video_format.h"

#include <cstring>
#include <dirent.h>
#include <fcntl.h>
#include <iostream>
#include <map>
#include <type_traits>
#include <unistd.h>

using namespace jalium;
namespace {
int failures = 0;
int liveHandles = 0;
uintptr_t nextHandle = 100;
const char* failAt = "";
uint32_t lastBindingCount = 0, lastPoolCount = 0, retains = 0, releases = 0;
bool forceDedicated = false;
uint32_t allocationCalls = 0, failAllocation = 0;
VkComponentSwizzle lastAlpha = VK_COMPONENT_SWIZZLE_IDENTITY;
VkSamplerYcbcrConversionCreateInfo lastConversion{};
VkImageMemoryBarrier lastBarrier{};
std::vector<VkImageAspectFlagBits> boundAspects;

void Check(bool condition, const char* message)
{
    if (!condition) { std::cerr << "FAILED: " << message << '\n'; ++failures; }
}
template <typename T> T Handle(uintptr_t value)
{
    if constexpr (std::is_pointer_v<T>) return reinterpret_cast<T>(value);
    else return static_cast<T>(value);
}
template <typename T> VkResult New(T* handle, const char* operation)
{
    if (std::strcmp(failAt, operation) == 0) return VK_ERROR_OUT_OF_DEVICE_MEMORY;
    *handle = Handle<T>(++nextHandle); ++liveHandles; return VK_SUCCESS;
}
int FdCount()
{
    int count = 0;
    if (auto* directory = opendir("/proc/self/fd")) {
        while (readdir(directory)) ++count;
        closedir(directory);
    }
    return count;
}
constexpr VkFormatFeatureFlags features = VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT |
    VK_FORMAT_FEATURE_DISJOINT_BIT | VK_FORMAT_FEATURE_MIDPOINT_CHROMA_SAMPLES_BIT |
    VK_FORMAT_FEATURE_COSITED_CHROMA_SAMPLES_BIT | VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT |
    VK_FORMAT_FEATURE_SAMPLED_IMAGE_YCBCR_CONVERSION_LINEAR_FILTER_BIT;

VKAPI_ATTR void VKAPI_CALL FormatProperties(VkPhysicalDevice, VkFormat, VkFormatProperties2* properties)
{
    properties->formatProperties.linearTilingFeatures = features;
    if (auto* list = static_cast<VkDrmFormatModifierPropertiesListEXT*>(properties->pNext)) {
        if (list->pDrmFormatModifierProperties && list->drmFormatModifierCount >= 1)
            list->pDrmFormatModifierProperties[0] = {0, 2, features};
        list->drmFormatModifierCount = 1;
    }
}
VKAPI_ATTR VkResult VKAPI_CALL ImageFormat(VkPhysicalDevice,
    const VkPhysicalDeviceImageFormatInfo2*, VkImageFormatProperties2* properties)
{
    if (!std::strcmp(failAt, "format")) return VK_ERROR_FORMAT_NOT_SUPPORTED;
    properties->imageFormatProperties.maxExtent = {4096, 4096, 1};
    auto* external = static_cast<VkExternalImageFormatProperties*>(properties->pNext);
    external->externalMemoryProperties.externalMemoryFeatures = VK_EXTERNAL_MEMORY_FEATURE_IMPORTABLE_BIT |
        (forceDedicated ? VK_EXTERNAL_MEMORY_FEATURE_DEDICATED_ONLY_BIT : 0);
    if (auto* conversion = static_cast<VkSamplerYcbcrConversionImageFormatProperties*>(external->pNext))
        conversion->combinedImageSamplerDescriptorCount = 3;
    return VK_SUCCESS;
}
VKAPI_ATTR VkResult VKAPI_CALL CreateImage(VkDevice, const VkImageCreateInfo* info,
    const VkAllocationCallbacks*, VkImage* image)
{
    Check(info->initialLayout == VK_IMAGE_LAYOUT_UNDEFINED, "external image creation has a legal initial layout");
    if (info->tiling == VK_IMAGE_TILING_DRM_FORMAT_MODIFIER_EXT) {
        const auto* external = static_cast<const VkExternalMemoryImageCreateInfo*>(info->pNext);
        const auto* modifier = static_cast<const VkImageDrmFormatModifierExplicitCreateInfoEXT*>(external->pNext);
        Check(modifier && modifier->drmFormatModifierPlaneCount == 2, "modifier plane count comes from format properties");
        Check(modifier && modifier->pPlaneLayouts[0].size == 0, "explicit modifier layout size is zero");
    }
    return New(image, "image");
}
VKAPI_ATTR void VKAPI_CALL Requirements(VkDevice, const VkImageMemoryRequirementsInfo2*, VkMemoryRequirements2* requirements)
{
    requirements->memoryRequirements = {4096, 256, 1};
}
VKAPI_ATTR void VKAPI_CALL Layout(VkDevice, VkImage, const VkImageSubresource* subresource, VkSubresourceLayout* layout)
{
    *layout = {};
    layout->rowPitch = !std::strcmp(failAt, "layout") ? 128 : 64;
    layout->offset = subresource->aspectMask == VK_IMAGE_ASPECT_PLANE_1_BIT ? 1024 : 0;
}
VKAPI_ATTR VkResult VKAPI_CALL FdProperties(VkDevice, VkExternalMemoryHandleTypeFlagBits,
    int fd, VkMemoryFdPropertiesKHR* properties)
{
    Check(fcntl(fd, F_GETFD) >= 0, "borrowed fd remains open");
    properties->memoryTypeBits = !std::strcmp(failAt, "memory-type") ? 0 : 1;
    return VK_SUCCESS;
}
VKAPI_ATTR void VKAPI_CALL MemoryProperties(VkPhysicalDevice, VkPhysicalDeviceMemoryProperties* properties)
{
    *properties = {}; properties->memoryTypeCount = 1;
    properties->memoryTypes[0].propertyFlags = VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT;
}
VKAPI_ATTR VkResult VKAPI_CALL Allocate(VkDevice, const VkMemoryAllocateInfo* info,
    const VkAllocationCallbacks*, VkDeviceMemory* memory)
{
    ++allocationCalls;
    if (failAllocation && allocationCalls == failAllocation) return VK_ERROR_OUT_OF_DEVICE_MEMORY;
    if (!std::strcmp(failAt, "device-lost")) return VK_ERROR_DEVICE_LOST;
    auto* import = static_cast<const VkImportMemoryFdInfoKHR*>(info->pNext);
    Check(import && import->fd >= 0 && fcntl(import->fd, F_GETFD) >= 0, "allocate receives an owned duplicate fd");
    Check((fcntl(import->fd, F_GETFD) & FD_CLOEXEC) != 0, "import fd cannot leak into spawned processes");
    auto result = New(memory, "memory");
    if (result == VK_SUCCESS) close(import->fd); // model Vulkan's ownership transfer
    return result;
}
VKAPI_ATTR VkResult VKAPI_CALL Bind(VkDevice, uint32_t count, const VkBindImageMemoryInfo* info)
{
    lastBindingCount = count; boundAspects.clear();
    for (uint32_t i = 0; i < count; ++i)
        if (info[i].pNext) boundAspects.push_back(static_cast<const VkBindImagePlaneMemoryInfo*>(info[i].pNext)->planeAspect);
    return !std::strcmp(failAt, "bind") ? VK_ERROR_OUT_OF_DEVICE_MEMORY : VK_SUCCESS;
}
VKAPI_ATTR VkResult VKAPI_CALL CreateView(VkDevice, const VkImageViewCreateInfo* info,
    const VkAllocationCallbacks*, VkImageView* view)
{
    lastAlpha = info->components.a;
    if (info->format == VK_FORMAT_G8_B8R8_2PLANE_420_UNORM ||
        info->format == VK_FORMAT_G10X6_B10X6R10X6_2PLANE_420_UNORM_3PACK16)
        Check(info->pNext != nullptr, "multiplanar view carries the sampler conversion");
    return New(view, "view");
}
VKAPI_ATTR VkResult VKAPI_CALL Conversion(VkDevice, const VkSamplerYcbcrConversionCreateInfo* info,
    const VkAllocationCallbacks*, VkSamplerYcbcrConversion* conversion)
{
    lastConversion = *info; return New(conversion, "conversion");
}
VKAPI_ATTR VkResult VKAPI_CALL Sampler(VkDevice, const VkSamplerCreateInfo* info,
    const VkAllocationCallbacks*, VkSampler* sampler)
{
    Check(!info->anisotropyEnable && !info->unnormalizedCoordinates,
          "conversion sampler uses legal normalized, non-anisotropic sampling");
    return New(sampler, "sampler");
}
VKAPI_ATTR VkResult VKAPI_CALL DescriptorLayout(VkDevice, const VkDescriptorSetLayoutCreateInfo* info,
    const VkAllocationCallbacks*, VkDescriptorSetLayout* layout)
{
    Check(info->bindingCount == 1 && info->pBindings[0].descriptorType == VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER &&
          info->pBindings[0].pImmutableSamplers, "conversion uses an immutable combined sampler");
    return New(layout, "descriptor-layout");
}
VKAPI_ATTR VkResult VKAPI_CALL Pool(VkDevice, const VkDescriptorPoolCreateInfo* info,
    const VkAllocationCallbacks*, VkDescriptorPool* pool)
{
    lastPoolCount = info->pPoolSizes[0].descriptorCount; return New(pool, "pool");
}
VKAPI_ATTR VkResult VKAPI_CALL Sets(VkDevice, const VkDescriptorSetAllocateInfo*, VkDescriptorSet* set)
{
    if (!std::strcmp(failAt, "set")) return VK_ERROR_OUT_OF_POOL_MEMORY;
    *set = Handle<VkDescriptorSet>(++nextHandle); return VK_SUCCESS;
}
VKAPI_ATTR void VKAPI_CALL Write(VkDevice, uint32_t count, const VkWriteDescriptorSet* writes, uint32_t, const VkCopyDescriptorSet*)
{
    Check(count == 1 && writes[0].pImageInfo->imageView &&
          writes[0].descriptorType == VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER, "descriptor samples the imported view");
}
VKAPI_ATTR VkResult VKAPI_CALL Semaphore(VkDevice, const VkSemaphoreCreateInfo*, const VkAllocationCallbacks*, VkSemaphore* semaphore)
{
    return New(semaphore, "semaphore");
}
VKAPI_ATTR VkResult VKAPI_CALL ImportSemaphore(VkDevice, const VkImportSemaphoreFdInfoKHR* info)
{
    Check(info->flags == VK_SEMAPHORE_IMPORT_TEMPORARY_BIT, "sync_file payload is imported temporarily");
    if (!std::strcmp(failAt, "semaphore-import")) return VK_ERROR_INVALID_EXTERNAL_HANDLE;
    close(info->fd); return VK_SUCCESS;
}
VKAPI_ATTR void VKAPI_CALL Barrier(VkCommandBuffer, VkPipelineStageFlags, VkPipelineStageFlags,
    VkDependencyFlags, uint32_t, const VkMemoryBarrier*, uint32_t, const VkBufferMemoryBarrier*,
    uint32_t count, const VkImageMemoryBarrier* barriers)
{
    Check(count == 1, "video ownership transfer names one image"); lastBarrier = barriers[0];
}
template <typename T> VKAPI_ATTR void VKAPI_CALL Destroy(VkDevice, T, const VkAllocationCallbacks*) { --liveHandles; }

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL DeviceProc(VkDevice, const char* name)
{
#define PROC(n, f) if (!std::strcmp(name, n)) return reinterpret_cast<PFN_vkVoidFunction>(f)
    PROC("vkCreateImage", CreateImage); PROC("vkGetImageMemoryRequirements2", Requirements);
    PROC("vkGetImageSubresourceLayout", Layout); PROC("vkGetMemoryFdPropertiesKHR", FdProperties);
    PROC("vkAllocateMemory", Allocate); PROC("vkBindImageMemory2", Bind); PROC("vkCreateImageView", CreateView);
    PROC("vkCreateSamplerYcbcrConversion", Conversion); PROC("vkCreateSampler", Sampler);
    PROC("vkCreateDescriptorSetLayout", DescriptorLayout); PROC("vkCreateDescriptorPool", Pool);
    PROC("vkAllocateDescriptorSets", Sets); PROC("vkUpdateDescriptorSets", Write);
    PROC("vkCreateSemaphore", Semaphore); PROC("vkImportSemaphoreFdKHR", ImportSemaphore);
    PROC("vkCmdPipelineBarrier", Barrier);
    PROC("vkDestroyImage", Destroy<VkImage>); PROC("vkDestroyImageView", Destroy<VkImageView>);
    PROC("vkFreeMemory", Destroy<VkDeviceMemory>); PROC("vkDestroySamplerYcbcrConversion", Destroy<VkSamplerYcbcrConversion>);
    PROC("vkDestroySampler", Destroy<VkSampler>); PROC("vkDestroyDescriptorSetLayout", Destroy<VkDescriptorSetLayout>);
    PROC("vkDestroyDescriptorPool", Destroy<VkDescriptorPool>); PROC("vkDestroySemaphore", Destroy<VkSemaphore>);
    return nullptr;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL InstanceProc(VkInstance, const char* name)
{
    PROC("vkGetPhysicalDeviceFormatProperties2", FormatProperties);
    PROC("vkGetPhysicalDeviceImageFormatProperties2", ImageFormat);
    PROC("vkGetPhysicalDeviceMemoryProperties", MemoryProperties);
    return nullptr;
#undef PROC
}
std::shared_ptr<VulkanDeviceGeneration> Generation()
{
    auto generation = std::make_shared<VulkanDeviceGeneration>();
    generation->ctx.device = Handle<VkDevice>(1);
    generation->ctx.getDeviceProcAddr = DeviceProc;
    generation->ctx.getInstanceProcAddr = InstanceProc;
    generation->ctx.valid = generation->ctx.dmaBufImportEnabled = generation->ctx.samplerYcbcrEnabled = true;
    generation->ctx.syncFdImportEnabled = true;
    return generation;
}
void Retain(uint64_t) { ++retains; }
void Release(uint64_t) { ++releases; }
JaliumVideoSurfaceDescriptor Descriptor(int fd, bool p010 = false)
{
    JaliumVideoSurfaceDescriptor descriptor{};
    descriptor.kind = JALIUM_VS_KIND_LINUX_DMABUF;
    descriptor.width = 16; descriptor.height = 16; descriptor.plane_count = 2;
    descriptor.format_hint = p010 ? JALIUM_VS_FORMAT_P010 : JALIUM_VS_FORMAT_NV12;
    descriptor.drm_fourcc = p010 ? vulkan_video::Fourcc('P','0','1','0') : vulkan_video::Fourcc('N','V','1','2');
    descriptor.color_space = JALIUM_VS_COLOR_BT709;
    descriptor.descriptor_flags = JALIUM_VS_FLAG_IMMUTABLE_READY;
    descriptor.acquire_fence_fd = -1;
    descriptor.planes[0] = {fd, 64, 0, 0, 0, 4096};
    descriptor.planes[1] = {fd, 64, 1024, 0, 0, 4096};
    descriptor.lifetime_context = 1;
    descriptor.lifetime_retain_callback = reinterpret_cast<uintptr_t>(&Retain);
    descriptor.lifetime_release_callback = reinterpret_cast<uintptr_t>(&Release);
    return descriptor;
}
} // namespace

int main()
{
    char name[] = "/tmp/jalium-vulkan-video-XXXXXX";
    const int fd = mkstemp(name); unlink(name);
    if (fd < 0) return 1;
    if (ftruncate(fd, 4096) != 0) { close(fd); return 1; }
    const int initialFds = FdCount();
    for (bool p010 : {false, true}) {
        auto generation = Generation();
        auto descriptor = Descriptor(fd, p010);
        auto* surface = VulkanImportedVideoSurface::Create(generation, descriptor);
        Check(surface != nullptr, "import NV12/P010 linear allocation");
        if (!surface) continue;
        Check(lastBindingCount == 1 && boundAspects.empty(), "shared allocation binds once without disjoint plane info");
        Check(lastPoolCount == 3, "descriptor pool counts the driver's expanded combined-sampler descriptors");
        surface->RecordAcquire(Handle<VkCommandBuffer>(1));
        Check(lastBarrier.oldLayout == VK_IMAGE_LAYOUT_GENERAL && lastBarrier.srcQueueFamilyIndex == VK_QUEUE_FAMILY_EXTERNAL,
              "external acquisition preserves producer pixels");
        surface->RecordRelease(Handle<VkCommandBuffer>(1));
        Check(lastBarrier.newLayout == VK_IMAGE_LAYOUT_GENERAL && lastBarrier.dstQueueFamilyIndex == VK_QUEUE_FAMILY_EXTERNAL,
              "ownership is returned for decoder recycling");
        const auto sampler = surface->Sampling();
        auto* second = VulkanImportedVideoSurface::Create(generation, descriptor);
        Check(second && second->Sampling() == sampler, "same conversion reuses its immutable sampler/layout");
        if (second) second->Release();
        const auto beforeRelease = releases;
        surface->RetainForFrame(); surface->Release();
        Check(releases == beforeRelease, "caller release cannot recycle an in-flight frame");
        surface->ReleaseForFrame();
        Check(releases == beforeRelease + 1, "final render fence reference returns producer ownership");
    }
    Check(liveHandles == 0, "successful imports release all handles");
    {
        auto generation = Generation();
        auto descriptor = Descriptor(fd);
        descriptor.plane_count = 1;
        descriptor.drm_fourcc = vulkan_video::Fourcc('X','R','2','4');
        descriptor.format_hint = JALIUM_VS_FORMAT_BGRX8;
        auto* surface = VulkanImportedVideoSurface::Create(generation, descriptor);
        Check(surface && surface->Sampling() && !surface->Sampling()->conversion && lastAlpha == VK_COMPONENT_SWIZZLE_ONE,
              "packed BGRX samples RGB without a YCbCr conversion and forces opaque alpha");
        if (surface) surface->Release();
        descriptor = Descriptor(fd); descriptor.descriptor_flags = 0;
        Check(!VulkanImportedVideoSurface::Create(generation, descriptor),
              "missing producer synchronization cannot silently sample an unready allocation");
        Check(!VulkanVideoSamplingResources::Create(generation, VK_FORMAT_G8_B8R8_2PLANE_420_UNORM,
            JALIUM_VS_COLOR_BT709, 0, features & ~VK_FORMAT_FEATURE_MIDPOINT_CHROMA_SAMPLES_BIT, 3),
            "unsupported chroma siting falls back instead of changing sample locations");
    }
    for (const char* operation : {"format", "conversion", "sampler", "descriptor-layout", "image", "layout",
                                  "memory-type", "memory", "bind", "view", "pool", "set"}) {
        failAt = operation;
        auto generation = Generation();
        auto* surface = VulkanImportedVideoSurface::Create(generation, Descriptor(fd));
        Check(!surface, operation);
        if (surface) surface->Release();
        Check(liveHandles == 0, "failed imports destroy partial Vulkan resources");
        Check(FdCount() == initialFds, "failed imports close duplicates and preserve borrowed fds");
    }
    failAt = "";
    {
        auto generation = Generation();
        failAt = "device-lost";
        Check(!VulkanImportedVideoSurface::Create(generation, Descriptor(fd)) && generation->IsLost(),
              "an import reporting device loss marks the shared device generation");
        failAt = "";
    }
    auto descriptor = Descriptor(fd);
    vulkan_video::Format format;
    descriptor.width = 17; Check(!vulkan_video::Describe(descriptor, format), "reject odd 4:2:0 dimensions");
    descriptor = Descriptor(fd); descriptor.color_space = 0;
    Check(!vulkan_video::Describe(descriptor, format), "reject unspecified YCbCr matrix");
    descriptor = Descriptor(fd); descriptor.descriptor_flags |= JALIUM_VS_FLAG_HDR_TRANSFER;
    Check(!vulkan_video::Describe(descriptor, format), "reject HDR requiring tone mapping");
    descriptor = Descriptor(fd); descriptor.planes[1].stride_bytes = 1;
    Check(!vulkan_video::Describe(descriptor, format), "reject undersized chroma stride");
    descriptor = Descriptor(fd); descriptor.planes[1].size_bytes = 1024;
    Check(!vulkan_video::Describe(descriptor, format), "reject truncated allocation");

    char otherName[] = "/tmp/jalium-vulkan-video-plane-XXXXXX";
    const int otherFd = mkstemp(otherName); unlink(otherName);
    if (otherFd < 0 || ftruncate(otherFd, 4096) != 0) { close(fd); return 1; }
    {
        auto generation = Generation(); generation->ctx.drmModifierEnabled = true;
        generation->ctx.foreignQueueEnabled = true;
        descriptor = Descriptor(fd); descriptor.planes[1].fd = otherFd;
        descriptor.color_space = JALIUM_VS_COLOR_BT601;
        descriptor.descriptor_flags |= JALIUM_VS_FLAG_FULL_RANGE | JALIUM_VS_FLAG_CHROMA_COSITED_X;
        auto* surface = VulkanImportedVideoSurface::Create(generation, descriptor);
        Check(surface && lastBindingCount == 2 && boundAspects.size() == 2 &&
              boundAspects[1] == VK_IMAGE_ASPECT_MEMORY_PLANE_1_BIT_EXT, "DRM disjoint import binds memory plane aspects");
        Check(lastConversion.ycbcrModel == VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_601 &&
              lastConversion.ycbcrRange == VK_SAMPLER_YCBCR_RANGE_ITU_FULL &&
              lastConversion.xChromaOffset == VK_CHROMA_LOCATION_COSITED_EVEN, "color metadata reaches sampler conversion");
        if (surface) {
            surface->RecordAcquire(Handle<VkCommandBuffer>(1));
            Check(lastBarrier.srcQueueFamilyIndex == VK_QUEUE_FAMILY_FOREIGN_EXT, "foreign queue is used only when enabled");
            surface->Release();
        }
        forceDedicated = true;
        Check(!VulkanImportedVideoSurface::Create(generation, descriptor), "dedicated-only disjoint import falls back cleanly");
        forceDedicated = false;
        allocationCalls = 0; failAllocation = 2;
        Check(!VulkanImportedVideoSurface::Create(generation, descriptor) && liveHandles == 0,
              "second-plane allocation failure cleans up the already imported first plane");
        failAllocation = 0;
    }
    close(otherFd);
    int pipeFds[2];
    if (pipe(pipeFds) == 0) {
        for (const char* operation : {"", "semaphore", "semaphore-import"}) {
            failAt = operation;
            auto generation = Generation();
            descriptor = Descriptor(fd); descriptor.acquire_fence_fd = pipeFds[0];
            auto* surface = VulkanImportedVideoSurface::Create(generation, descriptor);
            Check((surface != nullptr) == (*operation == 0), "pending fence imports or rejects atomically");
            if (surface) {
                Check(surface->AcquireSemaphore() != VK_NULL_HANDLE, "pending producer fence becomes a GPU wait");
                surface->MarkAcquireSubmitted();
                Check(surface->AcquireSemaphore() == VK_NULL_HANDLE, "binary acquire payload is waited exactly once");
                surface->Release();
            }
        }
        close(pipeFds[0]); close(pipeFds[1]);
    }
    failAt = "";
    Check(retains == releases && liveHandles == 0, "normal producer and driver lifetimes balance");
    {
        auto generation = Generation();
        auto* surface = VulkanImportedVideoSurface::Create(generation, Descriptor(fd));
        Check(surface != nullptr, "create device-lost lifetime fixture");
        if (surface) {
            surface->MarkAcquireSubmitted(); generation->MarkLost(); surface->Release();
            Check(retains == releases && liveHandles == 0, "authoritative device loss permits producer recycling and destruction");
        }
    }
    int quarantinedHandles = 0;
    {
        auto generation = Generation();
        auto* surface = VulkanImportedVideoSurface::Create(generation, Descriptor(fd));
        if (surface) {
            const auto beforeRelease = releases;
            quarantinedHandles = liveHandles;
            surface->MarkAcquireSubmitted(); generation->MarkAbandoned(); surface->Release();
            Check(releases == beforeRelease && liveHandles == quarantinedHandles,
                  "indeterminate live GPU quarantines producer ownership with its Vulkan handles");
        } else Check(false, "create quarantine lifetime fixture");
    }
    Check(retains == releases + 1 && liveHandles == quarantinedHandles,
          "only the deliberately quarantined allocation remains unrecycled");
    Check(FdCount() == initialFds, "all fence and memory duplicates close");
    close(fd);
    if (!failures) std::cout << "Vulkan video import contract tests passed.\n";
    return failures ? 1 : 0;
}
