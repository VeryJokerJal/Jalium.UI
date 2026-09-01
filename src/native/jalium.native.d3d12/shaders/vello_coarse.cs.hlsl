// Vello GPU Pipeline V3 — coarse
// Port of vello 0.10.0 shader/coarse.wgsl.
// Builds per-tile command lists (PTCL) from binned draw objects.
// One workgroup (256 threads) per bin (16x16 tiles).
//
// PTCL layout: the first word of each tile's 64-word static block is the
// blend-spill base index (NOT a command); commands start at +1.
//
// Bindings: b0 config | t0 scene | t1 draw_monoids | t2 bin_headers |
//           t3 info_bin_data | t4 paths | u0 tiles | u1 bump | u2 ptcl
// Dispatch: (width_in_bins, height_in_bins, 1)

#include "vello_shared.hlsli"

StructuredBuffer<uint> scene : register(t0);
StructuredBuffer<DrawMonoid> draw_monoids : register(t1);

struct BinHeader
{
    uint element_count;
    uint chunk_offset;
};

StructuredBuffer<BinHeader> bin_headers : register(t2);
StructuredBuffer<uint> info_bin_data : register(t3);
StructuredBuffer<VelloPath> paths : register(t4);

RWStructuredBuffer<VelloTile> tiles : register(u0);
RWByteAddressBuffer bump : register(u1);
RWStructuredBuffer<uint> ptcl : register(u2);

// Much of this code assumes WG_SIZE == N_TILE. If these diverge, then
// a fair amount of fixup is needed.
#define WG_SIZE 256u
#define LG_WG_SIZE 8u
#define N_SLICE 8u

groupshared uint sh_bitmaps[N_SLICE][N_TILE];
groupshared uint sh_part_count[WG_SIZE];
groupshared uint sh_part_offsets[WG_SIZE];
groupshared uint sh_drawobj_ix[WG_SIZE];
groupshared uint sh_tile_stride[WG_SIZE];
groupshared uint sh_tile_width[WG_SIZE];
groupshared uint sh_tile_x0y0[WG_SIZE];
groupshared uint sh_tile_count[WG_SIZE];
groupshared uint sh_tile_base[WG_SIZE];

// helper state for writing ptcl
static uint cmd_offset;
static uint cmd_limit;

// Make sure there is space for a command of given size, plus a jump if needed
void alloc_cmd(uint size)
{
    if (cmd_offset + size >= cmd_limit) {
        // We might be able to save a little bit of computation here
        // by setting the initial value of the bump allocator.
        uint ptcl_dyn_start = width_in_tiles * height_in_tiles * PTCL_INITIAL_ALLOC;
        uint alloc_base;
        bump.InterlockedAdd(BUMP_PTCL, PTCL_INCREMENT, alloc_base);
        uint new_cmd = ptcl_dyn_start + alloc_base;
        if (new_cmd + PTCL_INCREMENT > ptcl_size) {
            // This sets us up for technical UB, as lots of threads will be
            // writing to the same locations. But I think it's fine, and
            // predicating the writes would probably slow things down.
            new_cmd = 0u;
            uint dummy;
            bump.InterlockedOr(BUMP_FAILED, STAGE_COARSE, dummy);
        }
        ptcl[cmd_offset] = CMD_JUMP;
        ptcl[cmd_offset + 1u] = new_cmd;
        cmd_offset = new_cmd;
        cmd_limit = cmd_offset + (PTCL_INCREMENT - PTCL_HEADROOM);
    }
}

void write_path(VelloTile tile_data, uint tile_ix, uint draw_flags)
{
    // We overload the "segments" field to store both count (written by
    // path_count stage) and segment allocation (used by path_tiling and fine).
    uint n_segs = tile_data.segment_count_or_ix;
    if (n_segs != 0u) {
        uint seg_ix;
        bump.InterlockedAdd(BUMP_SEGMENTS, n_segs, seg_ix);
        tiles[tile_ix].segment_count_or_ix = ~seg_ix;
        alloc_cmd(4u);
        ptcl[cmd_offset] = CMD_FILL;
        bool even_odd = (draw_flags & DRAW_INFO_FLAGS_FILL_RULE_BIT) != 0u;
        uint size_and_rule = (n_segs << 1u) | (even_odd ? 1u : 0u);
        ptcl[cmd_offset + 1u] = size_and_rule;
        ptcl[cmd_offset + 2u] = seg_ix;
        ptcl[cmd_offset + 3u] = (uint)tile_data.backdrop;
        cmd_offset += 4u;
    } else {
        alloc_cmd(1u);
        ptcl[cmd_offset] = CMD_SOLID;
        cmd_offset += 1u;
    }
}

void write_color(uint rgba_color)
{
    alloc_cmd(2u);
    ptcl[cmd_offset] = CMD_COLOR;
    ptcl[cmd_offset + 1u] = rgba_color;
    cmd_offset += 2u;
}

void write_grad(uint ty, uint index, uint info_offset)
{
    alloc_cmd(3u);
    ptcl[cmd_offset] = ty;
    ptcl[cmd_offset + 1u] = index;
    ptcl[cmd_offset + 2u] = info_offset;
    cmd_offset += 3u;
}

void write_image(uint info_offset)
{
    alloc_cmd(2u);
    ptcl[cmd_offset] = CMD_IMAGE;
    ptcl[cmd_offset + 1u] = info_offset;
    cmd_offset += 2u;
}

void write_begin_clip()
{
    alloc_cmd(1u);
    ptcl[cmd_offset] = CMD_BEGIN_CLIP;
    cmd_offset += 1u;
}

void write_end_clip(uint blend, float alpha)
{
    alloc_cmd(3u);
    ptcl[cmd_offset] = CMD_END_CLIP;
    ptcl[cmd_offset + 1u] = blend;
    ptcl[cmd_offset + 2u] = asuint(alpha);
    cmd_offset += 3u;
}

void write_blurred_rounded_rect(uint rgba_color, uint info_offset)
{
    alloc_cmd(3u);
    ptcl[cmd_offset] = CMD_BLUR_RECT;
    ptcl[cmd_offset + 1u] = info_offset;
    ptcl[cmd_offset + 2u] = rgba_color;
    cmd_offset += 3u;
}

[numthreads(256, 1, 1)]
void main(uint3 local_id : SV_GroupThreadID, uint3 wg_id : SV_GroupID)
{
    // Exit early if prior stages failed, as we can't run this stage.
    // We need to check only prior stages, as if this stage has failed in
    // another workgroup, we still want to know this workgroup's memory
    // requirement.
    if (local_id.x == 0u) {
        uint f = bump.Load(BUMP_FAILED) & (STAGE_BINNING | STAGE_TILE_ALLOC | STAGE_FLATTEN);
        if (bump.Load(BUMP_SEG_COUNTS) > seg_counts_size) {
            f |= STAGE_PATH_COUNT;
        }
        // Reuse sh_part_count to hold failed flag, shmem is tight
        sh_part_count[0] = f;
    }
    GroupMemoryBarrierWithGroupSync();
    uint failed = sh_part_count[0];
    GroupMemoryBarrierWithGroupSync();
    if (failed != 0u) {
        if (wg_id.x == 0u && wg_id.y == 0u && local_id.x == 0u) {
            // propagate PATH_COUNT failure to path_tiling_setup so it doesn't
            // need to bind config
            uint dummy;
            bump.InterlockedOr(BUMP_FAILED, failed, dummy);
        }
        return;
    }
    uint width_in_bins = (width_in_tiles + N_TILE_X - 1u) / N_TILE_X;
    uint bin_ix = width_in_bins * wg_id.y + wg_id.x;
    uint height_in_bins = (height_in_tiles + N_TILE_Y - 1u) / N_TILE_Y;
    uint aligned_n_bins = (width_in_bins * height_in_bins + N_TILE - 1u) & ~(N_TILE - 1u);
    uint n_partitions = (n_drawobj + N_TILE - 1u) / N_TILE;

    // Coordinates of the top left of this bin, in tiles.
    uint bin_tile_x = N_TILE_X * wg_id.x;
    uint bin_tile_y = N_TILE_Y * wg_id.y;

    uint tile_x = local_id.x % N_TILE_X;
    uint tile_y = local_id.x / N_TILE_X;
    uint this_tile_ix = (bin_tile_y + tile_y) * width_in_tiles + bin_tile_x + tile_x;
    cmd_offset = this_tile_ix * PTCL_INITIAL_ALLOC;
    cmd_limit = cmd_offset + (PTCL_INITIAL_ALLOC - PTCL_HEADROOM);

    // clip state
    uint clip_zero_depth = 0u;
    uint clip_depth = 0u;

    uint partition_ix = 0u;
    uint rd_ix = 0u;
    uint wr_ix = 0u;
    uint part_start_ix = 0u;
    uint ready_ix = 0u;

    // blend state
    uint render_blend_depth = 0u;
    uint max_blend_depth = 0u;

    uint blend_offset = cmd_offset;
    cmd_offset += 1u;

    [loop]
    for (uint outer_safety = 0u; outer_safety < 65536u; outer_safety++) {
        for (uint iz = 0u; iz < N_SLICE; iz += 1u) {
            sh_bitmaps[iz][local_id.x] = 0u;
        }

        [loop]
        for (uint inner_safety = 0u; inner_safety < 65536u; inner_safety++) {
            if (ready_ix == wr_ix && partition_ix < n_partitions) {
                part_start_ix = ready_ix;
                uint count = 0u;
                if (partition_ix + local_id.x < n_partitions) {
                    uint in_ix = (partition_ix + local_id.x) * aligned_n_bins + bin_ix;
                    BinHeader bh = bin_headers[in_ix];
                    count = bh.element_count;
                    sh_part_offsets[local_id.x] = bh.chunk_offset;
                }
                // prefix sum the element counts
                for (uint i = 0u; i < LG_WG_SIZE; i += 1u) {
                    sh_part_count[local_id.x] = count;
                    GroupMemoryBarrierWithGroupSync();
                    if (local_id.x >= (1u << i)) {
                        count += sh_part_count[local_id.x - (1u << i)];
                    }
                    GroupMemoryBarrierWithGroupSync();
                }
                sh_part_count[local_id.x] = part_start_ix + count;
                GroupMemoryBarrierWithGroupSync();
                ready_ix = sh_part_count[WG_SIZE - 1u];
                partition_ix += WG_SIZE;
            }
            // use binary search to find draw object to read
            uint ix = rd_ix + local_id.x;
            if (ix >= wr_ix && ix < ready_ix) {
                uint part_ix = 0u;
                for (uint i = 0u; i < LG_WG_SIZE; i += 1u) {
                    uint probe = part_ix + ((N_TILE / 2u) >> i);
                    if (ix >= sh_part_count[probe - 1u]) {
                        part_ix = probe;
                    }
                }
                ix -= (part_ix > 0u) ? sh_part_count[part_ix - 1u] : part_start_ix;
                uint offset = bin_data_start + sh_part_offsets[part_ix];
                sh_drawobj_ix[local_id.x] = info_bin_data[offset + ix];
            }
            wr_ix = min(rd_ix + N_TILE, ready_ix);
            if (wr_ix - rd_ix >= N_TILE || (wr_ix >= ready_ix && partition_ix >= n_partitions)) {
                break;
            }
            GroupMemoryBarrierWithGroupSync();
        }
        // At this point, sh_drawobj_ix[0.. wr_ix - rd_ix] contains merged
        // binning results.
        uint tag = DRAWTAG_NOP;
        uint drawobj_ix = 0u;
        if (local_id.x + rd_ix < wr_ix) {
            drawobj_ix = sh_drawobj_ix[local_id.x];
            tag = scene[drawtag_base + drawobj_ix];
        }

        uint tile_count = 0u;
        if (tag != DRAWTAG_NOP) {
            uint path_ix = draw_monoids[drawobj_ix].path_ix;
            VelloPath path = paths[path_ix];
            uint stride = path.bbox.z - path.bbox.x;
            sh_tile_stride[local_id.x] = stride;
            int dx = (int)path.bbox.x - (int)bin_tile_x;
            int dy = (int)path.bbox.y - (int)bin_tile_y;
            int x0 = clamp(dx, 0, (int)N_TILE_X);
            int y0 = clamp(dy, 0, (int)N_TILE_Y);
            int x1 = clamp((int)path.bbox.z - (int)bin_tile_x, 0, (int)N_TILE_X);
            int y1 = clamp((int)path.bbox.w - (int)bin_tile_y, 0, (int)N_TILE_Y);
            sh_tile_width[local_id.x] = (uint)(x1 - x0);
            sh_tile_x0y0[local_id.x] = (uint)x0 | ((uint)y0 << 16u);
            tile_count = (uint)(x1 - x0) * (uint)(y1 - y0);
            // base relative to bin
            uint base = path.tiles - (uint)(dy * (int)stride + dx);
            sh_tile_base[local_id.x] = base;
        }

        // Prefix sum of tile counts
        sh_tile_count[local_id.x] = tile_count;
        for (uint i2 = 0u; i2 < LG_WG_SIZE; i2 += 1u) {
            GroupMemoryBarrierWithGroupSync();
            if (local_id.x >= (1u << i2)) {
                tile_count += sh_tile_count[local_id.x - (1u << i2)];
            }
            GroupMemoryBarrierWithGroupSync();
            sh_tile_count[local_id.x] = tile_count;
        }
        GroupMemoryBarrierWithGroupSync();
        uint total_tile_count = sh_tile_count[N_TILE - 1u];
        // Parallel iteration over all tiles
        [loop]
        for (uint ix2 = local_id.x; ix2 < total_tile_count; ix2 += N_TILE) {
            // Binary search to find draw object which contains this tile
            uint el_ix = 0u;
            for (uint i = 0u; i < LG_WG_SIZE; i += 1u) {
                uint probe = el_ix + ((N_TILE / 2u) >> i);
                if (ix2 >= sh_tile_count[probe - 1u]) {
                    el_ix = probe;
                }
            }
            uint dobj_ix = sh_drawobj_ix[el_ix];
            uint dtag = scene[drawtag_base + dobj_ix];
            uint seq_ix = ix2 - ((el_ix > 0u) ? sh_tile_count[el_ix - 1u] : 0u);
            uint width = sh_tile_width[el_ix];
            uint x0y0 = sh_tile_x0y0[el_ix];
            uint x = (x0y0 & 0xffffu) + seq_ix % width;
            uint y = (x0y0 >> 16u) + seq_ix / width;
            uint tile_ix = sh_tile_base[el_ix] + sh_tile_stride[el_ix] * y + x;
            VelloTile tile_data = tiles[tile_ix];
            bool is_clip = (dtag & 1u) != 0u;
            bool is_blend = false;
            if (is_clip) {
                const uint BLEND_CLIP = (128u << 8u) | 3u;
                uint scene_offset = draw_monoids[dobj_ix].scene_offset;
                uint dd = drawdata_base + scene_offset;
                uint blend = scene[dd];
                is_blend = blend != BLEND_CLIP;
            }

            uint di = draw_monoids[dobj_ix].info_offset;
            uint draw_flags = info_bin_data[di];
            bool even_odd = (draw_flags & DRAW_INFO_FLAGS_FILL_RULE_BIT) != 0u;
            uint n_segs = tile_data.segment_count_or_ix;

            // If this draw object represents an even-odd fill and we know that
            // no line segment crosses this tile and then this draw object
            // should not contribute to the tile if its backdrop (i.e. the
            // winding number of its top-left corner) is even.
            int bd = even_odd ? (abs(tile_data.backdrop) & 1) : tile_data.backdrop;
            bool backdrop_clear = bd == 0;
            bool include_tile = n_segs != 0u || (backdrop_clear == is_clip) || is_blend;
            if (include_tile) {
                uint el_slice = el_ix / 32u;
                uint el_mask = 1u << (el_ix & 31u);
                InterlockedOr(sh_bitmaps[el_slice][y * N_TILE_X + x], el_mask);
            }
        }
        GroupMemoryBarrierWithGroupSync();
        // At this point bit drawobj % 32 is set in
        // sh_bitmaps[drawobj / 32][y * N_TILE_X + x] if drawobj touches
        // tile (x, y).

        // Write per-tile command list for this tile
        uint slice_ix = 0u;
        uint bitmap = sh_bitmaps[0u][local_id.x];
        [loop]
        for (;;) {
            if (bitmap == 0u) {
                slice_ix += 1u;
                // potential optimization: make iteration limit dynamic
                if (slice_ix == N_SLICE) {
                    break;
                }
                bitmap = sh_bitmaps[slice_ix][local_id.x];
                if (bitmap == 0u) {
                    continue;
                }
            }

            uint el_ix = slice_ix * 32u + firstbitlow(bitmap);
            uint dobj_ix = sh_drawobj_ix[el_ix];
            // clear LSB of bitmap, using bit magic
            bitmap &= bitmap - 1u;
            uint drawtag = scene[drawtag_base + dobj_ix];
            DrawMonoid dm = draw_monoids[dobj_ix];
            uint dd = drawdata_base + dm.scene_offset;
            uint di = dm.info_offset;
            uint draw_flags = info_bin_data[di];
            if (clip_zero_depth == 0u) {
                uint tile_ix = sh_tile_base[el_ix] + sh_tile_stride[el_ix] * tile_y + tile_x;
                VelloTile tile_data = tiles[tile_ix];
                switch (drawtag) {
                    case DRAWTAG_FILL_COLOR: {
                        write_path(tile_data, tile_ix, draw_flags);
                        uint rgba_color = scene[dd];
                        write_color(rgba_color);
                        break;
                    }
                    case DRAWTAG_BLURRED_ROUNDED_RECT: {
                        write_path(tile_data, tile_ix, draw_flags);
                        uint rgba_color = scene[dd];
                        uint info_offset = di + 1u;
                        write_blurred_rounded_rect(rgba_color, info_offset);
                        break;
                    }
                    case DRAWTAG_FILL_LIN_GRADIENT: {
                        write_path(tile_data, tile_ix, draw_flags);
                        uint index = scene[dd];
                        uint info_offset = di + 1u;
                        write_grad(CMD_LIN_GRAD, index, info_offset);
                        break;
                    }
                    case DRAWTAG_FILL_RAD_GRADIENT: {
                        write_path(tile_data, tile_ix, draw_flags);
                        uint index = scene[dd];
                        uint info_offset = di + 1u;
                        write_grad(CMD_RAD_GRAD, index, info_offset);
                        break;
                    }
                    case DRAWTAG_FILL_SWEEP_GRADIENT: {
                        write_path(tile_data, tile_ix, draw_flags);
                        uint index = scene[dd];
                        uint info_offset = di + 1u;
                        write_grad(CMD_SWEEP_GRAD, index, info_offset);
                        break;
                    }
                    case DRAWTAG_FILL_IMAGE: {
                        write_path(tile_data, tile_ix, draw_flags);
                        write_image(di + 1u);
                        break;
                    }
                    case DRAWTAG_BEGIN_CLIP: {
                        bool ceo = (draw_flags & DRAW_INFO_FLAGS_FILL_RULE_BIT) != 0u;
                        int cbd = ceo ? (abs(tile_data.backdrop) & 1) : tile_data.backdrop;
                        bool backdrop_clear = cbd == 0;
                        if (tile_data.segment_count_or_ix == 0u && backdrop_clear) {
                            clip_zero_depth = clip_depth + 1u;
                        } else {
                            write_begin_clip();
                            render_blend_depth += 1u;
                            max_blend_depth = max(max_blend_depth, render_blend_depth);
                        }
                        clip_depth += 1u;
                        break;
                    }
                    case DRAWTAG_END_CLIP: {
                        clip_depth -= 1u;
                        write_path(tile_data, tile_ix, draw_flags);
                        uint blend = scene[dd];
                        float alpha = asfloat(scene[dd + 1u]);
                        write_end_clip(blend, alpha);
                        render_blend_depth -= 1u;
                        break;
                    }
                    default: {
                        break;
                    }
                }
            } else {
                // In "clip zero" state, suppress all drawing
                switch (drawtag) {
                    case DRAWTAG_BEGIN_CLIP: {
                        clip_depth += 1u;
                        break;
                    }
                    case DRAWTAG_END_CLIP: {
                        if (clip_depth == clip_zero_depth) {
                            clip_zero_depth = 0u;
                        }
                        clip_depth -= 1u;
                        break;
                    }
                    default: {
                        break;
                    }
                }
            }
        }

        rd_ix += N_TILE;
        if (rd_ix >= ready_ix && partition_ix >= n_partitions) {
            break;
        }
        GroupMemoryBarrierWithGroupSync();
    }
    if (bin_tile_x + tile_x < width_in_tiles && bin_tile_y + tile_y < height_in_tiles) {
        ptcl[cmd_offset] = CMD_END;
        uint blend_ix = 0u;
        if (max_blend_depth > BLEND_STACK_SPLIT) {
            uint scratch_size = (max_blend_depth - BLEND_STACK_SPLIT) * TILE_WIDTH * TILE_HEIGHT;
            bump.InterlockedAdd(BUMP_BLEND, scratch_size, blend_ix);
            if (blend_ix + scratch_size > blend_size) {
                uint dummy;
                bump.InterlockedOr(BUMP_FAILED, STAGE_COARSE, dummy);
            }
        }
        ptcl[blend_offset] = blend_ix;
    }
}
