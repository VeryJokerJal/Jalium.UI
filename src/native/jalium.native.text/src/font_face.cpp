#include "font_face.h"
#include "opentype_cmap.h"
#include "glyf_outline.h"
#include "cff_charstring.h"

#include <algorithm>

namespace jalium {

using font::ByteReader;
using font::SfntTables;

// OS/2 fsSelection bit 7 — USE_TYPO_METRICS (prefer sTypo* over hhea/usWin).
static constexpr uint16_t kFsSelUseTypoMetrics = 0x0080;

std::unique_ptr<FontFace> FontFace::Parse(std::vector<uint8_t> bytes, int faceIndex) {
    return Parse(std::make_shared<const std::vector<uint8_t>>(std::move(bytes)), faceIndex);
}

std::unique_ptr<FontFace> FontFace::Parse(std::shared_ptr<const std::vector<uint8_t>> bytes, int faceIndex) {
    if (!bytes || bytes->size() < 12) return nullptr;
    auto face = std::unique_ptr<FontFace>(new FontFace());
    face->bytes_ = std::move(bytes);
    if (!face->ParseInternal(faceIndex) || !face->valid_) return nullptr;
    return face;
}

FontFace::~FontFace() = default;

bool FontFace::ParseInternal(int faceIndex) {
    faceIndex_ = faceIndex;
    ByteReader file(bytes_->data(), bytes_->size());
    if (!tables_.Parse(file, faceIndex)) return false;

    ByteReader head = tables_.Table(font::kTag_head);
    ByteReader maxp = tables_.Table(font::kTag_maxp);
    ByteReader hhea = tables_.Table(font::kTag_hhea);
    if (head.Size() < 54 || maxp.Size() < 6 || hhea.Size() < 36) return false;

    unitsPerEm_       = head.U16(18);
    indexToLocFormat_ = head.S16(50);
    numGlyphs_        = maxp.U16(4);
    numberOfHMetrics_ = hhea.U16(34);
    if (unitsPerEm_ < 16 || unitsPerEm_ > 16384) return false;
    if (numGlyphs_ == 0) return false;
    if (numberOfHMetrics_ == 0) numberOfHMetrics_ = 1;

    hmtx_ = tables_.Table(font::kTag_hmtx);
    metrics_.hmtxValid = hmtx_.Size() >= static_cast<size_t>(numberOfHMetrics_) * 4;

    ResolveMetrics();

    // cmap (codepoint -> glyph). Absence is tolerated (GetGlyphIndex -> 0).
    ByteReader cmapTable = tables_.Table(font::kTag_cmap);
    if (cmapTable.Size() >= 4) {
        auto c = std::make_unique<font::Cmap>();
        if (c->Parse(cmapTable)) cmap_ = std::move(c);
    }

    // Outline source: glyf (TrueType), CFF, or CFF2 (variable fonts, rendered
    // at the default instance — Android 16 ships NotoSansCJK as a CFF2 OTC).
    if (tables_.outlineFormat == font::OutlineFormat::TrueType) {
        ByteReader glyfT = tables_.Table(font::kTag_glyf);
        ByteReader locaT = tables_.Table(font::kTag_loca);
        auto g = std::make_unique<font::GlyfOutlineSource>();
        if (g->Init(glyfT, locaT, indexToLocFormat_, numGlyphs_)) glyf_ = std::move(g);
    } else if (tables_.outlineFormat == font::OutlineFormat::CFF) {
        ByteReader cffT = tables_.Table(font::kTag_CFF);
        auto p = std::make_unique<font::CffFontProgram>();
        if (p->Parse(cffT, numGlyphs_)) cff_ = std::move(p);
    } else if (tables_.outlineFormat == font::OutlineFormat::CFF2) {
        ByteReader cff2T = tables_.Table(font::kTag_CFF2);
        auto p = std::make_unique<font::CffFontProgram>();
        if (p->Parse(cff2T, numGlyphs_, /*isCff2*/ true)) cff_ = std::move(p);
    }

    // A face with no outline source and no color-bitmap tables can never draw
    // anything, yet its cmap would still win fallback selection (which checks
    // coverage only) and every cluster mapped to it would render blank. Fail
    // the parse so face creation returns nullptr and selection moves to the
    // next candidate. CBDT/CBLC (and COLR) faces stay valid: the rasterizer
    // serves those from color records without outlines.
    if (!glyf_ && !cff_ && !HasColorTables()) return false;

    valid_ = true;
    return true;
}

// Mirror FreeType's face->ascender/descender/height selection so text_layout's
// line-metric arithmetic stays identical:
//   1. If OS/2 present with fsSelection USE_TYPO_METRICS → sTypo{Ascender,Descender,LineGap}.
//   2. Else hhea {ascender, descender, lineGap}.
//   3. Else, only when hhea ascender & descender are BOTH zero (broken hhea),
//      fall back to OS/2 sTypo if nonzero, otherwise usWinAscent / -usWinDescent.
void FontFace::ResolveMetrics() {
    ByteReader hhea = tables_.Table(font::kTag_hhea);
    int32_t asc = hhea.S16(4);
    int32_t desc = hhea.S16(6);
    int32_t gap = hhea.S16(8);

    ByteReader os2 = tables_.Table(font::kTag_OS2);
    bool haveOS2 = os2.Size() >= 78;   // through usWinDescent (offset 76..77)
    bool useTypo = false;

    if (haveOS2 && (os2.U16(62) & kFsSelUseTypoMetrics)) {
        asc  = os2.S16(68);
        desc = os2.S16(70);
        gap  = os2.S16(72);
        useTypo = true;
    } else if (asc == 0 && desc == 0 && haveOS2) {
        int32_t sTypoA = os2.S16(68), sTypoD = os2.S16(70), sTypoG = os2.S16(72);
        if (sTypoA != 0 || sTypoD != 0) {
            asc = sTypoA; desc = sTypoD; gap = sTypoG;
        } else {
            asc = static_cast<int32_t>(os2.U16(74));
            desc = -static_cast<int32_t>(os2.U16(76));
            gap = 0;
        }
    }

    metrics_.ascender  = asc;
    metrics_.descender = desc;
    metrics_.height    = asc - desc + gap;
    metrics_.usedTypoMetrics = useTypo;
}

uint16_t FontFace::GetAdvance(uint16_t gid) const noexcept {
    uint16_t idx = gid < numberOfHMetrics_ ? gid : static_cast<uint16_t>(numberOfHMetrics_ - 1);
    return hmtx_.U16(static_cast<size_t>(idx) * 4);
}

int16_t FontFace::GetLeftSideBearing(uint16_t gid) const noexcept {
    if (gid < numberOfHMetrics_)
        return hmtx_.S16(static_cast<size_t>(gid) * 4 + 2);
    // Trailing monospaced-advance region: a run of int16 left-side bearings.
    size_t off = static_cast<size_t>(numberOfHMetrics_) * 4
               + static_cast<size_t>(gid - numberOfHMetrics_) * 2;
    return hmtx_.S16(off);
}

// ── cmap / outline: wired in Stages 2 & 3 (cmap_/glyf_/cff_ null until then) ──

uint16_t FontFace::GetGlyphIndex(uint32_t codepoint) const noexcept {
    if (cmap_) return cmap_->GetGlyphIndex(codepoint);
    return 0;
}

bool FontFace::GetGlyphContours(uint16_t gid, float tolFU, GlyphOutline& out) const {
    out.contours.clear();
    out.hasInk = false;
    if (gid == 0 && !glyf_ && !cff_) return false;
    bool got = false;
    if (glyf_) got = glyf_->GetContours(gid, tolFU, out.contours, out.xMin, out.yMin, out.xMax, out.yMax);
    else if (cff_) got = cff_->GetContours(gid, tolFU, out.contours, out.xMin, out.yMin, out.xMax, out.yMax);
    out.hasInk = got && !out.contours.empty();
    return out.hasInk;
}

} // namespace jalium
