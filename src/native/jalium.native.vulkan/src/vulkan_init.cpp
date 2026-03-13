#include "jalium_api.h"
#include "vulkan_backend.h"
#include "vulkan_runtime.h"

#include <atomic>

#ifdef _WIN32
#include <Windows.h>
#endif

static std::atomic<bool> s_registered{false};

namespace jalium {

static IRenderBackend* CreateVulkanBackendWrapper()
{
    return CreateVulkanBackend();
}

static int32_t IsVulkanBackendAvailable()
{
    return IsExperimentalVulkanEnabled() && IsVulkanRuntimeAvailable() ? 1 : 0;
}

void RegisterVulkanBackend()
{
    bool expected = false;
    if (s_registered.compare_exchange_strong(expected, true)) {
        jalium_register_backend_ex(
            JALIUM_BACKEND_VULKAN,
            reinterpret_cast<JaliumBackendFactory>(&CreateVulkanBackendWrapper),
            &IsVulkanBackendAvailable);
    }
}

} // namespace jalium

extern "C" {
#if defined(_WIN32) && defined(JALIUM_STATIC)
void jalium_vulkan_init()
#elif defined(_WIN32)
__declspec(dllexport) void jalium_vulkan_init()
#else
__attribute__((visibility("default"))) void jalium_vulkan_init()
#endif
{
    jalium::RegisterVulkanBackend();
}
}

#if !defined(JALIUM_STATIC) && defined(_WIN32)
BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    (void)hModule;
    (void)lpReserved;

    if (ul_reason_for_call == DLL_PROCESS_ATTACH) {
        jalium::RegisterVulkanBackend();
    }

    return TRUE;
}
#elif !defined(_WIN32)
__attribute__((constructor))
static void RegisterVulkanBackendOnLoad()
{
    jalium::RegisterVulkanBackend();
}
#endif
