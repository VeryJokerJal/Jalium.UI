// Vello GPU Pipeline V2 — backdrop
// Computes inclusive prefix sum of backdrop values per tile row.
// Each workgroup handles one row of tiles (up to 64 tiles wide).
//
// Dispatch: ceil(total_tile_rows / 1), 1, 1
// where total_tile_rows = sum over all paths of (path.bbox.w - path.bbox.y)
// For simplicity: dispatch(width_of_path_row_list, 1, 1)
// Actually Vello dispatches (n_paths, 1, 1) where each WG handles one row.
// Here we use the simple approach: one WG per row, WG_SIZE=64.
// dispatch: (total_rows, 1, 1) — precomputed from path data.
//
// Alternative: dispatch(n_paths * max_height_in_tiles, 1, 1) with early exit.

#include "vello_shared.hlsli"

cbuffer VelloConfig : register(b0)
{
    uint width_in_tiles;
    uint height_in_tiles;
    uint target_width;
    uint target_height;
    uint base_color;
    uint n_drawobj;
    uint n_path;
    uint n_clip;
    uint bin_data_start;
    uint lines_size;
    uint binning_size;
    uint tiles_size;
    uint seg_counts_size;
    uint segments_size;
    uint blend_size;
    uint ptcl_size;
    uint num_segments;
    uint pad0, pad1, pad2, pad3, pad4, pad5, pad6;
};

// VelloTile: { backdrop(i32), segment_count_or_ix(u32) } = 8 bytes
RWStructuredBuffer<VelloTile> tiles : register(u0);

#define BACKDROP_WG_SIZE 64u

groupshared int sh_backdrop[BACKDROP_WG_SIZE];

// The dispatch uses wg_id.x as the row index into a flat list of all tile rows.
// We need a way to map wg_id.x -> (path, row_within_path).
// For simplicity, we dispatch with the path array as a lookup table.
// wg_id.x indexes directly into the tile array (one WG per tile row).
// The total number of workgroups = total number of tile rows allocated.

// Simple approach: wg_id.x = tile_row_index
// Each WG processes width_in_tiles tiles starting at tiles[wg_id.x * width_in_tiles]
// BUT this only works if tiles are laid out as a simple grid.
// In Vello, tiles are allocated per-path, so the layout is:
//   path0_tiles[stride0 * height0], path1_tiles[stride1 * height1], ...
// The backdrop needs to be computed per-path per-row.

// The reference Vello backdrop.wgsl is very simple: it reads tiles[ix] where
// ix = wg_id.x * width_in_tiles + local_id.x. This works because the dispatch
// count equals the total number of tile rows across all paths.

// For our implementation, each workgroup handles one row within the tile array.
// The dispatch count = total allocated tiles / width_per_row, but since paths have
// different widths (strides), we use the path array to look up stride.

// SIMPLIFICATION: Since our paths store contiguous tile blocks with variable strides,
// and Vello's backdrop shader expects a flat grid, we need to adapt.
// We dispatch one WG per path per row: dispatch(n_drawobj, max_height, 1)
// where each (gid.x, gid.y) = (drawobj_ix, row_within_path).

StructuredBuffer<VelloPath> paths : register(t0);

[numthreads(64, 1, 1)]
void main(uint3 groupId : SV_GroupID, uint3 localId : SV_GroupThreadID)
{
    uint drawobj_ix = groupId.x;
    uint row = groupId.y;

    if (drawobj_ix >= n_drawobj) return;

    VelloPath path = paths[drawobj_ix];
    int4 bbox = (int4)path.bbox;
    int stride = bbox.z - bbox.x;
    uint height = (uint)(bbox.w - bbox.y);

    if (row >= height || stride <= 0) return;
    if (localId.x >= (uint)stride) {
        sh_backdrop[localId.x] = 0;
    } else {
        uint ix = path.tiles + row * (uint)stride + localId.x;
        sh_backdrop[localId.x] = tiles[ix].backdrop;
    }

    // Inclusive prefix sum (log2(64) = 6 iterations)
    int backdrop = sh_backdrop[localId.x];
    [unroll]
    for (uint i = 0u; i < 6u; i++) {
        GroupMemoryBarrierWithGroupSync();
        if (localId.x >= (1u << i)) {
            backdrop += sh_backdrop[localId.x - (1u << i)];
        }
        GroupMemoryBarrierWithGroupSync();
        sh_backdrop[localId.x] = backdrop;
    }

    if (localId.x < (uint)stride) {
        uint ix = path.tiles + row * (uint)stride + localId.x;
        tiles[ix].backdrop = backdrop;
    }
}
