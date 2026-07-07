// Built-in ColorMatrix effect pixel shader (Stage 4 / S1 GPU-RT path).
//
// Pre-compiled to embedded SPIR-V (gen_effect_builtin_spv.ps1 ->
// vulkan_effect_builtin_shaders.h) so the built-in effect NEVER depends on a
// runtime DXC (dxcompiler.dll). This is the byte-for-byte source that used to
// live in kColorMatrixPsHlsl inside vulkan_render_target.cpp; the runtime DXC
// path is now reserved for user DrawShaderEffectFromSource shaders only.
//
// Semantics: a 5x4 row-major feColorMatrix on STRAIGHT (non-premultiplied) RGBA
// in [0,1] (WPF / Direct2D convention). The offscreen capture is PREMULTIPLIED,
// so un-premultiply before the matrix and re-premultiply the result for the
// pipeline's (ONE, ONE_MINUS_SRC_ALPHA) blend. uvScale is baked into the uv by
// the shared custom-shader VS (custom_shader_effect.vs.hlsl: uv = corner *
// uvScale), so this PS samples i.uv directly.
//
// Register classes must match the runtime compiler's binding shifts
// (VulkanShaderCompiler kBShift=0 / kTShift=16 / kSShift=32) — the gen script
// passes the same -fvk-{t,s,u}-shift so the descriptor layout in
// EnsureCustomShaderBase (b0/t0/s0) binds correctly.

cbuffer EffectConstants : register(b0)
{
    float4 cm0;  // m[0..3]   row-major 5x4 feColorMatrix, 20 floats packed tight
    float4 cm1;  // m[4..7]
    float4 cm2;  // m[8..11]
    float4 cm3;  // m[12..15]
    float4 cm4;  // m[16..19]
};
Texture2D content : register(t0);
SamplerState contentSampler : register(s0);
struct PsIn { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
float4 main(PsIn i) : SV_Target
{
    float4 s = content.Sample(contentSampler, i.uv);
    float a = s.a;
    float3 rgb = a > 0.0001f ? s.rgb / a : float3(0.0f, 0.0f, 0.0f);
    float r = rgb.r;
    float g = rgb.g;
    float b = rgb.b;
    float nr = saturate(cm0.x * r + cm0.y * g + cm0.z * b + cm0.w * a + cm1.x);
    float ng = saturate(cm1.y * r + cm1.z * g + cm1.w * b + cm2.x * a + cm2.y);
    float nb = saturate(cm2.z * r + cm2.w * g + cm3.x * b + cm3.y * a + cm3.z);
    float na = saturate(cm3.w * r + cm4.x * g + cm4.y * b + cm4.z * a + cm4.w);
    return float4(nr * na, ng * na, nb * na, na);
}
