// jalium_video_surface.h - GPU-friendly surface ABI for video frame streaming.
//
// Sits beside jalium_bitmap_* but targets a different use case: a single
// surface that is updated at video framerate (30-120 fps) and may wrap an
// external GPU resource (Windows D3D11-shared texture from MF DXVA, Android
// AHardwareBuffer, Apple IOSurface / CVPixelBuffer, etc.) for zero-copy
// hardware decode.
//
// Design notes:
//   - Same per-backend dispatch model as the bitmap path (see bitmap.cpp).
//   - BGRA8 CPU path is always available; external import is best-effort and
//     reports failure via wrap_external returning nullptr so the caller can
//     fall back to the CPU path.
//   - Lock returns a writable staging-buffer pointer; Unlock signals the
//     backend to copy into the device-local texture. Software backend's
//     Unlock is a no-op because the staging buffer IS the texture.
//   - Stages: ABI header + core dispatch + backend stubs land first; D3D12 /
//     Vulkan / Software real implementations land in stage 2; external
//     handle import (DXVA / AHardwareBuffer / IOSurface) lands in stage 3+.

#pragma once

#include "jalium_api.h"
#include "jalium_types.h"
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct JaliumVideoSurface JaliumVideoSurface;

/// Integer rectangle used for dirty-region hints on Unlock. Defined locally
/// instead of polluting jalium_types.h (which currently only exposes a float
/// JaliumRect for drawcalls).
typedef struct {
    int32_t x;
    int32_t y;
    int32_t width;
    int32_t height;
} JaliumVideoSurfaceDirtyRect;

/// What kind of underlying GPU resource (or CPU buffer) the surface owns.
typedef enum {
    /// CPU-side BGRA8 buffer that the backend stages to GPU on Unlock.
    /// This is the default and the only kind every backend supports.
    JALIUM_VS_KIND_BGRA8_CPU       = 0,

    /// Windows: ID3D11Texture2D shared via NT handle (CreateSharedHandle).
    /// handle0 = NT HANDLE returned by IDXGIResource1::CreateSharedHandle.
    /// Implemented in stage 3.
    JALIUM_VS_KIND_D3D11_SHARED    = 1,

    /// Cross-platform: VkImage imported via external memory extension
    /// (VK_KHR_external_memory_win32 / dma-buf / AHardwareBuffer).
    /// handle0 = (VkImage), handle1 = (VkDeviceMemory). Stage 5.
    JALIUM_VS_KIND_VK_IMAGE        = 2,

    /// Android AMediaCodec output surface backed by AHardwareBuffer.
    /// handle0 = (AHardwareBuffer*). Stage 4.
    JALIUM_VS_KIND_AHARDWAREBUFFER = 3,

    /// Apple IOSurface (CoreVideo IOSurface from VTDecompressionSession).
    /// handle0 = IOSurfaceID. Reserved for future Apple media module.
    JALIUM_VS_KIND_IOSURFACE       = 4,

    /// Direct Metal texture handle. Reserved for future Apple media module.
    JALIUM_VS_KIND_METAL_TEXTURE   = 5,

    /// CoreVideo CVPixelBufferRef wrapping IOSurface. Reserved for Apple.
    JALIUM_VS_KIND_CVPIXELBUFFER   = 6,
} JaliumVideoSurfaceKind;

/// Format hints for create / wrap. v1 supports only BGRA8; NV12 / P010 etc.
/// are reserved for direct-from-decoder paths (stage 3+).
typedef enum {
    JALIUM_VS_FORMAT_BGRA8         = 0,
    JALIUM_VS_FORMAT_NV12          = 1,
    JALIUM_VS_FORMAT_P010          = 2,
    JALIUM_VS_FORMAT_RGB10A2       = 3,
} JaliumVideoSurfaceFormat;

/// Descriptor for wrap_external. Caller-allocates and fills in.
typedef struct {
    JaliumVideoSurfaceKind kind;
    uint32_t               width;
    uint32_t               height;
    uint64_t               handle0;       ///< Primary OS handle (see kind doc).
    uint64_t               handle1;       ///< Secondary handle (VkDeviceMemory, NT-owner PID, ...).
    uint32_t               format_hint;   ///< JaliumVideoSurfaceFormat. 0 = BGRA8.
    uint32_t               reserved;
} JaliumVideoSurfaceDescriptor;

/// Creates a CPU-owned BGRA8 surface that the backend stages to GPU on Unlock.
/// `format_hint` is a JaliumVideoSurfaceFormat (0 = BGRA8). Returns nullptr if
/// the backend does not implement video surfaces yet (caller falls back).
JALIUM_API JaliumVideoSurface* jalium_video_surface_create(
    JaliumContext* ctx,
    uint32_t       width,
    uint32_t       height,
    uint32_t       format_hint);

/// Imports an external GPU resource (D3D11 shared texture, AHardwareBuffer,
/// IOSurface, ...) as a surface the render target can sample. Returns nullptr
/// if `desc->kind` is not supported by this backend / platform — caller should
/// fall back to `jalium_video_surface_create` + CPU upload.
JALIUM_API JaliumVideoSurface* jalium_video_surface_wrap_external(
    JaliumContext*                       ctx,
    const JaliumVideoSurfaceDescriptor*  desc);

/// Maps the surface's staging buffer for CPU write. Only valid on BGRA8_CPU
/// surfaces; external-import surfaces return failure.
///
/// On success: out_ptr is a writable BGRA8 buffer, out_stride is its row pitch
/// (>= width * 4, may be larger for alignment). Caller writes pixels then
/// calls jalium_video_surface_unlock.
///
/// Returns 1 on success, 0 on failure (e.g. external surface, backend stub).
JALIUM_API int32_t jalium_video_surface_lock(
    JaliumVideoSurface* s,
    uint8_t**           out_ptr,
    uint32_t*           out_stride);

/// Signals the backend that lock-staging is complete. The backend issues a
/// staging-to-texture copy (D3D12 / Vulkan) or just bumps a content revision
/// (Software). `dirty_rect_or_null` is a hint — pass NULL to invalidate the
/// whole surface.
///
/// Returns 1 on success.
JALIUM_API int32_t jalium_video_surface_unlock(
    JaliumVideoSurface*                  s,
    const JaliumVideoSurfaceDirtyRect*   dirty_rect_or_null);

JALIUM_API uint32_t              jalium_video_surface_get_width (JaliumVideoSurface* s);
JALIUM_API uint32_t              jalium_video_surface_get_height(JaliumVideoSurface* s);
JALIUM_API JaliumVideoSurfaceKind jalium_video_surface_get_kind  (JaliumVideoSurface* s);

JALIUM_API void                  jalium_video_surface_destroy(JaliumVideoSurface* s);

/// Draws a video surface into a render target. Coordinates / opacity / scaling
/// mode mirror jalium_draw_bitmap. scaling_mode is JaliumBitmapScalingMode.
JALIUM_API void jalium_render_target_draw_video_surface(
    JaliumRenderTarget* rt,
    JaliumVideoSurface* s,
    float x, float y, float w, float h,
    float opacity,
    int32_t scaling_mode);

// ============================================================================
// Telemetry (mirrors jalium_query_bitmap_stats; kept separate so video
// streaming load does not pollute the still-image bitmap dashboards).
// ============================================================================

typedef struct {
    uint64_t version;
    uint64_t surfacesCreated;
    uint64_t surfacesDestroyed;
    uint64_t cpuUploads;           ///< Lock/Unlock pairs that staged BGRA8.
    uint64_t cpuUploadBytes;
    uint64_t externalImports;      ///< wrap_external successes.
    uint64_t externalImportFails;  ///< wrap_external returned nullptr.
    int64_t  gpuResidentBytes;
    uint64_t reserved[16];
} JaliumVideoSurfaceStats;

#define JALIUM_VIDEO_SURFACE_STATS_VERSION 1u

JALIUM_API void jalium_query_video_surface_stats(JaliumVideoSurfaceStats* out);
JALIUM_API void jalium_reset_video_surface_stats(void);

#ifdef __cplusplus
} // extern "C"
#endif
