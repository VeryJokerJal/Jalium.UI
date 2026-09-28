#include "jalium_internal.h"

#include <atomic>
#include <cstdlib>
#include <cstring>
#include <new>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>
#else
#include <dlfcn.h>
#include <limits.h>
#include <stdio.h>
#endif

#ifdef __ANDROID__
#include <android/log.h>
#define LOGI_CTX(...) __android_log_print(ANDROID_LOG_INFO, "JaliumContext", __VA_ARGS__)
#define LOGE_CTX(...) __android_log_print(ANDROID_LOG_ERROR, "JaliumContext", __VA_ARGS__)
#else
#define LOGI_CTX(...)
#define LOGE_CTX(...)
#endif

namespace {

// Backend modules are part of the same trusted RID payload as
// jalium.native.core. Loading a bare filename would let the process current
// directory participate in DLL/DSO resolution, so resolve the backend only
// beside this module.
#ifdef _WIN32
const wchar_t* GetBackendLibraryName(JaliumBackend backend)
{
    switch (backend) {
        case JALIUM_BACKEND_VULKAN:   return L"jalium.native.vulkan.dll";
        case JALIUM_BACKEND_D3D12:    return L"jalium.native.d3d12.dll";
        case JALIUM_BACKEND_METAL:    return L"jalium.native.metal.dll";
        case JALIUM_BACKEND_SOFTWARE: return L"jalium.native.software.dll";
        default:                      return nullptr;
    }
}

bool LoadBackendLibraryFromModuleDirectory(JaliumBackend backend)
{
    const wchar_t* libraryName = GetBackendLibraryName(backend);
    if (!libraryName) return false;

    static const unsigned char moduleAnchor = 0;
    HMODULE module = nullptr;
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&moduleAnchor),
            &module)) {
        return false;
    }

    wchar_t modulePath[32768] = {};
    const DWORD length = GetModuleFileNameW(
        module, modulePath, static_cast<DWORD>(sizeof(modulePath) / sizeof(modulePath[0])));
    if (length == 0 || length >= sizeof(modulePath) / sizeof(modulePath[0])) {
        return false;
    }

    size_t directoryLength = length;
    while (directoryLength > 0 &&
           modulePath[directoryLength - 1] != L'\\' &&
           modulePath[directoryLength - 1] != L'/') {
        --directoryLength;
    }
    if (directoryLength == 0) return false;

    const size_t libraryNameLength = wcslen(libraryName);
    if (directoryLength + libraryNameLength >=
        sizeof(modulePath) / sizeof(modulePath[0])) {
        return false;
    }

    memcpy(
        modulePath + directoryLength,
        libraryName,
        (libraryNameLength + 1) * sizeof(wchar_t));
    return LoadLibraryExW(
               modulePath,
               nullptr,
               LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR |
                   LOAD_LIBRARY_SEARCH_DEFAULT_DIRS) != nullptr;
}
#else
const char* GetBackendLibraryName(JaliumBackend backend)
{
#if defined(__APPLE__)
    switch (backend) {
        case JALIUM_BACKEND_VULKAN:   return "libjalium.native.vulkan.dylib";
        case JALIUM_BACKEND_METAL:    return "libjalium.native.metal.dylib";
        case JALIUM_BACKEND_SOFTWARE: return "libjalium.native.software.dylib";
        default:                      return nullptr;
    }
#else
    switch (backend) {
        case JALIUM_BACKEND_VULKAN:   return "libjalium.native.vulkan.so";
        case JALIUM_BACKEND_SOFTWARE: return "libjalium.native.software.so";
        default:                      return nullptr;
    }
#endif
}

bool LoadBackendLibraryFromModuleDirectory(JaliumBackend backend)
{
    const char* libraryName = GetBackendLibraryName(backend);
    if (!libraryName) return false;

    static const unsigned char moduleAnchor = 0;
    Dl_info moduleInfo{};
    if (dladdr(&moduleAnchor, &moduleInfo) == 0 || !moduleInfo.dli_fname) {
        return false;
    }

    char modulePath[PATH_MAX] = {};
    if (!realpath(moduleInfo.dli_fname, modulePath)) {
        return false;
    }

    char* separator = strrchr(modulePath, '/');
    if (!separator) return false;
    const size_t directoryLength =
        static_cast<size_t>(separator - modulePath) + 1u;
    const size_t libraryNameLength = strlen(libraryName);
    if (directoryLength + libraryNameLength >= sizeof(modulePath)) {
        return false;
    }

    memcpy(
        modulePath + directoryLength,
        libraryName,
        libraryNameLength + 1u);
    return dlopen(modulePath, RTLD_NOW | RTLD_GLOBAL) != nullptr;
}
#endif

bool TryLoadBackendLibraryOnce(JaliumBackend backend)
{
    // Mirror BackendRegistry::MAX_BACKENDS — JaliumBackend values index this array.
    static constexpr int kMaxOnDemandBackends = 16;
    static std::atomic_flag attempted[kMaxOnDemandBackends] = {};

    const int backendIndex = static_cast<int>(backend);
    if (backendIndex < 0 || backendIndex >= kMaxOnDemandBackends ||
        attempted[backendIndex].test_and_set(std::memory_order_acq_rel)) {
        return false;
    }

    return LoadBackendLibraryFromModuleDirectory(backend);
}

// Reads JALIUM_RENDER_BACKEND and returns a JaliumBackend override, or
// JALIUM_BACKEND_AUTO if no valid override is present. Accepts the same values
// the managed selector understands: "vulkan"/"vk", "d3d12"/"dx12", "metal",
// "software"/"cpu". Anything else (including empty/unset) returns Auto.
JaliumBackend ReadBackendEnvOverride()
{
#if defined(_WIN32)
    char* raw = nullptr;
    size_t len = 0;
    if (_dupenv_s(&raw, &len, "JALIUM_RENDER_BACKEND") != 0 || raw == nullptr) {
        return JALIUM_BACKEND_AUTO;
    }
    // Lowercase in-place for case-insensitive matching.
    for (char* p = raw; *p; ++p) {
        if (*p >= 'A' && *p <= 'Z') {
            *p = static_cast<char>(*p + ('a' - 'A'));
        }
    }
    JaliumBackend selected = JALIUM_BACKEND_AUTO;
    if (strcmp(raw, "vulkan") == 0 || strcmp(raw, "vk") == 0) {
        selected = JALIUM_BACKEND_VULKAN;
    } else if (strcmp(raw, "d3d12") == 0 || strcmp(raw, "dx12") == 0 || strcmp(raw, "direct3d12") == 0) {
        selected = JALIUM_BACKEND_D3D12;
    } else if (strcmp(raw, "metal") == 0) {
        selected = JALIUM_BACKEND_METAL;
    } else if (strcmp(raw, "software") == 0 || strcmp(raw, "cpu") == 0) {
        selected = JALIUM_BACKEND_SOFTWARE;
    }
    free(raw);
    return selected;
#else
    const char* raw = std::getenv("JALIUM_RENDER_BACKEND");
    if (!raw || *raw == '\0') {
        return JALIUM_BACKEND_AUTO;
    }
    // Case-insensitive compare helper.
    auto iequals = [](const char* a, const char* b) {
        while (*a && *b) {
            char ca = (*a >= 'A' && *a <= 'Z') ? (*a + ('a' - 'A')) : *a;
            char cb = (*b >= 'A' && *b <= 'Z') ? (*b + ('a' - 'A')) : *b;
            if (ca != cb) return false;
            ++a; ++b;
        }
        return *a == 0 && *b == 0;
    };
    if (iequals(raw, "vulkan") || iequals(raw, "vk")) return JALIUM_BACKEND_VULKAN;
    if (iequals(raw, "d3d12") || iequals(raw, "dx12") || iequals(raw, "direct3d12")) return JALIUM_BACKEND_D3D12;
    if (iequals(raw, "metal")) return JALIUM_BACKEND_METAL;
    if (iequals(raw, "software") || iequals(raw, "cpu")) return JALIUM_BACKEND_SOFTWARE;
    return JALIUM_BACKEND_AUTO;
#endif
}

} // namespace

// ============================================================================
// C API
// ============================================================================

extern "C" {

JALIUM_API JaliumContext* jalium_context_create(JaliumBackend backend) {
    try {
    auto& registry = jalium::GetBackendRegistry();

    LOGI_CTX("jalium_context_create: requested backend=%d", (int)backend);

    // Honor JALIUM_RENDER_BACKEND unconditionally, because the managed
    // RenderBackendSelector resolves Auto → first-available concrete backend
    // *before* reaching the native layer (via IsBackendAvailable, which only
    // returns true for backends whose DLL has been loaded into this process).
    // By the time we get here "backend" is already the platform default even
    // if the user set the env var hoping to pick a different one. Override it
    // here so the env var still wins after-the-fact.
    {
        JaliumBackend envOverride = ReadBackendEnvOverride();
        if (envOverride != JALIUM_BACKEND_AUTO) {
            // Every backend in NativeMethods is strictly lazy — DLLs aren't
            // brought in until EnsureBackendInitialized fires for that backend.
            // If the env override picks a backend that nobody has loaded yet,
            // dlopen it here so its DllMain / __attribute__((constructor)) can
            // register its factory. Only after that can registry.IsAvailable
            // return the truth.
            if (!registry.IsAvailable(envOverride)) {
                (void)TryLoadBackendLibraryOnce(envOverride);
            }
            if (registry.IsAvailable(envOverride)) {
                backend = envOverride;
            }
        }
    }

    JaliumBackend actualBackend = backend;
    if (backend == JALIUM_BACKEND_AUTO) {
        const JaliumBackend preferredOrder[] = {
#if defined(_WIN32)
            JALIUM_BACKEND_D3D12,
            JALIUM_BACKEND_VULKAN,
            JALIUM_BACKEND_SOFTWARE
#elif defined(__APPLE__)
            JALIUM_BACKEND_METAL,
            JALIUM_BACKEND_VULKAN,
            JALIUM_BACKEND_SOFTWARE
#else
            JALIUM_BACKEND_VULKAN,
            JALIUM_BACKEND_SOFTWARE
#endif
        };

        for (auto candidate : preferredOrder) {
            bool avail = registry.IsAvailable(candidate);
            LOGI_CTX("  candidate %d: available=%d", (int)candidate, avail ? 1 : 0);
            if (avail) {
                actualBackend = candidate;
                break;
            }
        }

        if (actualBackend == JALIUM_BACKEND_AUTO) {
            LOGE_CTX("jalium_context_create: no backend available!");
            return nullptr;
        }
    }

    LOGI_CTX("jalium_context_create: using backend=%d", (int)actualBackend);

    // On-demand load: if the target backend hasn't been loaded yet (its DLL
    // hasn't been loaded → DllMain hasn't registered the factory), load it
    // now. This covers both the env-var override path and the case where the
    // managed layer passes an explicit backend (e.g. RenderBackend.Vulkan)
    // without the env var.
    //
    // We attempt the load at most once per backend for the lifetime of the
    // process. Without this guard, every jalium_context_create call where
    // IsAvailable() stays false (e.g. the DLL loads but its runtime probe
    // disqualifies the backend so no factory gets registered) would call
    // LoadLibrary/dlopen again and leak another module refcount.
    if (actualBackend != JALIUM_BACKEND_AUTO && !registry.IsAvailable(actualBackend)) {
        (void)TryLoadBackendLibraryOnce(actualBackend);
    }

    auto factory = registry.GetFactory(actualBackend);
    if (!factory) {
        LOGE_CTX("jalium_context_create: no factory for backend %d", (int)actualBackend);
        return nullptr;
    }

    auto* rawBackend = reinterpret_cast<jalium::IRenderBackend*>(factory());
    if (!rawBackend) {
        LOGE_CTX("jalium_context_create: factory returned null for backend %d", (int)actualBackend);
        return nullptr;
    }

    auto backendImpl = std::unique_ptr<jalium::IRenderBackend>(rawBackend);
    auto* ctx = new jalium::Context(actualBackend, std::move(backendImpl));
    LOGI_CTX("jalium_context_create: success, ctx=%p", (void*)ctx);
    return reinterpret_cast<JaliumContext*>(ctx);
    } catch (const std::bad_alloc&) {
        LOGE_CTX("jalium_context_create: allocation failed");
        return nullptr;
    } catch (...) {
        // Never allow a backend factory exception to cross the C ABI.
        LOGE_CTX("jalium_context_create: backend factory threw");
        return nullptr;
    }
}

JALIUM_API JaliumResult jalium_context_set_gpu_preference(
    JaliumContext* ctx,
    JaliumGpuPreference gpuPreference)
{
    if (!ctx) return JALIUM_ERROR_INVALID_ARGUMENT;
    if (gpuPreference < JALIUM_GPU_PREFERENCE_AUTO ||
        gpuPreference > JALIUM_GPU_PREFERENCE_MINIMUM_POWER) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    auto* impl = reinterpret_cast<jalium::Context*>(ctx)->GetBackendImpl();
    if (!impl) return JALIUM_ERROR_INVALID_STATE;
    return impl->SetGpuPreference(gpuPreference);
}

JALIUM_API void jalium_context_destroy(JaliumContext* ctx) {
    if (ctx) {
        delete reinterpret_cast<jalium::Context*>(ctx);
    }
}

JALIUM_API JaliumBackend jalium_context_get_backend(JaliumContext* ctx) {
    if (!ctx) return JALIUM_BACKEND_AUTO;
    return reinterpret_cast<jalium::Context*>(ctx)->GetBackend();
}

JALIUM_API JaliumResult jalium_context_get_last_error(JaliumContext* ctx) {
    if (!ctx) return JALIUM_ERROR_INVALID_ARGUMENT;
    return reinterpret_cast<jalium::Context*>(ctx)->GetLastError();
}

JALIUM_API const wchar_t* jalium_context_get_error_message(JaliumContext* ctx) {
    if (!ctx) return nullptr;
    return reinterpret_cast<jalium::Context*>(ctx)->GetErrorMessage();
}

JALIUM_API JaliumResult jalium_context_check_device_status(JaliumContext* ctx) {
    if (!ctx) return JALIUM_ERROR_INVALID_ARGUMENT;
    auto* impl = reinterpret_cast<jalium::Context*>(ctx)->GetBackendImpl();
    if (!impl) return JALIUM_ERROR_INVALID_ARGUMENT;
    return impl->CheckDeviceStatus();
}

JALIUM_API JaliumResult jalium_context_get_adapter_info(JaliumContext* ctx, JaliumAdapterInfo* info) {
    if (!ctx || !info) return JALIUM_ERROR_INVALID_ARGUMENT;
    *info = JaliumAdapterInfo{};
    auto* impl = reinterpret_cast<jalium::Context*>(ctx)->GetBackendImpl();
    if (!impl) return JALIUM_ERROR_INVALID_ARGUMENT;
    // 转发到具体 backend；D3D12 已实现，其他 backend 暂时走基类的 NOT_SUPPORTED 默认。
    return impl->GetAdapterInfo(info);
}

JALIUM_API JaliumRenderingEngine jalium_render_target_get_engine(JaliumRenderTarget* rt) {
    if (!rt) return JALIUM_ENGINE_AUTO;
    return reinterpret_cast<jalium::RenderTarget*>(rt)->GetRenderingEngine();
}

JALIUM_API JaliumResult jalium_render_target_set_engine(
    JaliumRenderTarget* rt,
    JaliumRenderingEngine engine)
{
    if (!rt) return JALIUM_ERROR_INVALID_ARGUMENT;
    return reinterpret_cast<jalium::RenderTarget*>(rt)->SetRenderingEngine(engine);
}

JALIUM_API JaliumResult jalium_context_set_default_engine(
    JaliumContext* ctx,
    JaliumRenderingEngine engine)
{
    if (!ctx) return JALIUM_ERROR_INVALID_ARGUMENT;
    reinterpret_cast<jalium::Context*>(ctx)->SetDefaultEngine(engine);
    return JALIUM_OK;
}

JALIUM_API JaliumRenderingEngine jalium_context_get_default_engine(JaliumContext* ctx) {
    if (!ctx) return JALIUM_ENGINE_AUTO;
    return reinterpret_cast<jalium::Context*>(ctx)->GetDefaultEngine();
}

JALIUM_API JaliumResult jalium_render_target_query_gpu_stats(
    JaliumRenderTarget* rt,
    JaliumGpuStats* out)
{
    if (!rt || !out) return JALIUM_ERROR_INVALID_ARGUMENT;
    return reinterpret_cast<jalium::RenderTarget*>(rt)->QueryGpuStats(out);
}

JALIUM_API JaliumResult jalium_render_target_get_present_info(
    JaliumRenderTarget* rt,
    JaliumPresentInfo* out)
{
    if (!rt || !out) return JALIUM_ERROR_INVALID_ARGUMENT;
    return reinterpret_cast<jalium::RenderTarget*>(rt)->GetPresentInfo(out);
}

JALIUM_API JaliumResult jalium_render_target_query_gpu_timing(
    JaliumRenderTarget* rt,
    JaliumGpuTimingStats* out)
{
    if (!rt || !out) return JALIUM_ERROR_INVALID_ARGUMENT;
    return reinterpret_cast<jalium::RenderTarget*>(rt)->QueryGpuTiming(out);
}

JALIUM_API JaliumResult jalium_render_target_wait_for_completion(
    JaliumRenderTarget* rt)
{
    if (!rt) return JALIUM_ERROR_INVALID_ARGUMENT;
    return reinterpret_cast<jalium::RenderTarget*>(rt)->WaitForCompletion();
}

JALIUM_API intptr_t jalium_render_target_get_frame_latency_waitable(
    JaliumRenderTarget* rt)
{
    if (!rt) return 0;
    return reinterpret_cast<jalium::RenderTarget*>(rt)->GetFrameLatencyWaitable();
}

JALIUM_API JaliumResult jalium_render_target_reclaim_idle_resources(
    JaliumRenderTarget* rt)
{
    if (!rt) return JALIUM_ERROR_INVALID_ARGUMENT;
    return reinterpret_cast<jalium::RenderTarget*>(rt)->ReclaimIdleResources();
}

JALIUM_API JaliumResult jalium_render_target_compact_idle_framebuffer_storage(
    JaliumRenderTarget* rt)
{
    if (!rt) return JALIUM_ERROR_INVALID_ARGUMENT;
    auto* target = reinterpret_cast<jalium::RenderTarget*>(rt);
    auto* provider = dynamic_cast<jalium::CpuFramebufferStorageProvider*>(target);
    return provider ? provider->CompactIdleFramebufferStorage() : JALIUM_OK;
}

JALIUM_API JaliumResult jalium_render_target_query_main_framebuffer_owned_bytes(
    JaliumRenderTarget* rt,
    uint64_t* out_bytes)
{
    if (out_bytes) *out_bytes = 0;
    if (!rt || !out_bytes) return JALIUM_ERROR_INVALID_ARGUMENT;
    auto* target = reinterpret_cast<jalium::RenderTarget*>(rt);
    auto* provider = dynamic_cast<jalium::CpuFramebufferStorageProvider*>(target);
    return provider
        ? provider->QueryMainFramebufferOwnedBytes(out_bytes)
        : JALIUM_ERROR_NOT_SUPPORTED;
}

// Two-phase back-buffer readback (backend parity verification). Request only
// latches a pending flag consumed by the next EndDraw; Fetch blocks on the
// capture's fence and copies BGRA8 rows out. See jalium_api.h for the full
// alpha/ordering contract. Backends without an implementation inherit the
// base-class JALIUM_ERROR_NOT_SUPPORTED.
JALIUM_API JaliumResult jalium_render_target_request_readback(
    JaliumRenderTarget* rt)
{
    if (!rt) return JALIUM_ERROR_INVALID_ARGUMENT;
    return reinterpret_cast<jalium::RenderTarget*>(rt)->RequestReadback();
}

JALIUM_API JaliumResult jalium_render_target_fetch_readback(
    JaliumRenderTarget* rt,
    uint8_t* buffer,
    uint32_t buffer_stride,
    int32_t* out_width,
    int32_t* out_height)
{
    if (!rt) return JALIUM_ERROR_INVALID_ARGUMENT;
    return reinterpret_cast<jalium::RenderTarget*>(rt)->FetchReadback(
        buffer, buffer_stride, out_width, out_height);
}

} // extern "C"
