struct PushConstants
{
    float4 rect;
    float4 glowColor;
    float4 glowInfo1;
    float4 glowInfo2;
    float2 screenSize;
    float2 padding;
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
    const float2 screenSize = max(gPushConstants.screenSize, float2(1.0f, 1.0f));
    const float2 pixel = input.uv * screenSize;
    const float2 center = gPushConstants.rect.xy + gPushConstants.rect.zw * 0.5f;
    const float2 halfSize = gPushConstants.rect.zw * 0.5f;
    const float radius = max(0.0f, gPushConstants.glowInfo1.x);
    const float strokeWidth = max(1.0f, gPushConstants.glowInfo1.y);
    const float dimOpacity = saturate(gPushConstants.glowInfo1.z);
    const float intensity = max(0.0f, gPushConstants.glowInfo1.w);

    const float distanceToBorder = RoundedRectDistance(pixel - center, halfSize, radius);
    const float outsideMask = saturate(sign(distanceToBorder));
    const float borderMask = saturate(1.0 - abs(distanceToBorder) / strokeWidth);
    const float glowMask = exp(-abs(distanceToBorder) / max(1.0, strokeWidth * 2.5));

    float4 color = float4(0.0, 0.0, 0.0, dimOpacity * outsideMask);
    color.rgb += gPushConstants.glowColor.rgb * glowMask * intensity;
    color.a = max(color.a, gPushConstants.glowColor.a * borderMask * intensity);
    return color;
}
