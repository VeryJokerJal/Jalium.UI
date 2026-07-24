#include "cff_charstring.h"

#include <cmath>
#include <cstdlib>
#include <string>

namespace jalium::font {

// ---------------------------------------------------------------------------
// DICT parsing: operator-key -> operand list. Keys use 1200+b1 for the 12-xx
// escape operators.
// ---------------------------------------------------------------------------
namespace {

struct Dict {
    std::vector<std::pair<int, std::vector<double>>> ops;
    const std::vector<double>* Get(int key) const {
        for (auto& kv : ops) if (kv.first == key) return &kv.second;
        return nullptr;
    }
    double Num(int key, size_t idx, double def) const {
        auto* v = Get(key); if (!v || idx >= v->size()) return def; return (*v)[idx];
    }
    bool Has(int key) const { return Get(key) != nullptr; }
};

// `cff2RegionCounts` non-null selects the CFF2 DICT dialect, which widens the
// one-byte operator range to 22 (vsindex), 23 (blend) and 24 (vstore). A DICT
// blend is collapsed to its default-instance values in place — [bases][n*k
// deltas][n] keeps the n bases and is NOT an entry boundary — because leaked
// deltas would shift positional operands of the entries that matter (e.g. the
// Subrs offset).
Dict ParseDict(const ByteReader& r, uint32_t off, uint32_t len,
               const std::vector<uint16_t>* cff2RegionCounts = nullptr) {
    Dict d; std::vector<double> operands;
    size_t vsIndex = 0;   // CFF2: DICT vsindex governs k for later DICT blends
    uint32_t p = off, end = off + len;
    while (p < end) {
        uint8_t b0 = r.U8(p);
        if (cff2RegionCounts && b0 == 23) {
            p++;
            if (!operands.empty()) {
                double n = operands.back(); operands.pop_back();
                size_t k = vsIndex < cff2RegionCounts->size() ? (*cff2RegionCounts)[vsIndex] : 0;
                if (n > 0 && k > 0) {
                    // Compare in double space: a corrupt count must not be cast first.
                    double drop = n * static_cast<double>(k);
                    if (drop <= static_cast<double>(operands.size()))
                        operands.resize(operands.size() - static_cast<size_t>(drop));
                    else
                        operands.clear();
                }
            }
        } else if (b0 <= 21 || (cff2RegionCounts && (b0 == 22 || b0 == 24))) {
            int op = b0; p++;
            if (b0 == 12) { op = 1200 + r.U8(p); p++; }
            if (op == 22 && cff2RegionCounts && !operands.empty() &&
                operands[0] >= 0 && operands[0] < 65536.0)
                vsIndex = static_cast<size_t>(operands[0]);
            d.ops.push_back({ op, operands }); operands.clear();
        } else if (b0 == 28) { operands.push_back(static_cast<int16_t>(r.U16(p + 1))); p += 3; }
        else if (b0 == 29) { operands.push_back(static_cast<int32_t>(r.U32(p + 1))); p += 5; }
        else if (b0 == 30) { // real number (BCD nibbles)
            p++; std::string s; bool done = false;
            while (p < end && !done) {
                uint8_t by = r.U8(p++);
                for (int half = 0; half < 2 && !done; ++half) {
                    int nib = half == 0 ? (by >> 4) : (by & 0xF);
                    if (nib <= 9) s += static_cast<char>('0' + nib);
                    else if (nib == 0xa) s += '.';
                    else if (nib == 0xb) s += 'E';
                    else if (nib == 0xc) s += "E-";
                    else if (nib == 0xe) s += '-';
                    else if (nib == 0xf) done = true;
                }
            }
            operands.push_back(std::atof(s.c_str()));
        }
        else if (b0 >= 32 && b0 <= 246) { operands.push_back(static_cast<double>(static_cast<int>(b0) - 139)); p++; }
        else if (b0 >= 247 && b0 <= 250) { operands.push_back((static_cast<int>(b0) - 247) * 256 + r.U8(p + 1) + 108); p += 2; }
        else if (b0 >= 251 && b0 <= 254) { operands.push_back(-(static_cast<int>(b0) - 251) * 256 - r.U8(p + 1) - 108); p += 2; }
        else { p++; }
    }
    return d;
}

// DICT operands arrive as doubles (b0==29 int32s, b0==30 BCD reals that can
// overflow to +/-inf); casting a non-finite, negative or oversized value
// straight to an unsigned type is UB. Clamp to [0, limit] and map anything
// outside to 0 ("absent"), which every call site already treats as missing.
uint32_t DictOffset(double v, uint64_t limit) {
    if (!std::isfinite(v) || v < 0.0 || v > static_cast<double>(limit)) return 0;
    return static_cast<uint32_t>(v);
}

} // namespace

CffIndex CffFontProgram::ParseIndex(uint32_t off, uint32_t& endOff) const {
    CffIndex ix;
    // CFF2 widened count to uint32; an empty INDEX ends right after count.
    const uint32_t headSize = isCff2_ ? 4u : 2u;
    const uint32_t count = isCff2_ ? cff_.U32(off) : cff_.U16(off);
    if (count == 0) { endOff = off + headSize; return ix; }
    uint8_t offSize = cff_.U8(off + headSize);
    if (offSize < 1 || offSize > 4) { endOff = off + headSize; return ix; }
    const uint64_t offArr = static_cast<uint64_t>(off) + headSize + 1;
    const uint64_t sz = cff_.Size();
    // The offset array must fit in the table: a corrupt 32-bit count would
    // otherwise reserve gigabytes for the offsets vector below.
    if (offArr > sz || (static_cast<uint64_t>(count) + 1) * offSize > sz - offArr) {
        endOff = off + headSize; return ix;
    }
    uint64_t dataBase = offArr + (static_cast<uint64_t>(count) + 1) * offSize - 1; // 1-based
    ix.offsets.reserve(static_cast<size_t>(count) + 1);
    uint32_t prev = 0;
    for (uint32_t i = 0; i <= count; ++i) {
        uint64_t abs = dataBase + cff_.UOff(static_cast<size_t>(offArr + static_cast<uint64_t>(i) * offSize), offSize);
        // Clamp into the table and enforce monotonicity so objLength() can never
        // underflow (→ huge len) nor point out of bounds on a corrupt INDEX.
        if (abs > sz) abs = sz;
        uint32_t a = static_cast<uint32_t>(abs);
        if (a < prev) a = prev;
        ix.offsets.push_back(a);
        prev = a;
    }
    endOff = ix.offsets.back();
    return ix;
}

bool CffFontProgram::Parse(const ByteReader& cffTable, uint16_t numGlyphs, bool isCff2) {
    cff_ = cffTable;
    numGlyphs_ = numGlyphs;
    valid_ = false;
    isCff2_ = isCff2;
    if (isCff2) return ParseCff2();
    if (cff_.Size() < 4) return false;

    uint8_t hdrSize = cff_.U8(2);
    uint32_t p = hdrSize, endOff = 0;
    ParseIndex(p, endOff); p = endOff;                 // Name INDEX
    CffIndex topIdx = ParseIndex(p, endOff); p = endOff; // Top DICT INDEX
    ParseIndex(p, endOff); p = endOff;                 // String INDEX
    globalSubrs_ = ParseIndex(p, endOff);              // Global Subr INDEX
    if (topIdx.count() == 0) return false;

    Dict top = ParseDict(cff_, topIdx.objOffset(0), topIdx.objLength(0));
    isCID_ = top.Has(1230); // ROS

    uint32_t csOff = static_cast<uint32_t>(top.Num(17, 0, 0));
    if (csOff == 0) return false;
    charStrings_ = ParseIndex(csOff, endOff);
    if (charStrings_.count() == 0) return false;

    if (isCID_) {
        // CID-keyed: FDArray (per-FD Private+subrs) + FDSelect (gid -> fd).
        uint32_t fdArrayOff = static_cast<uint32_t>(top.Num(1236, 0, 0));
        uint32_t fdSelectOff = static_cast<uint32_t>(top.Num(1237, 0, 0));
        if (fdArrayOff) {
            CffIndex fdArray = ParseIndex(fdArrayOff, endOff);
            for (uint32_t i = 0; i < fdArray.count(); ++i) {
                Dict fd = ParseDict(cff_, fdArray.objOffset(i), fdArray.objLength(i));
                CffIndex lsub; float nom = 0, def = 0;
                if (fd.Has(18)) {
                    uint32_t psz = static_cast<uint32_t>(fd.Num(18, 0, 0));
                    uint32_t poff = static_cast<uint32_t>(fd.Num(18, 1, 0));
                    Dict priv = ParseDict(cff_, poff, psz);
                    def = static_cast<float>(priv.Num(20, 0, 0));
                    nom = static_cast<float>(priv.Num(21, 0, 0));
                    if (priv.Has(19)) lsub = ParseIndex(poff + static_cast<uint32_t>(priv.Num(19, 0, 0)), endOff);
                }
                localSubrs_.push_back(std::move(lsub));
                nominalWidthX_.push_back(nom);
                defaultWidthX_.push_back(def);
            }
        }
        if (localSubrs_.empty()) { localSubrs_.emplace_back(); nominalWidthX_.push_back(0); defaultWidthX_.push_back(0); }
        ParseFdSelect(fdSelectOff);
    } else {
        // Non-CID: single Private DICT + local subrs.
        CffIndex lsub; float nom = 0, def = 0;
        if (top.Has(18)) {
            uint32_t psz = static_cast<uint32_t>(top.Num(18, 0, 0));
            uint32_t poff = static_cast<uint32_t>(top.Num(18, 1, 0));
            Dict priv = ParseDict(cff_, poff, psz);
            def = static_cast<float>(priv.Num(20, 0, 0));
            nom = static_cast<float>(priv.Num(21, 0, 0));
            if (priv.Has(19)) lsub = ParseIndex(poff + static_cast<uint32_t>(priv.Num(19, 0, 0)), endOff);
        }
        localSubrs_.push_back(std::move(lsub));
        nominalWidthX_.push_back(nom);
        defaultWidthX_.push_back(def);
    }

    valid_ = true;
    return true;
}

// CFF2 table: bare 5-byte header + Top DICT + Global Subr INDEX — there are no
// Name/TopDict/String INDEXes and no charset/encoding. Only the default
// instance is materialized: the VariationStore is read solely for each
// ItemVariationData's region count (k), which tells blend how many deltas to
// drop per operand.
bool CffFontProgram::ParseCff2() {
    if (cff_.Size() < 5 || cff_.U8(0) != 2) return false; // majorVersion
    uint8_t  hdrSize = cff_.U8(2);
    uint16_t topDictLen = cff_.U16(3);
    if (hdrSize < 5) return false;

    // CFF2 dialect is required here for the one-byte vstore operator (24); the
    // region-count list is still empty at this point, which is fine — Top DICT
    // keys are not blendable.
    Dict top = ParseDict(cff_, hdrSize, topDictLen, &ivdRegionCounts_);

    uint32_t endOff = 0;
    globalSubrs_ = ParseIndex(static_cast<uint32_t>(hdrSize) + topDictLen, endOff);

    // All CFF2 DICT-sourced offsets/sizes go through DictOffset: a hostile
    // DICT can put negative, oversized or non-finite numbers where offsets
    // are expected, and those must not reach a raw unsigned cast.
    const uint64_t sz = cff_.Size();
    uint32_t csOff = DictOffset(top.Num(17, 0, 0), sz);
    if (csOff == 0) return false;
    charStrings_ = ParseIndex(csOff, endOff);
    if (charStrings_.count() == 0) return false;

    // VariationStore: uint16 length prefix, then an ItemVariationStore whose
    // internal offsets are relative to its own start (after the prefix).
    uint32_t vsOff = DictOffset(top.Num(24, 0, 0), sz);
    if (vsOff) {
        uint32_t ivs = vsOff + 2;
        if (cff_.U16(ivs) == 1) { // ItemVariationStore format
            uint16_t ivdCount = cff_.U16(ivs + 6);
            ivdRegionCounts_.reserve(ivdCount);
            for (uint16_t i = 0; i < ivdCount; ++i) {
                // A NULL (0) offset is the spec's "no variation data" slot: k=0
                // makes blend keep its bases and pop the count, exactly the
                // default-instance meaning. Chasing it would misread the store
                // header (variationRegionListOffset's low half) as a count.
                uint32_t rel = cff_.U32(static_cast<size_t>(ivs) + 8 + static_cast<size_t>(i) * 4);
                ivdRegionCounts_.push_back(rel ? cff_.U16(static_cast<size_t>(ivs) + rel + 4)
                                               : static_cast<uint16_t>(0)); // regionIndexCount
            }
        }
    }

    // FDArray is required; FDSelect exists only with more than one Font DICT.
    uint32_t fdArrayOff = DictOffset(top.Num(1236, 0, 0), sz);
    if (fdArrayOff) {
        CffIndex fdArray = ParseIndex(fdArrayOff, endOff);
        // FDSelect can address at most 2^16 FDs; a larger (corrupt) count would
        // only burn time parsing DICTs nothing can reference.
        uint32_t fdCount = fdArray.count();
        if (fdCount > 65536u) fdCount = 65536u;
        for (uint32_t i = 0; i < fdCount; ++i) {
            Dict fd = ParseDict(cff_, fdArray.objOffset(i), fdArray.objLength(i), &ivdRegionCounts_);
            CffIndex lsub;
            uint16_t vsIdx = 0;
            if (fd.Has(18)) {
                uint32_t psz = DictOffset(fd.Num(18, 0, 0), sz);
                uint32_t poff = DictOffset(fd.Num(18, 1, 0), sz);
                Dict priv = ParseDict(cff_, poff, psz, &ivdRegionCounts_);
                // vsindex addresses at most 2^16 ItemVariationData entries.
                vsIdx = static_cast<uint16_t>(DictOffset(priv.Num(22, 0, 0), 0xFFFFu)); // initial ivd for this FD's charstrings
                if (priv.Has(19)) lsub = ParseIndex(poff + DictOffset(priv.Num(19, 0, 0), sz), endOff);
            }
            localSubrs_.push_back(std::move(lsub));
            nominalWidthX_.push_back(0); // CFF2 charstrings carry no widths (hmtx only)
            defaultWidthX_.push_back(0);
            fdVsIndex_.push_back(vsIdx);
        }
    }
    if (localSubrs_.empty()) {
        localSubrs_.emplace_back(); nominalWidthX_.push_back(0); defaultWidthX_.push_back(0);
        fdVsIndex_.push_back(0);
    }

    ParseFdSelect(DictOffset(top.Num(1237, 0, 0), sz));

    valid_ = true;
    return true;
}

void CffFontProgram::ParseFdSelect(uint32_t off) {
    if (!off || off >= cff_.Size()) return;
    fdSelect_.assign(numGlyphs_, 0);
    uint8_t fmt = cff_.U8(off);
    if (fmt == 0) {
        for (uint16_t g = 0; g < numGlyphs_; ++g) fdSelect_[g] = cff_.U8(off + 1 + g);
    } else if (fmt == 3) {
        uint16_t nRanges = cff_.U16(off + 1);
        size_t base = off + 3;
        // Ranges are strictly increasing per spec, so a forward-only cursor
        // keeps total writes bounded by numGlyphs_: a hostile non-monotonic
        // table cannot re-cover the same gid span once per range.
        uint32_t cursor = 0;
        for (uint16_t r = 0; r < nRanges; ++r) {
            uint16_t first = cff_.U16(base + r * 3);
            uint8_t  fd    = cff_.U8(base + r * 3 + 2);
            uint16_t next  = cff_.U16(base + (r + 1) * 3); // sentinel after last
            for (uint32_t g = first > cursor ? first : cursor; g < next && g < numGlyphs_; ++g) fdSelect_[g] = fd;
            if (next > cursor) cursor = next;
        }
    } else if (fmt == 4) {
        // CFF2 large-glyph form: uint32 nRanges, {uint32 first, uint16 fd}
        // records, uint32 sentinel. Clamp nRanges so a corrupt count cannot
        // spin the loop across the whole 32-bit range.
        uint64_t nRanges = cff_.U32(off + 1);
        const uint64_t base = static_cast<uint64_t>(off) + 5;
        const uint64_t maxRanges = cff_.Size() > base + 4 ? (cff_.Size() - base - 4) / 6 : 0;
        if (nRanges > maxRanges) nRanges = maxRanges;
        // Forward-only cursor, same rationale as format 3: without it every
        // range of a hostile table could sweep up to numGlyphs_ gids again.
        uint64_t cursor = 0;
        for (uint64_t r = 0; r < nRanges; ++r) {
            uint32_t first = cff_.U32(static_cast<size_t>(base + r * 6));
            uint16_t fd    = cff_.U16(static_cast<size_t>(base + r * 6 + 4));
            uint32_t next  = cff_.U32(static_cast<size_t>(base + (r + 1) * 6)); // sentinel after last
            for (uint64_t g = first > cursor ? first : cursor; g < next && g < numGlyphs_; ++g) fdSelect_[static_cast<size_t>(g)] = fd;
            if (next > cursor) cursor = next;
        }
    }
}

int CffFontProgram::FdForGlyph(uint16_t gid) const {
    if (fdSelect_.empty() || gid >= fdSelect_.size()) return 0;
    int fd = fdSelect_[gid];
    return (fd >= 0 && fd < static_cast<int>(localSubrs_.size())) ? fd : 0;
}

// ---------------------------------------------------------------------------
// Type2 / CFF2 charstring interpreter
// ---------------------------------------------------------------------------
namespace {

struct T2Interp {
    const ByteReader* cff = nullptr;
    const CffIndex* gsubr = nullptr;
    const CffIndex* lsubr = nullptr;
    int gbias = 0, lbias = 0;
    float nominalWidthX = 0;
    float tolFU = 1.0f;

    // CFF2 mode: no width prefix (haveWidth pre-set true by the caller), and
    // vsindex/blend are live. curK is the delta count per blend operand.
    bool cff2 = false;
    const std::vector<uint16_t>* ivdRegionCounts = nullptr;
    int  curK = 0;

    std::vector<Contour>* out = nullptr;
    Contour cur;
    bool   open = false;
    double x = 0, y = 0;
    double st[513] = {}; int sp = 0;  // CFF2 stack limit; value-init: a truncated charstring must never read uninitialized operands
    int    nStems = 0;
    bool   haveWidth = false;

    void push(double v) { if (sp < 513) st[sp++] = v; }
    void clear() { sp = 0; }

    void setVsIndex(double ivs) {
        curK = 0;
        if (ivdRegionCounts && ivs >= 0 && ivs < static_cast<double>(ivdRegionCounts->size()))
            curK = (*ivdRegionCounts)[static_cast<size_t>(ivs)];
    }

    void moveTo(double nx, double ny) {
        if (open && cur.VertexCount() >= 3) out->push_back(std::move(cur));
        cur = Contour(); open = true; x = nx; y = ny;
        cur.points.push_back(static_cast<float>(x)); cur.points.push_back(static_cast<float>(y));
    }
    void lineTo(double nx, double ny) {
        x = nx; y = ny; cur.points.push_back(static_cast<float>(x)); cur.points.push_back(static_cast<float>(y));
    }
    void curveTo(double x1, double y1, double x2, double y2, double x3, double y3) {
        FlattenCubicBezier(static_cast<float>(x), static_cast<float>(y), static_cast<float>(x1), static_cast<float>(y1),
                           static_cast<float>(x2), static_cast<float>(y2), static_cast<float>(x3), static_cast<float>(y3),
                           cur.points, tolFU);
        x = x3; y = y3;
    }
    void finish() { if (open && cur.VertexCount() >= 3) out->push_back(std::move(cur)); open = false; }

    void run(uint32_t off, uint32_t len, int depth) {
        if (depth > 10) return;
        const ByteReader& r = *cff;
        uint32_t p = off, end = off + len;
        while (p < end) {
            uint8_t b0 = r.U8(p++);
            if (b0 >= 32 || b0 == 28) {
                double v;
                if (b0 == 28) { v = static_cast<int16_t>(r.U16(p)); p += 2; }
                else if (b0 < 247) v = static_cast<int>(b0) - 139;
                else if (b0 < 251) { v = (static_cast<int>(b0) - 247) * 256 + r.U8(p) + 108; p += 1; }
                else if (b0 < 255) { v = -(static_cast<int>(b0) - 251) * 256 - r.U8(p) - 108; p += 1; }
                else { v = static_cast<int32_t>(r.U32(p)) / 65536.0; p += 4; }
                push(v); continue;
            }
            switch (b0) {
            case 1: case 3: case 18: case 23: // h/v stem(hm)
                if (!haveWidth && (sp & 1)) { haveWidth = true; }
                nStems += sp / 2; clear(); break;
            case 19: case 20: // hintmask/cntrmask
                if (!haveWidth && (sp & 1)) { haveWidth = true; }
                nStems += sp / 2; clear();
                p += (nStems + 7) / 8; break;
            case 21: { // rmoveto
                int i = 0; if (!haveWidth && sp > 2) i = 1; haveWidth = true;
                moveTo(x + st[i], y + st[i + 1]); clear(); break; }
            case 22: { // hmoveto
                int i = 0; if (!haveWidth && sp > 1) i = 1; haveWidth = true;
                moveTo(x + st[i], y); clear(); break; }
            case 4: { // vmoveto
                int i = 0; if (!haveWidth && sp > 1) i = 1; haveWidth = true;
                moveTo(x, y + st[i]); clear(); break; }
            case 5: // rlineto
                for (int i = 0; i + 1 < sp; i += 2) lineTo(x + st[i], y + st[i + 1]); clear(); break;
            case 6: case 7: { // hlineto / vlineto
                bool horiz = (b0 == 6);
                for (int i = 0; i < sp; ++i) { if (horiz) lineTo(x + st[i], y); else lineTo(x, y + st[i]); horiz = !horiz; }
                clear(); break; }
            case 8: // rrcurveto
                for (int i = 0; i + 5 < sp; i += 6) {
                    double x1 = x + st[i], y1 = y + st[i + 1], x2 = x1 + st[i + 2], y2 = y1 + st[i + 3], x3 = x2 + st[i + 4], y3 = y2 + st[i + 5];
                    curveTo(x1, y1, x2, y2, x3, y3);
                } clear(); break;
            case 24: { // rcurveline
                int i = 0; for (; i + 5 < sp - 2; i += 6) { double x1 = x + st[i], y1 = y + st[i+1], x2 = x1 + st[i+2], y2 = y1 + st[i+3], x3 = x2 + st[i+4], y3 = y2 + st[i+5]; curveTo(x1,y1,x2,y2,x3,y3); }
                lineTo(x + st[i], y + st[i + 1]); clear(); break; }
            case 25: { // rlinecurve
                int i = 0; for (; i + 1 < sp - 6; i += 2) lineTo(x + st[i], y + st[i + 1]);
                double x1 = x + st[i], y1 = y + st[i+1], x2 = x1 + st[i+2], y2 = y1 + st[i+3], x3 = x2 + st[i+4], y3 = y2 + st[i+5]; curveTo(x1,y1,x2,y2,x3,y3); clear(); break; }
            case 26: { // vvcurveto
                int i = 0; double dx1 = 0; if (sp & 1) { dx1 = st[0]; i = 1; }
                for (; i + 3 < sp; i += 4) { double x1 = x + dx1, y1 = y + st[i], x2 = x1 + st[i+1], y2 = y1 + st[i+2], x3 = x2, y3 = y2 + st[i+3]; curveTo(x1,y1,x2,y2,x3,y3); dx1 = 0; }
                clear(); break; }
            case 27: { // hhcurveto
                int i = 0; double dy1 = 0; if (sp & 1) { dy1 = st[0]; i = 1; }
                for (; i + 3 < sp; i += 4) { double x1 = x + st[i], y1 = y + dy1, x2 = x1 + st[i+1], y2 = y1 + st[i+2], x3 = x2 + st[i+3], y3 = y2; curveTo(x1,y1,x2,y2,x3,y3); dy1 = 0; }
                clear(); break; }
            case 30: case 31: { // vhcurveto / hvcurveto
                bool horiz = (b0 == 31); int i = 0;
                while (i + 3 < sp) {
                    bool last = (sp - i == 5);
                    double x1, y1, x2, y2, x3, y3;
                    if (horiz) { x1 = x + st[i]; y1 = y; x2 = x1 + st[i+1]; y2 = y1 + st[i+2]; y3 = y2 + st[i+3]; x3 = last ? x2 + st[i+4] : x2; }
                    else       { x1 = x; y1 = y + st[i]; x2 = x1 + st[i+1]; y2 = y1 + st[i+2]; x3 = x2 + st[i+3]; y3 = last ? y2 + st[i+4] : y2; }
                    curveTo(x1, y1, x2, y2, x3, y3); i += 4; horiz = !horiz;
                }
                clear(); break; }
            case 15: // CFF2 vsindex (reserved in Type2): selects the active ItemVariationData
                if (cff2 && sp > 0) setVsIndex(st[sp - 1]);
                clear(); break;
            case 16: { // CFF2 blend: [n bases][n*k deltas][n] — keep the n bases (default instance)
                if (!cff2 || sp <= 0) { clear(); break; }
                double n = st[sp - 1];
                if (n >= 0 && n * (curK + 1) + 1 <= static_cast<double>(sp))
                    sp -= 1 + static_cast<int>(n) * curK;
                else
                    clear();
                break; }
            case 10: { // callsubr
                if (sp > 0) { int idx = static_cast<int>(st[--sp]) + lbias;
                    if (lsubr && idx >= 0 && idx < static_cast<int>(lsubr->count())) run(lsubr->objOffset(idx), lsubr->objLength(idx), depth + 1); }
                break; }
            case 29: { // callgsubr
                if (sp > 0) { int idx = static_cast<int>(st[--sp]) + gbias;
                    if (gsubr && idx >= 0 && idx < static_cast<int>(gsubr->count())) run(gsubr->objOffset(idx), gsubr->objLength(idx), depth + 1); }
                break; }
            case 11: return; // return (reserved in CFF2 — treated as a tolerated no-op return)
            case 14: // endchar (reserved in CFF2; CFF2 ends implicitly at end-of-data)
                haveWidth = true; finish(); return;
            case 12: { // escape: flex family + misc
                uint8_t b1 = r.U8(p++);
                switch (b1) {
                case 34: { // hflex
                    if (sp >= 7) {
                        double dx1 = st[0], dx2 = st[1], dy2 = st[2], dx3 = st[3], dx4 = st[4], dx5 = st[5], dx6 = st[6];
                        double x1 = x + dx1, y1 = y, x2 = x1 + dx2, y2 = y1 + dy2, x3 = x2 + dx3, y3 = y2;
                        curveTo(x1, y1, x2, y2, x3, y3);
                        double x4 = x + dx4, y4 = y, x5 = x4 + dx5, y5 = y4 - dy2, x6 = x5 + dx6, y6 = y5;
                        curveTo(x4, y4, x5, y5, x6, y6);
                    } clear(); break; }
                case 35: { // flex
                    if (sp >= 13) {
                        double x1 = x + st[0], y1 = y + st[1], x2 = x1 + st[2], y2 = y1 + st[3], x3 = x2 + st[4], y3 = y2 + st[5];
                        curveTo(x1, y1, x2, y2, x3, y3);
                        double x4 = x + st[6], y4 = y + st[7], x5 = x4 + st[8], y5 = y4 + st[9], x6 = x5 + st[10], y6 = y5 + st[11];
                        curveTo(x4, y4, x5, y5, x6, y6);
                    } clear(); break; }
                case 36: { // hflex1
                    if (sp >= 9) {
                        double dy1 = st[1], dy2 = st[3], dy5 = st[7];
                        double x1 = x + st[0], y1 = y + dy1, x2 = x1 + st[2], y2 = y1 + dy2, x3 = x2 + st[4], y3 = y2;
                        curveTo(x1, y1, x2, y2, x3, y3);
                        double x4 = x + st[5], y4 = y, x5 = x4 + st[6], y5 = y4 + dy5, x6 = x5 + st[8], y6 = y5 - (dy1 + dy2 + dy5);
                        curveTo(x4, y4, x5, y5, x6, y6);
                    } clear(); break; }
                case 37: { // flex1
                    if (sp >= 11) {
                        double startX = x, startY = y;
                        double dx = st[0] + st[2] + st[4] + st[6] + st[8];
                        double dy = st[1] + st[3] + st[5] + st[7] + st[9];
                        double x1 = x + st[0], y1 = y + st[1];
                        double x2 = x1 + st[2], y2 = y1 + st[3];
                        double x3 = x2 + st[4], y3 = y2 + st[5];
                        curveTo(x1, y1, x2, y2, x3, y3);
                        double x4 = x3 + st[6], y4 = y3 + st[7];
                        double x5 = x4 + st[8], y5 = y4 + st[9];
                        double x6, y6;
                        if (std::fabs(dx) > std::fabs(dy)) { x6 = x5 + st[10]; y6 = startY; }
                        else                               { x6 = startX;      y6 = y5 + st[10]; }
                        curveTo(x4, y4, x5, y5, x6, y6);
                    }
                    clear(); break; }
                default: clear(); break;
                }
                break; }
            default: clear(); break;
            }
        }
    }
};

} // namespace

bool CffFontProgram::GetContours(uint16_t gid, float tolFU, std::vector<Contour>& out,
                                 float& xMin, float& yMin, float& xMax, float& yMax) const {
    if (!valid_ || gid >= charStrings_.count()) return false;
    int fd = FdForGlyph(gid);

    T2Interp t;
    t.cff = &cff_;
    t.gsubr = &globalSubrs_;
    t.lsubr = (fd < static_cast<int>(localSubrs_.size())) ? &localSubrs_[fd] : nullptr;
    t.gbias = SubrBias(globalSubrs_.count());
    t.lbias = t.lsubr ? SubrBias(t.lsubr->count()) : 0;
    t.nominalWidthX = (fd < static_cast<int>(nominalWidthX_.size())) ? nominalWidthX_[fd] : 0;
    t.tolFU = tolFU;
    t.out = &out;
    if (isCff2_) {
        t.cff2 = true;
        t.haveWidth = true; // CFF2 charstrings carry no leading width operand
        t.ivdRegionCounts = &ivdRegionCounts_;
        t.setVsIndex(fd < static_cast<int>(fdVsIndex_.size()) ? fdVsIndex_[fd] : 0);
    }

    size_t before = out.size();
    t.run(charStrings_.objOffset(gid), charStrings_.objLength(gid), 0);
    t.finish();
    if (out.size() == before) return false;

    xMin = yMin = 1e30f; xMax = yMax = -1e30f;
    for (size_t ci = before; ci < out.size(); ++ci) {
        const Contour& c = out[ci];
        for (uint32_t i = 0; i < c.VertexCount(); ++i) {
            float x = c.X(i), y = c.Y(i);
            if (x < xMin) xMin = x; if (x > xMax) xMax = x;
            if (y < yMin) yMin = y; if (y > yMax) yMax = y;
        }
    }
    return true;
}

} // namespace jalium::font
