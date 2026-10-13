#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>

#include "jalium_rendering_engine.h"  // EngineTransform (used in callers)

namespace jalium {

// Helpers for picking a flatten tolerance and quantizing transform scale into
// a stable cache-key bucket. These are pure header-only functions — every
// backend dll inlines them. No cross-dll dependencies.
//
// Why a bucket and not the raw scale: PathGeometryCache (jalium_path_cache.h)
// keys by source-space path data. If we mixed transform scale into the key
// directly (e.g. as a float), the cache would re-miss on every sub-pixel
// transform change — the gallery's smooth scale animations would never hit.
// Quantizing to log-scale buckets (4 per octave, ~19% step) makes the cache
// hit when the scale change is below the per-octave resolution threshold,
// and miss only when a new octave actually needs a different vertex density.
//
// Tolerance picking: pixel-space flatten already produces per-pixel-correct
// vertex counts (see d3d12_impeller_engine.cpp:1737-1755 commentary). We
// keep the base 0.25-pixel tolerance there and use ComputePixelTolerance
// only when callers want device-pixel-ratio-aware nudging — high-DPI
// surfaces (1.5×/2× DPR) want a tighter tolerance so on-screen pixels stay
// crisp, while low-DPR / smaller surfaces can afford to relax it.

// Largest non-translation scale factor from the 2x2 linear part of a 2x3
// affine. Used to decide:
//  1) effective flatten tolerance ("how big does a 0.25-source-unit error
//     look on screen?")
//  2) PathGeometryCache scaleBucket — see ScaleBucketFromMaxScale below.
inline float MaxScaleFromMatrix(float m11, float m12,
                                float m21, float m22) noexcept {
    // Largest singular value, rather than the largest column norm. The latter
    // underestimates shear and leaves a rotated/skewed curve under-tessellated.
    const double a = double(m11) * m11 + double(m12) * m12;
    const double b = double(m21) * m21 + double(m22) * m22;
    const double c = double(m11) * m21 + double(m12) * m22;
    return static_cast<float>(std::sqrt(0.5 *
        (a + b + std::hypot(a - b, 2.0 * c))));
}

inline float MaxScaleFromTransform(const EngineTransform& t) noexcept {
    return MaxScaleFromMatrix(t.m11, t.m12, t.m21, t.m22);
}

// Pixel-space flatten tolerance. The 0.25-pixel baseline is conservative
// against aliasing without exploding Wang's-formula N. We let high-DPR
// callers tighten it slightly and low-DPR callers relax it, clamped to
// [0.125, 1.0] so ear-clip can't blow up on one end nor edges become
// polygon-faceted on the other.
//
// devicePixelRatio: physical-pixels-per-DIP at the destination surface.
//                   1.0 on standard DPI; 1.5 / 1.75 / 2.0 on hi-DPI.
// qualityHint:      caller-supplied scale (e.g. animation phase, gallery
//                   quality slider). 1.0 = default.
inline float ComputePixelTolerance(float devicePixelRatio,
                                   float qualityHint = 1.0f) noexcept {
    constexpr float kBase     = 0.25f;
    constexpr float kFloor    = 0.125f;
    constexpr float kCeiling  = 1.0f;
    float s = devicePixelRatio * qualityHint;
    if (!(s > 0.5f)) s = 0.5f;  // also catches NaN via !(s>0.5)
    float tol = kBase / s;
    if (tol < kFloor) tol = kFloor;
    if (tol > kCeiling) tol = kCeiling;
    return tol;
}

// Log-scale bucket of a transform's max scale. Buckets are 4 per octave —
// roughly 19% step — so a 1.0×→1.18× scale change stays in the same bucket
// while 1.0×→1.20× crosses. This is below human-perceptible vertex-density
// change for icon-scale paths but coarse enough that cache hit rate stays
// useful under smooth animation.
//
// Convention: bucket 0 corresponds to maxScale ≈ 1.0×. Negative buckets are
// small scales, positive are large. The cast to uint32_t reinterprets — we
// only compare for equality, never order, so the bit pattern uniqueness is
// what matters.
inline uint32_t ScaleBucketFromMaxScale(float maxScale) noexcept {
    if (!(maxScale > 0.0f)) return 0u;  // also catches NaN
    int32_t b = static_cast<int32_t>(std::lround(std::log2(maxScale) * 4.0f));
    // Reinterpret the signed bucket as unsigned for hashing — equal bucket
    // → equal uint32_t, which is what HashPathInput needs.
    return static_cast<uint32_t>(b);
}

// ---------------------------------------------------------------------------
// Anti-aliasing route selection.
//
// A filled path can get its soft edges two ways, and they are not equal:
//
//   • Analytic coverage (jalium_scanline_rasterizer.h RasterizePathToRects).
//     Exact horizontal span area × 16 vertical sub-scanlines, emitted as
//     per-pixel-alpha rectangles. WPF/Skia-grade edges, and the rect list is
//     cacheable, so a static or merely scrolling UI pays for it once. Cost
//     scales with the path's DEVICE-SPACE AREA, which is why it cannot be
//     unconditional.
//
//   • An approximation: MSAA stencil-then-cover, or a triangle mesh plus a
//     boundary feather ring. Constant cost per path regardless of size, but
//     the edge quality is capped (a handful of coverage levels for MSAA;
//     for the feather ring, only the OUTSIDE of the edge softens at all,
//     because the interior mesh under it still rasterizes with binary
//     pixel-centre coverage).
//
// Icons, glyphs and control ornaments — very nearly every Path/Shape in a real
// UI — must take the analytic route or their edges visibly stair-step. Only
// genuinely large vector artwork, where per-frame rasterization would show up
// in the frame budget, keeps an approximation.
//
// This predicate is the single place that decision is made; every backend and
// every fill entry point routes through it so the same shape never renders at
// two different qualities depending on which entry point it happened to reach.
// ---------------------------------------------------------------------------
constexpr float kAnalyticFillMaxAreaPx = 512.0f * 512.0f;

inline bool PreferAnalyticFill(float devW, float devH) noexcept {
    // Degenerate extents are cheap to rasterize and are exactly what the
    // approximations handle worst (a sub-pixel sliver has no interior at all),
    // so route them analytically too. The !(x > 0) form also catches NaN.
    if (!(devW > 0.0f) || !(devH > 0.0f)) return true;
    return (devW * devH) <= kAnalyticFillMaxAreaPx;
}

// Device-space extent of a local-space AABB under a 2x3 affine.
inline void TransformedExtent(float minX, float minY, float maxX, float maxY,
                              float m11, float m12, float m21, float m22,
                              float dx, float dy,
                              float& outW, float& outH) noexcept {
    const float xs[4] = { minX, maxX, maxX, minX };
    const float ys[4] = { minY, minY, maxY, maxY };
    float loX = xs[0] * m11 + ys[0] * m21 + dx;
    float loY = xs[0] * m12 + ys[0] * m22 + dy;
    float hiX = loX, hiY = loY;
    for (int i = 1; i < 4; ++i) {
        const float x = m11 * xs[i] + m21 * ys[i] + dx;
        const float y = m12 * xs[i] + m22 * ys[i] + dy;
        if (x < loX) loX = x;
        if (y < loY) loY = y;
        if (x > hiX) hiX = x;
        if (y > hiY) hiY = y;
    }
    outW = hiX - loX;
    outH = hiY - loY;
}

inline void TransformedExtent(float minX, float minY, float maxX, float maxY,
                              const EngineTransform& t,
                              float& outW, float& outH) noexcept {
    TransformedExtent(minX, minY, maxX, maxY,
                      t.m11, t.m12, t.m21, t.m22, t.dx, t.dy, outW, outH);
}

// Local-space AABB over every coordinate pair in a path command buffer.
// Bezier control points bound their curve, so this over-estimates slightly —
// which is what a routing gate wants: it can only push work toward the cheaper
// route, never the reverse. Returns false only when there are no commands.
//
// Tags match jalium_triangulate.h: 0 LineTo, 1 CubicTo, 2 MoveTo, 3 QuadTo,
// 5 ClosePath. An unknown tag stops the walk and keeps what was measured.
inline bool PathCommandExtent(float startX, float startY,
                              const float* commands, uint32_t commandLength,
                              float& minX, float& minY,
                              float& maxX, float& maxY) noexcept {
    if (!commands || commandLength == 0) return false;
    minX = maxX = startX;
    minY = maxY = startY;
    for (uint32_t i = 0; i < commandLength; ) {
        int pairs;
        switch (static_cast<int>(commands[i])) {
            case 0: pairs = 1; break;   // LineTo   [tag, x, y]
            case 1: pairs = 3; break;   // CubicTo  [tag, c1, c2, end]
            case 2: pairs = 1; break;   // MoveTo   [tag, x, y]
            case 3: pairs = 2; break;   // QuadTo   [tag, c, end]
            case 5: pairs = 0; break;   // ClosePath[tag]
            default: return true;
        }
        if (i + 1u + static_cast<uint32_t>(pairs) * 2u > commandLength) return true;
        for (int p = 0; p < pairs; ++p) {
            const float x = commands[i + 1 + p * 2];
            const float y = commands[i + 2 + p * 2];
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
        i += 1u + static_cast<uint32_t>(pairs) * 2u;
    }
    return true;
}

}  // namespace jalium
