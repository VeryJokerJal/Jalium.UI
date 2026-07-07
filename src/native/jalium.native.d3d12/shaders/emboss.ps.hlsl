// D3D12 Emboss effect pixel shader (D6 Vulkan parity).
//
// Authoritative semantics: VulkanRenderTarget::DrawEmbossEffect
// (jalium.native.vulkan/src/vulkan_render_target.cpp) — directional emboss:
// per-pixel luminance difference between the pixel and a neighbour offset
// along the (inverted) light direction, biased to mid-grey:
//   luma(p)  = 0.299*R + 0.587*G + 0.114*B          (straight color, [0,1])
//   diff     = luma(p) - luma(p - (dx, dy))          (neighbour edge-clamped)
//   v        = clamp(0.5 + diff * amt, 0, 1)
//   out.rgb  = v (grey), out.a = source alpha (preserved)
// The CPU side derives (dx, dy) = round(normalize(lightDir) * sampleDist) in
// capture pixels and amt = (amount <= 0 ? 1 : amount) — field-for-field with
// the Vulkan reference (note: amount <= 0 falls back to amt = 1 there, so a
// zero-strength emboss is NOT a passthrough in the authoritative semantics).
//
// Backend-plumbing differences (documented, not semantic):
//   * The Vulkan CPU buffer is straight alpha; D3D12 captures are
//     PREMULTIPLIED, so samples are un-premultiplied before the luma and the
//     grey result is re-premultiplied for the ONE / INV_SRC_ALPHA PSO.
//   * Vulkan clamps neighbour coordinates to [0, width-1]x[0, height-1]; here
//     the sample uv is clamped to the capture sub-rect inset by half a texel
//     (texel centers), which reproduces the same edge-clamp with the linear
//     sampler returning the exact edge texel.
//
// Constants are uploaded by D3D12RenderTarget::DrawEmbossEffect as 8
// sequential floats matching this float4-aligned layout.

Texture2D srcTexture : register(t0);
SamplerState srcSampler : register(s0);

cbuffer EmbossConstants : register(b0)
{
    float4 dirAmount;   // x = dx (capture px), y = dy (capture px), z = amt, w unused
    float4 texelScale;  // xy = 1 / atlas size (uv per capture px), zw = uvScale
};

struct PsInput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

float LumaAt(float2 uv, float2 lo, float2 hi)
{
    float2 cuv = clamp(uv, lo, hi);
    float4 s = srcTexture.SampleLevel(srcSampler, cuv, 0);
    // Premultiplied -> straight before the Rec.601 luma (Vulkan reads straight
    // bytes; fully transparent pixels contribute 0 on both backends).
    float3 straight = (s.a > 0.0001) ? (s.rgb / s.a) : float3(0.0, 0.0, 0.0);
    return 0.299 * straight.r + 0.587 * straight.g + 0.114 * straight.b;
}

float4 main(PsInput input) : SV_Target
{
    float2 baseUv = input.uv * texelScale.zw;
    float2 halfTexel = 0.5 * texelScale.xy;
    float2 lo = halfTexel;
    float2 hi = max(texelScale.zw - halfTexel, halfTexel);

    float4 src = srcTexture.SampleLevel(srcSampler, clamp(baseUv, lo, hi), 0);

    float2 offs = dirAmount.xy * texelScale.xy;
    float diff = LumaAt(baseUv, lo, hi) - LumaAt(baseUv - offs, lo, hi);
    float v = saturate(0.5 + diff * dirAmount.z);

    // Grey rgb, source alpha preserved (Vulkan keeps the captured alpha);
    // premultiplied for the ONE / INV_SRC_ALPHA custom-effect PSO.
    return float4(v * src.a, v * src.a, v * src.a, src.a);
}
