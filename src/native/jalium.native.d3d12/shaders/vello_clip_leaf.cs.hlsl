// Vello GPU Pipeline V3 — clip_leaf
// Port of vello 0.10.0 shader/clip_leaf.wgsl (includes the 0.9.0 fix for
// invalid shared-memory reads on inactive lanes, vello #1637).
// Computes per-clip bounding boxes via stack monoid evaluation, and fixes up
// the EndClip draw monoids to match their BeginClip.
//
// Bindings: b0 config | t0 clip_inp | t1 path_bboxes | t2 reduced (Bic) |
//           t3 clip_els | u0 draw_monoids | u1 clip_bboxes (float4)
// Dispatch: (ceil(n_clip / 256), 1, 1) — skipped when zero
//
// NOTE: draw_monoids and clip_bboxes here are the REAL GPU clip pipeline —
// the previous Jalium port replaced these two stages with a CPU stack replay;
// this port restores the upstream GPU path (whose EndClip parent-revert logic
// is correct, unlike the earlier hand-written HLSL).

#include "vello_shared.hlsli"

StructuredBuffer<ClipInp> clip_inp : register(t0);
StructuredBuffer<PathBbox> path_bboxes : register(t1);
StructuredBuffer<Bic> reduced : register(t2);
StructuredBuffer<ClipEl> clip_els : register(t3);
RWStructuredBuffer<DrawMonoid> draw_monoids : register(u0);
RWStructuredBuffer<float4> clip_bboxes : register(u1);

#define WG_SIZE 256u
#define LG_WG_SIZE 8u

groupshared Bic sh_bic[510];
groupshared uint sh_stack[WG_SIZE];
groupshared float4 sh_stack_bbox[WG_SIZE];
groupshared float4 sh_bbox[WG_SIZE];
groupshared int sh_link[WG_SIZE];

int search_link(inout Bic bic, uint ix_in)
{
    uint ix = ix_in;
    uint j = 0u;
    [loop]
    while (j < LG_WG_SIZE) {
        uint base = 2u * WG_SIZE - (2u << (LG_WG_SIZE - j));
        if (((ix >> j) & 1u) != 0u) {
            Bic test = bic_combine(sh_bic[base + (ix >> j) - 1u], bic);
            if (test.b > 0u) {
                break;
            }
            bic = test;
            ix -= 1u << j;
        }
        j += 1u;
    }
    if (ix > 0u) {
        [loop]
        while (j > 0u) {
            j -= 1u;
            uint base = 2u * WG_SIZE - (2u << (LG_WG_SIZE - j));
            Bic test = bic_combine(sh_bic[base + (ix >> j) - 1u], bic);
            if (test.b == 0u) {
                bic = test;
                ix -= 1u << j;
            }
        }
    }
    if (ix > 0u) {
        return int(ix) - 1;
    } else {
        return int(~0u - bic.a);
    }
}

int load_clip_path(uint ix)
{
    if (ix < n_clip) {
        return clip_inp[ix].path_ix;
    } else {
        return int(0x80000000);
    }
}

[numthreads(256, 1, 1)]
void main(uint3 global_id : SV_DispatchThreadID, uint3 local_id : SV_GroupThreadID,
          uint3 wg_id : SV_GroupID)
{
    Bic bic = (Bic)0;
    if (local_id.x < wg_id.x) {
        bic = reduced[local_id.x];
    }
    sh_bic[local_id.x] = bic;
    for (uint i0 = 0u; i0 < LG_WG_SIZE; i0 += 1u) {
        GroupMemoryBarrierWithGroupSync();
        if (local_id.x + (1u << i0) < WG_SIZE) {
            Bic other = sh_bic[local_id.x + (1u << i0)];
            bic = bic_combine(bic, other);
        }
        GroupMemoryBarrierWithGroupSync();
        sh_bic[local_id.x] = bic;
    }
    GroupMemoryBarrierWithGroupSync();
    uint stack_size = sh_bic[0].b;
    // TODO: if stack depth > WG_SIZE desired, scan here

    // binary search in stack
    uint sp = WG_SIZE - 1u - local_id.x;
    uint ix = 0u;
    for (uint i1 = 0u; i1 < LG_WG_SIZE; i1 += 1u) {
        uint probe = ix + ((WG_SIZE / 2u) >> i1);
        if (sp < sh_bic[probe].b) {
            ix = probe;
        }
    }
    uint b = sh_bic[ix].b;
    float4 bbox = float4(-1e9, -1e9, 1e9, 1e9);
    if (sp < b) {
        ClipEl el = clip_els[ix * WG_SIZE + b - sp - 1u];
        sh_stack[local_id.x] = el.parent_ix;
        bbox = el.bbox;
    }
    // forward scan of bbox values of prefix stack
    for (uint i2 = 0u; i2 < LG_WG_SIZE; i2 += 1u) {
        sh_stack_bbox[local_id.x] = bbox;
        GroupMemoryBarrierWithGroupSync();
        if (local_id.x >= (1u << i2)) {
            bbox = bbox_intersect(sh_stack_bbox[local_id.x - (1u << i2)], bbox);
        }
        GroupMemoryBarrierWithGroupSync();
    }
    sh_stack_bbox[local_id.x] = bbox;

    // Read input and compute Bic binary tree
    int inp = load_clip_path(global_id.x);
    bool is_push = inp >= 0;
    bic.a = is_push ? 0u : 1u;
    bic.b = is_push ? 1u : 0u;
    sh_bic[local_id.x] = bic;
    if (is_push) {
        PathBbox pb = path_bboxes[inp];
        bbox = float4(float(pb.x0), float(pb.y0), float(pb.x1), float(pb.y1));
    } else {
        bbox = float4(-1e9, -1e9, 1e9, 1e9);
    }
    uint inbase = 0u;
    for (uint i3 = 0u; i3 < LG_WG_SIZE - 1u; i3 += 1u) {
        uint outbase = 2u * WG_SIZE - (1u << (LG_WG_SIZE - i3));
        GroupMemoryBarrierWithGroupSync();
        if (local_id.x < (1u << (LG_WG_SIZE - 1u - i3))) {
            uint in_off = inbase + local_id.x * 2u;
            sh_bic[outbase + local_id.x] = bic_combine(sh_bic[in_off], sh_bic[in_off + 1u]);
        }
        inbase = outbase;
    }
    GroupMemoryBarrierWithGroupSync();
    // search for predecessor node
    bic = (Bic)0;
    int link = -1;
    if (global_id.x < n_clip) {
        link = search_link(bic, local_id.x);
    }
    sh_link[local_id.x] = link;
    GroupMemoryBarrierWithGroupSync();
    // Use explicit control flow rather than select here, as some backends may
    // still materialize the indexed operand and expose invalid inactive lanes.
    int grandparent = link - 1;
    if (link >= 0) {
        grandparent = sh_link[link];
    }
    int parent;
    if (link >= 0) {
        parent = int(wg_id.x * WG_SIZE) + link;
    } else if (link + int(stack_size) >= 0) {
        parent = int(sh_stack[int(WG_SIZE) + link]);
    } else {
        parent = -1;
    }
    // bbox scan (intersect) across parent links
    for (uint i4 = 0u; i4 < LG_WG_SIZE; i4 += 1u) {
        if (i4 != 0u) {
            sh_link[local_id.x] = link;
        }
        sh_bbox[local_id.x] = bbox;
        GroupMemoryBarrierWithGroupSync();
        if (link >= 0) {
            bbox = bbox_intersect(sh_bbox[link], bbox);
            link = sh_link[link];
        }
        GroupMemoryBarrierWithGroupSync();
    }
    if (link + int(stack_size) >= 0) {
        bbox = bbox_intersect(sh_stack_bbox[int(WG_SIZE) + link], bbox);
    }
    // At this point, bbox is the intersection of bboxes on the path to the root
    sh_bbox[local_id.x] = bbox;
    GroupMemoryBarrierWithGroupSync();

    if (!is_push && global_id.x < n_clip) {
        // Fix up drawmonoid so path_ix of EndClip matches BeginClip
        ClipInp parent_clip = clip_inp[parent];
        int path_ix = parent_clip.path_ix;
        uint parent_ix = parent_clip.ix;
        uint end_ix = ~uint(inp);
        draw_monoids[end_ix].path_ix = uint(path_ix);
        // Make EndClip point to the same draw data as BeginClip
        draw_monoids[end_ix].scene_offset = draw_monoids[parent_ix].scene_offset;
        // Make EndClip point to the same info (draw flags) as BeginClip
        draw_monoids[end_ix].info_offset = draw_monoids[parent_ix].info_offset;
        if (grandparent >= 0) {
            bbox = sh_bbox[grandparent];
        } else if (grandparent + int(stack_size) >= 0) {
            bbox = sh_stack_bbox[int(WG_SIZE) + grandparent];
        } else {
            bbox = float4(-1e9, -1e9, 1e9, 1e9);
        }
    }
    if (global_id.x < n_clip) {
        clip_bboxes[global_id.x] = bbox;
    }
}
