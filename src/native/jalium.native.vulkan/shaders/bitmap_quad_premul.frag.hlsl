// PREMULTIPLIED-source variant of bitmap_quad.frag.hlsl for the GPU bitmap
// replay path (DrawBitmap / pixel-buffer uploads).
//
// Why it exists: D3D12 decodes bitmaps to PREMULTIPLIED BGRA (WIC 32bppPBGRA)
// and blends them with SrcBlend=ONE, so its LINEAR sampler interpolates in
// premultiplied space. The Vulkan replay used to upload STRAIGHT alpha and
// blend SRC_ALPHA — per-texel the two models are identical, but a linear
// sample taken BETWEEN texels of different alpha diverges (straight-space
// interpolation bleeds high-alpha RGB into low-alpha texels). The parity
// bitmap scene showed this as a 98% diff on the semi-transparent Linear
// quadrant while Nearest quadrants matched exactly.
//
// The replay now premultiplies bitmap pixels while packing the staging
// buffer and draws them through a pipeline that pairs THIS shader with
// PREMULTIPLIED SrcOver blend (srcColorBlendFactor = ONE), matching D3D12
// byte-for-byte. ink_composite.frag.hlsl is NOT reusable here: it predates
// the coverage-AA / inverse / per-corner / custom-quad push-constant
// extensions below and would regress rounded-clipped bitmaps.
//
// This file is a full copy of bitmap_quad.frag.hlsl with exactly one
// semantic change, the tail of main():
//   bitmap_quad  (STRAIGHT):  color.a *= opacity * coverage;   // alpha only
//   this shader  (PREMUL):    color   *= opacity * coverage;   // ALL channels
// Keep everything else in lockstep with bitmap_quad.frag.hlsl.

Texture2D bitmapTexture : register(t0);
SamplerState bitmapSampler : register(s1);

struct PushConstants
{
    float4 rect;
    float4 uvOpacity;
    float2 screenSize;
    float2 padding;
    float4 roundedClipRect;
    float2 roundedClipRadius;
    // clipFlags:
    //   .x > 0.5 → outer rounded clip enabled.
    //   .x > 1.5 → outer clip is INVERSE (PushRoundedRectClipExclude):
    //              keep 1 - coverage, masking the rect's INTERIOR.
    //   .y > 0.5 → inner rounded clip enabled (subtractive hole).
    // Per-corner outer-clip mode is signalled implicitly by
    // perCornerRadiusX/Y being non-zero (sum > 0); uniform-radius callers
    // leave those fields zero and the shader uses roundedClipRadius.
    float2 clipFlags;
    float4 innerRoundedClipRect;
    float2 innerRoundedClipRadius;
    float2 padding2;
    float4 quadPoint01;
    float4 quadPoint23;
    float2 geometryFlags;
    float2 padding3;
    // Per-corner OUTER-clip radii (TL, TR, BR, BL) — same clip semantics as
    // solid_rect.frag.hlsl / text_glyph.frag.hlsl. Must mirror the 192-byte
    // C++ BitmapQuadPushConstants byte for byte.
    float4 perCornerRadiusX;
    float4 perCornerRadiusY;
};

[[vk::push_constant]]
PushConstants gPushConstants;

struct PsInput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

// Coverage of a pixel against a rounded rectangle, on [0, 1] — ported from
// solid_rect.frag.hlsl so bitmaps clipped by a rounded container get the same
// 1-px anti-aliased clip edge instead of the previous hard boolean discard
// (which stair-stepped every rounded Image corner).
float CoverageRoundRect(float2 pixel, float4 rect, float2 radius)
{
    const float left   = rect.x;
    const float top    = rect.y;
    const float right  = rect.z;
    const float bottom = rect.w;
    const float rx = max(radius.x, 0.0f);
    const float ry = max(radius.y, 0.0f);

    // Cheap reject: more than 1 pixel outside the AABB → no contribution.
    if (pixel.x < left - 1.0f || pixel.y < top - 1.0f ||
        pixel.x > right + 1.0f || pixel.y > bottom + 1.0f) {
        return 0.0f;
    }

    // Square (rx == 0 || ry == 0): plain AABB SDF.
    if (rx <= 0.0f || ry <= 0.0f) {
        const float cx = clamp(pixel.x, left, right);
        const float cy = clamp(pixel.y, top, bottom);
        const float dxOut = pixel.x - cx;
        const float dyOut = pixel.y - cy;
        const float distOutside = sqrt(dxOut * dxOut + dyOut * dyOut);
        if (distOutside > 0.0f) {
            const float aa = max(fwidth(pixel.x), fwidth(pixel.y));
            return 1.0f - smoothstep(0.0f, max(aa, 0.5f), distOutside);
        }
        return 1.0f;
    }

    float anchorX;
    float anchorY;
    if (pixel.x < left + rx) anchorX = left + rx;
    else if (pixel.x > right - rx) anchorX = right - rx;
    else anchorX = pixel.x;

    if (pixel.y < top + ry) anchorY = top + ry;
    else if (pixel.y > bottom - ry) anchorY = bottom - ry;
    else anchorY = pixel.y;

    const float2 delta = float2((pixel.x - anchorX) / rx, (pixel.y - anchorY) / ry);
    const float deltaLen = length(delta);
    const float signedDist = deltaLen - 1.0f;

    const float radiusScale = min(rx, ry);
    const float pixelDist = signedDist * radiusScale;
    const float aa = max(fwidth(pixel.x), fwidth(pixel.y));
    return 1.0f - smoothstep(-max(aa * 0.5f, 0.25f),
                              max(aa * 0.5f, 0.25f),
                              pixelDist);
}

// Per-corner variant — quadrant selects each corner's own (rx, ry), then
// reuses CoverageRoundRect on a virtual single-radius rect (each quadrant
// only ever depends on its own corner). Same as solid_rect.frag.hlsl.
float CoveragePerCornerRoundRect(float2 pixel, float4 rect, float4 rxs, float4 rys)
{
    const float midX = (rect.x + rect.z) * 0.5f;
    const float midY = (rect.y + rect.w) * 0.5f;
    float rx, ry;
    // Order in rxs/rys matches the per-corner public API: TL, TR, BR, BL.
    if (pixel.y < midY) {
        if (pixel.x < midX) { rx = rxs.x; ry = rys.x; }       // TL
        else                { rx = rxs.y; ry = rys.y; }       // TR
    } else {
        if (pixel.x >= midX) { rx = rxs.z; ry = rys.z; }      // BR
        else                  { rx = rxs.w; ry = rys.w; }     // BL
    }
    const float halfW = max((rect.z - rect.x) * 0.5f, 0.0f);
    const float halfH = max((rect.w - rect.y) * 0.5f, 0.0f);
    rx = min(rx, halfW);
    ry = min(ry, halfH);
    return CoverageRoundRect(pixel, rect, float2(rx, ry));
}

float4 main(PsInput input) : SV_Target
{
    float coverage = 1.0f;

    // Outer rounded clip. Coverage (not boolean discard) so the clip edge is
    // anti-aliased; fwidth() runs before ANY discard (uniform control flow —
    // clipFlags are push constants), keeping helper-lane derivatives defined.
    if (gPushConstants.clipFlags.x > 0.5f) {
        const float perCornerSum = dot(gPushConstants.perCornerRadiusX, float4(1.0f, 1.0f, 1.0f, 1.0f))
                                 + dot(gPushConstants.perCornerRadiusY, float4(1.0f, 1.0f, 1.0f, 1.0f));
        float cov;
        if (perCornerSum > 0.001f) {
            cov = CoveragePerCornerRoundRect(input.position.xy,
                                             gPushConstants.roundedClipRect,
                                             gPushConstants.perCornerRadiusX,
                                             gPushConstants.perCornerRadiusY);
        } else {
            cov = CoverageRoundRect(input.position.xy,
                                    gPushConstants.roundedClipRect,
                                    gPushConstants.roundedClipRadius);
        }
        // clipFlags.x == 2 → INVERSE outer clip: keep the OUTSIDE.
        if (gPushConstants.clipFlags.x > 1.5f) {
            cov = 1.0f - cov;
        }
        coverage = cov;
        if (coverage <= 0.0f) {
            discard;
        }
    }

    // Inner rounded clip: subtractive hole (kept uniform-radius — only border
    // ring geometry arms it, never an ancestor clip).
    if (gPushConstants.clipFlags.y > 0.5f) {
        const float innerCov = CoverageRoundRect(input.position.xy,
                                                 gPushConstants.innerRoundedClipRect,
                                                 gPushConstants.innerRoundedClipRadius);
        coverage = saturate(coverage * (1.0f - innerCov));
        if (coverage <= 0.0f) {
            discard;
        }
    }

    float4 color = bitmapTexture.Sample(bitmapSampler, input.uv);
    // PREMULTIPLIED source + premultiplied-SrcOver pipeline (SrcBlend = ONE):
    // scale EVERY channel by opacity and clip coverage so the texel stays
    // premultiplied. This is the one line that differs from
    // bitmap_quad.frag.hlsl (which scales alpha only for its SRC_ALPHA blend).
    color *= saturate(gPushConstants.uvOpacity.z) * coverage;
    return color;
}
