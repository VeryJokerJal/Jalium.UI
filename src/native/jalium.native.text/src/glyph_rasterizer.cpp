#include "glyph_rasterizer.h"

#include <ft2build.h>
#include FT_FREETYPE_H
#include FT_GLYPH_H
#include FT_BITMAP_H
#include FT_LCD_FILTER_H

#include <cstring>
#include <algorithm>

namespace jalium {

GlyphRasterizer::GlyphRasterizer(FT_Library ftLib)
    : ftLibrary_(ftLib)
#if defined(__ANDROID__)
    , subpixelMode_(SubpixelMode::None)
#else
    , subpixelMode_(SubpixelMode::Horizontal)
#endif
{
}

GlyphRasterizer::~GlyphRasterizer() = default;

RasterizedGlyph GlyphRasterizer::Rasterize(
    FT_Face face,
    uint32_t glyphIndex,
    float fontSizePx,
    uint8_t subpixelX)
{
    RasterizedGlyph result{};

    if (!face || glyphIndex == 0)
        return result;

    // Set character size (FreeType uses 26.6 fixed point, 1/64th of a pixel)
    FT_Error err = FT_Set_Char_Size(face, 0,
        static_cast<FT_F26Dot6>(fontSizePx * 64.0f), 72, 72);
    if (err) return result;

    // Apply sub-pixel offset using FreeType's sub-pixel positioning
    // subpixelX is 0..3, representing 0, 0.25, 0.5, 0.75 pixel offsets
    FT_Vector subpixelOffset{};
    subpixelOffset.x = static_cast<FT_Pos>(subpixelX * 16); // 26.6 format: 16 = 0.25 pixels
    subpixelOffset.y = 0;
    FT_Set_Transform(face, nullptr, &subpixelOffset);

    // Load glyph
    FT_Int32 loadFlags = FT_LOAD_DEFAULT;
    if (subpixelMode_ == SubpixelMode::Horizontal)
        loadFlags |= FT_LOAD_TARGET_LCD;
    else if (subpixelMode_ == SubpixelMode::Vertical)
        loadFlags |= FT_LOAD_TARGET_LCD_V;

    err = FT_Load_Glyph(face, glyphIndex, loadFlags);
    if (err) return result;

    // Render glyph
    FT_Render_Mode renderMode = FT_RENDER_MODE_NORMAL;
    if (subpixelMode_ == SubpixelMode::Horizontal)
        renderMode = FT_RENDER_MODE_LCD;
    else if (subpixelMode_ == SubpixelMode::Vertical)
        renderMode = FT_RENDER_MODE_LCD_V;

    err = FT_Render_Glyph(face->glyph, renderMode);
    if (err) return result;

    FT_Bitmap& bitmap = face->glyph->bitmap;

    // Fill result metrics
    result.bearingX = face->glyph->bitmap_left;
    result.bearingY = face->glyph->bitmap_top;
    result.advanceX = static_cast<float>(face->glyph->advance.x) / 64.0f;

    if (bitmap.width == 0 || bitmap.rows == 0)
    {
        // Space character or empty glyph
        result.width = 0;
        result.height = 0;
        return result;
    }

    // Convert FreeType bitmap to RGBA8
    if (subpixelMode_ == SubpixelMode::Horizontal && bitmap.pixel_mode == FT_PIXEL_MODE_LCD)
    {
        // LCD mode: bitmap.width is 3x the actual pixel width (R,G,B sub-pixels)
        int32_t pixelWidth = static_cast<int32_t>(bitmap.width / 3);
        int32_t pixelHeight = static_cast<int32_t>(bitmap.rows);

        result.width = pixelWidth;
        result.height = pixelHeight;
        result.hasSubpixel = true;
        result.pixels.resize(pixelWidth * pixelHeight * 4);

        for (int32_t y = 0; y < pixelHeight; y++)
        {
            const uint8_t* src = bitmap.buffer + y * bitmap.pitch;
            uint8_t* dst = result.pixels.data() + y * pixelWidth * 4;

            for (int32_t x = 0; x < pixelWidth; x++)
            {
                uint8_t r = src[x * 3 + 0];
                uint8_t g = src[x * 3 + 1];
                uint8_t b = src[x * 3 + 2];
                uint8_t a = std::max({r, g, b}); // Alpha = max coverage

                dst[x * 4 + 0] = r;
                dst[x * 4 + 1] = g;
                dst[x * 4 + 2] = b;
                dst[x * 4 + 3] = a;
            }
        }
    }
    else if (bitmap.pixel_mode == FT_PIXEL_MODE_GRAY)
    {
        // Grayscale mode: single-channel coverage
        int32_t pixelWidth = static_cast<int32_t>(bitmap.width);
        int32_t pixelHeight = static_cast<int32_t>(bitmap.rows);

        result.width = pixelWidth;
        result.height = pixelHeight;
        result.hasSubpixel = false;
        result.pixels.resize(pixelWidth * pixelHeight * 4);

        for (int32_t y = 0; y < pixelHeight; y++)
        {
            const uint8_t* src = bitmap.buffer + y * bitmap.pitch;
            uint8_t* dst = result.pixels.data() + y * pixelWidth * 4;

            for (int32_t x = 0; x < pixelWidth; x++)
            {
                uint8_t coverage = src[x];
                dst[x * 4 + 0] = coverage; // R
                dst[x * 4 + 1] = coverage; // G
                dst[x * 4 + 2] = coverage; // B
                dst[x * 4 + 3] = coverage; // A
            }
        }
    }
    else if (bitmap.pixel_mode == FT_PIXEL_MODE_MONO)
    {
        // Monochrome bitmap
        int32_t pixelWidth = static_cast<int32_t>(bitmap.width);
        int32_t pixelHeight = static_cast<int32_t>(bitmap.rows);

        result.width = pixelWidth;
        result.height = pixelHeight;
        result.hasSubpixel = false;
        result.pixels.resize(pixelWidth * pixelHeight * 4);

        for (int32_t y = 0; y < pixelHeight; y++)
        {
            const uint8_t* src = bitmap.buffer + y * bitmap.pitch;
            uint8_t* dst = result.pixels.data() + y * pixelWidth * 4;

            for (int32_t x = 0; x < pixelWidth; x++)
            {
                uint8_t bit = (src[x >> 3] >> (7 - (x & 7))) & 1;
                uint8_t val = bit ? 255 : 0;
                dst[x * 4 + 0] = val;
                dst[x * 4 + 1] = val;
                dst[x * 4 + 2] = val;
                dst[x * 4 + 3] = val;
            }
        }
    }

    // Reset transform
    FT_Set_Transform(face, nullptr, nullptr);

    return result;
}

} // namespace jalium
