#pragma once

#include <cstddef>
#include <cstdint>
#include <iterator>
#include <list>
#include <memory>
#include <unordered_map>
#include <utility>

namespace jalium {

// Large analytic masks are expensive to regenerate. A separate LRU keeps
// short animated slices from evicting them, with limits on allocated storage.
// Entries are immutable after publication; pending draws retain shared owners.
// Mask provides a rects vector and Reset(), which clears logical state while
// preserving vector capacity. Acquire() grants the only mutable cache owner.
template <class Mask> class D3D12StrokeMaskCache {
  public:
    struct Limits {
        size_t largeThreshold = 128;
        size_t smallCount = 512;
        size_t smallBytes = 4u << 20;
        size_t largeCount = 1024;
        size_t largeBytes = 32u << 20;
        size_t recycledBytes = 1u << 20;
    };
    D3D12StrokeMaskCache() : D3D12StrokeMaskCache(Limits{}) {}
    explicit D3D12StrokeMaskCache(Limits limits) : limits_(limits) {}
    D3D12StrokeMaskCache(const D3D12StrokeMaskCache &) = delete;
    D3D12StrokeMaskCache &operator=(const D3D12StrokeMaskCache &) = delete;

    std::shared_ptr<const Mask> Find(uint64_t key) {
        auto found = entries_.find(key);
        if (found == entries_.end())
            return nullptr;
        auto node = found->second;
        auto &lane = node->large ? large_ : small_;
        lane.order.splice(lane.order.begin(), lane.order, node);
        return node->entry;
    }
    std::shared_ptr<Mask> Acquire() {
        auto entry = std::move(recycled_);
        if (!entry)
            return std::make_shared<Mask>();
        entry->Reset();
        return entry;
    }
    size_t Insert(uint64_t key, std::shared_ptr<Mask> entry) {
        const bool isLarge = entry->rects.size() >= limits_.largeThreshold;
        auto &target = isLarge ? large_ : small_;
        const size_t bytes = AllocatedBytes(*entry);
        const size_t maxBytes = isLarge ? limits_.largeBytes : limits_.smallBytes;
        const size_t maxCount = isLarge ? limits_.largeCount : limits_.smallCount;
        size_t evictions = 0;
        typename Map::node_type spare;
        auto remove = [&](Lane &lane, Iterator node, bool eviction) {
            lane.bytes -= node->bytes;
            auto mapNode = entries_.extract(node->key);
            if (!recycled_ && node->entry.use_count() == 1 && node->bytes <= limits_.recycledBytes)
                recycled_ = std::move(node->entry);
            if (!spare) {
                spare = std::move(mapNode);
                spareNodes_.splice(spareNodes_.begin(), lane.order, node);
            } else {
                lane.order.erase(node);
            }
            if (eviction)
                ++evictions;
        };
        auto existing = entries_.find(key);
        if (existing != entries_.end()) {
            auto node = existing->second;
            remove(node->large ? large_ : small_, node, false);
        }
        if (maxCount == 0 || bytes > maxBytes) {
            if (spare)
                spareNodes_.clear();
            return evictions;
        }
        while (target.order.size() >= maxCount || target.bytes > maxBytes - bytes)
            remove(target, std::prev(target.order.end()), true);

        if (spare) {
            auto node = spareNodes_.begin();
            node->key = key;
            node->entry = std::move(entry);
            node->bytes = bytes;
            node->large = isLarge;
            target.order.splice(target.order.begin(), spareNodes_, node);
            spare.key() = key;
            spare.mapped() = target.order.begin();
            entries_.insert(std::move(spare));
        } else {
            target.order.push_front({key, std::move(entry), bytes, isLarge});
            try {
                entries_.emplace(key, target.order.begin());
            } catch (...) {
                target.order.pop_front();
                throw;
            }
        }
        target.bytes += bytes;
        return evictions;
    }
    void ReleaseScratch() { recycled_.reset(); }
    size_t size() const { return entries_.size(); }
    size_t small_size() const { return small_.order.size(); }
    size_t large_size() const { return large_.order.size(); }
    size_t small_bytes() const { return small_.bytes; }
    size_t large_bytes() const { return large_.bytes; }
    size_t recycled_bytes() const { return recycled_ ? AllocatedBytes(*recycled_) : 0; }
    static size_t AllocatedBytes(const Mask &entry) {
        return entry.rects.capacity() * sizeof(typename decltype(entry.rects)::value_type);
    }

  private:
    struct Node {
        uint64_t key;
        std::shared_ptr<Mask> entry;
        size_t bytes;
        bool large;
    };
    using List = std::list<Node>;
    using Iterator = typename List::iterator;
    using Map = std::unordered_map<uint64_t, Iterator>;
    struct Lane {
        List order;
        size_t bytes = 0;
    };
    Limits limits_;
    Lane small_, large_;
    // Reuse one evicted list/map node without constructing a staging list on
    // each miss (MSVC's list sentinel itself allocates).
    List spareNodes_;
    Map entries_;
    std::shared_ptr<Mask> recycled_;
};
} // namespace jalium
