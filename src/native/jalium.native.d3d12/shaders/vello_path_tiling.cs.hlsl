// Vello GPU Pipeline V2 — path_tiling
// For each SegmentCount entry, computes tile-relative segment coordinates.
// Clips line segments to tile boundaries with numerical robustness.
//
// Dispatch: indirect (from path_tiling_setup)

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

RWByteAddressBuffer bump : register(u0);

StructuredBuffer<SegmentCount> seg_counts_buf : register(t0);
StructuredBuffer<LineSoup>     lines          : register(t1);
StructuredBuffer<VelloPath>    paths          : register(t2);

// Tiles: read segment_count_or_ix (which was inverted by coarse: ~seg_base)
// Using raw buffer for atomic-compatible reads
ByteAddressBuffer tile_data : register(t3);

RWStructuredBuffer<Segment> segments : register(u1);

uint span_fn(float a, float b)
{
    return (uint)max(ceil(max(a, b)) - floor(min(a, b)), 1.0);
}

#define EPSILON 1e-6

[numthreads(256, 1, 1)]
void main(uint3 globalId : SV_DispatchThreadID)
{
    uint n_segments_total;
    bump.InterlockedAdd(BUMP_SEG_COUNTS, 0u, n_segments_total); // atomic load
    if (globalId.x >= n_segments_total) return;

    SegmentCount sc = seg_counts_buf[globalId.x];
    LineSoup ln = lines[sc.line_ix];
    uint seg_within_slice = sc.counts >> 16u;
    uint seg_within_line = sc.counts & 0xFFFFu;

    // Coarse rasterization logic (must match path_count exactly)
    bool is_down = ln.p1.y >= ln.p0.y;
    float2 xy0 = is_down ? ln.p0 : ln.p1;
    float2 xy1 = is_down ? ln.p1 : ln.p0;
    float2 s0 = xy0 * TILE_SCALE;
    float2 s1 = xy1 * TILE_SCALE;
    uint count_x = span_fn(s0.x, s1.x) - 1u;
    uint count = count_x + span_fn(s0.y, s1.y);

    float dx = abs(s1.x - s0.x);
    float dy = s1.y - s0.y;
    float idxdy = 1.0 / (dx + dy);
    float a = dx * idxdy;
    bool is_positive_slope = s1.x >= s0.x;
    float x_sign = is_positive_slope ? 1.0 : -1.0;
    float xt0 = floor(s0.x * x_sign);
    float c = s0.x * x_sign - xt0;
    float y0i = floor(s0.y);
    float ytop = (s0.y == s1.y) ? ceil(s0.y) : (y0i + 1.0);
    float b = min((dy * c + dx * (ytop - s0.y)) * idxdy, ONE_MINUS_ULP);
    float robust_err = floor(a * ((float)(count) - 1.0) + b) - (float)count_x;
    if (robust_err != 0.0) {
        a -= ROBUST_EPSILON * sign(robust_err);
    }
    int x0i = (int)(xt0 * x_sign + 0.5 * (x_sign - 1.0));
    float z = floor(a * (float)seg_within_line + b);
    int x = x0i + (int)(x_sign * z);
    int y = (int)(y0i + (float)seg_within_line - z);

    VelloPath path = paths[ln.path_ix];
    int4 bbox = (int4)path.bbox;
    int stride = bbox.z - bbox.x;
    int tile_ix = (int)path.tiles + (y - bbox.y) * stride + x - bbox.x;

    // Read segment allocation base from tile (was inverted by coarse: ~seg_base)
    uint seg_count_or_ix = tile_data.Load(tile_ix * 8 + 4);
    uint seg_start = ~seg_count_or_ix;
    if ((int)seg_start < 0) return;
    // Bounds check: don't write past segment buffer capacity
    if (seg_start + seg_within_slice >= segments_size) return;

    float2 tile_xy = float2((float)x * (float)TILE_WIDTH, (float)y * (float)TILE_HEIGHT);
    float2 tile_xy1 = tile_xy + float2((float)TILE_WIDTH, (float)TILE_HEIGHT);

    // Clip top edge
    if (seg_within_line > 0u) {
        float z_prev = floor(a * ((float)seg_within_line - 1.0) + b);
        if (z == z_prev) {
            // Top edge is clipped
            float xt = xy0.x + (xy1.x - xy0.x) * (tile_xy.y - xy0.y) / (xy1.y - xy0.y);
            xt = clamp(xt, tile_xy.x + 1e-3, tile_xy1.x);
            xy0 = float2(xt, tile_xy.y);
        } else {
            // Left or right edge is clipped
            float x_clip = is_positive_slope ? tile_xy.x : tile_xy1.x;
            float yt = xy0.y + (xy1.y - xy0.y) * (x_clip - xy0.x) / (xy1.x - xy0.x);
            yt = clamp(yt, tile_xy.y + 1e-3, tile_xy1.y);
            xy0 = float2(x_clip, yt);
        }
    }

    // Clip bottom edge
    if (seg_within_line < count - 1u) {
        float z_next = floor(a * ((float)seg_within_line + 1.0) + b);
        if (z == z_next) {
            // Bottom edge is clipped
            float xt = xy0.x + (xy1.x - xy0.x) * (tile_xy1.y - xy0.y) / (xy1.y - xy0.y);
            xt = clamp(xt, tile_xy.x + 1e-3, tile_xy1.x);
            xy1 = float2(xt, tile_xy1.y);
        } else {
            // Left or right edge is clipped
            float x_clip = is_positive_slope ? tile_xy1.x : tile_xy.x;
            float yt = xy0.y + (xy1.y - xy0.y) * (x_clip - xy0.x) / (xy1.x - xy0.x);
            yt = clamp(yt, tile_xy.y + 1e-3, tile_xy1.y);
            xy1 = float2(x_clip, yt);
        }
    }

    // Convert to tile-relative coordinates
    float2 p0 = xy0 - tile_xy;
    float2 p1 = xy1 - tile_xy;
    float y_edge = 1e9;

    // Numerical robustness for segments touching tile left edge
    if (p0.x == 0.0) {
        if (p1.x == 0.0) {
            p0.x = EPSILON;
            if (p0.y == 0.0) {
                p1.x = EPSILON;
                p1.y = (float)TILE_HEIGHT;
            } else {
                p1.x = 2.0 * EPSILON;
                p1.y = p0.y;
            }
        } else if (p0.y == 0.0) {
            p0.x = EPSILON;
        } else {
            y_edge = p0.y;
        }
    } else if (p1.x == 0.0) {
        if (p1.y == 0.0) {
            p1.x = EPSILON;
        } else {
            y_edge = p1.y;
        }
    }

    // Avoid vertical lines aligned to pixel grid
    if (p0.x == floor(p0.x) && p0.x != 0.0) {
        p0.x -= EPSILON;
    }
    if (p1.x == floor(p1.x) && p1.x != 0.0) {
        p1.x -= EPSILON;
    }

    // Reverse direction if segment was going up
    if (!is_down) {
        float2 tmp = p0;
        p0 = p1;
        p1 = tmp;
    }

    Segment seg;
    seg.point0 = p0;
    seg.point1 = p1;
    seg.y_edge = y_edge;
    segments[seg_start + seg_within_slice] = seg;
}
