#pragma once

// ot_shaper.h
//
// OtShaper — the self-hosted text shaper replacing HarfBuzz. Maps codepoints to
// glyphs via the face's cmap, applies a pragmatic subset of OpenType Layout
// (GSUB single + ligature for ccmp/liga/rlig; GPOS pair-kerning; legacy 'kern'
// fallback), and reverses strongly-RTL runs. Complex scripts (Arabic joining,
// Indic reordering, mark positioning) intentionally degrade to base glyphs with
// correct advances rather than failing. Advances/offsets are emitted in pixels
// (font units * fontSizePx / unitsPerEm) to match the previous HarfBuzz output.

#include <cstdint>
#include <vector>

namespace jalium { class FontFace; }

namespace jalium::font {

struct ShapedGlyphItem {
    uint32_t glyphIndex = 0;
    uint32_t cluster    = 0;   // absolute index into the input codepoints
    float    advanceX   = 0.0f;
    float    advanceY   = 0.0f;
    float    offsetX    = 0.0f;
    float    offsetY    = 0.0f;
};

class OtShaper {
public:
    // Shapes UTF-32 `codepoints` with `face` at `fontSizePx` into `out` (visual
    // order). `isRtl` forces RTL; when false, a strongly-RTL run is auto-reversed.
    void Shape(const jalium::FontFace& face,
               const uint32_t* codepoints, uint32_t count,
               float fontSizePx, bool isRtl,
               std::vector<ShapedGlyphItem>& out);
};

} // namespace jalium::font
