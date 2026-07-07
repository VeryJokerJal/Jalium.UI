#include "glyf_outline.h"

#include <algorithm>
#include <cmath>

namespace jalium::font {

// glyf simple-glyph flag bits.
static constexpr uint8_t kOnCurve      = 0x01;
static constexpr uint8_t kXShort       = 0x02;
static constexpr uint8_t kYShort       = 0x04;
static constexpr uint8_t kRepeat       = 0x08;
static constexpr uint8_t kXSameOrPos   = 0x10;
static constexpr uint8_t kYSameOrPos   = 0x20;

// composite-glyph flag bits.
static constexpr uint16_t kArgsAreWords   = 0x0001;
static constexpr uint16_t kArgsAreXY      = 0x0002;
static constexpr uint16_t kHaveScale      = 0x0008;
static constexpr uint16_t kMoreComponents = 0x0020;
static constexpr uint16_t kHaveXYScale    = 0x0040;
static constexpr uint16_t kHave2x2        = 0x0080;

static constexpr int kMaxCompositeDepth = 6;

bool GlyfOutlineSource::Init(const ByteReader& glyfTable, const ByteReader& locaTable,
                             int indexToLocFormat, uint16_t numGlyphs) {
    glyf_ = glyfTable;
    numGlyphs_ = numGlyphs;
    loca_.clear();
    valid_ = false;

    size_t need = static_cast<size_t>(numGlyphs) + 1;
    if (indexToLocFormat == 0) {
        if (locaTable.Size() < need * 2) return false;
        loca_.reserve(need);
        for (size_t i = 0; i < need; ++i) loca_.push_back(static_cast<uint32_t>(locaTable.U16(i * 2)) * 2u);
    } else {
        if (locaTable.Size() < need * 4) return false;
        loca_.reserve(need);
        for (size_t i = 0; i < need; ++i) loca_.push_back(locaTable.U32(i * 4));
    }
    valid_ = true;
    return true;
}

// One glyf point in font units.
namespace { struct GPt { float x, y; bool on; }; }

bool GlyfOutlineSource::GetContours(uint16_t gid, float tolFU, std::vector<Contour>& out,
                                    float& xMin, float& yMin, float& xMax, float& yMax) const {
    if (!valid_ || gid + 1u >= loca_.size()) return false;
    size_t before = out.size();
    EmitGlyph(gid, 1, 0, 0, 1, 0, 0, tolFU, out, 0);
    if (out.size() == before) return false;

    xMin = yMin = 1e30f; xMax = yMax = -1e30f;
    for (size_t ci = before; ci < out.size(); ++ci) {
        const Contour& c = out[ci];
        for (uint32_t i = 0; i < c.VertexCount(); ++i) {
            float x = c.X(i), y = c.Y(i);
            xMin = std::min(xMin, x); xMax = std::max(xMax, x);
            yMin = std::min(yMin, y); yMax = std::max(yMax, y);
        }
    }
    return true;
}

void GlyfOutlineSource::EmitGlyph(uint16_t gid, float a, float b, float c, float d, float e, float f,
                                  float tolFU, std::vector<Contour>& out, int depth) const {
    if (depth > kMaxCompositeDepth || gid + 1u >= loca_.size()) return;
    uint32_t o0 = loca_[gid], o1 = loca_[gid + 1];
    if (o1 <= o0) return;                 // empty glyph (space)
    if (o0 + 10 > glyf_.Size()) return;

    int16_t numberOfContours = glyf_.S16(o0);
    size_t p = o0 + 10;

    // affine: (x,y) -> (a*x + c*y + e, b*x + d*y + f)
    auto TX = [&](float x, float y, float& X, float& Y) { X = a * x + c * y + e; Y = b * x + d * y + f; };

    if (numberOfContours >= 0) {
        // ---- simple glyph ----
        std::vector<uint16_t> endPts(static_cast<size_t>(numberOfContours));
        for (int i = 0; i < numberOfContours; ++i) { endPts[i] = glyf_.U16(p); p += 2; }
        int numPoints = numberOfContours ? (endPts.back() + 1) : 0;
        if (numPoints <= 0 || numPoints > 20000) return;
        uint16_t instrLen = glyf_.U16(p); p += 2 + instrLen;

        std::vector<uint8_t> flags; flags.reserve(numPoints);
        while (static_cast<int>(flags.size()) < numPoints) {
            uint8_t fl = glyf_.U8(p++); flags.push_back(fl);
            if (fl & kRepeat) {
                uint8_t rep = glyf_.U8(p++);
                for (int k = 0; k < rep && static_cast<int>(flags.size()) < numPoints; ++k) flags.push_back(fl);
            }
        }
        std::vector<GPt> pts(numPoints);
        int acc = 0;
        for (int i = 0; i < numPoints; ++i) {
            uint8_t fl = flags[i];
            if (fl & kXShort) { uint8_t dx = glyf_.U8(p++); acc += (fl & kXSameOrPos) ? dx : -static_cast<int>(dx); }
            else if (!(fl & kXSameOrPos)) { acc += glyf_.S16(p); p += 2; }
            pts[i].x = static_cast<float>(acc);
            pts[i].on = (fl & kOnCurve) != 0;
        }
        acc = 0;
        for (int i = 0; i < numPoints; ++i) {
            uint8_t fl = flags[i];
            if (fl & kYShort) { uint8_t dy = glyf_.U8(p++); acc += (fl & kYSameOrPos) ? dy : -static_cast<int>(dy); }
            else if (!(fl & kYSameOrPos)) { acc += glyf_.S16(p); p += 2; }
            pts[i].y = static_cast<float>(acc);
        }

        int start = 0;
        for (int ci = 0; ci < numberOfContours; ++ci) {
            int end = endPts[ci];
            // Guard corrupt / non-monotonic endPtsOfContours: numPoints derives
            // from the LAST endpoint, so an out-of-order earlier endpoint could
            // otherwise index pts[] out of bounds (heap overflow on a hostile font).
            if (end < start || end >= numPoints) break;
            int m = end - start + 1;

            // Cyclic sequence beginning at an on-curve point (synthesize one if
            // the contour is all off-curve).
            std::vector<GPt> seq;
            int first = -1;
            for (int i = 0; i < m; ++i) if (pts[start + i].on) { first = i; break; }
            if (first < 0) {
                GPt a0 = pts[start], b0 = pts[start + m - 1];
                GPt mid{ (a0.x + b0.x) * 0.5f, (a0.y + b0.y) * 0.5f, true };
                seq.push_back(mid);
                for (int i = 0; i < m; ++i) seq.push_back(pts[start + i]);
                seq.push_back(mid);
            } else {
                for (int i = 0; i <= m; ++i) seq.push_back(pts[start + (first + i) % m]);
            }
            // Insert implied on-curve midpoints between consecutive off-curve points.
            std::vector<GPt> full; full.push_back(seq[0]);
            for (size_t i = 1; i < seq.size(); ++i) {
                if (!seq[i - 1].on && !seq[i].on)
                    full.push_back(GPt{ (seq[i - 1].x + seq[i].x) * 0.5f, (seq[i - 1].y + seq[i].y) * 0.5f, true });
                full.push_back(seq[i]);
            }
            // Walk emitting lines + quadratics into a font-unit contour.
            Contour cont;
            float X, Y; TX(full[0].x, full[0].y, X, Y);
            cont.points.push_back(X); cont.points.push_back(Y);
            float curX = full[0].x, curY = full[0].y;
            size_t i = 1;
            while (i < full.size()) {
                if (full[i].on) {
                    TX(full[i].x, full[i].y, X, Y);
                    cont.points.push_back(X); cont.points.push_back(Y);
                    curX = full[i].x; curY = full[i].y; i += 1;
                } else {
                    const GPt& ctrl = full[i];
                    const GPt& endp = full[i + 1];
                    float sX, sY, cX, cY, eX, eY;
                    TX(curX, curY, sX, sY); TX(ctrl.x, ctrl.y, cX, cY); TX(endp.x, endp.y, eX, eY);
                    FlattenQuadraticBezier(sX, sY, cX, cY, eX, eY, cont.points, tolFU);
                    curX = endp.x; curY = endp.y; i += 2;
                }
            }
            if (cont.VertexCount() >= 3) out.push_back(std::move(cont));
            start = end + 1;
        }
    } else {
        // ---- composite glyph ----
        for (;;) {
            uint16_t flags   = glyf_.U16(p); p += 2;
            uint16_t compGid = glyf_.U16(p); p += 2;
            float dx, dy;
            if (flags & kArgsAreWords) { dx = glyf_.S16(p); p += 2; dy = glyf_.S16(p); p += 2; }
            else { dx = static_cast<int8_t>(glyf_.U8(p)); p += 1; dy = static_cast<int8_t>(glyf_.U8(p)); p += 1; }
            float sa = 1, sb = 0, sc = 0, sd = 1;
            if (flags & kHaveScale)        { sa = sd = glyf_.F2Dot14(p); p += 2; }
            else if (flags & kHaveXYScale) { sa = glyf_.F2Dot14(p); p += 2; sd = glyf_.F2Dot14(p); p += 2; }
            else if (flags & kHave2x2)     { sa = glyf_.F2Dot14(p); p += 2; sb = glyf_.F2Dot14(p); p += 2;
                                             sc = glyf_.F2Dot14(p); p += 2; sd = glyf_.F2Dot14(p); p += 2; }
            // Compose: final = parent(a,b,c,d,e,f) ∘ component(sa,sb,sc,sd,dx,dy).
            // Component offsets are XY values only (ARGS_ARE_XY); point-matching is not supported.
            if (flags & kArgsAreXY) {
                float ne = a * dx + c * dy + e;
                float nf = b * dx + d * dy + f;
                float na = a * sa + c * sb, nb = b * sa + d * sb;
                float nc = a * sc + c * sd, nd = b * sc + d * sd;
                EmitGlyph(compGid, na, nb, nc, nd, ne, nf, tolFU, out, depth + 1);
            }
            if (!(flags & kMoreComponents)) break;
        }
    }
}

} // namespace jalium::font
