#pragma once

// opentype_cmap.h
//
// Cmap — Unicode codepoint -> glyph id, an internal engine owned by FontFace.
// Selects the best available subtable (prefer full-Unicode format 12, then BMP
// format 4, then 6/0) and decodes formats 0, 4, 6, 12. Symbol subtables (3/0)
// get the 0xF000 private-use remap. All reads are bounds-checked via ByteReader
// so a malformed subtable yields .notdef rather than an OOB read.

#include "sfnt_reader.h"
#include <cstdint>

namespace jalium::font {

class Cmap {
public:
    // Selects and binds the best subtable from the whole `cmap` table span.
    // Returns false if no decodable subtable is present.
    bool Parse(const ByteReader& cmapTable);

    // 0 = .notdef / not covered.
    uint16_t GetGlyphIndex(uint32_t codepoint) const noexcept;

    bool IsValid() const noexcept { return format_ != 0xFFFF; }
    uint16_t Format() const noexcept { return format_; }

private:
    uint16_t   Lookup4 (uint32_t cp) const noexcept;
    uint16_t   Lookup6 (uint32_t cp) const noexcept;
    uint16_t   Lookup0 (uint32_t cp) const noexcept;
    uint16_t   Lookup12(uint32_t cp) const noexcept;

    ByteReader sub_;              // scoped to the selected subtable
    uint16_t   format_ = 0xFFFF;  // 0/4/6/12; 0xFFFF = none
    bool       symbolRemap_ = false;  // 3/0 symbol: try cp and 0xF000|cp
};

} // namespace jalium::font
