cbuffer FrameConstants : register(b0)
{
    float2 screenSize;
    float2 invScreenSize;
};

struct BitmapInstance
{
    float2 position;
    float2 size;
    float2 uvMin;
    float2 uvMax;
    float  opacity;
    float3 _pad;
};

StructuredBuffer<BitmapInstance> bitmaps : register(t0);

cbuffer InstanceOffset : register(b1)
{
    uint baseInstanceOffset;
};

struct VsOutput
{
    float4 clipPos : SV_Position;
    float2 uv      : TEXCOORD0;
    float  opacity : TEXCOORD1;
};

VsOutput main(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    static const float2 corners[6] = {
        float2(0, 0), float2(1, 0), float2(1, 1),
        float2(0, 0), float2(1, 1), float2(0, 1)
    };

    BitmapInstance b = bitmaps[instanceId + baseInstanceOffset];
    float2 corner = corners[vertexId];
    float2 pixelPos = b.position + corner * b.size;

    VsOutput o;
    o.clipPos = float4(
        pixelPos.x * invScreenSize.x * 2.0 - 1.0,
        1.0 - pixelPos.y * invScreenSize.y * 2.0,
        0.0, 1.0);
    o.uv = lerp(b.uvMin, b.uvMax, corner);
    o.opacity = b.opacity;
    return o;
}
