#define JALIUM_MEDIA_EXPORTS
#include "and_yuv_simd.h"

// SSE2 kernels for x86_64 / i386 (Android emulator). Vectorised implementation
// arrives in Commit 5. For Commit 1 we forward to the scalar path.

#if defined(__x86_64__) || defined(__i386__)

namespace jalium::media::android {

void NV12ToBgra_SSE2(const uint8_t* y, uint32_t ys, const uint8_t* uv, uint32_t uvs,
                     uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                     ColorMatrix matrix, jalium_pixel_format_t fmt)
{
    NV12ToBgra_Scalar(y, ys, uv, uvs, dst, dsts, w, h, matrix, fmt);
}

void NV21ToBgra_SSE2(const uint8_t* y, uint32_t ys, const uint8_t* vu, uint32_t vus,
                     uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                     ColorMatrix matrix, jalium_pixel_format_t fmt)
{
    NV21ToBgra_Scalar(y, ys, vu, vus, dst, dsts, w, h, matrix, fmt);
}

void I420ToBgra_SSE2(const uint8_t* y, uint32_t ys, const uint8_t* u, uint32_t us,
                     const uint8_t* v, uint32_t vs,
                     uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                     ColorMatrix matrix, jalium_pixel_format_t fmt)
{
    I420ToBgra_Scalar(y, ys, u, us, v, vs, dst, dsts, w, h, matrix, fmt);
}

} // namespace jalium::media::android

#endif // __x86_64__ || __i386__
