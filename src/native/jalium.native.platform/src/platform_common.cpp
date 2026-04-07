#include "jalium_platform.h"

#include <atomic>
#include <mutex>

// ============================================================================
// Platform Initialization (ref-counted)
// ============================================================================

static std::atomic<int32_t> g_initRefCount{0};

// Forward declarations for platform-specific init/shutdown
extern JaliumResult jalium_platform_init_impl();
extern void jalium_platform_shutdown_impl();
extern JaliumPlatform jalium_platform_get_current_impl();

JaliumResult jalium_platform_init(void)
{
    if (g_initRefCount.fetch_add(1, std::memory_order_acq_rel) == 0)
    {
        JaliumResult result = jalium_platform_init_impl();
        if (result != JALIUM_OK)
        {
            g_initRefCount.fetch_sub(1, std::memory_order_acq_rel);
            return result;
        }
    }
    return JALIUM_OK;
}

void jalium_platform_shutdown(void)
{
    if (g_initRefCount.fetch_sub(1, std::memory_order_acq_rel) == 1)
    {
        jalium_platform_shutdown_impl();
    }
}

JaliumPlatform jalium_platform_get_current(void)
{
    return jalium_platform_get_current_impl();
}

void jalium_platform_free(void* ptr)
{
    free(ptr);
}
