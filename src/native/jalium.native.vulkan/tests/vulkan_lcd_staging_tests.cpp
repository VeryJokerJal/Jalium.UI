#include "vulkan_lcd_staging.h"

#include <array>
#include <cstdint>
#include <iostream>

namespace {

int failures = 0;

void Check(bool condition, const char* message)
{
    if (!condition) {
        std::cerr << "FAILED: " << message << '\n';
        ++failures;
    }
}

} // namespace

int main()
{
    std::array<uint8_t, 4> staged = {0, 0, 0, 0};
    const std::array<uint8_t, 4> atlas = {240, 128, 32, 240};
    jalium::vulkan_lcd::AccumulateAtlasCoverage(staged.data(), atlas.data());
    Check(staged == std::array<uint8_t, 4>{32, 128, 240, 240},
          "RGBA atlas coverage maps to BGRA staging without channel collapse");

    const std::array<uint8_t, 4> overlapping = {64, 32, 192, 192};
    jalium::vulkan_lcd::AccumulateAtlasCoverage(
        staged.data(), overlapping.data());
    Check(staged[0] == jalium::vulkan_lcd::UnionCoverage(32, 192) &&
              staged[1] == jalium::vulkan_lcd::UnionCoverage(128, 32) &&
              staged[2] == jalium::vulkan_lcd::UnionCoverage(240, 64) &&
              staged[3] == std::max({staged[0], staged[1], staged[2]}),
          "overlapping LCD masks union independently per sub-pixel channel");

    const uint8_t destinationB = 20;
    const uint8_t destinationG = 40;
    const uint8_t destinationR = 60;
    const uint8_t textB = 220;
    const uint8_t textG = 180;
    const uint8_t textR = 140;
    const uint8_t coverageB = 32;
    const uint8_t coverageG = 128;
    const uint8_t coverageR = 224;
    const uint8_t resultB = jalium::vulkan_lcd::BlendChannel(
        textB, destinationB, coverageB);
    const uint8_t resultG = jalium::vulkan_lcd::BlendChannel(
        textG, destinationG, coverageG);
    const uint8_t resultR = jalium::vulkan_lcd::BlendChannel(
        textR, destinationR, coverageR);
    Check(resultB < resultG && resultG < resultR,
          "LCD blend attenuates destination independently instead of using max alpha");
    Check(jalium::vulkan_lcd::ScaleCoverage(200, 128) == 100,
          "text opacity scales channel coverage once");
    Check(jalium::vulkan_lcd::BlendAlpha(128, 192) == 224,
          "destination alpha uses max channel coverage SrcOver semantics");

    if (failures == 0)
        std::cout << "Vulkan LCD staging tests passed.\n";
    return failures == 0 ? 0 : 1;
}
