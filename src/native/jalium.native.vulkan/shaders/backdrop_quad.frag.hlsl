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
};

[[vk::push_constant]]
PushConstants gPushConstants;

struct PsInput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

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

float4 main(PsInput input) : SV_Target
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
    const float2 srcUv = gPushConstants.uvRemapOffset +
        panelUv * gPushConstants.uvRemapScale;
    const float2 texelStep = float2(
        gPushConstants.backdropInfo1.z,
        gPushConstants.backdropInfo1.w);
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
    if (RoundedRectDistancePerCorner(centeredPx, halfSizePx, radiiPx) > 0.0f) {
        discard;
    }

    float3 color = lerp(blurred.rgb, gPushConstants.tintColor.rgb, gPushConstants.tintColor.a);
    const float saturation = max(0.0f, gPushConstants.extraInfo.x);
    const float luminance = dot(color, float3(0.299, 0.587, 0.114));
    color = lerp(float3(luminance, luminance, luminance), color, saturation);

    // Luminosity (MicaEffect raises perceived brightness a few percent). Applied
    // after tint+saturation and before noise, field-for-field with the D3D12
    // snapshot-backdrop PS. extraInfo.w == 1 leaves the colour unchanged; it is
    // an INDEPENDENT slot from extraInfo.z (the fallback-alpha-floor switch).
    const float luminosity = max(0.0f, gPushConstants.extraInfo.w);
    color *= luminosity;

    const float noiseIntensity = max(0.0f, gPushConstants.extraInfo.y);
    const float noise = frac(sin(dot(input.position.xy, float2(12.9898, 78.233))) * 43758.5453);
    color += (noise - 0.5) * noiseIntensity * 0.04;

    // A15: the 0.08 + tintA*0.25 alpha FLOOR is a legacy visibility hack for
    // the FALLBACK source only (TRANSFER_SRC capture unavailable → the sampled
    // pixels may be the zero-alpha pixelBuffer_ snapshot, and without the
    // floor the whole backdrop vanished). extraInfo.z == 1 arms it on exactly
    // that branch; the live-capture path passes 0 so the real blurred alpha
    // flows through unmodified instead of being clamped up.
    const float fallbackFloor = saturate(gPushConstants.extraInfo.z);
    const float floorAlpha = (0.08 + gPushConstants.tintColor.a * 0.25) * fallbackFloor;
    return float4(color, max(blurred.a, floorAlpha));
}
