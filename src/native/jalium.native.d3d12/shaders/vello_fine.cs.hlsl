// Vello GPU Pipeline V3 — fine rasterizer (analytic area variant)
// Port of vello 0.10.0 shader/fine.wgsl compiled as `fine_area` (no msaa).
// Interprets each tile's PTCL, computing analytic-area anti-aliased coverage
// and compositing color/gradient/image/blur commands with full blend support.
//
// Includes the 0.9.0 half-pixel image sampling fix (#1606) and bicubic
// ImageQuality::High sampling (#1557).
//
// JALIUM DIVERGENCE: the final store keeps PREMULTIPLIED alpha (upstream
// un-premultiplies) because both Jalium composite paths (D3D12 bitmap quad,
// Vulkan SrcOver composite) expect premultiplied content.
//
// PTCL layout note: the first word of each tile's 64-word block is the
// blend-spill base offset, not a command; interpretation starts at +1.
// ptcl[0] == ~0u signals an earlier stage failure (fine does not bind bump).
//
// Bindings: b0 config | t0 segments | t1 ptcl | t2 info | u0 blend_spill |
//           u1 output | t3 gradients | t4 image_atlas
// Dispatch: (width_in_tiles, height_in_tiles, 1), workgroup (4, 16, 1)

#include "vello_shared.hlsli"
#include "vello_blend.hlsli"

StructuredBuffer<Segment> segments : register(t0);
StructuredBuffer<uint> ptcl : register(t1);
StructuredBuffer<uint> info : register(t2);

RWStructuredBuffer<uint> blend_spill : register(u0);
// The SPIR-V build annotates the storage image as Rgba8 so it matches the
// VK_FORMAT_R8G8B8A8_UNORM image view; fxc does not parse [[vk::...]]
// attributes, so the annotation is gated on the generator-only define.
#ifdef JALIUM_SPIRV
[[vk::image_format("rgba8")]]
#endif
RWTexture2D<unorm float4> output : register(u1);

Texture2D<float4> gradients : register(t3);
Texture2D<float4> image_atlas : register(t4);

#define GRADIENT_WIDTH 512

#define IMAGE_QUALITY_LOW 0u
#define IMAGE_QUALITY_MEDIUM 1u
#define IMAGE_QUALITY_HIGH 2u

#define LUMINANCE_MASK_LAYER 0x10000u

#define PIXELS_PER_THREAD 4u

float4 unpack4x8unorm_(uint v)
{
    return float4((float)(v & 0xffu), (float)((v >> 8u) & 0xffu),
                  (float)((v >> 16u) & 0xffu), (float)(v >> 24u)) * (1.0 / 255.0);
}

uint pack4x8unorm_(float4 v)
{
    float4 s = round(saturate(v) * 255.0);
    return (uint)s.x | ((uint)s.y << 8u) | ((uint)s.z << 16u) | ((uint)s.w << 24u);
}

float4 premul_alpha(float4 rgba)
{
    return float4(rgba.rgb * rgba.a, rgba.a);
}

// Error function approximation.
// https://raphlinus.github.io/graphics/2020/04/21/blurred-rounded-rects.html
float erf7(float x)
{
    // Clamp to prevent overflow. Intermediate steps calculate pow(x, 14).
    float y = clamp(x * 1.1283791671, -100.0, 100.0);
    float yy = y * y;
    float z = y + (0.24295 + (0.03395 + 0.0104 * yy) * yy) * (y * yy);
    return z / sqrt(1.0 + z * z);
}

float hypot_(float a, float b)
{
    return sqrt(a * a + b * b);
}

struct CmdFill
{
    uint size_and_rule;
    uint seg_data;
    int backdrop;
};

CmdFill read_fill(uint cmd_ix)
{
    CmdFill f;
    f.size_and_rule = ptcl[cmd_ix + 1u];
    f.seg_data = ptcl[cmd_ix + 2u];
    f.backdrop = (int)ptcl[cmd_ix + 3u];
    return f;
}

struct CmdBlurRect
{
    uint rgba_color;
    float4 matrx;
    float2 xlat;
    float width;
    float height;
    float radius;
    float std_dev;
};

CmdBlurRect read_blur_rect(uint cmd_ix)
{
    uint info_offset = ptcl[cmd_ix + 1u];
    CmdBlurRect b;
    b.rgba_color = ptcl[cmd_ix + 2u];
    b.matrx = float4(asfloat(info[info_offset]), asfloat(info[info_offset + 1u]),
                     asfloat(info[info_offset + 2u]), asfloat(info[info_offset + 3u]));
    b.xlat = float2(asfloat(info[info_offset + 4u]), asfloat(info[info_offset + 5u]));
    b.width = asfloat(info[info_offset + 6u]);
    b.height = asfloat(info[info_offset + 7u]);
    b.radius = asfloat(info[info_offset + 8u]);
    b.std_dev = asfloat(info[info_offset + 9u]);
    return b;
}

struct CmdLinGrad
{
    uint index;
    uint extend_mode_v;
    float line_x;
    float line_y;
    float line_c;
};

CmdLinGrad read_lin_grad(uint cmd_ix)
{
    uint index_mode = ptcl[cmd_ix + 1u];
    uint info_offset = ptcl[cmd_ix + 2u];
    CmdLinGrad g;
    g.index = index_mode >> 2u;
    g.extend_mode_v = index_mode & 0x3u;
    g.line_x = asfloat(info[info_offset]);
    g.line_y = asfloat(info[info_offset + 1u]);
    g.line_c = asfloat(info[info_offset + 2u]);
    return g;
}

struct CmdRadGrad
{
    uint index;
    uint extend_mode_v;
    float4 matrx;
    float2 xlat;
    float focal_x;
    float radius;
    uint kind;
    uint flags;
};

CmdRadGrad read_rad_grad(uint cmd_ix)
{
    uint index_mode = ptcl[cmd_ix + 1u];
    uint info_offset = ptcl[cmd_ix + 2u];
    CmdRadGrad g;
    g.index = index_mode >> 2u;
    g.extend_mode_v = index_mode & 0x3u;
    g.matrx = float4(asfloat(info[info_offset]), asfloat(info[info_offset + 1u]),
                     asfloat(info[info_offset + 2u]), asfloat(info[info_offset + 3u]));
    g.xlat = float2(asfloat(info[info_offset + 4u]), asfloat(info[info_offset + 5u]));
    g.focal_x = asfloat(info[info_offset + 6u]);
    g.radius = asfloat(info[info_offset + 7u]);
    uint flags_kind = info[info_offset + 8u];
    g.flags = flags_kind >> 3u;
    g.kind = flags_kind & 0x7u;
    return g;
}

struct CmdSweepGrad
{
    uint index;
    uint extend_mode_v;
    float4 matrx;
    float2 xlat;
    float t0;
    float t1;
};

CmdSweepGrad read_sweep_grad(uint cmd_ix)
{
    uint index_mode = ptcl[cmd_ix + 1u];
    uint info_offset = ptcl[cmd_ix + 2u];
    CmdSweepGrad g;
    g.index = index_mode >> 2u;
    g.extend_mode_v = index_mode & 0x3u;
    g.matrx = float4(asfloat(info[info_offset]), asfloat(info[info_offset + 1u]),
                     asfloat(info[info_offset + 2u]), asfloat(info[info_offset + 3u]));
    g.xlat = float2(asfloat(info[info_offset + 4u]), asfloat(info[info_offset + 5u]));
    g.t0 = asfloat(info[info_offset + 6u]);
    g.t1 = asfloat(info[info_offset + 7u]);
    return g;
}

struct CmdImage
{
    float4 matrx;
    float2 xlat;
    float2 atlas_offset;
    float2 extents;
    uint format;
    uint x_extend_mode;
    uint y_extend_mode;
    uint quality;
    float alpha;
    uint alpha_type;
};

CmdImage read_image(uint cmd_ix)
{
    uint info_offset = ptcl[cmd_ix + 1u];
    CmdImage im;
    im.matrx = float4(asfloat(info[info_offset]), asfloat(info[info_offset + 1u]),
                      asfloat(info[info_offset + 2u]), asfloat(info[info_offset + 3u]));
    im.xlat = float2(asfloat(info[info_offset + 4u]), asfloat(info[info_offset + 5u]));
    uint xy = info[info_offset + 6u];
    uint width_height = info[info_offset + 7u];
    uint sample_alpha = info[info_offset + 8u];
    im.alpha = (float)(sample_alpha & 0xFFu) / 255.0;
    im.format = sample_alpha >> 15u;
    im.alpha_type = (sample_alpha >> 14u) & 0x1u;
    im.quality = (sample_alpha >> 12u) & 0x3u;
    im.x_extend_mode = (sample_alpha >> 10u) & 0x3u;
    im.y_extend_mode = (sample_alpha >> 8u) & 0x3u;
    // The following are not intended to be bitcasts
    im.atlas_offset = float2((float)(xy >> 16u), (float)(xy & 0xffffu));
    im.extents = float2((float)(width_height >> 16u), (float)(width_height & 0xffffu));
    return im;
}

struct CmdEndClip
{
    uint blend;
    float alpha;
};

CmdEndClip read_end_clip(uint cmd_ix)
{
    CmdEndClip e;
    e.blend = ptcl[cmd_ix + 1u];
    e.alpha = asfloat(ptcl[cmd_ix + 2u]);
    return e;
}

#define PIXEL_FORMAT_RGBA 0u
#define PIXEL_FORMAT_BGRA 1u

// Normalises subpixel order loaded from an image, based on the image's format.
float4 pixel_format(float4 pixel, uint format)
{
    if (format == PIXEL_FORMAT_BGRA) {
        // The conversion from RGBA to BGRA is its own inverse.
        return pixel.bgra;
    }
    return pixel;
}

#define ALPHA_TYPE_ALPHA 0u
#define ALPHA_TYPE_PREMULTIPLIED 1u

// Premultiplies alpha if not already
float4 maybe_premul_alpha(float4 pixel, uint alpha_type)
{
    if (alpha_type == ALPHA_TYPE_PREMULTIPLIED) {
        return pixel;
    }
    return premul_alpha(pixel);
}

float extend_mode_normalized(float t, uint mode)
{
    switch (mode) {
        case EXTEND_PAD: {
            return clamp(t, 0.0, 1.0);
        }
        case EXTEND_REPEAT: {
            return frac(t);
        }
        default: {
            return abs(t - 2.0 * round(0.5 * t));
        }
    }
}

float apply_extend_mode(float t, uint mode, float max_v)
{
    switch (mode) {
        case EXTEND_PAD: {
            return clamp(t, 0.0, max_v);
        }
        default: {
            return extend_mode_normalized(t / max_v, mode) * max_v;
        }
    }
}

// Cubic resampler logic borrowed from Skia.
// Mitchell-Netravali cubic filter coefficients with parameters B=1/3 and C=1/3
static const float4 MF[4] = {
    float4((1.0 / 6.0) / 3.0,
           -(3.0 / 6.0) / 3.0 - 1.0 / 3.0,
           (3.0 / 6.0) / 3.0 + 2.0 * 1.0 / 3.0,
           -(1.0 / 6.0) / 3.0 - 1.0 / 3.0),
    float4(1.0 - (2.0 / 6.0) / 3.0,
           0.0,
           -3.0 + (12.0 / 6.0) / 3.0 + 1.0 / 3.0,
           2.0 - (9.0 / 6.0) / 3.0 - 1.0 / 3.0),
    float4((1.0 / 6.0) / 3.0,
           (3.0 / 6.0) / 3.0 + 1.0 / 3.0,
           3.0 - (15.0 / 6.0) / 3.0 - 2.0 * 1.0 / 3.0,
           -2.0 + (9.0 / 6.0) / 3.0 + 1.0 / 3.0),
    float4(0.0,
           0.0,
           -1.0 / 3.0,
           (1.0 / 6.0) / 3.0 + 1.0 / 3.0)
};

float single_weight(float t, float a, float b, float c, float d)
{
    return t * (t * (t * d + c) + b) + a;
}

float4 cubic_weights(float fract_v)
{
    return float4(single_weight(fract_v, MF[0][0], MF[0][1], MF[0][2], MF[0][3]),
                  single_weight(fract_v, MF[1][0], MF[1][1], MF[1][2], MF[1][3]),
                  single_weight(fract_v, MF[2][0], MF[2][1], MF[2][2], MF[2][3]),
                  single_weight(fract_v, MF[3][0], MF[3][1], MF[3][2], MF[3][3]));
}

float4 atlas_tap(float2 coords, float2 offs, float2 atlas_offset, float2 atlas_max, uint alpha_type)
{
    float2 p = clamp(coords + offs, atlas_offset, atlas_max);
    return maybe_premul_alpha(image_atlas.Load(int3((int2)p, 0)), alpha_type);
}

// Bicubic filtering using Mitchell filter with B=1/3, C=1/3.
// Each tap is premultiplied before filtering, matching the bilinear path.
float4 bicubic_sample(float2 coords, float2 atlas_offset, float2 atlas_max, uint alpha_type)
{
    float2 frac_coords = frac(coords + float2(0.5, 0.5));
    float4 cx = cubic_weights(frac_coords.x);
    float4 cy = cubic_weights(frac_coords.y);

    float4 s00 = atlas_tap(coords, float2(-1.5, -1.5), atlas_offset, atlas_max, alpha_type);
    float4 s10 = atlas_tap(coords, float2(-0.5, -1.5), atlas_offset, atlas_max, alpha_type);
    float4 s20 = atlas_tap(coords, float2(0.5, -1.5), atlas_offset, atlas_max, alpha_type);
    float4 s30 = atlas_tap(coords, float2(1.5, -1.5), atlas_offset, atlas_max, alpha_type);

    float4 s01 = atlas_tap(coords, float2(-1.5, -0.5), atlas_offset, atlas_max, alpha_type);
    float4 s11 = atlas_tap(coords, float2(-0.5, -0.5), atlas_offset, atlas_max, alpha_type);
    float4 s21 = atlas_tap(coords, float2(0.5, -0.5), atlas_offset, atlas_max, alpha_type);
    float4 s31 = atlas_tap(coords, float2(1.5, -0.5), atlas_offset, atlas_max, alpha_type);

    float4 s02 = atlas_tap(coords, float2(-1.5, 0.5), atlas_offset, atlas_max, alpha_type);
    float4 s12 = atlas_tap(coords, float2(-0.5, 0.5), atlas_offset, atlas_max, alpha_type);
    float4 s22 = atlas_tap(coords, float2(0.5, 0.5), atlas_offset, atlas_max, alpha_type);
    float4 s32 = atlas_tap(coords, float2(1.5, 0.5), atlas_offset, atlas_max, alpha_type);

    float4 s03 = atlas_tap(coords, float2(-1.5, 1.5), atlas_offset, atlas_max, alpha_type);
    float4 s13 = atlas_tap(coords, float2(-0.5, 1.5), atlas_offset, atlas_max, alpha_type);
    float4 s23 = atlas_tap(coords, float2(0.5, 1.5), atlas_offset, atlas_max, alpha_type);
    float4 s33 = atlas_tap(coords, float2(1.5, 1.5), atlas_offset, atlas_max, alpha_type);

    // Interpolate in x direction for each row
    float4 row0 = cx.x * s00 + cx.y * s10 + cx.z * s20 + cx.w * s30;
    float4 row1 = cx.x * s01 + cx.y * s11 + cx.z * s21 + cx.w * s31;
    float4 row2 = cx.x * s02 + cx.y * s12 + cx.z * s22 + cx.w * s32;
    float4 row3 = cx.x * s03 + cx.y * s13 + cx.z * s23 + cx.w * s33;
    // Interpolate in y direction
    float4 result = cy.x * row0 + cy.y * row1 + cy.z * row2 + cy.w * row3;

    // Clamp alpha first, then clamp premultiplied color channels against it.
    float a = clamp(result.a, 0.0, 1.0);
    return float4(clamp(result.rgb, 0.0.xxx, a.xxx), a);
}

// Analytic area anti-aliasing.
void fill_path(CmdFill fill, float2 xy, inout float area[PIXELS_PER_THREAD])
{
    uint n_segs = fill.size_and_rule >> 1u;
    bool even_odd = (fill.size_and_rule & 1u) != 0u;
    float backdrop_f = (float)fill.backdrop;
    for (uint iz = 0u; iz < PIXELS_PER_THREAD; iz += 1u) {
        area[iz] = backdrop_f;
    }
    [loop]
    for (uint i = 0u; i < n_segs; i++) {
        uint seg_off = fill.seg_data + i;
        Segment segment = segments[seg_off];
        float y = segment.point0.y - xy.y;
        float2 delta = segment.point1 - segment.point0;
        float y0 = clamp(y, 0.0, 1.0);
        float y1 = clamp(y + delta.y, 0.0, 1.0);
        float dy = y0 - y1;
        if (dy != 0.0) {
            float vec_y_recip = 1.0 / delta.y;
            float t0 = (y0 - y) * vec_y_recip;
            float t1 = (y1 - y) * vec_y_recip;
            float startx = segment.point0.x - xy.x;
            float x0 = startx + t0 * delta.x;
            float x1 = startx + t1 * delta.x;
            float xmin0 = min(x0, x1);
            float xmax0 = max(x0, x1);
            for (uint j = 0u; j < PIXELS_PER_THREAD; j += 1u) {
                float i_f = (float)j;
                float xmin = min(xmin0 - i_f, 1.0) - 1.0e-6;
                float xmax = xmax0 - i_f;
                float b = min(xmax, 1.0);
                float c = max(b, 0.0);
                float d = max(xmin, 0.0);
                float a = (b + 0.5 * (d * d - c * c) - xmin) / (xmax - xmin);
                area[j] += a * dy;
            }
        }
        float y_edge = sign(delta.x) * clamp(xy.y - segment.y_edge + 1.0, 0.0, 1.0);
        for (uint k = 0u; k < PIXELS_PER_THREAD; k += 1u) {
            area[k] += y_edge;
        }
    }
    if (even_odd) {
        // even-odd winding rule
        for (uint m = 0u; m < PIXELS_PER_THREAD; m += 1u) {
            float a = area[m];
            area[m] = abs(a - 2.0 * round(0.5 * a));
        }
    } else {
        // non-zero winding rule
        for (uint m = 0u; m < PIXELS_PER_THREAD; m += 1u) {
            area[m] = min(abs(area[m]), 1.0);
        }
    }
}

// The X size should be 16 / PIXELS_PER_THREAD
[numthreads(4, 16, 1)]
void main(uint3 global_id : SV_DispatchThreadID, uint3 local_id : SV_GroupThreadID,
          uint3 wg_id : SV_GroupID)
{
    if (ptcl[0] == ~0u) {
        // An earlier stage has failed, don't try to render.
        // We use ptcl[0] for this so we don't use up a binding for bump.
        return;
    }
    float2 xy = float2((float)(global_id.x * PIXELS_PER_THREAD), (float)global_id.y);
    float2 local_xy = float2((float)(local_id.x * PIXELS_PER_THREAD), (float)local_id.y);
    float4 rgba[PIXELS_PER_THREAD];
    float4 base_color_v = unpack4x8unorm_(base_color);
    for (uint ib = 0u; ib < PIXELS_PER_THREAD; ib += 1u) {
        rgba[ib] = base_color_v;
    }
    uint blend_stack[BLEND_STACK_SPLIT][PIXELS_PER_THREAD];
    uint clip_depth = 0u;
    float area[PIXELS_PER_THREAD];
    for (uint ia = 0u; ia < PIXELS_PER_THREAD; ia += 1u) {
        area[ia] = 0.0;
    }
    uint tile_ix = wg_id.y * width_in_tiles + wg_id.x;
    uint cmd_ix = tile_ix * PTCL_INITIAL_ALLOC;
    uint blend_offset = ptcl[cmd_ix];
    cmd_ix += 1u;
    // main interpretation loop
    [loop]
    for (;;) {
        uint tag = ptcl[cmd_ix];
        if (tag == CMD_END) {
            break;
        }
        switch (tag) {
            case CMD_FILL: {
                CmdFill fill = read_fill(cmd_ix);
                fill_path(fill, local_xy, area);
                cmd_ix += 4u;
                break;
            }
            case CMD_SOLID: {
                for (uint i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    area[i] = 1.0;
                }
                cmd_ix += 1u;
                break;
            }
            case CMD_COLOR: {
                uint rgba_color = ptcl[cmd_ix + 1u];
                float4 fg = unpack4x8unorm_(rgba_color);
                for (uint i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    float4 fg_i = fg * area[i];
                    rgba[i] = rgba[i] * (1.0 - fg_i.a) + fg_i;
                }
                cmd_ix += 2u;
                break;
            }
            case CMD_BEGIN_CLIP: {
                if (clip_depth < BLEND_STACK_SPLIT) {
                    for (uint i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                        blend_stack[clip_depth][i] = pack4x8unorm_(rgba[i]);
                        rgba[i] = 0.0.xxxx;
                    }
                } else {
                    uint blend_in_scratch = clip_depth - BLEND_STACK_SPLIT;
                    uint local_tile_ix = local_id.x * PIXELS_PER_THREAD + local_id.y * TILE_WIDTH;
                    uint local_blend_start =
                        blend_offset + blend_in_scratch * TILE_WIDTH * TILE_HEIGHT + local_tile_ix;
                    for (uint i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                        blend_spill[local_blend_start + i] = pack4x8unorm_(rgba[i]);
                        rgba[i] = 0.0.xxxx;
                    }
                }
                clip_depth += 1u;
                cmd_ix += 1u;
                break;
            }
            case CMD_END_CLIP: {
                CmdEndClip end_clip = read_end_clip(cmd_ix);
                clip_depth -= 1u;
                for (uint i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    uint bg_rgba;
                    if (clip_depth < BLEND_STACK_SPLIT) {
                        bg_rgba = blend_stack[clip_depth][i];
                    } else {
                        uint blend_in_scratch = clip_depth - BLEND_STACK_SPLIT;
                        uint local_tile_ix =
                            local_id.x * PIXELS_PER_THREAD + local_id.y * TILE_WIDTH;
                        uint local_blend_start = blend_offset +
                            blend_in_scratch * TILE_WIDTH * TILE_HEIGHT + local_tile_ix;
                        bg_rgba = blend_spill[local_blend_start + i];
                    }
                    float4 bg = unpack4x8unorm_(bg_rgba);
                    float4 fg = rgba[i] * area[i] * end_clip.alpha;
                    if (end_clip.blend == LUMINANCE_MASK_LAYER) {
                        if (area[i] == 0.0) {
                            rgba[i] = bg;
                            continue;
                        }
                        float luminance = clamp(svg_lum(unpremultiply(fg)) * fg.a, 0.0, 1.0);
                        rgba[i] = bg * luminance;
                    } else {
                        rgba[i] = blend_mix_compose(bg, fg, end_clip.blend);
                    }
                }
                cmd_ix += 3u;
                break;
            }
            case CMD_JUMP: {
                cmd_ix = ptcl[cmd_ix + 1u];
                break;
            }
            case CMD_BLUR_RECT: {
                // Approximation for the convolution of a gaussian filter with
                // a rounded rectangle.
                // https://raphlinus.github.io/graphics/2020/04/21/blurred-rounded-rects.html
                CmdBlurRect blur = read_blur_rect(cmd_ix);

                // Avoid division by 0
                float std_dev = max(blur.std_dev, 1e-5);
                float inv_std_dev = 1.0 / std_dev;

                float min_edge = min(blur.width, blur.height);
                float radius_max = 0.5 * min_edge;
                float r0 = min(hypot_(blur.radius, std_dev * 1.15), radius_max);
                float r1 = min(hypot_(blur.radius, std_dev * 2.0), radius_max);

                float exponent = 2.0 * r1 / r0;
                float inv_exponent = 1.0 / exponent;

                // Pull in long end (make less eccentric).
                float delta = 1.25 * std_dev *
                    (exp(-pow(0.5 * inv_std_dev * blur.width, 2.0)) -
                     exp(-pow(0.5 * inv_std_dev * blur.height, 2.0)));
                float width_v = blur.width + min(delta, 0.0);
                float height_v = blur.height - max(delta, 0.0);

                float scale =
                    0.5 * erf7(inv_std_dev * 0.5 * (max(width_v, height_v) - 0.5 * blur.radius));

                float4 blur_rgba = unpack4x8unorm_(blur.rgba_color);

                for (uint i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    // Transform fragment location to local 'uv' space of the
                    // rounded rectangle.
                    float2 my_xy = float2(xy.x + (float)i, xy.y);
                    float2 loc_xy = blur.matrx.xy * my_xy.x + blur.matrx.zw * my_xy.y + blur.xlat;
                    float x = loc_xy.x;
                    float y = loc_xy.y;

                    float y0 = abs(y) - (height_v * 0.5 - r1);
                    float y1 = max(y0, 0.0);

                    float x0 = abs(x) - (width_v * 0.5 - r1);
                    float x1 = max(x0, 0.0);

                    float d_pos = pow(pow(x1, exponent) + pow(y1, exponent), inv_exponent);
                    float d_neg = min(max(x0, y0), 0.0);
                    float d = d_pos + d_neg - r1;
                    float alpha =
                        scale * (erf7(inv_std_dev * (min_edge + d)) - erf7(inv_std_dev * d));

                    float4 fg_rgba = blur_rgba * alpha;
                    float4 fg_i = fg_rgba * area[i];
                    rgba[i] = rgba[i] * (1.0 - fg_i.a) + fg_i;
                }
                cmd_ix += 3u;
                break;
            }
            case CMD_LIN_GRAD: {
                CmdLinGrad lin = read_lin_grad(cmd_ix);
                float d = lin.line_x * xy.x + lin.line_y * xy.y + lin.line_c;
                for (uint i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    float my_d = d + lin.line_x * (float)i;
                    int x = (int)round(extend_mode_normalized(my_d, lin.extend_mode_v) *
                                       (float)(GRADIENT_WIDTH - 1));
                    float4 fg_rgba = gradients.Load(int3(x, (int)lin.index, 0));
                    float4 fg_i = fg_rgba * area[i];
                    rgba[i] = rgba[i] * (1.0 - fg_i.a) + fg_i;
                }
                cmd_ix += 3u;
                break;
            }
            case CMD_RAD_GRAD: {
                CmdRadGrad rad = read_rad_grad(cmd_ix);
                float focal_x = rad.focal_x;
                float radius = rad.radius;
                bool is_strip = rad.kind == RAD_GRAD_KIND_STRIP;
                bool is_circular = rad.kind == RAD_GRAD_KIND_CIRCULAR;
                bool is_focal_on_circle = rad.kind == RAD_GRAD_KIND_FOCAL_ON_CIRCLE;
                bool is_swapped = (rad.flags & RAD_GRAD_SWAPPED) != 0u;
                float r1_recip = is_circular ? 0.0 : (1.0 / radius);
                float less_scale = (is_swapped || (1.0 - focal_x) < 0.0) ? -1.0 : 1.0;
                float t_sign = sign(1.0 - focal_x);
                for (uint i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    float2 my_xy = float2(xy.x + (float)i, xy.y);
                    float2 loc_xy = rad.matrx.xy * my_xy.x + rad.matrx.zw * my_xy.y + rad.xlat;
                    float x = loc_xy.x;
                    float y = loc_xy.y;
                    float xx = x * x;
                    float yy = y * y;
                    float t = 0.0;
                    bool is_valid = true;
                    if (is_strip) {
                        float a = radius - yy;
                        t = sqrt(a) + x;
                        is_valid = a >= 0.0;
                    } else if (is_focal_on_circle) {
                        t = (xx + yy) / x;
                        is_valid = t >= 0.0 && x != 0.0;
                    } else if (radius > 1.0) {
                        t = sqrt(xx + yy) - x * r1_recip;
                    } else { // radius < 1.0
                        float a = xx - yy;
                        t = less_scale * sqrt(a) - x * r1_recip;
                        is_valid = a >= 0.0 && t >= 0.0;
                    }
                    if (is_valid) {
                        t = extend_mode_normalized(focal_x + t_sign * t, rad.extend_mode_v);
                        t = is_swapped ? (1.0 - t) : t;
                        int rx = (int)round(t * (float)(GRADIENT_WIDTH - 1));
                        float4 fg_rgba = gradients.Load(int3(rx, (int)rad.index, 0));
                        float4 fg_i = fg_rgba * area[i];
                        rgba[i] = rgba[i] * (1.0 - fg_i.a) + fg_i;
                    }
                }
                cmd_ix += 3u;
                break;
            }
            case CMD_SWEEP_GRAD: {
                CmdSweepGrad sweep = read_sweep_grad(cmd_ix);
                float scale = 1.0 / (sweep.t1 - sweep.t0);
                for (uint i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    float2 my_xy = float2(xy.x + (float)i, xy.y);
                    float2 loc_xy = sweep.matrx.xy * my_xy.x + sweep.matrx.zw * my_xy.y + sweep.xlat;
                    float x = loc_xy.x;
                    float y = loc_xy.y;
                    // xy_to_unit_angle from Skia; 7th degree polynomial
                    // approximation to atan, generated with sollya.
                    float xabs = abs(x);
                    float yabs = abs(y);
                    float slope = min(xabs, yabs) / max(xabs, yabs);
                    float s = slope * slope;
                    float phi = slope *
                        (0.15912117063999176025390625 +
                         s * (-5.185396969318389892578125e-2 +
                              s * (2.476101927459239959716796875e-2 +
                                   s * (-7.0547382347285747528076171875e-3))));
                    phi = (xabs < yabs) ? (1.0 / 4.0 - phi) : phi;
                    phi = (x < 0.0) ? (1.0 / 2.0 - phi) : phi;
                    phi = (y < 0.0) ? (1.0 - phi) : phi;
                    phi = (phi != phi) ? 0.0 : phi; // check for NaN
                    phi = (phi - sweep.t0) * scale;
                    float t = extend_mode_normalized(phi, sweep.extend_mode_v);
                    int ramp_x = (int)round(t * (float)(GRADIENT_WIDTH - 1));
                    float4 fg_rgba = gradients.Load(int3(ramp_x, (int)sweep.index, 0));
                    float4 fg_i = fg_rgba * area[i];
                    rgba[i] = rgba[i] * (1.0 - fg_i.a) + fg_i;
                }
                cmd_ix += 3u;
                break;
            }
            case CMD_IMAGE: {
                CmdImage image = read_image(cmd_ix);
                float2 atlas_max = image.atlas_offset + image.extents - float2(1.0, 1.0);
                if (image.quality == IMAGE_QUALITY_LOW) {
                    for (uint i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                        // We only need to load from the textures if the value
                        // will be used.
                        if (area[i] != 0.0) {
                            // Use pixel centers (+0.5) rather than pixel
                            // corners for correct sampling
                            float2 my_xy = float2(xy.x + (float)i + 0.5, xy.y + 0.5);
                            float2 atlas_uv =
                                image.matrx.xy * my_xy.x + image.matrx.zw * my_xy.y + image.xlat;
                            atlas_uv.x =
                                apply_extend_mode(atlas_uv.x, image.x_extend_mode, image.extents.x);
                            atlas_uv.y =
                                apply_extend_mode(atlas_uv.y, image.y_extend_mode, image.extents.y);
                            atlas_uv = atlas_uv + image.atlas_offset;
                            float2 atlas_uv_clamped =
                                clamp(atlas_uv, image.atlas_offset, atlas_max);
                            // Nearest neighbor sampling
                            float4 fg_rgba = maybe_premul_alpha(
                                image_atlas.Load(int3((int2)atlas_uv_clamped, 0)),
                                image.alpha_type);
                            float4 fg_i =
                                pixel_format(fg_rgba * area[i] * image.alpha, image.format);
                            rgba[i] = rgba[i] * (1.0 - fg_i.a) + fg_i;
                        }
                    }
                } else if (image.quality == IMAGE_QUALITY_HIGH) {
                    for (uint i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                        if (area[i] != 0.0) {
                            // Use pixel centers (+0.5) rather than pixel
                            // corners for correct sampling
                            float2 my_xy = float2(xy.x + (float)i + 0.5, xy.y + 0.5);
                            float2 atlas_uv =
                                image.matrx.xy * my_xy.x + image.matrx.zw * my_xy.y + image.xlat;
                            atlas_uv.x =
                                apply_extend_mode(atlas_uv.x, image.x_extend_mode, image.extents.x);
                            atlas_uv.y =
                                apply_extend_mode(atlas_uv.y, image.y_extend_mode, image.extents.y);
                            atlas_uv = atlas_uv + image.atlas_offset;
                            float4 fg_rgba =
                                bicubic_sample(atlas_uv, image.atlas_offset, atlas_max,
                                               image.alpha_type);
                            float4 fg_i =
                                pixel_format(fg_rgba * area[i] * image.alpha, image.format);
                            rgba[i] = rgba[i] * (1.0 - fg_i.a) + fg_i;
                        }
                    }
                } else { // IMAGE_QUALITY_MEDIUM (default)
                    for (uint i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                        if (area[i] != 0.0) {
                            // Use pixel centers (+0.5) rather than pixel
                            // corners for correct sampling
                            float2 my_xy = float2(xy.x + (float)i + 0.5, xy.y + 0.5);
                            float2 atlas_uv =
                                image.matrx.xy * my_xy.x + image.matrx.zw * my_xy.y + image.xlat;
                            atlas_uv.x =
                                apply_extend_mode(atlas_uv.x, image.x_extend_mode, image.extents.x);
                            atlas_uv.y =
                                apply_extend_mode(atlas_uv.y, image.y_extend_mode, image.extents.y);
                            atlas_uv = atlas_uv + image.atlas_offset - float2(0.5, 0.5);
                            float2 atlas_uv_clamped =
                                clamp(atlas_uv, image.atlas_offset, atlas_max);
                            // We know that the floor and ceil are within the
                            // atlas area because atlas_max and atlas_offset
                            // are integers
                            float4 uv_quad = float4(floor(atlas_uv_clamped), ceil(atlas_uv_clamped));
                            float2 uv_frac = frac(atlas_uv);
                            float4 a = maybe_premul_alpha(
                                image_atlas.Load(int3((int2)uv_quad.xy, 0)), image.alpha_type);
                            float4 b = maybe_premul_alpha(
                                image_atlas.Load(int3((int2)uv_quad.xw, 0)), image.alpha_type);
                            float4 c = maybe_premul_alpha(
                                image_atlas.Load(int3((int2)uv_quad.zy, 0)), image.alpha_type);
                            float4 dd = maybe_premul_alpha(
                                image_atlas.Load(int3((int2)uv_quad.zw, 0)), image.alpha_type);
                            // Bilinear sampling
                            float4 fg_rgba =
                                lerp(lerp(a, b, uv_frac.y), lerp(c, dd, uv_frac.y), uv_frac.x);
                            float4 fg_i =
                                pixel_format(fg_rgba * area[i] * image.alpha, image.format);
                            rgba[i] = rgba[i] * (1.0 - fg_i.a) + fg_i;
                        }
                    }
                }
                cmd_ix += 2u;
                break;
            }
            default: {
                break;
            }
        }
    }
    uint2 xy_uint = (uint2)xy;
    for (uint io = 0u; io < PIXELS_PER_THREAD; io += 1u) {
        uint2 coords = xy_uint + uint2(io, 0u);
        if (coords.x < target_width && coords.y < target_height) {
            float4 fg = rgba[io];
            // JALIUM DIVERGENCE: keep premultiplied alpha (upstream divides by
            // alpha here) — Jalium's composite paths expect premultiplied.
            output[(int2)coords] = fg;
        }
    }
}
