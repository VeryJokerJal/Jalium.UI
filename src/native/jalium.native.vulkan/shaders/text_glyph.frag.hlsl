// Dedicated text-glyph pipeline — fragment shader.
//
// One source, two COMPILE-TIME variants (gen_text_glyph_spv.ps1 compiles this
// file twice and embeds both SPIR-V blobs):
//   (default)        → GRAYSCALE single-source (the shipping default). The atlas
//                      stores R=G=B=coverage; emit PREMULTIPLIED colour * coverage.
//                      The grayscale pipeline has a single colour attachment + blend
//                      src=ONE / dst=ONE_MINUS_SRC_ALPHA. The output struct declares
//                      ONLY SV_Target0, so the single-attachment frameRenderPass
//                      consumes every output the shader writes (no
//                      Undefined-Value-ShaderOutputNotConsumed).
//   JALIUM_CLEARTYPE → CLEARTYPE dual-source (opt-in, requires dualSrcBlend). The
//                      atlas stores per-channel R/G/B sub-pixel coverage; emit
//                      SV_Target0 = premult colour weighted per-channel and
//                      SV_Target1 = per-channel coverage for the
//                      src=ONE / dst=ONE_MINUS_SRC1_COLOR blend. Mirrors the proven
//                      D3D12 bitmap_text.ps.hlsl dual-source contract.
//
// Why two compiled variants instead of the previous single shader + gClearType
// specialization constant: Vulkan dual-source blending reads SRC1 from the
// fragment output decorated Location 0, Index 1. A bare SV_Target1 is placed
// by DXC at Location 1, Index 0, which no *_SRC1_* blend factor ever reads —
// so the ClearType blend consumed undefined SRC1 data. The ClearType variant
// pins both outputs with explicit [[vk::location(0)]] + [[vk::index(0|1)]]
// (a layout only a dual-source pipeline may declare), while the grayscale
// variant stays single-output so the default path never carries a dual-source
// export it would not blend with.
//
// The colour arriving from the vertex stage is already premultiplied
// (VulkanGlyphAtlas::GenerateGlyphs' emitRun premultiplies).
//
// Colour-emoji glyphs are flagged with a negative-R sentinel on the instance
// colour; their authored premultiplied RGBA is baked into the atlas, so they
// sample the atlas directly. Equal SV_Target1 channels make the dual-source
// blend degenerate into a plain SrcOver alpha blend for them.

[[vk::binding(0)]] Texture2D atlasTexture;
[[vk::binding(1)]] SamplerState atlasSampler;

struct TextPushConstants
{
    float2 screenSize;
    float2 padding;
    float4 roundedClipRect;
    float2 roundedClipRadius;
    // clipFlags:
    //   .x > 0.5 → outer rounded clip enabled.
    //   .x > 1.5 → outer clip is INVERSE (PushRoundedRectClipExclude):
    //              keep 1 - coverage, masking the rect's INTERIOR.
    //   .y > 0.5 → inner rounded clip enabled (subtractive hole).
    float2 clipFlags;
    float4 innerRoundedClipRect;
    float2 innerRoundedClipRadius;
    float2 padding2;
    // Per-corner OUTER-clip radii (TL, TR, BR, BL) — armed by a non-uniform
    // ancestor clip; any non-zero entry switches the outer test off the
    // uniform (rx, ry) path. Same clip semantics as solid_rect / bitmap_quad.
    float4 perCornerRadiusX;
    float4 perCornerRadiusY;
};

[[vk::push_constant]]
TextPushConstants gPushConstants;

struct PsInput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    float4 color : TEXCOORD1;
};

#ifdef JALIUM_CLEARTYPE
// Dual-source output. Vulkan's dual-source-blend interface reads SRC1 from
// Location 0 / Index 1 — the explicit annotations ARE the fix; without them
// SV_Target1 lands at Location 1 / Index 0 and the ONE_MINUS_SRC1_COLOR /
// _ALPHA factors blend against undefined data.
struct PsOutput
{
    [[vk::location(0), vk::index(0)]] float4 color : SV_Target0;
    [[vk::location(0), vk::index(1)]] float4 coverage : SV_Target1;
};
#else
// Single-source output: exactly one target, matching the grayscale pipeline's
// single colour attachment.
struct PsOutput
{
    float4 color : SV_Target0;
};
#endif

// Signed distance to the (rx, ry) elliptical-corner rounded rectangle.
// <= 0 inside, > 0 outside. Port of the D3D12 rounded_clip.hlsli
// JaliumRoundedClipSdf; the only divergence is the corner-radius model —
// D3D12 carries per-corner scalar radii, this pipeline carries one (rx, ry)
// ellipse shared by all four corners, so the box is squashed by rx/ry first to
// turn the elliptical corner into a circular one. For rx == ry (the common
// case) the squash is the identity and the SDF matches D3D12 field-for-field.
float RoundedClipSdf(float2 p, float4 rect, float2 radius)
{
    float2 center   = (rect.xy + rect.zw) * 0.5f;
    float2 halfSize = max((rect.zw - rect.xy) * 0.5f, float2(0.0001f, 0.0001f));
    float2 q        = p - center;

    float rx = max(radius.x, 0.0f);
    float ry = max(radius.y, 0.0f);
    if (rx <= 0.0f || ry <= 0.0f) {
        rx = 0.0f;   // no corner rounding → plain rectangle SDF
    } else if (rx != ry) {
        // Squash Y so the (rx, ry) elliptical corner becomes a circular rx
        // corner; fwidth() in the caller re-normalises the AA band back to
        // ~1 screen pixel, so the softened edge width stays correct.
        float squash = rx / ry;
        q.y        *= squash;
        halfSize.y *= squash;
    }

    float minDim = min(halfSize.x, halfSize.y);
    float r      = clamp(rx, 0.0f, minDim);

    float2 d = abs(q) - halfSize + r;
    return min(max(d.x, d.y), 0.0f) + length(max(d, 0.0f)) - r;
}

// Coverage of the rounded-clip mask at the given fragment, anti-aliased via
// smoothstep over a 1-pixel band derived from fwidth(sdf) — the D3D12
// rounded_clip.hlsli RoundedClipCoverage, line for line. Returns 1 inside,
// 0 outside, and a soft transition across the corner edge so the four corner
// arcs read as smooth curves rather than the 1-pixel stair-step the previous
// hard-discard path produced. `inverse` keeps the OUTSIDE instead — the inner
// rounded clip carves an anti-aliased hole out of the glyph run.
//
// Callers multiply this against the fragment's output alpha (and the
// dual-source per-channel coverage for ClearType), so the corner mask composes
// with the glyph's own AA / blending. Keeping the function side-effect-free
// (no discard) and calling it before ANY discard in main means fwidth() can
// read the SDF across the 2x2 helper pixel quad without hitting undefined
// behaviour from a sibling lane having already executed discard.
float RoundedClipCoverage(float2 fragPos, float4 rect, float2 radius, bool inverse)
{
    float d = RoundedClipSdf(fragPos, rect, radius);
    // fwidth(d) ≈ 1.0 inside a true distance field; we still clamp to a
    // small floor so corners on very sharp transforms still get one pixel
    // of smoothing instead of collapsing to a step function.
    float aa = max(fwidth(d), 0.0001f);
    float cov = 1.0f - smoothstep(-aa * 0.5f, aa * 0.5f, d);
    return inverse ? (1.0f - cov) : cov;
}

// Per-corner variant: quadrant-select each corner's own (rx, ry) — the
// quadrant only ever depends on its own corner, so reusing the uniform
// RoundedClipCoverage on a virtual single-radius rect is exact. Corner order
// (TL, TR, BR, BL) matches solid_rect.frag.hlsl's CoveragePerCornerRoundRect
// and the per-corner clip public API.
float RoundedClipCoveragePerCorner(float2 fragPos, float4 rect,
                                   float4 rxs, float4 rys, bool inverse)
{
    const float midX = (rect.x + rect.z) * 0.5f;
    const float midY = (rect.y + rect.w) * 0.5f;
    float rx, ry;
    if (fragPos.y < midY) {
        if (fragPos.x < midX) { rx = rxs.x; ry = rys.x; }     // TL
        else                  { rx = rxs.y; ry = rys.y; }     // TR
    } else {
        if (fragPos.x >= midX) { rx = rxs.z; ry = rys.z; }    // BR
        else                    { rx = rxs.w; ry = rys.w; }   // BL
    }
    // Defensive per-axis clamp (managed pre-clamps each corner to min(w,h)/2;
    // this only fires on out-of-contract input) — mirrors solid_rect.
    const float halfW = max((rect.z - rect.x) * 0.5f, 0.0f);
    const float halfH = max((rect.w - rect.y) * 0.5f, 0.0f);
    rx = min(rx, halfW);
    ry = min(ry, halfH);
    return RoundedClipCoverage(fragPos, rect, float2(rx, ry), inverse);
}

PsOutput main(PsInput input)
{
    PsOutput o;

    // Rounded clips as alpha coverage instead of boolean discard (D3D12
    // bitmap_text.ps.hlsl parity): the coverage multiplies into SV_Target0 and
    // the SV_Target1 per-channel coverage below, so the clip edge composes
    // with the glyph AA instead of punching a binary 1px stair-step through
    // it. Both tests are uniform control flow (push constants) and every
    // fwidth() completes before the first discard.
    float clipCoverage = 1.0f;
    if (gPushConstants.clipFlags.x > 0.5f) {
        // clipFlags.x == 2 → INVERSE outer clip (keep the OUTSIDE). Per-corner
        // mode is implicit: any non-zero perCornerRadiusX/Y switches off the
        // uniform (rx, ry) path — same gates as solid_rect / bitmap_quad.
        const bool outerInverse = gPushConstants.clipFlags.x > 1.5f;
        const float perCornerSum = dot(gPushConstants.perCornerRadiusX, float4(1.0f, 1.0f, 1.0f, 1.0f))
                                 + dot(gPushConstants.perCornerRadiusY, float4(1.0f, 1.0f, 1.0f, 1.0f));
        if (perCornerSum > 0.001f) {
            clipCoverage *= RoundedClipCoveragePerCorner(input.position.xy, gPushConstants.roundedClipRect,
                                                         gPushConstants.perCornerRadiusX,
                                                         gPushConstants.perCornerRadiusY, outerInverse);
        } else {
            clipCoverage *= RoundedClipCoverage(input.position.xy, gPushConstants.roundedClipRect,
                                                gPushConstants.roundedClipRadius, outerInverse);
        }
    }
    if (gPushConstants.clipFlags.y > 0.5f) {
        clipCoverage *= RoundedClipCoverage(input.position.xy, gPushConstants.innerRoundedClipRect,
                                            gPushConstants.innerRoundedClipRadius, true);
    }

    // Colour-emoji: the atlas holds authored premultiplied RGBA; modulate by the
    // instance opacity carried in colour.a. Equal SV_Target1 channels => SrcOver.
    if (input.color.r < 0.0f) {
        float4 sampled = atlasTexture.Sample(atlasSampler, input.uv);
        float a = sampled.a * clipCoverage;
        if (a < 1.0f / 255.0f) discard;
        float4 emoji = sampled * (input.color.a * clipCoverage);
        o.color = emoji;
#ifdef JALIUM_CLEARTYPE
        o.coverage = float4(emoji.a, emoji.a, emoji.a, emoji.a);
#endif
        return o;
    }

#ifndef JALIUM_CLEARTYPE
    // Grayscale / AA text: atlas R channel is coverage; colour is premultiplied.
    float coverage = atlasTexture.Sample(atlasSampler, input.uv).r;
    // Contrast enhancement — same curve and constants as the D3D12
    // bitmap_text.ps.hlsl monochrome path, which applies it to grayscale
    // AND ClearType coverage alike. Without it every default-weight glyph
    // renders visibly lighter than on D3D12 (parity gap A3).
    float contrast = saturate(coverage * 1.2f - 0.1f);
    coverage = lerp(coverage, contrast, 0.3f);
    // Rounded clip attenuates the coverage so the clip edge inherits the
    // glyph AA instead of a hard cut.
    coverage *= clipCoverage;
    if (coverage < 1.0f / 255.0f) discard;
    o.color = input.color * coverage;
    return o;
#else
    // ClearType: atlas RGB is per-channel sub-pixel coverage (mirror D3D12).
    float3 coverage = atlasTexture.Sample(atlasSampler, input.uv).rgb;
    // Per-channel contrast enhancement for ClearType sharpness (D3D12 parity).
    float3 contrast = saturate(coverage * 1.2f - 0.1f);
    coverage = lerp(coverage, contrast, 0.3f);
    // The rounded-clip mask attenuates per-channel coverage + max coverage so
    // the clip-edge AA and the glyph ClearType sub-pixel AA stack naturally
    // instead of a 1px hard cut (D3D12 bitmap_text.ps.hlsl parity).
    coverage *= clipCoverage;
    float maxCoverage = max(coverage.r, max(coverage.g, coverage.b));
    if (maxCoverage < 1.0f / 255.0f) discard;
    // input.color premultiplied (rgb = textColor*alpha, a = alpha). Scale each
    // channel by its sub-pixel coverage; SV_Target1 = coverage*alpha for the
    // ONE_MINUS_SRC1_COLOR destination factor.
    o.color = float4(input.color.rgb * coverage, input.color.a * maxCoverage);
    o.coverage = float4(coverage * input.color.a, maxCoverage * input.color.a);
    return o;
#endif
}
