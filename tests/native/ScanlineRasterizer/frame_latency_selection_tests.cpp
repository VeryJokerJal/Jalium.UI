#include "d3d12_frame_latency_selection.h"
#include <cstdio>
#include <cstdlib>

using jalium::D3D12FrameLatencySelection;
constexpr uint64_t Refresh = 4'166'666;

void Require(bool condition, const char* message) {
    if (!condition) { std::fprintf(stderr, "%s\n", message); std::exit(1); }
}
void Frame(D3D12FrameLatencySelection& policy, uint64_t interval = 7'000'000,
           uint64_t gap = 1'000'000, bool allowed = true, uint64_t refresh = Refresh) {
    policy.BeginFrame(gap, refresh, allowed);
    policy.ObserveFrame(interval, gap, refresh, allowed);
}
void EnterTrial(D3D12FrameLatencySelection& policy) {
    for (int i = 0; i < 7; ++i) {
        Frame(policy);
        Require(policy.Depth() == 1, "A short spike must not deepen the queue");
    }
    Frame(policy);
    Require(policy.Depth() == 2, "Sustained missed refresh budgets should trial two frames");
}

int main() {
    D3D12FrameLatencySelection policy;
    for (int i = 0; i < 1000; ++i) Frame(policy, 4'166'666);
    Require(policy.Depth() == 1, "Rendering within budget must stay at one frame");
    for (int i = 0; i < 1000; ++i) Frame(policy, 17'000'000, 15'000'000);
    Require(policy.Depth() == 1, "Sparse requests must not queue extra frames");
    for (int i = 0; i < 1000; ++i) Frame(policy, 110'000'000, 1'000'000, true, 16'666'666);
    Require(policy.Depth() == 1, "Slow compositor acknowledgments must not deepen the queue");
    for (int i = 0; i < 1000; ++i) Frame(policy, 7'000'000, 1'000'000, false);
    Require(policy.Depth() == 1, "Host overrides and external pacing must bypass adaptation");
    for (int i = 0; i < 1000; ++i) Frame(policy, 7'000'000, 1'000'000, true, 0);
    Require(policy.Depth() == 1, "Unknown display timing must retain the original policy");

    EnterTrial(policy);
    for (int i = 0; i < 1000; ++i) Frame(policy, 4'500'000);
    Require(policy.Depth() == 2, "A sustained throughput gain should retain two frames");
    policy.BeginFrame(17'000'000, Refresh, true);
    Require(policy.Depth() == 1, "Return to one frame before waiting after an idle gap");
    EnterTrial(policy);
    Frame(policy, 4'500'000, 1'000'000, false);
    Require(policy.Depth() == 1, "Disabling adaptation must drain the deeper queue");

    policy.Reset();
    EnterTrial(policy);
    for (int i = 0; i < 16; ++i) Frame(policy, 6'900'000);
    Require(policy.Depth() == 1, "A deeper queue without a material cadence gain must be rejected");
    for (int i = 0; i < 120; ++i) Frame(policy);
    Require(policy.Depth() == 1, "Rejected trials need a cooldown");
    EnterTrial(policy);
    Frame(policy, 110'000'000);
    Require(policy.Depth() == 1, "A compositor slowdown must immediately restore one frame");

    policy.Reset();
    for (int i = 0; i < 8; ++i) Frame(policy, i % 2 == 0 ? 8'000'000 : 4'100'000);
    Require(policy.Depth() == 2, "Mixed one/two-refresh misses need a window, not consecutive misses");
    policy.Reset();
    for (int i = 0; i < 8; ++i) Frame(policy, i == 0 ? 10'000'000 : 4'100'000);
    Require(policy.Depth() == 1, "A single GC/scheduling pause must not activate the deeper queue");
    std::puts("frame latency selection: all checks passed");
}
