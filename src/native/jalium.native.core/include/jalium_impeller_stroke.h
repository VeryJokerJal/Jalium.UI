#pragma once

// jalium_impeller_stroke.h
//
// Backend-agnostic CPU stroke expansion shared by every Impeller-style engine
// (D3D12, Vulkan, …). Lives in jalium.native.core so a fix to a corner case
// in caps/joins/miter clipping lands once across all backends.
//
// Algorithm summary (Flutter Impeller-equivalent):
//   1. For every input segment, compute a unit normal.
//   2. Emit one quad per segment (4 verts, 6 indices), CCW winding.
//   3. Between adjacent segments, emit a join (round / bevel / miter, with
//      miter-limit fallback to bevel).
//   4. At the path's two endpoints (when not closed), emit a cap
//      (butt = nothing, square = extruded rectangle, round = hemicircle fan).
//   5. For closed contours, emit a join at the start vertex too — without it
//      the corner at the path's start point shows a wedge-shaped gap (the
//      "title-bar maximize icon notch" bug).
//   6. Sub-pixel strokes (<0.5px halfWidth) are clamped to 0.5px and their
//      alpha is faded by the lost coverage so they don't pop in/out as the
//      transform scales — UNLESS the caller is going through the analytic
//      AA rasterizer (collectContours != nullptr), where fractional coverage
//      handles hairlines naturally and the alpha hack would double-fade.
//
// Vertex contract: TVertex must be aggregate-constructible from
// { float x, float y, float r, float g, float b, float a }.

#include "jalium_rendering_engine.h"
#include "jalium_triangulate.h"   // for jalium::Contour
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <vector>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

namespace jalium {

// ---------------------------------------------------------------------------
// Cap / join enums — matches the convention used by both ImpellerD3D12Engine
// and ImpellerVulkanEngine. Integer values are part of the public Encode*
// API contract (passed in as int32_t lineCap / lineJoin).
// ---------------------------------------------------------------------------

enum class ImpellerCap  : int32_t { Butt = 0, Square = 1, Round = 2 };
enum class ImpellerJoin : int32_t { Miter = 0, Bevel = 1, Round = 2 };

namespace stroke_detail {

// Arc fans are built once as a flat {hub, a0, a1, …, aN} point list, then
// either triangulated into a vertex/index mesh (GPU binary raster) or pushed
// whole as one convex contour (analytic scanline raster). Keeping the angle
// math in one place is what stops the two output modes from drifting apart.

inline void BuildRoundCapArc(
    std::vector<float>& outXY,
    float cx, float cy, float nx, float ny,
    float halfWidth, bool isStart)
{
    constexpr uint32_t kSegments = 8;
    constexpr float kPi = (float)M_PI;

    float angle0 = std::atan2(ny, nx);
    // The segment normal already points to one edge of the stroke. Sweep
    // through the outward half of the cap: opposite the segment at the start,
    // along the segment at the end.
    float startAngle = angle0;
    float sweep = isStart ? kPi : -kPi;

    outXY.clear();
    outXY.push_back(cx);
    outXY.push_back(cy);
    for (uint32_t i = 0; i <= kSegments; ++i) {
        float t = (float)i / (float)kSegments;
        float angle = startAngle + sweep * t;
        outXY.push_back(cx + halfWidth * std::cos(angle));
        outXY.push_back(cy + halfWidth * std::sin(angle));
    }
}

// Returns false when the corner is degenerate (normals collinear) and no
// join geometry should be emitted at all.
inline bool BuildRoundJoinArc(
    std::vector<float>& outXY,
    float cx, float cy,
    float n0x, float n0y, float n1x, float n1y,
    float halfWidth)
{
    float cr = n0x * n1y - n0y * n1x;
    if (std::abs(cr) < 1e-5f) return false; // nearly collinear normals

    float sign = (cr > 0.0f) ? -1.0f : 1.0f;
    float a0x = n0x * sign, a0y = n0y * sign;
    float a1x = n1x * sign, a1y = n1y * sign;

    float angle0 = std::atan2(a0y, a0x);
    float angle1 = std::atan2(a1y, a1x);
    float diff = angle1 - angle0;
    while (diff >  (float)M_PI) diff -= 2.0f * (float)M_PI;
    while (diff < -(float)M_PI) diff += 2.0f * (float)M_PI;

    uint32_t segments = std::max(2u,
        (uint32_t)std::ceil(std::abs(diff) / (float)M_PI * 8.0f));

    outXY.clear();
    outXY.push_back(cx);
    outXY.push_back(cy);
    for (uint32_t i = 0; i <= segments; ++i) {
        float t = (float)i / (float)segments;
        float angle = angle0 + diff * t;
        outXY.push_back(cx + halfWidth * std::cos(angle));
        outXY.push_back(cy + halfWidth * std::sin(angle));
    }
    return true;
}

// Fan {hub, a0 … aN} -> triangle list.
template <typename TVertex>
inline void FanToMesh(
    std::vector<TVertex>& verts, std::vector<uint32_t>& indices,
    const std::vector<float>& xy, float r, float g, float b, float a)
{
    const uint32_t n = (uint32_t)(xy.size() / 2);
    if (n < 3) return;
    uint32_t base = (uint32_t)verts.size();
    for (uint32_t i = 0; i < n; ++i) verts.push_back({ xy[i * 2], xy[i * 2 + 1], r, g, b, a });
    for (uint32_t i = 0; i + 2 < n; ++i) {
        indices.push_back(base);
        indices.push_back(base + 1 + i);
        indices.push_back(base + 2 + i);
    }
}

} // namespace stroke_detail

// ---------------------------------------------------------------------------
// GenerateRoundCap — hemicircle fan centred on the cap point. isStart flips
// the sweep direction so the cap sits on the correct side of the line.
// ---------------------------------------------------------------------------

template <typename TVertex>
inline void GenerateRoundCap(
    std::vector<TVertex>& verts,
    std::vector<uint32_t>& indices,
    float cx, float cy,
    float nx, float ny,
    float halfWidth,
    float r, float g, float b, float a,
    bool isStart)
{
    std::vector<float> xy;
    stroke_detail::BuildRoundCapArc(xy, cx, cy, nx, ny, halfWidth, isStart);
    stroke_detail::FanToMesh<TVertex>(verts, indices, xy, r, g, b, a);
}

// ---------------------------------------------------------------------------
// GenerateRoundJoin — fills only the OUTER side of a corner. The inner side
// is already covered by the natural overlap of adjacent segment quads, so
// drawing a full circle (both sides) produces a visible bead at every
// polyline vertex (the "string of pearls" artifact on dense-flattened
// curves).
// ---------------------------------------------------------------------------

template <typename TVertex>
inline void GenerateRoundJoin(
    std::vector<TVertex>& verts,
    std::vector<uint32_t>& indices,
    float cx, float cy,
    float n0x, float n0y,
    float n1x, float n1y,
    float halfWidth,
    float r, float g, float b, float a)
{
    std::vector<float> xy;
    if (!stroke_detail::BuildRoundJoinArc(xy, cx, cy, n0x, n0y, n1x, n1y, halfWidth)) return;
    stroke_detail::FanToMesh<TVertex>(verts, indices, xy, r, g, b, a);
}

// ---------------------------------------------------------------------------
// ExpandStrokePath — the main entry point.
//
// flatPoints is the bezier-flattened polyline (x0,y0,x1,y1,...) in the
// coordinate space the caller wants the geometry emitted in. brushR/G/B/A is
// the un-premultiplied brush color; the function premultiplies internally
// (and applies hairline alpha fade if appropriate).
//
// featherScaleInSrc is the source-space distance that maps to ONE screen
// pixel — used to size the 1-px AA feather skirt and the hairline-clamp
// threshold. Pixel-space callers pass 1.0 (the default). Source-space cache
// callers (e.g. D3D12 EncodeStrokePath) pass 1/maxScale so the feather and
// hairline threshold stay at exactly 1 screen pixel after the cached mesh
// is transformed at emit time. Without this, a stroke cached at maxScale=2
// would emit a 2-px-wide feather skirt → strokes look ~2× too thick.
//
// When collectContours is non-null the function does NOT touch outVerts /
// outIndices and instead emits ONE convex contour per stroke primitive —
// a quad per segment, a fan per round join/cap, a tri/quad per miter or
// bevel join — each winding-normalized to CCW so a NonZero fill of the set
// is exactly the stroke's union. Used by callers that feed the geometry to
// the analytic-AA scanline rasterizer.
//
// This used to emit one 3-vertex contour PER TRIANGLE, which handed the
// rasterizer ~3× the edges it needed (every quad carried its shared diagonal
// twice) and one heap allocation per triangle — ~400 of them for a 24 px
// lucide glyph. Emitting whole primitives is the same covered region with
// far less to rasterize.
// ---------------------------------------------------------------------------

template <typename TVertex>
inline bool ExpandStrokePath(
    std::vector<TVertex>& outVerts,
    std::vector<uint32_t>& outIndices,
    const float* flatPoints, uint32_t pointCount,
    float strokeWidth,
    ImpellerJoin join, float miterLimit,
    ImpellerCap cap, bool closed,
    float brushR, float brushG, float brushB, float brushA,
    std::vector<Contour>* collectContours = nullptr,
    float featherScaleInSrc = 1.0f)
{
    if (pointCount < 2 || flatPoints == nullptr) return false;

    // Consecutive coincident vertices have no tangent. Leaving them in the
    // segment list creates zero normals and changes every join style at the
    // next real corner. Keep the all-coincident case intact so an explicit
    // zero-length round-capped stroke can still paint a dot.
    std::vector<float> compactPoints;
    for (uint32_t i = 1; i < pointCount; ++i) {
        const float dx = flatPoints[i * 2] - flatPoints[(i - 1) * 2];
        const float dy = flatPoints[i * 2 + 1] - flatPoints[(i - 1) * 2 + 1];
        if (dx * dx + dy * dy > 1e-12f) continue;
        compactPoints.reserve(static_cast<size_t>(pointCount) * 2u);
        compactPoints.insert(compactPoints.end(), flatPoints, flatPoints + 2);
        for (uint32_t j = 1; j < pointCount; ++j) {
            const float nextX = flatPoints[j * 2], nextY = flatPoints[j * 2 + 1];
            const float prevX = compactPoints[compactPoints.size() - 2];
            const float prevY = compactPoints[compactPoints.size() - 1];
            const float sx = nextX - prevX, sy = nextY - prevY;
            if (sx * sx + sy * sy > 1e-12f) {
                compactPoints.push_back(nextX);
                compactPoints.push_back(nextY);
            }
        }
        if (compactPoints.size() >= 4) {
            flatPoints = compactPoints.data();
            pointCount = static_cast<uint32_t>(compactPoints.size() / 2);
        }
        break;
    }

    // featherScaleInSrc must be > 0; clamp NaN / negatives to 1 (pixel-space).
    if (!(featherScaleInSrc > 0.0f)) featherScaleInSrc = 1.0f;

    // 0.5 screen-pixel half-width, expressed in the caller's coordinate space.
    // All the geometry below uses this for both the AA feather skirt and the
    // hairline clamp so the on-screen feel is 1-pixel regardless of whether
    // the caller is pre-transformed or going through a source-space cache.
    const float halfPxInSrc = 0.5f * featherScaleInSrc;

    float halfWidth = strokeWidth * 0.5f;
    float r = brushR * brushA;
    float g = brushG * brushA;
    float b = brushB * brushA;
    float a = brushA;

    const bool collect = (collectContours != nullptr);

    // Sub-pixel hairline alpha fade — only when going through the direct
    // (binary GPU) rasterization path. The analytic AA path takes the true
    // halfWidth so per-pixel coverage handles hairlines naturally.
    if (!collect && halfWidth < halfPxInSrc && halfWidth > 0.0f) {
        float fade = halfWidth / halfPxInSrc;
        r *= fade; g *= fade; b *= fade; a *= fade;
        halfWidth = halfPxInSrc;
    }

    std::vector<TVertex>& verts = outVerts;
    std::vector<uint32_t>& indices = outIndices;

    // Scratch for arc fans and for assembling one primitive's points before
    // it is handed to pushContourCCW.
    std::vector<float> arcXY;
    float quadXY[8];

    // Push one convex primitive as a CCW contour. The rasterizer unions the
    // set under NonZero, so every primitive must wind the same way; the
    // shoelace sign tells us whether to reverse.
    auto pushContourCCW = [&](const float* xy, uint32_t n) {
        if (n < 3) return;
        double sa = 0.0;
        for (uint32_t i = 0; i < n; ++i) {
            uint32_t j = (i + 1 < n) ? (i + 1) : 0u;
            sa += (double)xy[i * 2] * (double)xy[j * 2 + 1]
                - (double)xy[j * 2] * (double)xy[i * 2 + 1];
        }
        if (std::abs(sa) < 1e-7) return;   // degenerate — contributes no area
        // Built in place: constructing a temporary Contour and moving it in
        // costs an extra vector move per primitive, and there are hundreds
        // of primitives per frame.
        collectContours->emplace_back();
        std::vector<float>& pts = collectContours->back().points;
        pts.resize((size_t)n * 2);
        float* dst = pts.data();
        if (sa > 0.0) {
            std::memcpy(dst, xy, (size_t)n * 2 * sizeof(float));
        } else {
            for (uint32_t i = 0; i < n; ++i) {
                dst[i * 2]     = xy[(n - 1 - i) * 2];
                dst[i * 2 + 1] = xy[(n - 1 - i) * 2 + 1];
            }
        }
    };

    auto getX = [&](uint32_t i) { return flatPoints[i * 2]; };
    auto getY = [&](uint32_t i) { return flatPoints[i * 2 + 1]; };

    // A closed contour can arrive here two ways: already geometrically closed
    // (last point == first point, which is what the flattener produces from an
    // explicit ClosePath command) or as an open polyline plus closed=true, in
    // which case the wrap-around edge from the last point back to the first is
    // IMPLIED and must be stroked like any other segment. Emitting only the
    // closing join (what this function used to do) drew the corner but not the
    // edge, so every closed outline whose command stream lacked ClosePath came
    // out with one whole side missing — e.g. the warehouse icon rendering with
    // no right wall, the file icon with no bottom edge.
    const bool wrapSegment = closed && pointCount >= 3 &&
        (std::abs(getX(pointCount - 1) - getX(0)) > 1e-6f ||
         std::abs(getY(pointCount - 1) - getY(0)) > 1e-6f);
    // Segment i runs from point i to point nextIndex(i); the wrap segment is
    // the extra one at index pointCount-1 (last → first).
    const uint32_t segCount = wrapSegment ? pointCount : (pointCount - 1);
    auto nextIndex = [&](uint32_t i) -> uint32_t {
        const uint32_t n = i + 1;
        return (n < pointCount) ? n : 0u;
    };

    // One contour per segment plus one per join/cap; reserving up front keeps
    // the outer vector from reallocating (and move-constructing every Contour
    // it already holds) a dozen times per stroke.
    if (collect) collectContours->reserve(collectContours->size() + (size_t)segCount * 2 + 4);

    struct Segment { float nx, ny; };
    std::vector<Segment> segNormals;
    segNormals.reserve(segCount);
    for (uint32_t i = 0; i < segCount; ++i) {
        const uint32_t j = nextIndex(i);
        float dx = getX(j) - getX(i);
        float dy = getY(j) - getY(i);
        float len = std::sqrt(dx * dx + dy * dy);
        if (len < 1e-6f) { segNormals.push_back({0, 0}); continue; }
        segNormals.push_back({ -dy / len, dx / len });
    }

    // ---- Per-segment quads (+ optional outer-skirt vertex feather AA) ----
    //
    // Three emit modes:
    //
    // collect (analytic scanline raster):
    //   one 4-point contour, the exact stroke quad. Per-pixel coverage comes
    //   from the rasterizer, so emitting a feather skirt here would just be
    //   union'd in by NonZero filling and fatten the stroke by 1 px.
    //
    // !collect (D3D12 / Vulkan binary mesh → GPU triangle raster):
    //   8 verts / 6 triangles per segment with an outer feather skirt at
    //   alpha=0. GPU's barycentric interpolation of vertex alpha gives a
    //   1-px coverage gradient on each long edge — same visual quality as
    //   analytic per-rect coverage but emits ~2× verts (vs ~30× for full
    //   RasterizePathToRects). Skia / Flutter Impeller use this extended-
    //   quad pattern for stroke AA when MSAA isn't available.
    //
    // Symmetric ±0.5-px feather centred on the geometric stroke edge:
    //   inner alpha=a half-width = halfWidth - 0.5px (clamped ≥ 0)
    //   outer alpha=0 half-width = halfWidth + 0.5px
    // Net effect: total geometric width = strokeWidth + 1 px; the GPU's
    // bilinear interpolation across that 1-px feather gives 50% coverage
    // at the geometric edge, so the on-screen visual width matches the
    // requested strokeWidth.
    //
    // When halfWidth ≤ 0.5px (after the hairline clamp above the stroke
    // is exactly 0.5px wide), innerHalf collapses to 0 and the inner
    // verts on both sides degenerate to the centreline. The two feather
    // strips meet at that centreline and still produce a 1-px-wide
    // soft line — correct hairline behaviour.
    for (uint32_t i = 0; i < segCount; ++i) {
        const uint32_t j = nextIndex(i);
        float nx = segNormals[i].nx * halfWidth;
        float ny = segNormals[i].ny * halfWidth;
        float x0 = getX(i), y0 = getY(i);
        float x1 = getX(j), y1 = getY(j);

        if (collect) {
            quadXY[0] = x0 + nx; quadXY[1] = y0 + ny;
            quadXY[2] = x0 - nx; quadXY[3] = y0 - ny;
            quadXY[4] = x1 - nx; quadXY[5] = y1 - ny;
            quadXY[6] = x1 + nx; quadXY[7] = y1 + ny;
            pushContourCCW(quadXY, 4);
            continue;
        }

        float innerHalf = halfWidth - halfPxInSrc;
        if (innerHalf < 0.0f) innerHalf = 0.0f;
        float outerHalf = halfWidth + halfPxInSrc;
        float inx = segNormals[i].nx * innerHalf;
        float iny = segNormals[i].ny * innerHalf;
        float onx = segNormals[i].nx * outerHalf;
        float ony = segNormals[i].ny * outerHalf;

        uint32_t base = (uint32_t)verts.size();
        // 0,1: outer top feather (alpha 0)
        verts.push_back({ x0 + onx, y0 + ony, r, g, b, 0.0f });
        verts.push_back({ x1 + onx, y1 + ony, r, g, b, 0.0f });
        // 2,3: inner top edge (alpha a)
        verts.push_back({ x0 + inx, y0 + iny, r, g, b, a });
        verts.push_back({ x1 + inx, y1 + iny, r, g, b, a });
        // 4,5: inner bottom edge (alpha a)
        verts.push_back({ x0 - inx, y0 - iny, r, g, b, a });
        verts.push_back({ x1 - inx, y1 - iny, r, g, b, a });
        // 6,7: outer bottom feather (alpha 0)
        verts.push_back({ x0 - onx, y0 - ony, r, g, b, 0.0f });
        verts.push_back({ x1 - onx, y1 - ony, r, g, b, 0.0f });

        // Inner solid quad: (2,4,3) and (4,5,3)
        indices.push_back(base + 2); indices.push_back(base + 4); indices.push_back(base + 3);
        indices.push_back(base + 4); indices.push_back(base + 5); indices.push_back(base + 3);
        // Top feather strip: (0,2,1) and (2,3,1)
        indices.push_back(base + 0); indices.push_back(base + 2); indices.push_back(base + 1);
        indices.push_back(base + 2); indices.push_back(base + 3); indices.push_back(base + 1);
        // Bottom feather strip: (4,6,5) and (6,7,5)
        indices.push_back(base + 4); indices.push_back(base + 6); indices.push_back(base + 5);
        indices.push_back(base + 6); indices.push_back(base + 7); indices.push_back(base + 5);
    }

    // ---- Joins between adjacent segments ----
    auto emitJoin = [&](float n0x, float n0y, float n1x, float n1y, float cx, float cy) {
        if (join == ImpellerJoin::Round) {
            if (!stroke_detail::BuildRoundJoinArc(arcXY, cx, cy, n0x, n0y, n1x, n1y, halfWidth)) return;
            if (collect) pushContourCCW(arcXY.data(), (uint32_t)(arcXY.size() / 2));
            else         stroke_detail::FanToMesh<TVertex>(verts, indices, arcXY, r, g, b, a);
            return;
        }
        if (join == ImpellerJoin::Bevel) {
            // Two wedges, one per side of the corner.
            float t0[6] = { cx, cy, cx + n0x * halfWidth, cy + n0y * halfWidth,
                                    cx + n1x * halfWidth, cy + n1y * halfWidth };
            float t1[6] = { cx, cy, cx - n0x * halfWidth, cy - n0y * halfWidth,
                                    cx - n1x * halfWidth, cy - n1y * halfWidth };
            if (collect) { pushContourCCW(t0, 3); pushContourCCW(t1, 3); return; }
            uint32_t base = (uint32_t)verts.size();
            verts.push_back({ t0[0], t0[1], r, g, b, a });
            verts.push_back({ t0[2], t0[3], r, g, b, a });
            verts.push_back({ t0[4], t0[5], r, g, b, a });
            indices.push_back(base); indices.push_back(base + 1); indices.push_back(base + 2);
            base = (uint32_t)verts.size();
            verts.push_back({ t1[0], t1[1], r, g, b, a });
            verts.push_back({ t1[2], t1[3], r, g, b, a });
            verts.push_back({ t1[4], t1[5], r, g, b, a });
            indices.push_back(base); indices.push_back(base + 1); indices.push_back(base + 2);
            return;
        }
        // Miter (with bevel fallback when the miter exceeds miterLimit).
        float dot = n0x * n1x + n0y * n1y;
        float alignment = (dot + 1.0f) * 0.5f;
        if (alignment > 0.999f) return; // nearly straight, no join needed

        float cr = n0x * n1y - n0y * n1x;
        float dir = cr > 0 ? -1.0f : 1.0f;

        float p0x = cx + n0x * halfWidth * dir, p0y = cy + n0y * halfWidth * dir;
        float p1x = cx + n1x * halfWidth * dir, p1y = cy + n1y * halfWidth * dir;

        // Miter extension (only when within limit).
        bool haveMiter = false;
        float mtx = 0.0f, mty = 0.0f;
        if (alignment > 1e-6f) {
            float mx = (n0x + n1x) * 0.5f * halfWidth / alignment;
            float my = (n0y + n1y) * 0.5f * halfWidth / alignment;
            const float maxReach = halfWidth * miterLimit;
            if (mx * mx + my * my <= maxReach * maxReach) {
                haveMiter = true;
                mtx = cx + mx * dir; mty = cy + my * dir;
            }
        }

        if (collect) {
            if (haveMiter) {
                float wedge[8] = { cx, cy, p0x, p0y, mtx, mty, p1x, p1y };
                pushContourCCW(wedge, 4);
            } else {
                float bevel[6] = { cx, cy, p0x, p0y, p1x, p1y };
                pushContourCCW(bevel, 3);
            }
            return;
        }

        uint32_t base = (uint32_t)verts.size();
        verts.push_back({ cx, cy, r, g, b, a });
        verts.push_back({ p0x, p0y, r, g, b, a });
        verts.push_back({ p1x, p1y, r, g, b, a });
        if (haveMiter) {
            verts.push_back({ mtx, mty, r, g, b, a });
            indices.push_back(base); indices.push_back(base + 1); indices.push_back(base + 3);
            indices.push_back(base); indices.push_back(base + 3); indices.push_back(base + 2);
        } else {
            indices.push_back(base); indices.push_back(base + 1); indices.push_back(base + 2);
        }
    };

    // Vertex i is the junction of segment i-1 (arriving) and segment i
    // (leaving). With a wrap segment present this also covers the last input
    // vertex, whose leaving edge is the wrap.
    for (uint32_t i = 1; i < segCount; ++i) {
        emitJoin(segNormals[i - 1].nx, segNormals[i - 1].ny,
                 segNormals[i].nx, segNormals[i].ny,
                 getX(i), getY(i));
    }

    // Closing-vertex join — needed for closed contours so the start point
    // doesn't show a wedge-shaped gap.
    if (closed && pointCount >= 3 && segNormals.size() >= 2) {
        uint32_t lastSeg = (uint32_t)segNormals.size() - 1;
        emitJoin(segNormals[lastSeg].nx, segNormals[lastSeg].ny,
                 segNormals[0].nx, segNormals[0].ny,
                 getX(0), getY(0));
    }

    // ---- Caps (open contours only) ----
    if (!closed && pointCount >= 2) {
        // Square caps: the two stroke-edge points plus the same pair displaced
        // by the extrusion vector. The start and end caps build that vector
        // differently in the mesh code below, so it is passed in rather than
        // derived here — deriving it cost a sign flip that pushed the start
        // cap back along the line instead of past its end.
        auto emitSquareCapQuad = [&](float cx, float cy, float nx, float ny,
                                     float ex, float ey) {
            quadXY[0] = cx + nx * halfWidth + ex; quadXY[1] = cy + ny * halfWidth + ey;
            quadXY[2] = cx - nx * halfWidth + ex; quadXY[3] = cy - ny * halfWidth + ey;
            quadXY[4] = cx - nx * halfWidth;      quadXY[5] = cy - ny * halfWidth;
            quadXY[6] = cx + nx * halfWidth;      quadXY[7] = cy + ny * halfWidth;
            pushContourCCW(quadXY, 4);
        };

        // Start cap.
        float nx = segNormals[0].nx, ny = segNormals[0].ny;
        float cx = getX(0), cy = getY(0);
        if (cap == ImpellerCap::Round) {
            stroke_detail::BuildRoundCapArc(arcXY, cx, cy, nx, ny, halfWidth, true);
            if (collect) pushContourCCW(arcXY.data(), (uint32_t)(arcXY.size() / 2));
            else         stroke_detail::FanToMesh<TVertex>(verts, indices, arcXY, r, g, b, a);
        } else if (cap == ImpellerCap::Square) {
            float dx = -segNormals[0].ny, dy = segNormals[0].nx;
            if (collect) {
                emitSquareCapQuad(cx, cy, nx, ny, -dx * halfWidth, -dy * halfWidth);
            } else {
                uint32_t base = (uint32_t)verts.size();
                verts.push_back({ cx + nx * halfWidth - dx * halfWidth, cy + ny * halfWidth - dy * halfWidth, r, g, b, a });
                verts.push_back({ cx - nx * halfWidth - dx * halfWidth, cy - ny * halfWidth - dy * halfWidth, r, g, b, a });
                verts.push_back({ cx + nx * halfWidth, cy + ny * halfWidth, r, g, b, a });
                verts.push_back({ cx - nx * halfWidth, cy - ny * halfWidth, r, g, b, a });
                indices.push_back(base);     indices.push_back(base + 1); indices.push_back(base + 2);
                indices.push_back(base + 1); indices.push_back(base + 3); indices.push_back(base + 2);
            }
        }
        // End cap.
        uint32_t lastSeg = (uint32_t)segNormals.size() - 1;
        nx = segNormals[lastSeg].nx; ny = segNormals[lastSeg].ny;
        cx = getX(pointCount - 1); cy = getY(pointCount - 1);
        if (cap == ImpellerCap::Round) {
            stroke_detail::BuildRoundCapArc(arcXY, cx, cy, nx, ny, halfWidth, false);
            if (collect) pushContourCCW(arcXY.data(), (uint32_t)(arcXY.size() / 2));
            else         stroke_detail::FanToMesh<TVertex>(verts, indices, arcXY, r, g, b, a);
        } else if (cap == ImpellerCap::Square) {
            float dx = segNormals[lastSeg].ny, dy = -segNormals[lastSeg].nx;
            if (collect) {
                emitSquareCapQuad(cx, cy, nx, ny, dx * halfWidth, dy * halfWidth);
            } else {
                uint32_t base = (uint32_t)verts.size();
                verts.push_back({ cx + nx * halfWidth, cy + ny * halfWidth, r, g, b, a });
                verts.push_back({ cx - nx * halfWidth, cy - ny * halfWidth, r, g, b, a });
                verts.push_back({ cx + nx * halfWidth + dx * halfWidth, cy + ny * halfWidth + dy * halfWidth, r, g, b, a });
                verts.push_back({ cx - nx * halfWidth + dx * halfWidth, cy - ny * halfWidth + dy * halfWidth, r, g, b, a });
                indices.push_back(base);     indices.push_back(base + 1); indices.push_back(base + 2);
                indices.push_back(base + 1); indices.push_back(base + 3); indices.push_back(base + 2);
            }
        }
    }

    return true;
}

// ---------------------------------------------------------------------------
// WalkDashPattern — arc-length traversal of a polyline emitting on-segments.
//
// Iterates the dash pattern (alternating on/off lengths) starting at
// dashOffset (already normalized into the pattern), and invokes onSubContour
// for every "on" sub-contour. The sub-contour is a fresh polyline — caller
// can feed it back into ExpandStrokePath with cap=Butt to get a dashed
// stroke (Round caps require per-sub-contour cap emission).
//
// Each call gets (subPoints, subPointCount, isStart, isEnd) where
// isStart/isEnd describe whether the sub-contour starts at the very first
// or very last point of the source polyline (so the caller can promote the
// boundary cap from Butt back to the original cap style if desired).
// ---------------------------------------------------------------------------

template <typename Fn>
inline void WalkDashPattern(
    const float* flatPoints, uint32_t pointCount,
    const float* dashPattern, uint32_t dashCount, float dashOffset,
    Fn onSubContour)
{
    if (pointCount < 2 || dashPattern == nullptr || dashCount == 0) return;

    // Total dash cycle length.
    float totalLen = 0.0f;
    for (uint32_t i = 0; i < dashCount; ++i) totalLen += dashPattern[i];
    if (totalLen <= 0.0f) return;

    // Normalize dashOffset into [0, totalLen).
    float offset = std::fmod(dashOffset, totalLen);
    if (offset < 0.0f) offset += totalLen;

    // Find which dash entry the offset lands in, and how far into it.
    uint32_t curDash = 0;
    float accum = 0.0f;
    while (curDash < dashCount) {
        if (offset < accum + dashPattern[curDash]) break;
        accum += dashPattern[curDash];
        ++curDash;
    }
    if (curDash >= dashCount) curDash = 0;
    float distInCurDash = offset - accum;
    float remainingInDash = dashPattern[curDash] - distInCurDash;
    bool isOn = (curDash % 2 == 0);

    std::vector<float> sub;
    bool subActive = false;
    auto pushSub = [&](float x, float y) {
        sub.push_back(x);
        sub.push_back(y);
    };
    auto flushSub = [&](bool isStart, bool isEnd) {
        if (subActive && sub.size() >= 4) {
            onSubContour(sub.data(), (uint32_t)(sub.size() / 2), isStart, isEnd);
        }
        sub.clear();
        subActive = false;
    };

    if (isOn) {
        pushSub(flatPoints[0], flatPoints[1]);
        subActive = true;
    }

    bool sourceStartConsumed = false;

    for (uint32_t i = 0; i + 1 < pointCount; ++i) {
        float x0 = flatPoints[i * 2],     y0 = flatPoints[i * 2 + 1];
        float x1 = flatPoints[(i + 1) * 2], y1 = flatPoints[(i + 1) * 2 + 1];
        float dx = x1 - x0, dy = y1 - y0;
        float segLen = std::sqrt(dx * dx + dy * dy);
        if (segLen <= 1e-6f) continue;

        float consumed = 0.0f;
        while (consumed < segLen) {
            float remainSeg = segLen - consumed;
            float step = std::min(remainSeg, remainingInDash);
            float t1 = (consumed + step) / segLen;
            float ex = x0 + dx * t1, ey = y0 + dy * t1;

            if (isOn) {
                if (!subActive) {
                    float sx = x0 + dx * (consumed / segLen);
                    float sy = y0 + dy * (consumed / segLen);
                    pushSub(sx, sy);
                    subActive = true;
                }
                pushSub(ex, ey);
            }

            consumed += step;
            remainingInDash -= step;
            if (remainingInDash <= 1e-6f) {
                bool endsAtSourceEnd = (i + 1 == pointCount - 1) && (std::abs(consumed - segLen) < 1e-4f);
                if (isOn) flushSub(!sourceStartConsumed, endsAtSourceEnd);
                sourceStartConsumed = true;
                curDash = (curDash + 1) % dashCount;
                isOn = (curDash % 2 == 0);
                remainingInDash = dashPattern[curDash];
            }
        }
    }

    // Flush a trailing on-sub-contour that reached the polyline end.
    if (isOn) flushSub(!sourceStartConsumed, true);
}

} // namespace jalium
