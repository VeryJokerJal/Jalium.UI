// Vello GPU Pipeline V2 — clip_reduce
// Hierarchical reduction of clip begin/end pairs using BIC (Binary Inclusive Count).
// Matches Vello's clip_reduce.wgsl: performs stack-based matching within each workgroup
// and outputs per-workgroup BIC aggregates + matched ClipEl parent links.
//
// Dispatch: max((n_clip - 1) / 256, 0), 1, 1
// (only dispatched if n_clip > 256; otherwise clip_leaf handles everything)

#include "vello_shared.hlsli"

// SRV inputs
StructuredBuffer<ClipInp> clip_inp : register(t0);
ByteAddressBuffer path_bboxes : register(t1);  // PathBbox (24 bytes each)

// UAV outputs
RWStructuredBuffer<Bic> reduced : register(u0);
RWStructuredBuffer<ClipEl> clip_out : register(u1);

groupshared Bic sh_bic[WG_SIZE];
groupshared uint sh_stack[WG_SIZE];
groupshared float4 sh_stack_bbox[WG_SIZE];

Bic bic_combine(Bic a, Bic b)
{
    uint m = min(a.b, b.a);
    Bic c;
    c.a = a.a + b.a - m;
    c.b = a.b + b.b - m;
    return c;
}

[numthreads(256, 1, 1)]
void main(uint3 global_id : SV_DispatchThreadID,
          uint3 local_id : SV_GroupThreadID,
          uint3 wg_id : SV_GroupID)
{
    uint ix = global_id.x;

    // Read clip input
    ClipInp inp = clip_inp[ix];
    bool is_push = inp.ix >= 0;
    Bic bic;
    bic.a = is_push ? 0u : 1u;
    bic.b = is_push ? 1u : 0u;
    sh_bic[local_id.x] = bic;

    // Forward reduction (right-to-left scan for BIC)
    [unroll]
    for (uint i = 0u; i < 8u; i++) {  // log2(256) = 8
        GroupMemoryBarrierWithGroupSync();
        if (local_id.x + (1u << i) < WG_SIZE) {
            Bic other = sh_bic[local_id.x + (1u << i)];
            bic = bic_combine(bic, other);
        }
        GroupMemoryBarrierWithGroupSync();
        sh_bic[local_id.x] = bic;
    }

    // Output per-workgroup BIC aggregate
    if (local_id.x == 0u) {
        reduced[wg_id.x] = bic;
    }

    GroupMemoryBarrierWithGroupSync();

    // Stack-based matching within workgroup (sequential, single thread)
    if (local_id.x == 0u) {
        uint stack_ptr = 0u;
        for (uint j = 0u; j < WG_SIZE; j++) {
            uint cur_ix = wg_id.x * WG_SIZE + j;
            ClipInp cur_inp = clip_inp[cur_ix];
            if (cur_inp.ix >= 0) {
                // Push: BeginClip
                sh_stack[stack_ptr] = j;
                int path_ix = cur_inp.path_ix;
                uint addr = (uint)path_ix * 24u;
                int pbx0 = asint(path_bboxes.Load(addr + 0u));
                int pby0 = asint(path_bboxes.Load(addr + 4u));
                int pbx1 = asint(path_bboxes.Load(addr + 8u));
                int pby1 = asint(path_bboxes.Load(addr + 12u));
                sh_stack_bbox[stack_ptr] = float4((float)pbx0, (float)pby0, (float)pbx1, (float)pby1);
                stack_ptr++;
            } else {
                // Pop: EndClip
                if (stack_ptr > 0u) {
                    stack_ptr--;
                    uint parent_local = sh_stack[stack_ptr];
                    ClipEl el;
                    el.parent_ix = (int)(wg_id.x * WG_SIZE + parent_local);
                    el.bbox = sh_stack_bbox[stack_ptr];
                    clip_out[cur_ix] = el;
                }
            }
        }
    }
}
