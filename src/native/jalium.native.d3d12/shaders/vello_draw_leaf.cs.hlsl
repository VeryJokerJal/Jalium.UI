// Vello GPU Pipeline V3 — draw_leaf
// Port of vello 0.10.0 shader/draw_leaf.wgsl.
// Finishes the prefix sum of drawtags and decodes draw objects into the info
// stream (gradient/image/blur transforms) and the clip input stream.
//
// Bindings: b0 config | t0 scene | t1 reduced | t2 path_bbox
//           u0 draw_monoid | u1 info (info+bin_data combined buffer) | u2 clip_inp
// Dispatch: (min(ceil(n_drawobj / 256), 256), 1, 1)

#include "vello_shared.hlsli"

StructuredBuffer<uint> scene : register(t0);
StructuredBuffer<DrawMonoid> reduced : register(t1);
StructuredBuffer<PathBbox> path_bbox : register(t2);

RWStructuredBuffer<DrawMonoid> draw_monoid : register(u0);
RWStructuredBuffer<uint> info : register(u1);
RWStructuredBuffer<ClipInp> clip_inp : register(u2);

#define WG_SIZE 256u
#define LG_WG_SIZE 8u

groupshared DrawMonoid sh_scratch[WG_SIZE];

uint read_draw_tag_from_scene(uint ix)
{
    uint tag_word;
    if (ix < n_drawobj) {
        uint tag_ix = drawtag_base + ix;
        tag_word = scene[tag_ix];
    } else {
        tag_word = DRAWTAG_NOP;
    }
    return tag_word;
}

Transform read_transform(uint base_offset, uint ix)
{
    uint base = base_offset + ix * 6u;
    float c0 = asfloat(scene[base]);
    float c1 = asfloat(scene[base + 1u]);
    float c2 = asfloat(scene[base + 2u]);
    float c3 = asfloat(scene[base + 3u]);
    float c4 = asfloat(scene[base + 4u]);
    float c5 = asfloat(scene[base + 5u]);
    Transform t;
    t.matrx = float4(c0, c1, c2, c3);
    t.translate = float2(c4, c5);
    return t;
}

Transform from_poly2(float2 p0, float2 p1)
{
    Transform t;
    t.matrx = float4(p1.y - p0.y, p0.x - p1.x, p1.x - p0.x, p1.y - p0.y);
    t.translate = float2(p0.x, p0.y);
    return t;
}

Transform two_point_to_unit_line(float2 p0, float2 p1)
{
    Transform tmp1 = from_poly2(p0, p1);
    Transform inv = transform_inverse(tmp1);
    Transform tmp2 = from_poly2(float2(0.0, 0.0), float2(1.0, 0.0));
    return transform_mul(tmp2, inv);
}

[numthreads(256, 1, 1)]
void main(uint3 local_id : SV_GroupThreadID, uint3 wg_id : SV_GroupID)
{
    // The first workgroup has an identity prefix and never reads `reduced`.
    // Avoid scanning 256 identities (and allow the host to omit draw_reduce
    // when only this workgroup runs). This branch is workgroup-uniform.
    DrawMonoid prefix = draw_monoid_identity();
    DrawMonoid agg = draw_monoid_identity();
    if (wg_id.x != 0u) {
        if (local_id.x < wg_id.x) {
            agg = reduced[local_id.x];
        }
        sh_scratch[local_id.x] = agg;
        for (uint i = 0u; i < LG_WG_SIZE; i += 1u) {
            GroupMemoryBarrierWithGroupSync();
            if (local_id.x + (1u << i) < WG_SIZE) {
                DrawMonoid other = sh_scratch[local_id.x + (1u << i)];
                agg = combine_draw_monoid(agg, other);
            }
            GroupMemoryBarrierWithGroupSync();
            sh_scratch[local_id.x] = agg;
        }
        GroupMemoryBarrierWithGroupSync();
        prefix = sh_scratch[0];
    }

    // This is the same division of work as draw_reduce.
    uint num_blocks_total = (n_drawobj + WG_SIZE - 1u) / WG_SIZE;
    uint n_blocks_base = num_blocks_total / WG_SIZE;
    uint remainder = num_blocks_total % WG_SIZE;
    uint first_block = n_blocks_base * wg_id.x + min(wg_id.x, remainder);
    uint n_blocks = n_blocks_base + (wg_id.x < remainder ? 1u : 0u);
    uint block_start = first_block * WG_SIZE;
    uint blocks_end = block_start + n_blocks * WG_SIZE;
    [loop]
    while (block_start != blocks_end) {
        uint ix = block_start + local_id.x;
        uint tag_word = read_draw_tag_from_scene(ix);
        agg = map_draw_tag(tag_word);
        GroupMemoryBarrierWithGroupSync();
        sh_scratch[local_id.x] = agg;
        for (uint i = 0u; i < LG_WG_SIZE; i += 1u) {
            GroupMemoryBarrierWithGroupSync();
            if (local_id.x >= (1u << i)) {
                DrawMonoid other = sh_scratch[local_id.x - (1u << i)];
                agg = combine_draw_monoid(other, agg);
            }
            GroupMemoryBarrierWithGroupSync();
            sh_scratch[local_id.x] = agg;
        }
        DrawMonoid m = prefix;
        GroupMemoryBarrierWithGroupSync();
        if (local_id.x > 0u) {
            m = combine_draw_monoid(m, sh_scratch[local_id.x - 1u]);
        }
        // m now contains exclusive prefix sum of draw monoid
        if (ix < n_drawobj) {
            draw_monoid[ix] = m;
        }
        uint dd = drawdata_base + m.scene_offset;
        uint di = m.info_offset;
        if (tag_word == DRAWTAG_FILL_COLOR || tag_word == DRAWTAG_FILL_LIN_GRADIENT ||
            tag_word == DRAWTAG_FILL_RAD_GRADIENT || tag_word == DRAWTAG_FILL_SWEEP_GRADIENT ||
            tag_word == DRAWTAG_FILL_IMAGE || tag_word == DRAWTAG_BEGIN_CLIP ||
            tag_word == DRAWTAG_BLURRED_ROUNDED_RECT)
        {
            PathBbox bbox = path_bbox[m.path_ix];
            Transform transform = (Transform)0;
            uint draw_flags = bbox.draw_flags;
            if (tag_word == DRAWTAG_FILL_LIN_GRADIENT || tag_word == DRAWTAG_FILL_RAD_GRADIENT ||
                tag_word == DRAWTAG_FILL_SWEEP_GRADIENT || tag_word == DRAWTAG_FILL_IMAGE ||
                tag_word == DRAWTAG_BLURRED_ROUNDED_RECT)
            {
                transform = read_transform(transform_base, bbox.trans_ix);
            }
            switch (tag_word) {
                case DRAWTAG_FILL_COLOR: {
                    info[di] = draw_flags;
                    break;
                }
                case DRAWTAG_BEGIN_CLIP: {
                    info[di] = draw_flags;
                    break;
                }
                case DRAWTAG_FILL_LIN_GRADIENT: {
                    info[di] = draw_flags;
                    float2 p0 = float2(asfloat(scene[dd + 1u]), asfloat(scene[dd + 2u]));
                    float2 p1 = float2(asfloat(scene[dd + 3u]), asfloat(scene[dd + 4u]));
                    p0 = transform_apply(transform, p0);
                    p1 = transform_apply(transform, p1);
                    float2 dxy = p1 - p0;
                    float scale = 1.0 / dot(dxy, dxy);
                    float2 line_xy = dxy * scale;
                    float line_c = -dot(p0, line_xy);
                    info[di + 1u] = asuint(line_xy.x);
                    info[di + 2u] = asuint(line_xy.y);
                    info[di + 3u] = asuint(line_c);
                    break;
                }
                case DRAWTAG_FILL_RAD_GRADIENT: {
                    // Two-point conical gradient implementation based
                    // on the algorithm at <https://skia.org/docs/dev/design/conical/>
                    // This epsilon matches what Skia uses
                    const float GRADIENT_EPSILON = 1.0 / (float)(1u << 12u);
                    info[di] = draw_flags;
                    float2 p0 = float2(asfloat(scene[dd + 1u]), asfloat(scene[dd + 2u]));
                    float2 p1 = float2(asfloat(scene[dd + 3u]), asfloat(scene[dd + 4u]));
                    float r0 = asfloat(scene[dd + 5u]);
                    float r1 = asfloat(scene[dd + 6u]);
                    Transform user_to_gradient = transform_inverse(transform);
                    // Output variables
                    Transform xform = (Transform)0;
                    float focal_x = 0.0;
                    float radius = 0.0;
                    uint kind = 0u;
                    uint flags = 0u;
                    if (abs(r0 - r1) <= GRADIENT_EPSILON) {
                        // When the radii are the same, emit a strip gradient
                        kind = RAD_GRAD_KIND_STRIP;
                        float scaled = r0 / distance(p0, p1);
                        xform = transform_mul(two_point_to_unit_line(p0, p1), user_to_gradient);
                        radius = scaled * scaled;
                    } else {
                        // Assume a two point conical gradient unless the centers
                        // are equal.
                        kind = RAD_GRAD_KIND_CONE;
                        if (all(p0 == p1)) {
                            kind = RAD_GRAD_KIND_CIRCULAR;
                            // Nudge p0 a bit to avoid denormals.
                            p0 += GRADIENT_EPSILON;
                        }
                        if (r1 == 0.0) {
                            // If r1 == 0.0, swap the points and radii
                            flags |= RAD_GRAD_SWAPPED;
                            float2 tmp_p = p0;
                            p0 = p1;
                            p1 = tmp_p;
                            float tmp_r = r0;
                            r0 = r1;
                            r1 = tmp_r;
                        }
                        focal_x = r0 / (r0 - r1);
                        float2 cf = (1.0 - focal_x) * p0 + focal_x * p1;
                        radius = r1 / (distance(cf, p1));
                        Transform user_to_unit_line =
                            transform_mul(two_point_to_unit_line(cf, p1), user_to_gradient);
                        Transform user_to_scaled = user_to_unit_line;
                        // When r == 1.0, focal point is on circle
                        if (abs(radius - 1.0) <= GRADIENT_EPSILON) {
                            kind = RAD_GRAD_KIND_FOCAL_ON_CIRCLE;
                            float scale = 0.5 * abs(1.0 - focal_x);
                            Transform scale_t;
                            scale_t.matrx = float4(scale, 0.0, 0.0, scale);
                            scale_t.translate = float2(0.0, 0.0);
                            user_to_scaled = transform_mul(scale_t, user_to_unit_line);
                        } else {
                            float a = radius * radius - 1.0;
                            float scale_ratio = abs(1.0 - focal_x) / a;
                            float scale_x = radius * scale_ratio;
                            float scale_y = sqrt(abs(a)) * scale_ratio;
                            Transform scale_t;
                            scale_t.matrx = float4(scale_x, 0.0, 0.0, scale_y);
                            scale_t.translate = float2(0.0, 0.0);
                            user_to_scaled = transform_mul(scale_t, user_to_unit_line);
                        }
                        xform = user_to_scaled;
                    }
                    info[di + 1u] = asuint(xform.matrx.x);
                    info[di + 2u] = asuint(xform.matrx.y);
                    info[di + 3u] = asuint(xform.matrx.z);
                    info[di + 4u] = asuint(xform.matrx.w);
                    info[di + 5u] = asuint(xform.translate.x);
                    info[di + 6u] = asuint(xform.translate.y);
                    info[di + 7u] = asuint(focal_x);
                    info[di + 8u] = asuint(radius);
                    info[di + 9u] = (flags << 3u) | kind;
                    break;
                }
                case DRAWTAG_FILL_SWEEP_GRADIENT: {
                    info[di] = draw_flags;
                    float2 p0 = float2(asfloat(scene[dd + 1u]), asfloat(scene[dd + 2u]));
                    Transform translate_t;
                    translate_t.matrx = float4(1.0, 0.0, 0.0, 1.0);
                    translate_t.translate = p0;
                    Transform xform = transform_mul(transform, translate_t);
                    Transform inv = transform_inverse(xform);
                    info[di + 1u] = asuint(inv.matrx.x);
                    info[di + 2u] = asuint(inv.matrx.y);
                    info[di + 3u] = asuint(inv.matrx.z);
                    info[di + 4u] = asuint(inv.matrx.w);
                    info[di + 5u] = asuint(inv.translate.x);
                    info[di + 6u] = asuint(inv.translate.y);
                    info[di + 7u] = scene[dd + 3u];
                    info[di + 8u] = scene[dd + 4u];
                    break;
                }
                case DRAWTAG_FILL_IMAGE: {
                    info[di] = draw_flags;
                    Transform inv = transform_inverse(transform);
                    info[di + 1u] = asuint(inv.matrx.x);
                    info[di + 2u] = asuint(inv.matrx.y);
                    info[di + 3u] = asuint(inv.matrx.z);
                    info[di + 4u] = asuint(inv.matrx.w);
                    info[di + 5u] = asuint(inv.translate.x);
                    info[di + 6u] = asuint(inv.translate.y);
                    info[di + 7u] = scene[dd];
                    info[di + 8u] = scene[dd + 1u];
                    info[di + 9u] = scene[dd + 2u];
                    break;
                }
                case DRAWTAG_BLURRED_ROUNDED_RECT: {
                    info[di] = draw_flags;
                    Transform inv = transform_inverse(transform);
                    info[di + 1u] = asuint(inv.matrx.x);
                    info[di + 2u] = asuint(inv.matrx.y);
                    info[di + 3u] = asuint(inv.matrx.z);
                    info[di + 4u] = asuint(inv.matrx.w);
                    info[di + 5u] = asuint(inv.translate.x);
                    info[di + 6u] = asuint(inv.translate.y);
                    info[di + 7u] = scene[dd + 1u];
                    info[di + 8u] = scene[dd + 2u];
                    info[di + 9u] = scene[dd + 3u];
                    info[di + 10u] = scene[dd + 4u];
                    break;
                }
                default: {
                    break;
                }
            }
        }
        if (tag_word == DRAWTAG_BEGIN_CLIP || tag_word == DRAWTAG_END_CLIP) {
            uint path_ix = ~ix;
            if (tag_word == DRAWTAG_BEGIN_CLIP) {
                path_ix = m.path_ix;
            }
            ClipInp ci;
            ci.ix = ix;
            ci.path_ix = int(path_ix);
            clip_inp[m.clip_ix] = ci;
        }
        block_start += WG_SIZE;
        // break here on end to save monoid aggregation?
        prefix = combine_draw_monoid(prefix, sh_scratch[WG_SIZE - 1u]);
    }
}
