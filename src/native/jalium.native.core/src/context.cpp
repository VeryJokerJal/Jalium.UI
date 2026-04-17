#include "jalium_internal.h"

#include <atomic>
#include <cstdlib>
#include <cstring>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>
#else
#include <dlfcn.h>
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
    auto& registry = jalium::GetBackendRegistry();

    LOGI_CTX("jalium_context_create: requested backend=%d", (int)backend);

    // Honor JALIUM_RENDER_BACKEND unconditionally, because the managed
    // RenderBackendSelector on Windows resolves Auto → D3D12 *before* reaching
    // the native layer (via IsBackendAvailable which only returns true for
    // whatever the NativeMethods static ctor eagerly init'd — D3D12 on Windows).
    // By the time we get here "backend" is already D3D12 even if the user asked
    // for Auto with the env var hoping to pick Vulkan. Override it here.
    {
        JaliumBackend envOverride = ReadBackendEnvOverride();
        if (envOverride != JALIUM_BACKEND_AUTO) {
            // The per-platform NativeMethods static ctor only eagerly initializes
            // one GPU backend (D3D12 on Windows, Metal on macOS, Vulkan on
            // Linux/Android). Secondary backends stay unloaded until something
            // calls into their DLL. If the user asked for a backend that hasn't
            // been loaded yet, dlopen it so its DllMain/constructor registers
            // its factory. Only then can registry.IsAvailable return the truth.
            if (!registry.IsAvailable(envOverride)) {
#ifdef _WIN32
                const char* libName = nullptr;
                switch (envOverride) {
                    case JALIUM_BACKEND_VULKAN:   libName = "jalium.native.vulkan.dll"; break;
                    case JALIUM_BACKEND_D3D12:    libName = "jalium.native.d3d12.dll"; break;
                    case JALIUM_BACKEND_METAL:    libName = "jalium.native.metal.dll"; break;
                    case JALIUM_BACKEND_SOFTWARE: libName = "jalium.native.software.dll"; break;
                    default: break;
                }
                if (libName) {
                    (void)LoadLibraryA(libName);
                }
#else
                const char* libName = nullptr;
                switch (envOverride) {
                    case JALIUM_BACKEND_VULKAN:   libName = "libjalium.native.vulkan.so"; break;
                    case JALIUM_BACKEND_SOFTWARE: libName = "libjalium.native.software.so"; break;
                    default: break;
                }
                if (libName) {
                    (void)dlopen(libName, RTLD_NOW | RTLD_GLOBAL);
                }
#endif
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
        // Mirror BackendRegistry::MAX_BACKENDS — JaliumBackend values index this array.
        static constexpr int kMaxOnDemandBackends = 16;
        static std::atomic_flag s_onDemandAttempted[kMaxOnDemandBackends] = {};

        const int backendIdx = static_cast<int>(actualBackend);
        const bool firstAttempt = backendIdx >= 0 && backendIdx < kMaxOnDemandBackends
            && !s_onDemandAttempted[backendIdx].test_and_set(std::memory_order_acq_rel);

        if (firstAttempt) {
#ifdef _WIN32
            const char* libName = nullptr;
            switch (actualBackend) {
                case JALIUM_BACKEND_VULKAN:   libName = "jalium.native.vulkan.dll"; break;
                case JALIUM_BACKEND_D3D12:    libName = "jalium.native.d3d12.dll"; break;
                case JALIUM_BACKEND_METAL:    libName = "jalium.native.metal.dll"; break;
                case JALIUM_BACKEND_SOFTWARE: libName = "jalium.native.software.dll"; break;
                default: break;
            }
            if (libName) {
                LOGI_CTX("jalium_context_create: on-demand loading %s", libName);
                (void)LoadLibraryA(libName);
            }
#else
            const char* libName = nullptr;
            switch (actualBackend) {
                case JALIUM_BACKEND_VULKAN:   libName = "libjalium.native.vulkan.so"; break;
                case JALIUM_BACKEND_SOFTWARE: libName = "libjalium.native.software.so"; break;
                default: break;
            }
            if (libName) {
                LOGI_CTX("jalium_context_create: on-demand loading %s", libName);
                (void)dlopen(libName, RTLD_NOW | RTLD_GLOBAL);
            }
#endif
        }
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
    *info = {};
    return JALIUM_ERROR_NOT_SUPPORTED;
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

} // extern "C"
