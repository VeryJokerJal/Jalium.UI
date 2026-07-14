#pragma once

#include "jalium_types.h"
#include "jalium_backend.h"
#include "jalium_text_api.h"
#include "text_shaper.h"
#include "glyph_atlas.h"
#include "font_face.h"

#include <memory>
#include <string>
#include <vector>
#include <unordered_map>

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

class JALIUM_TEXT_API JaliumTextFormat : public TextFormat {
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

    /// Extended overload used by backends that must explicitly degrade an AA
    /// mode (Linux Vulkan stages through a single-alpha bitmap). Keep the
    /// original overload above as a real symbol for binary compatibility with
    /// separately rebuilt backend .so files.
    void GenerateGlyphQuads(
        const wchar_t* text, uint32_t textLength,
        float maxWidth, float maxHeight,
        float colorR, float colorG, float colorB, float colorA,
        float originX, float originY,
        std::vector<TextGlyphQuad>& outQuads,
        float renderScale,
        int32_t renderingModeOverride);

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

    /// Picks one face for a complete Unicode cluster.  Keeping combining marks,
    /// variation selectors and ZWJ emoji together is required for GSUB to see
    /// the sequence; splitting fallback at each codepoint breaks those glyphs.
    FontFace* ChooseFaceForCluster(
        const std::vector<uint32_t>& codepoints,
        uint64_t& outFontId);

    // Font state
    TextEngine*     engine_;
    std::unique_ptr<FontFace> face_;
    uint64_t        fontId_ = 0;
    float           fontSizePx_;
    std::wstring    fontFamily_;
    int32_t         fontWeight_;
    int32_t         fontStyle_;

    struct FallbackFaceEntry {
        std::unique_ptr<FontFace> face;
        uint64_t fontId = 0;
        std::string matchKey;
    };
    // Fontconfig may choose different faces for CJK, symbols, historic scripts
    // and emoji. Keep every successfully loaded face alive for this format and
    // cache both file matches and cluster decisions.
    std::vector<FallbackFaceEntry> fallbackFaces_;
    std::unordered_map<std::string, int32_t> fallbackMatchCache_;
    std::unordered_map<std::string, int32_t> clusterFaceCache_;

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
