#include "jalium_api.h"
#include "d3d12_backend.h"

#ifdef _WIN32
#include <Windows.h>
#include <atomic>

// Use atomic flag to avoid mutex issues during initialization
static std::atomic<bool> s_registered{false};

namespace jalium {

// Forward declaration of the factory function
static IRenderBackend* CreateD3D12BackendWrapper() {
    return CreateD3D12Backend();
}

void RegisterD3D12Backend() {
    bool expected = false;
    if (s_registered.compare_exchange_strong(expected, true)) {
        jalium_register_backend(
            JALIUM_BACKEND_D3D12,
            reinterpret_cast<JaliumBackendFactory>(&CreateD3D12BackendWrapper));
    }
}

} // namespace jalium

// Exported initialization function for both DLL and static NativeAOT linking.
extern "C" {
#if defined(JALIUM_STATIC)
    void jalium_d3d12_init() {
#else
    __declspec(dllexport) void jalium_d3d12_init() {
#endif
        jalium::RegisterD3D12Backend();
    }
}

#if !defined(JALIUM_STATIC)
// DLL entry point - also registers as fallback
BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    switch (ul_reason_for_call) {
        case DLL_PROCESS_ATTACH:
            // Register the backend - use atomic flag to avoid issues
            // This is safe because we use atomic compare_exchange, not mutex
            jalium::RegisterD3D12Backend();
            break;
        case DLL_THREAD_ATTACH:
        case DLL_THREAD_DETACH:
            break;
        case DLL_PROCESS_DETACH:
            break;
    }
    return TRUE;
}
#endif

#else
// Non-Windows platforms - use constructor attribute
namespace jalium {
static IRenderBackend* CreateD3D12BackendWrapper() {
    return CreateD3D12Backend();
}
}

__attribute__((constructor))
static void RegisterD3D12BackendOnLoad() {
    jalium_register_backend(
        JALIUM_BACKEND_D3D12,
        reinterpret_cast<JaliumBackendFactory>(&jalium::CreateD3D12BackendWrapper));
}
#endif
