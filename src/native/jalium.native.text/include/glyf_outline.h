#pragma once

// glyf_outline.h
//
// GlyfOutlineSource — TrueType 'glyf'/'loca' outline extraction, an internal
// engine owned by FontFace. Produces font-unit, y-up jalium::Contour lists
// with quadratic Bézier segments flattened (via FlattenQuadraticBezier).
// Handles simple glyphs (flag run-length, implied on-curve midpoints) and
// composite glyphs (F2Dot14 component transforms, recursion with a depth cap).

#include "sfnt_reader.h"
#include "jalium_triangulate.h"   // jalium::Contour + flatteners
#include <cstdint>
#include <vector>

namespace jalium::font {

class GlyfOutlineSource {
public:
    // Binds the glyf/loca spans and parses the loca offset array. Returns false
    // if loca is malformed. indexToLocFormat: 0=short(*2), 1=long.
    bool Init(const ByteReader& glyfTable, const ByteReader& locaTable,
              int indexToLocFormat, uint16_t numGlyphs);

    // Appends font-unit y-up contours for `gid` to `out`. Returns false for an
    // empty glyph (space) — out is left unchanged, bbox untouched. `tolFU` is
    // the max chord deviation (font units) for curve flattening.
    bool GetContours(uint16_t gid, float tolFU, std::vector<Contour>& out,
                     float& xMin, float& yMin, float& xMax, float& yMax) const;

    bool IsValid() const noexcept { return valid_; }

private:
    // Recursive worker: emit `gid`'s contours transformed by the 2x3 affine
    // (a,b,c,d,e,f) into `out`. depth guards composite recursion.
    void EmitGlyph(uint16_t gid, float a, float b, float c, float d, float e, float f,
                   float tolFU, std::vector<Contour>& out, int depth) const;

    ByteReader            glyf_;
    std::vector<uint32_t> loca_;    // numGlyphs+1 absolute offsets into glyf_
    uint16_t              numGlyphs_ = 0;
    bool                  valid_ = false;
};

} // namespace jalium::font
