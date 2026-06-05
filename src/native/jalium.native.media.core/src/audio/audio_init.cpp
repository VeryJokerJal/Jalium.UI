// Process-wide init / shutdown for the jalium audio subsystem.
//
// jalium_audio_initialize is callable repeatedly: each successful return
// increments an atomic refcount, each jalium_audio_shutdown decrements it.
// Today there is no heavyweight global to lazy-init (miniaudio devices and
// dr_wav decoders carry their own state), so the body is just bookkeeping.
// The refcount exists so future global resources (a process-wide mixer,
// codec factory tables, etc.) have a single place to lifecycle-tie to.

#include "jalium_audio.h"
#include "audio_internal.h"

#include <atomic>

namespace {
std::atomic<int> g_refcount{0};
} // namespace

namespace jalium::audio {
bool IsInitialized() noexcept
{
    return g_refcount.load(std::memory_order_acquire) > 0;
}
} // namespace jalium::audio

extern "C" {

JALIUM_MEDIA_API jalium_media_status_t jalium_audio_initialize(void)
{
    g_refcount.fetch_add(1, std::memory_order_acq_rel);
    return JALIUM_MEDIA_OK;
}

JALIUM_MEDIA_API void jalium_audio_shutdown(void)
{
    // Floor at zero so a stray shutdown does not flip the refcount negative
    // and silently re-arm IsInitialized() on the next init.
    int prev = g_refcount.load(std::memory_order_acquire);
    while (prev > 0) {
        if (g_refcount.compare_exchange_weak(
                prev, prev - 1,
                std::memory_order_acq_rel,
                std::memory_order_acquire)) {
            break;
        }
    }
}

} // extern "C"
