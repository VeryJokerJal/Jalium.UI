#pragma once

#include <cstdint>
#include <vector>

namespace jalium {

class FontFace;

// ============================================================================
// GlyphRasterizer: self-hosted glyph rasterization
//
// Rasterizes individual glyphs into RGBA bitmaps from a FontFace's font-unit
// outlines (glyf or CFF), scaling + y-flipping them and filling via the shared
// analytic-AA scanline rasterizer (RasterizePathToRects, NonZero). Produces
// premultiplied grayscale coverage (R=G=B=A). Optional LCD sub-pixel path is
// deferred; the default mode is grayscale on every platform.
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
    bool     isColor = false;     ///< True when pixels contain authored color (premultiplied RGBA)
};

/// Sub-pixel rendering mode.
enum class SubpixelMode {
    None,       ///< Grayscale anti-aliasing only
    Horizontal, ///< Horizontal LCD sub-pixel (RGB stripes)
    Vertical,   ///< Vertical LCD sub-pixel (RGB stripes)
};

enum class GlyphAntialiasMode : uint8_t {
    Aliased = 1,
    Grayscale = 2,
    HorizontalLcd = 3,
};

class GlyphRasterizer {
public:
    GlyphRasterizer();
    ~GlyphRasterizer();

    /// Sets the sub-pixel rendering mode.
    /// Desktop defaults to Horizontal; Android defaults to None.
    void SetSubpixelMode(SubpixelMode mode) { subpixelMode_ = mode; }

    /// Rasterizes a single glyph at the given size and sub-pixel offset.
    /// @param face Self-hosted font face (caller retains ownership).
    /// @param glyphIndex Glyph index from shaping.
    /// @param fontSizePx Font size in pixels.
    /// @param subpixelX Sub-pixel X offset quantized to 1/8 pixel (0..7).
    /// @return Rasterized glyph data, or empty on failure.
    RasterizedGlyph Rasterize(
        FontFace* face,
        uint32_t glyphIndex,
        float fontSizePx,
        uint8_t subpixelX = 0,
        GlyphAntialiasMode antialiasMode = GlyphAntialiasMode::Grayscale);

private:
    SubpixelMode subpixelMode_;
};

} // namespace jalium
