// Vello GPU Pipeline V2 — binning
// Bins draw objects into a grid of 16x16-tile bins using bitmap accumulation.
// Adapted from Vello's binning.wgsl for Jalium.UI's CPU encoding format.
//
// Dispatch: ceil(n_drawobj / 256), 1, 1

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

// DrawMonoid: CPU-computed prefix sum of draw objects
StructuredBuffer<DrawMonoid> draw_monoids : register(t0);

// PathBbox: atomic bboxes from flatten (24 bytes each)
ByteAddressBuffer path_bbox_buf : register(t1);

// Clip bboxes (float4 per clip, or empty if no clips)
StructuredBuffer<float4> clip_bbox_buf : register(t2);

// DrawTag from CPU encoding
StructuredBuffer<DrawTag> drawTags : register(t3);

RWByteAddressBuffer bump : register(u0);

// intersected_bbox: output clipped bounding boxes (float4 per draw)
RWStructuredBuffer<float4> intersected_bbox : register(u1);

// bin_data: output draw indices per bin
RWByteAddressBuffer bin_data : register(u2);

// bin_header: per-bin (element_count, chunk_offset)
RWStructuredBuffer<BinHeader> bin_header : register(u3);

// Conversion factors from pixel coordinates to bin coordinates
// SX = 1.0 / (N_TILE_X * TILE_WIDTH) = 1.0 / 256.0
// SY = 1.0 / (N_TILE_Y * TILE_HEIGHT) = 1.0 / 256.0
#define SX (1.0 / (float)(N_TILE_X * TILE_WIDTH))
#define SY (1.0 / (float)(N_TILE_Y * TILE_HEIGHT))

#define N_SLICE (WG_SIZE / 32u)  // 8
#define N_SUBSLICE 4u

// Shared memory bitmaps: sh_bitmaps[slice][bin_ix]
// 8 slices of 256 bins = 2048 uint32s
groupshared uint sh_bitmaps[N_SLICE][N_TILE];
// Packed count values: two u16 in a u32
groupshared uint sh_count[N_SUBSLICE][N_TILE];
groupshared uint sh_chunk_offset[N_TILE];
groupshared uint sh_previous_failed;

[numthreads(256, 1, 1)]
void main(uint3 globalId : SV_DispatchThreadID,
          uint3 localId : SV_GroupThreadID,
          uint3 wgId : SV_GroupID)
{
    // Zero bitmaps
    [unroll]
    for (uint i = 0; i < N_SLICE; i++) {
        sh_bitmaps[i][localId.x] = 0u;
    }

    if (localId.x == 0u) {
        uint n_lines;
        bump.InterlockedAdd(BUMP_LINES, 0u, n_lines);
        sh_previous_failed = (n_lines > lines_size) ? 1u : 0u;
    }
    GroupMemoryBarrierWithGroupSync();
    if (sh_previous_failed != 0u) {
        if (globalId.x == 0u) {
            uint dummy;
            bump.InterlockedOr(BUMP_FAILED, STAGE_FLATTEN, dummy);
        }
        return;
    }

    uint element_ix = globalId.x;
    int x0 = 0, y0 = 0, x1 = 0, y1 = 0;

    if (element_ix < n_drawobj) {
        DrawMonoid dm = draw_monoids[element_ix];
        DrawTag dt = drawTags[element_ix];

        // Compute clip bbox
        float4 clip_bbox = float4(-1e9, -1e9, 1e9, 1e9);
        if (dm.clip_ix > 0u && n_clip > 0u) {
            clip_bbox = clip_bbox_buf[min(dm.clip_ix - 1u, n_clip - 1u)];
        }

        // Read path bbox from atomic bbox buffer
        uint path_ix = dm.path_ix;
        uint addr = path_ix * 24;
        int pbx0 = asint(path_bbox_buf.Load(addr + 0));
        int pby0 = asint(path_bbox_buf.Load(addr + 4));
        int pbx1 = asint(path_bbox_buf.Load(addr + 8));
        int pby1 = asint(path_bbox_buf.Load(addr + 12));
        float4 pb = float4((float)pbx0, (float)pby0, (float)pbx1, (float)pby1);
        float4 bbox = bbox_intersect(clip_bbox, pb);

        intersected_bbox[element_ix] = bbox;

        if (bbox.x < bbox.z && bbox.y < bbox.w) {
            x0 = (int)floor(bbox.x * SX);
            y0 = (int)floor(bbox.y * SY);
            x1 = (int)ceil(bbox.z * SX);
            y1 = (int)ceil(bbox.w * SY);
        }
    }

    int width_in_bins = (int)((width_in_tiles + N_TILE_X - 1u) / N_TILE_X);
    int height_in_bins = (int)((height_in_tiles + N_TILE_Y - 1u) / N_TILE_Y);
    x0 = clamp(x0, 0, width_in_bins);
    y0 = clamp(y0, 0, height_in_bins);
    x1 = clamp(x1, 0, width_in_bins);
    y1 = clamp(y1, 0, height_in_bins);
    if (x0 == x1) y1 = y0;

    // Set bits in bitmaps for bins touched
    uint my_slice = localId.x / 32u;
    uint my_mask = 1u << (localId.x & 31u);
    int bx = x0, by = y0;
    while (by < y1) {
        uint bin_ix = (uint)(by * width_in_bins + bx);
        InterlockedOr(sh_bitmaps[my_slice][bin_ix], my_mask);
        bx++;
        if (bx == x1) {
            bx = x0;
            by++;
        }
    }

    GroupMemoryBarrierWithGroupSync();

    // Count elements per bin
    uint element_count = 0u;
    [unroll]
    for (uint si = 0u; si < N_SUBSLICE; si++) {
        element_count += countbits(sh_bitmaps[si * 2u][localId.x]);
        uint element_count_lo = element_count;
        element_count += countbits(sh_bitmaps[si * 2u + 1u][localId.x]);
        uint element_count_hi = element_count;
        sh_count[si][localId.x] = element_count_lo | (element_count_hi << 16u);
    }

    // Allocate output space
    uint chunk_offset;
    bump.InterlockedAdd(BUMP_BINNING, element_count, chunk_offset);
    if (chunk_offset + element_count > binning_size) {
        chunk_offset = 0u;
        uint dummy;
        bump.InterlockedOr(BUMP_FAILED, STAGE_BINNING, dummy);
    }
    sh_chunk_offset[localId.x] = chunk_offset;

    // Write bin header
    uint global_bin_ix = wgId.x * WG_SIZE + localId.x;
    BinHeader hdr;
    hdr.element_count = element_count;
    hdr.chunk_offset = chunk_offset;
    bin_header[global_bin_ix] = hdr;

    GroupMemoryBarrierWithGroupSync();

    // Write draw object indices to allocated bin_data slots
    bx = x0;
    by = y0;
    while (by < y1) {
        uint bin_ix = (uint)(by * width_in_bins + bx);
        uint out_mask = sh_bitmaps[my_slice][bin_ix];
        if ((out_mask & my_mask) != 0u) {
            uint idx = countbits(out_mask & (my_mask - 1u));
            if (my_slice > 0u) {
                uint count_ix = my_slice - 1u;
                uint count_packed = sh_count[count_ix / 2u][bin_ix];
                idx += (count_packed >> (16u * (count_ix & 1u))) & 0xFFFFu;
            }
            uint offset = bin_data_start + sh_chunk_offset[bin_ix];
            bin_data.Store((offset + idx) * 4, element_ix);
        }
        bx++;
        if (bx == x1) {
            bx = x0;
            by++;
        }
    }
}
