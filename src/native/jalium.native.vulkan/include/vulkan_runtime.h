#pragma once

#include "vulkan_minimal.h"

namespace jalium {

bool IsExperimentalVulkanEnabled();
bool IsVulkanRuntimeAvailable();
PFN_vkGetInstanceProcAddr GetVulkanGetInstanceProcAddr();
PFN_vkGetDeviceProcAddr GetVulkanGetDeviceProcAddr();

} // namespace jalium
