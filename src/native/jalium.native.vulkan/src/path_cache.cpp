#include "path_cache.h"

#include <cstring>
#include <utility>

namespace jalium {

namespace {

// FNV-1a 64-bit constants.
constexpr uint64_t kFnvOffset = 0xcbf29ce484222325ULL;
constexpr uint64_t kFnvPrime  = 0x100000001b3ULL;

inline uint64_t FnvMix(uint64_t h, uint64_t v) noexcept
{
    h ^= v;
    h *= kFnvPrime;
    return h;
}

inline uint64_t FnvMixBytes(uint64_t h, const void* data, size_t bytes) noexcept
{
    auto* p = static_cast<const uint8_t*>(data);
    for (size_t i = 0; i < bytes; ++i) {
        h ^= p[i];
        h *= kFnvPrime;
    }
    return h;
}

inline uint32_t FloatBits(float f) noexcept
{
    uint32_t bits;
    std::memcpy(&bits, &f, sizeof(bits));
    return bits;
}

} // namespace

uint64_t HashPathInput(float startX,
                       float startY,
                       const float* commands,
                       uint32_t commandLength,
                       int32_t fillRule) noexcept
{
    uint64_t h = kFnvOffset;
    h = FnvMix(h, FloatBits(startX));
    h = FnvMix(h, FloatBits(startY));
    h = FnvMix(h, static_cast<uint64_t>(fillRule));
    h = FnvMix(h, static_cast<uint64_t>(commandLength));
    if (commands && commandLength > 0) {
        h = FnvMixBytes(h, commands,
                        static_cast<size_t>(commandLength) * sizeof(float));
    }
    return h;
}

PathGeometryCache::PathGeometryCache(size_t capacity) : capacity_(capacity)
{
    map_.reserve(capacity * 2);
}

std::optional<PathGeometryCache::LookupResult>
PathGeometryCache::FindAndTouch(uint64_t key)
{
    auto it = map_.find(key);
    if (it == map_.end()) {
        ++misses_;
        return std::nullopt;
    }
    list_.splice(list_.begin(), list_, it->second);
    ++hits_;
    return LookupResult{ it->second->entry };
}

void PathGeometryCache::Insert(uint64_t key,
                               std::shared_ptr<const CachedPathGeometry> entry)
{
    if (!entry) return;

    // If key already present (concurrent inserts shouldn't happen on a single
    // render thread, but handle the case defensively), refresh in place.
    auto existing = map_.find(key);
    if (existing != map_.end()) {
        existing->second->entry = std::move(entry);
        list_.splice(list_.begin(), list_, existing->second);
        return;
    }

    while (list_.size() >= capacity_ && !list_.empty()) {
        const auto& tail = list_.back();
        map_.erase(tail.key);
        list_.pop_back();
    }

    list_.push_front(ListNode{ key, std::move(entry) });
    map_.emplace(key, list_.begin());
}

void PathGeometryCache::Clear()
{
    list_.clear();
    map_.clear();
}

} // namespace jalium
