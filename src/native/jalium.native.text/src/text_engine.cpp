#include "text_engine.h"
#include "font_provider.h"
#include "glyph_rasterizer.h"
#include "glyph_atlas.h"
#include "text_layout.h"

namespace jalium {

TextEngine::TextEngine() = default;

TextEngine::~TextEngine()
{
    // Order matters: destroy dependents first. FontFace instances are owned by
    // each JaliumTextFormat (RAII), so there is no library handle to tear down.
    glyphAtlas_.reset();
    glyphRasterizer_.reset();
    fontProvider_.reset();
}

JaliumResult TextEngine::Initialize()
{
    // Platform-specific font discovery (no FreeType — discovery libraries only).
#if defined(__ANDROID__)
    fontProvider_ = std::make_unique<FontProviderAndroid>();
#elif defined(__linux__)
    fontProvider_ = std::make_unique<FontProviderFontconfig>();
#else
    // Windows/Apple use their native text stacks; this engine is not used there.
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif

    glyphRasterizer_ = std::make_unique<GlyphRasterizer>();
    glyphAtlas_ = std::make_unique<GlyphAtlas>();

    return JALIUM_OK;
}

TextFormat* TextEngine::CreateTextFormat(
    const wchar_t* fontFamily,
    float fontSize,
    int32_t fontWeight,
    int32_t fontStyle)
{
    return new JaliumTextFormat(this, fontFamily, fontSize, fontWeight, fontStyle);
}

} // namespace jalium
