// Vello GPU Pipeline V3 — path_tiling
// Port of vello 0.10.0 shader/path_tiling.wgsl.
// Writes tile-relative path segments, clipping each line to its tile and
// deriving the y_edge winding carrier.
//
// Bindings: u0 bump | t0 seg_counts | t1 lines | t2 paths | t3 tiles | u1 segments
// Dispatch: indirect (from path_tiling_setup)
//
// NOTE: no config cbuffer, matching upstream.

#include "vello_shared.hlsli"

RWByteAddressBuffer bump : register(u0);
StructuredBuffer<SegmentCount> seg_counts : register(t0);
StructuredBuffer<LineSoup> lines : register(t1);
StructuredBuffer<VelloPath> paths : register(t2);
StructuredBuffer<VelloTile> tiles : register(t3);
RWStructuredBuffer<Segment> segments : register(u1);

uint span(float a, float b)
{
    return (uint)max(ceil(max(a, b)) - floor(min(a, b)), 1.0);
}

// One invocation for each tile that is to be written.
// Total number of invocations = bump.seg_counts
[numthreads(256, 1, 1)]
void main(uint3 global_id : SV_DispatchThreadID)
{
    uint n_segments = bump.Load(BUMP_SEG_COUNTS);
    if (global_id.x < n_segments) {
        SegmentCount seg_count = seg_counts[global_id.x];
        LineSoup ln = lines[seg_count.line_ix];
        uint counts = seg_count.counts;
        uint seg_within_slice = counts >> 16u;
        uint seg_within_line = counts & 0xffffu;

        // coarse rasterization logic
        bool is_down = ln.p1.y >= ln.p0.y;
        float2 xy0 = is_down ? ln.p0 : ln.p1;
        float2 xy1 = is_down ? ln.p1 : ln.p0;
        float2 s0 = xy0 * TILE_SCALE;
        float2 s1 = xy1 * TILE_SCALE;
        uint count_x = span(s0.x, s1.x) - 1u;
        uint count = count_x + span(s0.y, s1.y);
        float dx = abs(s1.x - s0.x);
        float dy = s1.y - s0.y;
        // Division by zero can't happen because zero-length lines
        // have already been discarded in the path_count stage.
        float idxdy = 1.0 / (dx + dy);
        float a = dx * idxdy;
        bool is_positive_slope = s1.x >= s0.x;
        float x_sign = is_positive_slope ? 1.0 : -1.0;
        float xt0 = floor(s0.x * x_sign);
        float c = s0.x * x_sign - xt0;
        float y0i = floor(s0.y);
        float ytop = (s0.y == s1.y) ? ceil(s0.y) : (y0i + 1.0);
        float b = min((dy * c + dx * (ytop - s0.y)) * idxdy, ONE_MINUS_ULP);
        float robust_err = floor(a * ((float)count - 1.0) + b) - (float)count_x;
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
        VelloTile til = tiles[tile_ix];
        uint seg_start = ~til.segment_count_or_ix;
        if ((int)seg_start < 0) {
            return;
        }
        float2 tile_xy = float2((float)x * (float)TILE_WIDTH, (float)y * (float)TILE_HEIGHT);
        float2 tile_xy1 = tile_xy + float2((float)TILE_WIDTH, (float)TILE_HEIGHT);

        if (seg_within_line > 0u) {
            float z_prev = floor(a * ((float)seg_within_line - 1.0) + b);
            if (z == z_prev) {
                // Top edge is clipped
                float xt = xy0.x + (xy1.x - xy0.x) * (tile_xy.y - xy0.y) / (xy1.y - xy0.y);
                xt = clamp(xt, tile_xy.x + 1e-3, tile_xy1.x);
                xy0 = float2(xt, tile_xy.y);
            } else {
                // If is_positive_slope, left edge is clipped, otherwise right
                float x_clip = is_positive_slope ? tile_xy.x : tile_xy1.x;
                float yt = xy0.y + (xy1.y - xy0.y) * (x_clip - xy0.x) / (xy1.x - xy0.x);
                yt = clamp(yt, tile_xy.y + 1e-3, tile_xy1.y);
                xy0 = float2(x_clip, yt);
            }
        }
        if (seg_within_line < count - 1u) {
            float z_next = floor(a * ((float)seg_within_line + 1.0) + b);
            if (z == z_next) {
                // Bottom edge is clipped
                float xt = xy0.x + (xy1.x - xy0.x) * (tile_xy1.y - xy0.y) / (xy1.y - xy0.y);
                xt = clamp(xt, tile_xy.x + 1e-3, tile_xy1.x);
                xy1 = float2(xt, tile_xy1.y);
            } else {
                // If is_positive_slope, right edge is clipped, otherwise left
                float x_clip = is_positive_slope ? tile_xy1.x : tile_xy.x;
                float yt = xy0.y + (xy1.y - xy0.y) * (x_clip - xy0.x) / (xy1.x - xy0.x);
                yt = clamp(yt, tile_xy.y + 1e-3, tile_xy1.y);
                xy1 = float2(x_clip, yt);
            }
        }
        float y_edge = 1e9;
        // Apply numerical robustness logic
        float2 p0 = xy0 - tile_xy;
        float2 p1 = xy1 - tile_xy;
        // When we move to f16, this will be f16::MIN_POSITIVE
        const float EPSILON = 1e-6;
        if (p0.x == 0.0) {
            if (p1.x == 0.0) {
                p0.x = EPSILON;
                if (p0.y == 0.0) {
                    // Entire tile
                    p1.x = EPSILON;
                    p1.y = (float)TILE_HEIGHT;
                } else {
                    // Make segment disappear
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
        // Hacky approach to numerical robustness in fine.
        // This just makes sure there are no vertical lines aligned to
        // the pixel grid internal to the tile.
        if (p0.x == floor(p0.x) && p0.x != 0.0) {
            p0.x -= EPSILON;
        }
        if (p1.x == floor(p1.x) && p1.x != 0.0) {
            p1.x -= EPSILON;
        }
        if (!is_down) {
            float2 tmp = p0;
            p0 = p1;
            p1 = tmp;
        }
        Segment segment;
        segment.point0 = p0;
        segment.point1 = p1;
        segment.y_edge = y_edge;
        segment.pad = 0.0;
        segments[seg_start + seg_within_slice] = segment;
    }
}
