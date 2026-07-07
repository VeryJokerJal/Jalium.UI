// D3D12 ColorMatrix effect pixel shader (D6 Vulkan parity).
//
// Authoritative semantics: VulkanRenderTarget::DrawColorMatrixEffect
// (jalium.native.vulkan/src/vulkan_render_target.cpp) — a 5x4 row-major color
// matrix in the feColorMatrix / WPF / Direct2D convention applied to STRAIGHT
// (non-premultiplied) RGBA in [0,1]:
//   out.ch = dot(row_ch, float4(r,g,b,a)) + row_ch_offset, clamped to [0,1].
//
// Backend-plumbing differences (documented, not semantic):
//   * The Vulkan CPU capture buffer holds straight-alpha BGRA, so it applies
//     the matrix to the stored bytes directly. D3D12 offscreen captures are
//     PREMULTIPLIED, so the sample is un-premultiplied first, transformed in
//     straight space, then re-premultiplied by the transformed alpha for the
//     shared ONE / INV_SRC_ALPHA custom-effect PSO (same convention as
//     transition_quad.ps.hlsl).
//   * The capture lives in a shared offscreen atlas LARGER than the capture
//     area; uvScale maps the quad's rect-local uv [0,1] onto the capture
//     sub-rect (same convention as DrawOffscreenBitmap / kOuterGlowPS).
//
// Identity-matrix check: rowR/G/B/A = unit rows, offsets = 0 gives
// out = (straight.rgb * a, a) = the original premultiplied sample, so an
// identity ColorMatrix reproduces the captured content exactly.
//
// Constants are uploaded by D3D12RenderTarget::DrawColorMatrixEffect as 24
// sequential floats; this cbuffer layout is float4-aligned so the raw copy
// lands field-for-field.

Texture2D srcTexture : register(t0);
SamplerState srcSampler : register(s0);

cbuffer ColorMatrixConstants : register(b0)
{
    float4 rowR;     // matrix[0..3]   — r,g,b,a weights for output R
    float4 rowG;     // matrix[5..8]   — weights for output G
    float4 rowB;     // matrix[10..13] — weights for output B
    float4 rowA;     // matrix[15..18] — weights for output A
    float4 offsets;  // matrix[4], matrix[9], matrix[14], matrix[19]
    float4 uvScale;  // xy = capture sub-rect scale in the shared atlas
};

struct PsInput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

float4 main(PsInput input) : SV_Target
{
    float2 uv = input.uv * uvScale.xy;
    float4 src = srcTexture.SampleLevel(srcSampler, uv, 0);

    // Premultiplied capture -> straight (the matrix operates in straight space).
    float3 straight = (src.a > 0.0001) ? (src.rgb / src.a) : float3(0.0, 0.0, 0.0);
    float4 c = float4(straight, src.a);

    float nr = saturate(dot(rowR, c) + offsets.x);
    float ng = saturate(dot(rowG, c) + offsets.y);
    float nb = saturate(dot(rowB, c) + offsets.z);
    float na = saturate(dot(rowA, c) + offsets.w);

    // Re-premultiply for the ONE / INV_SRC_ALPHA custom-effect PSO.
    return float4(nr * na, ng * na, nb * na, na);
}
