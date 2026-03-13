Texture2D sourceTexture : register(t0);
SamplerState sourceSampler : register(s1);

struct PushConstants
{
    float4 rect;
    float4 backdropInfo1;
    float4 tintColor;
    float4 extraInfo;
    float2 screenSize;
    float2 padding;
    float4 cornerRadii;
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
    const float2 texelStep = float2(gPushConstants.backdropInfo1.z, gPushConstants.backdropInfo1.w);
    const float radius = clamp(gPushConstants.backdropInfo1.y, 0.0f, 8.0f);
    const int blurRadius = min(8, max(0, (int)round(radius)));

    float4 blurred = 0.0f;
    int count = 0;
    for (int dy = -blurRadius; dy <= blurRadius; ++dy) {
        for (int dx = -blurRadius; dx <= blurRadius; ++dx) {
            blurred += sourceTexture.Sample(sourceSampler, input.uv + float2(dx, dy) * texelStep);
            ++count;
        }
    }
    blurred = count > 0 ? blurred / count : sourceTexture.Sample(sourceSampler, input.uv);

    const float2 centered = input.uv * 2.0f - 1.0f;
    if (RoundedRectDistancePerCorner(centered, float2(1.0f, 1.0f), gPushConstants.cornerRadii) > 0.0f) {
        discard;
    }

    float3 color = lerp(blurred.rgb, gPushConstants.tintColor.rgb, gPushConstants.tintColor.a);
    const float saturation = max(0.0f, gPushConstants.extraInfo.x);
    const float luminance = dot(color, float3(0.299, 0.587, 0.114));
    color = lerp(float3(luminance, luminance, luminance), color, saturation);

    const float noiseIntensity = max(0.0f, gPushConstants.extraInfo.y);
    const float noise = frac(sin(dot(input.position.xy, float2(12.9898, 78.233))) * 43758.5453);
    color += (noise - 0.5) * noiseIntensity * 0.04;

    return float4(color, max(blurred.a, 0.08 + gPushConstants.tintColor.a * 0.25));
}
