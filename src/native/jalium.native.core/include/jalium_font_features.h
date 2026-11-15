#pragma once

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <vector>

namespace jalium::font_features {

// Availability is a font-wide property, independent of the text being shaped.
// Validate a complete FeatureList before publishing any of its tags. An empty
// feature is still defined; a glyph-by-glyph probe must not select synthesis.
inline bool Collect(const uint8_t* data, size_t size, std::vector<uint32_t>& result)
{
    if (!data || size < 10) return false;
    auto u16 = [&](size_t at) { return static_cast<uint16_t>((data[at] << 8) | data[at + 1]); };
    auto u32 = [&](size_t at) { return static_cast<uint32_t>(u16(at)) << 16 | u16(at + 2); };
    if (u16(0) != 1 || u16(2) > 1) return false;
    const size_t header = u16(2) == 1 ? 14 : 10;
    if (size < header) return false;
    size_t list = u16(6);
    if (list < header || list > size - 2) return false;
    size_t count = u16(list);
    if (count > (size - list - 2) / 6) return false;
    const size_t recordsEnd = list + 2 + count * 6;
    std::vector<uint32_t> tags;
    tags.reserve(count);
    for (size_t i = 0; i < count; ++i) {
        size_t record = list + 2 + i * 6;
        size_t feature = list + u16(record + 4);
        if (feature < recordsEnd || feature > size - 4) return false;
        size_t lookups = u16(feature + 2);
        if (lookups > (size - feature - 4) / 2) return false;
        tags.push_back(u32(record));
    }
    result.insert(result.end(), tags.begin(), tags.end());
    return true;
}

inline void Query(std::vector<uint32_t>& available, const uint32_t* tags,
    uint32_t count, uint8_t* supported)
{
    std::sort(available.begin(), available.end());
    for (uint32_t i = 0; i < count; ++i)
        supported[i] = std::binary_search(available.begin(), available.end(), tags[i]) ? 1 : 0;
}

} // namespace jalium::font_features
