#include "ot_layout.h"

#include <algorithm>

namespace jalium::font {

int CoverageIndex(const ByteReader& r, uint32_t covOff, uint16_t gid) {
    uint16_t fmt = r.U16(covOff);
    if (fmt == 1) {
        uint16_t count = r.U16(covOff + 2);
        int lo = 0, hi = static_cast<int>(count) - 1;
        while (lo <= hi) {
            int mid = (lo + hi) / 2;
            uint16_t g = r.U16(covOff + 4 + static_cast<size_t>(mid) * 2);
            if (g == gid) return mid;
            if (g < gid) lo = mid + 1; else hi = mid - 1;
        }
        return -1;
    }
    if (fmt == 2) {
        uint16_t nRanges = r.U16(covOff + 2);
        // Ranges are sorted by start glyph — linear scan is fine (few ranges).
        for (uint16_t i = 0; i < nRanges; ++i) {
            size_t rec = covOff + 4 + static_cast<size_t>(i) * 6;
            uint16_t start = r.U16(rec);
            uint16_t end   = r.U16(rec + 2);
            uint16_t sci   = r.U16(rec + 4);
            if (gid >= start && gid <= end) return sci + (gid - start);
        }
        return -1;
    }
    return -1;
}

int ClassOfGlyph(const ByteReader& r, uint32_t cdOff, uint16_t gid) {
    if (cdOff == 0) return 0;
    uint16_t fmt = r.U16(cdOff);
    if (fmt == 1) {
        uint16_t startGlyph = r.U16(cdOff + 2);
        uint16_t count = r.U16(cdOff + 4);
        if (gid >= startGlyph && gid < static_cast<uint32_t>(startGlyph) + count)
            return r.U16(cdOff + 6 + static_cast<size_t>(gid - startGlyph) * 2);
        return 0;
    }
    if (fmt == 2) {
        uint16_t nRanges = r.U16(cdOff + 2);
        for (uint16_t i = 0; i < nRanges; ++i) {
            size_t rec = cdOff + 4 + static_cast<size_t>(i) * 6;
            uint16_t start = r.U16(rec);
            uint16_t end   = r.U16(rec + 2);
            uint16_t cls   = r.U16(rec + 4);
            if (gid >= start && gid <= end) return cls;
        }
        return 0;
    }
    return 0;
}

LayoutHeader ParseLayoutHeader(const ByteReader& r) {
    LayoutHeader h;
    if (r.Size() < 10) return h;
    h.scriptListOff  = r.U16(4);
    h.featureListOff = r.U16(6);
    h.lookupListOff  = r.U16(8);
    h.valid = (h.lookupListOff != 0);
    return h;
}

static constexpr uint32_t Tag4(char a, char b, char c, char d) {
    return (static_cast<uint32_t>(static_cast<uint8_t>(a)) << 24) |
           (static_cast<uint32_t>(static_cast<uint8_t>(b)) << 16) |
           (static_cast<uint32_t>(static_cast<uint8_t>(c)) << 8)  |
            static_cast<uint32_t>(static_cast<uint8_t>(d));
}

void CollectFeatureLookups(const ByteReader& r, const LayoutHeader& h,
                           const uint32_t* wantedTags, size_t wantedCount,
                           std::vector<uint16_t>& outLookups) {
    outLookups.clear();
    if (!h.valid) return;

    // Choose a script: prefer 'latn', then 'DFLT', else the first.
    uint32_t SL = h.scriptListOff;
    uint16_t scriptCount = r.U16(SL);
    const uint32_t kLatn = Tag4('l', 'a', 't', 'n');
    const uint32_t kDflt = Tag4('D', 'F', 'L', 'T');
    uint32_t latnOff = 0, dfltOff = 0, firstOff = 0;
    for (uint16_t i = 0; i < scriptCount; ++i) {
        size_t rec = SL + 2 + static_cast<size_t>(i) * 6;   // tag(4) + offset(2)
        uint32_t tag = r.U32(rec);
        uint16_t off = r.U16(rec + 4);
        uint32_t sOff = SL + off;
        if (i == 0) firstOff = sOff;
        if (tag == kLatn) latnOff = sOff;
        else if (tag == kDflt) dfltOff = sOff;
    }
    uint32_t scriptOff = latnOff ? latnOff : (dfltOff ? dfltOff : firstOff);
    if (scriptOff == 0) return;

    // LangSys: default, or the first named langsys if no default.
    uint16_t defLang = r.U16(scriptOff);
    uint32_t langSysOff = defLang ? (scriptOff + defLang) : 0;
    if (langSysOff == 0) {
        uint16_t langCount = r.U16(scriptOff + 2);
        if (langCount > 0) langSysOff = scriptOff + r.U16(scriptOff + 4 + 4); // first record's offset (tag4+off2)
    }
    if (langSysOff == 0) return;

    // Feature indices activated by this langsys.
    uint16_t featCount = r.U16(langSysOff + 4);
    std::vector<uint16_t> featIdx;
    featIdx.reserve(featCount);
    for (uint16_t k = 0; k < featCount; ++k)
        featIdx.push_back(r.U16(langSysOff + 6 + static_cast<size_t>(k) * 2));

    // Match feature tags -> collect their lookup indices.
    uint32_t FL = h.featureListOff;
    uint16_t featureCount = r.U16(FL);
    for (uint16_t fi : featIdx) {
        if (fi >= featureCount) continue;
        size_t rec = FL + 2 + static_cast<size_t>(fi) * 6;
        uint32_t tag = r.U32(rec);
        bool wanted = false;
        for (size_t w = 0; w < wantedCount; ++w) if (wantedTags[w] == tag) { wanted = true; break; }
        if (!wanted) continue;
        uint32_t Ft = FL + r.U16(rec + 4);
        uint16_t llc = r.U16(Ft + 2);
        for (uint16_t k = 0; k < llc; ++k) outLookups.push_back(r.U16(Ft + 4 + static_cast<size_t>(k) * 2));
    }

    // Apply in LookupList order, de-duplicated.
    std::sort(outLookups.begin(), outLookups.end());
    outLookups.erase(std::unique(outLookups.begin(), outLookups.end()), outLookups.end());
}

bool GetLookup(const ByteReader& r, const LayoutHeader& h, uint16_t lookupIndex,
               uint16_t extensionType, LookupAccess& out) {
    out.subtables.clear();
    if (!h.valid) return false;
    uint32_t LL = h.lookupListOff;
    uint16_t lookupCount = r.U16(LL);
    if (lookupIndex >= lookupCount) return false;
    uint32_t L = LL + r.U16(LL + 2 + static_cast<size_t>(lookupIndex) * 2);
    uint16_t type = r.U16(L);
    out.flag = r.U16(L + 2);
    uint16_t subCount = r.U16(L + 4);
    out.type = type;
    for (uint16_t i = 0; i < subCount; ++i) {
        uint32_t st = L + r.U16(L + 6 + static_cast<size_t>(i) * 2);
        if (type == extensionType) {
            // ExtensionSubst/Pos: format(1) u16, extensionLookupType u16, extensionOffset u32.
            uint16_t innerType = r.U16(st + 2);
            uint32_t extOff = r.U32(st + 4);
            out.type = innerType;
            out.subtables.push_back(st + extOff);
        } else {
            out.subtables.push_back(st);
        }
    }
    return !out.subtables.empty();
}

} // namespace jalium::font
