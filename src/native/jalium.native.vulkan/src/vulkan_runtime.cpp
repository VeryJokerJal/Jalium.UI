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
#if defined(__ANDROID__)
    // On Android, Vulkan is the primary GPU backend — not "experimental".
    // Always enable unless explicitly overridden to 0.
    const char* value = std::getenv("JALIUM_EXPERIMENTAL_VULKAN");
    if (value && (value[0] == '0' || value[0] == 'f' || value[0] == 'F'))
        return false;
    return true;
#elif defined(_WIN32)
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
#elif defined(__linux__)
    // On Linux, Vulkan is the primary GPU backend — not "experimental".
    // Always enable unless explicitly overridden to 0.
    const char* linuxValue = std::getenv("JALIUM_EXPERIMENTAL_VULKAN");
    if (linuxValue && (linuxValue[0] == '0' || linuxValue[0] == 'f' || linuxValue[0] == 'F'))
        return false;
    return true;
#else
    return IsEnabledValue(std::getenv("JALIUM_EXPERIMENTAL_VULKAN"));
#endif
}

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
