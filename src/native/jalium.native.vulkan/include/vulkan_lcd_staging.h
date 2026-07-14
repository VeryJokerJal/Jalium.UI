#pragma once

#include <algorithm>
#include <cstdint>

namespace jalium::vulkan_lcd {

inline uint8_t UnionCoverage(uint8_t destination, uint8_t source)
{
    return static_cast<uint8_t>(std::min<uint32_t>(
        255u, static_cast<uint32_t>(source) +
                  (static_cast<uint32_t>(destination) *
                   (255u - source) + 127u) / 255u));
}

// The CPU atlas is logical RGBA while Vulkan's shared upload image is BGRA.
// Accumulate a glyph sample into one staging texel without losing independent
// channel coverage. Alpha is the max coverage used for destination alpha.
inline void AccumulateAtlasCoverage(uint8_t* destinationBgra,
                                    const uint8_t* atlasRgba)
{
    destinationBgra[0] = UnionCoverage(destinationBgra[0], atlasRgba[2]);
    destinationBgra[1] = UnionCoverage(destinationBgra[1], atlasRgba[1]);
    destinationBgra[2] = UnionCoverage(destinationBgra[2], atlasRgba[0]);
    destinationBgra[3] = std::max({destinationBgra[0],
                                   destinationBgra[1],
                                   destinationBgra[2]});
}

inline uint8_t ScaleCoverage(uint8_t coverage, uint8_t alpha)
{
    return static_cast<uint8_t>(
        (static_cast<uint32_t>(coverage) * alpha + 127u) / 255u);
}

inline uint8_t BlendChannel(uint8_t textChannel,
                            uint8_t destinationChannel,
                            uint8_t channelCoverage)
{
    const uint32_t coverage = channelCoverage;
    return static_cast<uint8_t>(
        (static_cast<uint32_t>(textChannel) * coverage +
         static_cast<uint32_t>(destinationChannel) * (255u - coverage) +
         127u) / 255u);
}

inline uint8_t BlendAlpha(uint8_t destinationAlpha, uint8_t coverageAlpha)
{
    return static_cast<uint8_t>(std::min<uint32_t>(
        255u, static_cast<uint32_t>(coverageAlpha) +
                  (static_cast<uint32_t>(destinationAlpha) *
                   (255u - coverageAlpha) + 127u) / 255u));
}

} // namespace jalium::vulkan_lcd
