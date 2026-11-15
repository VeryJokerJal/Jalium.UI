#pragma once
#include <algorithm>
#include <cstdint>
#include <limits>
#include <utility>
#include <vector>

namespace jalium {
// Source ranges are UTF-16 even on platforms whose wchar_t holds a scalar.
// Store a normalized union so an overlapped shaping cluster is painted once.
inline bool TextGlyphVisible(uint32_t start, uint32_t end,
    const uint32_t* pairs, uint32_t count, int32_t mode)
{
    if (!mode) return true;
    uint32_t left = 0, right = count;
    while (left < right) {
        const auto middle = left + (right - left) / 2;
        if (pairs[middle * 2] < end) left = middle + 1;
        else right = middle;
    }
    bool overlaps = left && uint64_t(pairs[(left - 1) * 2]) + pairs[(left - 1) * 2 + 1] > start;
    return mode == 1 ? overlaps : !overlaps;
}

class TextGlyphRanges {
public:
    bool Set(const uint32_t* pairs, uint32_t count, int32_t mode) {
        if (mode < 0 || mode > 2 || count > 4096 || (count && !pairs) || (!mode && count)) return false;
        std::vector<std::pair<uint32_t, uint32_t>> sorted;
        for (uint32_t i = 0; i < count; ++i) {
            uint32_t start = pairs[2 * i], length = pairs[2 * i + 1];
            if (length > std::numeric_limits<uint32_t>::max() - start) return false;
            if (length) sorted.emplace_back(start, start + length);
        }
        std::sort(sorted.begin(), sorted.end());
        std::vector<uint32_t> merged;
        for (const auto& span : sorted) {
            if (!merged.empty() && span.first <= merged[merged.size() - 2] + merged.back())
                merged.back() = std::max(merged.back(), span.second - merged[merged.size() - 2]);
            else { merged.push_back(span.first); merged.push_back(span.second - span.first); }
        }
        ranges_.swap(merged); mode_ = mode; return true;
    }
    const std::vector<uint32_t>& Ranges() const { return ranges_; }
    const uint32_t* Data() const { return ranges_.data(); }
    uint32_t Count() const { return static_cast<uint32_t>(ranges_.size() / 2); }
    int32_t Mode() const { return mode_; }
private:
    std::vector<uint32_t> ranges_;
    int32_t mode_ = 0;
};
} // namespace jalium
