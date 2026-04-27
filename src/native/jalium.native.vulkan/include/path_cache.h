#pragma once

#include <cstdint>
#include <list>
#include <memory>
#include <optional>
#include <unordered_map>
#include <vector>

namespace jalium {

// Cache for the result of decomposing + triangulating a path.
//
// Why: TriangulateSimplePolygon is O(N³) ear-clipping. SVG icons routinely
// give 50–200 verts after bezier decomposition, so a single icon costs
// ~125 K – 8 M float ops to triangulate. Doing that every frame for every
// icon dominates CPU time on the Vulkan backend's GPU replay path.
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

    bool triangulationSucceeded = false;
};

// Bounded LRU cache: O(1) lookup + O(1) touch + per-insert single eviction.
// Same shape as TextLruCache (text_cache.h) — they share the LRU pattern.
class PathGeometryCache {
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

    void   Clear();
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

// FNV-1a 64-bit hash over (startX, startY, fillRule, commands as raw bytes).
// 64 bits is enough that collision rate on the Gallery's path working set
// (a few hundred unique paths) is below 1 in 4 billion, so we don't bother
// with a secondary equality check on the cached commands themselves —
// callers trust the hash. If a future workload could carry millions of
// distinct paths simultaneously, switch to a 128-bit hash or add a payload-
// equality check.
uint64_t HashPathInput(float startX,
                       float startY,
                       const float* commands,
                       uint32_t commandLength,
                       int32_t fillRule) noexcept;

} // namespace jalium
