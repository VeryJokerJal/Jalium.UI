#pragma once

#include "jalium_types.h"
#include "jalium_backend.h"
#include "text_shaper.h"
#include "glyph_atlas.h"
#include "font_face.h"

#include <memory>
#include <string>
#include <vector>

namespace jalium {

class TextEngine;
class GlyphRasterizer;

// ============================================================================
// JaliumTextFormat: Cross-platform TextFormat on the self-hosted font stack
//
// Implements the jalium::TextFormat abstract interface using:
// - OtShaper (self-hosted) for text shaping
// - FontFace (self-hosted) for metrics and glyph rasterization
// - Custom layout engine for line breaking, alignment, and hit testing
// ============================================================================

class JaliumTextFormat : public TextFormat {
public:
    JaliumTextFormat(
        TextEngine* engine,
        const wchar_t* fontFamily,
        float fontSize,
        int32_t fontWeight,
        int32_t fontStyle);
    ~JaliumTextFormat() override;

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

    /// Gets the font face used by this format.
    FontFace* GetFace() const { return face_.get(); }

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

    /// Shapes text, splitting it into per-face runs so codepoints the primary
    /// face lacks (e.g. CJK) are shaped+measured with a Unicode fallback face.
    /// All-primary text takes a fast path identical to shaping with face_ alone.
    ShapedRun ShapeWithFallback(const wchar_t* text, uint32_t textLength);

    /// Picks the face that should render a codepoint: primary if it has the
    /// glyph, else the fallback face if it does, else primary (renders .notdef).
    FontFace* ChooseFaceForCodepoint(uint32_t codepoint, uint64_t& outFontId) const;

    /// Lazily loads fallbackFace_ from the provider's fallback family. No-op
    /// after the first attempt; safe when the platform has no fallback font.
    void EnsureFallbackFace();

    // Font state
    TextEngine*     engine_;
    std::unique_ptr<FontFace> face_;
    uint64_t        fontId_ = 0;
    float           fontSizePx_;
    std::wstring    fontFamily_;
    int32_t         fontWeight_;
    int32_t         fontStyle_;

    // Unicode-coverage fallback face (e.g. Noto Sans CJK), loaded lazily the
    // first time the primary face lacks a glyph. A distinct fallbackFontId_
    // keeps its atlas entries from colliding with the primary face's.
    std::unique_ptr<FontFace> fallbackFace_;
    uint64_t        fallbackFontId_ = 0;
    bool            fallbackAttempted_ = false;

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

// Transitional alias so consumers (Vulkan/software backends) that still spell
// the old name keep compiling.
using FreeTypeTextFormat = JaliumTextFormat;

} // namespace jalium
