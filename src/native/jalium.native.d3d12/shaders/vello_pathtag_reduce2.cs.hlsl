// Vello GPU Pipeline V3 — pathtag_reduce2
// Port of vello 0.10.0 shader/pathtag_reduce2.wgsl.
// Second-level reduction of the pathtag monoid for the large scan path.
// D3D12/Vulkan dispatch this only when the first reduction exceeds 256
// workgroups. The count is exactly ceil(reduced_size / 256), so no
// out-of-logical-range elements are ever read.
//
// Bindings: t0 reduced_in | u0 reduced
// Dispatch: (align_up(path_tag_wgs, 256) / 256, 1, 1)

#include "vello_shared.hlsli"

StructuredBuffer<TagMonoid> reduced_in : register(t0);
RWStructuredBuffer<TagMonoid> reduced : register(u0);

#define LG_WG_SIZE 8u
#define WG_SIZE 256u

groupshared TagMonoid sh_scratch[WG_SIZE];

[numthreads(256, 1, 1)]
void main(uint3 global_id : SV_DispatchThreadID, uint3 local_id : SV_GroupThreadID)
{
    uint ix = global_id.x;
    TagMonoid agg = reduced_in[ix];
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
