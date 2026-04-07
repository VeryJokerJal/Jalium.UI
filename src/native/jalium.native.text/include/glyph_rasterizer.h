#pragma once

#include <cstdint>
#include <vector>

typedef struct FT_LibraryRec_* FT_Library;
typedef struct FT_FaceRec_*    FT_Face;

namespace jalium {

// ============================================================================
// GlyphRasterizer: FreeType-based glyph rasterization
//
// Rasterizes individual glyphs into RGBA bitmaps using FreeType.
// - Desktop (Linux): FT_RENDER_MODE_LCD for sub-pixel (ClearType-equivalent)
// - Mobile (Android): FT_RENDER_MODE_NORMAL for grayscale anti-aliasing
// ============================================================================

/// Rasterized glyph bitmap data.
struct RasterizedGlyph {
    std::vector<uint8_t> pixels;  ///< RGBA8 pixel data (premultiplied alpha)
    int32_t  width = 0;          ///< Bitmap width in pixels
    int32_t  height = 0;         ///< Bitmap height in pixels
    int32_t  bearingX = 0;       ///< Horizontal offset from pen position (pixels)
    int32_t  bearingY = 0;       ///< Vertical offset from baseline (pixels, positive = up)
    float    advanceX = 0.0f;    ///< Horizontal advance width
    bool     hasSubpixel = false; ///< True if rasterized with LCD sub-pixel rendering
};

/// Sub-pixel rendering mode.
enum class SubpixelMode {
    None,       ///< Grayscale anti-aliasing only
    Horizontal, ///< Horizontal LCD sub-pixel (RGB stripes)
    Vertical,   ///< Vertical LCD sub-pixel (RGB stripes)
};

class GlyphRasterizer {
public:
    explicit GlyphRasterizer(FT_Library ftLib);
    ~GlyphRasterizer();

    /// Sets the sub-pixel rendering mode.
    /// Desktop defaults to Horizontal; Android defaults to None.
    void SetSubpixelMode(SubpixelMode mode) { subpixelMode_ = mode; }

    /// Rasterizes a single glyph at the given size and sub-pixel offset.
    /// @param face FreeType font face (caller retains ownership).
    /// @param glyphIndex Glyph index from HarfBuzz shaping.
    /// @param fontSizePx Font size in pixels.
    /// @param subpixelX Sub-pixel X offset quantized to 1/4 pixel (0..3).
    /// @return Rasterized glyph data, or empty on failure.
    RasterizedGlyph Rasterize(
        FT_Face face,
        uint32_t glyphIndex,
        float fontSizePx,
        uint8_t subpixelX = 0);

private:
    FT_Library   ftLibrary_;
    SubpixelMode subpixelMode_;
};

} // namespace jalium
