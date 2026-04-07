#pragma once

#include "jalium_types.h"
#include "jalium_backend.h"
#include "text_shaper.h"
#include "glyph_atlas.h"

#include <string>
#include <vector>

namespace jalium {

class TextEngine;
class GlyphRasterizer;

// ============================================================================
// FreeTypeTextFormat: Cross-platform TextFormat using FreeType + HarfBuzz
//
// Implements the jalium::TextFormat abstract interface using:
// - HarfBuzz for text shaping
// - FreeType for metrics and glyph rasterization
// - Custom layout engine for line breaking, alignment, and hit testing
// ============================================================================

class FreeTypeTextFormat : public TextFormat {
public:
    FreeTypeTextFormat(
        TextEngine* engine,
        const wchar_t* fontFamily,
        float fontSize,
        int32_t fontWeight,
        int32_t fontStyle);
    ~FreeTypeTextFormat() override;

    // TextFormat interface
    void SetAlignment(int32_t alignment) override;
    void SetParagraphAlignment(int32_t alignment) override;
    void SetTrimming(int32_t trimming) override;
    void SetWordWrapping(int32_t wrapping) override;
    void SetLineSpacing(int32_t method, float spacing, float baseline) override;
    void SetMaxLines(uint32_t maxLines) override;

    JaliumResult MeasureText(
        const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight,
        JaliumTextMetrics* metrics) override;

    JaliumResult GetFontMetrics(JaliumTextMetrics* metrics) override;

    JaliumResult HitTestPoint(
        const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight,
        float pointX, float pointY,
        JaliumTextHitTestResult* result) override;

    JaliumResult HitTestTextPosition(
        const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight,
        uint32_t textPosition, int32_t isTrailingHit,
        JaliumTextHitTestResult* result) override;

    // Additional methods for rendering
    /// Generates glyph quads for the text shader.
    /// @param text UTF-16 text.
    /// @param textLength Number of characters.
    /// @param maxWidth Maximum layout width.
    /// @param maxHeight Maximum layout height.
    /// @param colorR, colorG, colorB, colorA Premultiplied text color.
    /// @param originX, originY Screen position of the text layout origin.
    /// @param outQuads Receives the generated glyph quads.
    void GenerateGlyphQuads(
        const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight,
        float colorR, float colorG, float colorB, float colorA,
        float originX, float originY,
        std::vector<TextGlyphQuad>& outQuads,
        float renderScale = 1.0f);

    /// Gets the FreeType face used by this format.
    FT_Face GetFace() const { return face_; }

    /// Gets the font ID for atlas lookup.
    uint64_t GetFontId() const { return fontId_; }

    /// Gets the font size in pixels.
    float GetFontSizePx() const { return fontSizePx_; }

private:
    // Layout engine internal types
    struct LayoutLine {
        uint32_t startIndex;        ///< Start character index in text
        uint32_t endIndex;          ///< End character index (exclusive)
        float    width;             ///< Line width in pixels
        float    baselineY;         ///< Y position of baseline
        std::vector<ShapedGlyph> glyphs;
    };

    struct LayoutResult {
        std::vector<LayoutLine> lines;
        float totalWidth;
        float totalHeight;
    };

    LayoutResult PerformLayout(
        const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight);

    void ApplyAlignment(LayoutResult& layout, float maxWidth, float maxHeight);

    // Font state
    TextEngine*     engine_;
    FT_Face         face_ = nullptr;
    uint64_t        fontId_ = 0;
    float           fontSizePx_;
    std::wstring    fontFamily_;
    int32_t         fontWeight_;
    int32_t         fontStyle_;

    // Layout settings
    int32_t  alignment_ = 0;           ///< JaliumTextAlignment
    int32_t  paragraphAlignment_ = 0;  ///< JaliumParagraphAlignment
    int32_t  trimming_ = 0;            ///< JaliumTextTrimming
    int32_t  wrapping_ = 0;            ///< JaliumWordWrapping
    float    lineSpacing_ = 0.0f;
    float    lineSpacingBaseline_ = 0.0f;
    int32_t  lineSpacingMethod_ = 0;
    uint32_t maxLines_ = 0;

    // Cached metrics
    float    ascent_ = 0.0f;
    float    descent_ = 0.0f;
    float    lineGap_ = 0.0f;
    float    lineHeight_ = 0.0f;

    // Shaper
    TextShaper shaper_;
};

} // namespace jalium
