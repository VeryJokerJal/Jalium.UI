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

struct VsOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

VsOutput main(uint vertexId : SV_VertexID)
{
    const float2 positions[3] = {
        float2(-1.0f, -1.0f),
        float2(-1.0f,  3.0f),
        float2( 3.0f, -1.0f)
    };

    const float2 uvs[3] = {
        float2(0.0f, 0.0f),
        float2(0.0f, 2.0f),
        float2(2.0f, 0.0f)
    };

    VsOutput output;
    output.position = float4(positions[vertexId], 0.0f, 1.0f);
    output.uv = uvs[vertexId];
    return output;
}
