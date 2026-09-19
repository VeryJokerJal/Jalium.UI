#include "rounded_clip.hlsli"

// Continuous sampling for Ideal grayscale text and transformed/supersampled
// strikes. Resolve supersampled coverage over the destination pixel footprint;
// explicit pixel-aligned text keeps bitmap_text.ps and its point sampler.
Texture2D<float4> glyphAtlas : register(t1);
SamplerState glyphSampler : register(s0);  // bilinear clamp (smooth sub-pixel for deformed text)

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

float4 SampleGlyphCoverage(float2 uv)
{
    uint atlasWidth = 1, atlasHeight = 1;
    glyphAtlas.GetDimensions(atlasWidth, atlasHeight);
    float2 size = float2(atlasWidth, atlasHeight);
    float2 dx = ddx(uv);
    float2 dy = ddy(uv);
    float footprint = max(length(dx * size), length(dy * size));
    if (footprint > 1.01)
    {
        // Resolve the full pixel footprint of supersampled strikes. A single
        // bilinear lookup undersamples a 2x bitmap at fractional positions,
        // making thin stems change coverage as a label scrolls or zooms.
        dx *= 0.25;
        dy *= 0.25;
        return (glyphAtlas.SampleLevel(glyphSampler, uv - dx - dy, 0) +
                glyphAtlas.SampleLevel(glyphSampler, uv + dx - dy, 0) +
                glyphAtlas.SampleLevel(glyphSampler, uv - dx + dy, 0) +
                glyphAtlas.SampleLevel(glyphSampler, uv + dx + dy, 0)) * 0.25;
    }
    return glyphAtlas.SampleLevel(glyphSampler, uv, 0);
}

PsOutput main(PsInput input)
{
    float clipCoverage = RoundedClipCoverage(input.clipPos.xy);

    // Atlas is R8G8B8A8_UNORM.
    float4 atlas = SampleGlyphCoverage(input.uv);

    // Colour-emoji sentinel (see bitmap_text.ps for details).
    if (input.color.r < 0.0)
    {
        float a = atlas.a * clipCoverage;
        if (a < 1.0 / 255.0) discard;
        float fg = input.color.a;
        PsOutput oc;
        oc.color    = float4(atlas.rgb * fg * clipCoverage, a * fg);
        float aw = a * fg;
        oc.coverage = float4(aw, aw, aw, aw);
        return oc;
    }

    // Monochrome (ClearType / Grayscale) path — .rgb is per-channel coverage.
    float3 coverage = atlas.rgb;

    // Monotonic enhanced contrast (matches the point path).
    // It strengthens partially covered edge pixels without erasing faint
    // coverage, keeping compressed stems present during deformation.
    coverage = saturate(coverage +
        coverage * (1.0 - coverage) * 0.5);

    coverage *= clipCoverage;
    float maxCoverage = max(coverage.r, max(coverage.g, coverage.b));
    if (maxCoverage < 1.0 / 255.0) discard;

    PsOutput o;
    o.color = float4(input.color.rgb * coverage, input.color.a * maxCoverage);
    o.coverage = float4(coverage * input.color.a, maxCoverage * input.color.a);
    return o;
}
