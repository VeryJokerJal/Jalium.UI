#pragma once

// jalium_scanline_rasterizer.h
//
// Cross-backend analytic-anti-aliased scanline rasterizer. Converts
// arbitrary contours (any number, any winding, any fill rule, concave /
// self-intersecting / with holes) into a list of axis-aligned rectangles
// whose per-rect alpha encodes the exact fractional coverage of the source
// path under D3D's top-left rule.
//
// This is the same algorithm the D3D12 Impeller engine carried locally;
// hoisted into jalium.native.core so the Vulkan Impeller engine can share
// the exact pixel output (and any future correctness fix lands once).
//
// Algorithm: 4× vertical subpixel sampling × continuous horizontal coverage.
// See the long-form comment inline below for derivation and correctness
// notes — moved verbatim from the D3D12 implementation.

#include "jalium_rendering_engine.h"
#include "jalium_triangulate.h"   // for jalium::Contour
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>
#include <utility>
#include <vector>

namespace jalium {

/// Output unit: an axis-aligned rectangle with a per-rect alpha in [0, 1].
/// The alpha is applied to the already-premultiplied brush color at emit
/// time (color * alpha, alpha * alpha), keeping the solid-fill PSO's
/// premult-alpha blending correct.
struct PixelRect {
    int x;
    int y;
    int w;
    int h;
    float alpha;
};

namespace scanline_detail {

struct RasterEdge {
    float yMin;     // half-open [yMin, yMax)
    float yMax;
    float xAtYMin;
    float dxdy;     // dx per unit dy (inverse slope)
    int   dir;      // +1 for downward edge, -1 for upward
};

} // namespace scanline_detail

// ----------------------------------------------------------------------------
// RasterizePathToRects — analytic anti-aliased scanline rasterizer.
//
// Output rectangles are appended to outRects (the function does NOT clear
// it first, so callers can layer multiple paths into the same buffer).
//
// Quality: 4× vertical × continuous horizontal gives roughly 32 unique
// coverage levels at edge pixels, visually indistinguishable from 8-bit AA
// on typical UI shapes. Straight horizontal/vertical edges remain perfectly
// sharp.
//
// Correctness:
//   • Half-open [yMin, yMax) on edges + half-open [fillFrom, fillTo) on
//     spans means a pixel center exactly on an edge is attributed to
//     exactly one side, never both (no double-cover seam darkening).
//   • The 4 sub-scanlines are sampled at (k+0.5)/4 offsets so coverage is
//     symmetric: a horizontal edge landing on the pixel's top or bottom
//     boundary gives 0 or 1, not 0 or 1 modulo bias.
//   • Path points exactly on integer coordinates no longer drop interior
//     pixels on triangles (this was the "scrollbar arrow has holes in the
//     middle" symptom under binary coverage — partial cover at any nearby
//     sub-row now carries the pixel).
// ----------------------------------------------------------------------------
inline void RasterizePathToRects(
    const std::vector<Contour>& contours,
    FillRule rule,
    std::vector<PixelRect>& outRects)
{
    using scanline_detail::RasterEdge;

    if (contours.empty()) return;

    std::vector<RasterEdge> edges;
    edges.reserve(64);

    float minY =  std::numeric_limits<float>::infinity();
    float maxY = -std::numeric_limits<float>::infinity();
    float minX =  std::numeric_limits<float>::infinity();
    float maxX = -std::numeric_limits<float>::infinity();

    auto addEdge = [&](float x0, float y0, float x1, float y1) {
        if (x0 < minX) minX = x0;
        if (x1 < minX) minX = x1;
        if (x0 > maxX) maxX = x0;
        if (x1 > maxX) maxX = x1;
        if (y0 < minY) minY = y0;
        if (y1 < minY) minY = y1;
        if (y0 > maxY) maxY = y0;
        if (y1 > maxY) maxY = y1;

        float dy = y1 - y0;
        if (std::abs(dy) < 1e-7f) return; // horizontal: no scanline crossings

        RasterEdge e;
        if (y0 < y1) {
            e.yMin = y0; e.yMax = y1;
            e.xAtYMin = x0;
            e.dxdy = (x1 - x0) / (y1 - y0);
            e.dir  = +1;
        } else {
            e.yMin = y1; e.yMax = y0;
            e.xAtYMin = x1;
            e.dxdy = (x0 - x1) / (y0 - y1);
            e.dir  = -1;
        }
        edges.push_back(e);
    };

    for (const auto& c : contours) {
        uint32_t n = c.VertexCount();
        if (n < 2) continue;
        for (uint32_t i = 0; i + 1 < n; ++i) {
            addEdge(c.X(i), c.Y(i), c.X(i + 1), c.Y(i + 1));
        }
        // Implicit close if the last vertex isn't already the first.
        if (n >= 3) {
            float fx0 = c.X(0),     fy0 = c.Y(0);
            float lx  = c.X(n - 1), ly  = c.Y(n - 1);
            if (std::abs(fx0 - lx) > 1e-6f || std::abs(fy0 - ly) > 1e-6f) {
                addEdge(lx, ly, fx0, fy0);
            }
        }
    }

    if (edges.empty()) return;

    int pxX0 = (int)std::floor(minX) - 1;
    int pxX1 = (int)std::ceil (maxX) + 1;
    int pxWidth = pxX1 - pxX0;
    if (pxWidth <= 0) return;

    int yStart = (int)std::floor(minY);
    int yEnd   = (int)std::ceil (maxY);
    if (yEnd <= yStart) return;

    constexpr int   kSub     = 4;
    constexpr float kSubStep = 1.0f / (float)kSub;

    std::vector<float> coverage((size_t)pxWidth, 0.0f);
    std::vector<std::pair<float, int>> crossings;
    crossings.reserve(edges.size());

    struct RunSpan { int x; int w; uint8_t qAlpha; };
    std::vector<RunSpan> prevSpans;
    std::vector<RunSpan> curSpans;
    int  runStartY = 0;
    bool runOpen   = false;

    auto flushRun = [&](int yExclusive) {
        if (!runOpen) return;
        int h = yExclusive - runStartY;
        if (h > 0) {
            for (const auto& s : prevSpans) {
                outRects.push_back({
                    s.x, runStartY, s.w, h,
                    (float)s.qAlpha / 255.0f
                });
            }
        }
        runOpen = false;
        prevSpans.clear();
    };

    for (int py = yStart; py < yEnd; ++py) {
        std::fill(coverage.begin(), coverage.end(), 0.0f);

        for (int k = 0; k < kSub; ++k) {
            float fy = (float)py + ((float)k + 0.5f) * kSubStep;
            if (fy < minY || fy >= maxY) continue;

            crossings.clear();
            for (const auto& e : edges) {
                if (fy < e.yMin || fy >= e.yMax) continue;
                float x = e.xAtYMin + (fy - e.yMin) * e.dxdy;
                crossings.push_back({ x, e.dir });
            }
            if (crossings.empty()) continue;

            std::sort(crossings.begin(), crossings.end(),
                [](const std::pair<float,int>& a, const std::pair<float,int>& b) {
                    return a.first < b.first;
                });

            int   winding  = 0;
            bool  inside   = false;
            float fillFrom = 0.0f;
            for (const auto& cr : crossings) {
                bool was = inside;
                if (rule == FillRule::NonZero) {
                    winding += cr.second;
                    inside   = (winding != 0);
                } else {
                    winding ^= 1;
                    inside   = (winding != 0);
                }
                if (!was && inside) {
                    fillFrom = cr.first;
                } else if (was && !inside) {
                    float fillTo = cr.first;
                    if (fillTo <= fillFrom) continue;

                    int pxA = (int)std::floor(fillFrom) - pxX0;
                    int pxB = (int)std::ceil (fillTo)   - pxX0;
                    if (pxA < 0) pxA = 0;
                    if (pxB > pxWidth) pxB = pxWidth;

                    for (int px = pxA; px < pxB; ++px) {
                        float pxLeft  = (float)(px + pxX0);
                        float pxRight = pxLeft + 1.0f;
                        float l = pxLeft  > fillFrom ? pxLeft  : fillFrom;
                        float r = pxRight < fillTo   ? pxRight : fillTo;
                        if (r > l) {
                            coverage[(size_t)px] += (r - l) * kSubStep;
                        }
                    }
                }
            }
        }

        // RLE the coverage row into runs of identical quantized alpha.
        curSpans.clear();
        {
            int px = 0;
            while (px < pxWidth) {
                float c0 = coverage[(size_t)px];
                if (c0 > 1.0f) c0 = 1.0f;
                int q0 = (int)(c0 * 255.0f + 0.5f);
                if (q0 <= 0) { ++px; continue; }

                int runEnd = px + 1;
                while (runEnd < pxWidth) {
                    float c1 = coverage[(size_t)runEnd];
                    if (c1 > 1.0f) c1 = 1.0f;
                    int q1 = (int)(c1 * 255.0f + 0.5f);
                    if (q1 != q0) break;
                    ++runEnd;
                }

                curSpans.push_back({
                    px + pxX0,
                    runEnd - px,
                    (uint8_t)q0
                });
                px = runEnd;
            }
        }

        // Vertical coalescing vs the currently-open run.
        bool same =
            runOpen &&
            curSpans.size() == prevSpans.size() &&
            std::equal(curSpans.begin(), curSpans.end(), prevSpans.begin(),
                [](const RunSpan& a, const RunSpan& b) {
                    return a.x == b.x && a.w == b.w && a.qAlpha == b.qAlpha;
                });

        if (!same) {
            flushRun(py);
            if (!curSpans.empty()) {
                prevSpans = curSpans;
                runStartY = py;
                runOpen   = true;
            }
        }
    }

    flushRun(yEnd);
}

} // namespace jalium
