cbuffer FrameConstants : register(b0)
{
    float2 screenSize;
    float2 invScreenSize;
};

struct VsInput
{
    float2 position : POSITION;
    float4 color    : COLOR0;
};

struct VsOutput
{
    float4 clipPos : SV_Position;
    float4 color   : COLOR0;
};

VsOutput main(VsInput input)
{
    VsOutput o;
    o.clipPos = float4(
        input.position.x * invScreenSize.x * 2.0 - 1.0,
        1.0 - input.position.y * invScreenSize.y * 2.0,
        0.0, 1.0);

    o.color = input.color;
    return o;
}
