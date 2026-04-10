// Vello GPU Pipeline V2 — clip_leaf
// Computes final clip bounding boxes for each clip operation.
// For BeginClip: clip bbox = path bbox (intersected with parent if nested).
// For EndClip:   clip bbox = intersection of path bbox with parent's bbox from clip_reduce.
//
// Dispatch: ceil(n_clip / 256), 1, 1

#include "vello_shared.hlsli"

cbuffer VelloConfig : register(b0)
{
    uint width_in_tiles;
    uint height_in_tiles;
    uint target_width;
    uint target_height;
    uint base_color;
    uint n_drawobj;
    uint n_path;
    uint n_clip;
    uint bin_data_start;
    uint lines_size;
    uint binning_size;
    uint tiles_size;
    uint seg_counts_size;
    uint segments_size;
    uint blend_size;
    uint ptcl_size;
    uint num_segments;
    uint pad0, pad1, pad2, pad3, pad4, pad5, pad6;
};

// SRV inputs
StructuredBuffer<ClipInp> clip_inp : register(t0);
ByteAddressBuffer path_bboxes : register(t1);   // PathBbox (24 bytes each)
StructuredBuffer<Bic> bic_reduced : register(t2);
StructuredBuffer<ClipEl> clip_els : register(t3);

// UAV outputs
RWStructuredBuffer<DrawMonoid> draw_monoids : register(u0);
RWStructuredBuffer<float4> clip_bboxes : register(u1);

[numthreads(256, 1, 1)]
void main(uint3 global_id : SV_DispatchThreadID)
{
    uint ix = global_id.x;
    if (ix >= n_clip) return;

    ClipInp inp = clip_inp[ix];
    bool is_push = inp.ix >= 0;

    // Read path bbox from atomic bbox buffer
    uint path_ix = (uint)inp.path_ix;
    uint addr = path_ix * 24u;
    int pbx0 = asint(path_bboxes.Load(addr + 0u));
    int pby0 = asint(path_bboxes.Load(addr + 4u));
    int pbx1 = asint(path_bboxes.Load(addr + 8u));
    int pby1 = asint(path_bboxes.Load(addr + 12u));
    float4 my_bbox = float4((float)pbx0, (float)pby0, (float)pbx1, (float)pby1);

    if (is_push) {
        // BeginClip: clip bbox is just the path bbox
        clip_bboxes[ix] = my_bbox;
    } else {
        // EndClip: intersect path bbox with parent bbox from clip_reduce
        ClipEl el = clip_els[ix];
        float4 parent_bbox = el.bbox;
        float4 intersected = bbox_intersect(my_bbox, parent_bbox);
        clip_bboxes[ix] = intersected;
    }
}
