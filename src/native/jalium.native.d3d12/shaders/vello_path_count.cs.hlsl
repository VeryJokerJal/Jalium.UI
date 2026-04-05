// Vello GPU Pipeline V2 — path_count
// For each line segment, counts how many tiles it crosses and records SegmentCount entries.
// Also updates tile backdrop values atomically.
//
// Dispatch: indirect (from path_count_setup)

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

StructuredBuffer<LineSoup> lines : register(t0);
StructuredBuffer<VelloPath> paths : register(t1);

// AtomicTile: backdrop (i32 atomic) + segment_count_or_ix (u32 atomic)
// Stored as raw buffer: 8 bytes per tile
RWByteAddressBuffer tile : register(u1);

RWStructuredBuffer<SegmentCount> seg_counts : register(u2);

uint span(float a, float b)
{
    return (uint)max(ceil(max(a, b)) - floor(min(a, b)), 1.0);
}

[numthreads(256, 1, 1)]
void main(uint3 globalId : SV_DispatchThreadID)
{
    uint n_lines;
    bump.InterlockedAdd(BUMP_LINES, 0u, n_lines); // atomic load

    if (globalId.x >= n_lines) return;

    LineSoup ln = lines[globalId.x];

    // Coarse rasterization: determine tile crossings
    bool is_down = ln.p1.y >= ln.p0.y;
    float2 xy0 = is_down ? ln.p0 : ln.p1;
    float2 xy1 = is_down ? ln.p1 : ln.p0;
    float2 s0 = xy0 * TILE_SCALE;
    float2 s1 = xy1 * TILE_SCALE;
    uint count_x = span(s0.x, s1.x) - 1u;
    uint count = count_x + span(s0.y, s1.y);

    uint line_ix = globalId.x;

    float dx = abs(s1.x - s0.x);
    float dy = s1.y - s0.y;
    if (dx + dy == 0.0) return;
    if (dy == 0.0 && floor(s0.y) == s0.y) return;

    float idxdy = 1.0 / (dx + dy);
    float a = dx * idxdy;
    bool is_positive_slope = s1.x >= s0.x;
    float x_sign = is_positive_slope ? 1.0 : -1.0;
    float xt0 = floor(s0.x * x_sign);
    float c = s0.x * x_sign - xt0;
    float y0 = floor(s0.y);
    float ytop = (s0.y == s1.y) ? ceil(s0.y) : (y0 + 1.0);
    float b = min((dy * c + dx * (ytop - s0.y)) * idxdy, ONE_MINUS_ULP);
    float robust_err = floor(a * ((float)(count) - 1.0) + b) - (float)count_x;
    if (robust_err != 0.0) {
        a -= ROBUST_EPSILON * sign(robust_err);
    }
    float x0 = xt0 * x_sign + (is_positive_slope ? 0.0 : -1.0);

    VelloPath path = paths[ln.path_ix];
    int4 bbox = (int4)path.bbox;
    float xmin = min(s0.x, s1.x);
    int stride = bbox.z - bbox.x;
    if (s0.y >= (float)bbox.w || s1.y <= (float)bbox.y || xmin >= (float)bbox.z || stride == 0) {
        return;
    }

    // Clip line to bounding box in "i" space
    uint imin = 0u;
    if (s0.y < (float)bbox.y) {
        float iminf = round(((float)bbox.y - y0 + b - a) / (1.0 - a)) - 1.0;
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

    // Vello reference (path_count.wgsl:123): delta = select(1, -1, is_down)
    int delta = is_down ? -1 : 1;
    int ymin_bd = 0;
    int ymax_bd = 0;

    if (max(s0.x, s1.x) <= (float)bbox.x) {
        ymin_bd = (int)ceil(s0.y);
        ymax_bd = (int)ceil(s1.y);
        imax = imin;
    } else {
        float fudge = is_positive_slope ? 1.0 : 0.0;
        if (xmin < (float)bbox.x) {
            float f = round((x_sign * ((float)bbox.x - x0) - b + fudge) / a);
            if (((x0 + x_sign * floor(a * f + b)) < (float)bbox.x) == is_positive_slope) {
                f += 1.0;
            }
            int ynext = (int)(y0 + f - floor(a * f + b) + 1.0);
            if (is_positive_slope) {
                if ((uint)f > imin) {
                    ymin_bd = (int)(y0 + ((y0 == s0.y) ? 0.0 : 1.0));
                    ymax_bd = ynext;
                    imin = (uint)f;
                }
            } else {
                if ((uint)f < imax) {
                    ymin_bd = ynext;
                    ymax_bd = (int)ceil(s1.y);
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

    // Backdrop for lines left of bbox
    ymin_bd = max(ymin_bd, bbox.y);
    ymax_bd = min(ymax_bd, bbox.w);
    for (int yb = ymin_bd; yb < ymax_bd; yb++) {
        int base = (int)path.tiles + (yb - bbox.y) * stride;
        int dummy;
        tile.InterlockedAdd(base * 8, delta, dummy); // backdrop at offset 0 within tile
    }

    // Allocate segment count entries
    uint seg_base;
    bump.InterlockedAdd(BUMP_SEG_COUNTS, imax - imin, seg_base);

    float last_z = floor(a * ((float)imin - 1.0) + b);

    for (uint i = imin; i < imax; i++) {
        float zf = a * (float)i + b;
        float z = floor(zf);
        int y = (int)(y0 + (float)i - z);
        int x = (int)(x0 + x_sign * z);
        int base = (int)path.tiles + (y - bbox.y) * stride - bbox.x;

        bool top_edge = (i == 0u) ? (y0 == s0.y) : (last_z == z);
        if (top_edge && x + 1 < bbox.z) {
            int x_bump = max(x + 1, bbox.x);
            int dummy;
            tile.InterlockedAdd((base + x_bump) * 8, delta, dummy);
        }

        // Atomic increment segment count for this tile
        uint seg_within_slice;
        tile.InterlockedAdd((base + x) * 8 + 4, 1u, seg_within_slice);

        // Pack counts
        uint counts = (seg_within_slice << 16u) | i;
        SegmentCount sc;
        sc.line_ix = line_ix;
        sc.counts = counts;
        uint seg_ix = seg_base + i - imin;
        if (seg_ix < seg_counts_size) {
            seg_counts[seg_ix] = sc;
        }

        last_z = z;
    }
}
