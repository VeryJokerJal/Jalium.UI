#pragma once

namespace jalium {

// Core renderer MSL. Built-in production packages compile the same source to
// metallib at build time; newLibraryWithSource keeps native tests and source
// checkouts self-contained and is also the fallback when the packaged library
// version does not match the C++ parameter ABI.
inline constexpr const char* kMetalCoreShaderSource = R"METAL(
#include <metal_stdlib>
using namespace metal;

struct VertexIn { packed_float4 data; };
struct VertexOut {
    float4 position [[position]];
    float2 uv [[user(locn0)]];
    float2 pixel;
};

vertex VertexOut jalium_vertex(
    const device VertexIn* vertices [[buffer(0)]],
    constant float* p [[buffer(1)]],
    uint vid [[vertex_id]])
{
    const float4 v = float4(vertices[vid].data);
    const float2 viewport = max(float2(p[0], p[1]), float2(1.0));
    VertexOut out;
    out.position = float4(v.x / viewport.x * 2.0 - 1.0,
                          1.0 - v.y / viewport.y * 2.0, 0.0, 1.0);
    out.uv = v.zw;
    out.pixel = v.xy;
    return out;
}

float spread_value(float t, uint spread)
{
    if (spread == 1u) return fract(t);
    if (spread == 2u) {
        float q = fmod(abs(t), 2.0);
        return q <= 1.0 ? q : 2.0 - q;
    }
    return clamp(t, 0.0, 1.0);
}

float4 sample_gradient(constant float* p, float2 local)
{
    const uint type = uint(max(p[3], 0.0));
    float t = 0.0;
    if (type == 1u) {
        float2 a = float2(p[36], p[37]);
        float2 b = float2(p[38], p[39]);
        float2 d = b - a;
        t = dot(local - a, d) / max(dot(d, d), 1e-6);
    } else {
        float2 center = float2(p[36], p[37]);
        float2 radius = max(abs(float2(p[38], p[39])), float2(1e-4));
        float2 origin = float2(p[40], p[41]);
        // A focal radial gradient is approximated by shifting the normalized
        // sample origin; this matches the common D3D/Vulkan UI case and keeps
        // the complete stop/spread contract on the GPU.
        float2 q = (local - mix(origin, center, 0.5)) / radius;
        t = length(q);
    }
    t = spread_value(t, uint(max(p[44], 0.0)));

    uint count = min(uint(max(p[45], 0.0)), 32u);
    if (count == 0u) return float4(p[4], p[5], p[6], p[7]);
    float4 previous = float4(p[49], p[50], p[51], p[52]);
    float previousPos = p[48];
    if (t <= previousPos) return previous;
    for (uint i = 1u; i < count; ++i) {
        const uint base = 48u + i * 5u;
        float pos = p[base];
        float4 color = float4(p[base + 1u], p[base + 2u],
                              p[base + 3u], p[base + 4u]);
        if (t <= pos) {
            float u = (t - previousPos) / max(pos - previousPos, 1e-6);
            return mix(previous, color, clamp(u, 0.0, 1.0));
        }
        previous = color;
        previousPos = pos;
    }
    return previous;
}

float2 to_local(constant float* p, float2 pixel)
{
    return float2(p[20] * pixel.x + p[22] * pixel.y + p[24],
                  p[21] * pixel.x + p[23] * pixel.y + p[25]);
}

float sd_round_rect(float2 point, float4 rect, float4 radii)
{
    float2 center = rect.xy + rect.zw * 0.5;
    float2 q0 = point - center;
    float radius = q0.x < 0.0
        ? (q0.y < 0.0 ? radii.x : radii.w)
        : (q0.y < 0.0 ? radii.y : radii.z);
    float2 q = abs(q0) - rect.zw * 0.5 + radius;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
}

float sd_ellipse(float2 point, float4 rect)
{
    float2 r = max(rect.zw * 0.5, float2(1e-5));
    float2 q = (point - (rect.xy + r)) / r;
    return (length(q) - 1.0) * min(r.x, r.y);
}

float sd_superellipse(float2 point, float4 rect, float exponent)
{
    float2 r = max(rect.zw * 0.5, float2(1e-5));
    float2 q = abs((point - (rect.xy + r)) / r);
    float n = max(exponent, 1.0);
    float v = pow(pow(q.x, n) + pow(q.y, n), 1.0 / n) - 1.0;
    return v * min(r.x, r.y);
}

float clip_coverage(constant float* p, float2 pixel)
{
    uint mode = uint(max(p[34], 0.0));
    if (mode == 0u) return 1.0;
    float d = sd_round_rect(pixel, float4(p[26], p[27], p[28], p[29]),
                           float4(p[30], p[31], p[32], p[33]));
    float inside = 1.0 - smoothstep(-0.75, 0.75, d);
    return mode == 2u ? 1.0 - inside : inside;
}

fragment float4 jalium_shape_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]])
{
    const uint primitive = uint(max(p[2], 0.0));
    const float2 local = to_local(p, in.pixel);
    const float4 rect = float4(p[8], p[9], p[10], p[11]);
    float distance = -1.0;
    if (primitive == 1u) {
        distance = sd_round_rect(local, rect, float4(p[12], p[13], p[14], p[15]));
    } else if (primitive == 2u) {
        distance = sd_ellipse(local, rect);
    } else if (primitive == 3u) {
        distance = sd_superellipse(local, rect, p[18]);
    }

    float coverage = primitive == 0u ? 1.0 : 1.0 - smoothstep(-0.75, 0.75, distance);
    float stroke = p[16];
    if (stroke > 0.0 && primitive != 0u) {
        float outer = 1.0 - smoothstep(-0.75, 0.75, distance);
        float inner = 1.0 - smoothstep(-0.75, 0.75, distance + stroke);
        coverage = max(outer - inner, 0.0);
    }
    coverage *= clip_coverage(p, in.pixel);

    float4 color = uint(max(p[3], 0.0)) == 0u
        ? float4(p[4], p[5], p[6], p[7])
        : sample_gradient(p, local);
    color.a *= p[17] * coverage;
    color.rgb *= color.a;
    return color;
}

fragment float4 jalium_texture_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]],
    texture2d<float> source [[texture(0)]],
    sampler sourceSampler [[sampler(0)]])
{
    float4 color = source.sample(sourceSampler, in.uv);
    if (uint(max(p[179], 0.0)) == 1u) {
        float4 tint = float4(p[4], p[5], p[6], p[7]);
        color = float4(tint.rgb * color.a, tint.a * color.a);
    }
    color *= p[17] * clip_coverage(p, in.pixel);
    return color;
}

fragment float4 jalium_yuv_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]],
    texture2d<float> lumaTexture [[texture(0)]],
    texture2d<float> chromaTexture [[texture(1)]],
    sampler sourceSampler [[sampler(0)]])
{
    float y = lumaTexture.sample(sourceSampler, in.uv).r;
    float2 cbcr = chromaTexture.sample(sourceSampler, in.uv).rg - 0.5;
    // BT.709 full-range matrix. Video-range producers are normalized by
    // CoreVideo's texture view before this stage; P010 follows the same path.
    float3 rgb;
    rgb.r = y + 1.5748 * cbcr.y;
    rgb.g = y - 0.1873 * cbcr.x - 0.4681 * cbcr.y;
    rgb.b = y + 1.8556 * cbcr.x;
    float alpha = p[17] * clip_coverage(p, in.pixel);
    return float4(clamp(rgb, 0.0, 1.0) * alpha, alpha);
}

fragment float4 jalium_effect_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]], texture2d<float> source [[texture(0)]],
    sampler sourceSampler [[sampler(0)]])
{
    float4 c = source.sample(sourceSampler, in.uv);
    uint mode = uint(max(p[180], 0.0));
    if (mode == 1u) {
        float4 r;
        r.r = dot(c, float4(p[181], p[182], p[183], p[184])) + p[197];
        r.g = dot(c, float4(p[185], p[186], p[187], p[188])) + p[198];
        r.b = dot(c, float4(p[189], p[190], p[191], p[192])) + p[199];
        r.a = dot(c, float4(p[193], p[194], p[195], p[196])) + p[200];
        c = clamp(r, 0.0, 1.0);
    } else if (mode == 2u) {
        float2 texel = float2(p[181], p[182]);
        float l = dot(source.sample(sourceSampler, in.uv - texel).rgb,
                      float3(0.299, 0.587, 0.114));
        float r = dot(source.sample(sourceSampler, in.uv + texel).rgb,
                      float3(0.299, 0.587, 0.114));
        float relief = p[183];
        c.rgb = clamp(c.rgb + (r - l) * relief, 0.0, 1.0);
    }
    c *= p[17] * clip_coverage(p, in.pixel);
    return c;
}

kernel void jalium_blur(texture2d<float, access::read> source [[texture(0)]],
    texture2d<float, access::write> destination [[texture(1)]],
    constant float* p [[buffer(0)]], uint2 gid [[thread_position_in_grid]])
{
    if (gid.x >= destination.get_width() || gid.y >= destination.get_height()) return;
    int radius = clamp(int(p[0]), 0, 32);
    bool horizontal = p[1] > 0.5;
    float sigma = max(p[2], max(float(radius) / 3.0, 0.5));
    float4 sum = 0.0;
    float weightSum = 0.0;
    for (int i = -radius; i <= radius; ++i) {
        int2 q = int2(gid) + (horizontal ? int2(i, 0) : int2(0, i));
        q = clamp(q, int2(0), int2(int(source.get_width()) - 1,
                                  int(source.get_height()) - 1));
        float weight = exp(-float(i * i) / (2.0 * sigma * sigma));
        sum += source.read(uint2(q)) * weight;
        weightSum += weight;
    }
    destination.write(sum / max(weightSum, 1e-6), gid);
}
)METAL";

} // namespace jalium
