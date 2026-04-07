#pragma once

#include <cstdint>
#include <string>
#include <vector>

typedef struct FT_FaceRec_* FT_Face;

// Forward declare HarfBuzz types to avoid header dependency
typedef struct hb_font_t hb_font_t;
typedef struct hb_buffer_t hb_buffer_t;

namespace jalium {

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
};

/// A run of shaped glyphs with a single font and direction.
struct ShapedRun {
    std::vector<ShapedGlyph> glyphs;
    FT_Face                  face;      ///< Font face used (not owned)
    uint64_t                 fontId;    ///< Font identifier for atlas lookup
    float                    fontSize;  ///< Font size in pixels
    bool                     isRtl;     ///< Right-to-left run
};

class TextShaper {
public:
    TextShaper();
    ~TextShaper();

    /// Shapes a run of text with the given font face.
    /// @param face FreeType face to use for shaping.
    /// @param fontId Unique identifier for the font.
    /// @param text UTF-16 text to shape.
    /// @param textLength Number of wchar_t characters.
    /// @param fontSizePx Font size in pixels.
    /// @param isRtl True for right-to-left text.
    /// @return Shaped glyph run.
    ShapedRun Shape(
        FT_Face face,
        uint64_t fontId,
        const wchar_t* text,
        uint32_t textLength,
        float fontSizePx,
        bool isRtl = false);

private:
    hb_buffer_t* hbBuffer_ = nullptr;
};

} // namespace jalium
