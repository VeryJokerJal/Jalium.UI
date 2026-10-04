// Vello GPU Pipeline V3 — clip_reduce
// Port of vello 0.10.0 shader/clip_reduce.wgsl.
// Reverse scan of the bicyclic semigroup for clip stack matching; emits per-
// workgroup Bic reductions and in-workgroup-matched clip elements.
//
// Bindings: t0 clip_inp | t1 path_bboxes | u0 reduced (Bic) | u1 clip_out (ClipEl)
// Dispatch: ((n_clip - 1) / 256, 1, 1) — skipped when zero
//
// NOTE: no config cbuffer, matching upstream.

#include "vello_shared.hlsli"

StructuredBuffer<ClipInp> clip_inp : register(t0);
StructuredBuffer<PathBbox> path_bboxes : register(t1);
RWStructuredBuffer<Bic> reduced : register(u0);
RWStructuredBuffer<ClipEl> clip_out : register(u1);

#define WG_SIZE 256u
#define LG_WG_SIZE 8u

groupshared Bic sh_bic[WG_SIZE];
groupshared uint sh_parent[WG_SIZE];
groupshared uint sh_path_ix[WG_SIZE];

[numthreads(256, 1, 1)]
void main(uint3 global_id : SV_DispatchThreadID, uint3 local_id : SV_GroupThreadID,
          uint3 wg_id : SV_GroupID)
{
    int inp = clip_inp[global_id.x].path_ix;
    bool is_push = inp >= 0;
    Bic bic;
    bic.a = is_push ? 0u : 1u;
    bic.b = is_push ? 1u : 0u;
    // reverse scan of bicyclic semigroup
    sh_bic[local_id.x] = bic;
    for (uint i = 0u; i < LG_WG_SIZE; i += 1u) {
        GroupMemoryBarrierWithGroupSync();
        if (local_id.x + (1u << i) < WG_SIZE) {
            Bic other = sh_bic[local_id.x + (1u << i)];
            bic = bic_combine(bic, other);
        }
        GroupMemoryBarrierWithGroupSync();
        sh_bic[local_id.x] = bic;
    }
    if (local_id.x == 0u) {
        reduced[wg_id.x] = bic;
    }
    GroupMemoryBarrierWithGroupSync();
    uint size = sh_bic[0].b;
    bic = (Bic)0;
    if (local_id.x + 1u < WG_SIZE) {
        bic = sh_bic[local_id.x + 1u];
    }
    if (is_push && bic.a == 0u) {
        uint local_ix = size - bic.b - 1u;
        sh_parent[local_ix] = local_id.x;
        sh_path_ix[local_ix] = uint(inp);
    }
    GroupMemoryBarrierWithGroupSync();
    // TODO: possibly do forward scan here if depth can exceed wg size
    if (local_id.x < size) {
        uint path_ix = sh_path_ix[local_id.x];
        PathBbox pb = path_bboxes[path_ix];
        uint parent_ix = sh_parent[local_id.x] + wg_id.x * WG_SIZE;
        ClipEl el;
        el.parent_ix = parent_ix;
        el.bbox = float4(float(pb.x0), float(pb.y0), float(pb.x1), float(pb.y1));
        clip_out[global_id.x] = el;
    }
}
