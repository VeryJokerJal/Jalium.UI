// Vello GPU Pipeline V2 — bbox_clear
// Clears atomic path bounding boxes to initial values before flatten.
// Each PathBbox is 24 bytes (6 x int32): {x0=INT_MAX, y0=INT_MAX, x1=INT_MIN, y1=INT_MIN, draw_flags, trans_ix}
//
// Dispatch: ceil(n_path / 256), 1, 1

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

// PathBbox stored as raw buffer: 6 x int32 per path = 24 bytes
// Offsets: x0=0, y0=4, x1=8, y1=12, draw_flags=16, trans_ix=20
RWByteAddressBuffer path_bboxes : register(u0);

// PathInfo from CPU encoding — to extract fill rule
StructuredBuffer<PathInfo> pathInfos : register(t0);

[numthreads(256, 1, 1)]
void main(uint3 gid : SV_DispatchThreadID)
{
    uint ix = gid.x;
    if (ix >= n_path) return;

    uint addr = ix * 24;

    // Set bbox to empty: x0=INT_MAX, y0=INT_MAX, x1=INT_MIN, y1=INT_MIN
    // Flatten's atomic InterlockedMin/Max will compute actual bounds from segments.
    path_bboxes.Store(addr +  0, asuint(0x7FFFFFFF));  // x0 = INT_MAX
    path_bboxes.Store(addr +  4, asuint(0x7FFFFFFF));  // y0 = INT_MAX
    path_bboxes.Store(addr +  8, asuint(0x80000000));  // x1 = INT_MIN
    path_bboxes.Store(addr + 12, asuint(0x80000000));  // y1 = INT_MIN

    // draw_flags: encode fill rule from PathInfo
    uint fillRule = pathInfos[ix].fillRule;
    uint draw_flags = (fillRule == FILL_EVEN_ODD) ? DRAW_INFO_FLAGS_FILL_RULE_BIT : 0u;
    path_bboxes.Store(addr + 16, draw_flags);
    path_bboxes.Store(addr + 20, 0u);  // trans_ix (unused)
}
