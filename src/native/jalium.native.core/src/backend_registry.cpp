#include "jalium_internal.h"

// ============================================================================
// C API Implementation
// ============================================================================

extern "C" {

JALIUM_API JaliumResult jalium_register_backend(JaliumBackend backend, JaliumBackendFactory factory) {
    if (!factory) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }
    return jalium_register_backend_ex(backend, factory, nullptr);
}

JALIUM_API JaliumResult jalium_register_backend_ex(
    JaliumBackend backend,
    JaliumBackendFactory factory,
    JaliumBackendAvailabilityCallback availability)
{
    if (!factory) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    return jalium::GetBackendRegistry().Register(backend, factory, availability);
}

JALIUM_API int32_t jalium_is_backend_available(JaliumBackend backend) {
    return jalium::GetBackendRegistry().IsAvailable(backend) ? 1 : 0;
}

} // extern "C"
