#include "jalium_api.h"
#include "d3d12_backend.h"
#include "d3d12_render_target.h"

#ifdef _WIN32
#include <Windows.h>
#include <atomic>

// Hybrid-graphics process hints. In NativeAOT builds this object is linked into
// the executable, so these become the well-known application exports consumed
// by NVIDIA Optimus and AMD PowerXpress. The explicit IDXGIAdapter selection in
// D3D12Backend remains authoritative; the exports also cover components that
// create a default graphics device before Jalium initializes.
//
// selectany lets a native host provide its own value without a duplicate-symbol
// failure when it statically links Jalium.
extern "C" {
    __declspec(selectany) __declspec(dllexport)
        unsigned long NvOptimusEnablement = 0x00000001;
    __declspec(selectany) __declspec(dllexport)
        int AmdPowerXpressRequestHighPerformance = 1;
}

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

#if defined(JALIUM_STATIC)
    int32_t jalium_d3d12_render_target_uses_software_display_route(
#else
    __declspec(dllexport) int32_t jalium_d3d12_render_target_uses_software_display_route(
#endif
        JaliumRenderTarget* rt) {
        if (!rt) return 0;
        // This ABI belongs to the D3D12 module and managed code calls it only
        // after checking RenderTarget.Backend. Keeping the cast here avoids
        // adding a virtual slot to the cross-backend RenderTarget ABI.
        auto* target = reinterpret_cast<jalium::D3D12RenderTarget*>(rt);
        return target->UsesSoftwareDisplayRoute() ? 1 : 0;
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
