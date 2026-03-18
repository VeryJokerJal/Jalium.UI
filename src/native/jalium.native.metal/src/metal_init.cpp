#include "jalium_api.h"
#include "metal_backend.h"

#include <atomic>

#ifdef _WIN32
#include <Windows.h>
#endif

static std::atomic<bool> s_registered{false};

namespace jalium {

static IRenderBackend* CreateMetalBackendWrapper()
{
    return CreateMetalBackend();
}

static int32_t IsMetalBackendAvailable()
{
#ifdef __APPLE__
    // On macOS/iOS, Metal is available if we can create a system default device.
    // This check is lightweight — MTLCreateSystemDefaultDevice() returns nil on
    // hardware that does not support Metal (pre-2012 Macs).
    @autoreleasepool {
        id<MTLDevice> device = MTLCreateSystemDefaultDevice();
        return device ? 1 : 0;
    }
#else
    // Metal is not available on non-Apple platforms.
    return 0;
#endif
}

void RegisterMetalBackend()
{
    bool expected = false;
    if (s_registered.compare_exchange_strong(expected, true)) {
        jalium_register_backend_ex(
            JALIUM_BACKEND_METAL,
            reinterpret_cast<JaliumBackendFactory>(&CreateMetalBackendWrapper),
            &IsMetalBackendAvailable);
    }
}

} // namespace jalium

extern "C" {
#if defined(_WIN32) && defined(JALIUM_STATIC)
void jalium_metal_init()
#elif defined(_WIN32)
__declspec(dllexport) void jalium_metal_init()
#else
__attribute__((visibility("default"))) void jalium_metal_init()
#endif
{
    jalium::RegisterMetalBackend();
}
}

#if !defined(JALIUM_STATIC) && defined(_WIN32)
BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    (void)hModule;
    (void)lpReserved;

    if (ul_reason_for_call == DLL_PROCESS_ATTACH) {
        jalium::RegisterMetalBackend();
    }

    return TRUE;
}
#elif !defined(_WIN32)
__attribute__((constructor))
static void RegisterMetalBackendOnLoad()
{
    jalium::RegisterMetalBackend();
}
#endif
