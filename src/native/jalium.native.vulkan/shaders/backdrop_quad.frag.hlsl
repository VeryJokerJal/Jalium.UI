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

float4 main(PsInput input) : SV_Target
{
    // Quad-local uv [0,1] over the panel -> source-texel uv. The in-app path's
    // uploadImage holds the FULL captured frame, so the panel occupies only the
    // sub-rect (uvRemapOffset .. uvRemapOffset+uvRemapScale). This mirrors D3D12
    // TryDrawSnapshotBackdropQuad's `srcUv = uvRemap.xy + input.uv * uvRemap.zw`.
    // The desktop path pushes identity (offset 0, scale 1), so srcUv == input.uv
    // and its sampling is byte-for-byte unchanged.
    const float2 srcUv = gPushConstants.uvRemapOffset + input.uv * gPushConstants.uvRemapScale;

    const float2 texelStep = float2(gPushConstants.backdropInfo1.z, gPushConstants.backdropInfo1.w);
    const float radius = clamp(gPushConstants.backdropInfo1.y, 0.0f, 8.0f);
    const int blurRadius = min(8, max(0, (int)round(radius)));

    float4 blurred = 0.0f;
    int count = 0;
    for (int dy = -blurRadius; dy <= blurRadius; ++dy) {
        for (int dx = -blurRadius; dx <= blurRadius; ++dx) {
            blurred += sourceTexture.Sample(sourceSampler, srcUv + float2(dx, dy) * texelStep);
            ++count;
        }
    }
    blurred = count > 0 ? blurred / count : sourceTexture.Sample(sourceSampler, srcUv);

    // Per-corner rounded-corner mask, evaluated in PIXEL space. cornerRadii
    // arrive in physical px (the record side bakes the transform scale in).
    // Previously the raw DIP radii were compared against this quad's
    // [-1,1]-normalized space — q = |p| - 1 + r goes positive everywhere once
    // r exceeds ~2, so ANY realistic corner radius discarded the entire
    // backdrop. Working in px units fixes that and gives the mask real
    // geometry to round against.
    const float2 halfSizePx = max(gPushConstants.rect.zw * 0.5f, float2(0.0001f, 0.0001f));
    const float2 centeredPx = (input.uv - 0.5f) * gPushConstants.rect.zw;
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
