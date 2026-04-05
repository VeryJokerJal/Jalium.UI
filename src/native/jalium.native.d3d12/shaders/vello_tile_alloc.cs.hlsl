// Vello GPU Pipeline V2 — tile_alloc
// Allocates tile storage for each draw object using workgroup prefix sum.
// Zeroes allocated tiles.
//
// Dispatch: ceil(n_drawobj / 256), 1, 1

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

// draw_bboxes: intersected bounding boxes per draw object (float4: x0,y0,x1,y1 in pixels)
StructuredBuffer<float4> draw_bboxes : register(t0);

// DrawTag from CPU encoding — to check tag type
StructuredBuffer<DrawTag> drawTags : register(t1);

// BumpAllocators
RWByteAddressBuffer bump : register(u0);

// VelloPath output: bbox + tile offset per draw object
RWStructuredBuffer<VelloPath> paths : register(u1);

// VelloTile output: allocated and zeroed tiles
RWStructuredBuffer<VelloTile> tiles : register(u2);

groupshared uint sh_tile_count[WG_SIZE];
groupshared uint sh_tile_offset;
groupshared uint sh_previous_failed;

[numthreads(256, 1, 1)]
void main(uint3 globalId : SV_DispatchThreadID, uint3 localId : SV_GroupThreadID)
{
    // Check for prior stage failure
    if (localId.x == 0u) {
        uint failed = 0;
        bump.InterlockedOr(BUMP_FAILED, 0u, failed); // atomic load
        sh_previous_failed = (failed & (STAGE_BINNING | STAGE_FLATTEN)) != 0u ? 1u : 0u;
    }
    GroupMemoryBarrierWithGroupSync();
    if (sh_previous_failed != 0u) return;

    float SX = 1.0 / (float)TILE_WIDTH;
    float SY = 1.0 / (float)TILE_HEIGHT;

    uint drawobj_ix = globalId.x;
    bool valid = (drawobj_ix < n_drawobj);

    // Determine tile bbox for this draw object
    int x0i = 0, y0i = 0, x1i = 0, y1i = 0;
    if (valid) {
        DrawTag dt = drawTags[drawobj_ix];
        // EndClip (tag==2) doesn't need tiles
        if (dt.tag != 2u) {
            float4 bbox = draw_bboxes[drawobj_ix];
            if (bbox.x < bbox.z && bbox.y < bbox.w) {
                x0i = (int)floor(bbox.x * SX);
                y0i = (int)floor(bbox.y * SY);
                x1i = (int)ceil(bbox.z * SX);
                y1i = (int)ceil(bbox.w * SY);
            }
        }
    }

    uint ux0 = (uint)clamp(x0i, 0, (int)width_in_tiles);
    uint uy0 = (uint)clamp(y0i, 0, (int)height_in_tiles);
    uint ux1 = (uint)clamp(x1i, 0, (int)width_in_tiles);
    uint uy1 = (uint)clamp(y1i, 0, (int)height_in_tiles);
    uint tile_count = (ux1 - ux0) * (uy1 - uy0);

    // Inclusive prefix sum of tile counts within workgroup
    uint total = tile_count;
    sh_tile_count[localId.x] = tile_count;

    // log2(256) = 8 iterations
    [unroll]
    for (uint i = 0u; i < 8u; i++) {
        GroupMemoryBarrierWithGroupSync();
        if (localId.x >= (1u << i)) {
            total += sh_tile_count[localId.x - (1u << i)];
        }
        GroupMemoryBarrierWithGroupSync();
        sh_tile_count[localId.x] = total;
    }

    // Last thread in workgroup allocates tile storage
    if (localId.x == WG_SIZE - 1u) {
        uint count = sh_tile_count[WG_SIZE - 1u];
        uint offset;
        bump.InterlockedAdd(BUMP_TILE, count, offset);
        if (offset + count > tiles_size) {
            offset = 0u;
            uint dummy;
            bump.InterlockedOr(BUMP_FAILED, STAGE_TILE_ALLOC, dummy);
        }
        sh_tile_offset = offset;
    }
    GroupMemoryBarrierWithGroupSync();

    uint tile_offset = sh_tile_offset;

    // Write path structure
    if (valid) {
        uint tile_subix = (localId.x > 0u) ? sh_tile_count[localId.x - 1u] : 0u;
        VelloPath path;
        path.bbox = uint4(ux0, uy0, ux1, uy1);
        path.tiles = tile_offset + tile_subix;
        paths[drawobj_ix] = path;
    }

    // Zero allocated tiles (cooperative across workgroup)
    uint total_count = sh_tile_count[WG_SIZE - 1u];
    for (uint j = localId.x; j < total_count; j += WG_SIZE) {
        VelloTile t;
        t.backdrop = 0;
        t.segment_count_or_ix = 0u;
        tiles[tile_offset + j] = t;
    }
}
