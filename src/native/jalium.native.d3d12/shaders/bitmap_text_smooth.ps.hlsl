#include "rounded_clip.hlsli"

// Bilinear variant of bitmap_text.ps — used ONLY for deformed (transform-scaled)
// text. The glyph bitmap is already rasterized at its final per-axis size, so it
// is displayed ~1:1; bilinear (s0) smooths the small in-bucket residual scaling
// AND the continuous sub-pixel position during an animated deform, so glyphs move
// together without per-glyph integer snapping/jitter and thin strokes don't
// shimmer. Normal 1:1 text keeps the point sampler (bitmap_text.ps) for max
// crispness. Identical logic otherwise (same ClearType dual-source output).
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

PsOutput main(PsInput input)
{
    float clipCoverage = RoundedClipCoverage(input.clipPos.xy);

    // Atlas is R8G8B8A8_UNORM.
    float4 atlas = glyphAtlas.Sample(glyphSampler, input.uv);

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

    // Contrast enhancement per channel (matches the point path).
    float3 contrast = saturate(coverage * 1.2 - 0.1);
    coverage = lerp(coverage, contrast, 0.3);

    coverage *= clipCoverage;
    float maxCoverage = max(coverage.r, max(coverage.g, coverage.b));
    if (maxCoverage < 1.0 / 255.0) discard;

    PsOutput o;
    o.color = float4(input.color.rgb * coverage, input.color.a * maxCoverage);
    o.coverage = float4(coverage * input.color.a, maxCoverage * input.color.a);
    return o;
}
