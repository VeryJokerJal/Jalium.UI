#pragma once

#include "jalium_types.h"
#include <cmath>
#include <cstddef>
#include <cstdint>

namespace jalium::font_units {

// OpenType 'post' uses big-endian FWORDs. Its underlinePosition is the top
// y-coordinate in font units, so negate it for the renderer's downward y axis.
inline void ReadPostUnderline(const void* table, size_t length, float scale,
    JaliumFontUnitMetrics* metrics) noexcept
{
    if (!table || length < 12 || !metrics || !std::isfinite(scale) || scale <= 0) return;
    const auto* bytes = static_cast<const uint8_t*>(table);
    const auto readSigned = [&](size_t offset) {
        const auto raw = (static_cast<uint16_t>(bytes[offset]) << 8) |
                         static_cast<uint16_t>(bytes[offset + 1]);
        return raw < 0x8000u ? static_cast<int32_t>(raw) : static_cast<int32_t>(raw) - 0x10000;
    };
    metrics->underlinePosition = -static_cast<float>(readSigned(8)) * scale;
    metrics->available |= 32;
    const auto thickness = readSigned(10);
    if (thickness > 0) {
        metrics->underlineThickness = static_cast<float>(thickness) * scale;
        metrics->available |= 64;
    }
}

} // namespace jalium::font_units
