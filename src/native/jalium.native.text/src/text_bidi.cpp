#include "text_bidi.h"
#include <algorithm>
#include <new>

namespace jalium {
namespace {
uint32_t Codepoint(const std::wstring& text, uint32_t index, uint32_t& units)
{
    units = 1;
    uint32_t cp = uint32_t(text[index]);
    if constexpr (sizeof(wchar_t) == 2) {
        if (cp >= 0xD800 && cp <= 0xDBFF && index + 1 < text.size()) {
            const uint32_t low = uint32_t(text[index + 1]);
            if (low >= 0xDC00 && low <= 0xDFFF) {
                units = 2;
                cp = 0x10000 + ((cp - 0xD800) << 10) + low - 0xDC00;
            }
        }
    }
    return cp;
}
}

TextBidi::TextBidi(const wchar_t* text, uint32_t length, int direction)
    : text_(text ? std::wstring(text, length) : std::wstring()), mirrored_(text_), levels_(text_.size())
{
    if (text_.empty()) return;
    try {
        const SBCodepointSequence sequence {sizeof(wchar_t) == 2 ? SBStringEncodingUTF16 : SBStringEncodingUTF32,
            text_.data(), text_.size()};
        algorithm_ = SBAlgorithmCreate(&sequence);
        if (!algorithm_) throw std::bad_alloc();
        for (SBUInteger start = 0; start < text_.size();) {
            auto paragraph = SBAlgorithmCreateParagraph(algorithm_, start, text_.size() - start,
                direction < 0 ? SBLevelDefaultLTR : SBLevel(direction));
            if (!paragraph) throw std::bad_alloc();
            try { paragraphs_.push_back(paragraph); }
            catch (...) { SBParagraphRelease(paragraph); throw; }
            const auto count = SBParagraphGetLength(paragraph);
            const auto* levels = SBParagraphGetLevelsPtr(paragraph);
            std::copy(levels, levels + count, levels_.begin() + start);
            start += count;
        }
        uniform_ = std::all_of(levels_.begin(), levels_.end(), [&](uint8_t value) { return value == levels_.front(); });
        for (uint32_t i = 0; i < text_.size();) {
            uint32_t units = 1;
            const uint32_t cp = Codepoint(text_, i, units);
            const auto mirror = (levels_[i] & 1) ? SBCodepointGetMirror(cp) : 0;
            if (mirror && mirror != SBCodepointInvalid && mirror != cp) {
                if constexpr (sizeof(wchar_t) == 4) mirrored_[i] = wchar_t(mirror);
                else if (units == 1 && mirror <= 0xFFFF) mirrored_[i] = wchar_t(mirror);
                else if (units == 2 && mirror > 0xFFFF) {
                    mirrored_[i] = wchar_t(0xD800 + ((mirror - 0x10000) >> 10));
                    mirrored_[i + 1] = wchar_t(0xDC00 + ((mirror - 0x10000) & 0x3FF));
                }
            }
            i += units;
        }
    } catch (...) {
        for (auto paragraph : paragraphs_) SBParagraphRelease(paragraph);
        if (algorithm_) SBAlgorithmRelease(algorithm_);
        throw;
    }
}

TextBidi::~TextBidi()
{
    for (auto paragraph : paragraphs_) SBParagraphRelease(paragraph);
    if (algorithm_) SBAlgorithmRelease(algorithm_);
}

bool TextBidi::IsFormattingControl(uint32_t cp)
{
    return cp == 0x061C || cp == 0x200E || cp == 0x200F ||
        (cp >= 0x202A && cp <= 0x202E) || (cp >= 0x2066 && cp <= 0x2069);
}

bool TextBidi::IsLineBreak(uint32_t cp)
{
    return cp == '\n' || cp == '\r' || cp == 0x85 || cp == 0x2028 || cp == 0x2029 ||
        (cp >= 0x1C && cp <= 0x1E);
}

void TextBidi::Reorder(std::vector<ShapedGlyph>& glyphs, uint32_t start, uint32_t end) const
{
    if (glyphs.empty() || end <= start) return;
    std::vector<ShapedGlyph> visual;
    visual.reserve(glyphs.size());
    for (auto paragraph : paragraphs_) {
        const auto pStart = uint32_t(SBParagraphGetOffset(paragraph));
        const auto pEnd = pStart + uint32_t(SBParagraphGetLength(paragraph));
        const auto first = std::max(start, pStart), last = std::min(end, pEnd);
        if (last <= first) continue;
        auto line = SBParagraphCreateLine(paragraph, first, last - first);
        if (!line) throw std::bad_alloc();
        try {
            const auto* runs = SBLineGetRunsPtr(line);
            const auto count = SBLineGetRunCount(line);
            for (SBUInteger i = 0; i < count; ++i) {
                const auto& run = runs[i];
                const auto begin = std::lower_bound(glyphs.begin(), glyphs.end(), run.offset,
                    [](const ShapedGlyph& glyph, SBUInteger offset) { return glyph.cluster < offset; });
                const auto finish = std::lower_bound(begin, glyphs.end(), run.offset + run.length,
                    [](const ShapedGlyph& glyph, SBUInteger offset) { return glyph.cluster < offset; });
                if (!(run.level & 1)) {
                    for (auto item = begin; item != finish; ++item) {
                        visual.push_back(*item); visual.back().bidiLevel = run.level;
                    }
                } else {
                    // L3: keep a base and its nonspacing/enclosing marks together.
                    auto cursor = finish;
                    while (cursor != begin) {
                        auto group = cursor - 1;
                        while (group != begin) {
                            uint32_t units = 1;
                            const auto category = SBCodepointGetGeneralCategory(Codepoint(text_, group->cluster, units));
                            if (category != SBGeneralCategoryMN && category != SBGeneralCategoryME) break;
                            --group;
                        }
                        for (auto item = group; item != cursor; ++item) {
                            visual.push_back(*item); visual.back().bidiLevel = run.level;
                        }
                        cursor = group;
                    }
                }
            }
        } catch (...) { SBLineRelease(line); throw; }
        SBLineRelease(line);
    }
    glyphs = std::move(visual);
}
} // namespace jalium
