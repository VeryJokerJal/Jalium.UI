#pragma once

// cff_charstring.h
//
// CffFontProgram — CFF (Type2 charstring) outline extraction for 'OTTO' fonts
// and CFF/CID CJK, an internal engine owned by FontFace. Parses the CFF INDEX
// structures, Top/Private DICTs, global/local subrs and (for CIDFonts) the
// FDArray/FDSelect, then interprets a glyph's Type2 charstring into font-unit,
// y-up jalium::Contour lists with cubic Béziers flattened (FlattenCubicBezier).

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
    // Parses the whole 'CFF ' table span. Returns false on malformed input.
    bool Parse(const ByteReader& cffTable, uint16_t numGlyphs);

    // Appends font-unit y-up contours for `gid` to `out`. Returns false for an
    // empty glyph. `tolFU` is the cubic-flatten tolerance (font units).
    bool GetContours(uint16_t gid, float tolFU, std::vector<Contour>& out,
                     float& xMin, float& yMin, float& xMax, float& yMax) const;

    bool IsValid() const noexcept { return valid_; }

private:
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
    // FDSelect (CID only): gid -> fd index. Empty => all fd 0.
    std::vector<uint8_t>  fdSelect_;
    bool                  isCID_ = false;
    uint16_t              numGlyphs_ = 0;
    bool                  valid_ = false;
};

} // namespace jalium::font
