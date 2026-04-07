#pragma once

#include "jalium_types.h"
#include "jalium_backend.h"

#include <memory>
#include <string>

// Forward declarations for FreeType
typedef struct FT_LibraryRec_* FT_Library;
typedef struct FT_FaceRec_*    FT_Face;

namespace jalium {

class FontProvider;
class GlyphRasterizer;
class GlyphAtlas;
class TextLayout;

// ============================================================================
// TextEngine: Cross-platform text rendering engine using FreeType + HarfBuzz
//
// Replaces DirectWrite on non-Windows platforms. Provides:
// - Font discovery (via FontProvider)
// - Text shaping (via HarfBuzz)
// - Glyph rasterization (via FreeType, with sub-pixel rendering on desktop)
// - Text layout (line breaking, word wrap, alignment, hit testing)
// - Glyph atlas management (CPU-side, 4096x4096 R8G8B8A8)
// ============================================================================

class TextEngine {
public:
    TextEngine();
    ~TextEngine();

    /// Initializes the text engine. Must be called before any other method.
    JaliumResult Initialize();

    /// Creates a TextFormat implementation using FreeType+HarfBuzz.
    TextFormat* CreateTextFormat(
        const wchar_t* fontFamily,
        float fontSize,
        int32_t fontWeight,
        int32_t fontStyle);

    /// Gets the glyph atlas (for GPU upload).
    GlyphAtlas* GetGlyphAtlas() { return glyphAtlas_.get(); }

    /// Gets the FreeType library handle.
    FT_Library GetFTLibrary() { return ftLibrary_; }

    /// Gets the font provider.
    FontProvider* GetFontProvider() { return fontProvider_.get(); }

    /// Gets the glyph rasterizer.
    GlyphRasterizer* GetGlyphRasterizer() { return glyphRasterizer_.get(); }

private:
    FT_Library                          ftLibrary_ = nullptr;
    std::unique_ptr<FontProvider>       fontProvider_;
    std::unique_ptr<GlyphRasterizer>    glyphRasterizer_;
    std::unique_ptr<GlyphAtlas>         glyphAtlas_;
};

} // namespace jalium
