// Vello GPU Pipeline V3 — pathtag_reduce
// Port of vello 0.10.0 shader/pathtag_reduce.wgsl.
// Per-workgroup reduction of path tag monoids over the scene tag stream.
//
// Bindings: b0 config | t0 scene | u0 reduced
// Dispatch: (path_tag_wgs, 1, 1) where path_tag_wgs = align_up(n_pathtags, 1024) / 1024

#include "vello_shared.hlsli"

StructuredBuffer<uint> scene : register(t0);
RWStructuredBuffer<TagMonoid> reduced : register(u0);

#define LG_WG_SIZE 8u
#define WG_SIZE 256u

groupshared TagMonoid sh_scratch[WG_SIZE];

[numthreads(256, 1, 1)]
void main(uint3 global_id : SV_DispatchThreadID, uint3 local_id : SV_GroupThreadID)
{
    uint ix = global_id.x;
    uint tag_word = scene[pathtag_base + ix];
    TagMonoid agg = reduce_tag(tag_word);
    sh_scratch[local_id.x] = agg;
    for (uint i = 0u; i < LG_WG_SIZE; i += 1u) {
        GroupMemoryBarrierWithGroupSync();
        if (local_id.x + (1u << i) < WG_SIZE) {
            TagMonoid other = sh_scratch[local_id.x + (1u << i)];
            agg = combine_tag_monoid(agg, other);
        }
        GroupMemoryBarrierWithGroupSync();
        sh_scratch[local_id.x] = agg;
    }
    if (local_id.x == 0u) {
        reduced[ix >> LG_WG_SIZE] = agg;
    }
}
