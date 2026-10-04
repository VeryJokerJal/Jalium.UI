// Vello GPU Pipeline V3 — tile_alloc
// Port of vello 0.10.0 shader/tile_alloc.wgsl.
// Allocates (and zeroes) the tile rectangle for each draw object.
//
// Bindings: b0 config | t0 scene | t1 draw_bboxes | u0 bump | u1 paths | u2 tiles
// Dispatch: (ceil(n_path / 256), 1, 1)
//
// NOTE: paths[] must be allocated with align_up(n_paths, 256) elements — lane
// 255 writes paths[drawobj_ix].tiles and every lane reads
// paths[drawobj_ix | 255].tiles, both of which can index past n_drawobj in the
// last workgroup.

#include "vello_shared.hlsli"

StructuredBuffer<uint> scene : register(t0);
StructuredBuffer<float4> draw_bboxes : register(t1);

RWByteAddressBuffer bump : register(u0);
RWStructuredBuffer<VelloPath> paths : register(u1);
RWStructuredBuffer<VelloTile> tiles : register(u2);

#define WG_SIZE 256u
#define LG_WG_SIZE 8u

groupshared uint sh_tile_count[WG_SIZE];
groupshared uint sh_previous_failed;

[numthreads(256, 1, 1)]
void main(uint3 global_id : SV_DispatchThreadID, uint3 local_id : SV_GroupThreadID)
{
    // Exit early if prior stages failed, as we can't run this stage.
    // We need to check only prior stages, as if this stage has failed in
    // another workgroup, we still want to know this workgroup's memory
    // requirement.
    if (local_id.x == 0u) {
        bool prior_failed = (bump.Load(BUMP_FAILED) & (STAGE_BINNING | STAGE_FLATTEN)) != 0u;
        sh_previous_failed = prior_failed ? 1u : 0u;
    }
    GroupMemoryBarrierWithGroupSync();
    uint failed = sh_previous_failed;
    GroupMemoryBarrierWithGroupSync();
    if (failed != 0u) {
        return;
    }
    // scale factors useful for converting coordinates to tiles
    const float SX = 1.0 / (float)TILE_WIDTH;
    const float SY = 1.0 / (float)TILE_HEIGHT;

    uint drawobj_ix = global_id.x;
    uint drawtag = DRAWTAG_NOP;
    if (drawobj_ix < n_drawobj) {
        drawtag = scene[drawtag_base + drawobj_ix];
    }
    int x0 = 0;
    int y0 = 0;
    int x1 = 0;
    int y1 = 0;
    if (drawtag != DRAWTAG_NOP && drawtag != DRAWTAG_END_CLIP) {
        float4 bbox = draw_bboxes[drawobj_ix];

        // Don't round up the bottom-right corner of the bbox if the area is
        // zero and leave the coordinates at 0. This will make `tile_count`
        // zero as the shape is clipped out.
        if (bbox.x < bbox.z && bbox.y < bbox.w) {
            x0 = (int)floor(bbox.x * SX);
            y0 = (int)floor(bbox.y * SY);
            x1 = (int)ceil(bbox.z * SX);
            y1 = (int)ceil(bbox.w * SY);
        }
    }
    uint ux0 = (uint)clamp(x0, 0, (int)width_in_tiles);
    uint uy0 = (uint)clamp(y0, 0, (int)height_in_tiles);
    uint ux1 = (uint)clamp(x1, 0, (int)width_in_tiles);
    uint uy1 = (uint)clamp(y1, 0, (int)height_in_tiles);
    uint tile_count = (ux1 - ux0) * (uy1 - uy0);
    uint total_tile_count = tile_count;
    sh_tile_count[local_id.x] = tile_count;
    for (uint i = 0u; i < LG_WG_SIZE; i += 1u) {
        GroupMemoryBarrierWithGroupSync();
        if (local_id.x >= (1u << i)) {
            total_tile_count += sh_tile_count[local_id.x - (1u << i)];
        }
        GroupMemoryBarrierWithGroupSync();
        sh_tile_count[local_id.x] = total_tile_count;
    }
    if (local_id.x == WG_SIZE - 1u) {
        uint count = sh_tile_count[WG_SIZE - 1u];
        uint offset;
        bump.InterlockedAdd(BUMP_TILE, count, offset);
        if (offset + count > tiles_size) {
            offset = 0u;
            uint dummy;
            bump.InterlockedOr(BUMP_FAILED, STAGE_TILE_ALLOC, dummy);
        }
        paths[drawobj_ix].tiles = offset;
    }
    // Using storage barriers is a workaround for what appears to be a
    // miscompilation when a normal workgroup-shared variable is used to
    // broadcast the value.
    DeviceMemoryBarrierWithGroupSync();
    uint tile_offset = paths[drawobj_ix | (WG_SIZE - 1u)].tiles;
    DeviceMemoryBarrierWithGroupSync();
    if (drawobj_ix < n_drawobj) {
        uint tile_subix = (local_id.x > 0u) ? sh_tile_count[local_id.x - 1u] : 0u;
        VelloPath path;
        path.bbox = uint4(ux0, uy0, ux1, uy1);
        path.tiles = tile_offset + tile_subix;
        paths[drawobj_ix] = path;
    }

    // zero allocated memory
    // Note: if the number of draw objects is small, utilization will be poor.
    uint total_count = sh_tile_count[WG_SIZE - 1u];
    for (uint j = local_id.x; j < total_count; j += WG_SIZE) {
        VelloTile t;
        t.backdrop = 0;
        t.segment_count_or_ix = 0u;
        tiles[tile_offset + j] = t;
    }
}
