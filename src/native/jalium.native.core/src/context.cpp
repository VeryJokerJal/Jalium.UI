#include "jalium_internal.h"

#ifdef __ANDROID__
#include <android/log.h>
#define LOGI_CTX(...) __android_log_print(ANDROID_LOG_INFO, "JaliumContext", __VA_ARGS__)
#define LOGE_CTX(...) __android_log_print(ANDROID_LOG_ERROR, "JaliumContext", __VA_ARGS__)
#else
#define LOGI_CTX(...)
#define LOGE_CTX(...)
#endif

// ============================================================================
// C API
// ============================================================================

extern "C" {

JALIUM_API JaliumContext* jalium_context_create(JaliumBackend backend) {
    auto& registry = jalium::GetBackendRegistry();

    LOGI_CTX("jalium_context_create: requested backend=%d", (int)backend);

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
