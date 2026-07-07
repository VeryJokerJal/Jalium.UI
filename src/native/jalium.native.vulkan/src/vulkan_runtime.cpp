#include "vulkan_runtime.h"

namespace jalium {

bool IsVulkanRuntimeAvailable()
{
#ifdef __ANDROID__
    // On Android, vkEnumerateInstanceVersion may not be available (Vulkan 1.0 only).
    // Use vkGetInstanceProcAddr to dynamically query it.
    auto pfnEnumVersion = reinterpret_cast<PFN_vkEnumerateInstanceVersion>(
        vkGetInstanceProcAddr(VK_NULL_HANDLE, "vkEnumerateInstanceVersion"));
    if (pfnEnumVersion) {
        uint32_t version = 0;
        return pfnEnumVersion(&version) == VK_SUCCESS && version >= VK_API_VERSION_1_0;
    }
    // Vulkan 1.0 doesn't have vkEnumerateInstanceVersion — assume available if we got here
    return true;
#else
    uint32_t version = 0;
    return vkEnumerateInstanceVersion(&version) == VK_SUCCESS && version >= VK_API_VERSION_1_0;
#endif
}

PFN_vkGetInstanceProcAddr GetVulkanGetInstanceProcAddr()
{
    return &vkGetInstanceProcAddr;
}

PFN_vkGetDeviceProcAddr GetVulkanGetDeviceProcAddr()
{
    return &vkGetDeviceProcAddr;
}

} // namespace jalium
