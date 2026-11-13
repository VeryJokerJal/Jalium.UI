#include "vulkan_capabilities.h"
#include "vulkan_graphics_constants.h"

#include <cstring>
#include <iostream>
#include <vector>

namespace {
int failures = 0;
bool hasCreateInstance = true;
bool hasEnumerateVersion = true;
uint32_t loaderVersion = VK_API_VERSION_1_1;
VkResult versionResult = VK_SUCCESS;

void Check(bool condition, const char* message)
{
    if (!condition) {
        std::cerr << "FAILED: " << message << '\n';
        ++failures;
    }
}

VKAPI_ATTR void VKAPI_CALL UnusedCreateInstance() {}

VKAPI_ATTR VkResult VKAPI_CALL EnumerateVersion(uint32_t* version)
{
    *version = loaderVersion;
    return versionResult;
}

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL GetInstanceProcAddr(VkInstance, const char* name)
{
    if (hasCreateInstance && std::strcmp(name, "vkCreateInstance") == 0)
        return &UnusedCreateInstance;
    if (hasEnumerateVersion && std::strcmp(name, "vkEnumerateInstanceVersion") == 0)
        return reinterpret_cast<PFN_vkVoidFunction>(&EnumerateVersion);
    return nullptr;
}
} // namespace

int main()
{
    using namespace jalium::vulkan_capabilities;
    Check(NegotiateInstanceApiVersion(VK_API_VERSION_1_0) == VK_API_VERSION_1_0 &&
          NegotiateInstanceApiVersion(VK_API_VERSION_1_1) == VK_API_VERSION_1_1 &&
          NegotiateInstanceApiVersion(VK_MAKE_VERSION(1, 4, 0)) == VK_API_VERSION_1_2,
          "API negotiation uses core scalar block layout without abandoning old loaders");
    Check(EffectiveDeviceApiVersion(VK_API_VERSION_1_0, VK_API_VERSION_1_2) == VK_API_VERSION_1_0 &&
          EffectiveDeviceApiVersion(VK_API_VERSION_1_2, VK_API_VERSION_1_0) == VK_API_VERSION_1_0 &&
          EffectiveDeviceApiVersion(VK_API_VERSION_1_2, VK_MAKE_VERSION(1, 4, 0)) == VK_API_VERSION_1_2,
          "device feature promotions honor both the application and physical-device API versions");
    Check(!RuntimeAvailable(nullptr), "a missing loader is unavailable");
    Check(RuntimeAvailable(&GetInstanceProcAddr), "a Vulkan 1.1 loader is available");
    hasEnumerateVersion = false;
    Check(RuntimeAvailable(&GetInstanceProcAddr),
          "a Vulkan 1.0 loader needs no vkEnumerateInstanceVersion export");
    hasCreateInstance = false;
    Check(!RuntimeAvailable(&GetInstanceProcAddr), "a loader must expose vkCreateInstance");
    hasCreateInstance = true;
    hasEnumerateVersion = true;
    versionResult = VK_ERROR_INITIALIZATION_FAILED;
    Check(!RuntimeAvailable(&GetInstanceProcAddr), "version-query failure is not availability");
    versionResult = VK_SUCCESS;
    loaderVersion = VK_MAKE_VERSION(0, 9, 0);
    Check(!RuntimeAvailable(&GetInstanceProcAddr), "versions before Vulkan 1.0 are rejected");
    loaderVersion = VK_MAKE_VERSION(1, 3, 0);
    Check(RuntimeAvailable(&GetInstanceProcAddr), "newer loaders remain available");

    std::vector<uint32_t> values{99};
    unsigned dataCalls = 0;
    const auto grows = [&](uint32_t* count, uint32_t* data) {
        if (!data) { *count = dataCalls == 0 ? 1 : 3; return VK_SUCCESS; }
        ++dataCalls;
        if (dataCalls == 1) { data[0] = 11; *count = 1; return VK_INCOMPLETE; }
        data[0] = 11; data[1] = 22; data[2] = 33; *count = 3;
        return VK_SUCCESS;
    };
    Check(Enumerate(values, grows) == VK_SUCCESS && dataCalls == 2 &&
              values == std::vector<uint32_t>{11, 22, 33},
          "a growing list retries VK_INCOMPLETE and publishes every entry");

    Check(Enumerate(values, [](uint32_t* count, uint32_t* data) {
              if (!data) { *count = 3; return VK_SUCCESS; }
              data[0] = 42; *count = 1; return VK_SUCCESS;
          }) == VK_SUCCESS && values == std::vector<uint32_t>{42},
          "a shrinking list discards uninitialized tail entries");

    unsigned emptyCalls = 0;
    Check(Enumerate(values, [&](uint32_t* count, uint32_t*) {
              ++emptyCalls; *count = 0; return VK_SUCCESS;
          }) == VK_SUCCESS && values.empty() && emptyCalls == 1,
          "an empty list clears previous results without a data query");

    values = {99};
    Check(Enumerate(values, [](uint32_t*, uint32_t*) {
              return VK_ERROR_SURFACE_LOST_KHR;
          }) == VK_ERROR_SURFACE_LOST_KHR && values.empty(),
          "a count-query failure propagates the original error and clears stale data");
    Check(Enumerate(values, [](uint32_t* count, uint32_t* data) {
              if (!data) { *count = 2; return VK_SUCCESS; }
              data[0] = 77; return VK_ERROR_DEVICE_LOST;
          }) == VK_ERROR_DEVICE_LOST && values.empty(),
          "a data-query failure never publishes partial results");

    unsigned countCalls = 0;
    dataCalls = 0;
    Check(Enumerate(values, [&](uint32_t* count, uint32_t* data) {
              if (!data) { ++countCalls; *count = 1; return VK_SUCCESS; }
              ++dataCalls; data[0] = 5; return VK_INCOMPLETE;
          }) == VK_INCOMPLETE && values.empty() &&
              countCalls == kEnumerationAttempts && dataCalls == kEnumerationAttempts,
          "perpetual list changes have a bounded retry budget and no partial output");
    countCalls = 0;
    Check(Enumerate(values, [&](uint32_t* count, uint32_t*) {
              ++countCalls; *count = 0; return VK_INCOMPLETE;
          }) == VK_INCOMPLETE && countCalls == kEnumerationAttempts && values.empty(),
          "an incomplete zero-count query cannot spin forever");

    Check(!SupportsVelloCompute(VK_QUEUE_GRAPHICS_BIT, true),
          "scalar block layout does not make a graphics-only queue compute-capable");
    Check(!SupportsVelloCompute(VK_QUEUE_GRAPHICS_BIT | VK_QUEUE_COMPUTE_BIT, false),
          "a compute queue still requires scalar block layout for Vello");
    Check(SupportsVelloCompute(VK_QUEUE_GRAPHICS_BIT | VK_QUEUE_COMPUTE_BIT, true),
          "a compute queue with scalar block layout enables Vello compute");

    const auto queueFamily = [](VkQueueFlags flags, uint32_t count = 1) {
        VkQueueFamilyProperties properties{};
        properties.queueFlags = flags;
        properties.queueCount = count;
        return properties;
    };
    auto selection = SelectQueueFamilies(std::vector<VkQueueFamilyProperties>{},
        [](uint32_t) { return true; });
    Check(!selection.Complete(), "a device without queues cannot render");
    selection = SelectQueueFamilies(
        std::vector{queueFamily(VK_QUEUE_GRAPHICS_BIT), queueFamily(VK_QUEUE_TRANSFER_BIT)},
        [](uint32_t index) { return index == 1; });
    Check(selection.Complete() && selection.graphics == 0 && selection.present == 1,
          "graphics and presentation may use separate queue families");
    selection = SelectQueueFamilies(std::vector{
        queueFamily(VK_QUEUE_GRAPHICS_BIT),
        queueFamily(VK_QUEUE_GRAPHICS_BIT | VK_QUEUE_COMPUTE_BIT)},
        [](uint32_t) { return true; });
    Check(selection.Complete() && selection.graphics == 1 && selection.present == 1 &&
              (selection.graphicsFlags & VK_QUEUE_COMPUTE_BIT),
          "a unified graphics/compute/presentation family is preferred");
    selection = SelectQueueFamilies(std::vector{
        queueFamily(VK_QUEUE_GRAPHICS_BIT),
        queueFamily(VK_QUEUE_GRAPHICS_BIT | VK_QUEUE_COMPUTE_BIT)},
        [](uint32_t index) { return index == 0; });
    Check(selection.graphics == 0 && selection.present == 0,
          "an existing unified family keeps the single-queue path");
    selection = SelectQueueFamilies(std::vector{
        queueFamily(VK_QUEUE_GRAPHICS_BIT), queueFamily(VK_QUEUE_TRANSFER_BIT),
        queueFamily(VK_QUEUE_GRAPHICS_BIT | VK_QUEUE_COMPUTE_BIT)},
        [](uint32_t index) { return index == 1; });
    Check(selection.graphics == 2 && selection.present == 1,
          "a split graphics family prefers compute capability when available");
    selection = SelectQueueFamilies(std::vector{queueFamily(VK_QUEUE_GRAPHICS_BIT)},
        [](uint32_t) { return false; });
    Check(!selection.Complete(), "graphics support alone cannot present");
    selection = SelectQueueFamilies(std::vector{queueFamily(VK_QUEUE_TRANSFER_BIT)},
        [](uint32_t) { return true; });
    Check(!selection.Complete(), "presentation support alone cannot draw");
    unsigned presentQueries = 0;
    selection = SelectQueueFamilies(std::vector{queueFamily(VK_QUEUE_GRAPHICS_BIT, 0)},
        [&](uint32_t) { ++presentQueries; return true; });
    Check(!selection.Complete() && presentQueries == 0,
          "empty queue families are never selected or queried for presentation");

    Check(!SupportsDrmFormatModifiers(VK_API_VERSION_1_0, true, true) &&
          !SupportsDrmFormatModifiers(VK_API_VERSION_1_1, true, false) &&
          SupportsDrmFormatModifiers(VK_API_VERSION_1_1, true, true) &&
          SupportsDrmFormatModifiers(VK_API_VERSION_1_2, true, false) &&
          !SupportsDrmFormatModifiers(VK_API_VERSION_1_2, false, true),
          "DRM modifiers enable image-format-list dependencies at the effective API version");

    using namespace jalium::vulkan_graphics_constants;
    Check(NeedsUniformBuffer(128) && !NeedsUniformBuffer(240) && !NeedsUniformBuffer(256),
          "graphics constants honor the actual device push-constant limit");
    // A small instruction stream isolates the relocation contract: pointer
    // types, variable storage, decorations and immutable source/result IDs.
    const uint32_t pushModule[] = {
        0x07230203, 0x00010000, 0, 5, 0,
        (3u << 16) | 22u, 1, 32,                // OpTypeFloat
        (4u << 16) | 32u, 2, 9, 1,             // OpTypePointer PushConstant
        (4u << 16) | 59u, 2, 3, 9};            // OpVariable PushConstant
    std::vector<uint32_t> relocated;
    Check(RemapToUniform(pushModule, std::size(pushModule), relocated) &&
              relocated.size() == std::size(pushModule) + 8 &&
              relocated[7] == 34 && relocated[8] == kDescriptorSet &&
              relocated[11] == 33 && relocated[12] == 0 &&
              relocated[18] == 2 && relocated[23] == 2 && pushModule[10] == 9,
          "SPIR-V relocation binds set 2 without changing source or result IDs");
    const uint32_t noPush[] = {0x07230203, 0x00010000, 0, 2, 0,
                              (3u << 16) | 22u, 1, 32};
    Check(RemapToUniform(noPush, std::size(noPush), relocated) &&
              relocated == std::vector<uint32_t>(std::begin(noPush), std::end(noPush)),
          "shaders without push constants remain byte-identical");
    const uint32_t malformed[] = {0x07230203, 0x00010000, 0, 2, 0, 0};
    Check(!RemapToUniform(malformed, std::size(malformed), relocated) && relocated.empty(),
          "zero-length SPIR-V instructions are rejected without a loop");
    const uint32_t truncated[] = {0x07230203, 0x00010000, 0, 2, 0, (4u << 16) | 22u};
    Check(!RemapToUniform(truncated, std::size(truncated), relocated) && relocated.empty(),
          "truncated SPIR-V instructions cannot read past the input");

    if (failures == 0) std::cout << "Vulkan capability tests passed.\n";
    return failures == 0 ? 0 : 1;
}
