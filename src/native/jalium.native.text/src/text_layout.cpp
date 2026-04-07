#include "text_layout.h"
#include "text_engine.h"
#include "font_provider.h"
#include "glyph_rasterizer.h"
#include "glyph_atlas.h"

#include <ft2build.h>
#include FT_FREETYPE_H

#include <cstring>
#include <cwchar>
#include <cmath>
#include <algorithm>
#include <functional>

namespace jalium {

// ============================================================================
// Font ID hashing
// ============================================================================

static uint64_t ComputeFontId(const wchar_t* family, int32_t weight, int32_t style)
{
    uint64_t h = 0xcbf29ce484222325ULL; // FNV-1a offset basis
    if (family)
    {
        for (const wchar_t* p = family; *p; ++p)
        {
            h ^= static_cast<uint64_t>(*p);
            h *= 0x100000001b3ULL; // FNV prime
        }
    }
    h ^= static_cast<uint64_t>(weight);
    h *= 0x100000001b3ULL;
    h ^= static_cast<uint64_t>(style);
    h *= 0x100000001b3ULL;
    return h;
}

// ============================================================================
// FreeTypeTextFormat
// ============================================================================

FreeTypeTextFormat::FreeTypeTextFormat(
    TextEngine* engine,
    const wchar_t* fontFamily,
    float fontSize,
    int32_t fontWeight,
    int32_t fontStyle)
    : engine_(engine)
    , fontSizePx_(fontSize)
    , fontFamily_(fontFamily ? fontFamily : L"")
    , fontWeight_(fontWeight)
    , fontStyle_(fontStyle)
{
    fontId_ = ComputeFontId(fontFamily, fontWeight, fontStyle);

    // Create FreeType face
    if (engine_ && engine_->GetFontProvider())
    {
        face_ = engine_->GetFontProvider()->CreateFace(
            engine_->GetFTLibrary(), fontFamily, fontWeight, fontStyle);
    }

    // Cache font metrics
    if (face_)
    {
        FT_Set_Char_Size(face_, 0,
            static_cast<FT_F26Dot6>(fontSizePx_ * 64.0f), 72, 72);

        // Get metrics from the face
        float unitsPerEm = static_cast<float>(face_->units_per_EM);
        float scale = fontSizePx_ / unitsPerEm;

        ascent_ = std::abs(static_cast<float>(face_->ascender)) * scale;
        descent_ = std::abs(static_cast<float>(face_->descender)) * scale;
        lineGap_ = static_cast<float>(face_->height) * scale - ascent_ - descent_;
        if (lineGap_ < 0) lineGap_ = 0;
        lineHeight_ = ascent_ + descent_ + lineGap_;
    }
    else
    {
        // Fallback metrics
        ascent_ = fontSizePx_ * 0.8f;
        descent_ = fontSizePx_ * 0.2f;
        lineGap_ = fontSizePx_ * 0.1f;
        lineHeight_ = ascent_ + descent_ + lineGap_;
    }
}

FreeTypeTextFormat::~FreeTypeTextFormat()
{
    if (face_)
    {
        FT_Done_Face(face_);
        face_ = nullptr;
    }
}

void FreeTypeTextFormat::SetAlignment(int32_t alignment) { alignment_ = alignment; }
void FreeTypeTextFormat::SetParagraphAlignment(int32_t alignment) { paragraphAlignment_ = alignment; }
void FreeTypeTextFormat::SetTrimming(int32_t trimming) { trimming_ = trimming; }
void FreeTypeTextFormat::SetWordWrapping(int32_t wrapping) { wrapping_ = wrapping; }
void FreeTypeTextFormat::SetMaxLines(uint32_t maxLines) { maxLines_ = maxLines; }

void FreeTypeTextFormat::SetLineSpacing(int32_t method, float spacing, float baseline)
{
    lineSpacingMethod_ = method;
    lineSpacing_ = spacing;
    lineSpacingBaseline_ = baseline;
}

// ============================================================================
// Layout Engine
// ============================================================================

FreeTypeTextFormat::LayoutResult FreeTypeTextFormat::PerformLayout(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight)
{
    LayoutResult layout{};

    if (!text || textLength == 0 || !face_)
    {
        layout.totalWidth = 0;
        layout.totalHeight = lineHeight_;
        return layout;
    }

    // Shape the entire text
    ShapedRun run = shaper_.Shape(face_, fontId_, text, textLength, fontSizePx_);

    // Effective line height
    float effectiveLineHeight = lineHeight_;
    if (lineSpacingMethod_ != 0 && lineSpacing_ > 0)
        effectiveLineHeight = lineSpacing_;

    // Build lines based on word wrapping mode
    bool noWrap = (wrapping_ == JALIUM_WORD_WRAP_NONE);
    bool charWrap = (wrapping_ == JALIUM_WORD_WRAP_CHARACTER);

    LayoutLine currentLine{};
    currentLine.startIndex = 0;
    currentLine.width = 0;
    currentLine.baselineY = ascent_;

    float penX = 0;

    for (size_t i = 0; i < run.glyphs.size(); i++)
    {
        const auto& glyph = run.glyphs[i];
        float glyphWidth = glyph.advanceX;

        // Check for newline character
        uint32_t charIndex = glyph.cluster;
        if (charIndex < textLength && (text[charIndex] == L'\n' || text[charIndex] == L'\r'))
        {
            // End current line
            currentLine.endIndex = charIndex;
            currentLine.glyphs.assign(
                run.glyphs.begin() + currentLine.startIndex,
                run.glyphs.begin() + i);
            layout.lines.push_back(std::move(currentLine));

            // Skip \r\n pair
            uint32_t nextStart = charIndex + 1;
            if (text[charIndex] == L'\r' && nextStart < textLength && text[nextStart] == L'\n')
                nextStart++;

            // Check max lines
            if (maxLines_ > 0 && layout.lines.size() >= maxLines_)
                break;

            // Start new line
            currentLine = {};
            currentLine.startIndex = static_cast<uint32_t>(i + 1);
            currentLine.width = 0;
            currentLine.baselineY = layout.lines.size() * effectiveLineHeight + ascent_;
            penX = 0;
            continue;
        }

        // Check wrap condition
        if (!noWrap && maxWidth > 0 && penX + glyphWidth > maxWidth && penX > 0)
        {
            if (charWrap || wrapping_ == JALIUM_WORD_WRAP_EMERGENCY)
            {
                // Break at current character
                currentLine.endIndex = charIndex;
                currentLine.glyphs.assign(
                    run.glyphs.begin() + currentLine.startIndex,
                    run.glyphs.begin() + i);
                layout.lines.push_back(std::move(currentLine));

                if (maxLines_ > 0 && layout.lines.size() >= maxLines_)
                    break;

                currentLine = {};
                currentLine.startIndex = static_cast<uint32_t>(i);
                currentLine.width = 0;
                currentLine.baselineY = layout.lines.size() * effectiveLineHeight + ascent_;
                penX = 0;
            }
            else
            {
                // Word wrap: scan back to last word boundary
                size_t breakAt = i;
                for (size_t j = i; j > 0; j--)
                {
                    uint32_t ci = run.glyphs[j - 1].cluster;
                    if (ci < textLength && (text[ci] == L' ' || text[ci] == L'\t'))
                    {
                        breakAt = j;
                        break;
                    }
                }

                if (breakAt == i && wrapping_ == JALIUM_WORD_WRAP_EMERGENCY)
                {
                    // No word boundary found, break at character
                    breakAt = i;
                }
                else if (breakAt == i)
                {
                    // No word boundary found and not emergency: don't break
                    penX += glyphWidth;
                    currentLine.width = penX;
                    continue;
                }

                // Recalculate line width up to break point
                float lineW = 0;
                for (size_t j = currentLine.startIndex; j < breakAt; j++)
                {
                    if (j < run.glyphs.size())
                        lineW += run.glyphs[j].advanceX;
                }

                uint32_t breakCharIndex = (breakAt < run.glyphs.size())
                    ? run.glyphs[breakAt].cluster : textLength;
                currentLine.endIndex = breakCharIndex;
                currentLine.width = lineW;
                currentLine.glyphs.assign(
                    run.glyphs.begin() + currentLine.startIndex,
                    run.glyphs.begin() + breakAt);
                layout.lines.push_back(std::move(currentLine));

                if (maxLines_ > 0 && layout.lines.size() >= maxLines_)
                    break;

                // Skip whitespace at break point
                size_t newStart = breakAt;
                while (newStart < run.glyphs.size())
                {
                    uint32_t ci = run.glyphs[newStart].cluster;
                    if (ci < textLength && (text[ci] == L' ' || text[ci] == L'\t'))
                        newStart++;
                    else
                        break;
                }

                currentLine = {};
                currentLine.startIndex = static_cast<uint32_t>(newStart);
                currentLine.width = 0;
                currentLine.baselineY = layout.lines.size() * effectiveLineHeight + ascent_;

                // Re-accumulate pen from the new start
                penX = 0;
                i = newStart - 1; // Will be incremented by loop
                continue;
            }
        }

        penX += glyphWidth;
        currentLine.width = penX;
    }

    // Add final line
    if (currentLine.startIndex < run.glyphs.size() &&
        (maxLines_ == 0 || layout.lines.size() < maxLines_))
    {
        currentLine.endIndex = textLength;
        currentLine.glyphs.assign(
            run.glyphs.begin() + currentLine.startIndex,
            run.glyphs.end());
        layout.lines.push_back(std::move(currentLine));
    }

    // Even empty text should have one line
    if (layout.lines.empty())
    {
        LayoutLine emptyLine{};
        emptyLine.startIndex = 0;
        emptyLine.endIndex = 0;
        emptyLine.width = 0;
        emptyLine.baselineY = ascent_;
        layout.lines.push_back(emptyLine);
    }

    // Calculate totals
    float maxLineWidth = 0;
    for (const auto& line : layout.lines)
        maxLineWidth = std::max(maxLineWidth, line.width);

    layout.totalWidth = maxLineWidth;
    layout.totalHeight = layout.lines.size() * effectiveLineHeight;

    return layout;
}

void FreeTypeTextFormat::ApplyAlignment(LayoutResult& layout, float maxWidth, float maxHeight)
{
    // Horizontal alignment
    for (auto& line : layout.lines)
    {
        float offset = 0;
        switch (alignment_)
        {
        case JALIUM_TEXT_ALIGN_TRAILING:
            offset = maxWidth - line.width;
            break;
        case JALIUM_TEXT_ALIGN_CENTER:
            offset = (maxWidth - line.width) * 0.5f;
            break;
        default: // LEADING
            break;
        }
        // Offset is applied when generating quads
        line.baselineY += 0; // Placeholder
    }

    // Paragraph (vertical) alignment
    if (maxHeight > 0 && layout.totalHeight < maxHeight)
    {
        float vOffset = 0;
        switch (paragraphAlignment_)
        {
        case JALIUM_PARAGRAPH_ALIGN_FAR:
            vOffset = maxHeight - layout.totalHeight;
            break;
        case JALIUM_PARAGRAPH_ALIGN_CENTER:
            vOffset = (maxHeight - layout.totalHeight) * 0.5f;
            break;
        default: // NEAR
            break;
        }

        for (auto& line : layout.lines)
            line.baselineY += vOffset;
    }
}

// ============================================================================
// TextFormat Interface Implementation
// ============================================================================

JaliumResult FreeTypeTextFormat::MeasureText(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    JaliumTextMetrics* metrics)
{
    if (!metrics) return JALIUM_ERROR_INVALID_ARGUMENT;

    auto layout = PerformLayout(text, textLength, maxWidth, maxHeight);

    metrics->width = layout.totalWidth;
    metrics->height = layout.totalHeight;
    metrics->lineHeight = lineHeight_;
    metrics->baseline = ascent_;
    metrics->ascent = ascent_;
    metrics->descent = descent_;
    metrics->lineGap = lineGap_;
    metrics->lineCount = static_cast<uint32_t>(layout.lines.size());

    return JALIUM_OK;
}

JaliumResult FreeTypeTextFormat::GetFontMetrics(JaliumTextMetrics* metrics)
{
    if (!metrics) return JALIUM_ERROR_INVALID_ARGUMENT;

    metrics->width = 0;
    metrics->height = lineHeight_;
    metrics->lineHeight = lineHeight_;
    metrics->baseline = ascent_;
    metrics->ascent = ascent_;
    metrics->descent = descent_;
    metrics->lineGap = lineGap_;
    metrics->lineCount = 1;

    return JALIUM_OK;
}

JaliumResult FreeTypeTextFormat::HitTestPoint(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    float pointX, float pointY,
    JaliumTextHitTestResult* result)
{
    if (!result) return JALIUM_ERROR_INVALID_ARGUMENT;

    auto layout = PerformLayout(text, textLength, maxWidth, maxHeight);
    ApplyAlignment(layout, maxWidth, maxHeight);

    float effectiveLineHeight = lineHeight_;
    if (lineSpacingMethod_ != 0 && lineSpacing_ > 0)
        effectiveLineHeight = lineSpacing_;

    // Find the line containing pointY
    int lineIndex = static_cast<int>(pointY / effectiveLineHeight);
    if (lineIndex < 0) lineIndex = 0;
    if (lineIndex >= static_cast<int>(layout.lines.size()))
        lineIndex = static_cast<int>(layout.lines.size()) - 1;

    if (layout.lines.empty())
    {
        result->textPosition = 0;
        result->isTrailingHit = 0;
        result->isInside = 0;
        result->caretX = 0;
        result->caretY = 0;
        result->caretHeight = lineHeight_;
        return JALIUM_OK;
    }

    const auto& line = layout.lines[lineIndex];

    // Apply horizontal alignment offset
    float xOffset = 0;
    switch (alignment_)
    {
    case JALIUM_TEXT_ALIGN_TRAILING:
        xOffset = maxWidth - line.width;
        break;
    case JALIUM_TEXT_ALIGN_CENTER:
        xOffset = (maxWidth - line.width) * 0.5f;
        break;
    default:
        break;
    }

    // Find character position by walking cumulative advances
    float cumX = xOffset;
    uint32_t hitPos = line.startIndex < layout.lines[lineIndex].glyphs.size()
        ? layout.lines[lineIndex].glyphs.empty() ? 0 : layout.lines[lineIndex].glyphs[0].cluster
        : 0;
    int32_t trailing = 0;

    for (size_t i = 0; i < line.glyphs.size(); i++)
    {
        float advance = line.glyphs[i].advanceX;
        float midX = cumX + advance * 0.5f;

        if (pointX < midX)
        {
            hitPos = line.glyphs[i].cluster;
            trailing = 0;
            break;
        }
        else if (pointX < cumX + advance)
        {
            hitPos = line.glyphs[i].cluster;
            trailing = 1;
            break;
        }

        cumX += advance;

        // If past the last glyph
        if (i == line.glyphs.size() - 1)
        {
            hitPos = line.glyphs[i].cluster;
            trailing = 1;
        }
    }

    bool inside = (pointX >= xOffset && pointX <= xOffset + line.width &&
                   pointY >= 0 && pointY < layout.totalHeight);

    result->textPosition = hitPos;
    result->isTrailingHit = trailing;
    result->isInside = inside ? 1 : 0;
    result->caretX = cumX;
    result->caretY = lineIndex * effectiveLineHeight;
    result->caretHeight = effectiveLineHeight;

    return JALIUM_OK;
}

JaliumResult FreeTypeTextFormat::HitTestTextPosition(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    uint32_t textPosition, int32_t isTrailingHit,
    JaliumTextHitTestResult* result)
{
    if (!result) return JALIUM_ERROR_INVALID_ARGUMENT;

    auto layout = PerformLayout(text, textLength, maxWidth, maxHeight);
    ApplyAlignment(layout, maxWidth, maxHeight);

    float effectiveLineHeight = lineHeight_;
    if (lineSpacingMethod_ != 0 && lineSpacing_ > 0)
        effectiveLineHeight = lineSpacing_;

    // Find the line containing textPosition
    int lineIndex = 0;
    for (size_t i = 0; i < layout.lines.size(); i++)
    {
        if (textPosition >= layout.lines[i].startIndex &&
            (i + 1 >= layout.lines.size() || textPosition < layout.lines[i + 1].startIndex))
        {
            lineIndex = static_cast<int>(i);
            break;
        }
    }

    if (layout.lines.empty())
    {
        result->textPosition = textPosition;
        result->isTrailingHit = isTrailingHit;
        result->isInside = 0;
        result->caretX = 0;
        result->caretY = 0;
        result->caretHeight = lineHeight_;
        return JALIUM_OK;
    }

    const auto& line = layout.lines[lineIndex];

    // Horizontal alignment offset
    float xOffset = 0;
    switch (alignment_)
    {
    case JALIUM_TEXT_ALIGN_TRAILING:
        xOffset = maxWidth - line.width;
        break;
    case JALIUM_TEXT_ALIGN_CENTER:
        xOffset = (maxWidth - line.width) * 0.5f;
        break;
    default:
        break;
    }

    // Walk glyphs to find the caret X position
    float caretX = xOffset;
    for (const auto& glyph : line.glyphs)
    {
        if (glyph.cluster >= textPosition)
        {
            if (glyph.cluster == textPosition && isTrailingHit)
                caretX += glyph.advanceX;
            break;
        }
        caretX += glyph.advanceX;
    }

    result->textPosition = textPosition;
    result->isTrailingHit = isTrailingHit;
    result->isInside = 1;
    result->caretX = caretX;
    result->caretY = lineIndex * effectiveLineHeight;
    result->caretHeight = effectiveLineHeight;

    return JALIUM_OK;
}

// ============================================================================
// Glyph Quad Generation (for GPU text rendering)
// ============================================================================

void FreeTypeTextFormat::GenerateGlyphQuads(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    float colorR, float colorG, float colorB, float colorA,
    float originX, float originY,
    std::vector<TextGlyphQuad>& outQuads,
    float renderScale)
{
    if (!face_ || !engine_ || !text || textLength == 0)
        return;

    // When DPI scaling is active, temporarily scale font size and metrics
    // so glyphs are rasterized at physical pixel resolution.
    float savedFontSize = fontSizePx_;
    float savedAscent = ascent_;
    float savedDescent = descent_;
    float savedLineGap = lineGap_;
    float savedLineHeight = lineHeight_;

    if (renderScale != 1.0f) {
        fontSizePx_ *= renderScale;
        ascent_ *= renderScale;
        descent_ *= renderScale;
        lineGap_ *= renderScale;
        lineHeight_ *= renderScale;
        maxWidth *= renderScale;
        maxHeight *= renderScale;
    }

    auto layout = PerformLayout(text, textLength, maxWidth, maxHeight);
    ApplyAlignment(layout, maxWidth, maxHeight);

    GlyphAtlas* atlas = engine_->GetGlyphAtlas();
    GlyphRasterizer* rasterizer = engine_->GetGlyphRasterizer();
    if (!atlas || !rasterizer)
        return;

    float invAtlasW = 1.0f / static_cast<float>(atlas->GetWidth());
    float invAtlasH = 1.0f / static_cast<float>(atlas->GetHeight());

    float effectiveLineHeight = lineHeight_;
    if (lineSpacingMethod_ != 0 && lineSpacing_ > 0)
        effectiveLineHeight = lineSpacing_;

    for (size_t lineIdx = 0; lineIdx < layout.lines.size(); lineIdx++)
    {
        const auto& line = layout.lines[lineIdx];

        // Horizontal alignment offset
        float xOffset = 0;
        switch (alignment_)
        {
        case JALIUM_TEXT_ALIGN_TRAILING:
            xOffset = maxWidth - line.width;
            break;
        case JALIUM_TEXT_ALIGN_CENTER:
            xOffset = (maxWidth - line.width) * 0.5f;
            break;
        default:
            break;
        }

        float penX = originX + xOffset;
        float baselineY = originY + line.baselineY;

        for (const auto& glyph : line.glyphs)
        {
            if (glyph.glyphIndex == 0)
            {
                penX += glyph.advanceX;
                continue;
            }

            // Sub-pixel quantization
            float fractionalX = penX - std::floor(penX);
            uint8_t subpixelX = static_cast<uint8_t>(fractionalX * 4.0f);
            if (subpixelX > 3) subpixelX = 3;

            // Get or rasterize glyph in atlas
            const auto& entry = atlas->GetOrInsert(
                *rasterizer, face_, fontId_,
                static_cast<uint16_t>(glyph.glyphIndex),
                static_cast<uint16_t>(fontSizePx_),
                subpixelX);

            if (entry.valid && entry.w > 0 && entry.h > 0)
            {
                TextGlyphQuad quad{};
                quad.posX = std::floor(penX) + glyph.offsetX + entry.bearingX;
                quad.posY = baselineY + glyph.offsetY - entry.bearingY;
                quad.sizeX = static_cast<float>(entry.w);
                quad.sizeY = static_cast<float>(entry.h);
                quad.uvMinX = static_cast<float>(entry.x) * invAtlasW;
                quad.uvMinY = static_cast<float>(entry.y) * invAtlasH;
                quad.uvMaxX = static_cast<float>(entry.x + entry.w) * invAtlasW;
                quad.uvMaxY = static_cast<float>(entry.y + entry.h) * invAtlasH;
                quad.colorR = colorR;
                quad.colorG = colorG;
                quad.colorB = colorB;
                quad.colorA = colorA;
                outQuads.push_back(quad);
            }

            penX += glyph.advanceX;
        }
    }

    // Restore original font state
    if (renderScale != 1.0f) {
        fontSizePx_ = savedFontSize;
        ascent_ = savedAscent;
        descent_ = savedDescent;
        lineGap_ = savedLineGap;
        lineHeight_ = savedLineHeight;
    }
}

} // namespace jalium
