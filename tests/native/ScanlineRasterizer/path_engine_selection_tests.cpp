#include "d3d12_path_engine_selection.h"
#include <iostream>
#include <stdexcept>

static void Require(bool value, const char* message) {
    if (!value) throw std::runtime_error(message);
}

int main() {
    try {
        jalium::D3D12PathEngineSelection selection;
        Require(!selection.UseGpuPaths(), "Ordinary UI must start on the CPU path engine.");
        for (int i=0;i<100;i++) selection.ObserveFrame(20000,100,100000);
        Require(!selection.UseGpuPaths(), "Cheap cached geometry should not trigger a switch.");
        for (int i=0;i<10;i++) selection.ObserveFrame(4095,32,3000000);
        Require(!selection.UseGpuPaths(), "Small geometry exceeded the entry threshold.");
        for (int i=0;i<10;i++) selection.ObserveFrame(4096,31,3000000);
        Require(!selection.UseGpuPaths(), "A single complex path triggered a switch.");
        selection.ObserveFrame(4096,32,3000000);
        Require(!selection.UseGpuPaths(), "One transient spike should not change the engine.");
        selection.ObserveFrame(0,0,0);
        selection.ObserveFrame(4096,32,3000000);
        Require(!selection.UseGpuPaths(), "Nonconsecutive heavy frames were combined.");
        selection.ObserveFrame(4096,32,3000000);
        Require(selection.UseGpuPaths(), "Sustained expensive geometry did not select GPU paths.");
        for (int i=0;i<29;i++) selection.ObserveFrame(0,0,0);
        Require(selection.UseGpuPaths(), "One light interaction caused engine flapping.");
        selection.ObserveFrame(0,0,0);
        Require(!selection.UseGpuPaths(), "Stable light content did not return to the UI engine.");
        selection.GpuUnavailable();
        for (int i=0;i<100;i++) selection.ObserveFrame(20000,100,10000000);
        Require(!selection.UseGpuPaths(), "Unavailable GPU paths were retried every frame.");
        selection.Reset();
        selection.ObserveFrame(20000,100,10000000);
        selection.ObserveFrame(20000,100,10000000);
        Require(selection.UseGpuPaths(), "A new engine request did not reset the policy.");
        std::cout << "Adaptive path-engine checks passed.\n";
        return 0;
    } catch (const std::exception& ex) { std::cerr << ex.what() << '\n'; return 1; }
}
