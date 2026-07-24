#include "../../jalium.native.core/shaders/continuous_corner.hlsli"

// Liquid glass — pixel shader.
//
// Full port of the D3D12 authority (kLiquidGlassPS in d3d12_shader_source.h):
// SDF shape (rounded-rect / super-ellipse), depth-zone refraction via circleMap,
// 7-tap chromatic dispersion, vibrancy + tint, mouse / dual-corner highlight,
// inner shadow, outer drop shadow, and (neighborCount>0) smooth-min fusion.
//
// Two differences from D3D12 are handled here:
//  1. Source texture. D3D12 samples a Gaussian-PREBLURRED snapshot (blurredTex).
//     The Vulkan GPU-RT path captures the LIVE, UN-blurred scene into
//     sourceTexture, so every background fetch goes through sampleBlurred(),
//     which reproduces D3D12's pre-blur IN the shader: a full Gaussian evaluated
//     on the integer texel grid in LINEAR light (matching gaussian_blur.cs.hlsl),
//     bilinearly interpolated at the fractional refraction UV — i.e. the same
//     convolve-then-interpolate discretisation as "pre-blur texture + one
//     bilinear fetch", without a second compute pass.
//  2. Blend. D3D12 uses premultiplied blend (Src=ONE, Dst=INV_SRC_ALPHA) and its
//     shader returns premultiplied (rgb*mask, mask). The Vulkan liquid-glass
//     pipeline uses straight-alpha blend (Src=SRC_ALPHA, Dst=INV_SRC_ALPHA), so
//     this shader returns STRAIGHT color (rgb, mask) — algebraically identical
//     after blending. The outer-shadow branch returns (0,0,0,a) which is the
//     same under either convention.

Texture2D sourceTexture : register(t0);
SamplerState sourceSampler : register(s1);

struct PushConstants
{
    float4 rect;          // x, y, w, h (screen px)
    float4 glassInfo1;    // cornerRadius, blurRadius, texelStepX, texelStepY
    float4 glassInfo2;    // refractionAmount, chromaticAberration, refractionHeight, shapeType
    float4 tintColor;     // r, g, b, tintOpacity
    float4 lightInfo;     // lightX, lightY, highlightOpacity, fallbackFloor
    float2 screenSize;    // viewport px
    float2 shapeExtra;    // shapeN, vibrancy
    float4 quadPoint01;   // (VS only)
    float4 quadPoint23;   // (VS only)
    float2 geometryFlags; // (VS only)
    float2 shadowInfo01;  // shadowOffset, shadowRadius
    float2 shadowInfo23;  // shadowOpacity, neighborCount
    float2 fusionInfo;    // fusionRadius, _pad
    float4 n0Rect;
    float4 n1Rect;
    float4 n2Rect;
    float4 n3Rect;
    float4 neighborRadii;
};

[[vk::push_constant]]
PushConstants gPushConstants;

struct PsInput
{
    float4 position  : SV_Position;
    float2 screenPos : TEXCOORD0;
};

// sRGB ↔ linear (must match D3D12 gaussian_blur.cs.hlsl). The captured scene is
// stored in sRGB; averaging in sRGB perceptually DARKENS mixed dark/bright
// regions. D3D12 pre-blurs in linear light, so we replicate that here or the
// refraction source reads ~3x too dark over the high-contrast stripes.
float3 SrgbToLinear(float3 s)
{
    float3 lo = s / 12.92f;
    float3 hi = pow(max((s + 0.055f) / 1.055f, 0.0f), 2.4f);
    return float3(s.x <= 0.04045f ? lo.x : hi.x,
                  s.y <= 0.04045f ? lo.y : hi.y,
                  s.z <= 0.04045f ? lo.z : hi.z);
}
float3 LinearToSrgb(float3 l)
{
    float3 lo = l * 12.92f;
    float3 hi = 1.055f * pow(max(l, 0.0f), 1.0f / 2.4f) - 0.055f;
    return float3(l.x <= 0.0031308f ? lo.x : hi.x,
                  l.y <= 0.0031308f ? lo.y : hi.y,
                  l.z <= 0.0031308f ? lo.z : hi.z);
}

// ── Blur-source sampling ────────────────────────────────────────────────────
// D3D12 pre-blurs the WHOLE snapshot on the integer pixel grid (gaussian_blur.cs
// .hlsl, Load() point taps, LINEAR light) into blurTempA_, then the refraction
// shader does ONE bilinear Sample() of that pre-blurred texture at the (fractional)
// refracted UV. To reproduce that exactly without a second pass we:
//   (1) evaluate the Gaussian at a texel CENTRE (integer grid) — sampleBlurredTexel,
//   (2) bilinearly blend the four texels around the fractional UV — sampleBlurred.
// This is algebraically "convolve-then-interpolate", matching D3D12's discretisation
// (a plain per-fetch Gaussian centred on the fractional UV is "interpolate-then-
// convolve" and leaves a ~4/255 sub-pixel bias across the refraction band).

// Gaussian half-width & sigma shared by all taps. kernelRadius = min(radius,64)
// in D3D12; capped at 12 here to bound the 4x-bilinear x 7-tap dispersion cost.
static int   gBlurK      = 0;
static float gTwoSigma2  = 1.0f;

void setupBlur(float blurRadius)
{
    gBlurK = (int)clamp(round(blurRadius), 0.0f, 12.0f);
    float sigma = max(blurRadius / 3.0f, 0.5f);
    gTwoSigma2 = 2.0f * sigma * sigma;
}

// Full Gaussian evaluated on the integer grid at texel (tx,ty). Returns sRGB
// (linear-space weighting, converted back on output) so it matches blurTempA_.
float4 sampleBlurredTexel(int tx, int ty, float2 texelStep)
{
    float2 c = (float2(tx, ty) + 0.5f) * texelStep;
    if (gBlurK <= 0) {
        return sourceTexture.Sample(sourceSampler, c);
    }
    float3 accRgb = 0.0f;
    float accA = 0.0f;
    float wsum = 0.0f;
    [loop] for (int dy = -gBlurK; dy <= gBlurK; ++dy) {
        float wy = exp(-(float)(dy * dy) / gTwoSigma2);
        [loop] for (int dx = -gBlurK; dx <= gBlurK; ++dx) {
            float wx = exp(-(float)(dx * dx) / gTwoSigma2);
            float w = wx * wy;
            float4 s = sourceTexture.Sample(sourceSampler, c + float2(dx, dy) * texelStep);
            accRgb += w * SrgbToLinear(s.rgb);
            accA += w * s.a;
            wsum += w;
        }
    }
    return wsum > 0.0f ? float4(LinearToSrgb(accRgb / wsum), accA / wsum)
                       : sourceTexture.Sample(sourceSampler, c);
}

// Bilinear blend of the pre-blurred value at the four texels around uv — the
// exact "pre-blur texture + bilinear refraction fetch" D3D12 performs.
float4 sampleBlurred(float2 uv, float2 texelStep, float blurRadius)
{
    if (gBlurK <= 0) {
        return sourceTexture.Sample(sourceSampler, uv);
    }
    float2 tc = uv / texelStep - 0.5f;      // texel-space, centre-origin
    float2 tf = floor(tc);
    float2 fr = tc - tf;
    int x0 = (int)tf.x, y0 = (int)tf.y;
    float4 c00 = sampleBlurredTexel(x0,     y0,     texelStep);
    float4 c10 = sampleBlurredTexel(x0 + 1, y0,     texelStep);
    float4 c01 = sampleBlurredTexel(x0,     y0 + 1, texelStep);
    float4 c11 = sampleBlurredTexel(x0 + 1, y0 + 1, texelStep);
    float4 cx0 = lerp(c00, c10, fr.x);
    float4 cx1 = lerp(c01, c11, fr.x);
    return lerp(cx0, cx1, fr.y);
}

// ── SDF functions (byte-for-byte with the D3D12 authority) ──────────────────
float sdRoundedRect(float2 coord, float2 halfSize, float radius)
{
    float2 cornerCoord = abs(coord) - (halfSize - float2(radius, radius));
    float outside = length(max(cornerCoord, 0.0f)) - radius;
    float inside = min(max(cornerCoord.x, cornerCoord.y), 0.0f);
    return outside + inside;
}

float2 gradSdRoundedRect(float2 coord, float2 halfSize, float radius)
{
    const float e = 0.5f;
    float dx = sdRoundedRect(coord + float2(e, 0), halfSize, radius)
             - sdRoundedRect(coord - float2(e, 0), halfSize, radius);
    float dy = sdRoundedRect(coord + float2(0, e), halfSize, radius)
             - sdRoundedRect(coord - float2(0, e), halfSize, radius);
    float2 g = float2(dx, dy);
    float len = length(g);
    return len > 0.001f ? g / len : float2(0, 1);
}

float sdContinuousCorner(float2 coord, float2 halfSize, float radius, float n)
{
    return JaliumSdContinuousCornerRectUniform(
        coord,
        halfSize,
        min(max(radius, 0.0f), min(halfSize.x, halfSize.y)),
        n);
}

float2 gradSdContinuousCorner(float2 coord, float2 halfSize, float radius, float n)
{
    const float e = 0.5f;
    float dx = sdContinuousCorner(coord + float2(e, 0), halfSize, radius, n)
             - sdContinuousCorner(coord - float2(e, 0), halfSize, radius, n);
    float dy = sdContinuousCorner(coord + float2(0, e), halfSize, radius, n)
             - sdContinuousCorner(coord - float2(0, e), halfSize, radius, n);
    float2 g = float2(dx, dy);
    float len = length(g);
    return len > 0.001f ? g / len : float2(0, 1);
}

float sdShape(float2 coord, float2 halfSize, float radius, float shapeType, float shapeN)
{
    if (shapeType > 0.5f)
        return sdContinuousCorner(coord, halfSize, radius, shapeN);
    return sdRoundedRect(coord, halfSize, radius);
}

float2 gradShape(float2 coord, float2 halfSize, float radius, float shapeType, float shapeN)
{
    if (shapeType > 0.5f)
        return gradSdContinuousCorner(coord, halfSize, radius, shapeN);
    return gradSdRoundedRect(coord, halfSize, radius);
}

float smin(float a, float b, float k)
{
    float h = max(k - abs(a - b), 0.0f) / k;
    return min(a, b) - h * h * k * 0.25f;
}

float neighborSdf(float2 pixelCoord, float4 nRect, float nRadius)
{
    float2 nCenter = nRect.xy + nRect.zw * 0.5f;
    float2 nHalf = nRect.zw * 0.5f;
    float nr = min(nRadius, min(nHalf.x, nHalf.y));
    return sdRoundedRect(pixelCoord - nCenter, nHalf, nr);
}

float evalCombinedSd(float2 pixelCoord, float2 center, float2 halfSize, float r,
                     float shapeType, float shapeN)
{
    float d = sdShape(pixelCoord - center, halfSize, r, shapeType, shapeN);
    int nCount = (int)gPushConstants.shadowInfo23.y;
    float k = gPushConstants.fusionInfo.x;
    if (nCount > 0) d = smin(d, neighborSdf(pixelCoord, gPushConstants.n0Rect, gPushConstants.neighborRadii.x), k);
    if (nCount > 1) d = smin(d, neighborSdf(pixelCoord, gPushConstants.n1Rect, gPushConstants.neighborRadii.y), k);
    if (nCount > 2) d = smin(d, neighborSdf(pixelCoord, gPushConstants.n2Rect, gPushConstants.neighborRadii.z), k);
    if (nCount > 3) d = smin(d, neighborSdf(pixelCoord, gPushConstants.n3Rect, gPushConstants.neighborRadii.w), k);
    return d;
}

float2 gradCombinedSd(float2 pixelCoord, float2 center, float2 halfSize, float r,
                      float shapeType, float shapeN)
{
    const float e = 0.5f;
    float dx = evalCombinedSd(pixelCoord + float2(e, 0), center, halfSize, r, shapeType, shapeN)
             - evalCombinedSd(pixelCoord - float2(e, 0), center, halfSize, r, shapeType, shapeN);
    float dy = evalCombinedSd(pixelCoord + float2(0, e), center, halfSize, r, shapeType, shapeN)
             - evalCombinedSd(pixelCoord - float2(0, e), center, halfSize, r, shapeType, shapeN);
    float2 g = float2(dx, dy);
    float len = length(g);
    return len > 0.001f ? g / len : float2(0, 1);
}

float circleMap(float x)
{
    return 1.0f - sqrt(1.0f - x * x);
}

float3 applyVibrancy(float3 color, float amount)
{
    float luminance = dot(color, float3(0.213f, 0.715f, 0.072f));
    return lerp(float3(luminance, luminance, luminance), color, amount);
}

float4 main(PsInput input) : SV_Target
{
    float2 pixelCoord = input.screenPos;

    const float2 rectXY = gPushConstants.rect.xy;
    const float2 rectWH = gPushConstants.rect.zw;
    const float cornerRadius = gPushConstants.glassInfo1.x;
    const float blurRadius = gPushConstants.glassInfo1.y;
    const float2 texelStep = gPushConstants.glassInfo1.zw;
    const float refractionAmount = gPushConstants.glassInfo2.x;
    const float chromaticAberration = gPushConstants.glassInfo2.y;
    const float refractionHeight = gPushConstants.glassInfo2.z;
    const float shapeType = gPushConstants.glassInfo2.w;
    const float shapeN = gPushConstants.shapeExtra.x;
    const float vibrancy = gPushConstants.shapeExtra.y;
    const float tintOpacity = gPushConstants.tintColor.a;
    const float3 tintRGB = gPushConstants.tintColor.rgb;
    const float highlightOpacity = gPushConstants.lightInfo.z;
    const float lightPosX = gPushConstants.lightInfo.x;
    const float lightPosY = gPushConstants.lightInfo.y;
    const float shadowOffset = gPushConstants.shadowInfo01.x;
    const float shadowRadius = gPushConstants.shadowInfo01.y;
    const float shadowOpacity = gPushConstants.shadowInfo23.x;
    const int nCount = (int)gPushConstants.shadowInfo23.y;
    const float fallbackFloor = saturate(gPushConstants.lightInfo.w);

    // Initialise the shared Gaussian kernel (used by sampleBlurred / *Texel).
    setupBlur(blurRadius);

    // Glass panel geometry (screen px).
    float2 glassCenter = rectXY + rectWH * 0.5f;
    float2 halfSize = rectWH * 0.5f;
    float2 centered = pixelCoord - glassCenter;
    float r = min(cornerRadius, min(halfSize.x, halfSize.y));

    // Combined SDF (self + neighbours via smooth min).
    float sd;
    float selfSd;
    if (nCount > 0) {
        sd = evalCombinedSd(pixelCoord, glassCenter, halfSize, r, shapeType, shapeN);
        selfSd = sdShape(centered, halfSize, r, shapeType, shapeN);
    } else {
        sd = sdShape(centered, halfSize, r, shapeType, shapeN);
        selfSd = sd;
    }

    // Voronoi ownership for fused panels.
    if (nCount > 0 && selfSd > 0.0f) {
        float minNSd = 1e10f;
        float2 closestNC = float2(0, 0);
        if (nCount > 0) { float d = neighborSdf(pixelCoord, gPushConstants.n0Rect, gPushConstants.neighborRadii.x); if (d < minNSd) { minNSd = d; closestNC = gPushConstants.n0Rect.xy + gPushConstants.n0Rect.zw * 0.5f; } }
        if (nCount > 1) { float d = neighborSdf(pixelCoord, gPushConstants.n1Rect, gPushConstants.neighborRadii.y); if (d < minNSd) { minNSd = d; closestNC = gPushConstants.n1Rect.xy + gPushConstants.n1Rect.zw * 0.5f; } }
        if (nCount > 2) { float d = neighborSdf(pixelCoord, gPushConstants.n2Rect, gPushConstants.neighborRadii.z); if (d < minNSd) { minNSd = d; closestNC = gPushConstants.n2Rect.xy + gPushConstants.n2Rect.zw * 0.5f; } }
        if (nCount > 3) { float d = neighborSdf(pixelCoord, gPushConstants.n3Rect, gPushConstants.neighborRadii.w); if (d < minNSd) { minNSd = d; closestNC = gPushConstants.n3Rect.xy + gPushConstants.n3Rect.zw * 0.5f; } }

        if (minNSd < selfSd)
            return float4(0, 0, 0, 0);
        if (minNSd - selfSd < 0.5f) {
            bool yield_ = (glassCenter.x > closestNC.x + 0.01f) ||
                          (abs(glassCenter.x - closestNC.x) <= 0.01f && glassCenter.y > closestNC.y + 0.01f);
            if (yield_) return float4(0, 0, 0, 0);
        }
    }

    float aaW = max(fwidth(sd), 0.5f);

    // ── OUTSIDE GLASS: outer shadow ──
    if (sd > aaW) {
        float2 shadowOff = float2(0.0f, 4.0f);
        float sdShadow;
        if (nCount > 0) {
            sdShadow = evalCombinedSd(pixelCoord - shadowOff, glassCenter, halfSize, r, shapeType, shapeN);
        } else {
            sdShadow = sdShape(centered - shadowOff, halfSize, r, shapeType, shapeN);
        }
        float outerShadow = 0.0f;
        if (sdShadow > 0.0f) {
            outerShadow = smoothstep(24.0f, 0.0f, sdShadow) * 0.1f;
        }
        return float4(0, 0, 0, outerShadow);
    }

    float glassMask = 1.0f - smoothstep(-aaW, aaW, sd);

    // ── REFRACTION ──
    float4 refracted;
    float2 baseUV = pixelCoord * texelStep;

    if (-sd < refractionHeight) {
        float sdClamped = min(sd, 0.0f);
        float2 grad;
        if (nCount > 0) {
            grad = gradCombinedSd(pixelCoord, glassCenter, halfSize, r, shapeType, shapeN);
        } else {
            float gradR = shapeType > 0.5f ? r
                : min(r * 1.5f, min(halfSize.x, halfSize.y));
            grad = normalize(gradShape(centered, halfSize, gradR, shapeType, shapeN));
        }

        float d = circleMap(1.0f - (-sdClamped) / refractionHeight) * (-refractionAmount);

        float2 normalizedCenter = centered / max(length(centered), 0.001f);
        float depthBlend = (nCount > 0) ? saturate(-selfSd / 8.0f) : 1.0f;
        grad = normalize(grad + normalizedCenter * depthBlend);

        float2 displacement = d * grad;
        float2 refractedUV = baseUV + displacement * texelStep;

        if (chromaticAberration > 0.01f) {
            float dispersionIntensity = chromaticAberration *
                ((centered.x * centered.y) / max(halfSize.x * halfSize.y, 1.0f));
            float2 dispersedUV = (d * grad * dispersionIntensity) * texelStep;

            refracted = float4(0, 0, 0, 0);

            float4 red    = sampleBlurred(refractedUV + dispersedUV, texelStep, blurRadius);
            refracted.r += red.r / 3.5f; refracted.a += red.a / 7.0f;

            float4 orange = sampleBlurred(refractedUV + dispersedUV * (2.0f / 3.0f), texelStep, blurRadius);
            refracted.r += orange.r / 3.5f; refracted.g += orange.g / 7.0f; refracted.a += orange.a / 7.0f;

            float4 yellow = sampleBlurred(refractedUV + dispersedUV * (1.0f / 3.0f), texelStep, blurRadius);
            refracted.r += yellow.r / 3.5f; refracted.g += yellow.g / 3.5f; refracted.a += yellow.a / 7.0f;

            float4 green  = sampleBlurred(refractedUV, texelStep, blurRadius);
            refracted.g += green.g / 3.5f; refracted.a += green.a / 7.0f;

            float4 cyan   = sampleBlurred(refractedUV - dispersedUV * (1.0f / 3.0f), texelStep, blurRadius);
            refracted.g += cyan.g / 3.5f; refracted.b += cyan.b / 3.0f; refracted.a += cyan.a / 7.0f;

            float4 blue   = sampleBlurred(refractedUV - dispersedUV * (2.0f / 3.0f), texelStep, blurRadius);
            refracted.b += blue.b / 3.0f; refracted.a += blue.a / 7.0f;

            float4 purple = sampleBlurred(refractedUV - dispersedUV, texelStep, blurRadius);
            refracted.r += purple.r / 7.0f; refracted.b += purple.b / 3.0f; refracted.a += purple.a / 7.0f;
        } else {
            refracted = sampleBlurred(refractedUV, texelStep, blurRadius);
        }
    } else {
        refracted = sampleBlurred(baseUV, texelStep, blurRadius);
    }

    // ── VIBRANCY + TINT ──
    refracted.rgb = applyVibrancy(refracted.rgb, vibrancy);
    refracted.rgb = lerp(refracted.rgb, tintRGB, tintOpacity);

    // ── HIGHLIGHT ──
    float edgeDist = -sd;
    float strokeCenter = 0.75f;
    float blurSigma = 0.5f;
    float strokeIntensity = exp(-((edgeDist - strokeCenter) * (edgeDist - strokeCenter)) / (2.0f * blurSigma * blurSigma));
    float glowIntensity = exp(-(edgeDist * edgeDist) / 18.0f) * 0.15f;
    float totalHighlight = strokeIntensity + glowIntensity;

    if (totalHighlight > 0.005f) {
        float2 hlGrad;
        if (nCount > 0) {
            hlGrad = gradCombinedSd(pixelCoord, glassCenter, halfSize, r, shapeType, shapeN);
        } else {
            float gradR2 = shapeType > 0.5f ? r
                : min(r * 1.5f, min(halfSize.x, halfSize.y));
            hlGrad = normalize(gradShape(centered, halfSize, gradR2, shapeType, shapeN));
        }

        float lightMod;
        if (lightPosX >= 0.0f) {
            float2 lightPos = float2(lightPosX, lightPosY);
            float2 toLight = lightPos - pixelCoord;
            float lightDist = length(toLight);
            float2 lightDir = normalize(toLight);

            float dirFactor = dot(hlGrad, lightDir);
            lightMod = smoothstep(-0.3f, 1.0f, dirFactor);

            float falloffRadius = max(halfSize.x, halfSize.y) * 1.5f;
            float radialFalloff = 1.0f - saturate(lightDist / falloffRadius);
            radialFalloff = radialFalloff * radialFalloff;

            float spec = exp(-(lightDist * lightDist) / (falloffRadius * falloffRadius * 0.15f)) * 0.6f;

            lightMod = lightMod * (radialFalloff * 0.8f + 0.2f) + spec;
        } else {
            float2 lightDir1 = normalize(float2(-1.0f, -1.0f));
            float dir1 = dot(hlGrad, lightDir1);
            float hl1 = smoothstep(-0.2f, 1.0f, dir1);

            float2 lightDir2 = normalize(float2(1.0f, 1.0f));
            float dir2 = dot(hlGrad, lightDir2);
            float hl2 = smoothstep(-0.2f, 1.0f, dir2) * 0.5f;

            lightMod = lerp(0.15f, 0.31f, max(hl1, hl2));
        }

        float hlAlpha = totalHighlight * lightMod * highlightOpacity;
        refracted.rgb += float3(hlAlpha, hlAlpha, hlAlpha);
    }

    // ── INNER SHADOW ──
    float sdOffset;
    if (nCount > 0) {
        sdOffset = evalCombinedSd(pixelCoord + float2(0.0f, shadowOffset), glassCenter, halfSize, r, shapeType, shapeN);
    } else {
        float2 shOff = float2(0.0f, shadowOffset);
        sdOffset = sdShape(centered + shOff, halfSize, r, shapeType, shapeN);
    }

    float shIntensity = 0.0f;
    if (sdOffset > -shadowRadius) {
        shIntensity = smoothstep(-shadowRadius, 0.0f, sdOffset);
    }
    float edgeShadow = smoothstep(shadowRadius, 0.0f, -selfSd);
    float edgeMask = smoothstep(0.0f, 4.0f, -selfSd);
    float totalShadow = max(shIntensity, edgeShadow * 0.2f) * shadowOpacity * edgeMask;
    refracted.rgb = lerp(refracted.rgb, float3(0, 0, 0), totalShadow);

    // ── COMPOSITE (straight alpha; see header note) ──
    float3 outRgb = max(refracted.rgb, 0.0f);
    float outA = glassMask;

    // Fallback visibility floor: only armed when the GPU-RT live capture was
    // unavailable (fallbackFloor == 1), so the sampled scene may be all-zero.
    // The live-capture path passes 0 and this is a strict no-op.
    outA = max(outA, (0.15f + tintOpacity * 0.35f) * fallbackFloor * glassMask);

    return float4(outRgb, outA);
}
