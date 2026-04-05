// Vello GPU Pipeline V2 — path_tiling_setup
// Reads bump.seg_counts to set up indirect dispatch arguments for path_tiling.
//
// Dispatch: 1, 1, 1

#include "vello_shared.hlsli"

RWByteAddressBuffer bump : register(u0);

// IndirectCount: { count_x, count_y, count_z }
RWByteAddressBuffer indirect : register(u1);

[numthreads(1, 1, 1)]
void main()
{
    uint n_seg_counts;
    bump.InterlockedAdd(BUMP_SEG_COUNTS, 0u, n_seg_counts); // atomic load

    // Check for prior failure
    uint failed;
    bump.InterlockedAdd(BUMP_FAILED, 0u, failed);
    if ((failed & (STAGE_FLATTEN | STAGE_TILE_ALLOC | STAGE_PATH_COUNT)) != 0u) {
        indirect.Store(0, 0u);
        indirect.Store(4, 1u);
        indirect.Store(8, 1u);
        return;
    }

    indirect.Store(0, (n_seg_counts + 255u) / 256u);
    indirect.Store(4, 1u);
    indirect.Store(8, 1u);
}
