#pragma once
#include "jalium_unicode_casing_data.h"
#include <algorithm>
#include <iterator>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>

namespace jalium::unicode_casing {

template <size_t N>
inline bool Contains(const data::Range (&ranges)[N], uint32_t value) {
    auto found = std::upper_bound(std::begin(ranges), std::end(ranges), value,
        [](uint32_t point, const data::Range& range) { return point < range.first; });
    return found != std::begin(ranges) && value <= std::prev(found)->last;
}
inline bool IsLowercase(uint32_t value) { return Contains(data::Lowercase, value); }
// CSS caps handles Unicode titlecase alongside uppercase.
inline bool IsUppercase(uint32_t value) { return Contains(data::Uppercase, value); }
inline bool IsCased(uint32_t value) { return Contains(data::Cased, value); }

enum class Case { Lower, Upper };

// Decode once and compute the Unicode contextual predicates in two linear
// passes. Context spans styling/font boundaries; a caller can transform any
// source range without losing the original paragraph's neighboring characters.
class Context {
    struct Scalar {
        uint32_t value;
        size_t start, length;
        bool casedBefore = false, casedAfter = false;
        bool afterI = false, afterSoftDotted = false, beforeDot = false, moreAbove = false;
    };
    std::u16string_view source_;
    std::vector<Scalar> scalars_;

    template <size_t N>
    static void Append(std::u16string& output, uint32_t value,
        const data::Mapping (&mappings)[N], const char16_t* text) {
        auto found = std::lower_bound(std::begin(mappings), std::end(mappings), value,
            [](const data::Mapping& mapping, uint32_t point) { return mapping.codepoint < point; });
        if (found != std::end(mappings) && found->codepoint == value) {
            output.append(text + found->offset, found->length);
        } else if (value <= 0xFFFF) output.push_back(static_cast<char16_t>(value));
        else {
            value -= 0x10000;
            output.push_back(static_cast<char16_t>(0xD800 + (value >> 10)));
            output.push_back(static_cast<char16_t>(0xDC00 + (value & 0x3FF)));
        }
    }
public:
    explicit Context(std::u16string_view source, uint32_t boundaries = 0) : source_(source) {
        if (boundaries & ~63u) throw std::invalid_argument("Unicode casing boundary flags");
        scalars_.reserve(source.size());
        for (size_t at = 0; at < source.size();) {
            uint32_t value = source[at]; size_t length = 1;
            if (value >= 0xD800 && value <= 0xDBFF && at + 1 < source.size() &&
                source[at + 1] >= 0xDC00 && source[at + 1] <= 0xDFFF) {
                value = 0x10000 + ((value - 0xD800) << 10) + source[at + 1] - 0xDC00; length = 2;
            }
            scalars_.push_back({value, at, length}); at += length;
        }
        bool cased = (boundaries & 1) != 0, afterI = (boundaries & 4) != 0, afterSoft = (boundaries & 8) != 0;
        for (auto& scalar : scalars_) {
            auto value = scalar.value;
            scalar.casedBefore = cased; scalar.afterI = afterI; scalar.afterSoftDotted = afterSoft;
            if (!Contains(data::CaseIgnorable, value)) cased = IsCased(value);
            if (!Contains(data::OtherCombining, value)) {
                afterI = value == 'I'; afterSoft = Contains(data::SoftDotted, value);
            }
        }
        bool beforeDot = (boundaries & 16) != 0, moreAbove = (boundaries & 32) != 0; cased = (boundaries & 2) != 0;
        for (auto it = scalars_.rbegin(); it != scalars_.rend(); ++it) {
            auto value = it->value;
            it->casedAfter = cased; it->beforeDot = beforeDot; it->moreAbove = moreAbove;
            if (!Contains(data::CaseIgnorable, value)) cased = IsCased(value);
            if (value == 0x0307) beforeDot = true;
            else if (!Contains(data::OtherCombining, value)) beforeDot = false;
            if (Contains(data::Above, value)) moreAbove = true;
            else if (!Contains(data::OtherCombining, value)) moreAbove = false;
        }
    }

    std::u16string Transform(size_t start, size_t length, Case mode, std::string_view language) const {
        if (start > source_.size() || length > source_.size() - start) throw std::out_of_range("Unicode casing source range");
        std::string tag(language);
        for (auto& value : tag) if (value >= 'A' && value <= 'Z') value += 'a' - 'A';
        auto separator = tag.find('-'); auto primary = tag.substr(0, separator);
        auto script = separator == std::string::npos ? std::string() : tag.substr(separator + 1, tag.find('-', separator + 1) - separator - 1);
        bool turkic = (primary == "tr" || primary == "az") && (script.size() != 4 || script == "latn");
        bool lithuanian = primary == "lt";
        std::u16string output; output.reserve(length);
        auto first = std::lower_bound(scalars_.begin(), scalars_.end(), start,
            [](const Scalar& scalar, size_t offset) { return scalar.start < offset; });
        auto end = start + length;
        if (length && (first == scalars_.end() || first->start != start ||
            (end < source_.size() && source_[end] >= 0xDC00 && source_[end] <= 0xDFFF &&
                source_[end - 1] >= 0xD800 && source_[end - 1] <= 0xDBFF)))
            throw std::invalid_argument("Unicode casing range splits a scalar");
        for (auto it = first; it != scalars_.end() && it->start < start + length; ++it) {
            auto value = it->value;
            if (mode == Case::Upper) {
                if (lithuanian && value == 0x0307 && it->afterSoftDotted) continue;
                if (turkic && value == 'i') { output.push_back(0x0130); continue; }
                Append(output, value, data::UpperMap, data::UpperText);
            } else {
                if (value == 0x03A3) { output.push_back(it->casedBefore && !it->casedAfter ? 0x03C2 : 0x03C3); continue; }
                if (turkic) {
                    if (value == 0x0130) { output.push_back('i'); continue; }
                    if (value == 'I') { output.push_back(it->beforeDot ? 'i' : 0x0131); continue; }
                    if (value == 0x0307 && it->afterI) continue;
                }
                if (lithuanian) {
                    if (value == 0x00CC) { output.append(u"i\u0307\u0300"); continue; }
                    if (value == 0x00CD) { output.append(u"i\u0307\u0301"); continue; }
                    if (value == 0x0128) { output.append(u"i\u0307\u0303"); continue; }
                    if (it->moreAbove && (value == 'I' || value == 'J' || value == 0x012E)) {
                        output.push_back(value == 0x012E ? 0x012F : value == 'I' ? 'i' : 'j'); output.push_back(0x0307); continue;
                    }
                }
                Append(output, value, data::LowerMap, data::LowerText);
            }
        }
        return output;
    }
};

} // namespace jalium::unicode_casing
