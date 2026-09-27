// Vello GPU Pipeline V3 — path_count
// Port of vello 0.10.0 shader/path_count.wgsl.
// For each flattened line, counts the tiles it touches (emitting SegmentCount
// records) and applies backdrop winding deltas.
//
// The tile buffer is accessed as a raw buffer (8 bytes per tile:
// backdrop @ +0, segment_count_or_ix @ +4) because both fields need atomics.
//
// Bindings: b0 config | u0 bump | t0 lines | t1 paths | u1 tile | u2 seg_counts
// Dispatch: indirect (from path_count_setup)

#include "vello_shared.hlsli"

RWByteAddressBuffer bump : register(u0);
StructuredBuffer<LineSoup> lines : register(t0);
StructuredBuffer<VelloPath> paths : register(t1);
RWByteAddressBuffer tile : register(u1);
RWStructuredBuffer<SegmentCount> seg_counts : register(u2);

// number of integer cells spanned by interval defined by a, b
uint span(float a, float b)
{
    return (uint)max(ceil(max(a, b)) - floor(min(a, b)), 1.0);
}

// This shader is dispatched with one thread for each line.
[numthreads(256, 1, 1)]
void main(uint3 global_id : SV_DispatchThreadID)
{
    uint n_lines = bump.Load(BUMP_LINES);
    uint count = 0u;
    if (global_id.x < n_lines) {
        LineSoup ln = lines[global_id.x];
        // coarse rasterization logic to count number of tiles touched by line
        bool is_down = ln.p1.y >= ln.p0.y;
        float2 xy0 = is_down ? ln.p0 : ln.p1;
        float2 xy1 = is_down ? ln.p1 : ln.p0;
        float2 s0 = xy0 * TILE_SCALE;
        float2 s1 = xy1 * TILE_SCALE;
        uint count_x = span(s0.x, s1.x) - 1u;
        count = count_x + span(s0.y, s1.y);
        uint line_ix = global_id.x;

        float dx = abs(s1.x - s0.x);
        float dy = s1.y - s0.y;
        if (dx + dy == 0.0) {
            // Zero-length segment, drop it. Note, this could be culled in the
            // flattening stage, but eliding the test here would be fragile, as
            // it would be pretty bad to let it slip through.
            return;
        }
        if (dy == 0.0 && floor(s0.y) == s0.y) {
            return;
        }
        float idxdy = 1.0 / (dx + dy);
        float a = dx * idxdy;
        bool is_positive_slope = s1.x >= s0.x;
        float x_sign = is_positive_slope ? 1.0 : -1.0;
        float xt0 = floor(s0.x * x_sign);
        float c = s0.x * x_sign - xt0;
        float y0 = floor(s0.y);
        float ytop = (s0.y == s1.y) ? ceil(s0.y) : (y0 + 1.0);
        float b = min((dy * c + dx * (ytop - s0.y)) * idxdy, ONE_MINUS_ULP);
        float robust_err = floor(a * ((float)count - 1.0) + b) - (float)count_x;
        if (robust_err != 0.0) {
            a -= ROBUST_EPSILON * sign(robust_err);
        }
        float x0 = xt0 * x_sign + (is_positive_slope ? 0.0 : -1.0);

        VelloPath path = paths[ln.path_ix];
        int4 bbox = (int4)path.bbox;
        float xmin = min(s0.x, s1.x);
        // If line is to left of bbox, we may still need to do backdrop
        int stride = bbox.z - bbox.x;
        if (s0.y >= (float)bbox.w || s1.y <= (float)bbox.y || xmin >= (float)bbox.z || stride == 0) {
            return;
        }
        // Clip line to bounding box. Clipping is done in "i" space.
        uint imin = 0u;
        if (s0.y < (float)bbox.y) {
            float iminf = round(((float)bbox.y - y0 + b - a) / (1.0 - a)) - 1.0;
            // Numerical robustness: goal is to find the first i value for which
            // the following predicate is false. Above formula is designed to
            // undershoot by 0.5.
            if (y0 + iminf - floor(a * iminf + b) < (float)bbox.y) {
                iminf += 1.0;
            }
            imin = (uint)iminf;
        }
        uint imax = count;
        if (s1.y > (float)bbox.w) {
            float imaxf = round(((float)bbox.w - y0 + b - a) / (1.0 - a)) - 1.0;
            if (y0 + imaxf - floor(a * imaxf + b) < (float)bbox.w) {
                imaxf += 1.0;
            }
            imax = (uint)imaxf;
        }
        int delta = is_down ? -1 : 1;
        int ymin = 0;
        int ymax = 0;
        if (max(s0.x, s1.x) <= (float)bbox.x) {
            ymin = (int)ceil(s0.y);
            ymax = (int)ceil(s1.y);
            imax = imin;
        } else {
            float fudge = is_positive_slope ? 0.0 : 1.0;
            if (xmin < (float)bbox.x) {
                float f = round((x_sign * ((float)bbox.x - x0) - b + fudge) / a);
                if (((x0 + x_sign * floor(a * f + b)) < (float)bbox.x) == is_positive_slope) {
                    f += 1.0;
                }
                int ynext = (int)(y0 + f - floor(a * f + b) + 1.0);
                if (is_positive_slope) {
                    if ((uint)f > imin) {
                        ymin = (int)(y0 + ((y0 == s0.y) ? 0.0 : 1.0));
                        ymax = ynext;
                        imin = (uint)f;
                    }
                } else {
                    if ((uint)f < imax) {
                        ymin = ynext;
                        ymax = (int)ceil(s1.y);
                        imax = (uint)f;
                    }
                }
            }
            if (max(s0.x, s1.x) > (float)bbox.z) {
                float f = round((x_sign * ((float)bbox.z - x0) - b + fudge) / a);
                if (((x0 + x_sign * floor(a * f + b)) < (float)bbox.z) == is_positive_slope) {
                    f += 1.0;
                }
                if (is_positive_slope) {
                    imax = min(imax, (uint)f);
                } else {
                    imin = max(imin, (uint)f);
                }
            }
        }
        imax = max(imin, imax);
        // Apply backdrop for part of line left of bbox
        ymin = max(ymin, bbox.y);
        ymax = min(ymax, bbox.w);
        for (int y = ymin; y < ymax; y++) {
            int base = (int)path.tiles + (y - bbox.y) * stride;
            int dummy;
            tile.InterlockedAdd(base * 8, delta, dummy);
        }
        float last_z = floor(a * ((float)imin - 1.0) + b);
        uint seg_base;
        bump.InterlockedAdd(BUMP_SEG_COUNTS, imax - imin, seg_base);
        for (uint i = imin; i < imax; i++) {
            uint subix = i;
            // coarse rasterization logic
            // Note: we hope fast-math doesn't strength reduce this.
            float zf = a * (float)subix + b;
            float z = floor(zf);
            // x, y are tile coordinates relative to render target
            int y = (int)(y0 + (float)subix - z);
            int x = (int)(x0 + x_sign * z);
            int base = (int)path.tiles + (y - bbox.y) * stride - bbox.x;
            bool top_edge = (subix == 0u) ? (y0 == s0.y) : (last_z == z);
            if (top_edge && x + 1 < bbox.z) {
                int x_bump = max(x + 1, bbox.x);
                int dummy;
                tile.InterlockedAdd((base + x_bump) * 8, delta, dummy);
            }
            uint seg_within_slice;
            tile.InterlockedAdd((base + x) * 8 + 4, 1u, seg_within_slice);
            // Pack two count values into a single u32
            uint counts = (seg_within_slice << 16u) | subix;
            SegmentCount seg_count;
            seg_count.line_ix = line_ix;
            seg_count.counts = counts;
            uint seg_ix = seg_base + i - imin;
            if (seg_ix < seg_counts_size) {
                seg_counts[seg_ix] = seg_count;
            }
            // Note: since we're iterating, we have a reliable value for last_z.
            last_z = z;
        }
    }
}
