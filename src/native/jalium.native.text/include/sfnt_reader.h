#pragma once

// sfnt_reader.h
//
// Shared big-endian, bounds-checked byte reader for the self-hosted font
// engine. Every sfnt/TrueType/OpenType/CFF parser TU reads through this single
// primitive so a malformed or truncated font can never read out of bounds:
// every accessor is range-checked and fails safe to 0, latching an `ok_` flag.
//
// sfnt data is big-endian. Offsets are absolute into the sub-span the reader
// was constructed over (a whole font, or one table scoped via Sub()).

#include <cstdint>
#include <cstddef>
#include <vector>

namespace jalium::font {

class ByteReader {
public:
    ByteReader() = default;
    ByteReader(const uint8_t* data, size_t size) : base_(data), size_(size) {}
    explicit ByteReader(const std::vector<uint8_t>& v) : base_(v.data()), size_(v.size()) {}

    const uint8_t* Data() const noexcept { return base_; }
    size_t         Size() const noexcept { return size_; }
    bool           Ok()   const noexcept { return ok_; }
    bool           Empty() const noexcept { return size_ == 0; }

    // ---- absolute-offset big-endian reads (bounds-checked, fail-safe 0) ----
    uint8_t  U8 (size_t o) const noexcept { if (!InBounds(o, 1)) return 0; return base_[o]; }
    int8_t   S8 (size_t o) const noexcept { return static_cast<int8_t>(U8(o)); }
    uint16_t U16(size_t o) const noexcept { if (!InBounds(o, 2)) return 0; return static_cast<uint16_t>((base_[o] << 8) | base_[o + 1]); }
    int16_t  S16(size_t o) const noexcept { return static_cast<int16_t>(U16(o)); }
    uint32_t U24(size_t o) const noexcept { if (!InBounds(o, 3)) return 0; return (static_cast<uint32_t>(base_[o]) << 16) | (static_cast<uint32_t>(base_[o + 1]) << 8) | base_[o + 2]; }
    uint32_t U32(size_t o) const noexcept { if (!InBounds(o, 4)) return 0; return (static_cast<uint32_t>(base_[o]) << 24) | (static_cast<uint32_t>(base_[o + 1]) << 16) | (static_cast<uint32_t>(base_[o + 2]) << 8) | base_[o + 3]; }
    int32_t  S32(size_t o) const noexcept { return static_cast<int32_t>(U32(o)); }
    uint32_t Tag(size_t o) const noexcept { return U32(o); }

    // Variable-width big-endian unsigned (CFF offSize 1..4).
    uint32_t UOff(size_t o, int sz) const noexcept {
        uint32_t v = 0;
        for (int i = 0; i < sz; ++i) v = (v << 8) | U8(o + static_cast<size_t>(i));
        return v;
    }

    // ---- fixed point ----
    float F2Dot14(size_t o) const noexcept { return static_cast<int16_t>(U16(o)) / 16384.0f; }  // 2.14 signed
    float Fixed  (size_t o) const noexcept { return static_cast<int32_t>(U32(o)) / 65536.0f; }   // 16.16 signed

    // A sub-reader scoped to [off, off+len), clamped into range. Its ok_ is
    // independent (starts true); reads past its own end fail safe.
    ByteReader Sub(size_t off, size_t len) const noexcept {
        if (off > size_) { off = size_; len = 0; }
        if (len > size_ - off) len = size_ - off;
        return ByteReader(base_ + off, len);
    }

    bool InBounds(size_t off, size_t len) const noexcept {
        if (off > size_ || len > size_ - off) { ok_ = false; return false; }
        return true;
    }

private:
    const uint8_t* base_ = nullptr;
    size_t         size_ = 0;
    mutable bool   ok_   = true;  // latches false on any out-of-bounds access
};

} // namespace jalium::font
