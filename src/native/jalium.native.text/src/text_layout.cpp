#include "text_layout.h"
#include "shared_text_paragraph.h"
#include "text_bidi.h"
#include "text_grapheme.h"
#include "jalium_text_glyph_ranges.h"
#include "text_engine.h"
#include "font_provider.h"
#include "glyph_rasterizer.h"
#include "glyph_atlas.h"
#include "font_face.h"
#include "jalium_text_options.h"
#include "jalium_font_underline.h"
#include "jalium_font_math.h"
#include "jalium_font_features.h"

#include <atomic>
#include <cstring>
#include <cwchar>
#include <cwctype>
#include <cmath>
#include <algorithm>
#include <functional>
#include <iomanip>
#include <locale>
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
    registeredFont_ = AcquireFontResource(fontFamily);
    {
        static std::atomic<uint64_t> s_nextInstanceId { 1 };
        instanceId_ = s_nextInstanceId.fetch_add(1, std::memory_order_relaxed);
    }

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

JaliumTextFormat::JaliumTextFormat(const JaliumTextFormat& source, SnapshotTag)
    : TextFormat(source), engine_(source.engine_), registeredFont_(source.registeredFont_),
      face_(source.face_ ? source.face_->Clone() : nullptr), fontId_(source.fontId_),
      fontSizePx_(source.fontSizePx_), fontFamily_(source.fontFamily_), fontWeight_(source.fontWeight_), fontStyle_(source.fontStyle_),
      baseDirection_(source.baseDirection_), alignment_(source.alignment_), paragraphAlignment_(source.paragraphAlignment_),
      trimming_(source.trimming_), wrapping_(source.wrapping_), lineSpacing_(source.lineSpacing_),
      lineSpacingBaseline_(source.lineSpacingBaseline_), lineSpacingMethod_(source.lineSpacingMethod_), maxLines_(source.maxLines_),
      ascent_(source.ascent_), descent_(source.descent_), lineGap_(source.lineGap_), lineHeight_(source.lineHeight_), fontFeatures_(source.fontFeatures_)
{
    static std::atomic<uint64_t> nextSnapshot{uint64_t(1) << 63};
    instanceId_ = nextSnapshot.fetch_add(1, std::memory_order_relaxed);
}

TextParagraph* JaliumTextFormat::CreateParagraph(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t count, float width, float minHeight, int32_t alignment, int32_t direction)
{
    return CreateParagraphWithWrapping(text, length, spans, count, width, minHeight, alignment, direction, 0);
}

TextParagraph* JaliumTextFormat::CreateParagraphWithWrapping(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t count, float width, float minHeight, int32_t alignment, int32_t direction, int32_t wrapping)
{
    return SharedTextParagraph::Create(*this, text, length, spans, count, width, minHeight, alignment, direction, wrapping);
}

TextParagraph* JaliumTextFormat::CreateSpacedParagraph(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t count, const JaliumTextSpacingUnit* units,
    uint32_t unitCount, float width, float minHeight, int32_t direction)
{
    if (!units || !unitCount) return nullptr;
    std::vector<JaliumTextAdvanceUnit> advances;
    advances.reserve(unitCount);
    for (uint32_t i = 0; i < unitCount; ++i)
        advances.push_back({units[i].textPosition, units[i].length, units[i].spacing, 0, 0, units[i].flags});
    return CreateAdvancedParagraph(text, length, spans, count, advances.data(), unitCount, width, minHeight, direction, false);
}

TextParagraph* JaliumTextFormat::CreateContextParagraph(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t count, const JaliumTextSpacingUnit* units,
    uint32_t unitCount, float width, float minHeight, int32_t direction)
{
    // Equal-font styles already share shaping; draw_paragraph_slice supplies
    // the current foreground independently of the retained neutral span paint.
    return CreateSpacedParagraph(text, length, spans, count, units, unitCount, width, minHeight, direction);
}

TextParagraph* JaliumTextFormat::CreateAdvancedParagraph(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t count, const JaliumTextAdvanceUnit* units,
    uint32_t unitCount, float width, float minHeight, int32_t direction, bool contextPaint)
{
    (void)contextPaint;
    std::unique_ptr<SharedTextParagraph> paragraph(SharedTextParagraph::Create(*this, text, length,
        spans, count, width, minHeight, 0, direction, JALIUM_WORD_WRAP_NONE));
    return paragraph && paragraph->ApplyAdvances(units, unitCount) ? paragraph.release() : nullptr;
}

JaliumTextFormat::~JaliumTextFormat() = default;   // unique_ptr<FontFace> owns the faces

JaliumResult JaliumTextFormat::SetFontFeatures(const JaliumFontFeature* features, uint32_t count)
{
    if (count > 16384 || (count && !features)) return JALIUM_ERROR_INVALID_ARGUMENT;
    std::vector<JaliumFontFeature> values;
    if (count) values.assign(features, features + count);
    fontFeatures_ = std::move(values);
    return JALIUM_OK;
}

JaliumResult JaliumTextFormat::GetFontFeatureSupport(uint32_t tables, const uint32_t* tags, uint32_t count, uint8_t* supported) const
{
    if (!face_) return JALIUM_ERROR_NOT_SUPPORTED;
    std::vector<uint32_t> available;
    const uint32_t tableTags[] = {font::kTag_GSUB, font::kTag_GPOS};
    for (unsigned i = 0; i < 2; ++i) {
        if (!(tables & (1u << i))) continue;
        auto table = face_->GetTable(tableTags[i]);
        font_features::Collect(table.Data(), table.Size(), available);
    }
    font_features::Query(available, tags, count, supported);
    return JALIUM_OK;
}

void JaliumTextFormat::SetAlignment(int32_t alignment)
{
    if (alignment_ != alignment) { alignment_ = alignment; ++styleGeneration_; }
}
void JaliumTextFormat::SetParagraphAlignment(int32_t alignment)
{
    if (paragraphAlignment_ != alignment) { paragraphAlignment_ = alignment; ++styleGeneration_; }
}
void JaliumTextFormat::SetTrimming(int32_t trimming)
{
    if (trimming_ != trimming) { trimming_ = trimming; ++styleGeneration_; }
}
void JaliumTextFormat::SetWordWrapping(int32_t wrapping)
{
    if (wrapping_ != wrapping) { wrapping_ = wrapping; ++styleGeneration_; }
}
void JaliumTextFormat::SetMaxLines(uint32_t maxLines)
{
    if (maxLines_ != maxLines) { maxLines_ = maxLines; ++styleGeneration_; }
}

void JaliumTextFormat::SetLineSpacing(int32_t method, float spacing, float baseline)
{
    if (lineSpacingMethod_ != method || lineSpacing_ != spacing || lineSpacingBaseline_ != baseline) {
        ++styleGeneration_;
    }
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
    return TextBidi::IsFormattingControl(cp) || cp == 0 || cp == 0x200Cu || cp == 0x200Du ||
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

ShapedRun JaliumTextFormat::ShapeWithFallback(const wchar_t* text, uint32_t textLength, const TextBidi& bidi, uint32_t offset)
{
    if (!face_ || !text || textLength == 0)
        return shaper_.Shape(face_.get(), fontId_, text, textLength, fontSizePx_, false, fontFeatures_);

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

    if (!needsFallback && bidi.Uniform())
        return shaper_.Shape(face_.get(), fontId_, text, textLength, fontSizePx_, false, fontFeatures_);

    // Font fallback and caret cells use the same Unicode grapheme boundaries,
    // including emoji sequences, Indic conjuncts, Hangul and spacing marks.
    struct Cluster { uint32_t start, end; FontFace* face; uint64_t fontId; };
    std::vector<Cluster> clusters;
    const auto boundaries = grapheme::Boundaries(std::wstring_view(text, textLength));
    for (size_t i = 0; i + 1 < boundaries.size(); ++i) {
        std::vector<uint32_t> codepoints;
        for (uint32_t position = boundaries[i]; position < boundaries[i + 1];) {
            uint32_t units = 0; codepoints.push_back(DecodeCodepoint(text, textLength, position, units));
            position += units ? units : 1;
        }
        uint64_t selectedId = fontId_;
        auto* selected = ChooseFaceForCluster(codepoints, selectedId);
        clusters.push_back({boundaries[i], boundaries[i + 1], selected, selectedId});
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
        while (endCluster < clusters.size() && clusters[endCluster].face == runFace &&
            bidi.Level(offset + clusters[endCluster].start) == bidi.Level(offset + runStart))
            ++endCluster;
        const uint32_t runEnd = clusters[endCluster - 1].end;

        ShapedRun part = shaper_.Shape(
            runFace, runFontId, text + runStart, runEnd - runStart, fontSizePx_, false, fontFeatures_);
        for (auto& g : part.glyphs)
        {
            g.cluster += runStart; // run-relative -> absolute into full text
            g.clusterEnd += runStart;
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
        layout.totalWidthIncludingTrailingWhitespace = 0;
        layout.totalHeight = lineHeight_;
        return layout;
    }

    // Shape the entire text, splitting into per-face runs so codepoints the
    // primary face lacks (e.g. CJK) are shaped + measured with a fallback face.
    TextBidi bidi(text, textLength, baseDirection_);
    ShapedRun run = ShapeWithFallback(bidi.MirroredText(), textLength, bidi);

    // Effective line height
    float effectiveLineHeight = lineHeight_;
    if (lineSpacingMethod_ != 0 && lineSpacing_ > 0)
        effectiveLineHeight = lineSpacing_;

    // Build lines based on word wrapping mode
    bool noWrap = (wrapping_ == JALIUM_WORD_WRAP_NONE);
    bool charWrap = (wrapping_ == JALIUM_WORD_WRAP_CHARACTER);

    const auto& glyphs = run.glyphs;
    size_t firstGlyph = 0, cursor = 0, lastWordBreak = 0;
    uint32_t firstCharacter = 0;
    float penX = 0;
    auto emit = [&](size_t first, size_t last, uint32_t characterStart, uint32_t characterEnd) {
        LayoutLine line{};
        line.startIndex = characterStart;
        line.endIndex = characterEnd;
        line.baselineY = layout.lines.size() * effectiveLineHeight + ascent_;
        line.glyphs.assign(glyphs.begin() + first, glyphs.begin() + last);
        for (const auto& glyph : line.glyphs) line.width += glyph.advanceX;
        layout.lines.push_back(std::move(line));
    };
    while (cursor < glyphs.size() && (maxLines_ == 0 || layout.lines.size() < maxLines_)) {
        const auto& glyph = glyphs[cursor];
        const uint32_t cp = glyph.cluster < textLength ? uint32_t(text[glyph.cluster]) : 0;
        if (TextBidi::IsLineBreak(cp)) {
            emit(firstGlyph, cursor, firstCharacter, glyph.cluster);
            uint32_t nextCharacter = glyph.cluster + 1;
            ++cursor;
            if (cp == '\r' && nextCharacter < textLength && text[nextCharacter] == L'\n') {
                ++nextCharacter;
                if (cursor < glyphs.size() && glyphs[cursor].cluster < nextCharacter) ++cursor;
            }
            firstGlyph = cursor; firstCharacter = nextCharacter; lastWordBreak = firstGlyph; penX = 0;
            continue;
        }
        if (!noWrap && maxWidth > 0 && penX > 0 && penX + glyph.advanceX > maxWidth) {
            size_t breakAt = firstGlyph;
            if (charWrap || wrapping_ == JALIUM_WORD_WRAP_EMERGENCY) breakAt = cursor;
            else if (lastWordBreak > firstGlyph) breakAt = lastWordBreak;
            if (breakAt > firstGlyph) {
                const uint32_t end = glyphs[breakAt].cluster;
                emit(firstGlyph, breakAt, firstCharacter, end);
                firstGlyph = breakAt; firstCharacter = end;
                // Whitespace already included at the preceding break keeps its
                // logical source range; discard only additional leading spaces.
                while (firstGlyph < glyphs.size()) {
                    const auto index = glyphs[firstGlyph].cluster;
                    if (index >= textLength || (text[index] != L' ' && text[index] != L'\t')) break;
                    firstCharacter = glyphs[firstGlyph].clusterEnd; ++firstGlyph;
                }
                cursor = firstGlyph; lastWordBreak = firstGlyph; penX = 0;
                continue;
            }
        }
        penX += glyph.advanceX;
        if (cp == ' ' || cp == '\t') lastWordBreak = cursor + 1;
        ++cursor;
    }
    if ((maxLines_ == 0 || layout.lines.size() < maxLines_) &&
        (firstGlyph < glyphs.size() || firstCharacter == textLength || layout.lines.empty()))
        emit(firstGlyph, glyphs.size(), firstCharacter, textLength);

    // Calculate totals
    float maxLineWidth = 0.0f;
    float maxLineWidthIncludingTrailingWhitespace = 0.0f;
    for (const auto& line : layout.lines) {
        maxLineWidthIncludingTrailingWhitespace = std::max(
            maxLineWidthIncludingTrailingWhitespace,
            line.width);

        float trimmedWidth = line.width;
        for (auto glyph = line.glyphs.rbegin(); glyph != line.glyphs.rend(); ++glyph) {
            const uint32_t characterIndex = glyph->cluster;
            if (characterIndex >= textLength || !std::iswspace(text[characterIndex])) {
                break;
            }
            trimmedWidth -= glyph->advanceX;
        }

        maxLineWidth = std::max(maxLineWidth, std::max(0.0f, trimmedWidth));
    }

    layout.totalWidth = maxLineWidth;
    layout.totalWidthIncludingTrailingWhitespace = maxLineWidthIncludingTrailingWhitespace;
    layout.totalHeight = layout.lines.size() * effectiveLineHeight;
    // Wrapping and trailing whitespace metrics use logical order. Reorder each
    // completed line with its original paragraph context, including L1 resets.
    for (auto& line : layout.lines) bidi.Reorder(line.glyphs, line.startIndex, line.endIndex);

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

bool JaliumTextFormat::BuildOutlinePath(const wchar_t* text, uint32_t textLength,
    std::string& path, float& width, float& baseline)
{
    path.clear();
    width = baseline = 0;
    if (!face_ || !text) return false;
    // SVG text spans one unwrapped run here. The same shaper and fallback faces
    // used for painted text supply glyph IDs, advances, and offsets.
    const auto layout = PerformLayout(text, textLength, 10000000.f, 10000000.f);
    width = layout.totalWidthIncludingTrailingWhitespace;
    baseline = ascent_;
    std::ostringstream output;
    output.imbue(std::locale::classic());
    output << std::setprecision(9);
    for (const auto& line : layout.lines) {
        float penX = 0;
        for (const auto& glyph : line.glyphs) {
            FontFace* face = glyph.face ? glyph.face : face_.get();
            if (glyph.glyphIndex && face && face->UnitsPerEm()) {
                const float scale = fontSizePx_ / face->UnitsPerEm();
                GlyphOutline outline;
                const float tolerance = std::max(1.f, .25f / scale);
                if (face->GetGlyphContours(static_cast<uint16_t>(glyph.glyphIndex),
                        tolerance, outline)) {
                    for (const auto& contour : outline.contours) {
                        if (contour.VertexCount() < 3) continue;
                        for (uint32_t i = 0; i < contour.VertexCount(); ++i) {
                            const float x = penX + glyph.offsetX + contour.X(i) * scale;
                            const float y = line.baselineY + glyph.offsetY - contour.Y(i) * scale;
                            output << (i == 0 ? 'M' : 'L') << x << ' ' << y << ' ';
                        }
                        output << "Z ";
                        if (output.tellp() > std::streamoff(8 * 1024 * 1024)) return false;
                    }
                }
            }
            penX += glyph.advanceX;
        }
    }
    path = output.str();
    return true;
}

JaliumResult JaliumTextFormat::MeasureText(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    JaliumTextMetrics* metrics)
{
    if (!metrics) return JALIUM_ERROR_INVALID_ARGUMENT;

    LayoutResult ownedLayout;
    if (!frozenLayout_) ownedLayout = PerformLayout(text, textLength, maxWidth, maxHeight);
    const auto& layout = frozenLayout_ ? *frozenLayout_ : ownedLayout;

    metrics->width = layout.totalWidth;
    metrics->widthIncludingTrailingWhitespace = layout.totalWidthIncludingTrailingWhitespace;
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
    metrics->widthIncludingTrailingWhitespace = 0;
    metrics->height = lineHeight_;
    metrics->lineHeight = lineHeight_;
    metrics->baseline = ascent_;
    metrics->ascent = ascent_;
    metrics->descent = descent_;
    metrics->lineGap = lineGap_;
    metrics->lineCount = 1;

    return JALIUM_OK;
}

JaliumResult JaliumTextFormat::GetFontUnitMetrics(JaliumFontUnitMetrics* metrics)
{
    if (!metrics) return JALIUM_ERROR_INVALID_ARGUMENT;
    *metrics = {sizeof(JaliumFontUnitMetrics), fontSizePx_ * .5f, ascent_,
        fontSizePx_ * .5f, fontSizePx_, ascent_, lineHeight_, 0};
    if (!face_) return JALIUM_OK;
    metrics->available = 16;
    const float scale = fontSizePx_ / face_->UnitsPerEm();
    const auto post = face_->GetTable(font::kTag_post);
    font_units::ReadPostUnderline(post.Data(), post.Size(), scale, metrics);
    const auto os2 = face_->GetTable(font::kTag_OS2);
    auto height = [&](uint32_t codepoint, size_t offset) {
        if (os2.Size() >= 90 && os2.U16(0) >= 2 && os2.S16(offset) > 0)
            return static_cast<float>(os2.S16(offset));
        GlyphOutline outline;
        const auto glyph = face_->GetGlyphIndex(codepoint);
        if (glyph && face_->GetGlyphContours(glyph, 1.f, outline) && outline.hasInk)
            return outline.yMax + (std::min)(0.f, outline.yMin);
        return 0.f;
    };
    const auto xHeight = height('x', 86);
    const auto capHeight = height('O', 88);
    if (xHeight > 0) { metrics->xHeight = xHeight * scale; metrics->available |= 1; }
    if (capHeight > 0) { metrics->capHeight = capHeight * scale; metrics->available |= 2; }
    auto advance = [&](uint32_t codepoint, float& result, uint32_t flag) {
        uint64_t fontId = 0;
        auto* face = ChooseFaceForCluster({codepoint}, fontId);
        if (!face) return;
        const auto glyph = face->GetGlyphIndex(codepoint);
        if (!glyph) return;
        result = face->GetAdvance(glyph) * fontSizePx_ / face->UnitsPerEm();
        metrics->available |= flag;
    };
    advance('0', metrics->zeroAdvance, 4);
    advance(0x6c34, metrics->ideographicAdvance, 8);
    return JALIUM_OK;
}

JaliumResult JaliumTextFormat::GetFontMathConstants(JaliumFontMathConstants* constants)
{
    if (!constants) return JALIUM_ERROR_INVALID_ARGUMENT;
    *constants = {sizeof(JaliumFontMathConstants), 0, 0, 0};
    if (!face_) return JALIUM_OK;
    const auto table = face_->GetTable(font::kTag_MATH);
    font_math::Read(table.Data(), table.Size(), constants);
    return JALIUM_OK;
}

JaliumResult JaliumTextFormat::HitTestPoint(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    float pointX, float pointY,
    JaliumTextHitTestResult* result)
{
    if (!result) return JALIUM_ERROR_INVALID_ARGUMENT;

    LayoutResult ownedLayout;
    if (!frozenLayout_) { ownedLayout = PerformLayout(text, textLength, maxWidth, maxHeight); ApplyAlignment(ownedLayout, maxWidth, maxHeight); }
    const auto& layout = frozenLayout_ ? *frozenLayout_ : ownedLayout;

    float effectiveLineHeight = lineHeight_;
    if (lineSpacingMethod_ != 0 && lineSpacing_ > 0)
        effectiveLineHeight = lineSpacing_;

    // Find the line containing pointY
    const float top = layout.lines.empty() ? 0 : layout.lines.front().baselineY - ascent_;
    int lineIndex = static_cast<int>(std::floor((pointY - top) / effectiveLineHeight));
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

    // Glyphs are in visual order; trailing is a logical edge and reverses for
    // odd levels. Return that exact edge, including for clicks outside a line.
    float cumX = xOffset;
    uint32_t hitPos = line.startIndex;
    int32_t trailing = 0;
    float caretX = xOffset;
    for (size_t i = 0; i < line.glyphs.size(); ++i) {
        const auto& glyph = line.glyphs[i];
        const float advance = glyph.advanceX;
        const bool rightHalf = pointX >= cumX + advance * .5f;
        if (pointX < cumX + advance || i + 1 == line.glyphs.size()) {
            hitPos = glyph.cluster;
            trailing = rightHalf != bool(glyph.bidiLevel & 1);
            caretX = cumX + (rightHalf ? advance : 0);
            break;
        }
        cumX += advance;
    }

    bool inside = (pointX >= xOffset && pointX <= xOffset + line.width &&
                   pointY >= top && pointY < top + layout.totalHeight);

    result->textPosition = hitPos;
    result->isTrailingHit = trailing;
    result->isInside = inside ? 1 : 0;
    result->caretX = caretX;
    result->caretY = line.baselineY - (line.ascent > 0 ? line.ascent : ascent_);
    result->caretHeight = line.height > 0 ? line.height : effectiveLineHeight;

    return JALIUM_OK;
}

JaliumResult JaliumTextFormat::HitTestTextPosition(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    uint32_t textPosition, int32_t isTrailingHit,
    JaliumTextHitTestResult* result)
{
    if (!result) return JALIUM_ERROR_INVALID_ARGUMENT;

    LayoutResult ownedLayout;
    if (!frozenLayout_) { ownedLayout = PerformLayout(text, textLength, maxWidth, maxHeight); ApplyAlignment(ownedLayout, maxWidth, maxHeight); }
    const auto& layout = frozenLayout_ ? *frozenLayout_ : ownedLayout;

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

    textPosition = std::min(textPosition, textLength);
    float caretX = xOffset;
    bool found = false;
    for (const auto& glyph : line.glyphs) {
        if (textPosition >= glyph.cluster && textPosition < glyph.clusterEnd) {
            const bool physicalRight = bool(isTrailingHit) != bool(glyph.bidiLevel & 1);
            if (physicalRight) caretX += glyph.advanceX;
            found = true;
            break;
        }
        caretX += glyph.advanceX;
    }
    if (!found && !line.glyphs.empty()) {
        // A formatting-only position or paragraph end has zero ink. Resolve it
        // to the nearest logical glyph edge rather than assuming visual order
        // has increasing source offsets.
        const ShapedGlyph* nearest = nullptr;
        for (const auto& glyph : line.glyphs)
            if (glyph.clusterEnd <= textPosition && (!nearest || glyph.clusterEnd > nearest->clusterEnd)) nearest = &glyph;
        if (nearest) {
            caretX = xOffset;
            for (const auto& glyph : line.glyphs) {
                if (&glyph == nearest) { if (!(glyph.bidiLevel & 1)) caretX += glyph.advanceX; break; }
                caretX += glyph.advanceX;
            }
        } else {
            const ShapedGlyph* following = nullptr;
            for (const auto& glyph : line.glyphs)
                if (glyph.cluster >= textPosition && (!following || glyph.cluster < following->cluster)) following = &glyph;
            caretX = xOffset;
            if (following) for (const auto& glyph : line.glyphs) {
                if (&glyph == following) { if (glyph.bidiLevel & 1) caretX += glyph.advanceX; break; }
                caretX += glyph.advanceX;
            }
        }
    }

    result->textPosition = textPosition;
    result->isTrailingHit = isTrailingHit;
    result->isInside = 1;
    result->caretX = caretX;
    result->caretY = line.baselineY - (line.ascent > 0 ? line.ascent : ascent_);
    result->caretHeight = line.height > 0 ? line.height : effectiveLineHeight;

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
    GenerateGlyphQuads(text, textLength, maxWidth, maxHeight, colorR, colorG, colorB, colorA,
        originX, originY, outQuads, renderScale, renderingModeOverride, nullptr, 0, 0);
}

void JaliumTextFormat::GenerateGlyphQuads(
    const wchar_t* text, uint32_t textLength, float maxWidth, float maxHeight,
    float colorR, float colorG, float colorB, float colorA, float originX, float originY,
    std::vector<TextGlyphQuad>& outQuads, float renderScale, int32_t renderingModeOverride,
    const uint32_t* glyphRanges, uint32_t rangeCount, int32_t visibility)
{
    GenerateGlyphQuadsForEngine(engine_, text, textLength, maxWidth, maxHeight,
        colorR, colorG, colorB, colorA, originX, originY, outQuads, renderScale, renderingModeOverride,
        glyphRanges, rangeCount, visibility);
}

void JaliumTextFormat::GenerateGlyphQuadsForEngine(TextEngine* rasterEngine,
    const wchar_t* text, uint32_t textLength, float maxWidth, float maxHeight,
    float colorR, float colorG, float colorB, float colorA, float originX, float originY,
    std::vector<TextGlyphQuad>& outQuads, float renderScale, int32_t renderingModeOverride,
    const uint32_t* glyphRanges, uint32_t rangeCount, int32_t visibility)
{
    if (!face_ || !rasterEngine || !text || textLength == 0) return;
    if (!std::isfinite(renderScale) || renderScale <= 0 || !std::isfinite(fontSizePx_ * renderScale)) renderScale = 1;
    if (frozenLayout_) {
        EmitGlyphQuads(rasterEngine, *frozenLayout_, text, textLength, maxWidth,
            colorR, colorG, colorB, colorA, originX, originY, outQuads, renderScale,
            renderingModeOverride, glyphRanges, rangeCount, visibility);
        return;
    }

    // When DPI scaling is active, temporarily scale font size and metrics
    // so glyphs are rasterized at physical pixel resolution.
    float savedFontSize = fontSizePx_;
    float savedAscent = ascent_;
    float savedDescent = descent_;
    float savedLineGap = lineGap_;
    float savedLineHeight = lineHeight_;
    struct RestoreMetrics {
        float& fontSize;
        float& ascent;
        float& descent;
        float& lineGap;
        float& lineHeight;
        float savedFontSize;
        float savedAscent;
        float savedDescent;
        float savedLineGap;
        float savedLineHeight;

        ~RestoreMetrics() noexcept
        {
            fontSize = savedFontSize;
            ascent = savedAscent;
            descent = savedDescent;
            lineGap = savedLineGap;
            lineHeight = savedLineHeight;
        }
    } restore{
        fontSizePx_, ascent_, descent_, lineGap_, lineHeight_,
        savedFontSize, savedAscent, savedDescent, savedLineGap, savedLineHeight
    };

    if (!std::isfinite(renderScale) || renderScale <= 0.0f ||
        !std::isfinite(fontSizePx_ * renderScale)) {
        renderScale = 1.0f;
    }

    if (renderScale != 1.0f) {
        fontSizePx_ *= renderScale;
        ascent_ *= renderScale;
        descent_ *= renderScale;
        lineGap_ *= renderScale;
        lineHeight_ *= renderScale;
        maxWidth *= renderScale;
        maxHeight *= renderScale;
    }

    LayoutResult ownedLayout;
    if (!frozenLayout_) { ownedLayout = PerformLayout(text, textLength, maxWidth, maxHeight); ApplyAlignment(ownedLayout, maxWidth, maxHeight); }
    const auto& layout = frozenLayout_ ? *frozenLayout_ : ownedLayout;

    EmitGlyphQuads(rasterEngine, layout, text, textLength, maxWidth,
        colorR, colorG, colorB, colorA, originX, originY, outQuads, 1, renderingModeOverride,
        glyphRanges, rangeCount, visibility);
}

void JaliumTextFormat::EmitGlyphQuads(TextEngine* rasterEngine, const LayoutResult& layout,
    const wchar_t* text, uint32_t textLength, float maxWidth,
    float colorR, float colorG, float colorB, float colorA, float originX, float originY,
    std::vector<TextGlyphQuad>& outQuads, float layoutScale, int32_t renderingModeOverride,
    const uint32_t* glyphRanges, uint32_t rangeCount, int32_t visibility) const
{
    GlyphAtlas* atlas = rasterEngine->GetGlyphAtlas();
    GlyphRasterizer* rasterizer = rasterEngine->GetGlyphRasterizer();
    if (!atlas || !rasterizer) {
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

    // Raster-size cap. A zoomed / print-resolution run (fontSizePx_ easily in
    // the thousands once renderScale is folded in) would rasterize bitmaps
    // larger than the 4096 atlas: unrepresentable glyphs, and each insertion
    // attempt evicting every other cached glyph. Rasterize at the cap and
    // scale the emitted quads back up — layout metrics keep the true size, so
    // only the bitmap is magnified (slightly soft at extreme zoom instead of
    // missing, and other text stays intact).
    // The atlas auto-resets when its packer runs out of room (see
    // GlyphAtlas::GetOrInsert). A reset in the MIDDLE of this run would leave
    // the quads emitted before it pointing at overwritten pixels, so emit the
    // whole run against one atlas generation: if the generation changed while
    // emitting, discard this run's quads and emit again (the atlas is now
    // nearly empty, so the second pass fits).
    std::vector<uint32_t> sourceOffsets;
    if (visibility) {
        sourceOffsets.resize(size_t(textLength) + 1);
        for (uint32_t i = 0; i < textLength; ++i)
            sourceOffsets[i + 1] = sourceOffsets[i] + (sizeof(wchar_t) > 2 && uint32_t(text[i]) > 0xFFFFu ? 2 : 1);
    }
    const size_t baseQuadCount = outQuads.size();
    for (int attempt = 0; attempt < 2; ++attempt)
    {
    const uint64_t atlasGenerationAtStart = atlas->GetGeneration();
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

        float penX = originX + (xOffset + line.originX) * layoutScale;
        float baselineY = originY + line.baselineY * layoutScale;

        for (const auto& glyph : line.glyphs)
        {
            if (glyph.glyphIndex == 0 || (visibility && !TextGlyphVisible(
                sourceOffsets[std::min(glyph.cluster, textLength)],
                sourceOffsets[std::min(glyph.clusterEnd, textLength)], glyphRanges, rangeCount, visibility)))
            {
                penX += glyph.advanceX * layoutScale;
                continue;
            }

            const float glyphSize = (glyph.fontSize > 0 ? glyph.fontSize : fontSizePx_) * layoutScale;
    constexpr float kMaxRasterPpem = 1024.0f;
    const uint16_t rasterPpem = static_cast<uint16_t>(std::lround(
        std::clamp(std::min(glyphSize, kMaxRasterPpem), 1.0f, 4095.0f)));
    const float rasterUpscale = glyphSize > kMaxRasterPpem
        ? glyphSize / static_cast<float>(rasterPpem)
        : 1.0f;
    // Pin sub-pixel phase 0 for big strikes (not just capped ones): 8 phase
    // variants of large glyphs are what overflow the atlas, and the ≤1/8-px
    // residual is invisible at these sizes.
    const bool rasterCapped = rasterUpscale != 1.0f || rasterPpem > 96;

            // Sub-pixel quantization to 1/8 pixel (8 buckets). At 1/4 pixel the
            // size-invariant 0.25px residual is a visible fraction of a small-font
            // advance and reads as phantom inter-glyph spacing; 1/8 pixel halves it.
            // A capped (upscaled) run pins phase 0: the phases are invisible once
            // the bitmap is magnified, and 8 variants of huge glyphs would flood
            // the atlas for nothing.
            // Shaping and CSS advances can move a retained glyph independently
            // of the original pen. Its final origin selects both the atlas
            // phase and the integer quad origin.
            const float glyphX = penX + glyph.offsetX * layoutScale;
            float fractionalX = glyphX - std::floor(glyphX);
            uint8_t subpixelX = rasterCapped
                ? static_cast<uint8_t>(0)
                : static_cast<uint8_t>(fractionalX * 8.0f);
            if (subpixelX > 7) subpixelX = 7;

            // Get or rasterize glyph in atlas. The glyph carries the face/font
            // it was shaped with (primary, or a CJK/Unicode fallback selected in
            // ShapeWithFallback), so rasterize from THAT face — not always face_.
            // rasterPpem is rounded (not truncated): HarfBuzz advances are
            // fractional, so a truncated raster size desyncs glyph ink width
            // from the advance and skews spacing at fractional sizes.
            FontFace* glyphFace   = glyph.face ? glyph.face : face_.get();
            uint64_t  glyphFontId = glyph.face ? glyph.fontId : fontId_;
            const auto& entry = atlas->GetOrInsert(
                *rasterizer, glyphFace, glyphFontId,
                static_cast<uint16_t>(glyph.glyphIndex),
                rasterPpem,
                subpixelX,
                glyphAaMode);

            if (entry.valid && entry.w > 0 && entry.h > 0)
            {
                TextGlyphQuad quad{};
                quad.posX = std::floor(glyphX) + entry.bearingX * rasterUpscale;
                quad.posY = baselineY + glyph.offsetY * layoutScale - entry.bearingY * rasterUpscale;
                quad.sizeX = static_cast<float>(entry.w) * rasterUpscale;
                quad.sizeY = static_cast<float>(entry.h) * rasterUpscale;
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

            penX += glyph.advanceX * layoutScale;
        }
    }
    if (atlas->GetGeneration() == atlasGenerationAtStart)
        break;
    outQuads.resize(baseQuadCount);
    }

}

} // namespace jalium
