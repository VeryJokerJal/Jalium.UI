// Linux-only fault injection. This library is preloaded by rendering tests;
// none of these hooks are compiled into the production Vulkan backend.
#include <vulkan/vulkan.h>

#include <atomic>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <dlfcn.h>
#include <algorithm>
#include <vector>

namespace {
template <typename T>
T Next(const char* name)
{
    return reinterpret_cast<T>(dlsym(RTLD_NEXT, name));
}

template <typename T>
VkResult IncompleteOnce(VkResult result, T* data,
                        std::atomic<bool>& injected, const char* name)
{
    if (data && result == VK_SUCCESS && !injected.exchange(true)) {
        std::fprintf(stderr, "[Vulkan test] injected VK_INCOMPLETE: %s\n", name);
        return VK_INCOMPLETE;
    }
    return result;
}

VKAPI_ATTR VkResult VKAPI_CALL InstanceExtensions(
    const char* layer, uint32_t* count, VkExtensionProperties* data)
{
    static std::atomic<bool> injected{false};
    return IncompleteOnce(Next<PFN_vkEnumerateInstanceExtensionProperties>(
        "vkEnumerateInstanceExtensionProperties")(layer, count, data),
        data, injected, "instance extensions");
}

VKAPI_ATTR VkResult VKAPI_CALL InstanceLayers(uint32_t* count, VkLayerProperties* data)
{
    static std::atomic<bool> injected{false};
    return IncompleteOnce(Next<PFN_vkEnumerateInstanceLayerProperties>(
        "vkEnumerateInstanceLayerProperties")(count, data), data, injected, "instance layers");
}

VKAPI_ATTR VkResult VKAPI_CALL PhysicalDevices(
    VkInstance instance, uint32_t* count, VkPhysicalDevice* data)
{
    static std::atomic<bool> injected{false};
    return IncompleteOnce(Next<PFN_vkEnumeratePhysicalDevices>(
        "vkEnumeratePhysicalDevices")(instance, count, data), data, injected, "physical devices");
}

VKAPI_ATTR VkResult VKAPI_CALL DeviceExtensions(
    VkPhysicalDevice device, const char* layer, uint32_t* count, VkExtensionProperties* data)
{
    static std::atomic<bool> injected{false};
    return IncompleteOnce(Next<PFN_vkEnumerateDeviceExtensionProperties>(
        "vkEnumerateDeviceExtensionProperties")(device, layer, count, data),
        data, injected, "device extensions");
}

VKAPI_ATTR VkResult VKAPI_CALL SurfaceFormats(
    VkPhysicalDevice device, VkSurfaceKHR surface, uint32_t* count, VkSurfaceFormatKHR* data)
{
    static std::atomic<bool> injected{false};
    return IncompleteOnce(Next<PFN_vkGetPhysicalDeviceSurfaceFormatsKHR>(
        "vkGetPhysicalDeviceSurfaceFormatsKHR")(device, surface, count, data),
        data, injected, "surface formats");
}

VKAPI_ATTR VkResult VKAPI_CALL PresentModes(
    VkPhysicalDevice device, VkSurfaceKHR surface, uint32_t* count, VkPresentModeKHR* data)
{
    static std::atomic<bool> injected{false};
    return IncompleteOnce(Next<PFN_vkGetPhysicalDeviceSurfacePresentModesKHR>(
        "vkGetPhysicalDeviceSurfacePresentModesKHR")(device, surface, count, data),
        data, injected, "present modes");
}

VKAPI_ATTR VkResult VKAPI_CALL SwapchainImages(
    VkDevice device, VkSwapchainKHR swapchain, uint32_t* count, VkImage* data)
{
    static std::atomic<bool> injected{false};
    return IncompleteOnce(Next<PFN_vkGetSwapchainImagesKHR>(
        "vkGetSwapchainImagesKHR")(device, swapchain, count, data),
        data, injected, "swapchain images");
}

bool GraphicsOnly()
{
    const char* value = std::getenv("JALIUM_VK_TEST_GRAPHICS_ONLY");
    return value && std::strcmp(value, "1") == 0;
}

VKAPI_ATTR void VKAPI_CALL LimitedConstants(VkPhysicalDevice device, VkPhysicalDeviceProperties* properties)
{
    Next<PFN_vkGetPhysicalDeviceProperties>("vkGetPhysicalDeviceProperties")(device, properties);
    properties->limits.maxPushConstantsSize = 128;
}

bool SplitQueues()
{
    const char* value = std::getenv("JALIUM_VK_TEST_SPLIT_QUEUES");
    return value && std::strcmp(value, "1") == 0;
}

// Expose two virtual families backed by one real graphics/present family.
// A distinct opaque presentation handle verifies the backend routes present
// correctly. The wrappers translate before entering the real validation layer.
uint32_t splitRealFamily = 0;
VkQueue splitRealPresentQueue = VK_NULL_HANDLE;
uint64_t splitPresentQueueMarker = 0;

VkQueue VirtualPresentQueue()
{
    return reinterpret_cast<VkQueue>(&splitPresentQueueMarker);
}

uint32_t RealGraphicsFamily(VkPhysicalDevice device)
{
    auto query = Next<PFN_vkGetPhysicalDeviceQueueFamilyProperties>(
        "vkGetPhysicalDeviceQueueFamilyProperties");
    uint32_t count = 0;
    query(device, &count, nullptr);
    std::vector<VkQueueFamilyProperties> families(count);
    query(device, &count, families.data());
    for (uint32_t index = 0; index < count; ++index)
        if (families[index].queueCount && (families[index].queueFlags & VK_QUEUE_GRAPHICS_BIT))
            return index;
    return VK_QUEUE_FAMILY_IGNORED;
}

VKAPI_ATTR void VKAPI_CALL QueueFamilies(
    VkPhysicalDevice device, uint32_t* count, VkQueueFamilyProperties* data)
{
    auto query = Next<PFN_vkGetPhysicalDeviceQueueFamilyProperties>(
        "vkGetPhysicalDeviceQueueFamilyProperties");
    if (SplitQueues()) {
        if (!data) { *count = 2; return; }
        uint32_t realCount = 0;
        query(device, &realCount, nullptr);
        std::vector<VkQueueFamilyProperties> real(realCount);
        query(device, &realCount, real.data());
        const auto graphics = RealGraphicsFamily(device);
        if (graphics == VK_QUEUE_FAMILY_IGNORED) { *count = 0; return; }
        *count = std::min(*count, 2u);
        if (*count > 0) data[0] = real[graphics];
        if (*count > 1) {
            data[1] = real[graphics];
            data[1].queueFlags = VK_QUEUE_TRANSFER_BIT;
        }
    } else query(device, count, data);
    if (data && GraphicsOnly()) {
        for (uint32_t i = 0; i < *count; ++i) data[i].queueFlags &= ~VK_QUEUE_COMPUTE_BIT;
        std::fprintf(stderr, "[Vulkan test] exposed graphics-only queue families\n");
    }
}

VKAPI_ATTR VkResult VKAPI_CALL SurfaceSupport(
    VkPhysicalDevice device, uint32_t family, VkSurfaceKHR surface, VkBool32* supported)
{
    if (family != 1) { *supported = VK_FALSE; return VK_SUCCESS; }
    return Next<PFN_vkGetPhysicalDeviceSurfaceSupportKHR>(
        "vkGetPhysicalDeviceSurfaceSupportKHR")(device, RealGraphicsFamily(device), surface, supported);
}

VKAPI_ATTR VkResult VKAPI_CALL CreateSplitDevice(VkPhysicalDevice physicalDevice,
    const VkDeviceCreateInfo* info, const VkAllocationCallbacks* allocator, VkDevice* device)
{
    if (info->queueCreateInfoCount != 2 ||
        info->pQueueCreateInfos[0].queueFamilyIndex != 0 ||
        info->pQueueCreateInfos[1].queueFamilyIndex != 1) {
        std::fputs("FAILED: split device must request graphics and presentation families\n", stderr);
        return VK_ERROR_INITIALIZATION_FAILED;
    }
    splitRealFamily = RealGraphicsFamily(physicalDevice);
    auto realInfo = *info;
    auto realQueueInfo = info->pQueueCreateInfos[0];
    realQueueInfo.queueFamilyIndex = splitRealFamily;
    realInfo.queueCreateInfoCount = 1;
    realInfo.pQueueCreateInfos = &realQueueInfo;
    std::fputs("[Vulkan test] validated creation of split queue families\n", stderr);
    return Next<PFN_vkCreateDevice>("vkCreateDevice")(physicalDevice, &realInfo, allocator, device);
}

VKAPI_ATTR void VKAPI_CALL GetSplitQueue(VkDevice device, uint32_t family, uint32_t index, VkQueue* queue)
{
    Next<PFN_vkGetDeviceQueue>("vkGetDeviceQueue")(device, splitRealFamily, index, queue);
    if (family == 1) {
        splitRealPresentQueue = *queue;
        *queue = VirtualPresentQueue();
    }
}

VKAPI_ATTR VkResult VKAPI_CALL CreateSplitCommandPool(VkDevice device,
    const VkCommandPoolCreateInfo* info, const VkAllocationCallbacks* allocator, VkCommandPool* pool)
{
    auto realInfo = *info;
    realInfo.queueFamilyIndex = splitRealFamily;
    return Next<PFN_vkCreateCommandPool>("vkCreateCommandPool")(device, &realInfo, allocator, pool);
}

VKAPI_ATTR VkResult VKAPI_CALL CreateSplitSwapchain(VkDevice device,
    const VkSwapchainCreateInfoKHR* info, const VkAllocationCallbacks* allocator, VkSwapchainKHR* swapchain)
{
    if (info->imageSharingMode != VK_SHARING_MODE_CONCURRENT ||
        info->queueFamilyIndexCount != 2 || !info->pQueueFamilyIndices ||
        info->pQueueFamilyIndices[0] != 0 || info->pQueueFamilyIndices[1] != 1) {
        std::fputs("FAILED: split swapchain images must be shared by both families\n", stderr);
        return VK_ERROR_INITIALIZATION_FAILED;
    }
    auto realInfo = *info;
    realInfo.imageSharingMode = VK_SHARING_MODE_EXCLUSIVE;
    realInfo.queueFamilyIndexCount = 0;
    realInfo.pQueueFamilyIndices = nullptr;
    return Next<PFN_vkCreateSwapchainKHR>("vkCreateSwapchainKHR")(device, &realInfo, allocator, swapchain);
}

VKAPI_ATTR VkResult VKAPI_CALL PresentSplitQueue(VkQueue queue, const VkPresentInfoKHR* info)
{
    if (queue != VirtualPresentQueue() || splitRealPresentQueue == VK_NULL_HANDLE) {
        std::fputs("FAILED: rendering must present through the separate presentation queue\n", stderr);
        return VK_ERROR_INITIALIZATION_FAILED;
    }
    return Next<PFN_vkQueuePresentKHR>("vkQueuePresentKHR")(splitRealPresentQueue, info);
}

VKAPI_ATTR void VKAPI_CALL ForbiddenDispatch(VkCommandBuffer, uint32_t, uint32_t, uint32_t)
{
    std::fputs("FAILED: compute dispatch on an injected graphics-only queue\n", stderr);
    std::abort();
}

VKAPI_ATTR void VKAPI_CALL ObserveDispatch(VkCommandBuffer command, uint32_t x, uint32_t y, uint32_t z)
{
    static std::atomic<bool> observed{false};
    if (!observed.exchange(true)) std::fputs("[Vulkan test] observed a real compute dispatch\n", stderr);
    Next<PFN_vkCmdDispatch>("vkCmdDispatch")(command, x, y, z);
}

PFN_vkVoidFunction Intercept(const char* name, PFN_vkVoidFunction real)
{
    if (!real) return nullptr;
#define INTERCEPT(api, function) \
    if (std::strcmp(name, #api) == 0) return reinterpret_cast<PFN_vkVoidFunction>(&function)
    INTERCEPT(vkEnumerateInstanceExtensionProperties, InstanceExtensions);
    INTERCEPT(vkEnumerateInstanceLayerProperties, InstanceLayers);
    INTERCEPT(vkEnumeratePhysicalDevices, PhysicalDevices);
    INTERCEPT(vkEnumerateDeviceExtensionProperties, DeviceExtensions);
    INTERCEPT(vkGetPhysicalDeviceSurfaceFormatsKHR, SurfaceFormats);
    INTERCEPT(vkGetPhysicalDeviceSurfacePresentModesKHR, PresentModes);
    INTERCEPT(vkGetSwapchainImagesKHR, SwapchainImages);
    INTERCEPT(vkGetPhysicalDeviceQueueFamilyProperties, QueueFamilies);
    if (std::getenv("JALIUM_VK_TEST_CONSTANTS_128")) {
        INTERCEPT(vkGetPhysicalDeviceProperties, LimitedConstants);
    }
    if (GraphicsOnly()) { INTERCEPT(vkCmdDispatch, ForbiddenDispatch); }
    INTERCEPT(vkCmdDispatch, ObserveDispatch);
    if (SplitQueues()) {
        INTERCEPT(vkGetPhysicalDeviceSurfaceSupportKHR, SurfaceSupport);
        INTERCEPT(vkCreateDevice, CreateSplitDevice);
        INTERCEPT(vkGetDeviceQueue, GetSplitQueue);
        INTERCEPT(vkCreateCommandPool, CreateSplitCommandPool);
        INTERCEPT(vkCreateSwapchainKHR, CreateSplitSwapchain);
        INTERCEPT(vkQueuePresentKHR, PresentSplitQueue);
    }
#undef INTERCEPT
    return real;
}
} // namespace

extern "C" VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL vkGetInstanceProcAddr(
    VkInstance instance, const char* name)
{
    const char* legacyLoader = std::getenv("JALIUM_VK_TEST_VULKAN10_LOADER");
    if (legacyLoader && std::strcmp(legacyLoader, "1") == 0 &&
        std::strcmp(name, "vkEnumerateInstanceVersion") == 0) {
        std::fputs("[Vulkan test] emulating a Vulkan 1.0 loader without version enumeration\n", stderr);
        return nullptr;
    }
    return Intercept(name, Next<PFN_vkGetInstanceProcAddr>("vkGetInstanceProcAddr")(instance, name));
}

extern "C" VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL vkGetDeviceProcAddr(
    VkDevice device, const char* name)
{
    return Intercept(name, Next<PFN_vkGetDeviceProcAddr>("vkGetDeviceProcAddr")(device, name));
}
