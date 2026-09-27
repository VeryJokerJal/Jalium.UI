// Vello GPU Pipeline V3 — pathtag_scan (large variant)
// Port of vello 0.10.0 shader/pathtag_scan.wgsl WITHOUT the `small` define:
// Jalium always runs the large scan chain (reduce -> reduce2 -> scan1 -> scan),
// so the parent prefix comes from the reduced_scan buffer produced by scan1.
//
// Bindings: b0 config | t0 scene | t1 parent (reduced_scan) | u0 tag_monoids
// Dispatch: (path_tag_wgs, 1, 1)

#include "vello_shared.hlsli"

StructuredBuffer<uint> scene : register(t0);
StructuredBuffer<TagMonoid> parent : register(t1);
RWStructuredBuffer<TagMonoid> tag_monoids : register(u0);

#define LG_WG_SIZE 8u
#define WG_SIZE 256u

groupshared TagMonoid sh_monoid[WG_SIZE];

[numthreads(256, 1, 1)]
void main(uint3 global_id : SV_DispatchThreadID, uint3 local_id : SV_GroupThreadID,
          uint3 wg_id : SV_GroupID)
{
    uint ix = global_id.x;
    uint tag_word = scene[pathtag_base + ix];
    TagMonoid agg_part = reduce_tag(tag_word);
    sh_monoid[local_id.x] = agg_part;
    for (uint i = 0u; i < LG_WG_SIZE; i += 1u) {
        GroupMemoryBarrierWithGroupSync();
        if (local_id.x >= (1u << i)) {
            TagMonoid other = sh_monoid[local_id.x - (1u << i)];
            agg_part = combine_tag_monoid(other, agg_part);
        }
        GroupMemoryBarrierWithGroupSync();
        sh_monoid[local_id.x] = agg_part;
    }
    GroupMemoryBarrierWithGroupSync();
    // prefix up to this workgroup
    TagMonoid tm = parent[wg_id.x];
    if (local_id.x > 0u) {
        tm = combine_tag_monoid(tm, sh_monoid[local_id.x - 1u]);
    }
    // exclusive prefix sum, granularity of 4 tag bytes
    tag_monoids[ix] = tm;
}
