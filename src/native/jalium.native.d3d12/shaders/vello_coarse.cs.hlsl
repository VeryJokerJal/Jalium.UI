// Vello GPU Pipeline V2 — coarse
// Builds Per-Tile Command Lists (PTCL) from binned draw objects.
// One workgroup (256 threads) per bin (16x16 tiles).
// Adapted from Vello's coarse.wgsl for Jalium.UI's CPU encoding format.
//
// Dispatch: (width_in_bins, height_in_bins, 1)

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

// Draw tags and data from CPU encoding
StructuredBuffer<DrawTag>    drawTags    : register(t0);
StructuredBuffer<DrawMonoid> draw_monoids : register(t1);
StructuredBuffer<PathDraw>   pathDraws   : register(t2);

// Bin headers and data
StructuredBuffer<BinHeader>  bin_headers : register(t3);
ByteAddressBuffer            info_bin_data : register(t4); // bin_data as raw for indexed read

// Path and tile data
StructuredBuffer<VelloPath>  paths       : register(t5);
ByteAddressBuffer            path_bbox_buf : register(t6);  // PathBbox draw_flags (fill rule etc.)

// Tile data (read-write for segment allocation inversion)
RWStructuredBuffer<VelloTile> tiles      : register(u0);

RWByteAddressBuffer          bump        : register(u1);
RWByteAddressBuffer          ptcl_buf    : register(u2);

#define N_SLICE (WG_SIZE / 32u)

groupshared uint sh_bitmaps[N_SLICE][N_TILE];
groupshared uint sh_part_count[WG_SIZE];
groupshared uint sh_part_offsets[WG_SIZE];
groupshared uint sh_drawobj_ix[WG_SIZE];
groupshared uint sh_tile_stride[WG_SIZE];
groupshared uint sh_tile_width[WG_SIZE];
groupshared uint sh_tile_x0y0[WG_SIZE];
groupshared uint sh_tile_count[WG_SIZE];
groupshared uint sh_tile_base[WG_SIZE];

// Per-thread PTCL write state
static uint cmd_offset;
static uint cmd_limit;

void alloc_cmd(uint size)
{
    if (cmd_offset + size >= cmd_limit) {
        uint ptcl_dyn_start = width_in_tiles * height_in_tiles * PTCL_INITIAL_ALLOC;
        uint new_cmd;
        bump.InterlockedAdd(BUMP_PTCL, PTCL_INCREMENT, new_cmd);
        new_cmd += ptcl_dyn_start;
        if (new_cmd + PTCL_INCREMENT > ptcl_size) {
            new_cmd = 0u;
            uint dummy;
            bump.InterlockedOr(BUMP_FAILED, STAGE_COARSE, dummy);
        }
        ptcl_buf.Store(cmd_offset * 4, CMD_JUMP);
        ptcl_buf.Store((cmd_offset + 1u) * 4, new_cmd);
        cmd_offset = new_cmd;
        cmd_limit = cmd_offset + (PTCL_INCREMENT - PTCL_HEADROOM);
    }
}

void write_path(VelloTile tile_val, uint tile_ix, uint draw_flags)
{
    uint raw_segs = tile_val.segment_count_or_ix;

    // Detect already-processed tile: coarse writes ~seg_ix (high bits set) on first pass.
    // If we see a value with the top bit set, this tile was already processed by an earlier
    // draw object — use the stored seg_base directly instead of re-allocating.
    if (raw_segs >= 0x80000000u) {
        // Already processed: extract seg_base and original n_segs from stored ~seg_ix.
        // We can't recover n_segs, so emit CMD_FILL with the existing allocation.
        uint seg_ix = ~raw_segs;
        // Read the segment count that was already allocated — we stored it in the tile's
        // backdrop field? No, backdrop is separate. We need to re-read from bump.
        // Actually, we don't know n_segs anymore. Use 0 segments to emit CMD_SOLID.
        alloc_cmd(1u);
        ptcl_buf.Store(cmd_offset * 4, CMD_SOLID);
        cmd_offset += 1u;
        return;
    }

    uint n_segs = min(raw_segs, 32768u); // safety clamp
    if (n_segs != 0u) {
        uint seg_ix;
        bump.InterlockedAdd(BUMP_SEGMENTS, n_segs, seg_ix);

        // Overflow check: if seg_ix exceeds buffer capacity, flag failure and bail
        if (seg_ix + n_segs > segments_size) {
            uint dummy;
            bump.InterlockedOr(BUMP_FAILED, STAGE_COARSE, dummy);
            // Still write ~seg_ix so path_tiling can detect the overflow
            tiles[tile_ix].segment_count_or_ix = ~seg_ix;
            return;
        }

        tiles[tile_ix].segment_count_or_ix = ~seg_ix;

        alloc_cmd(4u);
        bool even_odd = (draw_flags & DRAW_INFO_FLAGS_FILL_RULE_BIT) != 0u;
        uint size_and_rule = (n_segs << 1u) | (uint)even_odd;
        ptcl_buf.Store(cmd_offset * 4, CMD_FILL);
        ptcl_buf.Store((cmd_offset + 1u) * 4, size_and_rule);
        ptcl_buf.Store((cmd_offset + 2u) * 4, seg_ix);
        ptcl_buf.Store((cmd_offset + 3u) * 4, asuint(tile_val.backdrop));
        cmd_offset += 4u;
    } else {
        alloc_cmd(1u);
        ptcl_buf.Store(cmd_offset * 4, CMD_SOLID);
        cmd_offset += 1u;
    }
}

void write_color(uint rgba_color)
{
    alloc_cmd(2u);
    ptcl_buf.Store(cmd_offset * 4, CMD_COLOR);
    ptcl_buf.Store((cmd_offset + 1u) * 4, rgba_color);
    cmd_offset += 2u;
}

void write_grad(uint ty, uint gradIndex, PathDraw pd)
{
    alloc_cmd(8u);
    ptcl_buf.Store(cmd_offset * 4, ty);
    ptcl_buf.Store((cmd_offset + 1u) * 4, gradIndex);
    ptcl_buf.Store((cmd_offset + 2u) * 4, asuint(pd.gp0));
    ptcl_buf.Store((cmd_offset + 3u) * 4, asuint(pd.gp1));
    ptcl_buf.Store((cmd_offset + 4u) * 4, asuint(pd.gp2));
    ptcl_buf.Store((cmd_offset + 5u) * 4, asuint(pd.gp3));
    ptcl_buf.Store((cmd_offset + 6u) * 4, asuint(pd.gp4));
    ptcl_buf.Store((cmd_offset + 7u) * 4, asuint(pd.gp5));
    cmd_offset += 8u;
}

void write_image(PathDraw pd)
{
    alloc_cmd(7u);
    ptcl_buf.Store(cmd_offset * 4, CMD_IMAGE);
    ptcl_buf.Store((cmd_offset + 1u) * 4, pd.gradientIndex); // atlas index
    ptcl_buf.Store((cmd_offset + 2u) * 4, asuint(pd.gp0));   // u0
    ptcl_buf.Store((cmd_offset + 3u) * 4, asuint(pd.gp1));   // v0
    ptcl_buf.Store((cmd_offset + 4u) * 4, asuint(pd.gp2));   // u1
    ptcl_buf.Store((cmd_offset + 5u) * 4, asuint(pd.gp3));   // v1
    ptcl_buf.Store((cmd_offset + 6u) * 4, asuint(pd.color.a)); // opacity
    cmd_offset += 7u;
}

void write_begin_clip()
{
    alloc_cmd(1u);
    ptcl_buf.Store(cmd_offset * 4, CMD_BEGIN_CLIP);
    cmd_offset += 1u;
}

void write_end_clip(uint blend_mode, float alpha)
{
    alloc_cmd(3u);
    ptcl_buf.Store(cmd_offset * 4, CMD_END_CLIP);
    ptcl_buf.Store((cmd_offset + 1u) * 4, blend_mode);
    ptcl_buf.Store((cmd_offset + 2u) * 4, asuint(alpha));
    cmd_offset += 3u;
}

[numthreads(256, 1, 1)]
void main(uint3 localId : SV_GroupThreadID, uint3 wgId : SV_GroupID)
{
    // NOTE: Prior-stage failure check removed to avoid fxc X4026 errors.
    // The coarse shader will run on potentially invalid data if a prior stage
    // failed, but the failure is detected by the CPU after execution.

    uint width_in_bins = (width_in_tiles + N_TILE_X - 1u) / N_TILE_X;
    uint bin_ix = width_in_bins * wgId.y + wgId.x;
    uint n_partitions = (n_drawobj + N_TILE - 1u) / N_TILE;

    uint bin_tile_x = N_TILE_X * wgId.x;
    uint bin_tile_y = N_TILE_Y * wgId.y;
    uint tile_x = localId.x % N_TILE_X;
    uint tile_y = localId.x / N_TILE_X;
    uint this_tile_ix = (bin_tile_y + tile_y) * width_in_tiles + bin_tile_x + tile_x;

    cmd_offset = this_tile_ix * PTCL_INITIAL_ALLOC;
    cmd_limit = cmd_offset + (PTCL_INITIAL_ALLOC - PTCL_HEADROOM);

    uint clip_zero_depth = 0u;
    uint clip_depth = 0u;
    uint render_blend_depth = 0u;
    uint max_blend_depth = 0u;
    uint blend_offset = cmd_offset;
    cmd_offset += 1u;

    uint partition_ix = 0u;
    uint rd_ix = 0u;
    uint wr_ix = 0u;
    uint part_start_ix = 0u;
    uint ready_ix = 0u;

    [loop] for (uint outer_safety = 0u; outer_safety < 1024u; outer_safety++) {
        // Zero bitmaps
        [unroll]
        for (uint si = 0u; si < N_SLICE; si++) {
            sh_bitmaps[si][localId.x] = 0u;
        }

        // Fill sh_drawobj_ix from bin data
        [loop] for (uint inner_safety = 0u; inner_safety < 1024u; inner_safety++) {
            if (ready_ix == wr_ix && partition_ix < n_partitions) {
                part_start_ix = ready_ix;
                uint count = 0u;
                if (partition_ix + localId.x < n_partitions) {
                    uint in_ix = (partition_ix + localId.x) * N_TILE + bin_ix;
                    BinHeader bh = bin_headers[in_ix];
                    count = bh.element_count;
                    sh_part_offsets[localId.x] = bh.chunk_offset;
                }
                // Prefix sum
                [unroll]
                for (uint i = 0u; i < 8u; i++) {
                    sh_part_count[localId.x] = count;
                    GroupMemoryBarrierWithGroupSync();
                    if (localId.x >= (1u << i)) {
                        count += sh_part_count[localId.x - (1u << i)];
                    }
                    GroupMemoryBarrierWithGroupSync();
                }
                sh_part_count[localId.x] = part_start_ix + count;
                GroupMemoryBarrierWithGroupSync();
                ready_ix = sh_part_count[WG_SIZE - 1u];
                partition_ix += WG_SIZE;
            }
            // Binary search for draw object
            uint ix = rd_ix + localId.x;
            if (ix >= wr_ix && ix < ready_ix) {
                uint part_ix = 0u;
                [unroll]
                for (uint i = 0u; i < 8u; i++) {
                    uint probe = part_ix + ((N_TILE / 2u) >> i);
                    if (ix >= sh_part_count[probe - 1u]) {
                        part_ix = probe;
                    }
                }
                ix -= (part_ix > 0u) ? sh_part_count[part_ix - 1u] : part_start_ix;
                uint offset = bin_data_start + sh_part_offsets[part_ix];
                sh_drawobj_ix[localId.x] = info_bin_data.Load((offset + ix) * 4);
            }
            wr_ix = min(rd_ix + N_TILE, ready_ix);
            if (wr_ix - rd_ix >= N_TILE || (wr_ix >= ready_ix && partition_ix >= n_partitions)) {
                break;
            }
            GroupMemoryBarrierWithGroupSync();
        }

        // Process draw objects: determine tile coverage
        // IMPORTANT: default to invalid so threads outside the valid draw range
        // don't accidentally process draw object 0 (tag_val=0 would pass the check).
        uint tag_val = 0xFFFFFFFFu;
        uint drawobj_ix = 0xFFFFFFFFu;
        if (localId.x + rd_ix < wr_ix) {
            drawobj_ix = sh_drawobj_ix[localId.x];
            tag_val = drawTags[drawobj_ix].tag;
        }

        uint tile_count = 0u;
        if (tag_val != 0xFFFFFFFFu && drawobj_ix < n_drawobj) {
            DrawMonoid dm = draw_monoids[drawobj_ix];
            VelloPath path = paths[dm.path_ix];
            int4 pbbox = (int4)path.bbox;
            uint stride = (uint)(pbbox.z - pbbox.x);
            sh_tile_stride[localId.x] = stride;

            int dx = pbbox.x - (int)bin_tile_x;
            int dy = pbbox.y - (int)bin_tile_y;
            int bx0 = clamp(dx, 0, (int)N_TILE_X);
            int by0 = clamp(dy, 0, (int)N_TILE_Y);
            int bx1 = clamp(pbbox.z - (int)bin_tile_x, 0, (int)N_TILE_X);
            int by1 = clamp(pbbox.w - (int)bin_tile_y, 0, (int)N_TILE_Y);
            sh_tile_width[localId.x] = (uint)(bx1 - bx0);
            sh_tile_x0y0[localId.x] = (uint)bx0 | ((uint)by0 << 16u);
            tile_count = (uint)(bx1 - bx0) * (uint)(by1 - by0);
            uint base = path.tiles - (uint)(dy * (int)stride + dx);
            sh_tile_base[localId.x] = base;
        }

        // Prefix sum of tile counts
        sh_tile_count[localId.x] = tile_count;
        [unroll]
        for (uint pi = 0u; pi < 8u; pi++) {
            GroupMemoryBarrierWithGroupSync();
            if (localId.x >= (1u << pi)) {
                tile_count += sh_tile_count[localId.x - (1u << pi)];
            }
            GroupMemoryBarrierWithGroupSync();
            sh_tile_count[localId.x] = tile_count;
        }
        GroupMemoryBarrierWithGroupSync();

        uint total_tile_count = sh_tile_count[N_TILE - 1u];

        // Parallel iteration: determine which tiles each draw object touches
        for (uint tix = localId.x; tix < total_tile_count; tix += N_TILE) {
            // Binary search for draw object
            uint el_ix = 0u;
            [unroll]
            for (uint bi = 0u; bi < 8u; bi++) {
                uint probe = el_ix + ((N_TILE / 2u) >> bi);
                if (tix >= sh_tile_count[probe - 1u]) {
                    el_ix = probe;
                }
            }
            uint this_drawobj_ix = sh_drawobj_ix[el_ix];
            uint this_tag = drawTags[this_drawobj_ix].tag;
            uint seq_ix = tix - ((el_ix > 0u) ? sh_tile_count[el_ix - 1u] : 0u);
            uint width = sh_tile_width[el_ix];
            uint x0y0 = sh_tile_x0y0[el_ix];
            uint lx = (x0y0 & 0xFFFFu) + seq_ix % width;
            uint ly = (x0y0 >> 16u) + seq_ix / width;
            uint t_ix = sh_tile_base[el_ix] + sh_tile_stride[el_ix] * ly + lx;
            VelloTile t = tiles[t_ix];

            // Get draw flags (fill rule)
            DrawMonoid dm2 = draw_monoids[this_drawobj_ix];
            DrawTag dt2 = drawTags[this_drawobj_ix];
            PathInfo pi2 = { 0, 0, 0, 0, 0, 0, 0, 0 };
            if (dm2.path_ix < n_path) {
                // Read fill rule from PathBbox draw_flags
                uint paddr = dm2.path_ix * 24 + 16;
                uint draw_flags = path_bbox_buf.Load(paddr);
                bool even_odd = (draw_flags & DRAW_INFO_FLAGS_FILL_RULE_BIT) != 0u;
                uint n_segs = t.segment_count_or_ix;
                bool backdrop_clear = even_odd
                    ? ((abs(t.backdrop) & 1) == 0)
                    : (t.backdrop == 0);
                bool is_clip = (dt2.tag == 1u || dt2.tag == 2u);  // BeginClip or EndClip
                bool include_tile = (n_segs != 0u) || (!backdrop_clear) || is_clip;
                if (include_tile) {
                    uint el_slice = el_ix / 32u;
                    uint el_mask = 1u << (el_ix & 31u);
                    InterlockedOr(sh_bitmaps[el_slice][ly * N_TILE_X + lx], el_mask);
                }
            }
        }
        GroupMemoryBarrierWithGroupSync();

        // Write PTCL commands per tile
        uint slice_ix = 0u;
        uint bitmap = sh_bitmaps[0u][localId.x];
        [loop] for (;;) {
            if (bitmap == 0u) {
                slice_ix++;
                if (slice_ix == N_SLICE) break;
                bitmap = sh_bitmaps[slice_ix][localId.x];
                if (bitmap == 0u) continue;
            }

            uint el_ix = slice_ix * 32u + firstbitlow(bitmap);
            drawobj_ix = sh_drawobj_ix[el_ix];
            bitmap &= bitmap - 1u;  // clear LSB

            DrawTag dt = drawTags[drawobj_ix];
            DrawMonoid dm = draw_monoids[drawobj_ix];
            uint path_ix = dm.path_ix;

            // Read draw flags from PathBbox
            uint draw_flags = 0u;
            if (path_ix < n_path) {
                draw_flags = path_bbox_buf.Load(path_ix * 24 + 16);
            }

            if (clip_zero_depth == 0u) {
                uint t_ix = sh_tile_base[el_ix] + sh_tile_stride[el_ix] * tile_y + tile_x;
                VelloTile t = tiles[t_ix];

                if (dt.tag == 0u) {
                    // FILL
                    write_path(t, t_ix, draw_flags);
                    PathDraw pd = pathDraws[path_ix];
                    if (pd.brushType == BRUSH_SOLID) {
                        uint r8 = (uint)(min(pd.color.r, 1.0) * 255.0 + 0.5);
                        uint g8 = (uint)(min(pd.color.g, 1.0) * 255.0 + 0.5);
                        uint b8 = (uint)(min(pd.color.b, 1.0) * 255.0 + 0.5);
                        uint a8 = (uint)(min(pd.color.a, 1.0) * 255.0 + 0.5);
                        write_color(r8 | (g8 << 8u) | (b8 << 16u) | (a8 << 24u));
                    } else if (pd.brushType == BRUSH_LINEAR) {
                        write_grad(CMD_LIN_GRAD, pd.gradientIndex, pd);
                    } else if (pd.brushType == BRUSH_RADIAL) {
                        write_grad(CMD_RAD_GRAD, pd.gradientIndex, pd);
                    } else if (pd.brushType == BRUSH_SWEEP) {
                        write_grad(CMD_SWEEP_GRAD, pd.gradientIndex, pd);
                    } else if (pd.brushType == BRUSH_IMAGE) {
                        write_image(pd);
                    }
                } else if (dt.tag == 1u) {
                    // BEGIN_CLIP
                    bool even_odd = (draw_flags & DRAW_INFO_FLAGS_FILL_RULE_BIT) != 0u;
                    bool backdrop_clear = even_odd
                        ? ((abs(t.backdrop) & 1) == 0)
                        : (t.backdrop == 0);
                    if (t.segment_count_or_ix == 0u && backdrop_clear) {
                        clip_zero_depth = clip_depth + 1u;
                    } else {
                        write_begin_clip();
                        render_blend_depth++;
                        max_blend_depth = max(max_blend_depth, render_blend_depth);
                    }
                    clip_depth++;
                } else if (dt.tag == 2u) {
                    // END_CLIP
                    clip_depth--;
                    write_path(t, t_ix, draw_flags);
                    write_end_clip(dt.blendMode, dt.alpha);
                    render_blend_depth--;
                } else if (dt.tag == 3u) {
                    // BLUR_RECT
                    write_path(t, t_ix, draw_flags);
                    PathDraw pd = pathDraws[path_ix];
                    uint r8 = (uint)(min(pd.color.r, 1.0) * 255.0 + 0.5);
                    uint g8 = (uint)(min(pd.color.g, 1.0) * 255.0 + 0.5);
                    uint b8 = (uint)(min(pd.color.b, 1.0) * 255.0 + 0.5);
                    uint a8 = (uint)(min(pd.color.a, 1.0) * 255.0 + 0.5);
                    alloc_cmd(3u);
                    ptcl_buf.Store(cmd_offset * 4, CMD_BLUR_RECT);
                    ptcl_buf.Store((cmd_offset + 1u) * 4, 0u); // info_offset placeholder
                    ptcl_buf.Store((cmd_offset + 2u) * 4, r8 | (g8 << 8u) | (b8 << 16u) | (a8 << 24u));
                    cmd_offset += 3u;
                }
            } else {
                // In clip_zero state: suppress drawing
                if (dt.tag == 1u) {
                    clip_depth++;
                } else if (dt.tag == 2u) {
                    if (clip_depth == clip_zero_depth) {
                        clip_zero_depth = 0u;
                    }
                    clip_depth--;
                }
            }
        }

        rd_ix += N_TILE;
        if (rd_ix >= ready_ix && partition_ix >= n_partitions) break;
        GroupMemoryBarrierWithGroupSync();
    }

    // Terminate PTCL
    if (bin_tile_x + tile_x < width_in_tiles && bin_tile_y + tile_y < height_in_tiles) {
        ptcl_buf.Store(cmd_offset * 4, CMD_END);
        uint blend_ix = 0u;
        if (max_blend_depth > BLEND_STACK_SPLIT) {
            uint scratch_size = (max_blend_depth - BLEND_STACK_SPLIT) * TILE_WIDTH * TILE_HEIGHT;
            bump.InterlockedAdd(BUMP_BLEND, scratch_size, blend_ix);
            if (blend_ix + scratch_size > blend_size) {
                uint dummy;
                bump.InterlockedOr(BUMP_FAILED, STAGE_COARSE, dummy);
            }
        }
        ptcl_buf.Store(blend_offset * 4, blend_ix);
    }
}
