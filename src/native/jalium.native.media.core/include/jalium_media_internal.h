#pragma once

#include "jalium_media.h"
#include <stdint.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

/// Computes the tightly-packed BGRA8/RGBA8 stride in bytes for a given width.
static inline uint32_t jalium_media_compute_stride(uint32_t width)
{
    return width * 4u;
}

/// Allocates a 64-byte aligned pixel buffer of size_bytes. Returns NULL on OOM.
JALIUM_MEDIA_API void* jalium_media_aligned_alloc(size_t size_bytes);

/// Frees a buffer obtained from jalium_media_aligned_alloc.
JALIUM_MEDIA_API void jalium_media_aligned_free(void* ptr);

/// Swaps R and B channels in a packed 32bpp BGRA<->RGBA buffer in place.
/// width / height in pixels; stride_bytes the row pitch in bytes.
JALIUM_MEDIA_API void jalium_media_swap_rb_inplace(
    uint8_t* pixels,
    uint32_t width,
    uint32_t height,
    uint32_t stride_bytes);

#ifdef __cplusplus
}
#endif
