#include "text_cache.h"

#include <utility>

namespace jalium {

namespace {

// Combines all integer fields plus a wstring_view hash into a single size_t.
// Same algorithm for the owned-key and view-key paths so heterogeneous
// lookup is consistent (transparent hash invariant: hash(view) == hash(key)
// when the view names the same string).
inline size_t HashFields(std::wstring_view text,
                         uint32_t fontFamilyId,
                         int16_t fontHeight,
                         int16_t bitmapW,
                         int16_t bitmapH,
                         uint32_t brushBgra,
                         uint16_t drawFlags,
                         int16_t fontWeight,
                         uint8_t fontStyle) noexcept
{
    size_t h = std::hash<std::wstring_view>{}(text);

    auto mix = [&h](uint64_t v) {
        h ^= std::hash<uint64_t>{}(v) + 0x9e3779b97f4a7c15ULL + (h << 6) + (h >> 2);
    };

    // Pack the small ints into u64 chunks so we mix three times instead of
    // eight. The bit layout doesn't have to be canonical — only stable.
    const uint64_t a =  static_cast<uint64_t>(fontFamilyId)
                     | (static_cast<uint64_t>(static_cast<uint16_t>(fontHeight)) << 32)
                     | (static_cast<uint64_t>(static_cast<uint16_t>(bitmapW))   << 48);
    const uint64_t b =  static_cast<uint64_t>(static_cast<uint16_t>(bitmapH))
                     | (static_cast<uint64_t>(brushBgra) << 16)
                     | (static_cast<uint64_t>(drawFlags) << 48);
    const uint64_t c =  static_cast<uint64_t>(static_cast<uint16_t>(fontWeight))
                     | (static_cast<uint64_t>(fontStyle) << 16);
    mix(a);
    mix(b);
    mix(c);
    return h;
}

} // namespace

size_t TextCacheHash::operator()(const TextCacheKey& k) const noexcept
{
    return HashFields(std::wstring_view{k.text}, k.fontFamilyId, k.fontHeight,
                      k.bitmapW, k.bitmapH, k.brushBgra, k.drawFlags,
                      k.fontWeight, k.fontStyle);
}

size_t TextCacheHash::operator()(const TextCacheKeyView& v) const noexcept
{
    return HashFields(v.text, v.fontFamilyId, v.fontHeight,
                      v.bitmapW, v.bitmapH, v.brushBgra, v.drawFlags,
                      v.fontWeight, v.fontStyle);
}

bool TextCacheEq::operator()(const TextCacheKey& a, const TextCacheKey& b) const noexcept
{
    // Compare cheap integer fields first — strings are compared last so we
    // short-circuit on a hash collision that didn't actually match.
    return a.fontFamilyId == b.fontFamilyId
        && a.fontHeight   == b.fontHeight
        && a.bitmapW      == b.bitmapW
        && a.bitmapH      == b.bitmapH
        && a.brushBgra    == b.brushBgra
        && a.drawFlags    == b.drawFlags
        && a.fontWeight   == b.fontWeight
        && a.fontStyle    == b.fontStyle
        && a.text         == b.text;
}

bool TextCacheEq::operator()(const TextCacheKeyView& v, const TextCacheKey& k) const noexcept
{
    return v.fontFamilyId == k.fontFamilyId
        && v.fontHeight   == k.fontHeight
        && v.bitmapW      == k.bitmapW
        && v.bitmapH      == k.bitmapH
        && v.brushBgra    == k.brushBgra
        && v.drawFlags    == k.drawFlags
        && v.fontWeight   == k.fontWeight
        && v.fontStyle    == k.fontStyle
        && v.text         == std::wstring_view{k.text};
}

bool TextCacheEq::operator()(const TextCacheKey& k, const TextCacheKeyView& v) const noexcept
{
    return operator()(v, k);
}

TextLruCache::TextLruCache(size_t capacity) : capacity_(capacity)
{
    // Reserve double the capacity to keep load factor low — text caches see
    // many more lookups than evicts so a sparse table is worth the memory.
    map_.reserve(capacity * 2);
}

std::optional<TextLruCache::LookupResult>
TextLruCache::FindAndTouch(const TextCacheKeyView& view)
{
    auto it = map_.find(view);
    if (it == map_.end()) {
        ++misses_;
        return std::nullopt;
    }
    list_.splice(list_.begin(), list_, it->second);
    ++hits_;
    auto& node = *it->second;
    return LookupResult{ node.pixels, node.width, node.height };
}

void TextLruCache::Insert(TextCacheKey key,
                          std::shared_ptr<const std::vector<uint8_t>> pixels,
                          int width, int height)
{
    while (list_.size() >= capacity_ && !list_.empty()) {
        auto& tail = list_.back();
        totalBytes_ -= static_cast<int64_t>(tail.width) * tail.height * 4;
        map_.erase(tail.key);
        list_.pop_back();
    }

    list_.push_front(ListNode{ std::move(key), std::move(pixels), width, height });
    auto it = list_.begin();
    map_.emplace(it->key, it);
    totalBytes_ += static_cast<int64_t>(width) * height * 4;
}

void TextLruCache::Clear()
{
    list_.clear();
    map_.clear();
    totalBytes_ = 0;
    // Hit/miss counters intentionally NOT reset — callers may want to query
    // them across a Clear() (e.g. resize-induced flush) for diagnostics.
}

uint32_t FontFamilyInterner::Intern(std::wstring_view family)
{
    auto it = map_.find(family);
    if (it != map_.end()) {
        return it->second;
    }

    families_.emplace_back(std::wstring{family});
    const uint32_t id = static_cast<uint32_t>(families_.size());

    // The unordered_map's wstring_view key references the deque slot we just
    // wrote — deque guarantees that addresses of existing elements stay valid
    // when emplace_back grows the container, so this reference never dangles.
    map_.emplace(std::wstring_view{families_.back()}, id);
    return id;
}

std::wstring_view FontFamilyInterner::GetFamily(uint32_t id) const noexcept
{
    if (id == 0 || id - 1 >= families_.size()) return {};
    return families_[id - 1];
}

} // namespace jalium
