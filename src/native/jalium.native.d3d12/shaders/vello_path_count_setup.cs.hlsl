// Vello GPU Pipeline V2 — path_count_setup
// Reads bump.lines to set up indirect dispatch arguments for path_count.
//
// Dispatch: 1, 1, 1

#include "vello_shared.hlsli"

RWByteAddressBuffer bump : register(u0);

// IndirectCount: { count_x, count_y, count_z } for ExecuteIndirect
RWByteAddressBuffer indirect : register(u1);

[numthreads(1, 1, 1)]
void main()
{
    uint n_lines;
    bump.InterlockedAdd(BUMP_LINES, 0u, n_lines); // atomic load

    // Check for prior failure
    uint failed;
    bump.InterlockedAdd(BUMP_FAILED, 0u, failed);
    if ((failed & STAGE_FLATTEN) != 0u) {
        indirect.Store(0, 0u);
        indirect.Store(4, 1u);
        indirect.Store(8, 1u);
        return;
    }

    indirect.Store(0, (n_lines + 255u) / 256u);
    indirect.Store(4, 1u);
    indirect.Store(8, 1u);
}
