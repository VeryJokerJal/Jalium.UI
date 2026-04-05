cbuffer ShaderGeometry : register(b1)
{
    float4 rect;        // x, y, width, height (DIPs)
    float2 screenSize;  // viewport size in DIPs
    float2 _pad;
};

struct VsOutput
{
    float4 clipPos : SV_Position;
    float2 uv      : TEXCOORD0;
};

VsOutput main(uint vertexId : SV_VertexID)
{
    static const float2 corners[6] = {
        float2(0, 0), float2(1, 0), float2(1, 1),
        float2(0, 0), float2(1, 1), float2(0, 1)
    };

    float2 corner = corners[vertexId];
    float2 pixelPos = rect.xy + corner * rect.zw;

    VsOutput o;
    o.clipPos = float4(
        pixelPos.x / screenSize.x * 2.0 - 1.0,
        1.0 - pixelPos.y / screenSize.y * 2.0,
        0.0, 1.0);
    o.uv = corner;
    return o;
}
