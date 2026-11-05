#include "vulkan_runtime.h"
#include "vulkan_capabilities.h"

namespace jalium {

bool IsVulkanRuntimeAvailable()
{
    return vulkan_capabilities::RuntimeAvailable(&vkGetInstanceProcAddr);
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
