#pragma once

#include <cstdint>
#include <deque>
#include <list>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace jalium {

// Cache key for a rasterized text bitmap. The text is stored as an owned
// wstring; lookup goes through TextCacheKeyView (transparent / heterogeneous
// lookup) so the hot path never builds a temporary wstring.
struct TextCacheKey {
    std::wstring text;
    uint32_t fontFamilyId = 0;   // FontFamilyInterner id; collapses wstring compare into a single int.
    int16_t  fontHeight = 0;     // GDI LOGFONT.lfHeight (signed px).
    int16_t  bitmapW = 0;
    int16_t  bitmapH = 0;
    uint32_t brushBgra = 0;      // Premultiplied b | g<<8 | r<<16 | a<<24.
    uint16_t drawFlags = 0;      // DT_LEFT / DT_CENTER / etc.
    int16_t  fontWeight = 0;
    uint8_t  fontStyle = 0;      // 0=normal, 1=italic, 2=oblique.
};

// Same shape as TextCacheKey but with a wstring_view — used purely for
// lookups, never stored. Lets the hot path avoid building a wstring.
struct TextCacheKeyView {
    std::wstring_view text;
    uint32_t fontFamilyId = 0;
    int16_t  fontHeight = 0;
    int16_t  bitmapW = 0;
    int16_t  bitmapH = 0;
    uint32_t brushBgra = 0;
    uint16_t drawFlags = 0;
    int16_t  fontWeight = 0;
    uint8_t  fontStyle = 0;
};

struct TextCacheHash {
    using is_transparent = void;
    size_t operator()(const TextCacheKey& k) const noexcept;
    size_t operator()(const TextCacheKeyView& v) const noexcept;
};

struct TextCacheEq {
    using is_transparent = void;
    bool operator()(const TextCacheKey& a, const TextCacheKey& b) const noexcept;
    bool operator()(const TextCacheKeyView& v, const TextCacheKey& k) const noexcept;
    bool operator()(const TextCacheKey& k, const TextCacheKeyView& v) const noexcept;
};

// Bounded LRU cache for rasterized text bitmaps. Replaces the previous
// std::map<tuple<wstring,wstring,...>> + clear-on-overflow design which
// rebuilt the wstring every lookup and dropped the entire working set
// once the cap was reached.
//
// Operation cost:
//   - FindAndTouch: O(1) hash lookup + O(1) list splice. Zero allocation.
//   - Insert:       O(1) hash insert + O(1) list push_front. Evicts exactly
//                   one tail entry when at capacity (true LRU, not bulk wipe).
//
// Storage: bitmap pixels are held by shared_ptr. Recorded GpuReplayCommands
// keep a second strong ref while a frame is in flight, so evicting the cache
// entry mid-frame is safe — the command's pixel buffer stays alive.
class TextLruCache {
public:
    struct LookupResult {
        std::shared_ptr<const std::vector<uint8_t>> pixels;
        int width = 0;
        int height = 0;
    };

    explicit TextLruCache(size_t capacity);

    // Hot path. Returns std::nullopt on miss; on hit, splices the entry to
    // the head of the LRU list (touch) and returns its payload.
    std::optional<LookupResult> FindAndTouch(const TextCacheKeyView& view);

    // Insert a freshly rasterized bitmap. Evicts the LRU tail if at capacity.
    void Insert(TextCacheKey key,
                std::shared_ptr<const std::vector<uint8_t>> pixels,
                int width, int height);

    void Clear();

    size_t   Size()       const noexcept { return list_.size(); }
    size_t   Capacity()   const noexcept { return capacity_; }
    int64_t  TotalBytes() const noexcept { return totalBytes_; }
    uint64_t HitCount()   const noexcept { return hits_; }
    uint64_t MissCount()  const noexcept { return misses_; }

private:
    struct ListNode {
        TextCacheKey key;
        std::shared_ptr<const std::vector<uint8_t>> pixels;
        int width = 0;
        int height = 0;
    };
    using ListIt = std::list<ListNode>::iterator;

    std::list<ListNode> list_;
    std::unordered_map<TextCacheKey, ListIt, TextCacheHash, TextCacheEq> map_;
    size_t  capacity_;
    int64_t totalBytes_ = 0;
    uint64_t hits_   = 0;
    uint64_t misses_ = 0;
};

// Maps font-family wstrings to small uint32_t ids so the text cache key can
// fold a wstring compare into a single integer compare. The id is stable for
// the lifetime of the interner. Backing storage is a deque so that wstring
// addresses stay valid as new families are added — the unordered_map keys it
// on a wstring_view that points into the deque slot, so there is exactly one
// copy of each family string in the process.
class FontFamilyInterner {
public:
    // Returns an id ≥ 1 for the given family. Heterogeneous lookup — does
    // NOT allocate when the family is already interned.
    uint32_t Intern(std::wstring_view family);

    // For diagnostics / DevTools — empty view if id is out of range.
    std::wstring_view GetFamily(uint32_t id) const noexcept;

    size_t Size() const noexcept { return families_.size(); }

private:
    std::deque<std::wstring> families_;                              // index = id-1.
    std::unordered_map<std::wstring_view, uint32_t> map_;            // view -> id.
};

} // namespace jalium
