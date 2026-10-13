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
// Algorithm: N× vertical subpixel sampling × continuous horizontal coverage,
// where N adapts to the path's rasterization cost (16 for anything icon- or
// control-sized, 4 for large artwork). See the long-form comment inline below
// for derivation and correctness notes.

#include "jalium_rendering_engine.h"
#include "jalium_triangulate.h"   // for jalium::Contour
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>
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
    int   row0;     // first pixel row this edge can cross (relative to yStart)
    int   row1;     // one past the last such row
};

struct Crossing { float x; int dir; };
struct RunSpan  { int x; int w; uint8_t qAlpha; };

// Per-thread scratch reused across calls. Stroke and fill rasterization runs
// hundreds of times per frame on the render thread; allocating these buffers
// per call was pure malloc/free churn (and is punishing in the Debug CRT the
// app links against). Capacity is retained between calls, so after the first
// few frames these never allocate again.
struct RasterScratch {
    std::vector<RasterEdge> edges;
    std::vector<uint32_t>   rowStart;   // CSR offsets, rowCount + 1
    std::vector<uint32_t>   rowEdges;   // CSR payload, edge indices
    std::vector<uint32_t>   rowCursor;  // fill cursors
    std::vector<float>      coverage;
    std::vector<Crossing>   crossings;
    std::vector<RunSpan>    prevSpans;
    std::vector<RunSpan>    curSpans;
};

inline RasterScratch& Scratch() noexcept {
    static thread_local RasterScratch s;
    return s;
}

// Upper bound on CSR payload entries. An icon uses ~500; the cap only
// engages for pathological artwork (thousands of edges each spanning
// thousands of rows), where we fall back to the un-bucketed scan rather
// than allocate tens of MB. 2M entries = 8 MB.
constexpr size_t kRowBucketBudget = 1u << 21;

// Crossing counts above this take std::sort; below it, a hand-rolled
// insertion sort. Both are stable for equal x, so the winding walk sees the
// same sequence either way.
constexpr uint32_t kInsertionSortMax = 32;

inline void SortCrossings(Crossing* cr, uint32_t n) noexcept {
    if (n <= kInsertionSortMax) {
        for (uint32_t i = 1; i < n; ++i) {
            Crossing key = cr[i];
            uint32_t j = i;
            while (j > 0 && key.x < cr[j - 1].x) { cr[j] = cr[j - 1]; --j; }
            cr[j] = key;
        }
    } else {
        std::sort(cr, cr + n,
            [](const Crossing& a, const Crossing& b) { return a.x < b.x; });
    }
}

} // namespace scanline_detail

// ----------------------------------------------------------------------------
// RasterizePathToRects — analytic anti-aliased scanline rasterizer.
//
// Output rectangles are appended to outRects (the function does NOT clear
// it first, so callers can layer multiple paths into the same buffer).
//
// Quality: horizontal coverage is exact (continuous span area), vertical is
// sampled at kSub sub-scanlines. The vertical sample count is therefore the
// quality ceiling for NEAR-HORIZONTAL edges — they can only take kSub+1
// distinct alpha values. At the historical kSub = 4 that is five levels, which
// is plainly visible as a staircase on small icons; kSub = 16 gives seventeen
// and reads as smooth. Cost is linear in kSub, so it is spent only where it is
// affordable: the sub count drops back to 4 once (rows × edges) says this path
// is large artwork rather than an icon. Straight horizontal/vertical edges are
// exact at any kSub.
//
// Cost: the sub-scanline loop used to test EVERY edge of the path against
// EVERY sub-scanline — O(rows × kSub × edges). That is what made stroked
// icons expensive: a 24 px lucide glyph arrives as a few hundred edges, and
// 26 rows × 16 subs × 300 edges = 125k edge tests for a shape covering ~600
// pixels.
//
// Edges are now bucketed by pixel row (CSR layout: rowStart offsets into a
// flat rowEdges index array), so each sub-scanline only visits edges that
// overlap its row — typically a twelfth of the path. The buckets are filled
// by walking `edges` in index order, so the crossings list handed to the sort
// is the same sequence the exhaustive scan produced, keeping output
// bit-identical (verified rect-for-rect against the previous implementation).
//
// The inner loops work through raw pointers rather than vector iterators.
// That is not micro-tuning for its own sake: the shipped native build is
// /Od /RTC1 /MDd, where every checked vector access and every std::sort on
// vector iterators carries real overhead, and this is the hottest CPU loop
// in the renderer.
//
// Correctness:
//   • Half-open [yMin, yMax) on edges + half-open [fillFrom, fillTo) on
//     spans means a pixel center exactly on an edge is attributed to
//     exactly one side, never both (no double-cover seam darkening).
//   • The kSub sub-scanlines are sampled at (k+0.5)/kSub offsets so coverage
//     is symmetric: a horizontal edge landing on the pixel's top or bottom
//     boundary gives 0 or 1, not 0 or 1 modulo bias.
//   • Path points exactly on integer coordinates no longer drop interior
//     pixels on triangles (this was the "scrollbar arrow has holes in the
//     middle" symptom under binary coverage — partial cover at any nearby
//     sub-row now carries the pixel).
//   • A row bucket holds every edge whose [yMin, yMax) meets [py, py+1); the
//     per-sub-scanline yMin/yMax test still runs, so bucketing can only skip
//     edges that were already being rejected.
// ----------------------------------------------------------------------------
inline void RasterizePathToRects(
    const std::vector<Contour>& contours,
    FillRule rule,
    std::vector<PixelRect>& outRects,
    const PixelRect* clip = nullptr,
    bool aliased = false)
{
    using scanline_detail::RasterEdge;
    using scanline_detail::Crossing;
    using scanline_detail::RunSpan;

    if (contours.empty()) return;

    auto& scratch = scanline_detail::Scratch();
    auto& edges = scratch.edges;
    edges.clear();
    if (edges.capacity() < 64) edges.reserve(64);

    float minY =  std::numeric_limits<float>::infinity();
    float maxY = -std::numeric_limits<float>::infinity();
    float minX =  std::numeric_limits<float>::infinity();
    float maxX = -std::numeric_limits<float>::infinity();

    auto addEdge = [&](float x0, float y0, float x1, float y1) {
        if (!std::isfinite(x0) || !std::isfinite(y0) ||
            !std::isfinite(x1) || !std::isfinite(y1)) return;
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
        e.row0 = 0; e.row1 = 0;   // filled in once yStart is known
        edges.push_back(e);
    };

    for (const auto& c : contours) {
        uint32_t n = c.VertexCount();
        if (n < 2) continue;
        const float* p = c.points.data();
        for (uint32_t i = 0; i + 1 < n; ++i) {
            addEdge(p[i * 2], p[i * 2 + 1], p[i * 2 + 2], p[i * 2 + 3]);
        }
        // Implicit close if the last vertex isn't already the first.
        if (n >= 3) {
            float fx0 = p[0],           fy0 = p[1];
            float lx  = p[(n - 1) * 2], ly  = p[(n - 1) * 2 + 1];
            if (std::abs(fx0 - lx) > 1e-6f || std::abs(fy0 - ly) > 1e-6f) {
                addEdge(lx, ly, fx0, fy0);
            }
        }
    }

    if (edges.empty()) return;

    // Bound device-space work before converting to integers or allocating rows.
    // Edges stay intact: clipping edges individually would change winding.
    if (clip) {
        minX = std::max(minX, static_cast<float>(clip->x));
        maxX = std::min(maxX, static_cast<float>(clip->x) + clip->w);
        minY = std::max(minY, static_cast<float>(clip->y));
        maxY = std::min(maxY, static_cast<float>(clip->y) + clip->h);
    }
    if (!(minX < maxX) || !(minY < maxY)) return;
    constexpr float kSafeCoordinate = static_cast<float>(INT32_MAX / 4);
    if (minX < -kSafeCoordinate || maxX > kSafeCoordinate ||
        minY < -kSafeCoordinate || maxY > kSafeCoordinate) return;
    int pxX0 = (int)std::floor(minX) - (clip ? 0 : 1);
    int pxX1 = (int)std::ceil (maxX) + (clip ? 0 : 1);
    int pxWidth = pxX1 - pxX0;
    if (pxWidth <= 0) return;

    int yStart = (int)std::floor(minY);
    int yEnd   = (int)std::ceil (maxY);
    if (yEnd <= yStart) return;

    // Vertical sub-scanline count. Budgeted by (rows × edges): everything an
    // application actually draws as an icon, glyph or control glyph lands far
    // under the cap and gets the high-quality 16×, while a full-page vector
    // illustration keeps the historical 4× and the historical cost.
    constexpr uint64_t kHighQualityWorkBudget = 200000;
    const uint64_t rasterWork =
        (uint64_t)(yEnd - yStart) * (uint64_t)edges.size();
    const int   kSub     = aliased ? 1 : (rasterWork <= kHighQualityWorkBudget) ? 16 : 4;
    const float kSubStep = 1.0f / (float)kSub;

    const int    rowCount  = yEnd - yStart;
    const size_t edgeCount = edges.size();
    RasterEdge*  edgeData  = edges.data();

    // ── Bucket edges by pixel row (CSR) ────────────────────────────────
    // Row r (absolute py = yStart + r) holds every edge whose [yMin, yMax)
    // intersects [py, py+1): first row = floor(yMin), one-past-last row =
    // ceil(yMax). Both bounds are exact — floor(yMin) + 1 > yMin always, and
    // the largest py with py < yMax is ceil(yMax) - 1 for integral and
    // fractional yMax alike — so no edge can be dropped from a row it would
    // have contributed a crossing to.
    size_t totalEntries = 0;
    for (size_t i = 0; i < edgeCount; ++i) {
        RasterEdge& e = edgeData[i];
        int r0 = (int)std::floor(std::clamp(e.yMin, float(yStart), float(yEnd))) - yStart;
        int r1 = (int)std::ceil(std::clamp(e.yMax, float(yStart), float(yEnd))) - yStart;
        if (r1 < r0) r1 = r0;
        e.row0 = r0; e.row1 = r1;
        totalEntries += (size_t)(r1 - r0);
    }

    const bool bucketed =
        (totalEntries > 0 && totalEntries <= scanline_detail::kRowBucketBudget);

    const uint32_t* rowStartData = nullptr;
    const uint32_t* rowEdgeData  = nullptr;
    if (bucketed) {
        auto& rowStart  = scratch.rowStart;
        auto& rowEdges  = scratch.rowEdges;
        auto& rowCursor = scratch.rowCursor;

        rowStart.assign((size_t)rowCount + 1, 0u);
        uint32_t* rs = rowStart.data();
        for (size_t i = 0; i < edgeCount; ++i) {
            const RasterEdge& e = edgeData[i];
            for (int r = e.row0; r < e.row1; ++r) rs[r + 1]++;
        }
        for (int r = 0; r < rowCount; ++r) rs[r + 1] += rs[r];

        rowCursor.assign(rowStart.begin(), rowStart.end());
        rowEdges.resize(totalEntries);
        uint32_t* rc = rowCursor.data();
        uint32_t* re = rowEdges.data();
        // Walking edges in index order keeps every bucket sorted by edge
        // index, which is what makes the crossings sequence identical to
        // the exhaustive scan's.
        for (size_t i = 0; i < edgeCount; ++i) {
            const RasterEdge& e = edgeData[i];
            for (int r = e.row0; r < e.row1; ++r) re[rc[r]++] = (uint32_t)i;
        }
        rowStartData = rowStart.data();
        rowEdgeData  = rowEdges.data();
    }

    auto& coverageVec = scratch.coverage;
    coverageVec.assign((size_t)pxWidth, 0.0f);
    float* coverage = coverageVec.data();

    auto& crossVec = scratch.crossings;
    if (crossVec.size() < edgeCount) crossVec.resize(edgeCount);
    Crossing* cross = crossVec.data();

    auto& prevVec = scratch.prevSpans;
    auto& curVec  = scratch.curSpans;
    // A row's spans are runs of equal quantized alpha, so the real bound is
    // one per pixel — NOT one per edge. A near-horizontal edge ramps coverage
    // across hundreds of pixels, producing hundreds of distinct alpha runs out
    // of two edges; sizing by edge count silently truncated those rows on wide
    // shallow shapes (a full-width rule, a chart axis) while every icon-sized
    // test stayed under the cap.
    const size_t spanCap = (size_t)pxWidth + 1;
    if (prevVec.size() < spanCap) prevVec.resize(spanCap);
    if (curVec.size()  < spanCap) curVec.resize(spanCap);
    RunSpan* prevSpans = prevVec.data();
    RunSpan* curSpans  = curVec.data();
    size_t prevCount = 0;
    int    runStartY = 0;
    bool   runOpen   = false;

    for (int py = yStart; py < yEnd; ++py) {
        // Only the x range actually written needs re-zeroing afterwards; the
        // rest of `coverage` is already 0 and stays 0.
        int touchedLo = pxWidth;
        int touchedHi = 0;

        const uint32_t* bucket    = nullptr;
        size_t          bucketLen = edgeCount;
        if (bucketed) {
            const int r = py - yStart;
            const uint32_t lo = rowStartData[r];
            const uint32_t hi = rowStartData[r + 1];
            if (lo == hi) {
                // No edge reaches this row: nothing to draw, and any open run
                // must end here.
                if (runOpen) {
                    int h = py - runStartY;
                    if (h > 0) {
                        for (size_t si = 0; si < prevCount; ++si) {
                            const RunSpan& s = prevSpans[si];
                            outRects.push_back({ s.x, runStartY, s.w, h,
                                                 (float)s.qAlpha / 255.0f });
                        }
                    }
                    runOpen = false;
                    prevCount = 0;
                }
                continue;
            }
            bucket    = rowEdgeData + lo;
            bucketLen = (size_t)(hi - lo);
        }

        for (int k = 0; k < kSub; ++k) {
            float fy = (float)py + ((float)k + 0.5f) * kSubStep;
            if (fy < minY || fy >= maxY) continue;

            uint32_t crossCount = 0;
            if (bucketed) {
                for (size_t bi = 0; bi < bucketLen; ++bi) {
                    const RasterEdge& e = edgeData[bucket[bi]];
                    if (fy < e.yMin || fy >= e.yMax) continue;
                    cross[crossCount].x   = e.xAtYMin + (fy - e.yMin) * e.dxdy;
                    cross[crossCount].dir = e.dir;
                    ++crossCount;
                }
            } else {
                for (size_t ei = 0; ei < bucketLen; ++ei) {
                    const RasterEdge& e = edgeData[ei];
                    if (fy < e.yMin || fy >= e.yMax) continue;
                    cross[crossCount].x   = e.xAtYMin + (fy - e.yMin) * e.dxdy;
                    cross[crossCount].dir = e.dir;
                    ++crossCount;
                }
            }
            if (crossCount == 0) continue;

            scanline_detail::SortCrossings(cross, crossCount);

            int   winding  = 0;
            bool  inside   = false;
            float fillFrom = 0.0f;
            const bool nonZero = (rule == FillRule::NonZero);
            for (uint32_t ci = 0; ci < crossCount; ++ci) {
                const Crossing& cr = cross[ci];
                bool was = inside;
                if (nonZero) {
                    winding += cr.dir;
                    inside   = (winding != 0);
                } else {
                    winding ^= 1;
                    inside   = (winding != 0);
                }
                if (!was && inside) {
                    fillFrom = cr.x;
                } else if (was && !inside) {
                    float fillTo = cr.x;
                    if (fillTo <= fillFrom) continue;

                    // Clamp spans before integer conversion (a visible path
                    // can have control points far beyond the viewport).
                    fillFrom = std::max(fillFrom, static_cast<float>(pxX0));
                    fillTo = std::min(fillTo, static_cast<float>(pxX1));
                    if (!(fillFrom < fillTo)) continue;
                    int pxA = aliased ? (int)std::ceil(fillFrom - 0.5f) - pxX0
                                      : (int)std::floor(fillFrom) - pxX0;
                    int pxB = aliased ? (int)std::ceil(fillTo - 0.5f) - pxX0
                                      : (int)std::ceil(fillTo) - pxX0;
                    if (pxA < 0) pxA = 0;
                    if (pxB > pxWidth) pxB = pxWidth;
                    if (pxA < touchedLo) touchedLo = pxA;
                    if (pxB > touchedHi) touchedHi = pxB;

                    for (int px = pxA; px < pxB; ++px) {
                        float pxLeft  = (float)(px + pxX0);
                        float pxRight = pxLeft + 1.0f;
                        float l = pxLeft  > fillFrom ? pxLeft  : fillFrom;
                        float r = pxRight < fillTo   ? pxRight : fillTo;
                        if (r > l) {
                            coverage[px] += aliased ? 1.0f : (r - l) * kSubStep;
                        }
                    }
                }
            }
        }

        // RLE the coverage row into runs of identical quantized alpha. Only
        // the touched range can be non-zero, and a zero run is skipped
        // anyway, so scanning just [touchedLo, touchedHi) is equivalent.
        size_t curCount = 0;
        {
            int px = touchedLo;
            while (px < touchedHi) {
                float c0 = coverage[px];
                if (c0 > 1.0f) c0 = 1.0f;
                int q0 = (int)(c0 * 255.0f + 0.5f);
                if (q0 <= 0) { ++px; continue; }

                int runEnd = px + 1;
                while (runEnd < touchedHi) {
                    float c1 = coverage[runEnd];
                    if (c1 > 1.0f) c1 = 1.0f;
                    int q1 = (int)(c1 * 255.0f + 0.5f);
                    if (q1 != q0) break;
                    ++runEnd;
                }

                if (curCount < spanCap) {
                    curSpans[curCount].x      = px + pxX0;
                    curSpans[curCount].w      = runEnd - px;
                    curSpans[curCount].qAlpha = (uint8_t)q0;
                    ++curCount;
                }
                px = runEnd;
            }
        }

        // Re-zero only what we wrote, ready for the next row.
        for (int px = touchedLo; px < touchedHi; ++px) coverage[px] = 0.0f;

        // Vertical coalescing vs the currently-open run.
        bool same = runOpen && curCount == prevCount;
        if (same) {
            for (size_t si = 0; si < curCount; ++si) {
                if (curSpans[si].x != prevSpans[si].x ||
                    curSpans[si].w != prevSpans[si].w ||
                    curSpans[si].qAlpha != prevSpans[si].qAlpha) { same = false; break; }
            }
        }

        if (!same) {
            if (runOpen) {
                int h = py - runStartY;
                if (h > 0) {
                    for (size_t si = 0; si < prevCount; ++si) {
                        const RunSpan& s = prevSpans[si];
                        outRects.push_back({ s.x, runStartY, s.w, h,
                                             (float)s.qAlpha / 255.0f });
                    }
                }
                runOpen = false;
                prevCount = 0;
            }
            if (curCount != 0) {
                std::swap(prevSpans, curSpans);
                prevCount = curCount;
                runStartY = py;
                runOpen   = true;
            }
        }
    }

    if (runOpen) {
        int h = yEnd - runStartY;
        if (h > 0) {
            for (size_t si = 0; si < prevCount; ++si) {
                const RunSpan& s = prevSpans[si];
                outRects.push_back({ s.x, runStartY, s.w, h,
                                     (float)s.qAlpha / 255.0f });
            }
        }
    }
}

} // namespace jalium
