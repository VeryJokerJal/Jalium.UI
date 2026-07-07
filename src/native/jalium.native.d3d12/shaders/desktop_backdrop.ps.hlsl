// D3D12 port of the Vulkan desktop-backdrop shader semantics
// (jalium.native.vulkan/shaders/backdrop_quad.frag.hlsl, applied by
// DrawDesktopBackdrop): box blur clamped to 8 taps of one source texel each,
// tint lerp, saturation lerp on Rec.601 luma, and a screen-space hash noise -
// field-for-field the same math so Mica/Acrylic desktop backdrops match across
// backends. The Vulkan per-corner discard block is omitted on purpose: the
// desktop path always records zero corner radii (vulkan_render_target.cpp,
// DrawDesktopBackdrop -> TryRecordGpuBackdropCommand with 0,0,0,0), which makes
// that block a no-op. SampleLevel(.., 0) replaces Sample because the loop
// bounds are dynamic (fxc rejects gradient sampling there); the capture has a
// single mip so the result is identical. Output is premultiplied for the
// shared ONE / INV_SRC_ALPHA custom-effect PSO.

Texture2D sourceTexture : register(t0);
SamplerState sourceSampler : register(s0);

cbuffer BackdropConstants : register(b0)
{
    float4 blurInfo;   // x=blur radius (source texels; shader clamps to <=8 taps) y=texelStepX z=texelStepY w=unused
    float4 tintColor;  // rgb = tint color, a = tint opacity
    float4 extraInfo;  // x=saturation (1 = unchanged) y=noiseIntensity zw=unused
};

struct PsInput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

float4 main(PsInput input) : SV_Target
{
    const float2 texelStep = float2(blurInfo.y, blurInfo.z);
    const float radius = clamp(blurInfo.x, 0.0f, 8.0f);
    const int blurRadius = min(8, max(0, (int)round(radius)));

    float4 blurred = 0.0f;
    int count = 0;
    [loop]
    for (int dy = -blurRadius; dy <= blurRadius; ++dy) {
        [loop]
        for (int dx = -blurRadius; dx <= blurRadius; ++dx) {
            blurred += sourceTexture.SampleLevel(sourceSampler, input.uv + float2(dx, dy) * texelStep, 0);
            ++count;
        }
    }
    blurred = count > 0 ? blurred / count : sourceTexture.SampleLevel(sourceSampler, input.uv, 0);

    float3 color = lerp(blurred.rgb, tintColor.rgb, tintColor.a);
    const float saturation = max(0.0f, extraInfo.x);
    const float luminance = dot(color, float3(0.299, 0.587, 0.114));
    color = lerp(float3(luminance, luminance, luminance), color, saturation);

    const float noiseIntensity = max(0.0f, extraInfo.y);
    const float noise = frac(sin(dot(input.position.xy, float2(12.9898, 78.233))) * 43758.5453);
    color += (noise - 0.5) * noiseIntensity * 0.04;

    const float outA = max(blurred.a, 0.08f + tintColor.a * 0.25f);
    return float4(color * outA, outA);   // premultiplied out
}
