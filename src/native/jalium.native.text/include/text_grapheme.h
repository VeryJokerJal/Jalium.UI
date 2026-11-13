#pragma once
#include "unicode_grapheme_data.h"
#include <iterator>
#include <string_view>
#include <vector>

namespace jalium::grapheme {
enum Class : uint8_t { Other, CR, LF, Control, Extend, ZWJ, RI, Prepend, SpacingMark, L, V, T, LV, LVT };
inline uint8_t Properties(uint32_t cp)
{
    size_t first = 0, end = std::size(kProperties);
    while (first < end) { size_t middle = first + (end - first) / 2;
        if (kProperties[middle].last < cp) first = middle + 1; else end = middle; }
    return first < std::size(kProperties) && kProperties[first].first <= cp ? kProperties[first].value : 0;
}
// Extended grapheme clusters, UAX #29 revision 47 (Unicode 17). The scan is
// linear, including long Extend/Indic/RI sequences; no backwards rescanning.
inline std::vector<uint32_t> Boundaries(std::wstring_view text)
{
    std::vector<uint32_t> result{0};
    uint8_t previous = Other;
    uint32_t regionalCount = 0;
    unsigned indic = 0;
    bool pictographicExtend = false, joinedPictographic = false;
    for (uint32_t position = 0; position < text.size();) {
        const uint32_t start = position++;
        uint32_t cp = uint32_t(text[start]);
        if constexpr (sizeof(wchar_t) == 2) {
            if (cp >= 0xD800 && cp <= 0xDBFF && position < text.size() &&
                uint32_t(text[position]) >= 0xDC00 && uint32_t(text[position]) <= 0xDFFF)
                cp = 0x10000 + ((cp - 0xD800) << 10) + uint32_t(text[position++]) - 0xDC00;
        }
        const uint8_t properties = Properties(cp), current = properties & 15;
        const bool pictographic = (properties & 16) != 0;
        bool separate = start != 0;
        if (previous == CR && current == LF) separate = false; // GB3
        else if (previous == CR || previous == LF || previous == Control ||
                 current == CR || current == LF || current == Control) { } // GB4/5
        else if (previous == L && (current == L || current == V || current == LV || current == LVT)) separate = false;
        else if ((previous == LV || previous == V) && (current == V || current == T)) separate = false;
        else if ((previous == LVT || previous == T) && current == T) separate = false;
        else if (current == Extend || current == ZWJ || current == SpacingMark || previous == Prepend) separate = false;
        else if ((properties & 96) == 64 && indic == 2) separate = false; // GB9c
        else if (previous == ZWJ && joinedPictographic && pictographic) separate = false; // GB11
        else if (previous == RI && current == RI && regionalCount % 2) separate = false;
        if (separate) result.push_back(start);
        const uint8_t conjunct = properties & 96;
        if (conjunct == 64) indic = 1;
        else if (conjunct == 32) indic = indic ? 2 : 0;
        else if (conjunct != 96) indic = 0;
        joinedPictographic = current == ZWJ && pictographicExtend;
        if (pictographic) pictographicExtend = true;
        else if (current != Extend) pictographicExtend = false;
        regionalCount = current == RI ? regionalCount + 1 : 0;
        previous = current;
    }
    if (!text.empty()) result.push_back(static_cast<uint32_t>(text.size()));
    return result;
}
} // namespace jalium::grapheme
