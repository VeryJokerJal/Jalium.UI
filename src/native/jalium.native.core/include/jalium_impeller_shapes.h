#pragma once

// jalium_impeller_shapes.h
//
// Backend-agnostic shape generators used by the Impeller-style engines on
// every backend. Shared across D3D12 and Vulkan so a fix to a corner case
// (e.g. degenerate ellipse, tiny circle tessellation) lands once.
//
// All generators are TVertex-templated. TVertex must be aggregate-
// constructible from { float x, float y, float r, float g, float b, float a }
// (matches both ImpellerVertex and VkImpellerVertex).
//
// Output strategy: callers pass references to their own batch's vertex /
// index vectors. The functions append into them — they do NOT clear the
// vectors first, so callers can compose multiple shapes into one batch when
// useful (the Impeller engines never do that today, but the contract is
// permissive in case a future caller wants to).

#include "jalium_rendering_engine.h"
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <vector>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

namespace jalium {

// ===========================================================================
// Trig table — Flutter Impeller's Trigs class.
//
// One quadrant's worth of (cos, sin) values, evenly spaced in angle. The
// number of divisions is selected per shape from a target arc-deviation
// tolerance (kCircleTolerance pixels) so small circles use few segments and
// large circles use many.
// ===========================================================================

struct Trig {
    float cos;
    float sin;
    Trig() : cos(1.0f), sin(0.0f) {}
    Trig(float c, float s) : cos(c), sin(s) {}
};

class TrigCache {
public:
    static constexpr float kCircleTolerance = 0.1f;
    static constexpr size_t kCachedCount = 300;

    TrigCache() : cache_(kCachedCount + 1) {}

    const std::vector<Trig>& Get(size_t divisions) const {
        Ensure(divisions);
        return cache_[divisions];
    }

    static size_t ComputeDivisions(float pixelRadius) {
        if (pixelRadius <= 0.0f) return 1;
        float k = kCircleTolerance / pixelRadius;
        if (k >= 1.0f) return 1;
        return (size_t)std::ceil((float)(M_PI / 4.0) / std::acos(1.0f - k));
    }

private:
    void Ensure(size_t divisions) const {
        if (divisions >= cache_.size()) cache_.resize(divisions + 1);
        if (!cache_[divisions].empty()) return;

        auto& v = cache_[divisions];
        v.reserve(divisions + 1);
        double angleScale = (M_PI / 2.0) / (double)divisions;
        v.push_back({ 1.0f, 0.0f });
        for (size_t i = 1; i < divisions; ++i) {
            double a = (double)i * angleScale;
            v.push_back({ (float)std::cos(a), (float)std::sin(a) });
        }
        v.push_back({ 0.0f, 1.0f });
    }

    mutable std::vector<std::vector<Trig>> cache_;
};

// ===========================================================================
// Helpers
// ===========================================================================

namespace impeller_detail {

inline float MaxTransformScale(const EngineTransform& t) {
    return std::max(
        std::sqrt(t.m11 * t.m11 + t.m12 * t.m12),
        std::sqrt(t.m21 * t.m21 + t.m22 * t.m22));
}

inline void Apply(float& x, float& y, const EngineTransform& t) {
    float tx = t.m11 * x + t.m21 * y + t.dx;
    float ty = t.m12 * x + t.m22 * y + t.dy;
    x = tx;
    y = ty;
}

// Append triangle-strip indices (zig-zag winding) starting at base for
// vertCount sequential vertices.
inline void AppendStripIndices(std::vector<uint32_t>& indices,
                               uint32_t base, uint32_t vertCount) {
    if (vertCount < 3) return;
    indices.reserve(indices.size() + (vertCount - 2) * 3);
    for (uint32_t i = 0; i + 2 < vertCount; ++i) {
        if (i % 2 == 0) {
            indices.push_back(base + i);
            indices.push_back(base + i + 1);
            indices.push_back(base + i + 2);
        } else {
            indices.push_back(base + i + 1);
            indices.push_back(base + i);
            indices.push_back(base + i + 2);
        }
    }
}

} // namespace impeller_detail

// ===========================================================================
// IsConvexPolygon — checks that all cross products of consecutive edges have
// the same sign. O(n).
// ===========================================================================

inline bool IsConvexPolygon(const float* points, uint32_t pointCount) {
    if (pointCount < 3) return false;
    bool hasPositive = false, hasNegative = false;
    for (uint32_t i = 0; i < pointCount; ++i) {
        uint32_t j = (i + 1) % pointCount;
        uint32_t k = (i + 2) % pointCount;
        float ax = points[j * 2] - points[i * 2];
        float ay = points[j * 2 + 1] - points[i * 2 + 1];
        float bx = points[k * 2] - points[j * 2];
        float by = points[k * 2 + 1] - points[j * 2 + 1];
        float cross = ax * by - ay * bx;
        if (cross >  1e-6f) hasPositive = true;
        if (cross < -1e-6f) hasNegative = true;
        if (hasPositive && hasNegative) return false;
    }
    return true;
}

// ===========================================================================
// TessellateConvexFan — O(n) triangle fan from a known-convex polygon.
// Vertex 0 is the hub; appends pointCount vertices (already in screen space)
// and (pointCount - 2) * 3 indices.
// ===========================================================================

template <typename TVertex>
inline bool TessellateConvexFan(
    std::vector<TVertex>& outVerts,
    std::vector<uint32_t>& outIndices,
    const float* points, uint32_t pointCount,
    float r, float g, float b, float a)
{
    if (pointCount < 3) return false;

    uint32_t base = (uint32_t)outVerts.size();
    outVerts.reserve(outVerts.size() + pointCount);
    for (uint32_t i = 0; i < pointCount; ++i) {
        outVerts.push_back({ points[i * 2], points[i * 2 + 1], r, g, b, a });
    }

    outIndices.reserve(outIndices.size() + (pointCount - 2) * 3);
    for (uint32_t i = 1; i + 1 < pointCount; ++i) {
        outIndices.push_back(base);
        outIndices.push_back(base + i);
        outIndices.push_back(base + i + 1);
    }
    return true;
}

// ===========================================================================
// GenerateFilledCircleStrip — Flutter Impeller's EllipticalVertexGenerator
// for a circle. Triangle-strip zig-zag between paired quadrants.
// ===========================================================================

template <typename TVertex>
inline bool GenerateFilledCircleStrip(
    std::vector<TVertex>& outVerts,
    std::vector<uint32_t>& outIndices,
    float cx, float cy, float radius,
    float r, float g, float b, float a,
    const TrigCache& trigCache,
    const EngineTransform& transform)
{
    float maxScale = impeller_detail::MaxTransformScale(transform);
    size_t divisions = TrigCache::ComputeDivisions(maxScale * radius);
    const auto& trigs = trigCache.Get(divisions);

    uint32_t base = (uint32_t)outVerts.size();
    outVerts.reserve(outVerts.size() + trigs.size() * 4);

    // Quadrant pair: left side (Q1+Q4) — bottom-left, top-left.
    for (auto& t : trigs) {
        float ox = t.cos * radius, oy = t.sin * radius;
        float p1x = cx - ox, p1y = cy + oy;
        float p2x = cx - ox, p2y = cy - oy;
        impeller_detail::Apply(p1x, p1y, transform);
        impeller_detail::Apply(p2x, p2y, transform);
        outVerts.push_back({ p1x, p1y, r, g, b, a });
        outVerts.push_back({ p2x, p2y, r, g, b, a });
    }
    // Quadrant pair: right side (Q2+Q3) — swap cos/sin.
    for (auto& t : trigs) {
        float ox = t.sin * radius, oy = t.cos * radius;
        float p1x = cx + ox, p1y = cy + oy;
        float p2x = cx + ox, p2y = cy - oy;
        impeller_detail::Apply(p1x, p1y, transform);
        impeller_detail::Apply(p2x, p2y, transform);
        outVerts.push_back({ p1x, p1y, r, g, b, a });
        outVerts.push_back({ p2x, p2y, r, g, b, a });
    }

    uint32_t added = (uint32_t)outVerts.size() - base;
    impeller_detail::AppendStripIndices(outIndices, base, added);
    return true;
}

// ===========================================================================
// GenerateFilledEllipseStrip — same triangle-strip pattern as the circle but
// with separate rx / ry. Falls back to circle when nearly equal.
// ===========================================================================

template <typename TVertex>
inline bool GenerateFilledEllipseStrip(
    std::vector<TVertex>& outVerts,
    std::vector<uint32_t>& outIndices,
    float cx, float cy, float rx, float ry,
    float r, float g, float b, float a,
    const TrigCache& trigCache,
    const EngineTransform& transform)
{
    if (std::abs(rx - ry) < 0.01f) {
        return GenerateFilledCircleStrip<TVertex>(
            outVerts, outIndices, cx, cy, rx, r, g, b, a, trigCache, transform);
    }

    float maxScale = impeller_detail::MaxTransformScale(transform);
    size_t divisions = TrigCache::ComputeDivisions(maxScale * std::max(rx, ry));
    const auto& trigs = trigCache.Get(divisions);

    uint32_t base = (uint32_t)outVerts.size();
    outVerts.reserve(outVerts.size() + trigs.size() * 4);

    for (auto& t : trigs) {
        float ox = t.cos * rx, oy = t.sin * ry;
        float p1x = cx - ox, p1y = cy + oy;
        float p2x = cx - ox, p2y = cy - oy;
        impeller_detail::Apply(p1x, p1y, transform);
        impeller_detail::Apply(p2x, p2y, transform);
        outVerts.push_back({ p1x, p1y, r, g, b, a });
        outVerts.push_back({ p2x, p2y, r, g, b, a });
    }
    for (auto& t : trigs) {
        float ox = t.sin * rx, oy = t.cos * ry;
        float p1x = cx + ox, p1y = cy + oy;
        float p2x = cx + ox, p2y = cy - oy;
        impeller_detail::Apply(p1x, p1y, transform);
        impeller_detail::Apply(p2x, p2y, transform);
        outVerts.push_back({ p1x, p1y, r, g, b, a });
        outVerts.push_back({ p2x, p2y, r, g, b, a });
    }

    uint32_t added = (uint32_t)outVerts.size() - base;
    impeller_detail::AppendStripIndices(outIndices, base, added);
    return true;
}

// ===========================================================================
// GenerateFilledRoundRectStrip — corner arcs around an inset rect, swept as
// a single triangle strip. Degenerates to ellipse when corners fill the rect.
// ===========================================================================

template <typename TVertex>
inline bool GenerateFilledRoundRectStrip(
    std::vector<TVertex>& outVerts,
    std::vector<uint32_t>& outIndices,
    float x, float y, float w, float h, float rx, float ry,
    float r, float g, float b, float a,
    const TrigCache& trigCache,
    const EngineTransform& transform)
{
    if (rx * 2 >= w && ry * 2 >= h) {
        return GenerateFilledEllipseStrip<TVertex>(
            outVerts, outIndices,
            x + w * 0.5f, y + h * 0.5f, w * 0.5f, h * 0.5f,
            r, g, b, a, trigCache, transform);
    }

    float maxScale = impeller_detail::MaxTransformScale(transform);
    size_t divisions = TrigCache::ComputeDivisions(maxScale * std::max(rx, ry));
    const auto& trigs = trigCache.Get(divisions);

    float left = x + rx;
    float top = y + ry;
    float right = x + w - rx;
    float bottom = y + h - ry;

    uint32_t base = (uint32_t)outVerts.size();
    outVerts.reserve(outVerts.size() + trigs.size() * 4);

    // Left side: top-left and bottom-left corner arcs.
    for (auto& t : trigs) {
        float ox = t.cos * rx, oy = t.sin * ry;
        float p1x = left - ox, p1y = bottom + oy;
        float p2x = left - ox, p2y = top - oy;
        impeller_detail::Apply(p1x, p1y, transform);
        impeller_detail::Apply(p2x, p2y, transform);
        outVerts.push_back({ p1x, p1y, r, g, b, a });
        outVerts.push_back({ p2x, p2y, r, g, b, a });
    }
    // Right side: top-right and bottom-right corner arcs.
    for (auto& t : trigs) {
        float ox = t.sin * rx, oy = t.cos * ry;
        float p1x = right + ox, p1y = bottom + oy;
        float p2x = right + ox, p2y = top - oy;
        impeller_detail::Apply(p1x, p1y, transform);
        impeller_detail::Apply(p2x, p2y, transform);
        outVerts.push_back({ p1x, p1y, r, g, b, a });
        outVerts.push_back({ p2x, p2y, r, g, b, a });
    }

    uint32_t added = (uint32_t)outVerts.size() - base;
    impeller_detail::AppendStripIndices(outIndices, base, added);
    return true;
}

// ===========================================================================
// GenerateStrokedCircleStrip — outer + inner concentric rings, zig-zagged
// across the full circle. Degenerates to filled circle when stroke width
// covers the whole radius.
// ===========================================================================

template <typename TVertex>
inline bool GenerateStrokedCircleStrip(
    std::vector<TVertex>& outVerts,
    std::vector<uint32_t>& outIndices,
    float cx, float cy, float radius, float strokeWidth,
    float r, float g, float b, float a,
    const TrigCache& trigCache,
    const EngineTransform& transform)
{
    float halfW = strokeWidth * 0.5f;
    if (halfW <= 0 || halfW >= radius) {
        return GenerateFilledCircleStrip<TVertex>(
            outVerts, outIndices, cx, cy, radius, r, g, b, a, trigCache, transform);
    }

    float outerR = radius + halfW;
    float innerR = radius - halfW;
    float maxScale = impeller_detail::MaxTransformScale(transform);
    size_t divisions = TrigCache::ComputeDivisions(maxScale * outerR);
    const auto& trigs = trigCache.Get(divisions);

    uint32_t base = (uint32_t)outVerts.size();
    uint32_t totalPoints = (uint32_t)(trigs.size() * 4); // full circle
    outVerts.reserve(outVerts.size() + totalPoints * 2);

    for (uint32_t q = 0; q < 4; ++q) {
        for (size_t i = 0; i < trigs.size(); ++i) {
            float tc = trigs[i].cos, ts = trigs[i].sin;
            float ox = 0, oy = 0;
            switch (q) {
                case 0: ox = -tc; oy = -ts; break;
                case 1: ox =  ts; oy = -tc; break;
                case 2: ox =  tc; oy =  ts; break;
                case 3: ox = -ts; oy =  tc; break;
            }
            float pox = cx + ox * outerR, poy = cy + oy * outerR;
            float pix = cx + ox * innerR, piy = cy + oy * innerR;
            impeller_detail::Apply(pox, poy, transform);
            impeller_detail::Apply(pix, piy, transform);
            outVerts.push_back({ pox, poy, r, g, b, a });
            outVerts.push_back({ pix, piy, r, g, b, a });
        }
    }

    uint32_t added = (uint32_t)outVerts.size() - base;
    impeller_detail::AppendStripIndices(outIndices, base, added);

    // Close the ring: connect the last pair to the first pair.
    if (added >= 4) {
        outIndices.push_back(base + added - 2);
        outIndices.push_back(base + added - 1);
        outIndices.push_back(base);
        outIndices.push_back(base + added - 1);
        outIndices.push_back(base + 1);
        outIndices.push_back(base);
    }
    return true;
}

// ===========================================================================
// GenerateRoundCapLineStrip — thick line with hemicircle caps at both ends.
// Degenerates to filled circle if endpoints coincide.
// ===========================================================================

template <typename TVertex>
inline bool GenerateRoundCapLineStrip(
    std::vector<TVertex>& outVerts,
    std::vector<uint32_t>& outIndices,
    float x0, float y0, float x1, float y1, float radius,
    float r, float g, float b, float a,
    const TrigCache& trigCache,
    const EngineTransform& transform)
{
    float dx = x1 - x0, dy = y1 - y0;
    float len = std::sqrt(dx * dx + dy * dy);
    if (len < 1e-6f) {
        return GenerateFilledCircleStrip<TVertex>(
            outVerts, outIndices,
            (x0 + x1) * 0.5f, (y0 + y1) * 0.5f, radius,
            r, g, b, a, trigCache, transform);
    }

    float ax = dx / len * radius, ay = dy / len * radius;
    float px = -ay, py = ax;

    float maxScale = impeller_detail::MaxTransformScale(transform);
    size_t divisions = TrigCache::ComputeDivisions(maxScale * radius);
    const auto& trigs = trigCache.Get(divisions);

    uint32_t base = (uint32_t)outVerts.size();
    outVerts.reserve(outVerts.size() + trigs.size() * 4);

    // First half: hemicircle at p0, sweeping back along -along axis.
    for (auto& t : trigs) {
        float relAlongX = ax * t.cos, relAlongY = ay * t.cos;
        float relAcrossX = px * t.sin, relAcrossY = py * t.sin;
        float v1x = x0 - relAlongX + relAcrossX, v1y = y0 - relAlongY + relAcrossY;
        float v2x = x0 - relAlongX - relAcrossX, v2y = y0 - relAlongY - relAcrossY;
        impeller_detail::Apply(v1x, v1y, transform);
        impeller_detail::Apply(v2x, v2y, transform);
        outVerts.push_back({ v1x, v1y, r, g, b, a });
        outVerts.push_back({ v2x, v2y, r, g, b, a });
    }
    // Second half: hemicircle at p1, swap sin/cos for forward sweep.
    for (auto& t : trigs) {
        float relAlongX = ax * t.sin, relAlongY = ay * t.sin;
        float relAcrossX = px * t.cos, relAcrossY = py * t.cos;
        float v1x = x1 + relAlongX + relAcrossX, v1y = y1 + relAlongY + relAcrossY;
        float v2x = x1 + relAlongX - relAcrossX, v2y = y1 + relAlongY - relAcrossY;
        impeller_detail::Apply(v1x, v1y, transform);
        impeller_detail::Apply(v2x, v2y, transform);
        outVerts.push_back({ v1x, v1y, r, g, b, a });
        outVerts.push_back({ v2x, v2y, r, g, b, a });
    }

    uint32_t added = (uint32_t)outVerts.size() - base;
    impeller_detail::AppendStripIndices(outIndices, base, added);
    return true;
}

// ===========================================================================
// ComputeQuadrantDivisions — Impeller's adaptive arc tessellation count.
// Used by callers that don't go through TrigCache::ComputeDivisions.
// ===========================================================================

inline uint32_t ComputeQuadrantDivisions(float pixelRadius) {
    constexpr float kCircleTolerance = 0.1f;
    if (pixelRadius <= 0.0f) return 1;
    float k = kCircleTolerance / pixelRadius;
    if (k >= 1.0f) return 1;
    float n = std::ceil((float)M_PI / 4.0f / std::acos(1.0f - k));
    return std::max(1u, std::min((uint32_t)n, 64u));
}

// ===========================================================================
// ComputeStrokeAlphaCoverage — sub-pixel strokes are clamped to half a pixel
// and their alpha is faded so they don't pop in/out as the transform scales.
// ===========================================================================

inline float ComputeStrokeAlphaCoverage(float strokeWidth, float transformScale) {
    float pixelWidth = strokeWidth * transformScale;
    if (pixelWidth >= 1.0f) return 1.0f;
    return std::max(pixelWidth, 0.05f);
}

} // namespace jalium
