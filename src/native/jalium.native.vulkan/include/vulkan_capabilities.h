#pragma once

#include "vulkan_minimal.h"

#include <utility>
#include <vector>

namespace jalium::vulkan_capabilities {

constexpr uint32_t NegotiateInstanceApiVersion(uint32_t loaderApiVersion) noexcept
{
    if (loaderApiVersion >= VK_API_VERSION_1_2) return VK_API_VERSION_1_2;
    return loaderApiVersion >= VK_API_VERSION_1_1 ? VK_API_VERSION_1_1 : VK_API_VERSION_1_0;
}

constexpr uint32_t EffectiveDeviceApiVersion(uint32_t applicationVersion, uint32_t physicalDeviceVersion) noexcept
{
    return applicationVersion < physicalDeviceVersion ? applicationVersion : physicalDeviceVersion;
}

constexpr bool SupportsDrmFormatModifiers(uint32_t effectiveVersion, bool modifierExtension,
                                        bool imageFormatListExtension) noexcept
{
    return effectiveVersion >= VK_API_VERSION_1_1 && modifierExtension &&
        (effectiveVersion >= VK_API_VERSION_1_2 || imageFormatListExtension);
}

// Vulkan 1.0 loaders have no vkEnumerateInstanceVersion export. Query it via
// the 1.0 entry point so merely loading this backend does not require 1.1.
inline bool RuntimeAvailable(PFN_vkGetInstanceProcAddr getInstanceProcAddr) noexcept
{
    if (!getInstanceProcAddr ||
        !getInstanceProcAddr(VK_NULL_HANDLE, "vkCreateInstance")) {
        return false;
    }
    const auto enumerateVersion = reinterpret_cast<PFN_vkEnumerateInstanceVersion>(
        getInstanceProcAddr(VK_NULL_HANDLE, "vkEnumerateInstanceVersion"));
    if (!enumerateVersion) return true;
    uint32_t version = 0;
    return enumerateVersion(&version) == VK_SUCCESS && version >= VK_API_VERSION_1_0;
}

// The count can change between Vulkan's count and data calls. Do not publish
// a partial list on VK_INCOMPLETE, or retain zero-filled tail entries when the
// list shrinks. Bound retries so a changing driver cannot hang the UI thread.
inline constexpr uint32_t kEnumerationAttempts = 8;

template <typename T, typename Query>
VkResult Enumerate(std::vector<T>& values, Query&& query)
{
    values.clear();
    for (uint32_t attempt = 0; attempt < kEnumerationAttempts; ++attempt) {
        uint32_t count = 0;
        VkResult result = query(&count, nullptr);
        if (result != VK_SUCCESS && result != VK_INCOMPLETE) return result;
        if (count == 0) {
            if (result == VK_SUCCESS) return VK_SUCCESS;
            continue;
        }

        std::vector<T> candidate(count);
        result = query(&count, candidate.data());
        if (result == VK_INCOMPLETE) continue;
        if (result != VK_SUCCESS) return result;
        if (count > candidate.size()) return VK_ERROR_INITIALIZATION_FAILED;
        candidate.resize(count);
        values = std::move(candidate);
        return VK_SUCCESS;
    }
    return VK_INCOMPLETE;
}

constexpr bool SupportsVelloCompute(
    VkQueueFlags queueFlags, bool scalarBlockLayoutSupported) noexcept
{
    return scalarBlockLayoutSupported && (queueFlags & VK_QUEUE_COMPUTE_BIT) != 0;
}

struct QueueFamilies {
    uint32_t graphics = VK_QUEUE_FAMILY_IGNORED;
    uint32_t present = VK_QUEUE_FAMILY_IGNORED;
    VkQueueFlags graphicsFlags = 0;

    bool Complete() const noexcept
    {
        return graphics != VK_QUEUE_FAMILY_IGNORED && present != VK_QUEUE_FAMILY_IGNORED;
    }
};

template <typename SupportsPresent>
QueueFamilies SelectQueueFamilies(const std::vector<VkQueueFamilyProperties>& families,
                                  SupportsPresent&& supportsPresent)
{
    QueueFamilies split;
    QueueFamilies unified;
    for (uint32_t index = 0; index < families.size(); ++index) {
        const auto& family = families[index];
        if (family.queueCount == 0) continue;
        const bool present = supportsPresent(index);
        const bool graphics = (family.queueFlags & VK_QUEUE_GRAPHICS_BIT) != 0;
        const bool compute = (family.queueFlags & VK_QUEUE_COMPUTE_BIT) != 0;
        if (present && split.present == VK_QUEUE_FAMILY_IGNORED) split.present = index;
        if (graphics && (split.graphics == VK_QUEUE_FAMILY_IGNORED ||
                         (!(split.graphicsFlags & VK_QUEUE_COMPUTE_BIT) && compute))) {
            split.graphics = index;
            split.graphicsFlags = family.queueFlags;
        }
        if (graphics && present && (!unified.Complete() || compute)) {
            unified = {index, index, family.queueFlags};
            if (compute) return unified;
        }
    }
    // Preserve the single-family path when available. Otherwise presentation
    // and drawing can use different families with a concurrently shared WSI.
    return unified.Complete() ? unified : split;
}

} // namespace jalium::vulkan_capabilities
