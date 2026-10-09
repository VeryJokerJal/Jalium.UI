#include "jalium_platform.h"

#include <atomic>
#include <cstdlib>
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

#if !defined(__APPLE__)
int32_t jalium_platform_prefers_overlay_scrollbars(void)
{
    return 0;
}
int32_t jalium_platform_prefers_reduced_motion(void)
{
    return 0;
}
int32_t jalium_platform_text_word_boundary(const JaliumUtf16Char*, uint32_t, int32_t, int32_t)
{
    return -1;
}
int32_t jalium_platform_text_word_selection_boundary(const JaliumUtf16Char*, uint32_t, int32_t, int32_t, int32_t)
{
    return -1;
}
JaliumResult jalium_platform_text_word_range(
    const JaliumUtf16Char*, uint32_t, int32_t, int32_t* start, int32_t* length)
{
    if (start) *start = 0;
    if (length) *length = 0;
    return JALIUM_ERROR_NOT_SUPPORTED;
}
#endif

void jalium_platform_free(void* ptr)
{
    free(ptr);
}
