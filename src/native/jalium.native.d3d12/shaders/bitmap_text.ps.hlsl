Texture2D<float4> glyphAtlas : register(t1);
SamplerState glyphSampler : register(s1);  // point sampler for pixel-exact glyph sampling

struct PsInput
{
    float4 clipPos : SV_Position;
    float2 uv      : TEXCOORD0;
    float4 color   : COLOR0;
};

// Dual-source blending output for ClearType sub-pixel rendering.
// SV_Target0 = premultiplied color weighted by per-channel coverage
// SV_Target1 = per-channel coverage for INV_SRC1_COLOR destination blend
struct PsOutput
{
    float4 color    : SV_Target0;
    float4 coverage : SV_Target1;
};

PsOutput main(PsInput input)
{
    // Atlas is R8G8B8A8_UNORM: .rgb = per-channel sub-pixel coverage, .a = max coverage
    float4 atlas = glyphAtlas.Sample(glyphSampler, input.uv);
    float3 coverage = atlas.rgb;

    // Apply contrast enhancement per channel for ClearType sharpness
    float3 contrast = saturate(coverage * 1.2 - 0.1);
    coverage = lerp(coverage, contrast, 0.3);

    float maxCoverage = max(coverage.r, max(coverage.g, coverage.b));
    if (maxCoverage < 1.0 / 255.0) discard;

    // input.color is already premultiplied (rgb = textColor * textAlpha, a = textAlpha)
    // Scale each channel by its sub-pixel coverage
    PsOutput o;
    o.color = float4(input.color.rgb * coverage, input.color.a * maxCoverage);
    o.coverage = float4(coverage * input.color.a, maxCoverage * input.color.a);
    return o;
}
