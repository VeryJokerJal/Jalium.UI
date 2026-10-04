#define JALIUM_VULKAN_CLIP
#include "../../jalium.native.core/shaders/elliptical_clip.hlsli"

Texture2D sourceTexture : register(t0);
SamplerState sourceSampler : register(s1);

struct PushConstants
{
    float4 rect;
    float4 backdropInfo1;
    float4 tintColor;
    float4 extraInfo;
    float2 screenSize;
    float2 uvRemapOffset;   // (was padding) source-uv offset for the panel sub-rect
    float4 cornerRadii;
    float4 quadPoint01;
    float4 quadPoint23;
    float2 geometryFlags;
    float2 uvRemapScale;    // (was padding2) source-uv scale over the panel quad
    // Material colour pipeline (field-for-field with the D3D12
    // snapshot-backdrop PS). Appended to BOTH the VS and FS blocks — the
    // pipeline layout's push range spans both stages (VUID-10069).
    float4 materialInfo0;   // x=brightness y=contrast z=hueRotation (radians) w=opacity
    float4 materialInfo1;   // x=grayscale y=sepia z=invert w=frost jitter (source texels)
};

[[vk::push_constant]]
PushConstants gPushConstants;

struct PsInput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

// lowbias32 integer hash on the device pixel: stable per pixel, no visible
// lattice, identical in the D3D12 snapshot-backdrop PS so the grain matches.
uint HashPixel(uint2 p, uint salt)
{
    uint n = p.x * 1597334677u ^ p.y * 3812015801u ^ salt * 2654435761u;
    n ^= n >> 16;
    n *= 0x7feb352du;
    n ^= n >> 15;
    n *= 0x846ca68bu;
    n ^= n >> 16;
    return n;
}

float Hash01(uint2 p, uint salt)
{
    return (float)HashPixel(p, salt) * (1.0f / 4294967295.0f);
}

// Colour pipeline: the SAME order on every backend (D3D12 snapshot-backdrop
// PS, the software backend). A single CSS backdrop-filter function maps to one
// stage; ordered CSS lists need a separate pipeline:
//   blur -> brightness -> contrast -> saturation -> hueRotation -> grayscale
//        -> sepia -> invert -> tint -> luminosity -> noise
float3 ApplyColorPipeline(float3 color)
{
    color *= max(gPushConstants.materialInfo0.x, 0.0f);
    color = (color - 0.5f) * max(gPushConstants.materialInfo0.y, 0.0f) + 0.5f;

    const float saturation = max(0.0f, gPushConstants.extraInfo.x);
    float luma = dot(color, float3(0.213f, 0.715f, 0.072f));
    color = lerp(float3(luma, luma, luma), color, saturation);

    const float hue = gPushConstants.materialInfo0.z;
    if (abs(hue) > 0.0001f) {
        const float c = cos(hue);
        const float s = sin(hue);
        color = float3(
            dot(color, float3(0.213f + 0.787f * c - 0.213f * s,
                              0.715f - 0.715f * c - 0.715f * s,
                              0.072f - 0.072f * c + 0.928f * s)),
            dot(color, float3(0.213f - 0.213f * c + 0.143f * s,
                              0.715f + 0.285f * c + 0.140f * s,
                              0.072f - 0.072f * c - 0.283f * s)),
            dot(color, float3(0.213f - 0.213f * c - 0.787f * s,
                              0.715f - 0.715f * c + 0.715f * s,
                              0.072f + 0.928f * c + 0.072f * s)));
    }

    luma = dot(color, float3(0.2126f, 0.7152f, 0.0722f));
    color = lerp(color, float3(luma, luma, luma), saturate(gPushConstants.materialInfo1.x));

    const float3 sepia = float3(
        dot(color, float3(0.393f, 0.769f, 0.189f)),
        dot(color, float3(0.349f, 0.686f, 0.168f)),
        dot(color, float3(0.272f, 0.534f, 0.131f)));
    color = lerp(color, sepia, saturate(gPushConstants.materialInfo1.y));

    color = lerp(color, 1.0f - color, saturate(gPushConstants.materialInfo1.z));

    color = lerp(color, gPushConstants.tintColor.rgb, saturate(gPushConstants.tintColor.a));

    // Luminosity (MicaEffect raises perceived brightness a few percent).
    // extraInfo.w is an INDEPENDENT slot from extraInfo.z (the fallback
    // alpha-floor switch).
    color *= max(0.0f, gPushConstants.extraInfo.w);
    return color;
}

float RoundedRectDistancePerCorner(float2 p, float2 halfSize, float4 radii)
{
    float radius = radii.x;
    if (p.x > 0.0 && p.y < 0.0) radius = radii.y;
    else if (p.x > 0.0 && p.y > 0.0) radius = radii.z;
    else if (p.x < 0.0 && p.y > 0.0) radius = radii.w;
    radius = max(radius, 0.0);
    float2 q = abs(p) - halfSize + radius;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
}

float SrgbToLinearCh(float value)
{
    return value <= 0.04045f ? value / 12.92f
        : pow((value + 0.055f) / 1.055f, 2.4f);
}

float3 SrgbToLinear(float3 value)
{
    return float3(
        SrgbToLinearCh(value.x),
        SrgbToLinearCh(value.y),
        SrgbToLinearCh(value.z));
}

float LinearToSrgbCh(float value)
{
    value = max(value, 0.0f);
    return value <= 0.0031308f ? value * 12.92f
        : 1.055f * pow(value, 1.0f / 2.4f) - 0.055f;
}

float3 LinearToSrgb(float3 value)
{
    return float3(
        LinearToSrgbCh(value.x),
        LinearToSrgbCh(value.y),
        LinearToSrgbCh(value.z));
}

float4 SampleLinear(float2 uv, float2 uvLo, float2 uvHi)
{
    float4 sampleColor = sourceTexture.Sample(sourceSampler, clamp(uv, uvLo, uvHi));
    sampleColor.rgb = SrgbToLinear(sampleColor.rgb);
    return sampleColor;
}

float4 GaussianVertical(float2 uv, float2 texelStep, float2 uvLo, float2 uvHi,
                        float radius)
{
    if (radius <= 0.001f) return SampleLinear(uv, uvLo, uvHi);
    const int kernelRadius = min(64, max(1, (int)ceil(radius)));
    const float sigma = max(radius / 3.0f, 0.5f);
    const float baseRatio = exp(-0.5f / (sigma * sigma));
    const float ratioStep = baseRatio * baseRatio;
    float ratio = baseRatio;
    float weight = 1.0f;
    float weightSum = 1.0f;
    float4 sum = SampleLinear(uv, uvLo, uvHi);
    [loop]
    for (int i = 1; i <= 64; ++i) {
        if (i > kernelRadius) break;
        weight *= ratio;
        ratio *= ratioStep;
        const float2 offset = float2(0.0f, texelStep.y * float(i));
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

float4 UnclippedMain(PsInput input) : SV_Target
{
    // Panel uv [0,1] -> source-texel uv. The sampled image holds either a
    // cropped live capture (plus blur apron) or a compatibility snapshot, so
    // the panel occupies only uvRemapOffset .. uvRemapOffset+uvRemapScale.
    // The desktop path pushes identity (offset 0, scale 1), so srcUv == input.uv
    // and its sampling is byte-for-byte unchanged.
    // A live backdrop samples the screen-space scene, not texture coordinates
    // attached to the rotated/skewed panel quad. For an axis-aligned panel this
    // equals input.uv; for a custom quad SV_Position keeps every pixel aligned
    // with the captured backdrop AABB. geometryFlags.y is negative for an
    // in-app screen-space source (live or CPU compatibility snapshot) and
    // positive for a desktop/local source; its magnitude is validUvScale.y.
    const bool isScreenSpaceSource = gPushConstants.geometryFlags.y < 0.0f;
    float2 panelUv = input.uv;
    if (isScreenSpaceSource) {
        panelUv = (input.position.xy - gPushConstants.rect.xy) /
            max(gPushConstants.rect.zw, float2(0.0001f, 0.0001f));
    }
    float2 srcUv = gPushConstants.uvRemapOffset +
        panelUv * gPushConstants.uvRemapScale;
    const float2 texelStep = float2(
        gPushConstants.backdropInfo1.z,
        gPushConstants.backdropInfo1.w);
    const uint2 pixel = (uint2)input.position.xy;

    // Frosted kernel: jitter the resample position per pixel so the smooth
    // Gaussian picks up a fine frost grain that tracks the content beneath.
    const float frost = max(gPushConstants.materialInfo1.w, 0.0f);
    if (frost > 0.0f) {
        const float2 jitter = float2(Hash01(pixel, 11u), Hash01(pixel, 29u)) - 0.5f;
        srcUv += jitter * frost * texelStep;
    }
    const float radius = clamp(gPushConstants.backdropInfo1.y, 0.0f, 64.0f);
    const bool isVerticalPass = gPushConstants.backdropInfo1.x < 0.0f;
    const float2 validUvScale = float2(
        abs(gPushConstants.backdropInfo1.x),
        abs(gPushConstants.geometryFlags.y));
    const float2 uvLo = texelStep * 0.5f;
    const float2 uvHi = max(uvLo, validUvScale - texelStep * 0.5f);

    float4 blurred;
    if (isVerticalPass) {
        blurred = GaussianVertical(srcUv, texelStep, uvLo, uvHi, radius);
    } else {
        blurred = BoundedGaussian2D(srcUv, texelStep, uvLo, uvHi, radius);
    }
    blurred.rgb = LinearToSrgb(blurred.rgb);

    // Per-corner rounded-corner mask, evaluated in PIXEL space. cornerRadii
    // arrive in physical px (the record side bakes the transform scale in).
    // Previously the raw DIP radii were compared against this quad's
    // [-1,1]-normalized space — q = |p| - 1 + r goes positive everywhere once
    // r exceeds ~2, so ANY realistic corner radius discarded the entire
    // backdrop. Working in px units fixes that and gives the mask real
    // geometry to round against.
    float2 panelSizePx = gPushConstants.rect.zw;
    if (gPushConstants.geometryFlags.x > 0.5f) {
        panelSizePx = float2(
            length(gPushConstants.quadPoint01.zw - gPushConstants.quadPoint01.xy),
            length(gPushConstants.quadPoint23.zw - gPushConstants.quadPoint01.xy));
    }
    const float2 halfSizePx = max(panelSizePx * 0.5f, float2(0.0001f, 0.0001f));
    const float2 centeredPx = (input.uv - 0.5f) * panelSizePx;
    const float maxRadiusPx = min(halfSizePx.x, halfSizePx.y);
    const float4 radiiPx = clamp(gPushConstants.cornerRadii, 0.0f, maxRadiusPx);
    // Anti-aliased silhouette: the SDF is in pixel units, so a one-pixel
    // smoothstep around the edge gives the same coverage ramp the D3D12
    // shader derives from fwidth(). Fully outside pixels still discard so
    // the blend never touches them.
    const float cornerDist = RoundedRectDistancePerCorner(centeredPx, halfSizePx, radiiPx);
    const float cornerCov = 1.0f - smoothstep(-0.5f, 0.5f, cornerDist);
    if (cornerCov <= 0.0f) {
        discard;
    }

    float3 color = ApplyColorPipeline(blurred.rgb);

    // Full-range hash grain mixed at noiseIntensity (no 0.04 attenuation: the
    // intensity IS the amplitude, 0.02-0.05 reads as Acrylic film grain).
    const float noiseIntensity = max(0.0f, gPushConstants.extraInfo.y);
    if (noiseIntensity > 0.0f) {
        color += (Hash01(pixel, 3u) - 0.5f) * noiseIntensity;
    }
    color = saturate(color);

    // A15: the 0.08 + tintA*0.25 alpha FLOOR is a legacy visibility hack for
    // the FALLBACK source only (TRANSFER_SRC capture unavailable → the sampled
    // pixels may be the zero-alpha pixelBuffer_ snapshot, and without the
    // floor the whole backdrop vanished). extraInfo.z == 1 arms it on exactly
    // that branch; the live-capture path passes 0 so the real blurred alpha
    // flows through unmodified instead of being clamped up.
    const float fallbackFloor = gPushConstants.tintColor.a > 0.0f
        ? saturate(gPushConstants.extraInfo.z) : 0.0f;
    const float floorAlpha = (0.08 + gPushConstants.tintColor.a * 0.25) * fallbackFloor;
    const float opacity = saturate(gPushConstants.materialInfo0.w);
    return float4(color, max(blurred.a, floorAlpha) * cornerCov * opacity);
}

float4 main(PsInput input) : SV_Target
{
    float coverage = JaliumVulkanClipCoverage(input.position.xy);
    float4 color = UnclippedMain(input);
    color.a *= coverage;
    return color;
}
