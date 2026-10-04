// Vello GPU Pipeline V3 — draw_reduce
// Port of vello 0.10.0 shader/draw_reduce.wgsl.
// Per-workgroup reduction of draw monoids over the scene drawtag stream.
//
// Bindings: b0 config | t0 scene | u0 reduced
// Dispatch: (min(ceil(n_drawobj / 256), 256), 1, 1)

#include "vello_shared.hlsli"

StructuredBuffer<uint> scene : register(t0);
RWStructuredBuffer<DrawMonoid> reduced : register(u0);

#define WG_SIZE 256u
#define LG_WG_SIZE 8u

groupshared DrawMonoid sh_scratch[WG_SIZE];

uint read_draw_tag_from_scene(uint ix)
{
    uint tag_word;
    if (ix < n_drawobj) {
        uint tag_ix = drawtag_base + ix;
        tag_word = scene[tag_ix];
    } else {
        tag_word = DRAWTAG_NOP;
    }
    return tag_word;
}

[numthreads(256, 1, 1)]
void main(uint3 local_id : SV_GroupThreadID, uint3 wg_id : SV_GroupID)
{
    uint num_blocks_total = (n_drawobj + (WG_SIZE - 1u)) / WG_SIZE;
    // When the number of blocks exceeds the workgroup size, divide
    // the work evenly so each workgroup handles n_blocks / wg, with
    // the low workgroups doing one more each to handle the remainder.
    uint n_blocks_base = num_blocks_total / WG_SIZE;
    uint remainder = num_blocks_total % WG_SIZE;
    uint first_block = n_blocks_base * wg_id.x + min(wg_id.x, remainder);
    uint n_blocks = n_blocks_base + (wg_id.x < remainder ? 1u : 0u);
    uint block_index = first_block * WG_SIZE + local_id.x;
    DrawMonoid agg = draw_monoid_identity();
    [loop]
    for (uint i = 0u; i < n_blocks; i++) {
        uint tag_word = read_draw_tag_from_scene(block_index);
        agg = combine_draw_monoid(agg, map_draw_tag(tag_word));
        block_index += WG_SIZE;
    }
    sh_scratch[local_id.x] = agg;
    for (uint j = 0u; j < LG_WG_SIZE; j += 1u) {
        GroupMemoryBarrierWithGroupSync();
        if (local_id.x + (1u << j) < WG_SIZE) {
            DrawMonoid other = sh_scratch[local_id.x + (1u << j)];
            agg = combine_draw_monoid(agg, other);
        }
        GroupMemoryBarrierWithGroupSync();
        sh_scratch[local_id.x] = agg;
    }
    if (local_id.x == 0u) {
        reduced[wg_id.x] = agg;
    }
}
