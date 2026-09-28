#pragma once

#include "jalium_types.h"

#include <cstddef>
#include <cstdint>

namespace jalium::font_math {

// OpenType MATH v1 header: version (4 bytes), then MathConstants offset (2).
// The first two MathConstants values are signed percentages in big-endian order.
inline void Read(const void* data, size_t size, JaliumFontMathConstants* result) noexcept
{
    if (!result || !data || size < 10) return;
    const auto* bytes = static_cast<const uint8_t*>(data);
    auto u16 = [&](size_t offset) -> uint16_t {
        return static_cast<uint16_t>((bytes[offset] << 8) | bytes[offset + 1]);
    };
    if (u16(0) != 1) return;

    result->hasMathTable = 1;
    result->scriptPercentScaleDown = .71f;
    result->scriptScriptPercentScaleDown = .5041f;
    const auto constantsOffset = static_cast<size_t>(u16(4));
    if (constantsOffset < 10 || constantsOffset > size) return;
    if (size - constantsOffset >= 2) {
        const auto percent = static_cast<int16_t>(u16(constantsOffset));
        if (percent > 0) result->scriptPercentScaleDown = percent / 100.f;
    }
    if (size - constantsOffset >= 4) {
        const auto percent = static_cast<int16_t>(u16(constantsOffset + 2));
        if (percent > 0) result->scriptScriptPercentScaleDown = percent / 100.f;
    }
}

} // namespace jalium::font_math
