// Built-in OuterGlow effect pixel shader (Stage 4 / S1 GPU-RT path).
//
// Pre-compiled to embedded SPIR-V (gen_effect_builtin_spv.ps1 ->
// vulkan_effect_builtin_shaders.h) so the built-in effect NEVER depends on a
// runtime DXC (dxcompiler.dll). Shares the custom-shader pipeline family (shared
// VS custom_shader_effect.vs.hlsl + b0 UBO / t0 sampled image / s0 sampler at
// kBShift=0 / kTShift=16 / kSShift=32).
//
// Semantics: silhouette-following outer glow — a TRUE 2D radial gaussian blur of
// the captured element's ALPHA, tinted, with the element's own silhouette knocked
// out (glow lives only OUTSIDE the shape) and the crisp element content composited
// ON TOP. This is the field-for-field port of the D3D12 kOuterGlowPS
// (d3d12_render_target.cpp) — the previous Vulkan path drew 8 concentric
// rounded-rect halo layers (a rectangular bloom that ignored the actual glyph /
// shape silhouette; parity ~60% FAIL). Driving the halo from the alpha silhouette
// matches D3D12's per-pixel gaussian exactly.
//
// The single upload image is PREMULTIPLIED (the offscreen capture is blitted into
// uploadImage[0,0,w,h] top-down, same as the bitmap path). So:
//   * the halo samples the .a channel (premultiplied alpha == coverage);
//   * the crisp content sample is already premultiplied for the SrcOver composite;
//   * the output is premultiplied for the (ONE, ONE_MINUS_SRC_ALPHA) blend.
// Doing content-OVER-glow in ONE pass reproduces D3D12's "draw glow, then
// composite content on top" result while fitting the custom-shader path's
// single-draw / replace-the-element contract.
//
// b0 constants (12 floats). [4..7] are REPLAY-PATCHED (patchUvInfo) with the
// shared upload image's texel step + the element's uvScale — only the replay
// knows uploadWidth/Height:
//   [0..3] tint.rgb + global glow alpha (opacity*intensity, clamped on CPU)
//   [4..5] texel   = 1/uploadW, 1/uploadH      (one source pixel, UV units)  [patched]
//   [6..7] uvScale = capturePx/uploadPx         (quad uv[0,1] -> capture rect) [patched]
//   [8]    K taps each direction (grid is (2K+1)^2)
//   [9]    sigma (gaussian sigma in tap units)
//   [10]   stepPx = radiusPx / K                (per-tap spacing, source px)
//   [11]   unused

cbuffer EffectConstants : register(b0)
{
    float4 p0;  // tint.rgb, glowAlpha
    float4 p1;  // texelU, texelV, uvScaleX, uvScaleY  (replay-patched)
    float4 p2;  // K, sigma, stepPx, unused
};
Texture2D content : register(t0);
SamplerState contentSampler : register(s0);
struct PsIn { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

float4 main(PsIn i) : SV_Target
{
    int K = (int)p2.x;
    if (K < 1) K = 1;
    if (K > 12) K = 12;                 // (2K+1)^2 grid — hard cap on the inner loops
    float sigma = max(p2.y, 0.5f);
    float twoSigma2 = 2.0f * sigma * sigma;
    float2 step = float2(p1.x, p1.y) * p2.z;   // texel * stepPx (per-tap UV step)

    float2 uvScale = float2(p1.z, p1.w);
    float2 baseUv = i.uv;                // shared VS already baked uvScale into uv
    float2 lo = float2(0.0f, 0.0f);
    float2 hi = uvScale;                 // never sample past the cleared capture region

    // True 2D RADIAL gaussian over a (2K+1)x(2K+1) grid (matches kOuterGlowPS).
    float accumA = 0.0f;
    float accumW = 0.0f;
    [loop]
    for (int dy = -K; dy <= K; ++dy)
    {
        [loop]
        for (int dx = -K; dx <= K; ++dx)
        {
            float wgt = exp(-(float(dx * dx + dy * dy)) / twoSigma2);
            float2 uv = clamp(baseUv + float2(float(dx), float(dy)) * step, lo, hi);
            accumA += content.Sample(contentSampler, uv).a * wgt;
            accumW += wgt;
        }
    }

    // Knockout the element's own silhouette so the glow lives ONLY outside it.
    float centerA = content.Sample(contentSampler, clamp(baseUv, lo, hi)).a;
    float glowAlpha = (accumW > 0.0f) ? (accumA / accumW) : 0.0f;
    glowAlpha *= saturate(1.0f - centerA);
    glowAlpha *= p0.w;

    // Premultiplied glow.
    float3 glowRgb = p0.rgb * glowAlpha;

    // Crisp content sample (already premultiplied) composited OVER the glow:
    //   out = content + glow * (1 - content.a).
    float4 c = content.Sample(contentSampler, clamp(baseUv, lo, hi));
    float3 outRgb = c.rgb + glowRgb * (1.0f - c.a);
    float outA = c.a + glowAlpha * (1.0f - c.a);
    return float4(outRgb, outA);
}
