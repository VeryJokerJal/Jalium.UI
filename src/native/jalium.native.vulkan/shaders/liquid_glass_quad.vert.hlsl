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

struct VsOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

VsOutput main(uint vertexId : SV_VertexID)
{
    const float2 corners[6] = {
        float2(0.0f, 0.0f),
        float2(1.0f, 0.0f),
        float2(1.0f, 1.0f),
        float2(0.0f, 0.0f),
        float2(1.0f, 1.0f),
        float2(0.0f, 1.0f)
    };

    float2 pixelPosition = gPushConstants.rect.xy + corners[vertexId] * gPushConstants.rect.zw;
    if (gPushConstants.geometryFlags.x > 0.5f) {
        const float2 quadPoints[4] = {
            gPushConstants.quadPoint01.xy,
            gPushConstants.quadPoint01.zw,
            gPushConstants.quadPoint23.xy,
            gPushConstants.quadPoint23.zw
        };

        const uint indices[6] = { 0, 1, 2, 0, 2, 3 };
        pixelPosition = quadPoints[indices[vertexId]];
    }

    const float2 screenSize = max(gPushConstants.screenSize, float2(1.0f, 1.0f));

    VsOutput output;
    output.position = float4(
        pixelPosition.x / screenSize.x * 2.0f - 1.0f,
        1.0f - pixelPosition.y / screenSize.y * 2.0f,
        0.0f,
        1.0f);
    output.uv = corners[vertexId];
    return output;
}
