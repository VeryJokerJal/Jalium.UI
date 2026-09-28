// Vello GPU Pipeline V3 — pathtag_scan (small variant)
// Faithful port of vello 0.10.0 shader/pathtag_scan.wgsl with `small`
// enabled. When pathtag_reduce emits at most 256 monoids, this shader scans
// that first-level reduction in shared memory and removes the otherwise
// redundant pathtag_reduce2 + pathtag_scan1 dispatches.
//
// Bindings: b0 config | t0 scene | t1 reduced | u0 tag_monoids
// Dispatch: (path_tag_wgs, 1, 1), where path_tag_wgs <= 256

#include "vello_shared.hlsli"

StructuredBuffer<uint> scene : register(t0);
StructuredBuffer<TagMonoid> reduced : register(t1);
RWStructuredBuffer<TagMonoid> tag_monoids : register(u0);

#define LG_WG_SIZE 8u
#define WG_SIZE 256u

groupshared TagMonoid sh_parent[WG_SIZE];
groupshared TagMonoid sh_monoid[WG_SIZE];

[numthreads(256, 1, 1)]
void main(uint3 global_id : SV_DispatchThreadID, uint3 local_id : SV_GroupThreadID,
          uint3 wg_id : SV_GroupID)
{
    // The first workgroup has no parent prefix. In a single-workgroup scene
    // the host can omit pathtag_reduce entirely: no lane reads `reduced`.
    // Group ID is uniform, so skipping these barriers is safe for every lane.
    TagMonoid parent = tag_monoid_identity();
    if (wg_id.x != 0u) {
        TagMonoid agg = tag_monoid_identity();
        if (local_id.x < wg_id.x) {
            agg = reduced[local_id.x];
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
        GroupMemoryBarrierWithGroupSync();
        parent = sh_parent[0];
    }

    uint ix = global_id.x;
    uint tag_word = scene[pathtag_base + ix];
    TagMonoid agg_part = reduce_tag(tag_word);
    sh_monoid[local_id.x] = agg_part;
    for (uint j = 0u; j < LG_WG_SIZE; j += 1u) {
        GroupMemoryBarrierWithGroupSync();
        if (local_id.x >= (1u << j)) {
            TagMonoid other = sh_monoid[local_id.x - (1u << j)];
            agg_part = combine_tag_monoid(other, agg_part);
        }
        GroupMemoryBarrierWithGroupSync();
        sh_monoid[local_id.x] = agg_part;
    }
    GroupMemoryBarrierWithGroupSync();

    TagMonoid tm = parent;
    if (local_id.x > 0u) {
        tm = combine_tag_monoid(tm, sh_monoid[local_id.x - 1u]);
    }
    tag_monoids[ix] = tm;
}
