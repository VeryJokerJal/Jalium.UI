#pragma once

// font_face.h
//
// FontFace — the self-hosted replacement for FreeType's FT_Face. Holds the raw
// font-file bytes (immutable, shareable across faces), parses the sfnt metadata
// tables, and exposes the query surface the rest of the text engine needs:
// vertical metrics (with FreeType-parity selection so layout stays identical),
// cmap codepoint->glyph lookup, hmtx advances, and font-unit glyph outline
// extraction (glyf, CFF, or CFF2 rendered at its default instance).
//
// There is no FontLibrary: FT_Library global state is eliminated. Each FontFace
// is self-contained. Construction is via the static Parse() factory, which
// never throws and returns nullptr on malformed/unsupported input.

#include "jalium_triangulate.h"   // jalium::Contour (+ the Bézier flatteners)
#include "font_data.h"            // jalium::font::SfntTables

#include <cstdint>
#include <memory>
#include <vector>
#include <cstddef>

namespace jalium::font {
class Cmap;
class GlyfOutlineSource;
class CffFontProgram;
} // namespace jalium::font

namespace jalium {

// Font-unit outline of one glyph, in the sfnt y-UP coordinate system. Curves
// are ALREADY flattened to line segments; the rasterizer applies scale + y-flip.
struct GlyphOutline {
    std::vector<Contour> contours;
    float xMin = 0, yMin = 0, xMax = 0, yMax = 0;   // font-unit bbox of the ink
    bool  hasInk = false;                            // false => whitespace/empty
};

// Policy-resolved vertical metrics in DESIGN (font) units. These mirror
// FreeType's face->ascender / face->descender / face->height so the arithmetic
// in text_layout.cpp (ascent/descent/lineGap/lineHeight) stays byte-identical.
struct FontMetrics {
    int32_t ascender  = 0;   // == FT face->ascender  (design units)
    int32_t descender = 0;   // == FT face->descender (design units, usually <= 0)
    int32_t height    = 0;   // == FT face->height    (ascender - descender + lineGap)
    bool    usedTypoMetrics = false;
    bool    hmtxValid = false;
};

class FontFace {
public:
    // Takes ownership of the font-file bytes. faceIndex selects a TTC member
    // (0 for a bare font). Returns nullptr on malformed/unsupported input —
    // including faces with neither an outline source nor color-bitmap tables,
    // which could never draw anything yet would still win cmap-only fallback
    // selection.
    static std::unique_ptr<FontFace> Parse(std::vector<uint8_t> bytes, int faceIndex);

    // Same, but shares an immutable byte buffer: every face over one font file
    // (fallback faces of many text formats, TTC members) references a single
    // copy of the bytes. The provider's process-wide cache feeds this overload.
    static std::unique_ptr<FontFace> Parse(std::shared_ptr<const std::vector<uint8_t>> bytes, int faceIndex);

    ~FontFace();
    FontFace(const FontFace&) = delete;
    FontFace& operator=(const FontFace&) = delete;

    bool     IsValid()    const noexcept { return valid_; }
    uint16_t UnitsPerEm() const noexcept { return unitsPerEm_; }
    uint16_t NumGlyphs()  const noexcept { return numGlyphs_; }
    font::OutlineFormat OutlineFormatKind() const noexcept { return tables_.outlineFormat; }

    // FreeType-parity metric fields (design units).
    int32_t  Ascender()  const noexcept { return metrics_.ascender; }
    int32_t  Descender() const noexcept { return metrics_.descender; }
    int32_t  Height()    const noexcept { return metrics_.height; }
    const FontMetrics& Metrics() const noexcept { return metrics_; }

    // cmap: Unicode codepoint -> glyph id (0 = .notdef / not covered).
    uint16_t GetGlyphIndex(uint32_t codepoint) const noexcept;
    bool     HasGlyph(uint32_t codepoint) const noexcept { return GetGlyphIndex(codepoint) != 0; }

    // hmtx advance / left-side-bearing in DESIGN units ("last long metric
    // repeats" rule for advances). Shaper scales advance by fontSizePx/UnitsPerEm.
    uint16_t GetAdvance(uint16_t gid)         const noexcept;
    int16_t  GetLeftSideBearing(uint16_t gid) const noexcept;

    // Extract font-unit y-up contours, flattening curves so max chord deviation
    // <= flattenToleranceFontUnits. Composites fully resolved. Returns false for
    // empty/space glyphs (out.contours empty, out.hasInk false) — not an error.
    bool GetGlyphContours(uint16_t gid, float flattenToleranceFontUnits, GlyphOutline& out) const;

    // Raw table span for the shaper (GSUB/GPOS/kern parsed by OtShaper). Points
    // into the owned bytes; {nullptr,0}-equivalent empty reader if absent.
    font::ByteReader GetTable(uint32_t tag) const { return tables_.Table(tag); }
    bool HasTable(uint32_t tag) const { return tables_.Has(tag); }
    bool HasColorTables() const noexcept {
        return (tables_.Has(font::kTag_COLR) && tables_.Has(font::kTag_CPAL)) ||
               (tables_.Has(font::kTag_CBDT) && tables_.Has(font::kTag_CBLC));
    }
    const uint8_t* RawData() const noexcept { return bytes_ ? bytes_->data() : nullptr; }
    size_t RawSize() const noexcept { return bytes_ ? bytes_->size() : 0; }
    int FaceIndex() const noexcept { return faceIndex_; }

private:
    FontFace() = default;
    bool ParseInternal(int faceIndex);
    void ResolveMetrics();

    // Immutable file bytes, possibly shared with other faces (never written
    // after Parse, so concurrent readers need no synchronization); all spans
    // view into this buffer.
    std::shared_ptr<const std::vector<uint8_t>> bytes_;
    font::SfntTables tables_;

    uint16_t    unitsPerEm_       = 0;
    uint16_t    numGlyphs_        = 0;
    uint16_t    numberOfHMetrics_ = 0;
    int32_t     indexToLocFormat_ = 0;
    FontMetrics metrics_;
    bool        valid_ = false;
    int         faceIndex_ = 0;

    // Cached table spans used on the hot path.
    font::ByteReader hmtx_;

    // Internal outline/lookup engines (constructed lazily as stages are wired).
    std::unique_ptr<font::Cmap>             cmap_;
    std::unique_ptr<font::GlyfOutlineSource> glyf_;
    std::unique_ptr<font::CffFontProgram>    cff_;
};

} // namespace jalium
