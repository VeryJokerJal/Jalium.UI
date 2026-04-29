#pragma once

#include "jalium_media.h"
#include <stdint.h>

namespace jalium::media::android {

/// ITU-R color matrices used by Android MediaCodec output.
enum class ColorMatrix : int {
    Bt601 = 0,  ///< SDTV — most legacy content
    Bt709 = 1,  ///< HD — most modern H.264/HEVC content
};

/// NV12 (Y plane + interleaved UV plane) -> BGRA8 / RGBA8.
/// Strides are in bytes. The output buffer must be at least
/// dst_stride * height bytes.
void NV12ToBgra(
    const uint8_t* y_plane,  uint32_t y_stride,
    const uint8_t* uv_plane, uint32_t uv_stride,
    uint8_t*       dst,      uint32_t dst_stride,
    uint32_t width, uint32_t height,
    ColorMatrix matrix,
    jalium_pixel_format_t output_format);

/// NV21 (Y plane + interleaved VU plane) -> BGRA8 / RGBA8.
void NV21ToBgra(
    const uint8_t* y_plane,  uint32_t y_stride,
    const uint8_t* vu_plane, uint32_t vu_stride,
    uint8_t*       dst,      uint32_t dst_stride,
    uint32_t width, uint32_t height,
    ColorMatrix matrix,
    jalium_pixel_format_t output_format);

/// I420 (planar Y, U, V) -> BGRA8 / RGBA8.
void I420ToBgra(
    const uint8_t* y_plane, uint32_t y_stride,
    const uint8_t* u_plane, uint32_t u_stride,
    const uint8_t* v_plane, uint32_t v_stride,
    uint8_t*       dst,     uint32_t dst_stride,
    uint32_t width, uint32_t height,
    ColorMatrix matrix,
    jalium_pixel_format_t output_format);

// ----- Per-architecture kernels -----------------------------------------------
// One of these is selected at compile time based on ANDROID_ABI.

#if defined(__aarch64__)
void NV12ToBgra_NEON(const uint8_t* y, uint32_t ys, const uint8_t* uv, uint32_t uvs,
                     uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                     ColorMatrix matrix, jalium_pixel_format_t fmt);
void NV21ToBgra_NEON(const uint8_t* y, uint32_t ys, const uint8_t* vu, uint32_t vus,
                     uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                     ColorMatrix matrix, jalium_pixel_format_t fmt);
void I420ToBgra_NEON(const uint8_t* y, uint32_t ys, const uint8_t* u, uint32_t us,
                     const uint8_t* v, uint32_t vs,
                     uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                     ColorMatrix matrix, jalium_pixel_format_t fmt);
#elif defined(__x86_64__) || defined(__i386__)
void NV12ToBgra_SSE2(const uint8_t* y, uint32_t ys, const uint8_t* uv, uint32_t uvs,
                     uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                     ColorMatrix matrix, jalium_pixel_format_t fmt);
void NV21ToBgra_SSE2(const uint8_t* y, uint32_t ys, const uint8_t* vu, uint32_t vus,
                     uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                     ColorMatrix matrix, jalium_pixel_format_t fmt);
void I420ToBgra_SSE2(const uint8_t* y, uint32_t ys, const uint8_t* u, uint32_t us,
                     const uint8_t* v, uint32_t vs,
                     uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                     ColorMatrix matrix, jalium_pixel_format_t fmt);
#endif

// Always-available scalar fallback. Used for trailing edge pixels and ABIs
// without a SIMD kernel.
void NV12ToBgra_Scalar(const uint8_t* y, uint32_t ys, const uint8_t* uv, uint32_t uvs,
                       uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                       ColorMatrix matrix, jalium_pixel_format_t fmt);
void NV21ToBgra_Scalar(const uint8_t* y, uint32_t ys, const uint8_t* vu, uint32_t vus,
                       uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                       ColorMatrix matrix, jalium_pixel_format_t fmt);
void I420ToBgra_Scalar(const uint8_t* y, uint32_t ys, const uint8_t* u, uint32_t us,
                       const uint8_t* v, uint32_t vs,
                       uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                       ColorMatrix matrix, jalium_pixel_format_t fmt);

} // namespace jalium::media::android
