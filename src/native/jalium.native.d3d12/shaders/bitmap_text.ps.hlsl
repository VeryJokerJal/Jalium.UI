#include "rounded_clip.hlsli"

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
    // Rounded clip 改用 alpha coverage：避免 discard 在 ClearType 双源混合
    // 路径上把字形的子像素覆盖率打成二值锯齿。coverage 同时乘到 .color 与
    // SV_Target1 的 coverage 通道上，让圆角边缘的字也走 ClearType AA。
    float clipCoverage = RoundedClipCoverage(input.clipPos.xy);

    // Atlas is R8G8B8A8_UNORM.
    float4 atlas = glyphAtlas.Sample(glyphSampler, input.uv);

    // Colour-emoji sentinel: the CPU side flags COLR/CPAL glyphs by writing
    // input.color.r = -1.  Atlas RGB is the emoji's authored colour already
    // premultiplied with its own alpha; we honour the text Foreground only
    // for opacity (so e.g. a "fade-out" animation still works) and bypass
    // the per-channel ClearType dual-source blend, which would otherwise
    // punch holes in DEST.r/g/b based on the emoji palette and mangle the
    // colours.  Equal SV_Target1 channels make the dual-source blend
    // degenerate into a plain SrcOver alpha blend.
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

    // Monochrome (ClearType / Grayscale) path — .rgb is per-channel coverage,
    // .a is max coverage.
    float3 coverage = atlas.rgb;

    // Apply contrast enhancement per channel for ClearType sharpness
    float3 contrast = saturate(coverage * 1.2 - 0.1);
    coverage = lerp(coverage, contrast, 0.3);

    // 圆角裁剪以 alpha mask 形式衰减每通道覆盖率 + max coverage，让
    // 圆角边缘的子像素 AA 与字形 ClearType AA 自然叠加，不再 1px 硬切。
    coverage *= clipCoverage;
    float maxCoverage = max(coverage.r, max(coverage.g, coverage.b));
    if (maxCoverage < 1.0 / 255.0) discard;

    // input.color is already premultiplied (rgb = textColor * textAlpha, a = textAlpha)
    // Scale each channel by its sub-pixel coverage
    PsOutput o;
    o.color = float4(input.color.rgb * coverage, input.color.a * maxCoverage);
    o.coverage = float4(coverage * input.color.a, maxCoverage * input.color.a);
    return o;
}
