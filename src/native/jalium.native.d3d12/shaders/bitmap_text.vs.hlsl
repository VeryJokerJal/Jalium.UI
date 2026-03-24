cbuffer FrameConstants : register(b0)
{
    float2 screenSize;
    float2 invScreenSize;
};

struct GlyphInstance
{
    float2 position;
    float2 size;
    float2 uvMin;
    float2 uvMax;
    float4 color;
};

StructuredBuffer<GlyphInstance> glyphs : register(t0);

cbuffer InstanceOffset : register(b1)
{
    uint baseInstanceOffset;
};

struct VsOutput
{
    float4 clipPos : SV_Position;
    float2 uv      : TEXCOORD0;
    float4 color   : COLOR0;
};

VsOutput main(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    static const float2 corners[6] = {
        float2(0, 0), float2(1, 0), float2(1, 1),
        float2(0, 0), float2(1, 1), float2(0, 1)
    };

    GlyphInstance g = glyphs[instanceId + baseInstanceOffset];
    float2 corner = corners[vertexId];
    float2 pixelPos = g.position + corner * g.size;

    VsOutput o;
    o.clipPos = float4(
        pixelPos.x * invScreenSize.x * 2.0 - 1.0,
        1.0 - pixelPos.y * invScreenSize.y * 2.0,
        0.0, 1.0);
    o.uv = lerp(g.uvMin, g.uvMax, corner);
    o.color = g.color;
    return o;
}
