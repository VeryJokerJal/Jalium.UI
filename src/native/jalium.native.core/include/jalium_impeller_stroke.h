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
    constexpr uint32_t kSegments = 8;
    constexpr float kPi = (float)M_PI;

    float angle0 = std::atan2(ny, nx);
    float startAngle = isStart ? (angle0 + kPi * 0.5f) : (angle0 - kPi * 0.5f);
    float sweep = isStart ? kPi : -kPi;

    uint32_t base = (uint32_t)verts.size();
    verts.push_back({ cx, cy, r, g, b, a }); // hub

    for (uint32_t i = 0; i <= kSegments; ++i) {
        float t = (float)i / (float)kSegments;
        float angle = startAngle + sweep * t;
        verts.push_back({
            cx + halfWidth * std::cos(angle),
            cy + halfWidth * std::sin(angle),
            r, g, b, a });
    }

    for (uint32_t i = 0; i < kSegments; ++i) {
        indices.push_back(base);
        indices.push_back(base + 1 + i);
        indices.push_back(base + 2 + i);
    }
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
    float cr = n0x * n1y - n0y * n1x;
    if (std::abs(cr) < 1e-5f) return; // nearly collinear normals — no corner

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

    uint32_t base = (uint32_t)verts.size();
    verts.push_back({ cx, cy, r, g, b, a });
    for (uint32_t i = 0; i <= segments; ++i) {
        float t = (float)i / (float)segments;
        float angle = angle0 + diff * t;
        verts.push_back({
            cx + halfWidth * std::cos(angle),
            cy + halfWidth * std::sin(angle),
            r, g, b, a });
    }
    for (uint32_t i = 0; i < segments; ++i) {
        indices.push_back(base);
        indices.push_back(base + 1 + i);
        indices.push_back(base + 2 + i);
    }
}

// ---------------------------------------------------------------------------
// ExpandStrokePath — the main entry point.
//
// flatPoints is the bezier-flattened polyline (x0,y0,x1,y1,...) in screen
// space. brushR/G/B/A is the un-premultiplied brush color; the function
// premultiplies internally (and applies hairline alpha fade if appropriate).
//
// When collectContours is non-null the function does NOT touch outVerts /
// outIndices and instead packs each emitted triangle as a 3-vertex Contour.
// Used by callers that want to feed the stroke geometry through an analytic-
// AA path (every triangle is winding-normalized to CCW so that NonZero fill
// gives the correct union, see comment in the implementation).
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
    std::vector<Contour>* collectContours = nullptr)
{
    if (pointCount < 2 || flatPoints == nullptr) return false;

    float halfWidth = strokeWidth * 0.5f;
    float r = brushR * brushA;
    float g = brushG * brushA;
    float b = brushB * brushA;
    float a = brushA;

    // Sub-pixel hairline alpha fade — only when going through the direct
    // (binary GPU) rasterization path. The analytic AA path takes the true
    // halfWidth so per-pixel coverage handles hairlines naturally.
    if (!collectContours && halfWidth < 0.5f && halfWidth > 0.0f) {
        float fade = halfWidth / 0.5f;
        r *= fade; g *= fade; b *= fade; a *= fade;
        halfWidth = 0.5f;
    }

    // Local geometry vectors — used either to fill outVerts/outIndices
    // directly (collectContours == null) or as a scratch buffer that
    // we then chop into per-triangle contours below.
    std::vector<TVertex> localVerts;
    std::vector<uint32_t> localIndices;
    auto& verts = collectContours ? localVerts : outVerts;
    auto& indices = collectContours ? localIndices : outIndices;

    auto getX = [&](uint32_t i) { return flatPoints[i * 2]; };
    auto getY = [&](uint32_t i) { return flatPoints[i * 2 + 1]; };

    struct Segment { float nx, ny; };
    std::vector<Segment> segNormals;
    segNormals.reserve(pointCount - 1);
    for (uint32_t i = 0; i + 1 < pointCount; ++i) {
        float dx = getX(i + 1) - getX(i);
        float dy = getY(i + 1) - getY(i);
        float len = std::sqrt(dx * dx + dy * dy);
        if (len < 1e-6f) { segNormals.push_back({0, 0}); continue; }
        segNormals.push_back({ -dy / len, dx / len });
    }

    // ---- Per-segment quads (CCW: top-right, top-left, bottom-right, bottom-left) ----
    for (uint32_t i = 0; i + 1 < pointCount; ++i) {
        float nx = segNormals[i].nx * halfWidth;
        float ny = segNormals[i].ny * halfWidth;
        float x0 = getX(i), y0 = getY(i);
        float x1 = getX(i + 1), y1 = getY(i + 1);

        uint32_t base = (uint32_t)verts.size();
        verts.push_back({ x0 + nx, y0 + ny, r, g, b, a });
        verts.push_back({ x0 - nx, y0 - ny, r, g, b, a });
        verts.push_back({ x1 + nx, y1 + ny, r, g, b, a });
        verts.push_back({ x1 - nx, y1 - ny, r, g, b, a });
        indices.push_back(base);     indices.push_back(base + 1); indices.push_back(base + 2);
        indices.push_back(base + 1); indices.push_back(base + 3); indices.push_back(base + 2);
    }

    // ---- Joins between adjacent segments ----
    auto emitJoin = [&](float n0x, float n0y, float n1x, float n1y, float cx, float cy) {
        if (join == ImpellerJoin::Round) {
            GenerateRoundJoin<TVertex>(verts, indices, cx, cy,
                n0x, n0y, n1x, n1y, halfWidth, r, g, b, a);
            return;
        }
        if (join == ImpellerJoin::Bevel) {
            uint32_t base = (uint32_t)verts.size();
            verts.push_back({ cx, cy, r, g, b, a });
            verts.push_back({ cx + n0x * halfWidth, cy + n0y * halfWidth, r, g, b, a });
            verts.push_back({ cx + n1x * halfWidth, cy + n1y * halfWidth, r, g, b, a });
            indices.push_back(base); indices.push_back(base + 1); indices.push_back(base + 2);
            base = (uint32_t)verts.size();
            verts.push_back({ cx, cy, r, g, b, a });
            verts.push_back({ cx - n0x * halfWidth, cy - n0y * halfWidth, r, g, b, a });
            verts.push_back({ cx - n1x * halfWidth, cy - n1y * halfWidth, r, g, b, a });
            indices.push_back(base); indices.push_back(base + 1); indices.push_back(base + 2);
            return;
        }
        // Miter (with bevel fallback when the miter exceeds miterLimit).
        float dot = n0x * n1x + n0y * n1y;
        float alignment = (dot + 1.0f) * 0.5f;
        if (alignment > 0.999f) return; // nearly straight, no join needed

        float cr = n0x * n1y - n0y * n1x;
        float dir = cr > 0 ? -1.0f : 1.0f;

        // Bevel base triangle — always present.
        uint32_t base = (uint32_t)verts.size();
        verts.push_back({ cx, cy, r, g, b, a });
        verts.push_back({ cx + n0x * halfWidth * dir, cy + n0y * halfWidth * dir, r, g, b, a });
        verts.push_back({ cx + n1x * halfWidth * dir, cy + n1y * halfWidth * dir, r, g, b, a });
        indices.push_back(base); indices.push_back(base + 1); indices.push_back(base + 2);

        // Miter extension (only when within limit).
        if (alignment > 1e-6f) {
            float mx = (n0x + n1x) * 0.5f * halfWidth / alignment;
            float my = (n0y + n1y) * 0.5f * halfWidth / alignment;
            float miterDist2 = mx * mx + my * my;
            float miterLimitDist2 = miterLimit * miterLimit;
            if (miterDist2 <= miterLimitDist2) {
                uint32_t mbase = (uint32_t)verts.size();
                verts.push_back({ cx + mx * dir, cy + my * dir, r, g, b, a });
                indices.push_back(base);
                indices.push_back(base + 2);
                indices.push_back(mbase);
            }
        }
    };

    for (uint32_t i = 1; i + 1 < pointCount; ++i) {
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
        // Start cap.
        float nx = segNormals[0].nx, ny = segNormals[0].ny;
        float cx = getX(0), cy = getY(0);
        if (cap == ImpellerCap::Round) {
            GenerateRoundCap<TVertex>(verts, indices, cx, cy, nx, ny,
                halfWidth, r, g, b, a, true);
        } else if (cap == ImpellerCap::Square) {
            float dx = -segNormals[0].ny, dy = segNormals[0].nx;
            uint32_t base = (uint32_t)verts.size();
            verts.push_back({ cx + nx * halfWidth - dx * halfWidth, cy + ny * halfWidth - dy * halfWidth, r, g, b, a });
            verts.push_back({ cx - nx * halfWidth - dx * halfWidth, cy - ny * halfWidth - dy * halfWidth, r, g, b, a });
            verts.push_back({ cx + nx * halfWidth, cy + ny * halfWidth, r, g, b, a });
            verts.push_back({ cx - nx * halfWidth, cy - ny * halfWidth, r, g, b, a });
            indices.push_back(base);     indices.push_back(base + 1); indices.push_back(base + 2);
            indices.push_back(base + 1); indices.push_back(base + 3); indices.push_back(base + 2);
        }
        // End cap.
        uint32_t lastSeg = (uint32_t)segNormals.size() - 1;
        nx = segNormals[lastSeg].nx; ny = segNormals[lastSeg].ny;
        cx = getX(pointCount - 1); cy = getY(pointCount - 1);
        if (cap == ImpellerCap::Round) {
            GenerateRoundCap<TVertex>(verts, indices, cx, cy, nx, ny,
                halfWidth, r, g, b, a, false);
        } else if (cap == ImpellerCap::Square) {
            float dx = segNormals[lastSeg].ny, dy = -segNormals[lastSeg].nx;
            uint32_t base = (uint32_t)verts.size();
            verts.push_back({ cx + nx * halfWidth, cy + ny * halfWidth, r, g, b, a });
            verts.push_back({ cx - nx * halfWidth, cy - ny * halfWidth, r, g, b, a });
            verts.push_back({ cx + nx * halfWidth + dx * halfWidth, cy + ny * halfWidth + dy * halfWidth, r, g, b, a });
            verts.push_back({ cx - nx * halfWidth + dx * halfWidth, cy - ny * halfWidth + dy * halfWidth, r, g, b, a });
            indices.push_back(base);     indices.push_back(base + 1); indices.push_back(base + 2);
            indices.push_back(base + 1); indices.push_back(base + 3); indices.push_back(base + 2);
        }
    }

    if (verts.empty() || indices.empty()) return true;

    // Convert the triangle mesh to per-triangle contours (winding-normalized
    // to CCW) so the analytic-AA NonZero rasterizer sees stroke = union.
    if (collectContours) {
        for (size_t ti = 0; ti + 2 < indices.size(); ti += 3) {
            uint32_t i0 = indices[ti];
            uint32_t i1 = indices[ti + 1];
            uint32_t i2 = indices[ti + 2];
            if (i0 >= verts.size() || i1 >= verts.size() || i2 >= verts.size()) continue;
            float ax = verts[i0].x, ay = verts[i0].y;
            float bx = verts[i1].x, by = verts[i1].y;
            float cx = verts[i2].x, cy = verts[i2].y;
            float sa = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            if (std::abs(sa) < 1e-7f) continue;
            Contour tri;
            tri.points.reserve(6);
            tri.points.push_back(ax); tri.points.push_back(ay);
            if (sa > 0.0f) {
                tri.points.push_back(bx); tri.points.push_back(by);
                tri.points.push_back(cx); tri.points.push_back(cy);
            } else {
                tri.points.push_back(cx); tri.points.push_back(cy);
                tri.points.push_back(bx); tri.points.push_back(by);
            }
            collectContours->push_back(std::move(tri));
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
