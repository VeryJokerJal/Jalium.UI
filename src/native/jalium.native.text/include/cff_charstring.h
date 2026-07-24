#pragma once

// cff_charstring.h
//
// CffFontProgram — CFF (Type2 charstring) outline extraction for 'OTTO' fonts
// and CFF/CID CJK, an internal engine owned by FontFace. Parses the CFF INDEX
// structures, Top/Private DICTs, global/local subrs and (for CIDFonts) the
// FDArray/FDSelect, then interprets a glyph's Type2 charstring into font-unit,
// y-up jalium::Contour lists with cubic Béziers flattened (FlattenCubicBezier).
//
// The same program also parses 'CFF2' (OpenType Font Variations) tables in
// default-instance mode: blend deltas are consumed and discarded, so outlines
// come out as the default master. CFF2 differences handled here: bare header +
// Top DICT (no Name/TopDict/String INDEX), 32-bit INDEX counts, vsindex/blend
// operators in DICTs (22/23) and charstrings (15/16), FDSelect format 4, and
// charstrings without width prefixes or endchar/return.

#include "sfnt_reader.h"
#include "jalium_triangulate.h"
#include <cstdint>
#include <vector>

namespace jalium::font {

// One CFF INDEX resolved to (absolute offset, length) object slices.
struct CffIndex {
    std::vector<uint32_t> offsets;   // count+1 entries, absolute into the cff span
    uint32_t count() const { return offsets.empty() ? 0u : (uint32_t)(offsets.size() - 1); }
    uint32_t objOffset(uint32_t i) const { return offsets[i]; }
    uint32_t objLength(uint32_t i) const { return offsets[i + 1] - offsets[i]; }
};

class CffFontProgram {
public:
    // Parses the whole 'CFF ' (or, with isCff2, 'CFF2') table span. Returns
    // false on malformed input.
    bool Parse(const ByteReader& cffTable, uint16_t numGlyphs, bool isCff2 = false);

    // Appends font-unit y-up contours for `gid` to `out`. Returns false for an
    // empty glyph. `tolFU` is the cubic-flatten tolerance (font units).
    bool GetContours(uint16_t gid, float tolFU, std::vector<Contour>& out,
                     float& xMin, float& yMin, float& xMax, float& yMax) const;

    bool IsValid() const noexcept { return valid_; }

private:
    bool     ParseCff2();
    void     ParseFdSelect(uint32_t off);      // formats 0/3 (CFF) and 4 (CFF2)
    CffIndex ParseIndex(uint32_t off, uint32_t& endOff) const;
    int      FdForGlyph(uint16_t gid) const;   // FDSelect; 0 for non-CID
    static int SubrBias(uint32_t count) noexcept { return count < 1240 ? 107 : (count < 33900 ? 1131 : 32768); }

    ByteReader cff_;
    CffIndex   charStrings_;
    CffIndex   globalSubrs_;
    // Per-FD local subrs + nominalWidthX (index 0 used for non-CID fonts).
    std::vector<CffIndex> localSubrs_;
    std::vector<float>    nominalWidthX_;
    std::vector<float>    defaultWidthX_;
    // FDSelect (CID / CFF2 only): gid -> fd index. Empty => all fd 0.
    std::vector<uint16_t> fdSelect_;
    bool                  isCID_ = false;
    uint16_t              numGlyphs_ = 0;
    bool                  valid_ = false;

    // CFF2 state. ivdRegionCounts_[i] is regionIndexCount of ItemVariationData
    // i — the number of deltas (k) each blend operand carries. fdVsIndex_ is
    // the per-FD Private DICT vsindex, the charstring's initial ivd selection.
    bool                  isCff2_ = false;
    std::vector<uint16_t> ivdRegionCounts_;
    std::vector<uint16_t> fdVsIndex_;
};

} // namespace jalium::font
