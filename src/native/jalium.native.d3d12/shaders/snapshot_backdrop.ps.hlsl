// D3D12 in-app backdrop-filter shader (BlurEffect / AcrylicEffect / MicaEffect).
//
// This is the in-app sibling of desktop_backdrop.ps.hlsl. Where the desktop
// path samples a GDI desktop capture that fills the whole quad, this path
// samples the framebuffer SNAPSHOT (the full back buffer copied by
// CaptureSnapshot) and draws a SUB-REGION of it back, so it needs a UV remap
// (uvOffset + quadUv * uvScale) to address the sub-rect of the snapshot the
// backdrop occupies. It also carries per-corner rounding (in-app backdrops sit
// under rounded Borders; the desktop path always used square corners) and a
// luminosity multiplier (Mica raises perceived brightness a few percent).
//
// Colour math is field-for-field the Vulkan reference (backdrop_quad.frag.hlsl,
// mirrored in desktop_backdrop.ps.hlsl):
//   * <=8-tap box blur of one source texel each (fxc rejects gradient Sample in
//     a dynamic loop, so SampleLevel(...,0); the snapshot has a single mip so
//     the result is identical),
//   * tint lerp:        color = lerp(blurred, tint.rgb, tint.a),
//   * saturation lerp:  towards Rec.601 luma,
//   * luminosity:       color *= luminosity (NEW vs the desktop shader),
//   * screen-space hash noise: (frac(sin(dot(pos, k))*c) - 0.5) * intensity*0.04.
//
// Output is premultiplied for the shared ONE / INV_SRC_ALPHA custom-effect PSO,
// and the per-corner rounded-rect SDF multiplies coverage into the alpha so the
// backdrop is masked to the element's rounded silhouette (the ancestor AABB
// clip is applied on the CPU side via RSSetScissorRects, matching the desktop
// path and the rest of the D3D12 rounded-clip approximation).

Texture2D sourceTexture : register(t0);
SamplerState sourceSampler : register(s0);

cbuffer SnapshotBackdropConstants : register(b0)
{
    float4 blurInfo;    // x=blur radius (source texels; clamped to <=8 taps) y=texelStepX z=texelStepY w=unused
    float4 tintColor;   // rgb = tint color, a = tint opacity
    float4 extraInfo;   // x=saturation (1=unchanged) y=noiseIntensity z=luminosity (1=unchanged) w=unused
    float4 uvRemap;     // xy = uv offset into the snapshot, zw = uv scale over the quad
    float4 clipRect;    // (left, top, right, bottom) in PHYSICAL pixels (SV_Position space)
    float4 clipRadii;   // per-corner radii in physical pixels: (TL, TR, BR, BL)
};

struct PsInput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

// Per-corner rounded-rect SDF (iquilezles), identical convention to
// rounded_clip.hlsli: <= 0 inside, > 0 outside. Coordinates are physical pixels.
float BackdropRoundedSdf(float2 p)
{
    float2 center   = (clipRect.xy + clipRect.zw) * 0.5;
    float2 halfSize = max((clipRect.zw - clipRect.xy) * 0.5, float2(0.0001, 0.0001));
    float2 q        = p - center;
    float  minDim   = min(halfSize.x, halfSize.y);

    // clipRadii = (TL, TR, BR, BL); pick by quadrant.
    float radius = (q.x > 0.0)
        ? ((q.y > 0.0) ? clipRadii.z : clipRadii.y)
        : ((q.y > 0.0) ? clipRadii.w : clipRadii.x);
    radius = clamp(radius, 0.0, minDim);

    float2 d = abs(q) - halfSize + radius;
    return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - radius;
}

float4 main(PsInput input) : SV_Target
{
    // Quad-local uv [0,1] -> snapshot sub-rect uv.
    const float2 srcUv = uvRemap.xy + input.uv * uvRemap.zw;

    const float2 texelStep = float2(blurInfo.y, blurInfo.z);
    const float radius = clamp(blurInfo.x, 0.0f, 8.0f);
    const int blurRadius = min(8, max(0, (int)round(radius)));

    float4 blurred = 0.0f;
    int count = 0;
    [loop]
    for (int dy = -blurRadius; dy <= blurRadius; ++dy) {
        [loop]
        for (int dx = -blurRadius; dx <= blurRadius; ++dx) {
            blurred += sourceTexture.SampleLevel(sourceSampler, srcUv + float2(dx, dy) * texelStep, 0);
            ++count;
        }
    }
    blurred = count > 0 ? blurred / count : sourceTexture.SampleLevel(sourceSampler, srcUv, 0);

    // Tint lerp.
    float3 color = lerp(blurred.rgb, tintColor.rgb, tintColor.a);

    // Saturation lerp about Rec.601 luma.
    const float saturation = max(0.0f, extraInfo.x);
    const float luminance = dot(color, float3(0.299, 0.587, 0.114));
    color = lerp(float3(luminance, luminance, luminance), color, saturation);

    // Luminosity (Mica raises perceived brightness a few percent).
    const float luminosity = max(0.0f, extraInfo.z);
    color *= luminosity;

    // Screen-space hash noise.
    const float noiseIntensity = max(0.0f, extraInfo.y);
    const float noise = frac(sin(dot(input.position.xy, float2(12.9898, 78.233))) * 43758.5453);
    color += (noise - 0.5) * noiseIntensity * 0.04;

    color = max(color, 0.0f);

    // The snapshot backdrop is opaque content, so drive alpha the same way the
    // desktop path does (a floor plus a tint-weighted term) and fold the
    // per-corner rounded-rect coverage into it so corners fade smoothly.
    float baseA = max(blurred.a, 0.08f + tintColor.a * 0.25f);

    float sdf = BackdropRoundedSdf(input.position.xy);
    float aa = max(fwidth(sdf), 0.0001);
    float cornerCov = 1.0 - smoothstep(-aa * 0.5, aa * 0.5, sdf);

    const float outA = baseA * cornerCov;
    return float4(color * outA, outA);   // premultiplied out
}
