#pragma once

// font_data.h
//
// sfnt container decoding shared by every font subsystem: the four-char table
// tags, the offset-table / table-directory parse (including TrueType
// Collection 'ttcf' face selection), and the outline-flavor classification
// (TrueType glyf vs CFF vs CFF2). Header-only; built on ByteReader.

#include "sfnt_reader.h"

#include <cstdint>
#include <vector>

namespace jalium::font {

constexpr uint32_t MakeTag(char a, char b, char c, char d) noexcept {
    return (static_cast<uint32_t>(static_cast<uint8_t>(a)) << 24) |
           (static_cast<uint32_t>(static_cast<uint8_t>(b)) << 16) |
           (static_cast<uint32_t>(static_cast<uint8_t>(c)) << 8)  |
            static_cast<uint32_t>(static_cast<uint8_t>(d));
}

constexpr uint32_t kTag_head = MakeTag('h', 'e', 'a', 'd');
constexpr uint32_t kTag_maxp = MakeTag('m', 'a', 'x', 'p');
constexpr uint32_t kTag_hhea = MakeTag('h', 'h', 'e', 'a');
constexpr uint32_t kTag_hmtx = MakeTag('h', 'm', 't', 'x');
constexpr uint32_t kTag_OS2  = MakeTag('O', 'S', '/', '2');
constexpr uint32_t kTag_cmap = MakeTag('c', 'm', 'a', 'p');
constexpr uint32_t kTag_loca = MakeTag('l', 'o', 'c', 'a');
constexpr uint32_t kTag_glyf = MakeTag('g', 'l', 'y', 'f');
constexpr uint32_t kTag_CFF  = MakeTag('C', 'F', 'F', ' ');
constexpr uint32_t kTag_CFF2 = MakeTag('C', 'F', 'F', '2');
constexpr uint32_t kTag_kern = MakeTag('k', 'e', 'r', 'n');
constexpr uint32_t kTag_GPOS = MakeTag('G', 'P', 'O', 'S');
constexpr uint32_t kTag_GSUB = MakeTag('G', 'S', 'U', 'B');
constexpr uint32_t kTag_GDEF = MakeTag('G', 'D', 'E', 'F');
constexpr uint32_t kTag_post = MakeTag('p', 'o', 's', 't');
constexpr uint32_t kTag_name = MakeTag('n', 'a', 'm', 'e');

// sfnt version words
constexpr uint32_t kSfntVersion1 = 0x00010000; // TrueType outlines
constexpr uint32_t kSfntTrue     = MakeTag('t', 'r', 'u', 'e'); // Apple TrueType
constexpr uint32_t kSfntTyp1     = MakeTag('t', 'y', 'p', '1');
constexpr uint32_t kSfntOTTO     = MakeTag('O', 'T', 'T', 'O'); // CFF outlines
constexpr uint32_t kSfntTtcf     = MakeTag('t', 't', 'c', 'f'); // collection

enum class OutlineFormat { None, TrueType, CFF, CFF2 };

struct TableRecord {
    uint32_t tag    = 0;
    uint32_t offset = 0;  // absolute into the font file
    uint32_t length = 0;
};

// Parses the sfnt offset table + table directory. For a 'ttcf' collection the
// requested face index's sub-font is used (clamped to face 0 on overflow).
class SfntTables {
public:
    // `file` is a reader over the WHOLE font file. A copy (base+size) is kept so
    // Table()/Find() can produce absolute sub-spans. Returns false on malformed
    // input (missing directory, truncated records) — never throws.
    bool Parse(const ByteReader& file, int faceIndex) {
        file_ = file;
        records_.clear();
        outlineFormat = OutlineFormat::None;
        sfntVersion = 0;

        size_t sfntOff = 0;
        uint32_t first = file.U32(0);
        if (first == kSfntTtcf) {
            uint32_t numFonts = file.U32(8);
            if (numFonts == 0) return false;
            uint32_t idx = (faceIndex >= 0 && static_cast<uint32_t>(faceIndex) < numFonts)
                               ? static_cast<uint32_t>(faceIndex) : 0u;
            sfntOff = file.U32(12 + static_cast<size_t>(idx) * 4);
        }

        sfntVersion = file.U32(sfntOff);
        uint16_t numTables = file.U16(sfntOff + 4);
        if (numTables == 0 || numTables > 4096) return false;
        if (!file.InBounds(sfntOff + 12, static_cast<size_t>(numTables) * 16)) return false;

        records_.reserve(numTables);
        for (uint16_t i = 0; i < numTables; ++i) {
            size_t rec = sfntOff + 12 + static_cast<size_t>(i) * 16;
            TableRecord r;
            r.tag    = file.U32(rec);
            r.offset = file.U32(rec + 8);
            r.length = file.U32(rec + 12);
            // Reject records that point outside the file so downstream Sub() is safe.
            if (r.offset <= file.Size() && r.length <= file.Size() - r.offset)
                records_.push_back(r);
            else
                records_.push_back({ r.tag, 0, 0 }); // keep tag presence, zero span
        }

        uint32_t dummyOff, dummyLen;
        if (Find(kTag_CFF2, dummyOff, dummyLen))      outlineFormat = OutlineFormat::CFF2;
        else if (Find(kTag_CFF, dummyOff, dummyLen))  outlineFormat = OutlineFormat::CFF;
        else if (Find(kTag_glyf, dummyOff, dummyLen)) outlineFormat = OutlineFormat::TrueType;

        return !file.Empty() && !records_.empty();
    }

    bool Find(uint32_t tag, uint32_t& outOff, uint32_t& outLen) const {
        for (const auto& r : records_) {
            if (r.tag == tag) { outOff = r.offset; outLen = r.length; return r.length != 0; }
        }
        outOff = 0; outLen = 0; return false;
    }

    // A reader scoped to a table, or an empty reader if the table is absent.
    ByteReader Table(uint32_t tag) const {
        uint32_t off, len;
        if (!Find(tag, off, len)) return ByteReader(nullptr, 0);
        return file_.Sub(off, len);
    }

    bool Has(uint32_t tag) const {
        uint32_t o, l; return Find(tag, o, l);
    }

    OutlineFormat outlineFormat = OutlineFormat::None;
    uint32_t      sfntVersion   = 0;
    const std::vector<TableRecord>& Records() const { return records_; }

private:
    ByteReader               file_;
    std::vector<TableRecord> records_;
};

} // namespace jalium::font
