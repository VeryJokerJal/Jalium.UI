#pragma once

#include <cstdint>

namespace jalium {

// Auto starts with the inexpensive UI path engine. Sustained complex geometry
// that actually spends a frame budget on CPU preparation can use GPU paths.
// Separate entry/exit thresholds keep ordinary interaction from flapping modes.
class D3D12PathEngineSelection {
public:
    bool UseGpuPaths() const noexcept { return useGpu_ && !unavailable_; }

    void Reset() noexcept { useGpu_ = unavailable_ = false; heavy_ = light_ = 0; }
    void GpuUnavailable() noexcept { unavailable_ = true; useGpu_ = false; }

    void ObserveFrame(uint64_t points, uint32_t calls, uint64_t cpuPreparationNs) noexcept {
        if (unavailable_) return;
        if (!useGpu_) {
            const bool heavy = points >= 4096 && calls >= 32 && cpuPreparationNs >= 3'000'000;
            heavy_ = heavy ? heavy_ + 1 : 0;
            if (heavy_ >= 2) { useGpu_ = true; heavy_ = light_ = 0; }
        } else {
            const bool light = points < 1024 || calls < 8;
            light_ = light ? light_ + 1 : 0;
            if (light_ >= 30) { useGpu_ = false; heavy_ = light_ = 0; }
        }
    }

private:
    uint32_t heavy_ = 0;
    uint32_t light_ = 0;
    bool useGpu_ = false;
    bool unavailable_ = false;
};

} // namespace jalium
