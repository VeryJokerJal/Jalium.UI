#include "ot_shaper.h"
#include "ot_layout.h"
#include "font_face.h"
#include "font_data.h"

#include <algorithm>

namespace jalium::font {

static constexpr uint32_t Tag4(char a, char b, char c, char d) {
    return (static_cast<uint32_t>(static_cast<uint8_t>(a)) << 24) |
           (static_cast<uint32_t>(static_cast<uint8_t>(b)) << 16) |
           (static_cast<uint32_t>(static_cast<uint8_t>(c)) << 8)  |
            static_cast<uint32_t>(static_cast<uint8_t>(d));
}

// ---- GSUB helpers -----------------------------------------------------------

// Single substitution (type 1). Returns true + newGid if `gid` is covered.
static bool SingleSubst(const ByteReader& r, uint32_t st, uint16_t gid, uint16_t& newGid) {
    uint16_t fmt = r.U16(st);
    int ci = CoverageIndex(r, st + r.U16(st + 2), gid);
    if (ci < 0) return false;
    if (fmt == 1) { newGid = static_cast<uint16_t>((gid + r.S16(st + 4)) & 0xFFFF); return true; }
    if (fmt == 2) {
        uint16_t count = r.U16(st + 4);
        if (ci < count) { newGid = r.U16(st + 6 + static_cast<size_t>(ci) * 2); return true; }
    }
    return false;
}

// Ligature substitution (type 4). If glyphs[pos..] starts a ligature, returns
// its glyph + component count (>= 2).
static bool LigatureSubst(const ByteReader& r, uint32_t st,
                          const std::vector<ShapedGlyphItem>& g, size_t pos,
                          uint16_t& outLig, int& outComps) {
    if (r.U16(st) != 1) return false;
    int ci = CoverageIndex(r, st + r.U16(st + 2), static_cast<uint16_t>(g[pos].glyphIndex));
    if (ci < 0) return false;
    uint16_t ligSetCount = r.U16(st + 4);
    if (ci >= ligSetCount) return false;
    uint32_t ligSet = st + r.U16(st + 6 + static_cast<size_t>(ci) * 2);
    uint16_t ligCount = r.U16(ligSet);
    for (uint16_t j = 0; j < ligCount; ++j) {
        uint32_t lig = ligSet + r.U16(ligSet + 2 + static_cast<size_t>(j) * 2);
        uint16_t ligGid = r.U16(lig);
        uint16_t compCount = r.U16(lig + 2);
        // A ligature must consume >= 2 glyphs. A self-referential compCount==1
        // ligature would erase nothing and never advance in ApplyGsub, looping
        // forever on a hostile font (render-thread DoS).
        if (compCount < 2 || pos + compCount > g.size()) continue;
        bool match = true;
        for (uint16_t k = 1; k < compCount; ++k) {
            if (g[pos + k].glyphIndex != r.U16(lig + 4 + static_cast<size_t>(k - 1) * 2)) { match = false; break; }
        }
        if (match) { outLig = ligGid; outComps = compCount; return true; }
    }
    return false;
}

static void ApplyGsub(const ByteReader& gsub, std::vector<ShapedGlyphItem>& glyphs) {
    LayoutHeader h = ParseLayoutHeader(gsub);
    if (!h.valid) return;
    const uint32_t wanted[] = { Tag4('c','c','m','p'), Tag4('l','i','g','a'), Tag4('r','l','i','g') };
    std::vector<uint16_t> lookups;
    CollectFeatureLookups(gsub, h, wanted, 3, lookups);

    for (uint16_t li : lookups) {
        LookupAccess la;
        if (!GetLookup(gsub, h, li, /*extType*/ 7, la)) continue;
        if (la.type == 1) {
            for (auto& it : glyphs) {
                for (uint32_t st : la.subtables) {
                    uint16_t ng;
                    if (SingleSubst(gsub, st, static_cast<uint16_t>(it.glyphIndex), ng)) { it.glyphIndex = ng; break; }
                }
            }
        } else if (la.type == 4) {
            size_t i = 0;
            while (i < glyphs.size()) {
                bool merged = false;
                for (uint32_t st : la.subtables) {
                    uint16_t ligGid; int comps;
                    if (LigatureSubst(gsub, st, glyphs, i, ligGid, comps)) {
                        glyphs[i].glyphIndex = ligGid;
                        // cluster stays at the first component; drop the rest.
                        glyphs.erase(glyphs.begin() + i + 1, glyphs.begin() + i + comps);
                        merged = true;
                        break;
                    }
                }
                if (!merged) ++i;
            }
        }
        // Other GSUB lookup types intentionally skipped (graceful degradation).
    }
}

// ---- GPOS helpers -----------------------------------------------------------

static int PopcountBytes(uint16_t vf) { int c = 0; for (int b = 0; b < 8; ++b) if (vf & (1 << b)) ++c; return c * 2; }
static int VrXAdvance(const ByteReader& r, uint32_t at, uint16_t vf) {
    if (!(vf & 0x0004)) return 0;
    int off = 0; if (vf & 0x0001) off += 2; if (vf & 0x0002) off += 2;   // XPlacement, YPlacement precede XAdvance
    return r.S16(at + off);
}

// PairPos (type 2, formats 1 & 2). Returns the value1 XAdvance in font units.
static int PairPos(const ByteReader& r, uint32_t st, uint16_t g1, uint16_t g2) {
    uint16_t fmt = r.U16(st);
    uint16_t vf1 = r.U16(st + 4), vf2 = r.U16(st + 6);
    int ci = CoverageIndex(r, st + r.U16(st + 2), g1);
    if (ci < 0) return 0;
    if (fmt == 1) {
        uint16_t pairSetCount = r.U16(st + 8);
        if (ci >= pairSetCount) return 0;
        uint32_t ps = st + r.U16(st + 10 + static_cast<size_t>(ci) * 2);
        uint16_t pvc = r.U16(ps);
        int recSize = 2 + PopcountBytes(vf1) + PopcountBytes(vf2);
        for (uint16_t i = 0; i < pvc; ++i) {
            uint32_t rec = ps + 2 + static_cast<size_t>(i) * recSize;
            if (r.U16(rec) == g2) return VrXAdvance(r, rec + 2, vf1);
        }
        return 0;
    }
    if (fmt == 2) {
        uint16_t class1Count = r.U16(st + 12), class2Count = r.U16(st + 14);
        int c1 = ClassOfGlyph(r, st + r.U16(st + 8), g1);
        int c2 = ClassOfGlyph(r, st + r.U16(st + 10), g2);
        if (c1 >= class1Count || c2 >= class2Count) return 0;
        int recSize = PopcountBytes(vf1) + PopcountBytes(vf2);
        uint32_t rec = st + 16 + (static_cast<size_t>(c1) * class2Count + c2) * recSize;
        return VrXAdvance(r, rec, vf1);
    }
    return 0;
}

// Returns true if a GPOS 'kern' feature exists (and was applied).
static bool ApplyGposKern(const ByteReader& gpos, std::vector<ShapedGlyphItem>& glyphs, float scale) {
    LayoutHeader h = ParseLayoutHeader(gpos);
    if (!h.valid) return false;
    const uint32_t wanted[] = { Tag4('k','e','r','n') };
    std::vector<uint16_t> lookups;
    CollectFeatureLookups(gpos, h, wanted, 1, lookups);
    if (lookups.empty()) return false;
    if (glyphs.size() < 2) return true; // kern feature present; nothing to pair

    for (uint16_t li : lookups) {
        LookupAccess la;
        if (!GetLookup(gpos, h, li, /*extType*/ 9, la)) continue;
        if (la.type != 2) continue;
        for (size_t i = 0; i + 1 < glyphs.size(); ++i) {
            for (uint32_t st : la.subtables) {
                int adj = PairPos(gpos, st, static_cast<uint16_t>(glyphs[i].glyphIndex),
                                  static_cast<uint16_t>(glyphs[i + 1].glyphIndex));
                if (adj != 0) { glyphs[i].advanceX += adj * scale; break; }
            }
        }
    }
    return true;
}

// Legacy 'kern' table, format 0 (Microsoft). Applied only when GPOS has no kern.
static void ApplyLegacyKern(const ByteReader& kern, std::vector<ShapedGlyphItem>& glyphs, float scale) {
    if (kern.Size() < 4 || glyphs.size() < 2) return;
    uint16_t nTables = kern.U16(2);
    for (size_t i = 0; i + 1 < glyphs.size(); ++i) {
        uint16_t g1 = static_cast<uint16_t>(glyphs[i].glyphIndex);
        uint16_t g2 = static_cast<uint16_t>(glyphs[i + 1].glyphIndex);
        uint32_t key = (static_cast<uint32_t>(g1) << 16) | g2;
        uint32_t p = 4;
        for (uint16_t t = 0; t < nTables; ++t) {
            uint16_t stLen = kern.U16(p + 2);
            uint16_t coverage = kern.U16(p + 4);
            if ((coverage >> 8) == 0) {   // format 0
                uint32_t sp = p + 6;
                uint16_t nPairs = kern.U16(sp);
                uint32_t pairs = sp + 8;  // skip searchRange/entrySelector/rangeShift
                int lo = 0, hi = static_cast<int>(nPairs) - 1;
                while (lo <= hi) {
                    int mid = (lo + hi) / 2;
                    uint32_t rec = pairs + static_cast<size_t>(mid) * 6;
                    uint32_t k = (static_cast<uint32_t>(kern.U16(rec)) << 16) | kern.U16(rec + 2);
                    if (k == key) { glyphs[i].advanceX += kern.S16(rec + 4) * scale; lo = hi + 1; break; }
                    if (k < key) lo = mid + 1; else hi = mid - 1;
                }
            }
            p += stLen;
        }
    }
}

// ---- bidi -------------------------------------------------------------------

static bool IsStrongRtl(uint32_t cp) {
    return (cp >= 0x0590 && cp <= 0x05FF) ||   // Hebrew
           (cp >= 0x0600 && cp <= 0x06FF) ||   // Arabic
           (cp >= 0x0700 && cp <= 0x074F) ||   // Syriac
           (cp >= 0x0750 && cp <= 0x077F) ||   // Arabic Supplement
           (cp >= 0x08A0 && cp <= 0x08FF) ||   // Arabic Extended-A
           (cp >= 0xFB1D && cp <= 0xFDFF) ||   // Hebrew/Arabic presentation A
           (cp >= 0xFE70 && cp <= 0xFEFF);     // Arabic presentation B
}

// ---- driver -----------------------------------------------------------------

void OtShaper::Shape(const jalium::FontFace& face,
                     const uint32_t* cps, uint32_t count,
                     float fontSizePx, bool isRtl,
                     std::vector<ShapedGlyphItem>& out) {
    out.clear();
    if (!cps || count == 0) return;
    const float upem = static_cast<float>(face.UnitsPerEm());
    const float scale = upem > 0 ? fontSizePx / upem : 0.0f;

    out.reserve(count);
    int rtlVotes = 0, ltrVotes = 0;
    for (uint32_t i = 0; i < count; ++i) {
        ShapedGlyphItem it;
        it.glyphIndex = face.GetGlyphIndex(cps[i]);
        it.cluster = i;
        out.push_back(it);
        if (IsStrongRtl(cps[i])) ++rtlVotes;
        else if (cps[i] >= 'A') ++ltrVotes;
    }

    // GSUB (may merge/replace glyphs, changing count).
    ByteReader gsub = face.GetTable(font::kTag_GSUB);
    if (gsub.Size() >= 10) ApplyGsub(gsub, out);

    // Advances come from the FINAL glyph ids (post-GSUB), unhinted linear scale.
    for (auto& it : out) it.advanceX = face.GetAdvance(static_cast<uint16_t>(it.glyphIndex)) * scale;

    // GPOS kerning, else legacy kern table.
    ByteReader gpos = face.GetTable(font::kTag_GPOS);
    bool gposKern = false;
    if (gpos.Size() >= 10) gposKern = ApplyGposKern(gpos, out, scale);
    if (!gposKern) ApplyLegacyKern(face.GetTable(font::kTag_kern), out, scale);

    // BiDi: reverse strongly-RTL runs (approximate — full UAX#9 is above us).
    if (isRtl || rtlVotes > ltrVotes)
        std::reverse(out.begin(), out.end());
}

} // namespace jalium::font
