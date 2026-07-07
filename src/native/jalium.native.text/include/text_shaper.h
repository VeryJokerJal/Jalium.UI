#pragma once

#include <cstdint>
#include <string>
#include <vector>

#include "ot_shaper.h"

namespace jalium {

class FontFace;

// ============================================================================
// TextShaper: HarfBuzz text shaping wrapper
//
// Performs text shaping to produce positioned glyph runs from Unicode text.
// Handles complex scripts, ligatures, kerning, and bidirectional text.
// ============================================================================

/// A single shaped glyph with positioning information.
struct ShapedGlyph {
    uint32_t glyphIndex;    ///< Glyph ID in the font
    uint32_t cluster;       ///< Index into original text (character offset)
    float    advanceX;      ///< Horizontal advance
    float    advanceY;      ///< Vertical advance
    float    offsetX;       ///< Horizontal offset from pen position
    float    offsetY;       ///< Vertical offset from pen position
    // Face/font this glyph must be rasterized from. Normally the format's
    // primary face, but for codepoints the primary lacks (e.g. CJK rendered
    // through a Noto Sans CJK fallback) these point at the fallback face so the
    // atlas lookup uses the correct face + a collision-free fontId.
    FontFace* face = nullptr;
    uint64_t  fontId = 0;
};

/// A run of shaped glyphs with a single font and direction.
struct ShapedRun {
    std::vector<ShapedGlyph> glyphs;
    FontFace*                face;      ///< Font face used (not owned)
    uint64_t                 fontId;    ///< Font identifier for atlas lookup
    float                    fontSize;  ///< Font size in pixels
    bool                     isRtl;     ///< Right-to-left run
};

class TextShaper {
public:
    TextShaper();
    ~TextShaper();

    /// Shapes a run of text with the given font face.
    /// @param face Font face to use for shaping.
    /// @param fontId Unique identifier for the font.
    /// @param text UTF-16 text to shape.
    /// @param textLength Number of wchar_t characters.
    /// @param fontSizePx Font size in pixels.
    /// @param isRtl True for right-to-left text.
    /// @return Shaped glyph run.
    ShapedRun Shape(
        FontFace* face,
        uint64_t fontId,
        const wchar_t* text,
        uint32_t textLength,
        float fontSizePx,
        bool isRtl = false);

private:
    font::OtShaper otShaper_;
};

} // namespace jalium
