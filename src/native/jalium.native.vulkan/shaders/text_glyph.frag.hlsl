// Dedicated text-glyph pipeline — fragment shader.
//
// One source, two variants selected by the gClearType specialization constant:
//   gClearType == 0  → GRAYSCALE single-source (the shipping default). The atlas
//                      stores R=G=B=coverage; emit PREMULTIPLIED colour * coverage.
//                      The grayscale pipeline has a single colour attachment + blend
//                      src=ONE / dst=ONE_MINUS_SRC_ALPHA, so SV_Target1 is ignored.
//   gClearType == 1  → CLEARTYPE dual-source (opt-in, requires dualSrcBlend). The
//                      atlas stores per-channel R/G/B sub-pixel coverage; emit
//                      SV_Target0 = premult colour weighted per-channel and
//                      SV_Target1 = per-channel coverage for the
//                      src=ONE / dst=ONE_MINUS_SRC1_COLOR blend. Mirrors the proven
//                      D3D12 bitmap_text.ps.hlsl dual-source contract.
//
// The colour arriving from the vertex stage is already premultiplied
// (VulkanGlyphAtlas::GenerateGlyphs' emitRun premultiplies).
//
// Colour-emoji glyphs are flagged with a negative-R sentinel on the instance
// colour; their authored premultiplied RGBA is baked into the atlas, so they
// sample the atlas directly. Equal SV_Target1 channels make the dual-source
// blend degenerate into a plain SrcOver alpha blend for them.

[[vk::constant_id(0)]] const uint gClearType = 0;

[[vk::binding(0)]] Texture2D atlasTexture;
[[vk::binding(1)]] SamplerState atlasSampler;

struct TextPushConstants
{
    float2 screenSize;
    float2 padding;
    float4 roundedClipRect;
    float2 roundedClipRadius;
    float2 clipFlags;
    float4 innerRoundedClipRect;
    float2 innerRoundedClipRadius;
    float2 padding2;
};

[[vk::push_constant]]
TextPushConstants gPushConstants;

struct PsInput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    float4 color : TEXCOORD1;
};

// Dual-source output. The grayscale pipeline (single colour attachment) ignores
// SV_Target1; the ClearType pipeline consumes it as the per-channel SRC1 factor.
struct PsOutput
{
    float4 color : SV_Target0;
    float4 coverage : SV_Target1;
};

bool IsInsideRoundRect(float2 pixel, float4 rect, float2 radius)
{
    const float left = rect.x;
    const float top = rect.y;
    const float right = rect.z;
    const float bottom = rect.w;
    if (pixel.x < left || pixel.y < top || pixel.x > right || pixel.y > bottom) {
        return false;
    }

    const float rx = max(radius.x, 0.0f);
    const float ry = max(radius.y, 0.0f);
    if (rx <= 0.0f || ry <= 0.0f) {
        return true;
    }

    if (pixel.x < left + rx && pixel.y < top + ry) {
        const float2 delta = (pixel - float2(left + rx, top + ry)) / float2(rx, ry);
        return dot(delta, delta) <= 1.0f;
    }
    if (pixel.x > right - rx && pixel.y < top + ry) {
        const float2 delta = (pixel - float2(right - rx, top + ry)) / float2(rx, ry);
        return dot(delta, delta) <= 1.0f;
    }
    if (pixel.x < left + rx && pixel.y > bottom - ry) {
        const float2 delta = (pixel - float2(left + rx, bottom - ry)) / float2(rx, ry);
        return dot(delta, delta) <= 1.0f;
    }
    if (pixel.x > right - rx && pixel.y > bottom - ry) {
        const float2 delta = (pixel - float2(right - rx, bottom - ry)) / float2(rx, ry);
        return dot(delta, delta) <= 1.0f;
    }

    return true;
}

PsOutput main(PsInput input)
{
    PsOutput o;
    if (gPushConstants.clipFlags.x > 0.5f && !IsInsideRoundRect(input.position.xy, gPushConstants.roundedClipRect, gPushConstants.roundedClipRadius)) {
        discard;
    }
    if (gPushConstants.clipFlags.y > 0.5f && IsInsideRoundRect(input.position.xy, gPushConstants.innerRoundedClipRect, gPushConstants.innerRoundedClipRadius)) {
        discard;
    }

    // Colour-emoji: the atlas holds authored premultiplied RGBA; modulate by the
    // instance opacity carried in colour.a. Equal SV_Target1 channels => SrcOver.
    if (input.color.r < 0.0f) {
        float4 emoji = atlasTexture.Sample(atlasSampler, input.uv) * input.color.a;
        o.color = emoji;
        o.coverage = float4(emoji.a, emoji.a, emoji.a, emoji.a);
        return o;
    }

    if (gClearType == 0) {
        // Grayscale / AA text: atlas R channel is coverage; colour is premultiplied.
        float coverage = atlasTexture.Sample(atlasSampler, input.uv).r;
        float4 c = input.color * coverage;
        o.color = c;
        o.coverage = float4(c.a, c.a, c.a, c.a);  // ignored by single-attachment pipeline
        return o;
    }

    // ClearType: atlas RGB is per-channel sub-pixel coverage (mirror D3D12).
    float3 coverage = atlasTexture.Sample(atlasSampler, input.uv).rgb;
    // Per-channel contrast enhancement for ClearType sharpness (D3D12 parity).
    float3 contrast = saturate(coverage * 1.2f - 0.1f);
    coverage = lerp(coverage, contrast, 0.3f);
    float maxCoverage = max(coverage.r, max(coverage.g, coverage.b));
    // input.color premultiplied (rgb = textColor*alpha, a = alpha). Scale each
    // channel by its sub-pixel coverage; SV_Target1 = coverage*alpha for the
    // ONE_MINUS_SRC1_COLOR destination factor.
    o.color = float4(input.color.rgb * coverage, input.color.a * maxCoverage);
    o.coverage = float4(coverage * input.color.a, maxCoverage * input.color.a);
    return o;
}
