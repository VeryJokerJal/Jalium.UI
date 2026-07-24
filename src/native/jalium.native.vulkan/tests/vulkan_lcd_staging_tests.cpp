#include "vulkan_lcd_staging.h"
#include "vulkan_render_lifecycle.h"

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
    using jalium::vulkan_lifecycle::Gate;
    using jalium::vulkan_lifecycle::IdleReclaimGate;
    using jalium::vulkan_lifecycle::ShouldAutoRepairSwapchain;

    Gate lifecycle;
    Check(lifecycle.TryBeginFrame(), "idle target starts a frame");
    Check(!lifecycle.TryEnterExclusive(),
          "resize cannot enter while BeginDraw is in progress");
    Check(lifecycle.CommitBeginFrame(), "BeginDraw commits the drawing state");
    Check(!lifecycle.TryBeginFrame(), "a second BeginDraw is rejected");
    Check(!lifecycle.TryEnterExclusive(), "resize is busy during an open frame");
    Check(lifecycle.TryEndFrame(), "EndDraw exclusively claims the open frame");
    Check(!lifecycle.TryEndFrame(), "a concurrent EndDraw is rejected");
    Check(!lifecycle.TryEnterExclusive(), "resize is busy while EndDraw presents");
    Check(lifecycle.CompleteEndFrame(), "EndDraw releases the target");
    Check(lifecycle.TryEnterExclusive(), "resize enters between frames");
    Check(!lifecycle.TryBeginFrame(), "BeginDraw is rejected during resize");
    Check(lifecycle.LeaveExclusive(), "resize releases the target");

    Gate abortedBegin;
    Check(abortedBegin.TryBeginFrame(), "failed BeginDraw claims the target");
    Check(abortedBegin.AbortBeginFrame(), "failed BeginDraw returns to idle");
    Check(abortedBegin.TryEnterExclusive(),
          "resize proceeds after a failed BeginDraw was unwound");
    Check(abortedBegin.LeaveExclusive(), "failed-Begin test releases resize");

    Check(!ShouldAutoRepairSwapchain(true, true, false),
          "Windows waits while the surface still disagrees with the target size");
    Check(ShouldAutoRepairSwapchain(true, true, true),
          "Windows repairs a final OUT_OF_DATE after the surface size stabilizes");
    Check(ShouldAutoRepairSwapchain(true, false, false),
          "Windows variable-extent WSI can rebuild directly at the target size");
    Check(ShouldAutoRepairSwapchain(false, true, false),
          "non-Windows WSI keeps in-place swapchain repair");

    IdleReclaimGate idleReclaim;
    constexpr int64_t idleWindow = 2'000'000'000LL;
    Check(!idleReclaim.TryClaim(3'000'000'000LL, idleWindow),
          "a never-rendered target has nothing to reclaim");
    idleReclaim.NoteActivity(1'000'000'000LL);
    Check(!idleReclaim.TryClaim(2'999'999'999LL, idleWindow),
          "an actively rendered target is not reclaimed by the periodic scan");
    Check(idleReclaim.TryClaim(3'000'000'000LL, idleWindow),
          "a genuinely idle target is reclaimed once");
    Check(!idleReclaim.TryClaim(4'000'000'000LL, idleWindow),
          "repeated scans in one idle period are cheap no-ops");
    idleReclaim.NoteActivity(5'000'000'000LL);
    Check(idleReclaim.TryClaim(7'000'000'000LL, idleWindow),
          "fresh activity opens one later reclaim period");

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
