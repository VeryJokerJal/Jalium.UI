#pragma once

#include <atomic>
#include <cstdint>

namespace jalium::vulkan_lifecycle {

enum class State : uint8_t {
    Idle,
    BeginningFrame,
    Drawing,
    EndingFrame,
    Exclusive,
};

class Gate final {
public:
    bool TryBeginFrame() noexcept
    {
        State expected = State::Idle;
        return state_.compare_exchange_strong(expected, State::BeginningFrame,
            std::memory_order_acq_rel, std::memory_order_acquire);
    }

    bool CommitBeginFrame() noexcept
    {
        State expected = State::BeginningFrame;
        return state_.compare_exchange_strong(expected, State::Drawing,
            std::memory_order_acq_rel, std::memory_order_acquire);
    }

    bool AbortBeginFrame() noexcept
    {
        State expected = State::BeginningFrame;
        return state_.compare_exchange_strong(expected, State::Idle,
            std::memory_order_acq_rel, std::memory_order_acquire);
    }

    bool TryEndFrame() noexcept
    {
        State expected = State::Drawing;
        return state_.compare_exchange_strong(expected, State::EndingFrame,
            std::memory_order_acq_rel, std::memory_order_acquire);
    }

    bool CompleteEndFrame() noexcept
    {
        State expected = State::EndingFrame;
        return state_.compare_exchange_strong(expected, State::Idle,
            std::memory_order_acq_rel, std::memory_order_acquire);
    }

    bool TryEnterExclusive() noexcept
    {
        State expected = State::Idle;
        return state_.compare_exchange_strong(expected, State::Exclusive,
            std::memory_order_acq_rel, std::memory_order_acquire);
    }

    bool LeaveExclusive() noexcept
    {
        State expected = State::Exclusive;
        return state_.compare_exchange_strong(expected, State::Idle,
            std::memory_order_acq_rel, std::memory_order_acquire);
    }

    State Current() const noexcept
    {
        return state_.load(std::memory_order_acquire);
    }

private:
    std::atomic<State> state_{State::Idle};
};

// The managed reclaimer is driven by CompositionTarget.Rendering. A scan can
// therefore arrive while a window is rendering continuously; the callback
// itself is not proof that this particular native target is idle. Track real
// target activity and make repeated scans for the same idle period no-ops.
class IdleReclaimGate final {
public:
    void NoteActivity(int64_t nowNanoseconds) noexcept
    {
        if (nowNanoseconds > 0) {
            lastActivityNanoseconds_.store(nowNanoseconds, std::memory_order_release);
        }
    }

    bool TryClaim(int64_t nowNanoseconds, int64_t minimumIdleNanoseconds) noexcept
    {
        const int64_t lastActivity =
            lastActivityNanoseconds_.load(std::memory_order_acquire);
        if (lastActivity <= 0 || nowNanoseconds < lastActivity ||
            nowNanoseconds - lastActivity < minimumIdleNanoseconds) {
            return false;
        }

        int64_t lastClaimed =
            lastClaimedActivityNanoseconds_.load(std::memory_order_acquire);
        while (lastClaimed != lastActivity) {
            if (lastClaimedActivityNanoseconds_.compare_exchange_weak(
                    lastClaimed, lastActivity,
                    std::memory_order_acq_rel, std::memory_order_acquire)) {
                return true;
            }
        }
        return false;
    }

private:
    std::atomic<int64_t> lastActivityNanoseconds_{0};
    std::atomic<int64_t> lastClaimedActivityNanoseconds_{0};
};

constexpr bool ShouldAutoRepairSwapchain(
    bool isWindows, bool hasFixedSurfaceExtent,
    bool fixedSurfaceExtentMatchesTarget) noexcept
{
    // Win32 interactive sizing changes the WSI extent before the authoritative
    // Resize call. Rebuilding against the stale target size in that interval
    // causes recreate thrash. A variable-extent surface can use the target size
    // directly; a fixed-extent surface is safe only after both extents agree.
    return !isWindows || !hasFixedSurfaceExtent ||
        fixedSurfaceExtentMatchesTarget;
}

} // namespace jalium::vulkan_lifecycle
