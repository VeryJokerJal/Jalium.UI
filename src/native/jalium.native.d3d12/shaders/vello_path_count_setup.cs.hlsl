// Vello GPU Pipeline V3 — path_count_setup
// Port of vello 0.10.0 shader/path_count_setup.wgsl.
// Computes the indirect dispatch size for the path_count stage.
//
// Bindings: u0 bump | u1 indirect
// Dispatch: (1, 1, 1)

#include "vello_shared.hlsli"

RWByteAddressBuffer bump : register(u0);
RWByteAddressBuffer indirect : register(u1);

#define WG_SIZE 256u

[numthreads(1, 1, 1)]
void main()
{
    if (bump.Load(BUMP_FAILED) != 0u) {
        indirect.Store(0, 0u);
    } else {
        uint lines_count = bump.Load(BUMP_LINES);
        indirect.Store(0, (lines_count + (WG_SIZE - 1u)) / WG_SIZE);
    }
    indirect.Store(4, 1u);
    indirect.Store(8, 1u);
}
