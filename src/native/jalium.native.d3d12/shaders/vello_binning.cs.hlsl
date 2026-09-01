// Vello GPU Pipeline V3 — binning
// Port of vello 0.10.0 shader/binning.wgsl (includes the 0.10.0 fix for scenes
// whose binning requires more than 256 bins, vello #1700: block-relative
// shared-memory indexing + aligned bin_header stride).
//
// Bindings: b0 config | t0 draw_monoids | t1 path_bbox_buf | t2 clip_bbox_buf |
//           u0 intersected_bbox | u1 bump | u2 bin_data | u3 bin_header
// Dispatch: (ceil(n_drawobj / 256), 1, 1)

#include "vello_shared.hlsli"

StructuredBuffer<DrawMonoid> draw_monoids : register(t0);
StructuredBuffer<PathBbox> path_bbox_buf : register(t1);
StructuredBuffer<float4> clip_bbox_buf : register(t2);

RWStructuredBuffer<float4> intersected_bbox : register(u0);
RWByteAddressBuffer bump : register(u1);
RWStructuredBuffer<uint> bin_data : register(u2);

struct BinHeader
{
    uint element_count;
    uint chunk_offset;
};

RWStructuredBuffer<BinHeader> bin_header : register(u3);

// conversion factors from coordinates to bin
#define SX (1.0 / (float)(N_TILE_X * TILE_WIDTH))
#define SY (1.0 / (float)(N_TILE_Y * TILE_HEIGHT))

#define WG_SIZE 256u
#define N_SLICE 8u
#define N_SUBSLICE 4u

groupshared uint sh_bitmaps[N_SLICE][N_TILE];
// store count values packed two u16's to a u32
groupshared uint sh_count[N_SUBSLICE][N_TILE];
groupshared uint sh_chunk_offset[N_TILE];
groupshared uint sh_previous_failed;

[numthreads(256, 1, 1)]
void main(uint3 global_id : SV_DispatchThreadID, uint3 local_id : SV_GroupThreadID,
          uint3 wg_id : SV_GroupID)
{
    for (uint iz = 0u; iz < N_SLICE; iz += 1u) {
        sh_bitmaps[iz][local_id.x] = 0u;
    }
    if (local_id.x == 0u) {
        bool prior_failed = bump.Load(BUMP_LINES) > lines_size;
        sh_previous_failed = prior_failed ? 1u : 0u;
    }
    // also functions as barrier to protect zeroing of bitmaps
    GroupMemoryBarrierWithGroupSync();
    uint failed = sh_previous_failed;
    GroupMemoryBarrierWithGroupSync();
    if (failed != 0u) {
        if (global_id.x == 0u) {
            uint dummy;
            bump.InterlockedOr(BUMP_FAILED, STAGE_FLATTEN, dummy);
        }
        return;
    }

    // Read inputs and determine coverage of bins for the draw object global_id.x
    uint element_ix = global_id.x;
    int x0 = 0;
    int y0 = 0;
    int x1 = 0;
    int y1 = 0;
    if (element_ix < n_drawobj) {
        DrawMonoid draw_monoid = draw_monoids[element_ix];
        float4 clip_bbox = float4(-1e9, -1e9, 1e9, 1e9);
        if (draw_monoid.clip_ix > 0u) {
            // `clip_ix` should always be valid as long as the monoids are
            // correct; the bounds clamp is kept for defensive correctness.
            clip_bbox = clip_bbox_buf[min(draw_monoid.clip_ix - 1u, n_clip - 1u)];
        }
        // For clip elements, clip_box is the bbox of the clip path,
        // intersected with enclosing clips.
        // For other elements, it is the bbox of the enclosing clips.

        PathBbox path_bbox = path_bbox_buf[draw_monoid.path_ix];
        float4 pb = float4((float)path_bbox.x0, (float)path_bbox.y0,
                           (float)path_bbox.x1, (float)path_bbox.y1);
        float4 bbox = bbox_intersect(clip_bbox, pb);

        intersected_bbox[element_ix] = bbox;

        // A zero or negative-area intersection leaves the coordinates at 0 so
        // the path is clipped out and never assigned to a bin.
        if (bbox.x < bbox.z && bbox.y < bbox.w) {
            x0 = (int)floor(bbox.x * SX);
            y0 = (int)floor(bbox.y * SY);
            x1 = (int)ceil(bbox.z * SX);
            y1 = (int)ceil(bbox.w * SY);
        }
    }
    int width_in_bins = (int)((width_in_tiles + N_TILE_X - 1u) / N_TILE_X);
    int height_in_bins = (int)((height_in_tiles + N_TILE_Y - 1u) / N_TILE_Y);
    uint n_bins = (uint)(width_in_bins * height_in_bins);
    uint aligned_n_bins = (n_bins + N_TILE - 1u) & ~(N_TILE - 1u);

    x0 = clamp(x0, 0, width_in_bins);
    y0 = clamp(y0, 0, height_in_bins);
    x1 = clamp(x1, 0, width_in_bins);
    y1 = clamp(y1, 0, height_in_bins);
    if (x0 == x1) {
        y1 = y0;
    }

    int y0_width = y0 * width_in_bins;
    int y1_width = y1 * width_in_bins;

    uint my_slice = local_id.x / 32u;
    uint my_mask = 1u << (local_id.x & 31u);

    // Loop over the blocks of bins in chunks of N_TILE (256)
    uint next_block = N_TILE;
    [loop]
    for (uint block_start = 0u; block_start < n_bins; ) {
        // Write coverage for the current block
        for (int y_offset = y0_width; y_offset < y1_width; y_offset += width_in_bins) {
            uint start_bin = max((uint)(y_offset + x0), block_start);
            uint end_bin = min((uint)(y_offset + x1), next_block);
            for (uint bin_ix = start_bin; bin_ix < end_bin; bin_ix += 1u) {
                uint sh_bin_ix = bin_ix - block_start;
                InterlockedOr(sh_bitmaps[my_slice][sh_bin_ix], my_mask);
            }
        }
        GroupMemoryBarrierWithGroupSync();

        // Accumulate counts and allocate output segment for the bins in this block
        uint cur_bin_ix = block_start + local_id.x;
        uint element_count = 0u;
        for (uint i = 0u; i < N_SUBSLICE; i += 1u) {
            element_count += countbits(sh_bitmaps[i * 2u][local_id.x]);
            uint element_count_lo = element_count;
            element_count += countbits(sh_bitmaps[i * 2u + 1u][local_id.x]);
            uint element_count_hi = element_count;
            uint element_count_packed = element_count_lo | (element_count_hi << 16u);
            sh_count[i][local_id.x] = element_count_packed;
        }

        // element_count is the number of draw objects covering this thread's bin
        uint chunk_offset;
        bump.InterlockedAdd(BUMP_BINNING, element_count, chunk_offset);
        if (chunk_offset + element_count > binning_size) {
            chunk_offset = 0u;
            uint dummy;
            bump.InterlockedOr(BUMP_FAILED, STAGE_BINNING, dummy);
        }
        sh_chunk_offset[local_id.x] = chunk_offset;

        uint header_ix = wg_id.x * aligned_n_bins + cur_bin_ix;
        bin_header[header_ix].element_count = element_count;
        bin_header[header_ix].chunk_offset = chunk_offset;
        GroupMemoryBarrierWithGroupSync();

        // Output the element indices to bin_data
        for (int y_offset2 = y0_width; y_offset2 < y1_width; y_offset2 += width_in_bins) {
            uint start_bin = max((uint)(y_offset2 + x0), block_start);
            uint end_bin = min((uint)(y_offset2 + x1), next_block);
            for (uint bin_ix = start_bin; bin_ix < end_bin; bin_ix += 1u) {
                uint sh_bin_ix = bin_ix - block_start;
                uint out_mask = sh_bitmaps[my_slice][sh_bin_ix];
                if ((out_mask & my_mask) != 0u) {
                    uint idx = countbits(out_mask & (my_mask - 1u));
                    if (my_slice > 0u) {
                        uint count_ix = my_slice - 1u;
                        uint count_packed = sh_count[count_ix / 2u][sh_bin_ix];
                        idx += (count_packed >> (16u * (count_ix & 1u))) & 0xffffu;
                    }
                    uint offset = bin_data_start + sh_chunk_offset[sh_bin_ix];
                    bin_data[offset + idx] = element_ix;
                }
            }
        }

        block_start = next_block;
        if (next_block < aligned_n_bins) {
            GroupMemoryBarrierWithGroupSync();
            for (uint i2 = 0u; i2 < N_SLICE; i2 += 1u) {
                sh_bitmaps[i2][local_id.x] = 0u;
            }
            GroupMemoryBarrierWithGroupSync();
            next_block += N_TILE;
        }
    }
}
