#include "text_layout.h"
#include "text_engine.h"
#include "font_provider.h"
#include "glyph_rasterizer.h"
#include "glyph_atlas.h"
#include "font_face.h"
#include "jalium_text_options.h"

#include <cstring>
#include <cwchar>
#include <cmath>
#include <algorithm>
#include <functional>
#include <sstream>

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
// JaliumTextFormat
// ============================================================================

JaliumTextFormat::JaliumTextFormat(
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

    // Create the self-hosted font face (owns the font-file bytes via RAII).
    if (engine_ && engine_->GetFontProvider())
    {
        face_ = engine_->GetFontProvider()->CreateFace(
            fontFamily, fontWeight, fontStyle);
    }

    // Cache font metrics. FontFace exposes FreeType-parity ascender/descender/
    // height in design units, so this arithmetic is byte-identical to before.
    if (face_)
    {
        float unitsPerEm = static_cast<float>(face_->UnitsPerEm());
        float scale = fontSizePx_ / unitsPerEm;

        ascent_ = std::abs(static_cast<float>(face_->Ascender())) * scale;
        descent_ = std::abs(static_cast<float>(face_->Descender())) * scale;
        lineGap_ = static_cast<float>(face_->Height()) * scale - ascent_ - descent_;
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

JaliumTextFormat::~JaliumTextFormat() = default;   // unique_ptr<FontFace> owns the faces

void JaliumTextFormat::SetAlignment(int32_t alignment) { alignment_ = alignment; }
void JaliumTextFormat::SetParagraphAlignment(int32_t alignment) { paragraphAlignment_ = alignment; }
void JaliumTextFormat::SetTrimming(int32_t trimming) { trimming_ = trimming; }
void JaliumTextFormat::SetWordWrapping(int32_t wrapping) { wrapping_ = wrapping; }
void JaliumTextFormat::SetMaxLines(uint32_t maxLines) { maxLines_ = maxLines; }

void JaliumTextFormat::SetLineSpacing(int32_t method, float spacing, float baseline)
{
    lineSpacingMethod_ = method;
    lineSpacing_ = spacing;
    lineSpacingBaseline_ = baseline;
}

// ============================================================================
// Glyph-level font fallback (e.g. CJK)
// ============================================================================

// Decode the Unicode codepoint starting at wchar_t index i. wchar_t is 4 bytes
// (UTF-32) on Android/Linux — the only platforms that build this FreeType path —
// and 2 bytes (UTF-16 + surrogate pairs) on Windows. The shaper fed HarfBuzz via
// the same sizeof(wchar_t) switch, so clusters index the same units this walks.
// outUnits = wchar_t consumed (1, or 2 for a UTF-16 surrogate pair).
static uint32_t DecodeCodepoint(
    const wchar_t* text, uint32_t textLength, uint32_t i, uint32_t& outUnits)
{
    outUnits = 1;
    if (!text || i >= textLength) return 0;
    if constexpr (sizeof(wchar_t) == 4)
    {
        return static_cast<uint32_t>(text[i]);
    }
    else
    {
        uint16_t ch = static_cast<uint16_t>(text[i]);
        if (ch >= 0xD800 && ch <= 0xDBFF && i + 1 < textLength)
        {
            uint16_t lo = static_cast<uint16_t>(text[i + 1]);
            if (lo >= 0xDC00 && lo <= 0xDFFF)
            {
                outUnits = 2;
                return 0x10000u + ((static_cast<uint32_t>(ch) - 0xD800u) << 10)
                                + (static_cast<uint32_t>(lo) - 0xDC00u);
            }
        }
        return ch;
    }
}

static bool IsFallbackIgnorable(uint32_t cp)
{
    return cp == 0 || cp == 0x200Cu || cp == 0x200Du ||
           (cp >= 0xFE00u && cp <= 0xFE0Fu) ||
           (cp >= 0xE0100u && cp <= 0xE01EFu);
}

static bool FaceCoversCluster(FontFace* face, const std::vector<uint32_t>& codepoints)
{
    if (!face) return false;
    bool sawRenderable = false;
    for (uint32_t cp : codepoints) {
        if (IsFallbackIgnorable(cp) || cp < 0x20u) continue;
        sawRenderable = true;
        if (!face->HasGlyph(cp)) return false;
    }
    return sawRenderable;
}

static std::string ClusterKey(const std::vector<uint32_t>& codepoints)
{
    std::string key;
    key.reserve(codepoints.size() * sizeof(uint32_t));
    for (uint32_t cp : codepoints)
        for (int shift = 0; shift < 32; shift += 8)
            key.push_back(static_cast<char>((cp >> shift) & 0xffu));
    return key;
}

FontFace* JaliumTextFormat::ChooseFaceForCluster(
    const std::vector<uint32_t>& codepoints,
    uint64_t& outFontId)
{
    if (FaceCoversCluster(face_.get(), codepoints)) {
        outFontId = fontId_;
        return face_.get();
    }

    const std::string clusterKey = ClusterKey(codepoints);
    auto cached = clusterFaceCache_.find(clusterKey);
    if (cached != clusterFaceCache_.end()) {
        if (cached->second >= 0 && static_cast<size_t>(cached->second) < fallbackFaces_.size()) {
            auto& entry = fallbackFaces_[static_cast<size_t>(cached->second)];
            outFontId = entry.fontId;
            return entry.face.get();
        }
        outFontId = fontId_;
        return face_.get();
    }

    FontProvider* provider = engine_ ? engine_->GetFontProvider() : nullptr;
    if (provider) {
        auto matches = provider->FindFallbackFonts(
            codepoints, fontFamily_.c_str(), fontWeight_, fontStyle_);
        for (const auto& match : matches) {
            const std::string matchKey = match.path + "#" + std::to_string(match.faceIndex);
            int32_t index = -1;
            auto known = fallbackMatchCache_.find(matchKey);
            if (known != fallbackMatchCache_.end()) {
                index = known->second;
            } else {
                auto candidate = provider->CreateFace(match);
                if (candidate) {
                    FallbackFaceEntry entry;
                    entry.face = std::move(candidate);
                    entry.matchKey = matchKey;
                    entry.fontId = ComputeFontId(
                        std::wstring(matchKey.begin(), matchKey.end()).c_str(),
                        fontWeight_, fontStyle_);
                    index = static_cast<int32_t>(fallbackFaces_.size());
                    fallbackFaces_.push_back(std::move(entry));
                }
                fallbackMatchCache_[matchKey] = index;
            }
            if (index >= 0 && FaceCoversCluster(fallbackFaces_[static_cast<size_t>(index)].face.get(), codepoints)) {
                clusterFaceCache_[clusterKey] = index;
                auto& entry = fallbackFaces_[static_cast<size_t>(index)];
                outFontId = entry.fontId;
                return entry.face.get();
            }
        }
    }

    clusterFaceCache_[clusterKey] = -1;
    outFontId = fontId_;
    return face_.get(); // renders .notdef; GenerateGlyphQuads skips glyph index 0
}

static bool IsClusterExtender(uint32_t cp)
{
    return (cp >= 0x0300u && cp <= 0x036Fu) ||
           (cp >= 0x1AB0u && cp <= 0x1AFFu) ||
           (cp >= 0x1DC0u && cp <= 0x1DFFu) ||
           (cp >= 0x20D0u && cp <= 0x20FFu) ||
           (cp >= 0xFE20u && cp <= 0xFE2Fu) ||
           (cp >= 0xFE00u && cp <= 0xFE0Fu) ||
           (cp >= 0xE0100u && cp <= 0xE01EFu) ||
           (cp >= 0x1F3FBu && cp <= 0x1F3FFu) ||
           (cp >= 0xE0020u && cp <= 0xE007Fu);
}

static bool IsRegionalIndicator(uint32_t cp)
{
    return cp >= 0x1F1E6u && cp <= 0x1F1FFu;
}

ShapedRun JaliumTextFormat::ShapeWithFallback(const wchar_t* text, uint32_t textLength)
{
    if (!face_ || !text || textLength == 0)
        return shaper_.Shape(face_.get(), fontId_, text, textLength, fontSizePx_);

    // Does the primary face cover every (non-control) codepoint? If so, shape
    // the whole string in one pass — byte-for-byte the pre-fallback behaviour —
    // and never load the (large) CJK fallback face for Latin-only text.
    bool needsFallback = false;
    for (uint32_t i = 0; i < textLength; )
    {
        uint32_t units = 0;
        uint32_t cp = DecodeCodepoint(text, textLength, i, units);
        i += (units ? units : 1);
        if (cp >= 0x20 && face_->GetGlyphIndex(cp) == 0)
        {
            needsFallback = true;
            break;
        }
    }

    if (!needsFallback)
        return shaper_.Shape(face_.get(), fontId_, text, textLength, fontSizePx_);

    // Decode into grapheme-like clusters, then split into maximal runs sharing
    // a font. This is deliberately a compact subset of UAX #29 covering the
    // sequences that affect font fallback: combining marks, variation
    // selectors, emoji modifiers, RI flags and chained ZWJ emoji.
    struct Unit { uint32_t cp, start, end; };
    std::vector<Unit> units;
    for (uint32_t p = 0; p < textLength; ) {
        uint32_t count = 0;
        const uint32_t cp = DecodeCodepoint(text, textLength, p, count);
        if (count == 0) count = 1;
        units.push_back({cp, p, p + count});
        p += count;
    }
    struct Cluster { uint32_t start, end; FontFace* face; uint64_t fontId; };
    std::vector<Cluster> clusters;
    for (size_t u = 0; u < units.size(); ) {
        const size_t begin = u++;
        if (IsRegionalIndicator(units[begin].cp) && u < units.size() && IsRegionalIndicator(units[u].cp)) ++u;
        while (u < units.size() && IsClusterExtender(units[u].cp)) ++u;
        while (u < units.size() && units[u].cp == 0x200Du) {
            ++u; // include joiner
            if (u < units.size()) ++u; // and the joined base
            while (u < units.size() && IsClusterExtender(units[u].cp)) ++u;
        }
        std::vector<uint32_t> clusterCodepoints;
        clusterCodepoints.reserve(u - begin);
        for (size_t j = begin; j < u; ++j) clusterCodepoints.push_back(units[j].cp);
        uint64_t selectedId = fontId_;
        FontFace* selected = ChooseFaceForCluster(clusterCodepoints, selectedId);
        clusters.push_back({units[begin].start, units[u - 1].end, selected, selectedId});
    }

    // Split into maximal runs that share a face, shape each with its own face
    // (so advances/metrics are correct), concatenate with absolute clusters.
    ShapedRun combined{};
    combined.face = face_.get();
    combined.fontId = fontId_;
    combined.fontSize = fontSizePx_;
    combined.isRtl = false;

    size_t ci = 0;
    while (ci < clusters.size())
    {
        const uint32_t runStart = clusters[ci].start;
        FontFace* runFace = clusters[ci].face;
        const uint64_t runFontId = clusters[ci].fontId;
        size_t endCluster = ci + 1;
        while (endCluster < clusters.size() && clusters[endCluster].face == runFace)
            ++endCluster;
        const uint32_t runEnd = clusters[endCluster - 1].end;

        ShapedRun part = shaper_.Shape(
            runFace, runFontId, text + runStart, runEnd - runStart, fontSizePx_);
        for (auto& g : part.glyphs)
        {
            g.cluster += runStart; // run-relative -> absolute into full text
            combined.glyphs.push_back(g);
        }
        ci = endCluster;
    }
    return combined;
}

// ============================================================================
// Layout Engine
// ============================================================================

JaliumTextFormat::LayoutResult JaliumTextFormat::PerformLayout(
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

    // Shape the entire text, splitting into per-face runs so codepoints the
    // primary face lacks (e.g. CJK) are shaped + measured with a fallback face.
    ShapedRun run = ShapeWithFallback(text, textLength);

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

void JaliumTextFormat::ApplyAlignment(LayoutResult& layout, float maxWidth, float maxHeight)
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

JaliumResult JaliumTextFormat::MeasureText(
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

JaliumResult JaliumTextFormat::GetFontMetrics(JaliumTextMetrics* metrics)
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

JaliumResult JaliumTextFormat::HitTestPoint(
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

JaliumResult JaliumTextFormat::HitTestTextPosition(
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

void JaliumTextFormat::GenerateGlyphQuads(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    float colorR, float colorG, float colorB, float colorA,
    float originX, float originY,
    std::vector<TextGlyphQuad>& outQuads,
    float renderScale)
{
    GenerateGlyphQuads(text, textLength, maxWidth, maxHeight,
        colorR, colorG, colorB, colorA, originX, originY,
        outQuads, renderScale, -1);
}

void JaliumTextFormat::GenerateGlyphQuads(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    float colorR, float colorG, float colorB, float colorA,
    float originX, float originY,
    std::vector<TextGlyphQuad>& outQuads,
    float renderScale,
    int32_t renderingModeOverride)
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
    if (!atlas || !rasterizer) {
        if (renderScale != 1.0f) {
            fontSizePx_ = savedFontSize; ascent_ = savedAscent; descent_ = savedDescent;
            lineGap_ = savedLineGap; lineHeight_ = savedLineHeight;
        }
        return;
    }

    const int32_t resolvedMode = renderingModeOverride >= 0
        ? jalium::text_options::ResolveMode(renderingModeOverride)
        : ResolveEffectiveTextRenderingMode();
    const GlyphAntialiasMode glyphAaMode =
        resolvedMode == JALIUM_TEXT_AA_ALIASED ? GlyphAntialiasMode::Aliased :
        resolvedMode == JALIUM_TEXT_AA_CLEARTYPE ? GlyphAntialiasMode::HorizontalLcd :
                                                   GlyphAntialiasMode::Grayscale;

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

            // Sub-pixel quantization to 1/8 pixel (8 buckets). At 1/4 pixel the
            // size-invariant 0.25px residual is a visible fraction of a small-font
            // advance and reads as phantom inter-glyph spacing; 1/8 pixel halves it.
            float fractionalX = penX - std::floor(penX);
            uint8_t subpixelX = static_cast<uint8_t>(fractionalX * 8.0f);
            if (subpixelX > 7) subpixelX = 7;

            // Get or rasterize glyph in atlas. The glyph carries the face/font
            // it was shaped with (primary, or a CJK/Unicode fallback selected in
            // ShapeWithFallback), so rasterize from THAT face — not always face_.
            FontFace* glyphFace   = glyph.face ? glyph.face : face_.get();
            uint64_t  glyphFontId = glyph.face ? glyph.fontId : fontId_;
            const auto& entry = atlas->GetOrInsert(
                *rasterizer, glyphFace, glyphFontId,
                static_cast<uint16_t>(glyph.glyphIndex),
                // Round (not truncate) the raster em: HarfBuzz advances are
                // fractional, so a truncated raster size desyncs glyph ink width
                // from the advance and skews spacing at fractional (high-DPI/scaled) sizes.
                static_cast<uint16_t>(std::lround(fontSizePx_)),
                subpixelX,
                glyphAaMode);

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
                quad.flags = entry.flags;
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
