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
    float  samplerIdx;  // 0=linear, 1=point/nearest, 2=anisotropic
    float2 _pad;
    float4 xform0;      // m11, m12, m21, m22  (per-instance 2x3 affine)
    float4 xform1;      // dx, dy, _, _
};

StructuredBuffer<BitmapInstance> bitmaps : register(t0);

cbuffer InstanceOffset : register(b1)
{
    uint baseInstanceOffset;
};

struct VsOutput
{
    float4 clipPos     : SV_Position;
    float2 uv          : TEXCOORD0;
    float  opacity     : TEXCOORD1;
    nointerpolation float samplerIdx : TEXCOORD2;
};

VsOutput main(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    static const float2 corners[6] = {
        float2(0, 0), float2(1, 0), float2(1, 1),
        float2(0, 0), float2(1, 1), float2(0, 1)
    };

    BitmapInstance b = bitmaps[instanceId + baseInstanceOffset];
    float2 corner = corners[vertexId];

    // Per-instance 2x3 affine applied to the quad corners — rotation /
    // negative-diagonal (180 deg) / skew now render correctly. The corner-indexed
    // UV below is unchanged, so a 180-deg flip naturally maps uvMax to the
    // opposite screen corner (the content flips, fixing the old upright bug).
    // Identity transform => bit-identical to the legacy axis-aligned quad.
    float2 localPt = b.position + corner * b.size;
    float2 pixelPos = float2(
        localPt.x * b.xform0.x + localPt.y * b.xform0.z + b.xform1.x,
        localPt.x * b.xform0.y + localPt.y * b.xform0.w + b.xform1.y);

    VsOutput o;
    o.clipPos = float4(
        pixelPos.x * invScreenSize.x * 2.0 - 1.0,
        1.0 - pixelPos.y * invScreenSize.y * 2.0,
        0.0, 1.0);
    o.uv = lerp(b.uvMin, b.uvMax, corner);
    o.opacity = b.opacity;
    o.samplerIdx = b.samplerIdx;
    return o;
}
