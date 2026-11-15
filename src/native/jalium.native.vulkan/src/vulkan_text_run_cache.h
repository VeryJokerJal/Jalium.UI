#pragma once
#include <cstdint>
#include <cstring>
#include <cwchar>
#include <list>
#include <memory>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

namespace jalium::vulkan_text_cache {
struct SelfHostedTextRunKey {
    uint64_t formatInstanceId = 0;
    uint32_t formatStyleGeneration = 0;
    uint32_t textLength = 0;
    uint64_t textHash = 0;
    int32_t visibility = 0;
    std::vector<uint32_t> glyphRanges;
    float    maxWidth = 0.0f;
    float    maxHeight = 0.0f;
    float    renderScale = 1.0f;
    int32_t  aaMode = 0;
    uint8_t  r = 0, g = 0, b = 0, a8 = 0;

    bool operator==(const SelfHostedTextRunKey& o) const noexcept
    {
        return formatInstanceId == o.formatInstanceId &&
               formatStyleGeneration == o.formatStyleGeneration &&
               textLength == o.textLength && textHash == o.textHash &&
               visibility == o.visibility && glyphRanges == o.glyphRanges &&
               maxWidth == o.maxWidth && maxHeight == o.maxHeight &&
               renderScale == o.renderScale && aaMode == o.aaMode &&
               r == o.r && g == o.g && b == o.b && a8 == o.a8;
    }
};

struct SelfHostedTextRunKeyHash {
    size_t operator()(const SelfHostedTextRunKey& k) const noexcept
    {
        uint64_t h = k.textHash ^ static_cast<uint64_t>(k.visibility);
        for (auto value : k.glyphRanges) { h ^= value; h *= 0x100000001B3ULL; }
        h ^= k.formatInstanceId * 0x9E3779B97F4A7C15ULL;
        h ^= (static_cast<uint64_t>(k.formatStyleGeneration) << 32) ^ k.textLength;
        uint32_t wBits = 0, hBits = 0, sBits = 0;
        std::memcpy(&wBits, &k.maxWidth, sizeof(wBits));
        std::memcpy(&hBits, &k.maxHeight, sizeof(hBits));
        std::memcpy(&sBits, &k.renderScale, sizeof(sBits));
        h ^= (static_cast<uint64_t>(wBits) << 32) ^ hBits;
        h *= 0x100000001B3ULL;
        h ^= (static_cast<uint64_t>(sBits) << 32) ^ static_cast<uint32_t>(k.aaMode);
        h *= 0x100000001B3ULL;
        h ^= (static_cast<uint64_t>(k.r) << 24) | (static_cast<uint64_t>(k.g) << 16) |
             (static_cast<uint64_t>(k.b) << 8) | k.a8;
        return static_cast<size_t>(h ^ (h >> 29));
    }
};

struct SelfHostedTextRunEntry {
    // Exact text kept for equality (the 64-bit hash alone is not proof).
    std::wstring text;
    // Finished bitmaps (either may be null when that plane has no pixels).
    std::shared_ptr<std::vector<uint8_t>> pixels;
    std::shared_ptr<std::vector<uint8_t>> lcdPixels;
    int32_t bitmapWidth = 0;
    int32_t bitmapHeight = 0;
    float   bitmapOffsetX = 0.0f;   // in the run's scaled (physical) space
    float   bitmapOffsetY = 0.0f;
    float   layoutOriginY = 0.0f;
    size_t  bytes = 0;
    // The run produces nothing to draw (no quads / degenerate box); cached so
    // the shaping is not repeated every frame either.
    bool    empty = true;
    std::list<SelfHostedTextRunKey>::iterator lruIt;
};

class SelfHostedTextRunCache {
public:
    static constexpr size_t kMaxEntries = 2048;
    static constexpr size_t kMaxBytes = 48u * 1024u * 1024u;

    SelfHostedTextRunEntry* Find(const SelfHostedTextRunKey& key, const wchar_t* text)
    {
        auto it = map_.find(key);
        if (it == map_.end()) return nullptr;
        SelfHostedTextRunEntry& e = it->second;
        if (e.text.size() != key.textLength ||
            (key.textLength > 0 && std::wmemcmp(e.text.data(), text, key.textLength) != 0)) {
            return nullptr;   // hash collision: treat as a miss (the insert replaces it)
        }
        lru_.splice(lru_.begin(), lru_, e.lruIt);   // most-recently-used first
        return &e;
    }

    SelfHostedTextRunEntry* Insert(const SelfHostedTextRunKey& key, const wchar_t* text,
                                   SelfHostedTextRunEntry&& entry)
    {
        // A rejected entry remains with the caller for immediate drawing.
        // Never insert an entry that Trim would immediately erase and then
        // return a dangling pointer to that erased map element.
        const size_t bytes = (entry.pixels ? entry.pixels->size() : 0) +
            (entry.lcdPixels ? entry.lcdPixels->size() : 0) +
            size_t(key.textLength) * sizeof(wchar_t) + sizeof(SelfHostedTextRunEntry) +
            2 * (sizeof(SelfHostedTextRunKey) + key.glyphRanges.size() * sizeof(uint32_t));
        if (bytes > kMaxBytes) return nullptr;
        auto existing = map_.find(key);
        if (existing != map_.end()) {
            bytes_ -= existing->second.bytes;
            lru_.erase(existing->second.lruIt);
            map_.erase(existing);
        }
        entry.text.assign(text, text + key.textLength);
        entry.bytes = bytes;
        lru_.push_front(key);
        entry.lruIt = lru_.begin();
        bytes_ += entry.bytes;
        auto [it, inserted] = map_.emplace(key, std::move(entry));
        (void)inserted;
        Trim();
        return &it->second;
    }

    void Clear()
    {
        map_.clear();
        lru_.clear();
        bytes_ = 0;
    }

private:
    void Trim()
    {
        while (!lru_.empty() && (map_.size() > kMaxEntries || bytes_ > kMaxBytes)) {
            const SelfHostedTextRunKey& victim = lru_.back();
            auto it = map_.find(victim);
            if (it != map_.end()) {
                bytes_ -= it->second.bytes;
                map_.erase(it);
            }
            lru_.pop_back();
        }
    }

    std::unordered_map<SelfHostedTextRunKey, SelfHostedTextRunEntry, SelfHostedTextRunKeyHash> map_;
    std::list<SelfHostedTextRunKey> lru_;
    size_t bytes_ = 0;
};

} // namespace jalium::vulkan_text_cache
