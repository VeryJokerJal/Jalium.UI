// Vello GPU Pipeline V3 — path_tiling_setup
// Port of vello 0.10.0 shader/path_tiling_setup.wgsl.
// Computes the indirect dispatch size for the path_tiling stage, and signals
// the fine rasterizer (which does not bind bump) when a prior stage failed.
//
// Bindings: u0 bump | u1 indirect | u2 ptcl
// Dispatch: (1, 1, 1)

#include "vello_shared.hlsli"

RWByteAddressBuffer bump : register(u0);
RWByteAddressBuffer indirect : register(u1);
RWStructuredBuffer<uint> ptcl : register(u2);

#define WG_SIZE 256u

[numthreads(1, 1, 1)]
void main()
{
    if (bump.Load(BUMP_FAILED) != 0u) {
        indirect.Store(0, 0u);
        // signal fine rasterizer that failure happened (it doesn't bind bump)
        ptcl[0] = ~0u;
    } else {
        uint segments = bump.Load(BUMP_SEG_COUNTS);
        indirect.Store(0, (segments + (WG_SIZE - 1u)) / WG_SIZE);
    }
    indirect.Store(4, 1u);
    indirect.Store(8, 1u);
}
