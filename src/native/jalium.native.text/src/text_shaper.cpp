#include "text_shaper.h"
#include "font_face.h"

#include <vector>

namespace jalium {

TextShaper::TextShaper() = default;
TextShaper::~TextShaper() = default;

ShapedRun TextShaper::Shape(
    FontFace* face,
    uint64_t fontId,
    const wchar_t* text,
    uint32_t textLength,
    float fontSizePx,
    bool isRtl)
{
    ShapedRun run{};
    run.face = face;
    run.fontId = fontId;
    run.fontSize = fontSizePx;
    run.isRtl = isRtl;

    if (!face || !text || textLength == 0)
        return run;

    // Decode wchar_t text into Unicode codepoints, tracking the original wchar_t
    // index of each so cluster values stay in the caller's text coordinate space.
    // wchar_t is 4 bytes (UTF-32) on Linux/Android; 2 bytes (UTF-16) elsewhere.
    std::vector<uint32_t> cps;
    std::vector<uint32_t> clusterMap;   // codepoint index -> wchar_t index
    cps.reserve(textLength);
    clusterMap.reserve(textLength);
    if constexpr (sizeof(wchar_t) == 4) {
        for (uint32_t i = 0; i < textLength; ++i) { cps.push_back(static_cast<uint32_t>(text[i])); clusterMap.push_back(i); }
    } else {
        for (uint32_t i = 0; i < textLength; ) {
            uint16_t ch = static_cast<uint16_t>(text[i]);
            if (ch >= 0xD800 && ch <= 0xDBFF && i + 1 < textLength) {
                uint16_t lo = static_cast<uint16_t>(text[i + 1]);
                if (lo >= 0xDC00 && lo <= 0xDFFF) {
                    cps.push_back(0x10000u + ((static_cast<uint32_t>(ch) - 0xD800u) << 10) + (static_cast<uint32_t>(lo) - 0xDC00u));
                    clusterMap.push_back(i);
                    i += 2; continue;
                }
            }
            cps.push_back(ch); clusterMap.push_back(i); ++i;
        }
    }

    std::vector<font::ShapedGlyphItem> items;
    otShaper_.Shape(*face, cps.data(), static_cast<uint32_t>(cps.size()), fontSizePx, isRtl, items);

    run.glyphs.reserve(items.size());
    for (const auto& it : items) {
        ShapedGlyph sg{};
        sg.glyphIndex = it.glyphIndex;
        sg.cluster = (it.cluster < clusterMap.size()) ? clusterMap[it.cluster] : it.cluster;
        sg.advanceX = it.advanceX;
        sg.advanceY = it.advanceY;
        sg.offsetX = it.offsetX;
        sg.offsetY = it.offsetY;
        sg.face = face;
        sg.fontId = fontId;
        run.glyphs.push_back(sg);
    }
    return run;
}

} // namespace jalium
