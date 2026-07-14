// jalium_video_surface_* C ABI dispatch — pattern mirrors bitmap.cpp.
//
// All entry points are thin trampolines that look up the active rendering
// backend from the JaliumContext, dispatch into the backend's
// CreateVideoSurface / WrapExternalVideoSurface / VideoSurface virtual
// methods, and surface the result back through the opaque
// `JaliumVideoSurface*` handle. Telemetry counters are atomic so the lock
// path and the draw path can hit the same counters without coordination.

#include "jalium_internal.h"
#include "jalium_video_surface.h"
#include "jalium_backend.h"

#include <atomic>
#include <cstring>

namespace {

struct Counters {
    std::atomic<uint64_t> surfacesCreated{0};
    std::atomic<uint64_t> surfacesDestroyed{0};
    std::atomic<uint64_t> cpuUploads{0};
    std::atomic<uint64_t> cpuUploadBytes{0};
    std::atomic<uint64_t> externalImports{0};
    std::atomic<uint64_t> externalImportFails{0};
    std::atomic<int64_t>  gpuResidentBytes{0};
};

Counters g_stats;

} // namespace

extern "C" {

JALIUM_API JaliumVideoSurface* jalium_video_surface_create(
    JaliumContext* ctx,
    uint32_t       width,
    uint32_t       height,
    uint32_t       format_hint)
{
    if (!ctx || width == 0 || height == 0) {
        return nullptr;
    }
    auto backend = jalium::GetBackendFromContext(ctx);
    if (!backend) {
        return nullptr;
    }
    auto* surface = backend->CreateVideoSurface(width, height, format_hint);
    if (surface) {
        g_stats.surfacesCreated.fetch_add(1, std::memory_order_relaxed);
    }
    return reinterpret_cast<JaliumVideoSurface*>(surface);
}

JALIUM_API JaliumVideoSurface* jalium_video_surface_wrap_external(
    JaliumContext*                       ctx,
    const JaliumVideoSurfaceDescriptor*  desc)
{
    if (!ctx || !desc || desc->width == 0 || desc->height == 0) {
        return nullptr;
    }
    auto backend = jalium::GetBackendFromContext(ctx);
    if (!backend) {
        return nullptr;
    }
    auto* surface = backend->WrapExternalVideoSurface(desc);
    if (surface) {
        g_stats.surfacesCreated.fetch_add(1, std::memory_order_relaxed);
        g_stats.externalImports.fetch_add(1, std::memory_order_relaxed);
    } else {
        g_stats.externalImportFails.fetch_add(1, std::memory_order_relaxed);
    }
    return reinterpret_cast<JaliumVideoSurface*>(surface);
}

JALIUM_API int32_t jalium_video_surface_lock(
    JaliumVideoSurface* s,
    uint8_t**           out_ptr,
    uint32_t*           out_stride)
{
    if (out_ptr) *out_ptr = nullptr;
    if (out_stride) *out_stride = 0;
    if (!s || !out_ptr || !out_stride) {
        return 0;
    }
    auto* impl = reinterpret_cast<jalium::VideoSurface*>(s);
    return impl->Lock(out_ptr, out_stride) ? 1 : 0;
}

JALIUM_API int32_t jalium_video_surface_unlock(
    JaliumVideoSurface*                  s,
    const JaliumVideoSurfaceDirtyRect*   dirty_rect_or_null)
{
    if (!s) return 0;
    auto* impl = reinterpret_cast<jalium::VideoSurface*>(s);
    const bool ok = impl->Unlock(dirty_rect_or_null);
    if (ok) {
        // Optimistic count: per-surface upload byte count is what the backend
        // can measure precisely; here we just bump a generic Unlock counter.
        // Stage 2 D3D12 / Software impls will fill in cpuUploadBytes.
        g_stats.cpuUploads.fetch_add(1, std::memory_order_relaxed);
    }
    return ok ? 1 : 0;
}

JALIUM_API uint32_t jalium_video_surface_get_width(JaliumVideoSurface* s)
{
    if (!s) return 0;
    return reinterpret_cast<jalium::VideoSurface*>(s)->GetWidth();
}

JALIUM_API uint32_t jalium_video_surface_get_height(JaliumVideoSurface* s)
{
    if (!s) return 0;
    return reinterpret_cast<jalium::VideoSurface*>(s)->GetHeight();
}

JALIUM_API JaliumVideoSurfaceKind jalium_video_surface_get_kind(JaliumVideoSurface* s)
{
    if (!s) return JALIUM_VS_KIND_BGRA8_CPU;
    return reinterpret_cast<jalium::VideoSurface*>(s)->GetKind();
}

JALIUM_API void jalium_video_surface_destroy(JaliumVideoSurface* s)
{
    if (!s) return;
    g_stats.surfacesDestroyed.fetch_add(1, std::memory_order_relaxed);
    reinterpret_cast<jalium::VideoSurface*>(s)->Release();
}

JALIUM_API void jalium_render_target_draw_video_surface(
    JaliumRenderTarget* rt,
    JaliumVideoSurface* s,
    float x, float y, float w, float h,
    float opacity,
    int32_t scaling_mode)
{
    if (!rt || !s) return;
    auto* target  = reinterpret_cast<jalium::RenderTarget*>(rt);
    auto* surface = reinterpret_cast<jalium::VideoSurface*>(s);
    target->DrawVideoSurface(surface, x, y, w, h, opacity, scaling_mode);
}

JALIUM_API void jalium_query_video_surface_stats(JaliumVideoSurfaceStats* out)
{
    if (!out) return;
    std::memset(out, 0, sizeof(*out));
    out->version             = JALIUM_VIDEO_SURFACE_STATS_VERSION;
    out->surfacesCreated     = g_stats.surfacesCreated.load(std::memory_order_relaxed);
    out->surfacesDestroyed   = g_stats.surfacesDestroyed.load(std::memory_order_relaxed);
    out->cpuUploads          = g_stats.cpuUploads.load(std::memory_order_relaxed);
    out->cpuUploadBytes      = g_stats.cpuUploadBytes.load(std::memory_order_relaxed);
    out->externalImports     = g_stats.externalImports.load(std::memory_order_relaxed);
    out->externalImportFails = g_stats.externalImportFails.load(std::memory_order_relaxed);
    out->gpuResidentBytes    = g_stats.gpuResidentBytes.load(std::memory_order_relaxed);
}

JALIUM_API void jalium_reset_video_surface_stats(void)
{
    g_stats.surfacesCreated.store(0, std::memory_order_relaxed);
    g_stats.surfacesDestroyed.store(0, std::memory_order_relaxed);
    g_stats.cpuUploads.store(0, std::memory_order_relaxed);
    g_stats.cpuUploadBytes.store(0, std::memory_order_relaxed);
    g_stats.externalImports.store(0, std::memory_order_relaxed);
    g_stats.externalImportFails.store(0, std::memory_order_relaxed);
    g_stats.gpuResidentBytes.store(0, std::memory_order_relaxed);
}

} // extern "C"
