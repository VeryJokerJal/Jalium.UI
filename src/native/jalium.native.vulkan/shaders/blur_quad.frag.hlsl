Texture2D blurTexture : register(t0);
SamplerState blurSampler : register(s1);

struct PushConstants
{
    float4 rect;
    float4 blurInfo1;
    float4 blurInfo2;
    float4 blurTint;
    float2 screenSize;
    float2 padding;
    float4 roundedClipRect;
    float2 roundedClipRadius;
    // clipFlags.x > 0.5 arms the OUTER rounded clip. (.y is currently unused:
    // blur wires only the outer clip + custom-quad geometry CPU-side, never an
    // inner/subtractive clip. Kept as float2 so this block stays byte-identical
    // to blur_quad.vert.hlsl and the C++ BlurPushConstants — 160 bytes.)
    //
    // DO NOT add fragment-only fields here. The blur pipeline layout's
    // VkPushConstantRange is sizeof(BlurPushConstants) and spans BOTH the vertex
    // and fragment stages, so any FS-only field pushes this block past that range
    // and trips VUID-VkGraphicsPipelineCreateInfo-layout-10069 (an inner-clip
    // block copied from bitmap_quad.frag used to do exactly that: FS 192 B vs the
    // 160 B range). If blur ever needs an inner clip, extend the VS block and the
    // C++ struct in lockstep and widen the range.
    float2 clipFlags;
    float4 quadPoint01;
    float4 quadPoint23;
    float2 geometryFlags;
    float2 padding2;
};

[[vk::push_constant]]
PushConstants gPushConstants;

struct PsInput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
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

// sRGB ↔ linear conversion so blurring averages in linear light space.
float SrgbToLinearCh(float s)
{
    return (s <= 0.04045) ? s / 12.92 : pow((s + 0.055) / 1.055, 2.4);
}
float3 SrgbToLinear(float3 s)
{
    return float3(SrgbToLinearCh(s.x), SrgbToLinearCh(s.y), SrgbToLinearCh(s.z));
}
float LinearToSrgbCh(float l)
{
    return (l <= 0.0031308) ? l * 12.92 : 1.055 * pow(l, 1.0 / 2.4) - 0.055;
}
float3 LinearToSrgb(float3 l)
{
    return float3(LinearToSrgbCh(l.x), LinearToSrgbCh(l.y), LinearToSrgbCh(l.z));
}

// ============================================================================
// TRUE Gaussian blur (D3D12 gaussian_blur.cs.hlsl parity, C-beta-3).
//
// D3D12 runs a two-pass SEPARABLE Gaussian (horizontal then vertical) with:
//     sigma        = radius / 3               (floor 0.5)
//     kernelRadius = (int)min(radius, 64)     (floor 1)
//     weight(d)    = exp(-0.5 * (d/sigma)^2)
//     sRGB -> linear on load, normalise by Σweight, linear -> sRGB on store.
//
// A Gaussian is separable, so applying the SAME 1-D kernel along X then Y is
// mathematically identical to convolving with the single 2-D RADIAL kernel
//     w(dx,dy) = exp(-0.5 * (dx^2 + dy^2) / sigma^2)
//              = exp(-0.5*dx^2/sigma^2) * exp(-0.5*dy^2/sigma^2).
// The Vulkan path now executes those same horizontal and vertical passes via
// a per-frame scratch image, matching both the pixels and linear sample cost.
// Each pass performs only (2R+1) samples instead of a quadratic 2-D kernel.
// The former box kernel (uniform weights, radius clamp 12) diverged from the
// Gaussian skirt the wider the radius (effect-blur 8/16/30 FAIL 22.4%).
//
// One tap == one SOURCE pixel: texelStep is 1/pixelWidth, 1/pixelHeight
// (blurInfo1.z, blurInfo1.w) — matching D3D12's k = -kernelRadius..kernelRadius
// integer-pixel stepping. Taps clamp to texel centres inside uvScale, so they
// never read past the isolated element into the unused upload allocation.
// region edge; without this the blurred skirt would darken toward the bounds).
// ============================================================================
#define MAX_KERNEL_RADIUS 64

float4 SampleLinear(float2 uv, float2 uvLo, float2 uvHi)
{
    float4 sampleColor = blurTexture.Sample(blurSampler, clamp(uv, uvLo, uvHi));
    sampleColor.rgb = SrgbToLinear(sampleColor.rgb);
    return sampleColor;
}

float4 Gaussian1D(float2 uv, float2 axisStep, float2 uvLo, float2 uvHi,
                  int kernelRadius, float sigma)
{
    float4 sum = SampleLinear(uv, uvLo, uvHi);
    float weightSum = 1.0f;

    // Generate Gaussian weights with a recurrence. This pays for one exp per
    // pixel instead of one exp per tap:
    //   w(i+1) / w(i) = exp(-(2i+1) / (2*sigma^2)).
    const float baseRatio = exp(-0.5f / (sigma * sigma));
    const float ratioStep = baseRatio * baseRatio;
    float ratio = baseRatio;
    float weight = 1.0f;
    [loop]
    for (int i = 1; i <= MAX_KERNEL_RADIUS; ++i) {
        if (i > kernelRadius) break;
        weight *= ratio;
        ratio *= ratioStep;
        const float2 offset = axisStep * float(i);
        sum += (SampleLinear(uv - offset, uvLo, uvHi) +
                SampleLinear(uv + offset, uvLo, uvHi)) * weight;
        weightSum += 2.0f * weight;
    }
    return sum / max(weightSum, 0.0001f);
}

float Binomial5Weight(int offset)
{
    const int distance = abs(offset);
    return distance == 0 ? 6.0f : (distance == 1 ? 4.0f : 1.0f);
}

// Allocation or format failures should not bring back an unbounded R^2 shader.
// This 5x5 binomial approximation spans the requested radius and is capped at
// 25 taps; the normal path uses the exact separable kernel above.
float4 BoundedGaussian2D(float2 uv, float2 texelStep, float2 uvLo, float2 uvHi,
                         float radius)
{
    if (radius <= 0.001f) return SampleLinear(uv, uvLo, uvHi);
    const float2 stride = texelStep * (radius * 0.5f);
    float4 sum = 0.0f;
    float weightSum = 0.0f;
    [unroll]
    for (int y = -2; y <= 2; ++y) {
        const float wy = Binomial5Weight(y);
        [unroll]
        for (int x = -2; x <= 2; ++x) {
            const float weight = wy * Binomial5Weight(x);
            sum += SampleLinear(uv + float2(float(x), float(y)) * stride,
                                uvLo, uvHi) * weight;
            weightSum += weight;
        }
    }
    return sum / weightSum;
}

float4 main(PsInput input) : SV_Target
{
    if (gPushConstants.clipFlags.x > 0.5f && !IsInsideRoundRect(input.position.xy, gPushConstants.roundedClipRect, gPushConstants.roundedClipRadius)) {
        discard;
    }

    const float radius = clamp(gPushConstants.blurInfo2.x, 0.0f, (float)MAX_KERNEL_RADIUS);

    // One source texel along each axis (matches D3D12's per-pixel k stepping).
    // texelStep MUST be 1/uploadWidth in UV so each tap advances exactly one
    // source texel: blurInfo1.xy = uvScale = pixelWidth/uploadWidth and
    // blurInfo1.zw = 1/pixelWidth, so uvScale*(1/pixelWidth) = 1/uploadWidth.
    // (Using blurInfo1.zw alone = 1/pixelWidth would step uploadWidth/pixelWidth
    // texels/tap whenever the shared upload image is larger than this capture,
    // mis-scaling the kernel — the C-beta-3 first-pass bug.)
    const float2 texelStep = float2(
        gPushConstants.blurInfo1.x * gPushConstants.blurInfo1.z,
        gPushConstants.blurInfo1.y * gPushConstants.blurInfo1.w);

    // Never sample past the isolated capture region (top-left [0, uvScale] of the
    // shared upload image); the apron beyond it is uninitialised/black.
    // Clamp to texel centres inside the valid capture. The shared upload image
    // is grow-only, so sampling [0, uvScale] inclusively can blend stale pixels
    // from the unused allocation immediately to the right/bottom.
    const float2 uvScale = float2(gPushConstants.blurInfo1.x, gPushConstants.blurInfo1.y);
    const float2 uvLo = texelStep * 0.5f;
    const float2 uvHi = max(uvLo, uvScale - texelStep * 0.5f);

    float2 sampleUv = input.uv;
    if (gPushConstants.geometryFlags.y > 0.5f) {
        const float2 panelUv = (input.position.xy - gPushConstants.rect.xy) /
            max(gPushConstants.rect.zw, float2(0.0001f, 0.0001f));
        sampleUv = panelUv * uvScale;
    }

    const float passMode = gPushConstants.blurInfo2.w;
    float4 color;
    if (passMode > 0.5f) {
        const int kernelRadius = min(MAX_KERNEL_RADIUS, max(1, (int)ceil(radius)));
        const float sigma = max(radius / 3.0f, 0.5f);
        const float2 axisStep = passMode < 1.5f
            ? float2(texelStep.x, 0.0f)
            : float2(0.0f, texelStep.y);
        color = radius <= 0.001f
            ? SampleLinear(sampleUv, uvLo, uvHi)
            : Gaussian1D(sampleUv, axisStep, uvLo, uvHi, kernelRadius, sigma);
    } else {
        color = BoundedGaussian2D(sampleUv, texelStep, uvLo, uvHi, radius);
    }
    color.rgb = LinearToSrgb(color.rgb);

    // The blurred source is PREMULTIPLIED (the offscreen/upload content is premul),
    // so `color` is premultiplied here. The blur pipeline now blends with
    // srcColorBlendFactor = ONE (premultiplied SrcOver) — matching the bitmap-premul
    // / ink pipelines — so the fixed-function stage must NOT multiply by alpha again.
    // (The former SRC_ALPHA factor double-attenuated the low-alpha skirt, crushing
    // it darker/tighter than D3D12's gaussian: the effect-blur FAIL.)
    if (gPushConstants.blurInfo2.z > 0.5f) {
        // Tinted silhouette (DropShadow / OuterGlow CPU-pixel fallback): use the
        // blurred alpha as coverage and emit the tint PREMULTIPLIED (tint.a scales
        // the coverage) so the ONE blend composites it correctly.
        float a = color.a * gPushConstants.blurTint.a;
        color = float4(gPushConstants.blurTint.rgb * a, a);
    }
    // Opacity scales the whole PREMULTIPLIED color (rgb and a together).
    color *= saturate(gPushConstants.blurInfo2.y);
    return color;
}
