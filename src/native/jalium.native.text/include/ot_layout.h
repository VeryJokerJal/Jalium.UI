#pragma once

// ot_layout.h
//
// Shared OpenType Layout (GSUB/GPOS) common-table navigators used by the
// shaper: the header's three list offsets, script/langsys/feature -> lookup
// collection, Coverage (formats 1/2), ClassDef (formats 1/2), and Extension
// (GSUB type 7 / GPOS type 9) unwrapping. All reads are bounds-checked.

#include "sfnt_reader.h"
#include <cstdint>
#include <vector>

namespace jalium::font {

// glyph -> coverage index within a Coverage table at absolute offset covOff,
// or -1 if not covered.
int CoverageIndex(const ByteReader& r, uint32_t covOff, uint16_t gid);

// glyph -> class value (0 = default) within a ClassDef table at classDefOff.
int ClassOfGlyph(const ByteReader& r, uint32_t classDefOff, uint16_t gid);

struct LayoutHeader {
    uint32_t scriptListOff = 0;   // absolute into the GSUB/GPOS reader
    uint32_t featureListOff = 0;
    uint32_t lookupListOff = 0;
    bool     valid = false;
};

LayoutHeader ParseLayoutHeader(const ByteReader& r);

// Collect (in lookup-list order) the lookups activated by any feature whose
// 4-char tag is in wantedTags, taking the default LangSys of the preferred
// script (latn, else DFLT, else the first script).
void CollectFeatureLookups(const ByteReader& r, const LayoutHeader& h,
                           const uint32_t* wantedTags, size_t wantedCount,
                           std::vector<uint16_t>& outLookups);

// One lookup resolved to its effective type + subtable offsets. Extension
// lookups are transparently unwrapped: `type` is the inner type and
// `subtables` point at the inner subtables.
struct LookupAccess {
    uint16_t type = 0;
    uint16_t flag = 0;
    std::vector<uint32_t> subtables;  // absolute offsets into the reader
};

// extensionType: 7 for GSUB, 9 for GPOS (the type whose subtables wrap others).
bool GetLookup(const ByteReader& r, const LayoutHeader& h, uint16_t lookupIndex,
               uint16_t extensionType, LookupAccess& out);

} // namespace jalium::font
