#include "opentype_cmap.h"

namespace jalium::font {

bool Cmap::Parse(const ByteReader& cmapTable) {
    format_ = 0xFFFF;
    symbolRemap_ = false;
    if (cmapTable.Size() < 4) return false;

    uint16_t numSubtables = cmapTable.U16(2);
    size_t   bestOff = 0;
    int      bestScore = -1;
    bool     bestSymbol = false;

    for (uint16_t i = 0; i < numSubtables; ++i) {
        size_t rec = 4 + static_cast<size_t>(i) * 8;
        if (!cmapTable.InBounds(rec, 8)) break;
        uint16_t plat = cmapTable.U16(rec);
        uint16_t enc  = cmapTable.U16(rec + 2);
        uint32_t off  = cmapTable.U32(rec + 4);
        if (off + 2 > cmapTable.Size()) continue;
        uint16_t fmt = cmapTable.U16(off);

        // Only formats we can decode are eligible (format-aware selection: a
        // subtable we cannot parse must never be chosen, else lookups return 0).
        int base;
        switch (fmt) {
            case 12: base = 400; break;
            case 4:  base = 300; break;
            case 6:  base = 200; break;
            case 0:  base = 100; break;
            default: continue;
        }
        int bonus = 0;
        bool symbol = false;
        if (fmt == 12 && plat == 3 && enc == 10) bonus = 60;
        else if (fmt == 4 && plat == 3 && enc == 1) bonus = 60;
        else if (plat == 0) bonus = 40;                       // Unicode platform
        else if (fmt == 4 && plat == 3 && enc == 0) { bonus = 20; symbol = true; } // symbol

        int score = base + bonus;
        if (score > bestScore) { bestScore = score; bestOff = off; bestSymbol = symbol; }
    }

    if (bestScore < 0) return false;
    // Scope sub_ to the SELECTED subtable's own declared length so a corrupt
    // idRangeOffset / index cannot read glyph ids from a sibling subtable or the
    // cmap header (which would map a codepoint to a bogus nonzero glyph). Length
    // is a u16 at +2 for formats 0/4/6, a u32 at +4 for format 12.
    uint16_t bestFmt = cmapTable.U16(bestOff);
    size_t subLen = (bestFmt >= 8) ? cmapTable.U32(bestOff + 4) : cmapTable.U16(bestOff + 2);
    if (subLen < 4 || subLen > cmapTable.Size() - bestOff) subLen = cmapTable.Size() - bestOff;
    sub_ = cmapTable.Sub(bestOff, subLen);
    format_ = sub_.U16(0);
    symbolRemap_ = bestSymbol;
    return true;
}

uint16_t Cmap::GetGlyphIndex(uint32_t cp) const noexcept {
    uint16_t g = 0;
    switch (format_) {
        case 12: g = Lookup12(cp); break;
        case 4:  g = Lookup4(cp);  break;
        case 6:  g = Lookup6(cp);  break;
        case 0:  g = Lookup0(cp);  break;
        default: return 0;
    }
    if (g == 0 && symbolRemap_ && cp <= 0xFF) {
        // Symbol fonts map their glyphs into the 0xF000 private-use block.
        uint32_t remap = 0xF000u + cp;
        switch (format_) {
            case 12: g = Lookup12(remap); break;
            case 4:  g = Lookup4(remap);  break;
            case 6:  g = Lookup6(remap);  break;
            case 0:  g = Lookup0(remap);  break;
        }
    }
    return g;
}

uint16_t Cmap::Lookup4(uint32_t cp) const noexcept {
    if (cp > 0xFFFF) return 0;
    uint16_t segX2 = sub_.U16(6);
    uint16_t segCount = segX2 / 2;
    size_t endArr   = 14;
    size_t startArr = endArr + segX2 + 2;   // +2 reservedPad
    size_t deltaArr = startArr + segX2;
    size_t rangeArr = deltaArr + segX2;
    for (uint16_t s = 0; s < segCount; ++s) {
        uint16_t endCode = sub_.U16(endArr + s * 2);
        if (cp <= endCode) {
            uint16_t startCode = sub_.U16(startArr + s * 2);
            if (cp < startCode) return 0;
            int16_t  idDelta = sub_.S16(deltaArr + s * 2);
            uint16_t idRange = sub_.U16(rangeArr + s * 2);
            if (idRange == 0)
                return static_cast<uint16_t>((cp + idDelta) & 0xFFFF);
            // glyphId address per the OpenType idRangeOffset pointer trick.
            size_t gAddr = rangeArr + s * 2 + idRange + static_cast<size_t>(cp - startCode) * 2;
            uint16_t g = sub_.U16(gAddr);
            if (g == 0) return 0;
            return static_cast<uint16_t>((g + idDelta) & 0xFFFF);
        }
    }
    return 0;
}

uint16_t Cmap::Lookup6(uint32_t cp) const noexcept {
    uint16_t firstCode  = sub_.U16(6);
    uint16_t entryCount = sub_.U16(8);
    if (cp < firstCode || cp >= static_cast<uint32_t>(firstCode) + entryCount) return 0;
    return sub_.U16(10 + static_cast<size_t>(cp - firstCode) * 2);
}

uint16_t Cmap::Lookup0(uint32_t cp) const noexcept {
    if (cp >= 256) return 0;
    return sub_.U8(6 + cp);
}

uint16_t Cmap::Lookup12(uint32_t cp) const noexcept {
    uint32_t nGroups = sub_.U32(12);
    // Groups are sorted by startCharCode — binary search.
    uint32_t lo = 0, hi = nGroups;
    while (lo < hi) {
        uint32_t mid = lo + (hi - lo) / 2;
        size_t gr = 16 + static_cast<size_t>(mid) * 12;
        uint32_t startC = sub_.U32(gr);
        uint32_t endC   = sub_.U32(gr + 4);
        if (cp < startC) hi = mid;
        else if (cp > endC) lo = mid + 1;
        else return static_cast<uint16_t>(sub_.U32(gr + 8) + (cp - startC));
    }
    return 0;
}

} // namespace jalium::font
