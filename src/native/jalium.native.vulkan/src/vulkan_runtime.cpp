#include "vulkan_runtime.h"

#include <cstdlib>
#include <cstring>

namespace jalium {

namespace {

bool IsEnabledValue(const char* value)
{
    if (!value || *value == '\0') {
        return false;
    }

#ifdef _WIN32
    return _stricmp(value, "1") == 0
        || _stricmp(value, "true") == 0
        || _stricmp(value, "yes") == 0
        || _stricmp(value, "on") == 0;
#else
    return strcasecmp(value, "1") == 0
        || strcasecmp(value, "true") == 0
        || strcasecmp(value, "yes") == 0
        || strcasecmp(value, "on") == 0;
#endif
}

} // namespace

bool IsExperimentalVulkanEnabled()
{
#ifdef _WIN32
    char* value = nullptr;
    size_t valueLength = 0;
    if (_dupenv_s(&value, &valueLength, "JALIUM_EXPERIMENTAL_VULKAN") != 0) {
        return false;
    }

    const bool enabled = IsEnabledValue(value);
    if (value) {
        free(value);
    }

    return enabled;
#else
    return IsEnabledValue(std::getenv("JALIUM_EXPERIMENTAL_VULKAN"));
#endif
}

bool IsVulkanRuntimeAvailable()
{
    uint32_t version = 0;
    return vkEnumerateInstanceVersion(&version) == VK_SUCCESS && version >= VK_API_VERSION_1_0;
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
