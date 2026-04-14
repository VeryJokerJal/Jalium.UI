#pragma once

#include <cstdint>
#include <cmath>
#include <vector>
#include <algorithm>
#include <numeric>
#include <limits>

namespace jalium {

// ============================================================================
// Contour representation for compound paths
// ============================================================================

/// A single contour (sub-path) as a flat array of (x, y) pairs.
struct Contour {
    std::vector<float> points; // x0,y0, x1,y1, ...
    uint32_t VertexCount() const { return static_cast<uint32_t>(points.size() / 2); }
    float X(uint32_t i) const { return points[i * 2]; }
    float Y(uint32_t i) const { return points[i * 2 + 1]; }
};

// ============================================================================
// Ear-clipping polygon triangulator
// ============================================================================

// Ear-clipping polygon triangulator for simple (convex or concave) polygons.
// Input:  points - array of float pairs (x0,y0,x1,y1,...), count - number of vertices.
// Output: outIndices - triangle index triples (v0,v1,v2, v3,v4,v5, ...).
// Returns true on success, false if degenerate (< 3 points).
inline bool TriangulatePolygon(const float* points, uint32_t count, std::vector<uint32_t>& outIndices) {
    outIndices.clear();
    if (count < 3) return false;

    // Build index list
    std::vector<uint32_t> indices(count);
    for (uint32_t i = 0; i < count; ++i) indices[i] = i;

    // Compute signed area to determine winding
    float signedArea = 0.0f;
    for (uint32_t i = 0; i < count; ++i) {
        uint32_t j = (i + 1) % count;
        signedArea += points[i * 2] * points[j * 2 + 1];
        signedArea -= points[j * 2] * points[i * 2 + 1];
    }
    // If CW winding (negative area), reverse index list so ear-clipping
    // always works with CCW assumption.
    bool isCW = signedArea < 0.0f;

    auto getX = [&](uint32_t idx) { return points[idx * 2]; };
    auto getY = [&](uint32_t idx) { return points[idx * 2 + 1]; };

    // Cross product of vectors (b-a) x (c-a)
    auto cross = [&](uint32_t a, uint32_t b, uint32_t c) -> float {
        return (getX(b) - getX(a)) * (getY(c) - getY(a)) -
               (getY(b) - getY(a)) * (getX(c) - getX(a));
    };

    // Check if point p is strictly inside triangle (a,b,c) using signed areas.
    // Uses an epsilon to exclude points that are on or very near edges — this is
    // critical for robustness when Bézier flattening produces near-collinear points.
    auto pointInTriangle = [&](uint32_t p, uint32_t a, uint32_t b, uint32_t c) -> bool {
        float d1 = (getX(p) - getX(b)) * (getY(a) - getY(b)) - (getX(a) - getX(b)) * (getY(p) - getY(b));
        float d2 = (getX(p) - getX(c)) * (getY(b) - getY(c)) - (getX(b) - getX(c)) * (getY(p) - getY(c));
        float d3 = (getX(p) - getX(a)) * (getY(c) - getY(a)) - (getX(c) - getX(a)) * (getY(p) - getY(a));
        // Use epsilon to treat on-edge / near-edge points as OUTSIDE.
        // This prevents near-collinear vertices from blocking valid ears.
        constexpr float kPitEps = 1e-3f;
        bool hasNeg = (d1 < -kPitEps) || (d2 < -kPitEps) || (d3 < -kPitEps);
        bool hasPos = (d1 >  kPitEps) || (d2 >  kPitEps) || (d3 >  kPitEps);
        return hasNeg && !hasPos || hasPos && !hasNeg;
    };

    uint32_t n = count;
    uint32_t maxIterations = n * n; // Safety limit
    uint32_t iterations = 0;

    while (n > 2 && iterations < maxIterations) {
        bool earFound = false;
        for (uint32_t i = 0; i < n; ++i) {
            uint32_t prev = (i + n - 1) % n;
            uint32_t next = (i + 1) % n;

            uint32_t a = indices[prev];
            uint32_t b = indices[i];
            uint32_t c = indices[next];

            float cp = cross(a, b, c);
            // For CCW polygon, an ear vertex has positive cross product.
            // Only treat near-zero cross products (truly collinear vertices from
            // Bézier flattening) as convex — never allow actually-concave vertices
            // through, as that produces overlapping triangles.
            constexpr float kConvexEps = 1e-7f;
            bool isConvex = isCW ? (cp < kConvexEps) : (cp > -kConvexEps);
            if (!isConvex) {
                ++iterations;
                continue;
            }

            // Check no other vertex is inside this triangle
            bool isEar = true;
            for (uint32_t j = 0; j < n; ++j) {
                if (j == prev || j == i || j == next) continue;
                if (pointInTriangle(indices[j], a, b, c)) {
                    isEar = false;
                    break;
                }
            }

            if (isEar) {
                outIndices.push_back(a);
                outIndices.push_back(b);
                outIndices.push_back(c);
                // Remove vertex i
                indices.erase(indices.begin() + i);
                --n;
                earFound = true;
                break;
            }
            ++iterations;
        }

        if (!earFound) {
            // Degenerate polygon or collinear points — two-pass fallback:
            //  Pass 1: find the most-convex vertex whose triangle contains no
            //          other polygon vertex (a valid ear the normal loop missed
            //          due to epsilon sensitivity).
            //  Pass 2: if every candidate has interior vertices, pick the
            //          most-convex one anyway and force-clip (last resort).
            if (n >= 3) {
                struct Candidate { uint32_t idx; float score; };
                std::vector<Candidate> candidates(n);
                for (uint32_t k = 0; k < n; ++k) {
                    uint32_t kp = (k + n - 1) % n;
                    uint32_t kn = (k + 1) % n;
                    float cp2 = cross(indices[kp], indices[k], indices[kn]);
                    candidates[k] = { k, isCW ? -cp2 : cp2 };
                }
                // Sort by convexity score (descending)
                std::sort(candidates.begin(), candidates.end(),
                    [](const Candidate& a, const Candidate& b) { return a.score > b.score; });

                bool clipped = false;
                // Pass 1 — try to find a valid (no interior vertices) candidate
                for (auto& cand : candidates) {
                    uint32_t ki = cand.idx;
                    uint32_t kp = (ki + n - 1) % n;
                    uint32_t kn = (ki + 1) % n;
                    uint32_t va = indices[kp], vb = indices[ki], vc = indices[kn];
                    float triArea = std::abs(cross(va, vb, vc));
                    if (triArea <= 1e-6f) continue;
                    // Check no other vertex lies inside this triangle
                    bool hasInterior = false;
                    for (uint32_t j = 0; j < n; ++j) {
                        if (j == kp || j == ki || j == kn) continue;
                        if (pointInTriangle(indices[j], va, vb, vc)) {
                            hasInterior = true;
                            break;
                        }
                    }
                    if (!hasInterior) {
                        outIndices.push_back(va);
                        outIndices.push_back(vb);
                        outIndices.push_back(vc);
                        indices.erase(indices.begin() + ki);
                        --n;
                        clipped = true;
                        break;
                    }
                }
                // Pass 2 — all candidates have interior vertices; force-clip
                // the most-convex one to guarantee progress.
                if (!clipped) {
                    auto& best = candidates[0];
                    uint32_t ki = best.idx;
                    uint32_t kp = (ki + n - 1) % n;
                    uint32_t kn = (ki + 1) % n;
                    float triArea = std::abs(cross(indices[kp], indices[ki], indices[kn]));
                    if (triArea > 1e-6f) {
                        outIndices.push_back(indices[kp]);
                        outIndices.push_back(indices[ki]);
                        outIndices.push_back(indices[kn]);
                    }
                    indices.erase(indices.begin() + ki);
                    --n;
                }
            } else {
                break;
            }
        }
    }

    return outIndices.size() >= 3;
}

// ============================================================================
// Compound polygon helpers (bridge-edge algorithm for holes)
// ============================================================================

/// Compute the signed area of a contour. Positive = CCW, negative = CW.
inline float ContourSignedArea(const Contour& c) {
    float area = 0.0f;
    uint32_t n = c.VertexCount();
    for (uint32_t i = 0; i < n; ++i) {
        uint32_t j = (i + 1) % n;
        area += c.X(i) * c.Y(j);
        area -= c.X(j) * c.Y(i);
    }
    return area * 0.5f;
}

/// Check if a point is inside a contour (ray-casting / winding number).
inline bool PointInsideContour(float px, float py, const Contour& c) {
    uint32_t n = c.VertexCount();
    bool inside = false;
    for (uint32_t i = 0, j = n - 1; i < n; j = i++) {
        float xi = c.X(i), yi = c.Y(i);
        float xj = c.X(j), yj = c.Y(j);
        if (((yi > py) != (yj > py)) &&
            (px < (xj - xi) * (py - yi) / (yj - yi) + xi)) {
            inside = !inside;
        }
    }
    return inside;
}

/// Check if edge (p1→p2) intersects edge (p3→p4), not counting shared endpoints.
inline bool EdgesIntersect(float p1x, float p1y, float p2x, float p2y,
                           float p3x, float p3y, float p4x, float p4y) {
    float d1x = p2x - p1x, d1y = p2y - p1y;
    float d2x = p4x - p3x, d2y = p4y - p3y;
    float denom = d1x * d2y - d1y * d2x;
    if (std::abs(denom) < 1e-10f) return false; // parallel
    float t = ((p3x - p1x) * d2y - (p3y - p1y) * d2x) / denom;
    float u = ((p3x - p1x) * d1y - (p3y - p1y) * d1x) / denom;
    // Strict interior intersection (exclude endpoints to allow bridge edges to touch contours)
    return (t > 1e-6f && t < 1.0f - 1e-6f && u > 1e-6f && u < 1.0f - 1e-6f);
}

/// Check if a bridge edge from (ax,ay) to (bx,by) intersects any edge of the given contours.
inline bool BridgeIntersectsContours(float ax, float ay, float bx, float by,
                                     const std::vector<Contour>& contours) {
    for (auto& c : contours) {
        uint32_t n = c.VertexCount();
        for (uint32_t i = 0; i < n; ++i) {
            uint32_t j = (i + 1) % n;
            if (EdgesIntersect(ax, ay, bx, by, c.X(i), c.Y(i), c.X(j), c.Y(j)))
                return true;
        }
    }
    return false;
}

/// Merge holes into an outer contour using bridge edges.
/// The resulting polygon is a single simple polygon with zero-width cuts.
/// outer must be CCW; holes must be CW.
inline Contour MergeContoursWithBridges(const Contour& outer, const std::vector<Contour>& holes) {
    if (holes.empty()) return outer;

    // Copy outer contour into result
    Contour result = outer;

    // Sort holes by their rightmost X coordinate (descending) for stable bridge insertion
    struct HoleRef {
        uint32_t idx;
        float maxX;
        uint32_t maxXVertex; // vertex index within the hole with max X
    };
    std::vector<HoleRef> sortedHoles(holes.size());
    for (uint32_t h = 0; h < (uint32_t)holes.size(); ++h) {
        float mx = -std::numeric_limits<float>::max();
        uint32_t mxi = 0;
        for (uint32_t v = 0; v < holes[h].VertexCount(); ++v) {
            if (holes[h].X(v) > mx) {
                mx = holes[h].X(v);
                mxi = v;
            }
        }
        sortedHoles[h] = { h, mx, mxi };
    }
    std::sort(sortedHoles.begin(), sortedHoles.end(),
              [](const HoleRef& a, const HoleRef& b) { return a.maxX > b.maxX; });

    for (auto& hr : sortedHoles) {
        auto& hole = holes[hr.idx];
        uint32_t holeVertex = hr.maxXVertex;
        float hx = hole.X(holeVertex), hy = hole.Y(holeVertex);

        // Find the closest visible vertex on the result contour
        uint32_t bestVertex = 0;
        float bestDist = std::numeric_limits<float>::max();

        uint32_t rn = result.VertexCount();
        for (uint32_t i = 0; i < rn; ++i) {
            float rx = result.X(i), ry = result.Y(i);
            float dx = rx - hx, dy = ry - hy;
            float dist = dx * dx + dy * dy;
            if (dist < bestDist) {
                // Check that the bridge edge doesn't intersect any contour edge
                bool intersects = false;
                for (uint32_t e = 0; e < rn; ++e) {
                    uint32_t enext = (e + 1) % rn;
                    if (e == i || enext == i) continue;
                    if (EdgesIntersect(hx, hy, rx, ry,
                                       result.X(e), result.Y(e),
                                       result.X(enext), result.Y(enext))) {
                        intersects = true;
                        break;
                    }
                }
                if (!intersects) {
                    bestDist = dist;
                    bestVertex = i;
                }
            }
        }

        // Insert hole into result at bestVertex with a bridge edge:
        // ... outerBefore, result[bestVertex], hole[holeVertex], hole[holeVertex+1], ...,
        //     hole[holeVertex-1], hole[holeVertex], result[bestVertex], outerAfter ...
        uint32_t hn = hole.VertexCount();
        std::vector<float> merged;
        merged.reserve(result.points.size() + hole.points.size() + 4);

        // Copy result up to and including bestVertex
        for (uint32_t i = 0; i <= bestVertex; ++i) {
            merged.push_back(result.X(i));
            merged.push_back(result.Y(i));
        }
        // Insert hole starting from holeVertex, wrapping around
        for (uint32_t i = 0; i <= hn; ++i) {
            uint32_t vi = (holeVertex + i) % hn;
            merged.push_back(hole.X(vi));
            merged.push_back(hole.Y(vi));
        }
        // Duplicate bridge endpoint to close the cut
        merged.push_back(result.X(bestVertex));
        merged.push_back(result.Y(bestVertex));
        // Copy rest of result
        for (uint32_t i = bestVertex + 1; i < rn; ++i) {
            merged.push_back(result.X(i));
            merged.push_back(result.Y(i));
        }

        result.points = std::move(merged);
    }

    return result;
}

// ============================================================================
// Path simplification — remove duplicate and near-collinear vertices
// ============================================================================

/// Remove near-duplicate vertices (distance < epsilon) from a contour.
/// Returns a new contour with redundant vertices removed.
inline Contour RemoveDuplicateVertices(const Contour& c, float epsilon = 0.1f) {
    uint32_t n = c.VertexCount();
    if (n < 2) return c;

    float eps2 = epsilon * epsilon;
    Contour result;
    result.points.reserve(c.points.size());
    result.points.push_back(c.X(0));
    result.points.push_back(c.Y(0));

    for (uint32_t i = 1; i < n; ++i) {
        float dx = c.X(i) - result.points[result.points.size() - 2];
        float dy = c.Y(i) - result.points[result.points.size() - 1];
        if (dx * dx + dy * dy >= eps2) {
            result.points.push_back(c.X(i));
            result.points.push_back(c.Y(i));
        }
    }

    // Also check last vs first
    if (result.VertexCount() >= 2) {
        float dx = result.X(result.VertexCount() - 1) - result.X(0);
        float dy = result.Y(result.VertexCount() - 1) - result.Y(0);
        if (dx * dx + dy * dy < eps2) {
            result.points.pop_back();
            result.points.pop_back();
        }
    }

    return result;
}

/// Remove near-collinear vertices where the cross product of adjacent edges
/// is below a threshold. This reduces vertex count without changing shape.
/// Uses iterative single-vertex removal: each pass removes only the vertex
/// with the smallest cross product, then recomputes neighbors, preventing
/// cascading shape distortion from greedy batch deletion.
inline Contour RemoveCollinearVertices(const Contour& c, float areaThreshold = 0.5f) {
    uint32_t n = c.VertexCount();
    if (n <= 3) return c;

    // Work on a mutable copy of indices into the original points
    std::vector<uint32_t> live;
    live.resize(n);
    for (uint32_t i = 0; i < n; ++i) live[i] = i;

    auto crossAt = [&](const std::vector<uint32_t>& idx, uint32_t pos) -> float {
        uint32_t sz = (uint32_t)idx.size();
        uint32_t prev = (pos + sz - 1) % sz;
        uint32_t next = (pos + 1) % sz;
        uint32_t ip = idx[prev], ic = idx[pos], in_ = idx[next];
        float ax = c.X(ic) - c.X(ip), ay = c.Y(ic) - c.Y(ip);
        float bx = c.X(in_) - c.X(ic), by = c.Y(in_) - c.Y(ic);
        return std::abs(ax * by - ay * bx);
    };

    // Iteratively remove the most-collinear vertex until none remain below
    // the threshold or we'd drop below 3 vertices.
    while (live.size() > 3) {
        float minCross = std::numeric_limits<float>::max();
        uint32_t minIdx = 0;
        for (uint32_t i = 0; i < (uint32_t)live.size(); ++i) {
            float cr = crossAt(live, i);
            if (cr < minCross) {
                minCross = cr;
                minIdx = i;
            }
        }
        if (minCross >= areaThreshold) break; // all remaining vertices are significant
        live.erase(live.begin() + minIdx);
    }

    if (live.size() < 3) return c;

    Contour result;
    result.points.reserve(live.size() * 2);
    for (uint32_t idx : live) {
        result.points.push_back(c.X(idx));
        result.points.push_back(c.Y(idx));
    }
    return result;
}

/// Full simplification pipeline: remove duplicates, then collinear vertices.
inline Contour SimplifyContour(const Contour& c, float dupEpsilon = 0.1f, float collinearThreshold = 0.5f) {
    Contour step1 = RemoveDuplicateVertices(c, dupEpsilon);
    if (step1.VertexCount() < 3) return c;
    Contour step2 = RemoveCollinearVertices(step1, collinearThreshold);
    if (step2.VertexCount() < 3) return c;
    return step2;
}

// ============================================================================
// NonZero fill rule support via winding number triangulation
// ============================================================================

/// Compute winding number of point (px,py) with respect to all contours.
inline int ComputeWindingNumber(float px, float py, const std::vector<Contour>& contours) {
    int winding = 0;
    for (auto& c : contours) {
        uint32_t n = c.VertexCount();
        for (uint32_t i = 0; i < n; ++i) {
            uint32_t j = (i + 1) % n;
            float yi = c.Y(i), yj = c.Y(j);
            if (yi <= py) {
                if (yj > py) {
                    // Upward crossing
                    float cross = (c.X(j) - c.X(i)) * (py - yi) - (px - c.X(i)) * (yj - yi);
                    if (cross > 0) ++winding;
                }
            } else {
                if (yj <= py) {
                    // Downward crossing
                    float cross = (c.X(j) - c.X(i)) * (py - yi) - (px - c.X(i)) * (yj - yi);
                    if (cross < 0) --winding;
                }
            }
        }
    }
    return winding;
}

// ============================================================================
// Y-Monotone polygon detection and triangulation
// ============================================================================

/// Check if a polygon is Y-monotone. A polygon is Y-monotone if any horizontal
/// line intersects its boundary at most twice — equivalently, Y changes direction
/// at most twice when traversing the boundary (one local min, one local max).
inline bool IsYMonotone(const float* points, uint32_t count) {
    if (count < 3) return false;

    auto getY = [&](uint32_t i) { return points[i * 2 + 1]; };

    // Count direction changes in Y along the boundary (including wrap-around).
    // A polygon is Y-monotone if it has exactly 2 direction changes.
    int dirChanges = 0;
    int firstDir = 0;  // first non-zero direction
    int prevDir = 0;   // -1 = decreasing, +1 = increasing, 0 = flat
    for (uint32_t i = 0; i < count; ++i) {
        uint32_t next = (i + 1) % count;
        float dy = getY(next) - getY(i);
        int dir = (dy > 1e-6f) ? 1 : (dy < -1e-6f) ? -1 : 0;
        if (dir != 0) {
            if (firstDir == 0) firstDir = dir;
            if (prevDir != 0 && dir != prevDir) ++dirChanges;
            prevDir = dir;
        }
    }
    // Check wrap-around: last non-zero direction vs first non-zero direction
    if (prevDir != 0 && firstDir != 0 && prevDir != firstDir)
        ++dirChanges;

    return dirChanges <= 2;
}

/// Triangulate a Y-monotone polygon in O(n) time using the stack-based algorithm.
/// The polygon vertices are in order (CW or CCW). Output: triangle index triples.
inline bool TriangulateMonotone(const float* points, uint32_t count,
                                 std::vector<uint32_t>& outIndices) {
    outIndices.clear();
    if (count < 3) return false;
    if (count == 3) {
        outIndices = {0, 1, 2};
        return true;
    }

    auto getX = [&](uint32_t i) { return points[i * 2]; };
    auto getY = [&](uint32_t i) { return points[i * 2 + 1]; };

    // Find top and bottom vertices (min and max Y)
    uint32_t topIdx = 0, botIdx = 0;
    for (uint32_t i = 1; i < count; ++i) {
        if (getY(i) < getY(topIdx) || (getY(i) == getY(topIdx) && getX(i) < getX(topIdx)))
            topIdx = i;
        if (getY(i) > getY(botIdx) || (getY(i) == getY(botIdx) && getX(i) > getX(botIdx)))
            botIdx = i;
    }

    // Split into left chain (top → bottom going forward) and right chain (top → bottom going backward)
    // chain[i] = 0 for left chain, 1 for right chain
    std::vector<uint8_t> chain(count, 0);

    // Walk forward from topIdx to botIdx → left chain
    {
        uint32_t v = topIdx;
        while (v != botIdx) {
            chain[v] = 0; // left
            v = (v + 1) % count;
        }
    }
    // Walk backward from topIdx to botIdx → right chain
    {
        uint32_t v = topIdx;
        while (v != botIdx) {
            chain[v] = 1; // right (overwrite topIdx to right is OK, we'll set it below)
            v = (v + count - 1) % count;
        }
    }
    chain[topIdx] = 2; // top (on both chains)
    chain[botIdx] = 2; // bottom (on both chains)

    // Sort vertices by Y (ascending), break ties by X
    std::vector<uint32_t> sorted(count);
    std::iota(sorted.begin(), sorted.end(), 0u);
    std::sort(sorted.begin(), sorted.end(), [&](uint32_t a, uint32_t b) {
        float ya = getY(a), yb = getY(b);
        if (std::abs(ya - yb) > 1e-6f) return ya < yb;
        return getX(a) < getX(b);
    });

    // Stack-based monotone triangulation
    auto cross2d = [&](uint32_t a, uint32_t b, uint32_t c) -> float {
        return (getX(b) - getX(a)) * (getY(c) - getY(a)) -
               (getY(b) - getY(a)) * (getX(c) - getX(a));
    };

    std::vector<uint32_t> stack;
    stack.push_back(sorted[0]);
    stack.push_back(sorted[1]);

    for (uint32_t i = 2; i < count; ++i) {
        uint32_t vi = sorted[i];
        uint32_t st = stack.back();

        // The bottom vertex (last in sorted order) must flush all remaining
        // stack vertices — treat it as "different chain" to force a full flush.
        bool isBottom = (i == count - 1);

        // Determine if vi and stack top are on the same chain.
        // chain==2 means top/bottom (on both chains). For the bottom vertex,
        // force "different chain" to ensure complete triangulation.
        bool sameChain = false;
        if (!isBottom) {
            if (chain[vi] == 2 || chain[st] == 2)
                sameChain = true; // top vertex matches either chain
            else
                sameChain = (chain[vi] == chain[st]);
        }

        if (!sameChain) {
            // Different chains (or bottom vertex) — pop all from stack and create triangles
            while (stack.size() > 1) {
                uint32_t s1 = stack.back(); stack.pop_back();
                uint32_t s2 = stack.back();
                outIndices.push_back(vi);
                outIndices.push_back(s1);
                outIndices.push_back(s2);
            }
            stack.pop_back();
            if (!isBottom) {
                stack.push_back(sorted[i - 1]);
                stack.push_back(vi);
            }
        } else {
            // Same chain — pop while we can form valid triangles
            uint32_t last = stack.back(); stack.pop_back();
            while (!stack.empty()) {
                uint32_t prev = stack.back();
                float cp = cross2d(vi, last, prev);
                // For left chain, valid if cp > 0 (CCW triangle)
                // For right chain, valid if cp < 0
                bool valid = (chain[vi] == 1) ? (cp < -1e-6f) : (cp > 1e-6f);
                if (!valid) break;
                outIndices.push_back(vi);
                outIndices.push_back(last);
                outIndices.push_back(prev);
                last = prev;
                stack.pop_back();
            }
            stack.push_back(last);
            stack.push_back(vi);
        }
    }

    return outIndices.size() >= 3;
}

// ============================================================================
// Convex polygon detection
// ============================================================================

/// Check if a polygon is convex by verifying all cross products have the same sign.
inline bool IsConvexPolygonFast(const float* points, uint32_t count) {
    if (count < 3) return false;
    if (count == 3) return true;

    auto getX = [&](uint32_t i) { return points[i * 2]; };
    auto getY = [&](uint32_t i) { return points[i * 2 + 1]; };

    bool hasPos = false, hasNeg = false;
    for (uint32_t i = 0; i < count; ++i) {
        uint32_t j = (i + 1) % count;
        uint32_t k = (i + 2) % count;
        float cp = (getX(j) - getX(i)) * (getY(k) - getY(j)) -
                   (getY(j) - getY(i)) * (getX(k) - getX(j));
        if (cp > 1e-6f) hasPos = true;
        if (cp < -1e-6f) hasNeg = true;
        if (hasPos && hasNeg) return false;
    }
    return true;
}

// ============================================================================
// Robust polygon triangulation — master dispatch
// ============================================================================

/// Triangulate a polygon using the best available algorithm:
/// 1. Trivial (count <= 3): direct
/// 2. Convex: fan triangulation O(n)
/// 3. Small polygons (count <= 12): ear-clipping directly (most reliable at small sizes)
/// 4. Y-Monotone: stack-based O(n)
/// 5. General concave: improved ear-clipping O(n²)
/// Falls back gracefully through each level.
inline bool TriangulatePolygonRobust(const float* points, uint32_t count,
                                      std::vector<uint32_t>& outIndices) {
    outIndices.clear();
    if (count < 3) return false;
    if (count == 3) {
        outIndices = {0, 1, 2};
        return true;
    }

    // Always use ear-clipping — no convex fan or Y-monotone shortcuts.
    // Fan triangulation can produce pixel gaps when IsConvexPolygon has
    // false positives (near-collinear vertices from curve flattening).
    // Y-monotone can produce gaps on small shapes due to chain assignment
    // precision. Ear-clipping is robust for all polygon types.
    return TriangulatePolygon(points, count, outIndices);
}

// ============================================================================
// Compound path triangulation
// ============================================================================

/// Triangulate a compound path with multiple contours respecting fill rules.
/// fillRule: 0 = EvenOdd, 1 = NonZero (Winding).
/// All contours are in their original winding order.
/// Returns flattened triangle vertices as (x,y) pairs.
inline bool TriangulateCompoundPath(const std::vector<Contour>& contours,
                                     int32_t fillRule,
                                     std::vector<float>& outVertices) {
    outVertices.clear();
    if (contours.empty()) return false;

    // Single contour: simplify (only for large contours) and triangulate
    if (contours.size() == 1) {
        // Only simplify contours with many vertices — small shapes (arrows,
        // icons, scrollbar glyphs) have few vertices where each one matters.
        const auto& src = contours[0];
        Contour simplified = (src.VertexCount() > 16)
            ? SimplifyContour(src) : src;
        uint32_t count = simplified.VertexCount();
        if (count < 3) return false;
        std::vector<uint32_t> indices;
        if (!TriangulatePolygonRobust(simplified.points.data(), count, indices)) return false;
        outVertices.reserve(indices.size() * 2);
        for (uint32_t idx : indices) {
            outVertices.push_back(simplified.X(idx));
            outVertices.push_back(simplified.Y(idx));
        }
        return true;
    }

    // Classify contours as outer (CCW, positive area) or hole (CW, negative area).
    // For EvenOdd, we use bridge edges to merge holes into outers.
    // For NonZero, contour winding direction determines fill.

    struct ContourInfo {
        uint32_t index;
        float signedArea;
        bool isOuter;   // positive signed area = CCW = outer
        int32_t parentOuter; // index into outers array, or -1
    };
    std::vector<ContourInfo> info(contours.size());
    for (uint32_t i = 0; i < (uint32_t)contours.size(); ++i) {
        info[i].index = i;
        info[i].signedArea = ContourSignedArea(contours[i]);
        info[i].isOuter = (info[i].signedArea >= 0);
        info[i].parentOuter = -1;
    }

    // Collect outer contours and assign holes to their parent outer contour
    std::vector<uint32_t> outerIndices;
    std::vector<uint32_t> holeIndices;
    for (uint32_t i = 0; i < (uint32_t)contours.size(); ++i) {
        if (info[i].isOuter) {
            outerIndices.push_back(i);
        } else {
            holeIndices.push_back(i);
        }
    }

    // If no outer contours, treat the first contour as outer (reverse it)
    if (outerIndices.empty()) {
        outerIndices.push_back(0);
        info[0].isOuter = true;
        // Remove from holes
        holeIndices.erase(
            std::remove(holeIndices.begin(), holeIndices.end(), 0u),
            holeIndices.end());
    }

    // For each hole, find which outer contour contains it
    for (uint32_t hi : holeIndices) {
        auto& hole = contours[hi];
        // Use first vertex of hole as test point
        float tx = hole.X(0), ty = hole.Y(0);
        for (uint32_t oi : outerIndices) {
            if (PointInsideContour(tx, ty, contours[oi])) {
                info[hi].parentOuter = static_cast<int32_t>(oi);
                break;
            }
        }
    }

    // For each outer contour, collect its holes and merge using bridge edges
    for (uint32_t oi : outerIndices) {
        const auto& outerSrc = contours[oi];
        Contour outer = (outerSrc.VertexCount() > 16)
            ? SimplifyContour(outerSrc) : outerSrc;
        // Ensure outer is CCW
        if (ContourSignedArea(outer) < 0) {
            std::vector<float> rev;
            rev.reserve(outer.points.size());
            for (int32_t i = (int32_t)outer.VertexCount() - 1; i >= 0; --i) {
                rev.push_back(outer.X(i));
                rev.push_back(outer.Y(i));
            }
            outer.points = std::move(rev);
        }

        std::vector<Contour> myHoles;
        for (uint32_t hi : holeIndices) {
            if (info[hi].parentOuter == (int32_t)oi) {
                const auto& holeSrc = contours[hi];
                Contour hole = (holeSrc.VertexCount() > 16)
                    ? SimplifyContour(holeSrc) : holeSrc;
                // Ensure hole is CW
                if (ContourSignedArea(hole) > 0) {
                    std::vector<float> rev;
                    rev.reserve(hole.points.size());
                    for (int32_t i = (int32_t)hole.VertexCount() - 1; i >= 0; --i) {
                        rev.push_back(hole.X(i));
                        rev.push_back(hole.Y(i));
                    }
                    hole.points = std::move(rev);
                }
                myHoles.push_back(std::move(hole));
            }
        }

        // Merge holes into outer contour
        Contour merged = MergeContoursWithBridges(outer, myHoles);
        uint32_t count = merged.VertexCount();
        if (count < 3) continue;

        // Triangulate the merged polygon using robust dispatch
        std::vector<uint32_t> indices;
        if (!TriangulatePolygonRobust(merged.points.data(), count, indices)) continue;

        // For NonZero fill rule, verify each triangle's centroid has non-zero winding
        if (fillRule == 1) {
            for (uint32_t t = 0; t + 2 < (uint32_t)indices.size(); t += 3) {
                float cx = (merged.X(indices[t]) + merged.X(indices[t+1]) + merged.X(indices[t+2])) / 3.0f;
                float cy = (merged.Y(indices[t]) + merged.Y(indices[t+1]) + merged.Y(indices[t+2])) / 3.0f;
                int wn = ComputeWindingNumber(cx, cy, contours);
                if (wn != 0) {
                    outVertices.push_back(merged.X(indices[t]));
                    outVertices.push_back(merged.Y(indices[t]));
                    outVertices.push_back(merged.X(indices[t+1]));
                    outVertices.push_back(merged.Y(indices[t+1]));
                    outVertices.push_back(merged.X(indices[t+2]));
                    outVertices.push_back(merged.Y(indices[t+2]));
                }
            }
        } else {
            // EvenOdd: all triangles from the merged (with holes removed) polygon are valid
            for (uint32_t idx : indices) {
                outVertices.push_back(merged.X(idx));
                outVertices.push_back(merged.Y(idx));
            }
        }
    }

    // Handle orphan holes (not inside any outer contour) - treat as standalone paths
    for (uint32_t hi : holeIndices) {
        if (info[hi].parentOuter < 0) {
            const auto& orphanSrc = contours[hi];
            Contour simplified = (orphanSrc.VertexCount() > 16)
                ? SimplifyContour(orphanSrc) : orphanSrc;
            uint32_t count = simplified.VertexCount();
            if (count < 3) continue;
            std::vector<uint32_t> indices;
            if (!TriangulatePolygonRobust(simplified.points.data(), count, indices)) continue;
            for (uint32_t idx : indices) {
                outVertices.push_back(simplified.X(idx));
                outVertices.push_back(simplified.Y(idx));
            }
        }
    }

    return outVertices.size() >= 6;
}

// ============================================================================
// Bezier flattening
// ============================================================================

// Flatten a cubic bezier curve into line segments using De Casteljau subdivision.
// Appends resulting points (x,y pairs) to outPoints. Does NOT include the start point.
// p0..p3 are control points (x,y each). tolerance is the max distance for flattening.
// depth limits recursion to prevent stack overflow on pathological curves.
inline void FlattenCubicBezier(float p0x, float p0y, float p1x, float p1y,
                                float p2x, float p2y, float p3x, float p3y,
                                std::vector<float>& outPoints, float tolerance = 1.0f,
                                int depth = 0) {
    // Check if the curve is flat enough by measuring control point deviation
    float dx = p3x - p0x;
    float dy = p3y - p0y;
    float len2 = dx * dx + dy * dy;

    if (len2 < 1e-6f || depth >= 32) {
        // Degenerate or max depth reached: output endpoint
        outPoints.push_back(p3x);
        outPoints.push_back(p3y);
        return;
    }

    float invLen = 1.0f / std::sqrt(len2);
    float nx = -dy * invLen;
    float ny = dx * invLen;

    float d1 = std::abs(nx * (p1x - p0x) + ny * (p1y - p0y));
    float d2 = std::abs(nx * (p2x - p0x) + ny * (p2y - p0y));

    if (d1 + d2 <= tolerance) {
        // Flat enough - output endpoint
        outPoints.push_back(p3x);
        outPoints.push_back(p3y);
        return;
    }

    // Subdivide at t=0.5
    float m01x = (p0x + p1x) * 0.5f, m01y = (p0y + p1y) * 0.5f;
    float m12x = (p1x + p2x) * 0.5f, m12y = (p1y + p2y) * 0.5f;
    float m23x = (p2x + p3x) * 0.5f, m23y = (p2y + p3y) * 0.5f;
    float m012x = (m01x + m12x) * 0.5f, m012y = (m01y + m12y) * 0.5f;
    float m123x = (m12x + m23x) * 0.5f, m123y = (m12y + m23y) * 0.5f;
    float midx = (m012x + m123x) * 0.5f, midy = (m012y + m123y) * 0.5f;

    FlattenCubicBezier(p0x, p0y, m01x, m01y, m012x, m012y, midx, midy, outPoints, tolerance, depth + 1);
    FlattenCubicBezier(midx, midy, m123x, m123y, m23x, m23y, p3x, p3y, outPoints, tolerance, depth + 1);
}

// Flatten a quadratic bezier curve using direct recursive subdivision.
// Uses the same adaptive tolerance approach as the reference implementation:
// measures distance of control point to the chord (p0→p2) and subdivides
// until flat enough, avoiding the precision loss of degree elevation.
inline void FlattenQuadraticBezier(float p0x, float p0y, float p1x, float p1y,
                                    float p2x, float p2y,
                                    std::vector<float>& outPoints, float tolerance = 1.0f,
                                    int depth = 0) {
    constexpr int kMaxDepth = 16;

    // Measure distance of control point from chord (p0→p2)
    float dx = p2x - p0x;
    float dy = p2y - p0y;
    float len2 = dx * dx + dy * dy;

    if (len2 < 1e-6f || depth >= kMaxDepth) {
        outPoints.push_back(p2x);
        outPoints.push_back(p2y);
        return;
    }

    float invLen = 1.0f / std::sqrt(len2);
    float nx = -dy * invLen;
    float ny = dx * invLen;
    float dist = std::abs(nx * (p1x - p0x) + ny * (p1y - p0y));

    if (dist <= tolerance) {
        outPoints.push_back(p2x);
        outPoints.push_back(p2y);
        return;
    }

    // De Casteljau subdivision at t=0.5
    float m01x = (p0x + p1x) * 0.5f, m01y = (p0y + p1y) * 0.5f;
    float m12x = (p1x + p2x) * 0.5f, m12y = (p1y + p2y) * 0.5f;
    float midx = (m01x + m12x) * 0.5f, midy = (m01y + m12y) * 0.5f;

    FlattenQuadraticBezier(p0x, p0y, m01x, m01y, midx, midy, outPoints, tolerance, depth + 1);
    FlattenQuadraticBezier(midx, midy, m12x, m12y, p2x, p2y, outPoints, tolerance, depth + 1);
}

// ============================================================================
// SVG elliptical arc flattening
// ============================================================================

// Helper: compute the angle of a 2D vector.
inline float VectorAngle2D(float ux, float uy, float vx, float vy) {
    float dot = ux * vx + uy * vy;
    float cross = ux * vy - uy * vx;
    float lenU = std::sqrt(ux * ux + uy * uy);
    float lenV = std::sqrt(vx * vx + vy * vy);
    float denom = lenU * lenV;
    if (denom < 1e-10f) return 0.0f;
    float cosVal = std::clamp(dot / denom, -1.0f, 1.0f);
    float angle = std::acos(cosVal);
    if (cross < 0.0f) angle = -angle;
    return angle;
}

// Helper: compute adaptive step count for an arc.
inline int ComputeArcStepCount(float radius, float sweepAngle, float tolerance) {
    float r = std::max(radius, 1e-3f);
    float tol = std::max(tolerance, 1e-3f);
    float c = std::clamp(1.0f - tol / r, -1.0f, 1.0f);
    float maxStep = 2.0f * std::acos(c);
    if (!std::isfinite(maxStep) || maxStep < 0.05f) maxStep = 0.05f;
    return std::max(1, static_cast<int>(std::ceil(std::abs(sweepAngle) / maxStep)));
}

// Flatten an SVG elliptical arc (endpoint parameterization) into line segments.
// Appends resulting points (x,y pairs) to outPoints. Does NOT include the start point.
// Follows the SVG spec F.6.5-F.6.6 for endpoint-to-center conversion.
inline void FlattenSvgArc(float startX, float startY, float endX, float endY,
                           float rx, float ry, float xAxisRotationDegrees,
                           bool largeArc, bool sweep,
                           std::vector<float>& outPoints, float tolerance = 1.0f) {
    constexpr float kPi = 3.14159265358979323846f;
    constexpr float kEps = 1e-5f;

    // Degenerate cases
    if ((std::abs(startX - endX) < kEps && std::abs(startY - endY) < kEps) ||
        rx <= kEps || ry <= kEps) {
        outPoints.push_back(endX);
        outPoints.push_back(endY);
        return;
    }

    rx = std::abs(rx);
    ry = std::abs(ry);

    float phi = xAxisRotationDegrees * kPi / 180.0f;
    float cosPhi = std::cos(phi);
    float sinPhi = std::sin(phi);

    float dx = (startX - endX) * 0.5f;
    float dy = (startY - endY) * 0.5f;

    float x1p = cosPhi * dx + sinPhi * dy;
    float y1p = -sinPhi * dx + cosPhi * dy;

    float rxSq = rx * rx;
    float rySq = ry * ry;
    float x1pSq = x1p * x1p;
    float y1pSq = y1p * y1p;

    // Ensure radii are large enough
    float radiiCheck = x1pSq / rxSq + y1pSq / rySq;
    if (radiiCheck > 1.0f) {
        float scale = std::sqrt(radiiCheck);
        rx *= scale;
        ry *= scale;
        rxSq = rx * rx;
        rySq = ry * ry;
    }

    // Compute center point
    float numerator = rxSq * rySq - rxSq * y1pSq - rySq * x1pSq;
    float denominator = rxSq * y1pSq + rySq * x1pSq;
    float sign = (largeArc == sweep) ? -1.0f : 1.0f;
    float factor = (denominator <= kEps) ? 0.0f : sign * std::sqrt(std::max(0.0f, numerator / denominator));

    float cxp = factor * (rx * y1p / ry);
    float cyp = factor * (-ry * x1p / rx);

    float cx = cosPhi * cxp - sinPhi * cyp + (startX + endX) * 0.5f;
    float cy = sinPhi * cxp + cosPhi * cyp + (startY + endY) * 0.5f;

    // Compute start and sweep angles
    float v1x = (x1p - cxp) / rx, v1y = (y1p - cyp) / ry;
    float v2x = (-x1p - cxp) / rx, v2y = (-y1p - cyp) / ry;

    float startAngle = VectorAngle2D(1.0f, 0.0f, v1x, v1y);
    float deltaAngle = VectorAngle2D(v1x, v1y, v2x, v2y);

    if (!sweep && deltaAngle > 0.0f) {
        deltaAngle -= 2.0f * kPi;
    } else if (sweep && deltaAngle < 0.0f) {
        deltaAngle += 2.0f * kPi;
    }

    // Generate points along the arc
    int steps = ComputeArcStepCount(std::max(rx, ry), deltaAngle, tolerance);
    for (int i = 1; i <= steps; ++i) {
        float t = static_cast<float>(i) / static_cast<float>(steps);
        float angle = startAngle + deltaAngle * t;
        float x = cosPhi * (rx * std::cos(angle)) - sinPhi * (ry * std::sin(angle)) + cx;
        float y = sinPhi * (rx * std::cos(angle)) + cosPhi * (ry * std::sin(angle)) + cy;
        outPoints.push_back(x);
        outPoints.push_back(y);
    }
}

// ============================================================================
// Path command tags
// ============================================================================

// Command encoding format:
//   tag 0 = LineTo       [0, x, y]                                       (3 floats)
//   tag 1 = CubicBezierTo [1, cp1x, cp1y, cp2x, cp2y, ex, ey]           (7 floats)
//   tag 2 = MoveTo        [2, x, y]                                       (3 floats)
//   tag 3 = QuadBezierTo  [3, cpx, cpy, ex, ey]                           (5 floats)
//   tag 4 = ArcTo         [4, ex, ey, rx, ry, xRotDeg, largeArc, sweep]   (8 floats)
//   tag 5 = ClosePath     [5]                                              (1 float)

constexpr int kTagLineTo = 0;
constexpr int kTagCubicTo = 1;
constexpr int kTagMoveTo = 2;
constexpr int kTagQuadTo = 3;
constexpr int kTagArcTo = 4;
constexpr int kTagClosePath = 5;

// ============================================================================
// Path command flattening
// ============================================================================

/// Dispatch a single path command starting at commands[i], appending flattened
/// points to outPoints and updating curX/curY. Returns the number of floats consumed,
/// or 0 if the command is unrecognized (caller should break).
inline uint32_t DispatchPathCommand(const float* commands, uint32_t i, uint32_t commandLength,
                                     float& curX, float& curY,
                                     std::vector<float>& outPoints, float tolerance) {
    int tag = static_cast<int>(commands[i]);
    switch (tag) {
    case kTagLineTo:
        if (i + 2 < commandLength) {
            float x = commands[i + 1], y = commands[i + 2];
            outPoints.push_back(x);
            outPoints.push_back(y);
            curX = x; curY = y;
            return 3;
        }
        return 0;
    case kTagCubicTo:
        if (i + 6 < commandLength) {
            float cp1x = commands[i + 1], cp1y = commands[i + 2];
            float cp2x = commands[i + 3], cp2y = commands[i + 4];
            float ex = commands[i + 5], ey = commands[i + 6];
            FlattenCubicBezier(curX, curY, cp1x, cp1y, cp2x, cp2y, ex, ey, outPoints, tolerance);
            curX = ex; curY = ey;
            return 7;
        }
        return 0;
    case kTagQuadTo:
        if (i + 4 < commandLength) {
            float cpx = commands[i + 1], cpy = commands[i + 2];
            float ex = commands[i + 3], ey = commands[i + 4];
            FlattenQuadraticBezier(curX, curY, cpx, cpy, ex, ey, outPoints, tolerance);
            curX = ex; curY = ey;
            return 5;
        }
        return 0;
    case kTagArcTo:
        if (i + 7 < commandLength) {
            float ex = commands[i + 1], ey = commands[i + 2];
            float rx = commands[i + 3], ry = commands[i + 4];
            float xRotDeg = commands[i + 5];
            bool largeArc = commands[i + 6] != 0.0f;
            bool sweep = commands[i + 7] != 0.0f;
            FlattenSvgArc(curX, curY, ex, ey, rx, ry, xRotDeg, largeArc, sweep, outPoints, tolerance);
            curX = ex; curY = ey;
            return 8;
        }
        return 0;
    default:
        return 0;
    }
}

// Flatten a path command buffer into a polygon (array of x,y pairs).
// Returns the flattened points including the start point.
inline std::vector<float> FlattenPathCommands(float startX, float startY,
                                               const float* commands, uint32_t commandLength,
                                               float tolerance = 1.0f) {
    std::vector<float> pts;
    pts.push_back(startX);
    pts.push_back(startY);

    float curX = startX, curY = startY;
    float subpathStartX = startX, subpathStartY = startY;
    uint32_t i = 0;
    while (i < commandLength) {
        int tag = static_cast<int>(commands[i]);
        if (tag == kTagMoveTo && i + 2 < commandLength) {
            float x = commands[i + 1], y = commands[i + 2];
            pts.push_back(x);
            pts.push_back(y);
            curX = x; curY = y;
            subpathStartX = x; subpathStartY = y;
            i += 3;
        } else if (tag == kTagClosePath) {
            // Close: add line back to subpath start if not already there
            if (std::abs(curX - subpathStartX) > 1e-4f || std::abs(curY - subpathStartY) > 1e-4f) {
                pts.push_back(subpathStartX);
                pts.push_back(subpathStartY);
            }
            curX = subpathStartX; curY = subpathStartY;
            i += 1;
        } else {
            uint32_t consumed = DispatchPathCommand(commands, i, commandLength, curX, curY, pts, tolerance);
            if (consumed == 0) break;
            i += consumed;
        }
    }
    return pts;
}

/// Flatten a path command buffer into multiple contours.
/// Returns a vector of Contour, each with its flattened points.
inline std::vector<Contour> FlattenPathToContours(float startX, float startY,
                                                    const float* commands, uint32_t commandLength,
                                                    float tolerance = 1.0f) {
    std::vector<Contour> contours;
    Contour current;
    current.points.push_back(startX);
    current.points.push_back(startY);
    bool contourClosed = false;

    float curX = startX, curY = startY;
    float subpathStartX = startX, subpathStartY = startY;
    uint32_t i = 0;
    while (i < commandLength) {
        int tag = static_cast<int>(commands[i]);
        if (tag == kTagMoveTo && i + 2 < commandLength) {
            // MoveTo: finish current contour and start a new one
            if (current.VertexCount() >= 2) {
                contours.push_back(std::move(current));
            }
            current = Contour();
            float x = commands[i + 1], y = commands[i + 2];
            current.points.push_back(x);
            current.points.push_back(y);
            curX = x; curY = y;
            subpathStartX = x; subpathStartY = y;
            contourClosed = false;
            i += 3;
        } else if (tag == kTagClosePath) {
            // Close: add line back to subpath start
            if (std::abs(curX - subpathStartX) > 1e-4f || std::abs(curY - subpathStartY) > 1e-4f) {
                current.points.push_back(subpathStartX);
                current.points.push_back(subpathStartY);
            }
            curX = subpathStartX; curY = subpathStartY;
            contourClosed = true;
            // Flush closed contour immediately
            if (current.VertexCount() >= 3) {
                contours.push_back(std::move(current));
            }
            current = Contour();
            i += 1;
        } else {
            uint32_t consumed = DispatchPathCommand(commands, i, commandLength, curX, curY, current.points, tolerance);
            if (consumed == 0) break;
            i += consumed;
        }
    }

    // Push last contour
    if (current.VertexCount() >= 2) {
        contours.push_back(std::move(current));
    }

    return contours;
}

// ============================================================================
// Gradient interpolation helpers for path triangles
// ============================================================================

/// Compute linear gradient color at a point (px, py) given gradient geometry.
/// startX/Y → endX/Y defines the gradient line.
/// Stops: array of (position, r, g, b, a) tuples.
/// Returns premultiplied linear-space RGBA.
struct GradientColor { float r, g, b, a; };

inline GradientColor SampleLinearGradient(float px, float py,
                                           float startX, float startY, float endX, float endY,
                                           const float* stops, uint32_t stopCount) {
    // Project point onto gradient line
    float dx = endX - startX, dy = endY - startY;
    float len2 = dx * dx + dy * dy;
    float t = 0.0f;
    if (len2 > 1e-6f) {
        t = ((px - startX) * dx + (py - startY) * dy) / len2;
    }
    t = (std::max)(0.0f, (std::min)(1.0f, t));

    // Find the two surrounding stops and interpolate
    // stops layout: [pos0, r0, g0, b0, a0, pos1, r1, g1, b1, a1, ...]
    if (stopCount == 0) return { 0, 0, 0, 0 };
    if (stopCount == 1 || t <= stops[0]) {
        return { stops[1], stops[2], stops[3], stops[4] };
    }
    for (uint32_t i = 1; i < stopCount; ++i) {
        float pos = stops[i * 5];
        if (t <= pos || i == stopCount - 1) {
            float prevPos = stops[(i - 1) * 5];
            float span = pos - prevPos;
            float frac = (span > 1e-6f) ? (t - prevPos) / span : 0.0f;
            frac = (std::max)(0.0f, (std::min)(1.0f, frac));
            return {
                stops[(i-1)*5+1] + frac * (stops[i*5+1] - stops[(i-1)*5+1]),
                stops[(i-1)*5+2] + frac * (stops[i*5+2] - stops[(i-1)*5+2]),
                stops[(i-1)*5+3] + frac * (stops[i*5+3] - stops[(i-1)*5+3]),
                stops[(i-1)*5+4] + frac * (stops[i*5+4] - stops[(i-1)*5+4])
            };
        }
    }
    uint32_t last = stopCount - 1;
    return { stops[last*5+1], stops[last*5+2], stops[last*5+3], stops[last*5+4] };
}

inline GradientColor SampleRadialGradient(float px, float py,
                                           float centerX, float centerY,
                                           float radiusX, float radiusY,
                                           const float* stops, uint32_t stopCount) {
    float dx = (px - centerX) / (std::max)(radiusX, 1e-6f);
    float dy = (py - centerY) / (std::max)(radiusY, 1e-6f);
    float t = std::sqrt(dx * dx + dy * dy);
    t = (std::max)(0.0f, (std::min)(1.0f, t));

    // Reuse linear gradient stop lookup with the computed t
    if (stopCount == 0) return { 0, 0, 0, 0 };
    if (stopCount == 1 || t <= stops[0]) {
        return { stops[1], stops[2], stops[3], stops[4] };
    }
    for (uint32_t i = 1; i < stopCount; ++i) {
        float pos = stops[i * 5];
        if (t <= pos || i == stopCount - 1) {
            float prevPos = stops[(i - 1) * 5];
            float span = pos - prevPos;
            float frac = (span > 1e-6f) ? (t - prevPos) / span : 0.0f;
            frac = (std::max)(0.0f, (std::min)(1.0f, frac));
            return {
                stops[(i-1)*5+1] + frac * (stops[i*5+1] - stops[(i-1)*5+1]),
                stops[(i-1)*5+2] + frac * (stops[i*5+2] - stops[(i-1)*5+2]),
                stops[(i-1)*5+3] + frac * (stops[i*5+3] - stops[(i-1)*5+3]),
                stops[(i-1)*5+4] + frac * (stops[i*5+4] - stops[(i-1)*5+4])
            };
        }
    }
    uint32_t last = stopCount - 1;
    return { stops[last*5+1], stops[last*5+2], stops[last*5+3], stops[last*5+4] };
}

} // namespace jalium
