Texture2D sourceTexture : register(t0);
SamplerState sourceSampler : register(s1);

struct PushConstants
{
    float4 rect;
    float4 glassInfo1;
    float4 glassInfo2;
    float4 tintColor;
    float4 lightInfo;
    float2 screenSize;
    float2 padding;
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

float RoundedRectDistance(float2 p, float2 halfSize, float radius)
{
    float2 q = abs(p) - halfSize + radius;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
}

float4 main(PsInput input) : SV_Target
{
    const float2 uv = input.uv;
    const float2 texelStep = float2(gPushConstants.glassInfo1.z, gPushConstants.glassInfo1.w);
    const float radius = clamp(gPushConstants.glassInfo1.y, 0.0f, 8.0f);
    const int blurRadius = min(8, max(0, (int)round(radius)));

    float4 blurred = 0.0f;
    int count = 0;
    for (int dy = -blurRadius; dy <= blurRadius; ++dy) {
        for (int dx = -blurRadius; dx <= blurRadius; ++dx) {
            blurred += sourceTexture.Sample(sourceSampler, uv + float2(dx, dy) * texelStep);
            ++count;
        }
    }
    blurred = count > 0 ? blurred / count : sourceTexture.Sample(sourceSampler, uv);

    const float2 centered = uv * 2.0f - 1.0f;
    const float2 halfSize = float2(1.0f, 1.0f);
    const float cornerRadius = clamp(gPushConstants.glassInfo1.x, 0.0f, 1.0f);
    const float signedDistance = RoundedRectDistance(centered, halfSize, cornerRadius);
    if (signedDistance > 0.0f) {
        discard;
    }

    const float2 normal = normalize(centered + 0.0001);
    const float refraction = gPushConstants.glassInfo2.x * 0.02;
    const float chroma = gPushConstants.glassInfo2.y * 0.01;
    float2 refractedUv = uv + normal * refraction;

    float r = sourceTexture.Sample(sourceSampler, refractedUv + normal * chroma).r;
    float g = sourceTexture.Sample(sourceSampler, refractedUv).g;
    float b = sourceTexture.Sample(sourceSampler, refractedUv - normal * chroma).b;
    float4 refracted = float4(r, g, b, blurred.a);

    float4 glass = lerp(blurred, refracted, 0.6);
    glass.rgb = lerp(glass.rgb, gPushConstants.tintColor.rgb, gPushConstants.tintColor.a);

    const float2 lightPos = gPushConstants.lightInfo.xy;
    const float highlight = saturate(1.0 - distance(uv, lightPos)) * gPushConstants.lightInfo.z;
    const float edge = saturate(1.0 - abs(signedDistance) * 20.0f);
    glass.rgb += (highlight * edge) * 0.25;
    glass.a = saturate(max(glass.a, 0.15 + gPushConstants.tintColor.a * 0.35));
    return glass;
}
