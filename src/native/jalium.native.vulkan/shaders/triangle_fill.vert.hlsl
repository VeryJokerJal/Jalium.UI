struct PushConstants
{
    float4 color;
    float2 screenSize;
    float2 padding;
    float4 roundedClipRect;
    float2 roundedClipRadius;
    float2 clipFlags;
};

[[vk::push_constant]]
PushConstants gPushConstants;

struct VsInput
{
    float2 position : POSITION;
};

struct VsOutput
{
    float4 position : SV_Position;
};

VsOutput main(VsInput input)
{
    const float2 screenSize = max(gPushConstants.screenSize, float2(1.0f, 1.0f));

    VsOutput output;
    output.position = float4(
        input.position.x / screenSize.x * 2.0f - 1.0f,
        1.0f - input.position.y / screenSize.y * 2.0f,
        0.0f,
        1.0f);
    return output;
}
