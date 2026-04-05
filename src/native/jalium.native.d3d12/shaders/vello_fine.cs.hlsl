// Vello GPU Pipeline V2 — fine rasterization
// Per-pixel analytical anti-aliased rendering from PTCL commands.
// Reads tile-relative Segment data and PTCL command stream.
// Supports CMD_JUMP for dynamic PTCL allocation.
//
// Dispatch: (width_in_tiles, height_in_tiles, 1)

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

StructuredBuffer<Segment>  seg_data     : register(t0);
ByteAddressBuffer          ptcl         : register(t1);
StructuredBuffer<uint>     gradRamps    : register(t2);  // gradient ramp data
ByteAddressBuffer          info_data    : register(t3);  // draw info (for gradients)
Texture2D<float4>          imageAtlas   : register(t4);
SamplerState               imageSampler : register(s0);

RWTexture2D<float4>        output       : register(u0);
RWByteAddressBuffer        blend_spill  : register(u1);

// Analytical area formula (Vello's signed area)
// Analytical area computation — matches Vello reference fill_path (fine.wgsl:905-959)
// Called per-segment per-pixel. tile_xy is pixel position within tile (0..TILE_WIDTH).
float fill_path(float2 tile_xy, Segment seg, float area)
{
    float2 p0 = seg.point0;
    float2 p1 = seg.point1;
    float2 delta = p1 - p0;

    // Analytical area from line segment crossing this pixel row
    float y = p0.y - tile_xy.y;
    float y0 = clamp(y, 0.0, 1.0);
    float y1 = clamp(y + delta.y, 0.0, 1.0);
    float dy = y0 - y1;
    if (dy != 0.0) {
        float vec_y_recip = 1.0 / delta.y;
        float t0 = (y0 - y) * vec_y_recip;
        float t1 = (y1 - y) * vec_y_recip;
        float startx = p0.x - tile_xy.x;
        float x0 = startx + t0 * delta.x;
        float x1 = startx + t1 * delta.x;
        float xmin0 = min(x0, x1);
        float xmax0 = max(x0, x1);
        float xmin = min(xmin0, 1.0) - 1e-6;
        float xmax = xmax0;
        float b = min(xmax, 1.0);
        float c = max(b, 0.0);
        float d = max(xmin, 0.0);
        float a = (b + 0.5 * (d * d - c * c) - xmin) / (xmax - xmin);
        area += a * dy;
    }

    // y_edge contribution: smooth winding for segments touching tile left edge (x=0).
    // Vello reference (fine.wgsl:941):
    //   let y_edge = sign(delta.x) * clamp(xy.y - segment.y_edge + 1.0, 0.0, 1.0);
    //   area[i] += y_edge;
    area += sign(delta.x) * clamp(tile_xy.y - seg.y_edge + 1.0, 0.0, 1.0);

    return area;
}

// Sample gradient ramp
float4 SampleGradient(uint gradIndex, float t)
{
    t = clamp(t, 0.0, 1.0);
    float fi = t * (float)(GRAD_RAMP_WIDTH - 1);
    uint i0 = (uint)fi;
    uint i1 = min(i0 + 1, GRAD_RAMP_WIDTH - 1);
    float frac = fi - (float)i0;
    uint base = gradIndex * GRAD_RAMP_WIDTH;
    uint rgba0 = gradRamps[base + i0];
    uint rgba1 = gradRamps[base + i1];
    float4 c0 = float4(
        (float)(rgba0 & 0xFF) / 255.0,
        (float)((rgba0 >> 8) & 0xFF) / 255.0,
        (float)((rgba0 >> 16) & 0xFF) / 255.0,
        (float)((rgba0 >> 24) & 0xFF) / 255.0);
    float4 c1 = float4(
        (float)(rgba1 & 0xFF) / 255.0,
        (float)((rgba1 >> 8) & 0xFF) / 255.0,
        (float)((rgba1 >> 16) & 0xFF) / 255.0,
        (float)((rgba1 >> 24) & 0xFF) / 255.0);
    return lerp(c0, c1, frac);
}

// Extend mode helper
float apply_extend_mode(float t, uint mode)
{
    if (mode == EXTEND_PAD) {
        return clamp(t, 0.0, 1.0);
    } else if (mode == EXTEND_REPEAT) {
        return frac(t);
    } else { // EXTEND_REFLECT
        t = frac(t * 0.5) * 2.0;
        return (t > 1.0) ? (2.0 - t) : t;
    }
}

// Porter-Duff + mix blend
float4 blend_mix_compose(float4 backdrop, float4 src, uint mode)
{
    // For simplicity, SrcOver compositing
    // TODO: implement full blend mode table from Vello
    uint compose_mode = mode & 0xFFu;
    uint mix_mode = (mode >> 8u) & 0xFFu;

    // Default SrcOver
    return src + backdrop * (1.0 - src.a);
}

// Workgroup size: 4x16 = 64 threads (matches Vello fine)
// Each thread handles PIXELS_PER_THREAD=4 pixels vertically
#define PIXELS_PER_THREAD 4u

[numthreads(4, 16, 1)]
void main(uint3 gId : SV_GroupID, uint3 lId : SV_GroupThreadID)
{
    uint tx = gId.x, ty = gId.y;
    if (tx >= width_in_tiles || ty >= height_in_tiles) return;

    uint tIdx = ty * width_in_tiles + tx;

    // Each thread handles a column of PIXELS_PER_THREAD pixels
    uint pixelX = tx * TILE_WIDTH + lId.x * PIXELS_PER_THREAD;
    uint pixelY = ty * TILE_HEIGHT + lId.y;

    // Read initial PTCL offset (blend_ix stored at slot 0)
    uint cmd_ix = tIdx * PTCL_INITIAL_ALLOC;
    uint blend_ix = ptcl.Load(cmd_ix * 4);
    cmd_ix += 1u;

    // Process 4 pixels at a time (PIXELS_PER_THREAD along x)
    float4 rgba[4];
    [unroll] for (uint pi = 0; pi < PIXELS_PER_THREAD; pi++) {
        rgba[pi] = float4(0, 0, 0, 0);
    }

    // Clip stack (shallow, in registers)
    float4 clip_stack[4];
    uint clip_depth = 0u;

    float area[4];

    [loop] for (uint safety = 0; safety < 4096; safety++) {
        uint cmd = ptcl.Load(cmd_ix * 4);
        cmd_ix++;

        if (cmd == CMD_END) break;

        if (cmd == CMD_JUMP) {
            cmd_ix = ptcl.Load(cmd_ix * 4);
            continue;
        }

        if (cmd == CMD_FILL) {
            uint size_and_rule = ptcl.Load(cmd_ix * 4); cmd_ix++;
            uint seg_base = ptcl.Load(cmd_ix * 4); cmd_ix++;
            int backdrop = asint(ptcl.Load(cmd_ix * 4)); cmd_ix++;

            uint n_segs = size_and_rule >> 1u;
            bool even_odd = (size_and_rule & 1u) != 0u;
            n_segs = min(n_segs, 4096u); // safety clamp to prevent TDR

            [unroll] for (uint pi = 0; pi < PIXELS_PER_THREAD; pi++) {
                area[pi] = (float)backdrop;
            }

            for (uint si = 0; si < n_segs; si++) {
                Segment seg = seg_data[seg_base + si];
                [unroll] for (uint pi = 0; pi < PIXELS_PER_THREAD; pi++) {
                    // Segments are tile-relative (0..TILE_WIDTH/HEIGHT), so use local pixel position within tile
                    float2 tile_xy = float2((float)(lId.x * PIXELS_PER_THREAD + pi), (float)lId.y);
                    area[pi] = fill_path(tile_xy, seg, area[pi]);
                }
            }

            [unroll] for (uint pi = 0; pi < PIXELS_PER_THREAD; pi++) {
                if (even_odd) {
                    area[pi] = abs(area[pi] - 2.0 * round(0.5 * area[pi]));
                } else {
                    area[pi] = min(abs(area[pi]), 1.0);
                }
            }
        }
        else if (cmd == CMD_SOLID) {
            [unroll] for (uint pi = 0; pi < PIXELS_PER_THREAD; pi++) {
                area[pi] = 1.0;
            }
        }
        else if (cmd == CMD_COLOR) {
            uint packed_rgba = ptcl.Load(cmd_ix * 4); cmd_ix++;
            float4 color;
            color.r = (float)(packed_rgba & 0xFF) / 255.0;
            color.g = (float)((packed_rgba >> 8) & 0xFF) / 255.0;
            color.b = (float)((packed_rgba >> 16) & 0xFF) / 255.0;
            color.a = (float)((packed_rgba >> 24) & 0xFF) / 255.0;
            [unroll] for (uint pi = 0; pi < PIXELS_PER_THREAD; pi++) {
                if (area[pi] > 1.0 / 255.0) {
                    float4 src = color * area[pi];
                    rgba[pi] = src + rgba[pi] * (1.0 - src.a);
                }
                area[pi] = 0.0;
            }
        }
        else if (cmd == CMD_LIN_GRAD) {
            uint gradIndex = ptcl.Load(cmd_ix * 4); cmd_ix++;
            float p0x = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float p0y = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float p1x = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float p1y = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float extF = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            // Remaining gradient params
            float dummy1 = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float dummy2 = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;

            [unroll] for (uint pi = 0; pi < PIXELS_PER_THREAD; pi++) {
                if (area[pi] > 1.0 / 255.0) {
                    float px = (float)(pixelX + pi) + 0.5;
                    float py = (float)pixelY + 0.5;
                    float2 d = float2(p1x - p0x, p1y - p0y);
                    float lenSq = dot(d, d);
                    float t = (lenSq > 1e-9) ? dot(float2(px - p0x, py - p0y), d) / lenSq : 0.0;
                    float4 color = SampleGradient(gradIndex, t);
                    float4 src = color * area[pi];
                    rgba[pi] = src + rgba[pi] * (1.0 - src.a);
                }
                area[pi] = 0.0;
            }
        }
        else if (cmd == CMD_RAD_GRAD) {
            uint gradIndex = ptcl.Load(cmd_ix * 4); cmd_ix++;
            float cx = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float cy = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float rx = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float ry = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float ox = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float oy = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float extF2 = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;

            [unroll] for (uint pi = 0; pi < PIXELS_PER_THREAD; pi++) {
                if (area[pi] > 1.0 / 255.0) {
                    float px = (float)(pixelX + pi) + 0.5;
                    float py = (float)pixelY + 0.5;
                    float ddx = (rx > 1e-6) ? (px - cx) / rx : 0.0;
                    float ddy = (ry > 1e-6) ? (py - cy) / ry : 0.0;
                    float t = sqrt(ddx * ddx + ddy * ddy);
                    float4 color = SampleGradient(gradIndex, t);
                    float4 src = color * area[pi];
                    rgba[pi] = src + rgba[pi] * (1.0 - src.a);
                }
                area[pi] = 0.0;
            }
        }
        else if (cmd == CMD_SWEEP_GRAD) {
            uint gradIndex = ptcl.Load(cmd_ix * 4); cmd_ix++;
            float scx = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float scy = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float st0 = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float st1 = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float extF3 = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float dummy3 = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float dummy4 = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;

            [unroll] for (uint pi = 0; pi < PIXELS_PER_THREAD; pi++) {
                if (area[pi] > 1.0 / 255.0) {
                    float px = (float)(pixelX + pi) + 0.5;
                    float py = (float)pixelY + 0.5;
                    float angle = atan2(py - scy, px - scx);
                    float t = (angle / (2.0 * 3.14159265359) + 0.5);
                    t = st0 + t * (st1 - st0);
                    float4 color = SampleGradient(gradIndex, t);
                    float4 src = color * area[pi];
                    rgba[pi] = src + rgba[pi] * (1.0 - src.a);
                }
                area[pi] = 0.0;
            }
        }
        else if (cmd == CMD_IMAGE) {
            uint atlasIdx = ptcl.Load(cmd_ix * 4); cmd_ix++;
            float u0 = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float v0 = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float u1 = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float v1 = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            float opacity = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;

            [unroll] for (uint pi = 0; pi < PIXELS_PER_THREAD; pi++) {
                if (area[pi] > 1.0 / 255.0) {
                    float px = (float)(pixelX + pi) + 0.5;
                    float py = (float)pixelY + 0.5;
                    float u = u0 + (u1 - u0) * (px / (float)target_width);
                    float v = v0 + (v1 - v0) * (py / (float)target_height);
                    float4 color = imageAtlas.SampleLevel(imageSampler, float2(u, v), 0);
                    color *= opacity;
                    float4 src = color * area[pi];
                    rgba[pi] = src + rgba[pi] * (1.0 - src.a);
                }
                area[pi] = 0.0;
            }
        }
        else if (cmd == CMD_BEGIN_CLIP) {
            [unroll] for (uint pi = 0; pi < PIXELS_PER_THREAD; pi++) {
                if (clip_depth < BLEND_STACK_SPLIT) {
                    clip_stack[clip_depth] = rgba[pi];
                }
                rgba[pi] = float4(0, 0, 0, 0);
            }
            clip_depth++;
        }
        else if (cmd == CMD_END_CLIP) {
            uint blend_mode = ptcl.Load(cmd_ix * 4); cmd_ix++;
            float clip_alpha = asfloat(ptcl.Load(cmd_ix * 4)); cmd_ix++;
            clip_depth--;
            [unroll] for (uint pi = 0; pi < PIXELS_PER_THREAD; pi++) {
                float4 bg;
                if (clip_depth < BLEND_STACK_SPLIT) {
                    bg = clip_stack[clip_depth];
                } else {
                    bg = float4(0, 0, 0, 0); // spill (simplified)
                }
                float4 fg = rgba[pi];
                fg *= area[pi] * clip_alpha;
                rgba[pi] = fg + bg * (1.0 - fg.a);
                area[pi] = 0.0;
            }
        }
        else if (cmd == CMD_BLUR_RECT) {
            uint info_off = ptcl.Load(cmd_ix * 4); cmd_ix++;
            uint packed_rgba = ptcl.Load(cmd_ix * 4); cmd_ix++;
            float4 color;
            color.r = (float)(packed_rgba & 0xFF) / 255.0;
            color.g = (float)((packed_rgba >> 8) & 0xFF) / 255.0;
            color.b = (float)((packed_rgba >> 16) & 0xFF) / 255.0;
            color.a = (float)((packed_rgba >> 24) & 0xFF) / 255.0;
            [unroll] for (uint pi = 0; pi < PIXELS_PER_THREAD; pi++) {
                if (area[pi] > 1.0 / 255.0) {
                    float4 src = color * area[pi];
                    rgba[pi] = src + rgba[pi] * (1.0 - src.a);
                }
                area[pi] = 0.0;
            }
        }
    }

    // Write output pixels
    [unroll] for (uint pi = 0; pi < PIXELS_PER_THREAD; pi++) {
        uint ox = pixelX + pi;
        uint oy = pixelY;
        if (ox < target_width && oy < target_height) {
            // Apply base color
            float4 bg;
            bg.r = (float)(base_color & 0xFF) / 255.0;
            bg.g = (float)((base_color >> 8) & 0xFF) / 255.0;
            bg.b = (float)((base_color >> 16) & 0xFF) / 255.0;
            bg.a = (float)((base_color >> 24) & 0xFF) / 255.0;
            float4 final_color = rgba[pi] + bg * (1.0 - rgba[pi].a);
            output[uint2(ox, oy)] = final_color;
        }
    }
}
