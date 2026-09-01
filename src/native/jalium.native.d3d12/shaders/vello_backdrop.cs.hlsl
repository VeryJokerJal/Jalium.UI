// Vello GPU Pipeline V3 — backdrop (dynamic)
// Port of vello 0.10.0 shader/backdrop_dyn.wgsl.
// Prefix sum of backdrop values along tile rows for dynamically allocated tiles.
//
// Bindings: b0 config | u0 bump | t0 paths | u1 tiles
// Dispatch: (ceil(n_path / 256), 1, 1)

#include "vello_shared.hlsli"

RWByteAddressBuffer bump : register(u0);
StructuredBuffer<VelloPath> paths : register(t0);
RWStructuredBuffer<VelloTile> tiles : register(u1);

#define WG_SIZE 256u
#define LG_WG_SIZE 8u

groupshared uint sh_row_width[WG_SIZE];
groupshared uint sh_row_count[WG_SIZE];
groupshared uint sh_offset[WG_SIZE];

[numthreads(256, 1, 1)]
void main(uint3 global_id : SV_DispatchThreadID, uint3 local_id : SV_GroupThreadID)
{
    // Abort if any of the prior stages failed.
    if (local_id.x == 0u) {
        sh_row_count[0] = bump.Load(BUMP_FAILED);
    }
    GroupMemoryBarrierWithGroupSync();
    uint failed = sh_row_count[0];
    GroupMemoryBarrierWithGroupSync();
    if (failed != 0u) {
        return;
    }
    uint drawobj_ix = global_id.x;
    uint row_count = 0u;
    if (drawobj_ix < n_drawobj) {
        VelloPath path = paths[drawobj_ix];
        sh_row_width[local_id.x] = path.bbox.z - path.bbox.x;
        row_count = path.bbox.w - path.bbox.y;
        sh_offset[local_id.x] = path.tiles;
    } else {
        // Explicitly zero the row width, just in case.
        sh_row_width[local_id.x] = 0u;
    }
    sh_row_count[local_id.x] = row_count;

    // Prefix sum of row counts
    for (uint i = 0u; i < LG_WG_SIZE; i += 1u) {
        GroupMemoryBarrierWithGroupSync();
        if (local_id.x >= (1u << i)) {
            row_count += sh_row_count[local_id.x - (1u << i)];
        }
        GroupMemoryBarrierWithGroupSync();
        sh_row_count[local_id.x] = row_count;
    }
    GroupMemoryBarrierWithGroupSync();
    uint total_rows = sh_row_count[WG_SIZE - 1u];
    for (uint row = local_id.x; row < total_rows; row += WG_SIZE) {
        uint el_ix = 0u;
        for (uint j = 0u; j < LG_WG_SIZE; j += 1u) {
            uint probe = el_ix + ((WG_SIZE / 2u) >> j);
            if (row >= sh_row_count[probe - 1u]) {
                el_ix = probe;
            }
        }
        uint width = sh_row_width[el_ix];
        if (width > 0u) {
            uint seq_ix = row - (el_ix > 0u ? sh_row_count[el_ix - 1u] : 0u);
            uint tile_ix = sh_offset[el_ix] + seq_ix * width;
            int sum = tiles[tile_ix].backdrop;
            for (uint x = 1u; x < width; x += 1u) {
                tile_ix += 1u;
                sum += tiles[tile_ix].backdrop;
                tiles[tile_ix].backdrop = sum;
            }
        }
    }
}
