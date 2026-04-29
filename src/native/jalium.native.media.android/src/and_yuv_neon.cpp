#define JALIUM_MEDIA_EXPORTS
#include "and_yuv_simd.h"

// arm64-v8a NEON kernels for NV12 / NV21 / I420 -> BGRA8 / RGBA8.
// Process 8 destination pixels per iteration; trailing edge handled by the scalar fallback.
// Both BT.601 and BT.709 are vectorised via C++ templates (zero overhead — branch-free, fully constexpr).

#if defined(__aarch64__)

#include <arm_neon.h>
#include <cstring>

namespace jalium::media::android {

namespace {

// Fixed-point Q10 coefficients (multiplied by 1024).
template <ColorMatrix M>
struct YuvCoeffs {
    static constexpr int Y_OFFSET  = 16;
    static constexpr int UV_OFFSET = 128;
    static constexpr int Y_MULT    = 1192;       // 1.164 * 1024
    static constexpr int R_V       = (M == ColorMatrix::Bt709) ? 1836 : 1634;
    static constexpr int G_U       = (M == ColorMatrix::Bt709) ? -218 : -401;
    static constexpr int G_V       = (M == ColorMatrix::Bt709) ? -546 : -833;
    static constexpr int B_U       = (M == ColorMatrix::Bt709) ? 2166 : 2065;
};

// Computes 8 BGRA pixels from 8 Y + 8 (duplicated) U + 8 (duplicated) V values.
// Writes 32 bytes to `dst`, with alpha = 0xFF.
template <ColorMatrix M>
inline void Yuv8ToBgra(uint8x8_t y8, uint8x8_t u8, uint8x8_t v8,
                       uint8_t* dst, jalium_pixel_format_t fmt)
{
    using C = YuvCoeffs<M>;

    // Convert to centered signed 16-bit (Y - 16, U/V - 128).
    int16x8_t y_off = vreinterpretq_s16_u16(vsubl_u8(y8, vdup_n_u8(C::Y_OFFSET)));
    int16x8_t u_off = vreinterpretq_s16_u16(vsubl_u8(u8, vdup_n_u8(C::UV_OFFSET)));
    int16x8_t v_off = vreinterpretq_s16_u16(vsubl_u8(v8, vdup_n_u8(C::UV_OFFSET)));

    // Widen to int32 for the multiply-accumulate stage.
    int32x4_t y_lo = vmovl_s16(vget_low_s16(y_off));
    int32x4_t y_hi = vmovl_s16(vget_high_s16(y_off));
    int32x4_t u_lo = vmovl_s16(vget_low_s16(u_off));
    int32x4_t u_hi = vmovl_s16(vget_high_s16(u_off));
    int32x4_t v_lo = vmovl_s16(vget_low_s16(v_off));
    int32x4_t v_hi = vmovl_s16(vget_high_s16(v_off));

    int32x4_t yY_lo = vmulq_n_s32(y_lo, C::Y_MULT);
    int32x4_t yY_hi = vmulq_n_s32(y_hi, C::Y_MULT);

    // R = (Y*Y_MULT + V*R_V + 512) >> 10
    int32x4_t r_lo = vshrq_n_s32(vaddq_s32(vmlaq_n_s32(yY_lo, v_lo, C::R_V), vdupq_n_s32(512)), 10);
    int32x4_t r_hi = vshrq_n_s32(vaddq_s32(vmlaq_n_s32(yY_hi, v_hi, C::R_V), vdupq_n_s32(512)), 10);

    // G = (Y*Y_MULT + U*G_U + V*G_V + 512) >> 10
    int32x4_t g_lo = vmlaq_n_s32(yY_lo, u_lo, C::G_U);
    int32x4_t g_hi = vmlaq_n_s32(yY_hi, u_hi, C::G_U);
    g_lo = vmlaq_n_s32(g_lo, v_lo, C::G_V);
    g_hi = vmlaq_n_s32(g_hi, v_hi, C::G_V);
    g_lo = vshrq_n_s32(vaddq_s32(g_lo, vdupq_n_s32(512)), 10);
    g_hi = vshrq_n_s32(vaddq_s32(g_hi, vdupq_n_s32(512)), 10);

    // B = (Y*Y_MULT + U*B_U + 512) >> 10
    int32x4_t b_lo = vshrq_n_s32(vaddq_s32(vmlaq_n_s32(yY_lo, u_lo, C::B_U), vdupq_n_s32(512)), 10);
    int32x4_t b_hi = vshrq_n_s32(vaddq_s32(vmlaq_n_s32(yY_hi, u_hi, C::B_U), vdupq_n_s32(512)), 10);

    // Narrow to int16 then saturate-to-unsigned-8.
    int16x8_t r_s16 = vcombine_s16(vmovn_s32(r_lo), vmovn_s32(r_hi));
    int16x8_t g_s16 = vcombine_s16(vmovn_s32(g_lo), vmovn_s32(g_hi));
    int16x8_t b_s16 = vcombine_s16(vmovn_s32(b_lo), vmovn_s32(b_hi));

    uint8x8_t r_u8 = vqmovun_s16(r_s16);
    uint8x8_t g_u8 = vqmovun_s16(g_s16);
    uint8x8_t b_u8 = vqmovun_s16(b_s16);
    uint8x8_t a_u8 = vdup_n_u8(0xFF);

    uint8x8x4_t out;
    if (fmt == JALIUM_PF_BGRA8) {
        out.val[0] = b_u8;
        out.val[1] = g_u8;
        out.val[2] = r_u8;
    } else {
        out.val[0] = r_u8;
        out.val[1] = g_u8;
        out.val[2] = b_u8;
    }
    out.val[3] = a_u8;
    vst4_u8(dst, out);
}

// Lookup tables for "duplicate every other byte" — used to expand 4 UV pairs
// (8 bytes packed U V U V U V U V) into 8 U or 8 V bytes (each repeated for
// the pair of adjacent pixels that share it).
constexpr uint8_t kIdxU_NV12[8] = { 0, 0, 2, 2, 4, 4, 6, 6 };
constexpr uint8_t kIdxV_NV12[8] = { 1, 1, 3, 3, 5, 5, 7, 7 };
// NV21 is V then U; swap.
constexpr uint8_t kIdxU_NV21[8] = { 1, 1, 3, 3, 5, 5, 7, 7 };
constexpr uint8_t kIdxV_NV21[8] = { 0, 0, 2, 2, 4, 4, 6, 6 };

// Process one Y row producing one BGRA row. Returns the number of pixels
// covered by the SIMD path; remaining pixels (< 8 trailing) are handled by the caller.
template <ColorMatrix M>
inline uint32_t NV12_Row_NEON(
    const uint8_t* y_row, const uint8_t* uv_row,
    uint8_t* dst_row, uint32_t width, jalium_pixel_format_t fmt,
    bool is_nv21)
{
    const uint8x8_t idx_u = vld1_u8(is_nv21 ? kIdxU_NV21 : kIdxU_NV12);
    const uint8x8_t idx_v = vld1_u8(is_nv21 ? kIdxV_NV21 : kIdxV_NV12);

    uint32_t x = 0;
    while (x + 8 <= width) {
        uint8x8_t y8  = vld1_u8(y_row + x);
        uint8x8_t uv8 = vld1_u8(uv_row + x);  // 4 UV pairs = 8 bytes, covers 8 Y pixels (2 per UV pair)
        uint8x8_t u8  = vtbl1_u8(uv8, idx_u);
        uint8x8_t v8  = vtbl1_u8(uv8, idx_v);

        Yuv8ToBgra<M>(y8, u8, v8, dst_row + static_cast<size_t>(x) * 4u, fmt);
        x += 8;
    }
    return x;
}

template <ColorMatrix M>
inline uint32_t I420_Row_NEON(
    const uint8_t* y_row, const uint8_t* u_row, const uint8_t* v_row,
    uint8_t* dst_row, uint32_t width, jalium_pixel_format_t fmt)
{
    // U/V planes have width/2 samples per row. We need each U/V sample replicated
    // for two adjacent Y pixels. Load 4 chroma samples → expand to 8 via vzip.
    const uint8x8_t zero = vdup_n_u8(0);

    uint32_t x = 0;
    while (x + 8 <= width) {
        uint8x8_t y8 = vld1_u8(y_row + x);

        // Load 4 U + 4 V samples without overreading the half-width chroma rows.
        // (vld1_u8 reads 8 bytes; we only have 4 valid here, so memcpy via uint32_t.)
        uint32_t u_packed = 0, v_packed = 0;
        std::memcpy(&u_packed, u_row + x / 2, 4);
        std::memcpy(&v_packed, v_row + x / 2, 4);
        uint8x8_t u4 = vreinterpret_u8_u32(vdup_n_u32(u_packed));
        uint8x8_t v4 = vreinterpret_u8_u32(vdup_n_u32(v_packed));

        // Replicate each chroma sample for two adjacent pixels.
        // vzip(low,low).val[0] = { u0,u0,u1,u1,u2,u2,u3,u3 } when low 4 lanes are u0..u3.
        uint8x8x2_t u_zip = vzip_u8(u4, u4);
        uint8x8x2_t v_zip = vzip_u8(v4, v4);
        uint8x8_t u8 = u_zip.val[0];
        uint8x8_t v8 = v_zip.val[0];
        (void)zero;

        Yuv8ToBgra<M>(y8, u8, v8, dst_row + static_cast<size_t>(x) * 4u, fmt);
        x += 8;
    }
    return x;
}

// Trailing edge: copy a sub-region row by row using the scalar dispatch,
// shifted by `start_x` pixels.
inline void NV12_Tail_Scalar(
    const uint8_t* y_row, uint32_t y_stride_unused,
    const uint8_t* uv_row, uint32_t uv_stride_unused,
    uint8_t* dst_row, uint32_t dst_stride_unused,
    uint32_t start_x, uint32_t width,
    ColorMatrix matrix, jalium_pixel_format_t fmt,
    bool is_nv21)
{
    if (start_x >= width) return;
    (void)y_stride_unused; (void)uv_stride_unused; (void)dst_stride_unused;

    if (is_nv21) {
        NV21ToBgra_Scalar(
            y_row + start_x,            /*y_stride*/ 0,
            uv_row + (start_x & ~1u),   /*uv_stride*/ 0,
            dst_row + static_cast<size_t>(start_x) * 4u, /*dst_stride*/ 0,
            width - start_x, /*height*/ 1, matrix, fmt);
    } else {
        NV12ToBgra_Scalar(
            y_row + start_x,            /*y_stride*/ 0,
            uv_row + (start_x & ~1u),   /*uv_stride*/ 0,
            dst_row + static_cast<size_t>(start_x) * 4u, /*dst_stride*/ 0,
            width - start_x, /*height*/ 1, matrix, fmt);
    }
}

inline void I420_Tail_Scalar(
    const uint8_t* y_row, const uint8_t* u_row, const uint8_t* v_row,
    uint8_t* dst_row,
    uint32_t start_x, uint32_t width,
    ColorMatrix matrix, jalium_pixel_format_t fmt)
{
    if (start_x >= width) return;
    I420ToBgra_Scalar(
        y_row + start_x,        /*y_stride*/ 0,
        u_row + start_x / 2,    /*u_stride*/ 0,
        v_row + start_x / 2,    /*v_stride*/ 0,
        dst_row + static_cast<size_t>(start_x) * 4u, /*dst_stride*/ 0,
        width - start_x, /*height*/ 1, matrix, fmt);
}

template <ColorMatrix M>
void NV12ToBgra_NEON_Impl(
    const uint8_t* y, uint32_t ys, const uint8_t* uv, uint32_t uvs,
    uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
    jalium_pixel_format_t fmt, bool is_nv21)
{
    for (uint32_t row = 0; row < h; ++row) {
        const uint8_t* y_row  = y + static_cast<size_t>(row)     * ys;
        const uint8_t* uv_row = uv + static_cast<size_t>(row / 2) * uvs;
        uint8_t*       d_row  = dst + static_cast<size_t>(row)    * dsts;

        uint32_t covered = NV12_Row_NEON<M>(y_row, uv_row, d_row, w, fmt, is_nv21);
        if (covered < w) {
            NV12_Tail_Scalar(y_row, 0, uv_row, 0, d_row, 0,
                             covered, w, M, fmt, is_nv21);
        }
    }
}

template <ColorMatrix M>
void I420ToBgra_NEON_Impl(
    const uint8_t* y, uint32_t ys, const uint8_t* u, uint32_t us,
    const uint8_t* v, uint32_t vs,
    uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
    jalium_pixel_format_t fmt)
{
    for (uint32_t row = 0; row < h; ++row) {
        const uint8_t* y_row = y + static_cast<size_t>(row)     * ys;
        const uint8_t* u_row = u + static_cast<size_t>(row / 2) * us;
        const uint8_t* v_row = v + static_cast<size_t>(row / 2) * vs;
        uint8_t*       d_row = dst + static_cast<size_t>(row)    * dsts;

        uint32_t covered = I420_Row_NEON<M>(y_row, u_row, v_row, d_row, w, fmt);
        if (covered < w) {
            I420_Tail_Scalar(y_row, u_row, v_row, d_row, covered, w, M, fmt);
        }
    }
}

} // anonymous

void NV12ToBgra_NEON(const uint8_t* y, uint32_t ys, const uint8_t* uv, uint32_t uvs,
                     uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                     ColorMatrix matrix, jalium_pixel_format_t fmt)
{
    if (matrix == ColorMatrix::Bt709) {
        NV12ToBgra_NEON_Impl<ColorMatrix::Bt709>(y, ys, uv, uvs, dst, dsts, w, h, fmt, /*is_nv21*/ false);
    } else {
        NV12ToBgra_NEON_Impl<ColorMatrix::Bt601>(y, ys, uv, uvs, dst, dsts, w, h, fmt, /*is_nv21*/ false);
    }
}

void NV21ToBgra_NEON(const uint8_t* y, uint32_t ys, const uint8_t* vu, uint32_t vus,
                     uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                     ColorMatrix matrix, jalium_pixel_format_t fmt)
{
    if (matrix == ColorMatrix::Bt709) {
        NV12ToBgra_NEON_Impl<ColorMatrix::Bt709>(y, ys, vu, vus, dst, dsts, w, h, fmt, /*is_nv21*/ true);
    } else {
        NV12ToBgra_NEON_Impl<ColorMatrix::Bt601>(y, ys, vu, vus, dst, dsts, w, h, fmt, /*is_nv21*/ true);
    }
}

void I420ToBgra_NEON(const uint8_t* y, uint32_t ys, const uint8_t* u, uint32_t us,
                     const uint8_t* v, uint32_t vs,
                     uint8_t* dst, uint32_t dsts, uint32_t w, uint32_t h,
                     ColorMatrix matrix, jalium_pixel_format_t fmt)
{
    if (matrix == ColorMatrix::Bt709) {
        I420ToBgra_NEON_Impl<ColorMatrix::Bt709>(y, ys, u, us, v, vs, dst, dsts, w, h, fmt);
    } else {
        I420ToBgra_NEON_Impl<ColorMatrix::Bt601>(y, ys, u, us, v, vs, dst, dsts, w, h, fmt);
    }
}

} // namespace jalium::media::android

#endif // __aarch64__
