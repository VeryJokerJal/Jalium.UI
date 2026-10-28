#pragma once

#include <cstdint>

namespace jalium {

// One frame minimizes queueing. A continuous producer that misses its refresh
// budget can trial a second frame to overlap CPU/GPU work. Retain it only after
// observing a real cadence improvement; sparse input and slow compositors stay
// at one frame. Explicit host settings bypass this policy at the call site.
class D3D12FrameLatencySelection {
public:
    uint32_t Depth() const noexcept { return depth_; }
    void Reset() noexcept { *this = {}; }

    void BeginFrame(uint64_t producerGapNs, uint64_t refreshNs, bool allowed) noexcept {
        if (!allowed || refreshNs == 0 || producerGapNs > refreshNs * 2)
            SingleFrame(false);
    }

    void ObserveFrame(uint64_t intervalNs, uint64_t producerGapNs,
                      uint64_t refreshNs, bool allowed) noexcept {
        const bool continuous = allowed && refreshNs != 0 && producerGapNs <= refreshNs;
        const bool healthy = intervalNs > 0 && refreshNs != 0 && intervalNs <= refreshNs * 3;
        if (!continuous || !healthy) {
            SingleFrame(depth_ == 2 && !healthy);
            return;
        }
        if (cooldown_ != 0) {
            --cooldown_;
            return;
        }
        if (depth_ == 1) {
            baselineSum_ += intervalNs;
            if (intervalNs > refreshNs + refreshNs / 4) ++pressured_;
            if (++baselineFrames_ < 8) return;
            const uint64_t mean = baselineSum_ / baselineFrames_;
            if (pressured_ >= 4 && mean > refreshNs + refreshNs / 4) {
                depth_ = 2;
                baselineNs_ = mean;
                trial_ = true;
            }
            baselineSum_ = baselineFrames_ = pressured_ = 0;
        } else if (trial_) {
            trialSum_ += intervalNs;
            if (++trialFrames_ < 16) return;
            // A deeper queue must improve sustained cadence by at least 10%.
            if (trialSum_ / trialFrames_ > baselineNs_ * 9 / 10)
                SingleFrame(true);
            else
                trial_ = false;
        }
    }

private:
    void SingleFrame(bool backoff) noexcept {
        depth_ = 1;
        baselineSum_ = baselineNs_ = trialSum_ = 0;
        baselineFrames_ = pressured_ = trialFrames_ = 0;
        trial_ = false;
        if (backoff) cooldown_ = 120;
    }

    uint64_t baselineSum_ = 0, baselineNs_ = 0, trialSum_ = 0;
    uint32_t baselineFrames_ = 0, pressured_ = 0, trialFrames_ = 0;
    uint32_t depth_ = 1, cooldown_ = 0;
    bool trial_ = false;
};

} // namespace jalium
