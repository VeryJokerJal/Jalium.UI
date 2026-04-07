#include "text_shaper.h"

#include <ft2build.h>
#include FT_FREETYPE_H

#include <hb.h>
#include <hb-ft.h>

#include <cstring>

namespace jalium {

TextShaper::TextShaper()
{
    hbBuffer_ = hb_buffer_create();
}

TextShaper::~TextShaper()
{
    if (hbBuffer_)
        hb_buffer_destroy(hbBuffer_);
}

ShapedRun TextShaper::Shape(
    FT_Face face,
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

    // Set FreeType face size
    FT_Set_Char_Size(face, 0,
        static_cast<FT_F26Dot6>(fontSizePx * 64.0f), 72, 72);

    // Create HarfBuzz font from FreeType face
    hb_font_t* hbFont = hb_ft_font_create_referenced(face);

    // Reset buffer
    hb_buffer_reset(hbBuffer_);
    hb_buffer_set_direction(hbBuffer_, isRtl ? HB_DIRECTION_RTL : HB_DIRECTION_LTR);
    hb_buffer_set_script(hbBuffer_, HB_SCRIPT_COMMON);

    // Add text to buffer
    // wchar_t is 4 bytes on Linux (UTF-32), 2 bytes on Windows (UTF-16)
    if constexpr (sizeof(wchar_t) == 4)
    {
        hb_buffer_add_utf32(hbBuffer_,
            reinterpret_cast<const uint32_t*>(text),
            static_cast<int>(textLength), 0, static_cast<int>(textLength));
    }
    else
    {
        hb_buffer_add_utf16(hbBuffer_,
            reinterpret_cast<const uint16_t*>(text),
            static_cast<int>(textLength), 0, static_cast<int>(textLength));
    }

    hb_buffer_guess_segment_properties(hbBuffer_);

    // Shape
    hb_shape(hbFont, hbBuffer_, nullptr, 0);

    // Extract glyph info
    unsigned int glyphCount = 0;
    hb_glyph_info_t* glyphInfo = hb_buffer_get_glyph_infos(hbBuffer_, &glyphCount);
    hb_glyph_position_t* glyphPos = hb_buffer_get_glyph_positions(hbBuffer_, &glyphCount);

    run.glyphs.reserve(glyphCount);

    for (unsigned int i = 0; i < glyphCount; i++)
    {
        ShapedGlyph sg{};
        sg.glyphIndex = glyphInfo[i].codepoint;
        sg.cluster = glyphInfo[i].cluster;
        sg.advanceX = static_cast<float>(glyphPos[i].x_advance) / 64.0f;
        sg.advanceY = static_cast<float>(glyphPos[i].y_advance) / 64.0f;
        sg.offsetX = static_cast<float>(glyphPos[i].x_offset) / 64.0f;
        sg.offsetY = static_cast<float>(glyphPos[i].y_offset) / 64.0f;
        run.glyphs.push_back(sg);
    }

    hb_font_destroy(hbFont);

    return run;
}

} // namespace jalium
