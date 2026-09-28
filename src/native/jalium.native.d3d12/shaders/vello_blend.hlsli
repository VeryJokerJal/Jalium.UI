// Vello GPU Pipeline V3 — blend modes
// Faithful port of vello 0.10.0 shader/shared/blend.wgsl to HLSL.
// Only the fine stage includes this.

#ifndef VELLO_BLEND_HLSLI
#define VELLO_BLEND_HLSLI

// Color mixing modes

#define MIX_NORMAL      0u
#define MIX_MULTIPLY    1u
#define MIX_SCREEN      2u
#define MIX_OVERLAY     3u
#define MIX_DARKEN      4u
#define MIX_LIGHTEN     5u
#define MIX_COLOR_DODGE 6u
#define MIX_COLOR_BURN  7u
#define MIX_HARD_LIGHT  8u
#define MIX_SOFT_LIGHT  9u
#define MIX_DIFFERENCE  10u
#define MIX_EXCLUSION   11u
#define MIX_HUE         12u
#define MIX_SATURATION  13u
#define MIX_COLOR       14u
#define MIX_LUMINOSITY  15u
#define MIX_CLIP        128u

// Componentwise vector select — portable across fxc (no select intrinsic)
// and dxc HLSL2021 (no vector ?:). Returns t where c is true, else f.
float3 select3(bool3 c, float3 t, float3 f)
{
    return float3(c.x ? t.x : f.x, c.y ? t.y : f.y, c.z ? t.z : f.z);
}

float3 blend_screen(float3 cb, float3 cs)
{
    return cb + cs - (cb * cs);
}

float color_dodge(float cb, float cs)
{
    if (cb == 0.0) {
        return 0.0;
    } else if (cs == 1.0) {
        return 1.0;
    } else {
        return min(1.0, cb / (1.0 - cs));
    }
}

float color_burn(float cb, float cs)
{
    if (cb == 1.0) {
        return 1.0;
    } else if (cs == 0.0) {
        return 0.0;
    } else {
        return 1.0 - min(1.0, (1.0 - cb) / cs);
    }
}

float3 hard_light(float3 cb, float3 cs)
{
    bool3 lo = cs <= 0.5.xxx;
    float3 hi = blend_screen(cb, 2.0 * cs - 1.0);
    float3 lo_v = cb * 2.0 * cs;
    return select3(lo, lo_v, hi);
}

float3 soft_light(float3 cb, float3 cs)
{
    bool3 dark = cb <= 0.25.xxx;
    float3 d_hi = sqrt(cb);
    float3 d_lo = ((16.0 * cb - 12.0) * cb + 4.0) * cb;
    float3 d = select3(dark, d_lo, d_hi);
    bool3 lo = cs <= 0.5.xxx;
    float3 hi_v = cb + (2.0 * cs - 1.0) * (d - cb);
    float3 lo_v = cb - (1.0 - 2.0 * cs) * cb * (1.0 - cb);
    return select3(lo, lo_v, hi_v);
}

float color_sat(float3 c)
{
    return max(c.x, max(c.y, c.z)) - min(c.x, min(c.y, c.z));
}

float color_lum(float3 c)
{
    float3 f = float3(0.3, 0.59, 0.11);
    return dot(c, f);
}

float svg_lum(float3 c)
{
    float3 f = float3(0.2125, 0.7154, 0.0721);
    return dot(c, f);
}

float3 clip_color(float3 c_in)
{
    float3 c = c_in;
    float l = color_lum(c);
    float n = min(c.x, min(c.y, c.z));
    float x = max(c.x, max(c.y, c.z));
    if (n < 0.0) {
        c = l + (((c - l) * l) / (l - n));
    }
    if (x > 1.0) {
        c = l + (((c - l) * (1.0 - l)) / (x - l));
    }
    return c;
}

float3 set_lum(float3 c, float l)
{
    return clip_color(c + (l - color_lum(c)));
}

void set_sat_inner(inout float cmin, inout float cmid, inout float cmax, float s)
{
    if (cmax > cmin) {
        cmid = ((cmid - cmin) * s) / (cmax - cmin);
        cmax = s;
    } else {
        cmid = 0.0;
        cmax = 0.0;
    }
    cmin = 0.0;
}

float3 set_sat(float3 c, float s)
{
    float r = c.r;
    float g = c.g;
    float b = c.b;
    if (r <= g) {
        if (g <= b) {
            set_sat_inner(r, g, b, s);
        } else {
            if (r <= b) {
                set_sat_inner(r, b, g, s);
            } else {
                set_sat_inner(b, r, g, s);
            }
        }
    } else {
        if (r <= b) {
            set_sat_inner(g, r, b, s);
        } else {
            if (g <= b) {
                set_sat_inner(g, b, r, s);
            } else {
                set_sat_inner(b, g, r, s);
            }
        }
    }
    return float3(r, g, b);
}

// Blends two RGB colors together. The colors are assumed to be in sRGB
// color space, and this function does not take alpha into account.
float3 blend_mix(float3 cb, float3 cs, uint mode)
{
    float3 b = 0.0.xxx;
    switch (mode) {
        case MIX_MULTIPLY: {
            b = cb * cs;
            break;
        }
        case MIX_SCREEN: {
            b = blend_screen(cb, cs);
            break;
        }
        case MIX_OVERLAY: {
            b = hard_light(cs, cb);
            break;
        }
        case MIX_DARKEN: {
            b = min(cb, cs);
            break;
        }
        case MIX_LIGHTEN: {
            b = max(cb, cs);
            break;
        }
        case MIX_COLOR_DODGE: {
            b = float3(color_dodge(cb.x, cs.x), color_dodge(cb.y, cs.y), color_dodge(cb.z, cs.z));
            break;
        }
        case MIX_COLOR_BURN: {
            b = float3(color_burn(cb.x, cs.x), color_burn(cb.y, cs.y), color_burn(cb.z, cs.z));
            break;
        }
        case MIX_HARD_LIGHT: {
            b = hard_light(cb, cs);
            break;
        }
        case MIX_SOFT_LIGHT: {
            b = soft_light(cb, cs);
            break;
        }
        case MIX_DIFFERENCE: {
            b = abs(cb - cs);
            break;
        }
        case MIX_EXCLUSION: {
            b = cb + cs - 2.0 * cb * cs;
            break;
        }
        case MIX_HUE: {
            b = set_lum(set_sat(cs, color_sat(cb)), color_lum(cb));
            break;
        }
        case MIX_SATURATION: {
            b = set_lum(set_sat(cb, color_sat(cs)), color_lum(cb));
            break;
        }
        case MIX_COLOR: {
            b = set_lum(cs, color_lum(cb));
            break;
        }
        case MIX_LUMINOSITY: {
            b = set_lum(cb, color_lum(cs));
            break;
        }
        default: {
            b = cs;
            break;
        }
    }
    return b;
}

// Composition modes

#define COMPOSE_CLEAR        0u
#define COMPOSE_COPY         1u
#define COMPOSE_DEST         2u
#define COMPOSE_SRC_OVER     3u
#define COMPOSE_DEST_OVER    4u
#define COMPOSE_SRC_IN       5u
#define COMPOSE_DEST_IN      6u
#define COMPOSE_SRC_OUT      7u
#define COMPOSE_DEST_OUT     8u
#define COMPOSE_SRC_ATOP     9u
#define COMPOSE_DEST_ATOP    10u
#define COMPOSE_XOR          11u
#define COMPOSE_PLUS         12u
#define COMPOSE_PLUS_LIGHTER 13u

// Apply general compositing operation.
// Inputs are separated colors and alpha, output is premultiplied.
float4 blend_compose(float3 cb, float3 cs, float ab, float as_, uint compose_mode)
{
    float fa = 0.0;
    float fb = 0.0;
    switch (compose_mode) {
        case COMPOSE_COPY: {
            fa = 1.0;
            fb = 0.0;
            break;
        }
        case COMPOSE_DEST: {
            fa = 0.0;
            fb = 1.0;
            break;
        }
        case COMPOSE_SRC_OVER: {
            fa = 1.0;
            fb = 1.0 - as_;
            break;
        }
        case COMPOSE_DEST_OVER: {
            fa = 1.0 - ab;
            fb = 1.0;
            break;
        }
        case COMPOSE_SRC_IN: {
            fa = ab;
            fb = 0.0;
            break;
        }
        case COMPOSE_DEST_IN: {
            fa = 0.0;
            fb = as_;
            break;
        }
        case COMPOSE_SRC_OUT: {
            fa = 1.0 - ab;
            fb = 0.0;
            break;
        }
        case COMPOSE_DEST_OUT: {
            fa = 0.0;
            fb = 1.0 - as_;
            break;
        }
        case COMPOSE_SRC_ATOP: {
            fa = ab;
            fb = 1.0 - as_;
            break;
        }
        case COMPOSE_DEST_ATOP: {
            fa = 1.0 - ab;
            fb = as_;
            break;
        }
        case COMPOSE_XOR: {
            fa = 1.0 - ab;
            fb = 1.0 - as_;
            break;
        }
        case COMPOSE_PLUS: {
            fa = 1.0;
            fb = 1.0;
            break;
        }
        case COMPOSE_PLUS_LIGHTER: {
            return min(1.0.xxxx, float4(as_ * cs + ab * cb, as_ + ab));
        }
        default: {
            break;
        }
    }
    float as_fa = as_ * fa;
    float ab_fb = ab * fb;
    float3 co = as_fa * cs + ab_fb * cb;
    // Modes like COMPOSE_PLUS can generate alpha > 1.0, so clamp.
    return float4(co, min(as_fa + ab_fb, 1.0));
}

float3 unpremultiply(float4 color)
{
    float EPSILON = 1e-15;
    // Max with a small epsilon to avoid NaNs.
    float inv_alpha = 1.0 / max(color.a, EPSILON);
    return color.rgb * inv_alpha;
}

// Apply color mixing and composition. Both input and output colors are
// premultiplied RGB.
float4 blend_mix_compose(float4 backdrop, float4 src, uint mode)
{
    const uint BLEND_DEFAULT = ((MIX_NORMAL << 8u) | COMPOSE_SRC_OVER);
    if ((mode & 0x7fffu) == BLEND_DEFAULT) {
        // Both normal+src_over blend and clip case
        return backdrop * (1.0 - src.a) + src;
    }
    // Un-premultiply colors for blending.
    float3 cs = unpremultiply(src);
    float3 cb = unpremultiply(backdrop);
    uint mix_mode = mode >> 8u;
    float3 mixed = blend_mix(cb, cs, mix_mode);
    cs = lerp(cs, mixed, backdrop.a);
    uint compose_mode = mode & 0xffu;
    if (compose_mode == COMPOSE_SRC_OVER) {
        float3 co = lerp(backdrop.rgb, cs, src.a);
        return float4(co, src.a + backdrop.a * (1.0 - src.a));
    } else {
        return blend_compose(cb, cs, backdrop.a, src.a, compose_mode);
    }
}

#endif // VELLO_BLEND_HLSLI
