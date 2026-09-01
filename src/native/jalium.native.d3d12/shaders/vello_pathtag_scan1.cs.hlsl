// Vello GPU Pipeline V3 — pathtag_scan1
// Port of vello 0.10.0 shader/pathtag_scan1.wgsl.
// Scan of the first-level reduced tag monoids given two levels of reduction.
//
// Bindings: t0 reduced | t1 reduced2 | u0 tag_monoids (reduced_scan)
// Dispatch: (align_up(path_tag_wgs, 256) / 256, 1, 1)

#include "vello_shared.hlsli"

StructuredBuffer<TagMonoid> reduced : register(t0);
StructuredBuffer<TagMonoid> reduced2 : register(t1);
RWStructuredBuffer<TagMonoid> tag_monoids : register(u0);

#define LG_WG_SIZE 8u
#define WG_SIZE 256u

groupshared TagMonoid sh_parent[WG_SIZE];
groupshared TagMonoid sh_monoid[WG_SIZE];

[numthreads(256, 1, 1)]
void main(uint3 global_id : SV_DispatchThreadID, uint3 local_id : SV_GroupThreadID,
          uint3 wg_id : SV_GroupID)
{
    TagMonoid agg = tag_monoid_identity();
    if (local_id.x < wg_id.x) {
        agg = reduced2[local_id.x];
    }
    sh_parent[local_id.x] = agg;
    for (uint i = 0u; i < LG_WG_SIZE; i += 1u) {
        GroupMemoryBarrierWithGroupSync();
        if (local_id.x + (1u << i) < WG_SIZE) {
            TagMonoid other = sh_parent[local_id.x + (1u << i)];
            agg = combine_tag_monoid(agg, other);
        }
        GroupMemoryBarrierWithGroupSync();
        sh_parent[local_id.x] = agg;
    }

    uint ix = global_id.x;
    agg = reduced[ix];
    sh_monoid[local_id.x] = agg;
    for (uint j = 0u; j < LG_WG_SIZE; j += 1u) {
        GroupMemoryBarrierWithGroupSync();
        if (local_id.x >= (1u << j)) {
            TagMonoid other = sh_monoid[local_id.x - (1u << j)];
            agg = combine_tag_monoid(other, agg);
        }
        GroupMemoryBarrierWithGroupSync();
        sh_monoid[local_id.x] = agg;
    }
    GroupMemoryBarrierWithGroupSync();
    // prefix up to this workgroup
    TagMonoid tm = sh_parent[0];
    if (local_id.x > 0u) {
        tm = combine_tag_monoid(tm, sh_monoid[local_id.x - 1u]);
    }
    // exclusive prefix sum, granularity of 4 tag bytes * workgroup size
    tag_monoids[ix] = tm;
}
