// D3D12 in-app backdrop-material shader (BackdropBlurEffect / AcrylicEffect /
// MicaEffect / FrostedGlassEffect / ColorAdjustmentEffect).
//
// This is the in-app sibling of desktop_backdrop.ps.hlsl. Where the desktop
// path samples a GDI desktop capture that fills the whole quad, this path
// samples a SUB-REGION of the framebuffer snapshot and draws it back, so it
// needs a UV remap (uvOffset + quadUv * uvScale) to address the sub-rect the
// backdrop occupies.
//
// Blur: the heavy lifting is done BEFORE this pass. BlurSnapshotRegion runs
// the separable Gaussian compute shader (gaussian_blur.cs.hlsl: DPI-scaled
// radius, 1x/2x/4x downsample, linear-light, sigma from the material) over the
// region of the snapshot under the panel and t0 is that pre-blurred scratch
// texture; blurInfo.x is then 0 and the quad is a plain resample. Only when
// the compute route is unavailable (blur resources not created, scratch could
// not grow mid-frame) does t0 fall back to the raw snapshot with the small
// <=8-tap box below as a degraded-but-visible blur.
//
// Colour pipeline: the SAME order on every backend (Vulkan
// backdrop_quad.frag.hlsl, the software backend). A single CSS backdrop-filter
// function maps to one stage; ordered CSS lists need a separate pipeline:
//   blur -> brightness -> contrast -> saturation -> hueRotation -> grayscale
//        -> sepia -> invert -> tint -> luminosity -> noise
// Noise is full-range hash grain mixed at noiseIntensity (integer hash, no
// frac(sin()) banding). The Frosted kernel adds a per-pixel sample jitter of
// materialInfo1.w texels before the resample for frost grain.
//
// Output is premultiplied for the shared ONE / INV_SRC_ALPHA custom-effect PSO;
// the per-corner rounded-rect SDF multiplies anti-aliased coverage into the
// alpha and materialInfo0.w (effect opacity) scales the whole result (the
// ancestor AABB clip is applied CPU-side via RSSetScissorRects).

Texture2D sourceTexture : register(t0);
SamplerState sourceSampler : register(s0);
#define JALIUM_BACKDROP_ELLIPTICAL_CLIP
#include "../../jalium.native.core/shaders/elliptical_clip.hlsli"

cbuffer SnapshotBackdropConstants : register(b0)
{
    float4 blurInfo;       // x=residual blur radius (source texels, <=8, 0 on the compute route) y=texelStepX z=texelStepY w=unused
    float4 tintColor;      // rgb = tint colour, a = effective tint opacity
    float4 extraInfo;      // x=saturation y=noiseIntensity z=luminosity w=elliptical clip count
    float4 uvRemap;        // xy = uv offset into the source, zw = uv scale over the quad
    float4 clipRect;       // (left, top, right, bottom) in PHYSICAL pixels (SV_Position space)
    float4 clipRadii;      // per-corner radii in physical pixels: (TL, TR, BR, BL)
    float4 materialInfo0;  // x=brightness y=contrast z=hueRotation (radians) w=opacity
    float4 materialInfo1;  // x=grayscale y=sepia z=invert w=frost jitter (source texels)
};

struct PsInput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

// Per-corner rounded-rect SDF (iquilezles), <= 0 inside, > 0 outside. Physical
// pixels so the anti-aliasing ramp is exactly one device pixel wide.
float BackdropRoundedSdf(float2 p)
{
    float2 center   = (clipRect.xy + clipRect.zw) * 0.5;
    float2 halfSize = max((clipRect.zw - clipRect.xy) * 0.5, float2(0.0001, 0.0001));
    float2 q        = p - center;
    float  minDim   = min(halfSize.x, halfSize.y);
    float radius = (q.x > 0.0) ? ((q.y > 0.0) ? clipRadii.z : clipRadii.y)
                               : ((q.y > 0.0) ? clipRadii.w : clipRadii.x);
    radius = clamp(radius, 0.0, minDim);
    float2 d = abs(q) - halfSize + radius;
    return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - radius;
}

// lowbias32 integer hash on the device pixel: stable per pixel, no visible
// lattice, identical in the Vulkan backdrop shader so the grain matches.
uint HashPixel(uint2 p, uint salt)
{
    uint n = p.x * 1597334677u ^ p.y * 3812015801u ^ salt * 2654435761u;
    n ^= n >> 16;
    n *= 0x7feb352du;
    n ^= n >> 15;
    n *= 0x846ca68bu;
    n ^= n >> 16;
    return n;
}

float Hash01(uint2 p, uint salt)
{
    return (float)HashPixel(p, salt) * (1.0f / 4294967295.0f);
}

float3 ApplyColorPipeline(float3 color)
{
    // brightness
    color *= max(materialInfo0.x, 0.0f);

    // contrast (pivot at mid grey)
    color = (color - 0.5f) * max(materialInfo0.y, 0.0f) + 0.5f;

    // CSS feColorMatrix saturation coefficients.
    const float saturation = max(extraInfo.x, 0.0f);
    float luma = dot(color, float3(0.213f, 0.715f, 0.072f));
    color = lerp(float3(luma, luma, luma), color, saturation);

    // CSS feColorMatrix hueRotate coefficients.
    const float hue = materialInfo0.z;
    if (abs(hue) > 0.0001f) {
        const float c = cos(hue);
        const float s = sin(hue);
        color = float3(
            dot(color, float3(0.213f + 0.787f * c - 0.213f * s,
                              0.715f - 0.715f * c - 0.715f * s,
                              0.072f - 0.072f * c + 0.928f * s)),
            dot(color, float3(0.213f - 0.213f * c + 0.143f * s,
                              0.715f + 0.285f * c + 0.140f * s,
                              0.072f - 0.072f * c - 0.283f * s)),
            dot(color, float3(0.213f - 0.213f * c - 0.787f * s,
                              0.715f - 0.715f * c + 0.715f * s,
                              0.072f + 0.928f * c + 0.072f * s)));
    }

    // grayscale
    luma = dot(color, float3(0.2126f, 0.7152f, 0.0722f));
    color = lerp(color, float3(luma, luma, luma), saturate(materialInfo1.x));

    // sepia
    const float3 sepia = float3(
        dot(color, float3(0.393f, 0.769f, 0.189f)),
        dot(color, float3(0.349f, 0.686f, 0.168f)),
        dot(color, float3(0.272f, 0.534f, 0.131f)));
    color = lerp(color, sepia, saturate(materialInfo1.y));

    // invert
    color = lerp(color, 1.0f - color, saturate(materialInfo1.z));

    // tint composites on top of the filtered backdrop
    color = lerp(color, tintColor.rgb, saturate(tintColor.a));

    // luminosity
    color *= max(extraInfo.z, 0.0f);
    return color;
}

float4 main(PsInput input) : SV_Target
{
    const uint2 pixel = (uint2)input.position.xy;
    const float2 texelStep = float2(blurInfo.y, blurInfo.z);

    float2 srcUv = uvRemap.xy + input.uv * uvRemap.zw;

    // Frosted kernel: jitter the resample position per pixel so the smooth
    // Gaussian picks up a fine frost grain that tracks the content beneath.
    const float frost = max(materialInfo1.w, 0.0f);
    if (frost > 0.0f) {
        const float2 jitter = float2(Hash01(pixel, 11u), Hash01(pixel, 29u)) - 0.5f;
        srcUv += jitter * frost * texelStep;
    }

    // Degraded route only (blurInfo.x == 0 on the compute route): small box
    // blur straight on the snapshot. fxc rejects gradient Sample in a dynamic
    // loop, so SampleLevel(..., 0); the sources have a single mip.
    const float radius = clamp(blurInfo.x, 0.0f, 8.0f);
    const int blurRadius = min(8, max(0, (int)round(radius)));

    float4 blurred = 0.0f;
    if (blurRadius > 0) {
        int count = 0;
        [loop]
        for (int dy = -blurRadius; dy <= blurRadius; ++dy) {
            [loop]
            for (int dx = -blurRadius; dx <= blurRadius; ++dx) {
                blurred += sourceTexture.SampleLevel(sourceSampler, srcUv + float2(dx, dy) * texelStep, 0);
                ++count;
            }
        }
        blurred /= (float)count;
    } else {
        blurred = sourceTexture.SampleLevel(sourceSampler, srcUv, 0);
    }

    float3 color = ApplyColorPipeline(blurred.rgb);

    // grain
    const float noiseIntensity = max(extraInfo.y, 0.0f);
    if (noiseIntensity > 0.0f) {
        color += (Hash01(pixel, 3u) - 0.5f) * noiseIntensity;
    }

    color = saturate(color);

    // Only tinted materials retain the legacy visibility floor. CSS filter
    // functions without tint must preserve a transparent backdrop's alpha.
    const float floorA = tintColor.a > 0.0f ? 0.08f + tintColor.a * 0.25f : 0.0f;
    const float baseA = max(blurred.a, floorA);

    const float sdf = BackdropRoundedSdf(input.position.xy);
    const float aa = max(fwidth(sdf), 0.0001f);
    const float cornerCov = 1.0f - smoothstep(-aa * 0.5f, aa * 0.5f, sdf);

    const uint ellipticalClipCount = (uint)extraInfo.w;
    const float ellipseCov = ellipticalClipCount == 0u ? 1.0f
        : JaliumEllipticalClipCoverage(ellipticalClipCount, input.position.xy);
    const float outA = baseA * cornerCov * ellipseCov * saturate(materialInfo0.w);
    return float4(color * outA, outA);   // premultiplied out
}
