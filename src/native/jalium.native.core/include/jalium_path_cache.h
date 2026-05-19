#pragma once

#include <cstdint>
#include <list>
#include <memory>
#include <optional>
#include <unordered_map>
#include <vector>

#include "jalium_api.h"
#include "jalium_triangulate.h"  // Contour (cached boundary for edge-AA feather)

namespace jalium {

// Cross-backend cache for the result of decomposing + triangulating a path.
//
// Originally Vulkan-only (path_cache.h in jalium.native.vulkan); hoisted to
// jalium.native.core in 2026-05 so D3D12 Impeller can layer it underneath
// its pixel-space rect cache. Both backends now link to the same LRU type;
// the rect cache (which is pixel-space and partitions by transform) and
// this geometry cache (which is source-space and ignores transform until
// scaleBucket lands in a later commit) coexist as two tiers.
//
// Why: TriangulateSimplePolygon is O(N³) ear-clipping. SVG icons routinely
// give 50–200 verts after bezier decomposition, so a single icon costs
// ~125 K – 8 M float ops to triangulate. Doing that every frame for every
// icon dominates CPU time on backends that hit the per-path tessellator.
//
// What we cache: the *local-space* point list (post-bezier-decompose,
// pre-transform) and, if triangulation succeeded, the local-space triangle
// vertex list. The current frame's transform is applied to these lazily
// when we record the GPU command — that's an O(N) op, not O(N³).
//
// Key: a 64-bit hash of (startX, startY, fillRule, commands[…]). Same
// path data → same key → same triangulation. The brush color does NOT
// participate in the key because it doesn't affect the triangulation;
// it's applied at GPU command record time.
struct CachedPathGeometry {
    // Always populated. (x, y) pairs in local (pre-transform) space, including
    // the implicit MoveTo at (startX, startY) plus every command-derived vertex
    // (bezier sample points, line endpoints, etc.).
    std::vector<float> localPoints;

    // Populated if TriangulateSimplePolygon succeeded. Each triangle is three
    // (x, y) pairs in local space. Empty when triangulation failed (e.g. self-
    // intersecting glyph outline, multi-subpath SVG icon) — the caller must
    // fall back to CPU rasterization in that case.
    std::vector<float> localTriangles;

    // Local-space boundary contours (post-bezier-flatten, pre-transform).
    // Populated by the D3D12 Impeller engine so the per-frame emit step can
    // build a constant-width edge-AA feather ring around the fill on its
    // non-MSAA target without re-flattening. Vulkan leaves this empty (its
    // pipeline AAs differently). Cached here, like localTriangles, so a
    // moving / scaled / rotated path never re-flattens.
    std::vector<Contour> contours;

    bool triangulationSucceeded = false;
};

// Bounded LRU cache: O(1) lookup + O(1) touch + per-insert single eviction.
// Same shape as TextLruCache (text_cache.h) — they share the LRU pattern.
class JALIUM_API PathGeometryCache {
public:
    struct LookupResult {
        // shared_ptr so cache evictions are safe even if a recorded GPU
        // command (or transient std::vector copy) outlives this lookup. Same
        // safety model as TextLruCache.
        std::shared_ptr<const CachedPathGeometry> entry;
    };

    explicit PathGeometryCache(size_t capacity);

    // Returns nullopt on miss; on hit, splices the entry to the LRU head.
    std::optional<LookupResult> FindAndTouch(uint64_t key);

    // Insert a freshly-computed geometry. Evicts the LRU tail if at capacity.
    void Insert(uint64_t key, std::shared_ptr<const CachedPathGeometry> entry);

    void     Clear();
    size_t   Size()       const noexcept { return list_.size(); }
    size_t   Capacity()   const noexcept { return capacity_; }
    uint64_t HitCount()   const noexcept { return hits_; }
    uint64_t MissCount()  const noexcept { return misses_; }

private:
    struct ListNode {
        uint64_t key;
        std::shared_ptr<const CachedPathGeometry> entry;
    };
    using ListIt = std::list<ListNode>::iterator;

    std::list<ListNode> list_;
    std::unordered_map<uint64_t, ListIt> map_;
    size_t capacity_;
    uint64_t hits_   = 0;
    uint64_t misses_ = 0;
};

// FNV-1a 64-bit hash over (startX, startY, fillRule, scaleBucket, commands
// as raw bytes). 64 bits is enough that collision rate on the Gallery's
// path working set (a few hundred unique paths × handful of scale buckets)
// is below 1 in 4 billion, so we don't bother with a secondary equality
// check on the cached commands themselves — callers trust the hash. If a
// future workload could carry millions of distinct paths simultaneously,
// switch to a 128-bit hash or add a payload-equality check.
//
// scaleBucket partitions the cache by transform-scale octave (see
// ScaleBucketFromMaxScale in jalium_flatten.h). Same source path data
// rendered at 1.0× and 4.0× scale gets two cache entries so the cached
// vertex density actually matches the on-screen size; 1.0× and 1.1× share
// an entry because the per-octave bucket is wider than 10%.
JALIUM_API uint64_t HashPathInput(float startX,
                                  float startY,
                                  const float* commands,
                                  uint32_t commandLength,
                                  int32_t fillRule,
                                  uint32_t scaleBucket) noexcept;

} // namespace jalium
