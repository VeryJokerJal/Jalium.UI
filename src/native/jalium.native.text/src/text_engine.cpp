#include "text_engine.h"
#include "font_provider.h"
#include "glyph_rasterizer.h"
#include "glyph_atlas.h"
#include "text_layout.h"

#include <ft2build.h>
#include FT_FREETYPE_H
#include FT_LCD_FILTER_H

namespace jalium {

TextEngine::TextEngine() = default;

TextEngine::~TextEngine()
{
    // Order matters: destroy dependents first
    glyphAtlas_.reset();
    glyphRasterizer_.reset();
    fontProvider_.reset();

    if (ftLibrary_)
    {
        FT_Done_FreeType(ftLibrary_);
        ftLibrary_ = nullptr;
    }
}

JaliumResult TextEngine::Initialize()
{
    // Initialize FreeType
    FT_Error err = FT_Init_FreeType(&ftLibrary_);
    if (err != 0)
        return JALIUM_ERROR_INITIALIZATION_FAILED;

    // Enable LCD filter for sub-pixel rendering
    FT_Library_SetLcdFilter(ftLibrary_, FT_LCD_FILTER_DEFAULT);

    // Create platform-specific font provider
#if defined(__ANDROID__)
    fontProvider_ = std::make_unique<FontProviderAndroid>();
#elif defined(__linux__)
    fontProvider_ = std::make_unique<FontProviderFontconfig>();
#else
    // Fallback: should not reach here on Windows (uses DirectWrite)
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif

    // Create glyph rasterizer
    glyphRasterizer_ = std::make_unique<GlyphRasterizer>(ftLibrary_);

#if defined(__ANDROID__)
    glyphRasterizer_->SetSubpixelMode(SubpixelMode::None);
#else
    glyphRasterizer_->SetSubpixelMode(SubpixelMode::Horizontal);
#endif

    // Create glyph atlas
    glyphAtlas_ = std::make_unique<GlyphAtlas>();

    return JALIUM_OK;
}

TextFormat* TextEngine::CreateTextFormat(
    const wchar_t* fontFamily,
    float fontSize,
    int32_t fontWeight,
    int32_t fontStyle)
{
    return new FreeTypeTextFormat(this, fontFamily, fontSize, fontWeight, fontStyle);
}

} // namespace jalium
