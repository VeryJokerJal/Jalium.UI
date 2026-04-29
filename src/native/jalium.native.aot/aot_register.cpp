// jalium.native.aot — static aggregation entry point.
//
// In a NativeAOT single-exe build every native module is linked as .lib, so
// there is no DllMain to auto-register backends with the core BackendRegistry.
// This translation unit exposes one extern "C" aggregator function that the
// managed entry point calls exactly once at startup; that call strongly
// references each backend's *_init() function which in turn forces the
// linker to pull the corresponding .obj (and therefore the backend factory)
// into the final image.

#include "jalium_api.h"

extern "C" {

// Forward declarations for the per-backend init symbols. In the static flavor
// each one is provided by jalium_<backend>_init.cpp without dllexport.
void jalium_d3d12_init(void);
void jalium_software_init(void);
#ifdef JALIUM_AOT_INCLUDE_VULKAN
void jalium_vulkan_init(void);
#endif
#ifdef JALIUM_AOT_INCLUDE_BROWSER
int  jalium_webview2_initialize(void);
void jalium_webview2_shutdown(void);
#endif

// jalium.native.media has no "register" entry — instead force-link by taking the
// address of jalium_media_initialize so the linker pulls the .lib in.
typedef int jalium_media_status_t;
extern jalium_media_status_t jalium_media_initialize(void);
extern void                  jalium_media_shutdown(void);
extern uint32_t              jalium_media_supported_video_codecs(void);

// Sole AOT aggregation entry. Managed code P/Invokes this once before any
// jalium_context_create. JALIUM_API collapses to nothing under JALIUM_STATIC,
// so the symbol is just an extern "C" function in the .lib that NativeAOT
// resolves through DirectPInvoke without any LoadLibrary.
JALIUM_API void jalium_aot_register_all_backends(void) {
#if defined(_WIN32)
    // Primary GPU backend on Windows.
    jalium_d3d12_init();
#endif

#ifdef JALIUM_AOT_INCLUDE_VULKAN
    jalium_vulkan_init();
#endif

    // Software rasterizer is always registered as the universal fallback.
    jalium_software_init();

    // Force-pull jalium.native.media symbols so NativeAOT's DirectPInvoke can
    // resolve jalium_media_* without needing a runtime DLL search. A taken-address
    // alone can be elided by LTO; a real init/shutdown round-trip cannot.
    if (jalium_media_initialize() == 0) {
        (void)jalium_media_supported_video_codecs();
        jalium_media_shutdown();
    }
}

} // extern "C"
