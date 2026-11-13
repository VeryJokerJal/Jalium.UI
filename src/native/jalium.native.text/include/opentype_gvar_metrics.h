#pragma once
#include "sfnt_reader.h"
#include <algorithm>
#include <array>
#include <bitset>
#include <cmath>
#include <vector>

namespace jalium::font {
// Only the two vertical phantom coordinates are needed for an advance. Stream
// packed runs without allocating/decompressing every outline point or doing IUP
// interpolation: omitted phantom points have zero deltas.
class GvarVerticalMetrics {
    ByteReader gvar_, glyf_, loca_, vmtx_;
    uint16_t glyphs_ = 0, metrics_ = 0, axes_ = 0, sharedCount_ = 0;
    size_t shared_ = 0, data_ = 0;
    bool longOffsets_ = false, longLoca_ = false, baseValid_ = false, valid_ = false;

    struct PointMap {
        unsigned count = 0;
        std::array<int, 2> wanted{-1, -1};
    };
    struct Tuple {
        uint16_t bytes = 0;
        bool privatePoints = false;
        double scalar = 0;
    };

    static bool Points(ByteReader data, size_t& at, unsigned total, PointMap& map) {
        if (!data.InBounds(at, 1)) return false;
        unsigned count = data.U8(at++);
        if (!count) {
            map = {total, {int(total - 2), int(total - 1)}};
            return true;
        }
        if (count & 0x80) {
            if (!data.InBounds(at, 1)) return false;
            count = ((count & 0x7f) << 8) | data.U8(at++);
        }
        if (!count || count > total) return false;
        map = {count, {-1, -1}};
        unsigned logical = 0, point = 0;
        while (logical < count) {
            if (!data.InBounds(at, 1)) return false;
            unsigned control = data.U8(at++), run = (control & 0x7f) + 1;
            unsigned bytes = (control & 0x80) ? 2 : 1;
            if (run > count - logical || !data.InBounds(at, size_t(run) * bytes)) return false;
            for (unsigned i = 0; i < run; ++i, ++logical, at += bytes) {
                unsigned delta = bytes == 2 ? data.U16(at) : data.U8(at);
                if ((logical && !delta) || delta >= total || point >= total - delta) return false;
                point += delta;
                if (point == total - 2) map.wanted[0] = int(logical);
                if (point == total - 1) map.wanted[1] = int(logical);
            }
        }
        return true;
    }

    static bool Deltas(ByteReader data, size_t& at, const PointMap& map,
        std::array<double, 2>& values) {
        values = {};
        unsigned logical = 0;
        while (logical < map.count) {
            if (!data.InBounds(at, 1)) return false;
            unsigned control = data.U8(at++), run = (control & 0x3f) + 1;
            if (run > map.count - logical) return false;
            unsigned bytes = (control & 0x80) ? 0 : (control & 0x40) ? 2 : 1;
            if (!data.InBounds(at, size_t(run) * bytes)) return false;
            if (bytes) for (unsigned target = 0; target < 2; ++target) {
                int index = map.wanted[target];
                if (index >= int(logical) && index < int(logical + run)) {
                    size_t offset = at + size_t(index - int(logical)) * bytes;
                    values[target] = bytes == 2 ? data.S16(offset) : data.S8(offset);
                }
            }
            at += size_t(run) * bytes;
            logical += run;
        }
        return true;
    }

    bool Glyph(uint16_t glyph, unsigned& points, int& metricsGlyph) const {
        const size_t stride = longLoca_ ? 4 : 2;
        if (glyph >= glyphs_ || !loca_.InBounds(size_t(glyph) * stride, stride * 2)) return false;
        const size_t begin = longLoca_ ? loca_.U32(size_t(glyph) * 4) : size_t(loca_.U16(size_t(glyph) * 2)) * 2;
        const size_t end = longLoca_ ? loca_.U32(size_t(glyph + 1) * 4) : size_t(loca_.U16(size_t(glyph + 1) * 2)) * 2;
        if (end < begin || !glyf_.InBounds(begin, end - begin)) return false;
        points = 0;
        metricsGlyph = -1;
        if (begin == end) return true;
        auto data = glyf_.Sub(begin, end - begin);
        if (!data.InBounds(0, 10)) return false;
        const int contours = data.S16(0);
        if (contours >= 0) {
            if (!data.InBounds(10, size_t(contours) * 2)) return false;
            for (int i = 0; i < contours; ++i) {
                unsigned next = unsigned(data.U16(10 + size_t(i) * 2)) + 1;
                if (next <= points) return false;
                points = next;
            }
        } else {
            // -1 is recommended, but any negative contour count is composite.
            size_t at = 10;
            unsigned flags = 0;
            do {
                if (!data.InBounds(at, 4)) return false;
                flags = data.U16(at);
                unsigned child = data.U16(at + 2);
                if (child >= glyphs_) return false;
                if (flags & 0x200) metricsGlyph = int(child); // Last USE_MY_METRICS wins.
                unsigned transforms = ((flags & 0x8) != 0) + ((flags & 0x40) != 0) + ((flags & 0x80) != 0);
                if (transforms > 1) return false;
                size_t bytes = 4 + ((flags & 1) ? 4 : 2) + ((flags & 0x8) ? 2 : (flags & 0x40) ? 4 : (flags & 0x80) ? 8 : 0);
                if (!data.InBounds(at, bytes)) return false;
                at += bytes;
                if (++points > 65535) return false;
            } while (flags & 0x20);
        }
        return points <= 65536;
    }

    bool Delta(uint16_t glyph, unsigned total, const std::vector<double>& coordinates,
        double& result, double& topDelta) const {
        topDelta = 0;
        const size_t begin = longOffsets_ ? gvar_.U32(20 + size_t(glyph) * 4) : size_t(gvar_.U16(20 + size_t(glyph) * 2)) * 2;
        const size_t end = longOffsets_ ? gvar_.U32(20 + size_t(glyph + 1) * 4) : size_t(gvar_.U16(20 + size_t(glyph + 1) * 2)) * 2;
        result = 0;
        if (end < begin || data_ > gvar_.Size() || begin > gvar_.Size() - data_ ||
            !gvar_.InBounds(data_ + begin, end - begin)) return false;
        if (begin == end) return true;
        auto data = gvar_.Sub(data_ + begin, end - begin);
        if (!data.InBounds(0, 4) || (data.U16(0) & 0x7000)) return false;
        const unsigned count = data.U16(0) & 0xfff;
        size_t header = 4, serialized = data.U16(2);
        if (serialized < 4 || serialized > data.Size()) return false;
        std::vector<Tuple> tuples;
        tuples.reserve(count);
        for (unsigned i = 0; i < count; ++i) {
            if (!data.InBounds(header, 4)) return false;
            Tuple tuple{data.U16(header), (data.U16(header + 2) & 0x2000) != 0, 1};
            const unsigned flags = data.U16(header + 2);
            if (flags & 0x1000) return false;
            header += 4;
            ByteReader peak;
            if (flags & 0x8000) {
                if (!data.InBounds(header, size_t(axes_) * 2)) return false;
                peak = data.Sub(header, size_t(axes_) * 2);
                header += size_t(axes_) * 2;
            } else {
                const unsigned index = flags & 0xfff;
                if (index >= sharedCount_) return false;
                peak = gvar_.Sub(shared_ + size_t(index) * axes_ * 2, size_t(axes_) * 2);
            }
            ByteReader region;
            if (flags & 0x4000) {
                if (!data.InBounds(header, size_t(axes_) * 4)) return false;
                region = data.Sub(header, size_t(axes_) * 4);
                header += size_t(axes_) * 4;
            }
            for (unsigned axis = 0; axis < axes_; ++axis) {
                const double p = peak.F2Dot14(size_t(axis) * 2), value = coordinates[axis];
                if (p < -1 || p > 1) return false;
                if (p == 0) continue;
                if (region.Empty()) {
                    if (value == 0 || value * p < 0) tuple.scalar = 0;
                    else if (std::abs(value) < std::abs(p)) tuple.scalar *= value / p;
                } else {
                    const double first = region.F2Dot14(size_t(axis) * 2);
                    const double last = region.F2Dot14(size_t(axes_ + axis) * 2);
                    if (first < -1 || last > 1 || first > p || p > last) return false;
                    if (first < 0 && last > 0) continue;
                    if (value < first || value > last) tuple.scalar = 0;
                    else if (value < p) tuple.scalar *= (value - first) / (p - first);
                    else if (value > p) tuple.scalar *= (last - value) / (last - p);
                }
            }
            tuples.push_back(tuple);
        }
        if (header > serialized) return false;
        PointMap shared{total, {int(total - 2), int(total - 1)}};
        if ((data.U16(0) & 0x8000) && !Points(data, serialized, total, shared)) return false;
        for (const auto& tuple : tuples) {
            if (!data.InBounds(serialized, tuple.bytes)) return false;
            auto block = data.Sub(serialized, tuple.bytes);
            serialized += tuple.bytes;
            if (tuple.scalar == 0) continue;
            size_t at = 0;
            PointMap points = shared;
            std::array<double, 2> ignored{}, y{};
            if ((tuple.privatePoints && !Points(block, at, total, points)) ||
                !Deltas(block, at, points, ignored) || !Deltas(block, at, points, y)) return false;
            result += (y[0] - y[1]) * tuple.scalar;
            topDelta += y[0] * tuple.scalar;
        }
        return std::isfinite(result);
    }

public:
    GvarVerticalMetrics(ByteReader gvar, ByteReader glyf, ByteReader loca,
        ByteReader head, ByteReader maxp, ByteReader vhea, ByteReader vmtx)
        : gvar_(gvar), glyf_(glyf), loca_(loca), vmtx_(vmtx) {
        if (!head.InBounds(0, 54) || !maxp.InBounds(0, 6) || !vhea.InBounds(0, 36) ||
            (head.S16(50) != 0 && head.S16(50) != 1)) return;
        glyphs_ = maxp.U16(4);
        metrics_ = vhea.U16(34);
        longLoca_ = head.S16(50) == 1;
        baseValid_ = glyphs_ && metrics_ && metrics_ <= glyphs_;
        if (!baseValid_ || !gvar.InBounds(0, 20) || gvar.U16(0) != 1 ||
            gvar.U16(2) != 0 || (gvar.U16(14) & ~1)) return;
        axes_ = gvar.U16(4);
        sharedCount_ = gvar.U16(6);
        shared_ = gvar.U32(8);
        data_ = gvar.U32(16);
        longOffsets_ = (gvar.U16(14) & 1) != 0;
        const size_t offsetsEnd = 20 + size_t(glyphs_ + 1) * (longOffsets_ ? 4 : 2);
        if (!glyphs_ || !metrics_ || metrics_ > glyphs_ || !axes_ ||
            gvar.U16(12) != glyphs_ ||
            !gvar.InBounds(0, offsetsEnd) || data_ < offsetsEnd || data_ > gvar.Size() ||
            (sharedCount_ && (shared_ < offsetsEnd || !gvar.InBounds(shared_, size_t(sharedCount_) * axes_ * 2)))) return;
        valid_ = true;
    }

    // The default vertical phantom point is yMax + topSideBearing. The TSB
    // array after the long metrics has a different index from the advance.
    bool BaseOrigin(uint16_t glyph, double& origin) const {
        if (!baseValid_ || glyph >= glyphs_) return false;
        const size_t stride = longLoca_ ? 4 : 2;
        if (!loca_.InBounds(size_t(glyph) * stride, stride * 2)) return false;
        const size_t begin = longLoca_ ? loca_.U32(size_t(glyph) * 4) : size_t(loca_.U16(size_t(glyph) * 2)) * 2;
        const size_t end = longLoca_ ? loca_.U32(size_t(glyph + 1) * 4) : size_t(loca_.U16(size_t(glyph + 1) * 2)) * 2;
        if (end < begin || !glyf_.InBounds(begin, end - begin) || (end != begin && end - begin < 10)) return false;
        const size_t at = glyph < metrics_ ? size_t(glyph) * 4 + 2 : size_t(metrics_) * 4 + size_t(glyph - metrics_) * 2;
        if (!vmtx_.InBounds(at, 2)) return false;
        origin = (begin == end ? 0 : glyf_.S16(begin + 8)) + vmtx_.S16(at);
        return true;
    }
    bool Advance(uint16_t glyph, const std::vector<double>& coordinates, double& advance) const {
        return Compute(glyph, coordinates, advance, nullptr);
    }
    bool Metrics(uint16_t glyph, const std::vector<double>& coordinates, double& advance, double& origin) const {
        return Compute(glyph, coordinates, advance, &origin);
    }
private:
    bool Compute(uint16_t glyph, const std::vector<double>& coordinates, double& advance, double* origin) const {
        if (!valid_ || glyph >= glyphs_ || coordinates.size() != axes_ ||
            std::any_of(coordinates.begin(), coordinates.end(), [](double v) { return !std::isfinite(v) || v < -1 || v > 1; })) return false;
        std::bitset<65536> visited;
        // Iteration handles arbitrarily deep valid USE_MY_METRICS chains without
        // consuming the C++ call stack; repeated glyphs reject a cycle.
        for (;;) {
            if (visited.test(glyph)) return false;
            visited.set(glyph);
            unsigned points = 0;
            int child = -1;
            if (!Glyph(glyph, points, child)) return false;
            if (child >= 0) { glyph = uint16_t(child); continue; }
            const size_t at = size_t(std::min<unsigned>(glyph, metrics_ - 1)) * 4;
            if (!vmtx_.InBounds(at, 2)) return false;
            double delta = 0, topDelta = 0;
            if (!Delta(glyph, points + 4, coordinates, delta, topDelta)) return false;
            advance = vmtx_.U16(at) + delta;
            if (origin) {
                if (!BaseOrigin(glyph, *origin)) return false;
                *origin += topDelta;
                if (!std::isfinite(*origin)) return false;
            }
            return std::isfinite(advance) && advance >= 0;
        }
    }
};
} // namespace jalium::font
