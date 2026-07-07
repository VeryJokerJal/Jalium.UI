// Built-in Emboss effect pixel shader (Stage 4 / S1 GPU-RT path).
//
// Pre-compiled to embedded SPIR-V (gen_effect_builtin_spv.ps1 ->
// vulkan_effect_builtin_shaders.h) so the built-in effect NEVER depends on a
// runtime DXC (dxcompiler.dll). This is the byte-for-byte source that used to
// live in kEmbossPsHlsl inside vulkan_render_target.cpp; the runtime DXC path is
// now reserved for user DrawShaderEffectFromSource shaders only.
//
// Semantics: directional emboss — per-pixel luminance difference between the
// pixel and a neighbour offset along the (inverted) light direction, biased to
// mid-grey. Straight-alpha Rec.601 luma; source alpha preserved; result
// re-premultiplied for the (ONE, ONE_MINUS_SRC_ALPHA) blend. uvScale is baked
// into the uv by the shared VS. The half-texel inset [halfTexel, uvMax-halfTexel]
// reproduces the D3D12 emboss.ps.hlsl edge clamp: with a LINEAR sampler on the
// shared upload image (larger than the capture) it stops the neighbour fetch
// bleeding adjacent atlas content at the sub-rect boundary (matches the CPU
// path's integer [0, dim-1] clamp).
//
// Register classes must match the runtime compiler's binding shifts
// (VulkanShaderCompiler kBShift=0 / kTShift=16 / kSShift=32).

cbuffer EffectConstants : register(b0)
{
    float4 p0;  // x=amount  y=lightDx(px)  z=lightDy(px)  w=unused
    float4 p1;  // x=1/uploadW  y=1/uploadH  z=uvScaleX  w=uvScaleY (replay-patched)
};
Texture2D content : register(t0);
SamplerState contentSampler : register(s0);
struct PsIn { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
float LumaAt(float2 uv, float2 lo, float2 hi)
{
    float4 s = content.Sample(contentSampler, clamp(uv, lo, hi));
    float3 rgb = s.a > 0.0001f ? s.rgb / s.a : float3(0.0f, 0.0f, 0.0f);
    return dot(rgb, float3(0.299f, 0.587f, 0.114f));
}
float4 main(PsIn i) : SV_Target
{
    float2 uvMax = p1.zw;
    float2 halfTexel = 0.5f * p1.xy;
    float2 lo = halfTexel;
    float2 hi = max(uvMax - halfTexel, halfTexel);
    float2 off = float2(p0.y, p0.z) * p1.xy;
    float diff = LumaAt(i.uv, lo, hi) - LumaAt(i.uv - off, lo, hi);
    float v = saturate(0.5f + diff * p0.x);
    float a = content.Sample(contentSampler, clamp(i.uv, lo, hi)).a;
    return float4(v * a, v * a, v * a, a);
}
