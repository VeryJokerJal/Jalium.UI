#include "jalium_api.h"
#include "software_backend.h"

#include <atomic>

#ifdef _WIN32
#include <Windows.h>
#endif

static std::atomic<bool> s_registered{false};

namespace jalium {

static IRenderBackend* CreateSoftwareBackendWrapper()
{
    return CreateSoftwareBackend();
}

static int32_t IsSoftwareBackendAvailable()
{
    // Software rasterizer is always available as a fallback.
    return 1;
}

void RegisterSoftwareBackend()
{
    bool expected = false;
    if (s_registered.compare_exchange_strong(expected, true)) {
        jalium_register_backend_ex(
            JALIUM_BACKEND_SOFTWARE,
            reinterpret_cast<JaliumBackendFactory>(&CreateSoftwareBackendWrapper),
            &IsSoftwareBackendAvailable);
    }
}

} // namespace jalium

extern "C" {
#if defined(_WIN32) && defined(JALIUM_STATIC)
void jalium_software_init()
#elif defined(_WIN32)
__declspec(dllexport) void jalium_software_init()
#else
__attribute__((visibility("default"))) void jalium_software_init()
#endif
{
    jalium::RegisterSoftwareBackend();
}
}

#if !defined(JALIUM_STATIC) && defined(_WIN32)
BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    (void)hModule;
    (void)lpReserved;

    if (ul_reason_for_call == DLL_PROCESS_ATTACH) {
        jalium::RegisterSoftwareBackend();
    }

    return TRUE;
}
#elif !defined(_WIN32)
__attribute__((constructor))
static void RegisterSoftwareBackendOnLoad()
{
    jalium::RegisterSoftwareBackend();
}
#endif
