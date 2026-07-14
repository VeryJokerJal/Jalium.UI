#pragma once

#include "jalium_types.h"
#include "jalium_backend.h"
#include "jalium_text_api.h"

#include <memory>
#include <string>

namespace jalium {

class FontProvider;
class GlyphRasterizer;
class GlyphAtlas;
class TextLayout;

// ============================================================================
// TextEngine: Cross-platform text rendering engine (self-hosted font stack)
//
// Replaces DirectWrite on non-Windows platforms. Provides:
// - Font discovery (via FontProvider)
// - Text shaping (via the self-hosted OtShaper)
// - Glyph rasterization (via the self-hosted GlyphRasterizer + FontFace outlines)
// - Text layout (line breaking, word wrap, alignment, hit testing)
// - Glyph atlas management (CPU-side, 4096x4096 R8G8B8A8)
// ============================================================================

class JALIUM_TEXT_API TextEngine {
public:
    TextEngine();
    ~TextEngine();

    /// Initializes the text engine. Must be called before any other method.
    JaliumResult Initialize();

    /// Creates a TextFormat implementation on the self-hosted font stack.
    TextFormat* CreateTextFormat(
        const wchar_t* fontFamily,
        float fontSize,
        int32_t fontWeight,
        int32_t fontStyle);

    /// Gets the glyph atlas (for GPU upload).
    GlyphAtlas* GetGlyphAtlas() { return glyphAtlas_.get(); }

    /// Gets the font provider.
    FontProvider* GetFontProvider() { return fontProvider_.get(); }

    /// Gets the glyph rasterizer.
    GlyphRasterizer* GetGlyphRasterizer() { return glyphRasterizer_.get(); }

private:
    std::unique_ptr<FontProvider>       fontProvider_;
    std::unique_ptr<GlyphRasterizer>    glyphRasterizer_;
    std::unique_ptr<GlyphAtlas>         glyphAtlas_;
};

} // namespace jalium
