#pragma once
#include "sfnt_reader.h"
#include <algorithm>
#include <array>
#include <set>

namespace jalium::font {
struct VerticalAliases {
    std::array<uint32_t, 2> tags{}; // Original vert / vrt2, zero when absent.
    bool Any() const { return tags[0] || tags[1]; }
};
inline void VerticalWrite16(std::vector<uint8_t>& b, size_t at, uint16_t value) {
    b[at] = uint8_t(value >> 8); b[at + 1] = uint8_t(value);
}
inline void VerticalWrite32(std::vector<uint8_t>& b, size_t at, uint32_t value) {
    VerticalWrite16(b, at, uint16_t(value >> 16)); VerticalWrite16(b, at + 2, uint16_t(value));
}
// CoreText couples the reserved vert/vrt2 switches and suppresses them in
// horizontal shaping. Give the existing GSUB features collision-free private
// names, leaving every lookup, glyph, language, variation and outline intact.
// Re-sort records and all references; this is a font adapter, not a shaper.
inline bool AliasVerticalFeatures(std::vector<uint8_t>& bytes, VerticalAliases& aliases) {
    ByteReader r(bytes);
    if (!r.InBounds(0, 10) || r.U16(0) != 1 || r.U16(2) > 1) return false;
    const size_t scripts = r.U16(4), features = r.U16(6), lookups = r.U16(8);
    if (!scripts || !features || !lookups || !r.InBounds(features, 2) || !r.InBounds(lookups, 2)) return false;
    const uint16_t count = r.U16(features);
    if (!r.InBounds(features + 2, size_t(count) * 6) ||
        !r.InBounds(lookups + 2, size_t(r.U16(lookups)) * 2)) return false;
    struct Feature { uint32_t tag; uint16_t offset, original; };
    std::vector<Feature> records; std::set<uint32_t> tags;
    for (uint16_t i = 0; i < count; ++i) {
        const size_t at = features + 2 + size_t(i) * 6;
        const size_t table = features + r.U16(at + 4);
        if (!r.InBounds(table, 4) || !r.InBounds(table + 4, size_t(r.U16(table + 2)) * 2)) return false;
        records.push_back({r.U32(at), r.U16(at + 4), i}); tags.insert(r.U32(at));
    }
    unsigned next = 0;
    auto unused = [&]() -> uint32_t {
        for (; next < 100; ++next) {
            uint32_t tag = 0x4a760000u | uint32_t('0' + next / 10) << 8 | uint32_t('0' + next % 10);
            if (!tags.count(tag)) { ++next; tags.insert(tag); return tag; }
        }
        return 0;
    };
    VerticalAliases mapped;
    for (auto& feature : records) for (unsigned kind = 0; kind < 2; ++kind) {
        if (feature.tag != (kind == 0 ? 0x76657274u : 0x76727432u)) continue;
        if (!mapped.tags[kind]) mapped.tags[kind] = unused();
        if (!mapped.tags[kind]) return false;
        feature.tag = mapped.tags[kind]; break;
    }
    if (!mapped.Any()) { aliases = mapped; return true; }
    std::stable_sort(records.begin(), records.end(), [](auto& a, auto& b) { return a.tag < b.tag; });
    std::vector<uint16_t> remap(count);
    for (uint16_t i = 0; i < count; ++i) remap[records[i].original] = i;
    // Stage writes so malformed input never leaves a partially aliased table.
    auto output = bytes;
    for (size_t i = 0; i < records.size(); ++i) {
        size_t at = features + 2 + i * 6;
        VerticalWrite32(output, at, records[i].tag); VerticalWrite16(output, at + 4, records[i].offset);
    }
    std::set<size_t> languages;
    auto language = [&](size_t at) -> bool {
        if (!languages.insert(at).second) return true;
        if (!r.InBounds(at, 6)) return false;
        const uint16_t required = r.U16(at + 2), length = r.U16(at + 4);
        if ((required != 0xffff && required >= count) || !r.InBounds(at + 6, size_t(length) * 2)) return false;
        if (required != 0xffff) VerticalWrite16(output, at + 2, remap[required]);
        std::vector<uint16_t> indices;
        for (size_t i = 0; i < length; ++i) {
            auto index = r.U16(at + 6 + i * 2); if (index >= count) return false;
            indices.push_back(remap[index]);
        }
        std::sort(indices.begin(), indices.end());
        for (size_t i = 0; i < indices.size(); ++i) VerticalWrite16(output, at + 6 + i * 2, indices[i]);
        return true;
    };
    if (!r.InBounds(scripts, 2) || !r.InBounds(scripts + 2, size_t(r.U16(scripts)) * 6)) return false;
    for (size_t i = 0; i < r.U16(scripts); ++i) {
        size_t script = scripts + r.U16(scripts + 2 + i * 6 + 4);
        if (!r.InBounds(script, 4) || !r.InBounds(script + 4, size_t(r.U16(script + 2)) * 6)) return false;
        if (r.U16(script) && !language(script + r.U16(script))) return false;
        for (size_t j = 0; j < r.U16(script + 2); ++j) {
            auto off = r.U16(script + 4 + j * 6 + 4);
            if (!off || !language(script + off)) return false;
        }
    }
    if (r.U16(2) == 1) {
        if (!r.InBounds(10, 4)) return false;
        size_t variation = r.U32(10);
        if (variation) {
            if (!r.InBounds(variation, 8) || r.U16(variation) != 1 || r.U16(variation + 2) != 0 ||
                size_t(r.U32(variation + 4)) > (r.Size() - variation - 8) / 8) return false;
            std::set<size_t> substitutions;
            for (size_t i = 0; i < r.U32(variation + 4); ++i) {
                size_t offset = r.U32(variation + 8 + i * 8 + 4);
                if (!offset) return false;
                size_t table = variation + offset;
                if (!substitutions.insert(table).second) continue;
                if (!r.InBounds(table, 6) || r.U16(table) != 1 || r.U16(table + 2) != 0 ||
                    !r.InBounds(table + 6, size_t(r.U16(table + 4)) * 6)) return false;
                struct Substitution { uint16_t index; uint32_t offset; };
                std::vector<Substitution> values;
                for (size_t j = 0; j < r.U16(table + 4); ++j) {
                    size_t at = table + 6 + j * 6; auto index = r.U16(at); auto alternate = r.U32(at + 2);
                    if (index >= count || !alternate || !r.InBounds(table + alternate, 4) ||
                        !r.InBounds(table + alternate + 4, size_t(r.U16(table + alternate + 2)) * 2)) return false;
                    values.push_back({remap[index], alternate});
                }
                std::stable_sort(values.begin(), values.end(), [](auto& a, auto& b) { return a.index < b.index; });
                for (size_t j = 0; j < values.size(); ++j) {
                    size_t at = table + 6 + j * 6;
                    VerticalWrite16(output, at, values[j].index); VerticalWrite32(output, at + 2, values[j].offset);
                }
            }
        }
    }
    bytes.swap(output); aliases = mapped;
    return true;
}
} // namespace jalium::font
