#define JALIUM_MEDIA_EXPORTS
#include "and_yuv_simd.h"

#include <algorithm>
#include <cstdint>

namespace jalium::media::android {

namespace {

// ITU-R BT.601 limited-range coefficients (most legacy SDR content).
struct CoeffsBt601 {
    static constexpr int Y_OFFSET  = 16;
    static constexpr int UV_OFFSET = 128;
    // Fixed-point, scaled by 1<<10
    static constexpr int Y_MULT  = 1192;  // 1.164 * 1024
    static constexpr int R_V     = 1634;  // 1.596 * 1024
    static constexpr int G_U     = -401;  // -0.391 * 1024
    static constexpr int G_V     = -833;  // -0.813 * 1024
    static constexpr int B_U     = 2065;  // 2.018 * 1024
};

// ITU-R BT.709 limited-range coefficients (most modern HD H.264/HEVC content).
struct CoeffsBt709 {
    static constexpr int Y_OFFSET  = 16;
    static constexpr int UV_OFFSET = 128;
    static constexpr int Y_MULT  = 1192;
    static constexpr int R_V     = 1836;  // 1.793 * 1024
    static constexpr int G_U     = -218;  // -0.213 * 1024
    static constexpr int G_V     = -546;  // -0.533 * 1024
    static constexpr int B_U     = 2166;  // 2.115 * 1024
};

inline uint8_t Clamp8(int v)
{
    return static_cast<uint8_t>(std::clamp(v, 0, 255));
}

template <typename C>
inline void YuvToBgraPixel(int y, int u, int v, uint8_t* out, jalium_pixel_format_t fmt)
{
    int Y = (y - C::Y_OFFSET) * C::Y_MULT;
    int U = u - C::UV_OFFSET;
    int V = v - C::UV_OFFSET;

    int r = (Y + C::R_V * V + 512) >> 10;
    int g = (Y + C::G_U * U + C::G_V * V + 512) >> 10;
    int b = (Y + C::B_U * U + 512) >> 10;

    if (fmt == JALIUM_PF_BGRA8) {
        out[0] = Clamp8(b);
        out[1] = Clamp8(g);
        out[2] = Clamp8(r);
    } else { // JALIUM_PF_RGBA8
        out[0] = Clamp8(r);
        out[1] = Clamp8(g);
        out[2] = Clamp8(b);
    }
    out[3] = 0xFF;
}

template <typename C>
void NV12ToBgraImpl_BothMatrices(
    const uint8_t* y_plane, uint32_t y_stride,
    const uint8_t* uv_plane, uint32_t uv_stride,
    uint8_t* dst, uint32_t dst_stride,
    uint32_t width, uint32_t height,
    jalium_pixel_format_t fmt)
{
    for (uint32_t row = 0; row < height; ++row) {
        const uint8_t* yRow  = y_plane + static_cast<size_t>(row) * y_stride;
        const uint8_t* uvRow = uv_plane + static_cast<size_t>(row / 2) * uv_stride;
        uint8_t*       dRow  = dst + static_cast<size_t>(row) * dst_stride;
        for (uint32_t col = 0; col < width; ++col) {
            int u = uvRow[(col / 2) * 2 + 0];
            int v = uvRow[(col / 2) * 2 + 1];
            YuvToBgraPixel<C>(yRow[col], u, v, dRow + static_cast<size_t>(col) * 4, fmt);
        }
    }
}

template <typename C>
void NV21ToBgraImpl_BothMatrices(
    const uint8_t* y_plane, uint32_t y_stride,
    const uint8_t* vu_plane, uint32_t vu_stride,
    uint8_t* dst, uint32_t dst_stride,
    uint32_t width, uint32_t height,
    jalium_pixel_format_t fmt)
{
    for (uint32_t row = 0; row < height; ++row) {
        const uint8_t* yRow  = y_plane + static_cast<size_t>(row) * y_stride;
        const uint8_t* vuRow = vu_plane + static_cast<size_t>(row / 2) * vu_stride;
        uint8_t*       dRow  = dst + static_cast<size_t>(row) * dst_stride;
        for (uint32_t col = 0; col < width; ++col) {
            int v = vuRow[(col / 2) * 2 + 0];
            int u = vuRow[(col / 2) * 2 + 1];
            YuvToBgraPixel<C>(yRow[col], u, v, dRow + static_cast<size_t>(col) * 4, fmt);
        }
    }
}

template <typename C>
void I420ToBgraImpl_BothMatrices(
    const uint8_t* y_plane, uint32_t y_stride,
    const uint8_t* u_plane, uint32_t u_stride,
    const uint8_t* v_plane, uint32_t v_stride,
    uint8_t* dst, uint32_t dst_stride,
    uint32_t width, uint32_t height,
    jalium_pixel_format_t fmt)
{
    for (uint32_t row = 0; row < height; ++row) {
        const uint8_t* yRow = y_plane + static_cast<size_t>(row) * y_stride;
        const uint8_t* uRow = u_plane + static_cast<size_t>(row / 2) * u_stride;
        const uint8_t* vRow = v_plane + static_cast<size_t>(row / 2) * v_stride;
        uint8_t*       dRow = dst + static_cast<size_t>(row) * dst_stride;
        for (uint32_t col = 0; col < width; ++col) {
            int u = uRow[col / 2];
            int v = vRow[col / 2];
            YuvToBgraPixel<C>(yRow[col], u, v, dRow + static_cast<size_t>(col) * 4, fmt);
        }
    }
}

} // namespace

void NV12ToBgra_Scalar(const uint8_t* y, uint32_t ys, const uint8_t* uv, uint32_t uvs,
                       uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                       ColorMatrix matrix, jalium_pixel_format_t fmt)
{
    if (matrix == ColorMatrix::Bt709) {
        NV12ToBgraImpl_BothMatrices<CoeffsBt709>(y, ys, uv, uvs, dst, dsts, w, h, fmt);
    } else {
        NV12ToBgraImpl_BothMatrices<CoeffsBt601>(y, ys, uv, uvs, dst, dsts, w, h, fmt);
    }
}

void NV21ToBgra_Scalar(const uint8_t* y, uint32_t ys, const uint8_t* vu, uint32_t vus,
                       uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                       ColorMatrix matrix, jalium_pixel_format_t fmt)
{
    if (matrix == ColorMatrix::Bt709) {
        NV21ToBgraImpl_BothMatrices<CoeffsBt709>(y, ys, vu, vus, dst, dsts, w, h, fmt);
    } else {
        NV21ToBgraImpl_BothMatrices<CoeffsBt601>(y, ys, vu, vus, dst, dsts, w, h, fmt);
    }
}

void I420ToBgra_Scalar(const uint8_t* y, uint32_t ys, const uint8_t* u, uint32_t us,
                       const uint8_t* v, uint32_t vs,
                       uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                       ColorMatrix matrix, jalium_pixel_format_t fmt)
{
    if (matrix == ColorMatrix::Bt709) {
        I420ToBgraImpl_BothMatrices<CoeffsBt709>(y, ys, u, us, v, vs, dst, dsts, w, h, fmt);
    } else {
        I420ToBgraImpl_BothMatrices<CoeffsBt601>(y, ys, u, us, v, vs, dst, dsts, w, h, fmt);
    }
}

// ----- Public dispatch (selects SIMD or scalar at compile time) ----------

void NV12ToBgra(
    const uint8_t* y_plane,  uint32_t y_stride,
    const uint8_t* uv_plane, uint32_t uv_stride,
    uint8_t*       dst,      uint32_t dst_stride,
    uint32_t width, uint32_t height,
    ColorMatrix matrix,
    jalium_pixel_format_t output_format)
{
#if defined(__aarch64__)
    NV12ToBgra_NEON(y_plane, y_stride, uv_plane, uv_stride,
                    dst, dst_stride, width, height, matrix, output_format);
#elif defined(__x86_64__) || defined(__i386__)
    NV12ToBgra_SSE2(y_plane, y_stride, uv_plane, uv_stride,
                    dst, dst_stride, width, height, matrix, output_format);
#else
    NV12ToBgra_Scalar(y_plane, y_stride, uv_plane, uv_stride,
                      dst, dst_stride, width, height, matrix, output_format);
#endif
}

void NV21ToBgra(
    const uint8_t* y_plane,  uint32_t y_stride,
    const uint8_t* vu_plane, uint32_t vu_stride,
    uint8_t*       dst,      uint32_t dst_stride,
    uint32_t width, uint32_t height,
    ColorMatrix matrix,
    jalium_pixel_format_t output_format)
{
#if defined(__aarch64__)
    NV21ToBgra_NEON(y_plane, y_stride, vu_plane, vu_stride,
                    dst, dst_stride, width, height, matrix, output_format);
#elif defined(__x86_64__) || defined(__i386__)
    NV21ToBgra_SSE2(y_plane, y_stride, vu_plane, vu_stride,
                    dst, dst_stride, width, height, matrix, output_format);
#else
    NV21ToBgra_Scalar(y_plane, y_stride, vu_plane, vu_stride,
                      dst, dst_stride, width, height, matrix, output_format);
#endif
}

void I420ToBgra(
    const uint8_t* y_plane, uint32_t y_stride,
    const uint8_t* u_plane, uint32_t u_stride,
    const uint8_t* v_plane, uint32_t v_stride,
    uint8_t*       dst,     uint32_t dst_stride,
    uint32_t width, uint32_t height,
    ColorMatrix matrix,
    jalium_pixel_format_t output_format)
{
#if defined(__aarch64__)
    I420ToBgra_NEON(y_plane, y_stride, u_plane, u_stride, v_plane, v_stride,
                    dst, dst_stride, width, height, matrix, output_format);
#elif defined(__x86_64__) || defined(__i386__)
    I420ToBgra_SSE2(y_plane, y_stride, u_plane, u_stride, v_plane, v_stride,
                    dst, dst_stride, width, height, matrix, output_format);
#else
    I420ToBgra_Scalar(y_plane, y_stride, u_plane, u_stride, v_plane, v_stride,
                      dst, dst_stride, width, height, matrix, output_format);
#endif
}

} // namespace jalium::media::android
