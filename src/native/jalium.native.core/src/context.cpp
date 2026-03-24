#include "jalium_internal.h"

// ============================================================================
// C API
// ============================================================================

extern "C" {

JALIUM_API JaliumContext* jalium_context_create(JaliumBackend backend) {
    auto& registry = jalium::GetBackendRegistry();

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
            if (registry.IsAvailable(candidate)) {
                actualBackend = candidate;
                break;
            }
        }

        if (actualBackend == JALIUM_BACKEND_AUTO) {
            return nullptr;
        }
    }

    auto factory = registry.GetFactory(actualBackend);
    if (!factory) return nullptr;

    auto* rawBackend = reinterpret_cast<jalium::IRenderBackend*>(factory());
    if (!rawBackend) return nullptr;

    auto backendImpl = std::unique_ptr<jalium::IRenderBackend>(rawBackend);
    auto* ctx = new jalium::Context(actualBackend, std::move(backendImpl));
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

} // extern "C"
