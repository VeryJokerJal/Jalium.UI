#include "vulkan_resources.h"
#include "vulkan_ink_layer.h"
#include <cstring>
#include <iostream>
#include <type_traits>
#if defined(__ANDROID__)
#include <android/hardware_buffer.h>
#include <fcntl.h>
#include <unistd.h>
#endif

using namespace jalium;
namespace {
int failures = 0, live = 0, retains = 0, releases = 0;
uintptr_t next = 100;
const char* failAt = "";
bool lost = false, noMemory = false, importable = true;
bool externalAndroid = true;
uint64_t externalIdentifier = 913;
uint32_t poolCount = 0, conversionCount = 0, semaphoreImports = 0;
VkSamplerYcbcrConversionCreateInfo conversion{};
VkImageMemoryBarrier barrier{};
void Check(bool condition, const char* message) {
    if (!condition) { ++failures; std::cerr << "FAILED: " << message << '\n'; }
}
template<class T> T Handle(uintptr_t n) {
    if constexpr (std::is_pointer_v<T>) return reinterpret_cast<T>(n);
    else return static_cast<T>(n);
}
template<class T> VkResult New(T* handle, const char* name) {
    if (!std::strcmp(failAt,name)) return lost ? VK_ERROR_DEVICE_LOST : VK_ERROR_OUT_OF_DEVICE_MEMORY;
    *handle=Handle<T>(++next); ++live; return VK_SUCCESS;
}
void Retain(uint64_t) { ++retains; }
void Release(uint64_t) { ++releases; }
constexpr VkFormatFeatureFlags features = VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT |
    VK_FORMAT_FEATURE_MIDPOINT_CHROMA_SAMPLES_BIT | VK_FORMAT_FEATURE_COSITED_CHROMA_SAMPLES_BIT |
    VK_FORMAT_FEATURE_SAMPLED_IMAGE_FILTER_LINEAR_BIT | VK_FORMAT_FEATURE_SAMPLED_IMAGE_YCBCR_CONVERSION_LINEAR_FILTER_BIT;
VKAPI_ATTR void VKAPI_CALL Format(VkPhysicalDevice, VkFormat, VkFormatProperties2* p) {
    p->formatProperties.optimalTilingFeatures = features;
}
VKAPI_ATTR VkResult VKAPI_CALL ImageFormat(VkPhysicalDevice, const VkPhysicalDeviceImageFormatInfo2* q, VkImageFormatProperties2* p) {
    Check(q->tiling == VK_IMAGE_TILING_OPTIMAL && q->usage == VK_IMAGE_USAGE_SAMPLED_BIT, "sample-only optimal external image query");
    p->imageFormatProperties.maxExtent={4096,4096,1};
    auto* e=static_cast<VkExternalImageFormatProperties*>(p->pNext);
    e->externalMemoryProperties.externalMemoryFeatures=importable ? VK_EXTERNAL_MEMORY_FEATURE_IMPORTABLE_BIT|VK_EXTERNAL_MEMORY_FEATURE_DEDICATED_ONLY_BIT : 0;
    if (e->pNext) static_cast<VkSamplerYcbcrConversionImageFormatProperties*>(e->pNext)->combinedImageSamplerDescriptorCount=2;
    return !std::strcmp(failAt,"format") ? VK_ERROR_FORMAT_NOT_SUPPORTED : VK_SUCCESS;
}
VKAPI_ATTR VkResult VKAPI_CALL Image(VkDevice, const VkImageCreateInfo* p, const VkAllocationCallbacks*, VkImage* image) {
    Check(p->initialLayout == VK_IMAGE_LAYOUT_UNDEFINED && p->extent.width == 64 && p->extent.height == 32, "external image extent/initial creation layout");
    Check(p->mipLevels==1 && p->arrayLayers==1 && p->samples==VK_SAMPLE_COUNT_1_BIT, "external image shape");
#if defined(__ANDROID__)
    auto* e=static_cast<const VkExternalMemoryImageCreateInfo*>(p->pNext);
    if (externalAndroid) {
        auto* a=static_cast<const VkExternalFormatANDROID*>(e->pNext);
        Check(p->format==VK_FORMAT_UNDEFINED && a && a->externalFormat==externalIdentifier, "Android opaque external format preserved");
    }
#endif
    return New(image,"image");
}
VKAPI_ATTR void VKAPI_CALL Requirements(VkDevice, const VkImageMemoryRequirementsInfo2*, VkMemoryRequirements2* p) {p->memoryRequirements={8192,256,1};}
VKAPI_ATTR VkResult VKAPI_CALL Allocate(VkDevice, const VkMemoryAllocateInfo* p, const VkAllocationCallbacks*, VkDeviceMemory* m) {
    const auto* import=static_cast<const VkBaseInStructure*>(p->pNext);
    const auto* dedicated=reinterpret_cast<const VkMemoryDedicatedAllocateInfo*>(import->pNext);
    Check(dedicated && dedicated->sType==VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO && dedicated->image, "dedicated import binds its image");
    Check(p->allocationSize==8192 && p->memoryTypeIndex==0, "external allocation size and compatible memory type");
    return New(m,"memory");
}
VKAPI_ATTR VkResult VKAPI_CALL Bind(VkDevice, VkImage, VkDeviceMemory, VkDeviceSize offset) {
    Check(offset==0,"dedicated import has no offset");
    return !std::strcmp(failAt,"bind") ? VK_ERROR_OUT_OF_DEVICE_MEMORY : VK_SUCCESS;
}
VKAPI_ATTR VkResult VKAPI_CALL View(VkDevice, const VkImageViewCreateInfo* p, const VkAllocationCallbacks*, VkImageView* v) {
    Check(p->subresourceRange.aspectMask==VK_IMAGE_ASPECT_COLOR_BIT,"external view uses COLOR aspect");
    return New(v,"view");
}
VKAPI_ATTR VkResult VKAPI_CALL Conversion(VkDevice, const VkSamplerYcbcrConversionCreateInfo* p, const VkAllocationCallbacks*, VkSamplerYcbcrConversion* c) {
    conversion=*p; ++conversionCount;
#if defined(__ANDROID__)
    if (externalAndroid) Check(p->pNext && static_cast<const VkExternalFormatANDROID*>(p->pNext)->externalFormat==externalIdentifier,"conversion carries Android external format");
#endif
    return New(c,"conversion");
}
VKAPI_ATTR VkResult VKAPI_CALL Sampler(VkDevice, const VkSamplerCreateInfo* p, const VkAllocationCallbacks*, VkSampler* s) {
    Check(p->addressModeU==VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE && !p->anisotropyEnable,"external sampler restrictions");
    return New(s,"sampler");
}
VKAPI_ATTR VkResult VKAPI_CALL Layout(VkDevice, const VkDescriptorSetLayoutCreateInfo* p, const VkAllocationCallbacks*, VkDescriptorSetLayout* l) {
    Check(p->pBindings[0].descriptorType==VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER && p->pBindings[0].pImmutableSamplers,"immutable combined external sampler");return New(l,"layout");
}
VKAPI_ATTR VkResult VKAPI_CALL Pool(VkDevice, const VkDescriptorPoolCreateInfo* p, const VkAllocationCallbacks*, VkDescriptorPool* d) {
    poolCount=p->pPoolSizes[0].descriptorCount;return New(d,"pool");
}
VKAPI_ATTR VkResult VKAPI_CALL Set(VkDevice, const VkDescriptorSetAllocateInfo*, VkDescriptorSet* s) {
    if (!std::strcmp(failAt,"set")) return VK_ERROR_OUT_OF_POOL_MEMORY;
    *s=Handle<VkDescriptorSet>(++next);return VK_SUCCESS;
}
VKAPI_ATTR void VKAPI_CALL Write(VkDevice, uint32_t count, const VkWriteDescriptorSet* p, uint32_t, const VkCopyDescriptorSet*) {
    Check(count==1 && p->descriptorCount==1 && p->pImageInfo->imageLayout==VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,"external descriptor image layout");
}
#define DESTROY(name,type) VKAPI_ATTR void VKAPI_CALL name(VkDevice,type,const VkAllocationCallbacks*) {--live;}
DESTROY(DestroyImage,VkImage) DESTROY(DestroyView,VkImageView) DESTROY(FreeMemory,VkDeviceMemory)
DESTROY(DestroyConversion,VkSamplerYcbcrConversion) DESTROY(DestroySampler,VkSampler)
DESTROY(DestroyLayout,VkDescriptorSetLayout) DESTROY(DestroyPool,VkDescriptorPool) DESTROY(DestroySemaphore,VkSemaphore)
#undef DESTROY
VKAPI_ATTR void VKAPI_CALL Barrier(VkCommandBuffer,VkPipelineStageFlags,VkPipelineStageFlags,VkDependencyFlags,uint32_t,const VkMemoryBarrier*,uint32_t,const VkBufferMemoryBarrier*,uint32_t,const VkImageMemoryBarrier* p) {barrier=*p;}
#if defined(_WIN32) || defined(JALIUM_VULKAN_TEST_WIN32_IMPORT)
VKAPI_ATTR VkResult VKAPI_CALL HandleProperties(VkDevice, VkExternalMemoryHandleTypeFlagBits type,HANDLE handle,VkMemoryWin32HandlePropertiesKHR* p) {
    Check(type==VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D11_TEXTURE_BIT && handle,"NT D3D11 texture handle type");p->memoryTypeBits=noMemory ? 0 : 1;
    return !std::strcmp(failAt,"properties") ? VK_ERROR_INVALID_EXTERNAL_HANDLE : VK_SUCCESS;
}
#else
AHardwareBuffer_Desc description{64,32,1,0x22,AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE,64,0,0};
VKAPI_ATTR VkResult VKAPI_CALL BufferProperties(VkDevice,const AHardwareBuffer*,VkAndroidHardwareBufferPropertiesANDROID* p) {
    p->allocationSize=8192;p->memoryTypeBits=noMemory ? 0 : 1;
    auto* f=static_cast<VkAndroidHardwareBufferFormatPropertiesANDROID*>(p->pNext);
    f->format=externalAndroid ? VK_FORMAT_UNDEFINED : VK_FORMAT_G8_B8R8_2PLANE_420_UNORM;
    f->externalFormat=externalIdentifier;f->formatFeatures=features;
    f->suggestedYcbcrModel=VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_601;
    f->suggestedYcbcrRange=VK_SAMPLER_YCBCR_RANGE_ITU_NARROW;
    f->suggestedXChromaOffset=VK_CHROMA_LOCATION_MIDPOINT;f->suggestedYChromaOffset=VK_CHROMA_LOCATION_COSITED_EVEN;
    return !std::strcmp(failAt,"properties") ? VK_ERROR_INVALID_EXTERNAL_HANDLE : VK_SUCCESS;
}
VKAPI_ATTR VkResult VKAPI_CALL Semaphore(VkDevice,const VkSemaphoreCreateInfo*,const VkAllocationCallbacks*,VkSemaphore* s) {return New(s,"semaphore");}
VKAPI_ATTR VkResult VKAPI_CALL ImportSemaphore(VkDevice,const VkImportSemaphoreFdInfoKHR* p) {
    ++semaphoreImports;
    Check(p->flags==VK_SEMAPHORE_IMPORT_TEMPORARY_BIT && p->handleType==VK_EXTERNAL_SEMAPHORE_HANDLE_TYPE_SYNC_FD_BIT,"temporary sync_file semaphore import");
    if (!std::strcmp(failAt,"fence")) return VK_ERROR_INVALID_EXTERNAL_HANDLE;
    close(p->fd);return VK_SUCCESS;
}
#endif
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL DeviceProc(VkDevice,const char* n) {
#define PROC(v,f) if (!std::strcmp(n,"vk" #v)) return reinterpret_cast<PFN_vkVoidFunction>(&f)
    PROC(CreateImage,Image);PROC(GetImageMemoryRequirements2,Requirements);PROC(AllocateMemory,Allocate);
    PROC(BindImageMemory,Bind);PROC(CreateImageView,View);PROC(CreateSamplerYcbcrConversion,Conversion);
    PROC(CreateSampler,Sampler);PROC(CreateDescriptorSetLayout,Layout);PROC(CreateDescriptorPool,Pool);
    PROC(AllocateDescriptorSets,Set);PROC(UpdateDescriptorSets,Write);PROC(CmdPipelineBarrier,Barrier);
    PROC(DestroyImage,DestroyImage);PROC(DestroyImageView,DestroyView);PROC(FreeMemory,FreeMemory);
    PROC(DestroySamplerYcbcrConversion,DestroyConversion);PROC(DestroySampler,DestroySampler);
    PROC(DestroyDescriptorSetLayout,DestroyLayout);PROC(DestroyDescriptorPool,DestroyPool);PROC(DestroySemaphore,DestroySemaphore);
#if defined(_WIN32) || defined(JALIUM_VULKAN_TEST_WIN32_IMPORT)
    PROC(GetMemoryWin32HandlePropertiesKHR,HandleProperties);
#else
    PROC(GetAndroidHardwareBufferPropertiesANDROID,BufferProperties);PROC(CreateSemaphore,Semaphore);PROC(ImportSemaphoreFdKHR,ImportSemaphore);
#endif
#undef PROC
    return nullptr;
}
VKAPI_ATTR void VKAPI_CALL PhysicalProperties(VkPhysicalDevice,VkPhysicalDeviceProperties* p) {p->limits.maxImageDimension2D=4096;}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL InstanceProc(VkInstance,const char* n) {
    if (!std::strcmp(n,"vkGetPhysicalDeviceProperties")) return reinterpret_cast<PFN_vkVoidFunction>(&PhysicalProperties);
    if (!std::strcmp(n,"vkGetPhysicalDeviceFormatProperties2")) return reinterpret_cast<PFN_vkVoidFunction>(&Format);
    if (!std::strcmp(n,"vkGetPhysicalDeviceImageFormatProperties2")) return reinterpret_cast<PFN_vkVoidFunction>(&ImageFormat);
    return nullptr;
}
std::shared_ptr<VulkanDeviceGeneration> Generation() {
    auto g=std::make_shared<VulkanDeviceGeneration>();g->ctx.valid=true;
    g->ctx.device=Handle<VkDevice>(1);g->ctx.physicalDevice=Handle<VkPhysicalDevice>(2);
    g->ctx.getDeviceProcAddr=&DeviceProc;g->ctx.getInstanceProcAddr=&InstanceProc;
    g->ctx.samplerYcbcrEnabled=true;g->ctx.win32ImportEnabled=true;g->ctx.androidHardwareBufferEnabled=true;
    g->ctx.foreignQueueEnabled=true;g->ctx.syncFdImportEnabled=true;g->ctx.graphicsQueueFamily=3;
    g->ctx.externalFormatDescriptorCount=3;return g;
}
JaliumVideoSurfaceDescriptor Descriptor() {
    JaliumVideoSurfaceDescriptor d{};d.width=64;d.height=32;d.acquire_fence_fd=-1;
    d.descriptor_flags=JALIUM_VS_FLAG_IMMUTABLE_READY;d.color_space=JALIUM_VS_COLOR_BT709;
    d.lifetime_context=42;d.lifetime_retain_callback=reinterpret_cast<uint64_t>(&Retain);d.lifetime_release_callback=reinterpret_cast<uint64_t>(&Release);
#if defined(_WIN32) || defined(JALIUM_VULKAN_TEST_WIN32_IMPORT)
    d.kind=JALIUM_VS_KIND_D3D11_SHARED;d.handle0=reinterpret_cast<uint64_t>(CreateEventW(nullptr,FALSE,FALSE,nullptr));
    d.format_hint=JALIUM_VS_FORMAT_NV12;
#else
    d.kind=JALIUM_VS_KIND_AHARDWAREBUFFER;d.handle0=123;d.format_hint=JALIUM_VS_FORMAT_NV12;
#endif
    return d;
}
}
#if defined(__ANDROID__)
extern "C" __attribute__((visibility("default"))) void AHardwareBuffer_describe(const AHardwareBuffer*, AHardwareBuffer_Desc* p) {*p=description;}
#endif
int main() {
    auto d=Descriptor();
    {
        auto g=Generation();auto* s=VulkanImportedVideoSurface::Create(g,d);
        Check(s && s->GetKind()==d.kind,"platform import succeeds with its actual kind");
        if (s) {
            s->RecordAcquire(Handle<VkCommandBuffer>(5));
#if defined(_WIN32) || defined(JALIUM_VULKAN_TEST_WIN32_IMPORT)
            Check(barrier.srcQueueFamilyIndex==VK_QUEUE_FAMILY_EXTERNAL,"D3D11 external queue family");
#else
            Check(barrier.srcQueueFamilyIndex==VK_QUEUE_FAMILY_FOREIGN_EXT,"Android foreign queue family");
            Check(poolCount==3,"opaque Android format reserves maximum descriptors");
#endif
            Check(barrier.oldLayout==VK_IMAGE_LAYOUT_GENERAL && barrier.newLayout==VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,"acquire preserves producer pixels");
            s->RecordRelease(Handle<VkCommandBuffer>(5));Check(barrier.newLayout==VK_IMAGE_LAYOUT_GENERAL,"release restores producer layout");
            s->RetainForFrame();s->Release();Check(releases==0,"queued frame retains producer");
            s->MarkAcquireSubmitted();s->ReleaseForFrame();Check(releases==1,"last GPU frame releases producer");
        }
    }
    Check(live==0,"success tears down every Vulkan handle");
    for (const char* stage : {"properties","image","memory","bind","conversion","sampler","layout","view","pool","set"}) {
        failAt=stage;auto g=Generation();int before=retains;
        auto* s=VulkanImportedVideoSurface::Create(g,d);Check(!s,"failed stage rejects import");if(s)s->Release();
        Check(live==0 && retains==before,"failed stage leaks no Vulkan or producer references");
    }
    failAt="";
#if defined(_WIN32) || defined(JALIUM_VULKAN_TEST_WIN32_IMPORT)
    {
        auto g=Generation();auto p010=d;p010.format_hint=JALIUM_VS_FORMAT_P010;
        p010.color_space=JALIUM_VS_COLOR_BT601;
        p010.descriptor_flags|=JALIUM_VS_FLAG_FULL_RANGE|JALIUM_VS_FLAG_CHROMA_COSITED_X;
        auto* s=VulkanImportedVideoSurface::Create(g,p010);
        Check(s && conversion.format==VK_FORMAT_G10X6_B10X6R10X6_2PLANE_420_UNORM_3PACK16 &&
            conversion.ycbcrModel==VK_SAMPLER_YCBCR_MODEL_CONVERSION_YCBCR_601 &&
            conversion.ycbcrRange==VK_SAMPLER_YCBCR_RANGE_ITU_FULL &&
            conversion.xChromaOffset==VK_CHROMA_LOCATION_COSITED_EVEN,"P010 keeps ten-bit format and exact color metadata");
        if(s)s->Release();
    }
#endif
    {
        auto g=Generation();noMemory=true;Check(!VulkanImportedVideoSurface::Create(g,d),"incompatible memory types reject import");noMemory=false;
        auto invalid=d;invalid.width=0;Check(!VulkanImportedVideoSurface::Create(g,invalid),"zero dimensions reject import");
        invalid=d;invalid.descriptor_flags|=JALIUM_VS_FLAG_HDR_TRANSFER;Check(!VulkanImportedVideoSurface::Create(g,invalid),"HDR without tone mapping rejects import");
        g->MarkLost();Check(!VulkanImportedVideoSurface::Create(g,d),"lost generation rejects new imports");
    }
#if defined(_WIN32) || defined(JALIUM_VULKAN_TEST_WIN32_IMPORT)
    {
        auto g=Generation();auto invalid=d;invalid.descriptor_flags=0;Check(!VulkanImportedVideoSurface::Create(g,invalid),"unfenced reusable D3D texture is rejected");
        invalid=d;invalid.handle1=GetCurrentProcessId()+1;Check(!VulkanImportedVideoSurface::Create(g,invalid),"foreign process NT handle is rejected");
        importable=false;Check(!VulkanImportedVideoSurface::Create(g,d),"unsupported external format rejects import");importable=true;
        invalid=d;invalid.format_hint=JALIUM_VS_FORMAT_BGRX8;auto* rgb=VulkanImportedVideoSurface::Create(g,invalid);Check(rgb && !rgb->Sampling()->conversion,"packed BGRX uses RGB sampler");if(rgb)rgb->Release();
    }
#else
    {
        auto g=Generation();auto invalid=d;invalid.lifetime_context=0;invalid.descriptor_flags=0;Check(!VulkanImportedVideoSurface::Create(g,invalid),"AHB allocation alone cannot prevent pool reuse");
        description.usage|=AHARDWAREBUFFER_USAGE_PROTECTED_CONTENT;Check(!VulkanImportedVideoSurface::Create(g,d),"protected buffers reject unprotected sampling");description.usage=AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE;
        externalAndroid=false;auto* s=VulkanImportedVideoSurface::Create(g,d);Check(s && poolCount==2,"known YUV format queries its descriptor count");if(s)s->Release();externalAndroid=true;
        auto* a=VulkanImportedVideoSurface::Create(g,d);int count=conversionCount;
        externalIdentifier++;auto* b=VulkanImportedVideoSurface::Create(g,d);
        Check(a && b && conversionCount==count+1 && a->Sampling()!=b->Sampling(),"different external formats never alias sampler cache");
        if(a)a->Release();if(b)b->Release();externalIdentifier--;
        int fds[2];Check(pipe(fds)==0,"acquire fence fixture");invalid=d;invalid.acquire_fence_fd=fds[0];invalid.descriptor_flags=0;
        g->ctx.syncFdImportEnabled=false;Check(!VulkanImportedVideoSurface::Create(g,invalid),"pending fence without import support rejects");
        g->ctx.syncFdImportEnabled=true;failAt="fence";Check(!VulkanImportedVideoSurface::Create(g,invalid),"failed semaphore import cleans up");failAt="";
        s=VulkanImportedVideoSurface::Create(g,invalid);Check(s && s->AcquireSemaphore(),"pending fence creates initial GPU wait");
        if(s){s->MarkAcquireSubmitted();Check(!s->AcquireSemaphore(),"temporary binary acquire wait consumed once");s->Release();}
        close(fds[0]);close(fds[1]);
    }
#endif
    {
        auto g=Generation();lost=true;failAt="memory";Check(!VulkanImportedVideoSurface::Create(g,d) && g->IsLost(),"allocation device loss latches generation");lost=false;failAt="";
    }
#if defined(_WIN32) || defined(JALIUM_VULKAN_TEST_WIN32_IMPORT)
    CloseHandle(reinterpret_cast<HANDLE>(static_cast<uintptr_t>(d.handle0)));
#endif
#if defined(JALIUM_VULKAN_TEST_WIN32_IMPORT)
    Check(win32TestHandles==0,"every duplicated and original NT handle is closed exactly once");
#endif
    Check(live==0,"all failure paths clean up Vulkan handles");
    if (!failures) std::cout << "External video platform import tests passed.\n";
    return failures ? 1 : 0;
}
