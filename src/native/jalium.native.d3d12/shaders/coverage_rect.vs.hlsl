cbuffer FrameConstants : register(b0)
{
    float2 screenSize;
    float2 invScreenSize;
};

struct Instance
{
    float2 position;
    float2 size;
    float4 fillColor;
    float4 xform0;
    float2 translation;
    float exponent;
    float opacity;
};
ByteAddressBuffer instances : register(t0);
cbuffer InstanceOffset : register(b1)
{
    uint baseInstanceOffset;
    uint indexTableByteOffset;
};

struct VsOutput
{
    float4 clipPos : SV_Position;
    float2 localPos : TEXCOORD0;
    nointerpolation float2 rectSize : TEXCOORD1;
    float4 fillColor : COLOR0;
    nointerpolation float3 shapeParams : TEXCOORD2; // exponent, AA scale, coverage mode
};

VsOutput main(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    static const float2 corners[6] = {
        float2(0, 0), float2(1, 0), float2(1, 1),
        float2(0, 0), float2(1, 1), float2(0, 1)
    };
    uint taggedOffset = instances.Load(indexTableByteOffset + (instanceId + baseInstanceOffset) * 4);
    uint byteOffset = taggedOffset & ~1u;
    float4 bounds = asfloat(instances.Load4(byteOffset));
    Instance inst;
    inst.position = bounds.xy;
    inst.size = bounds.zw;
    inst.fillColor = asfloat(instances.Load4(byteOffset + 16));
    VsOutput o = (VsOutput)0;
    float2 pixelPos;
    [branch]
    if ((taggedOffset & 1u) == 0)
    {
        // Coverage has already been transformed and antialiased on the CPU.
        pixelPos = inst.position + corners[vertexId] * inst.size;
        o.fillColor = inst.fillColor;
        o.shapeParams.z = 2.0;
    }
    else
    {
        inst.xform0 = asfloat(instances.Load4(byteOffset + 32));
        float4 xform1 = asfloat(instances.Load4(byteOffset + 48));
        inst.translation = xform1.xy;
        inst.exponent = xform1.z;
        inst.opacity = xform1.w;
        // Preserve the ellipse vertex/AA arithmetic of the general SDF path.
        float m11 = inst.xform0.x, m12 = inst.xform0.y;
        float m21 = inst.xform0.z, m22 = inst.xform0.w;
        float sx = length(float2(m11, m12));
        float sy = length(float2(m21, m22));
        float2 expand = float2(1.0 / max(sx, 1e-4), 1.0 / max(sy, 1e-4));
        o.localPos = corners[vertexId] * (inst.size + expand * 2.0) - expand;
        float2 localPt = inst.position + o.localPos;
        pixelPos = float2(localPt.x * m11 + localPt.y * m21 + inst.translation.x,
                          localPt.x * m12 + localPt.y * m22 + inst.translation.y);
        o.rectSize = inst.size;
        o.fillColor = inst.fillColor * inst.opacity;
        float shear = max(abs(m12) / max(sx, 1e-4), abs(m21) / max(sy, 1e-4));
        o.shapeParams = float3(inst.exponent, 1.0 + 0.4 * saturate(shear * 16.0), 0.0);
    }
    o.clipPos = float4(
        pixelPos.x * invScreenSize.x * 2.0 - 1.0,
        1.0 - pixelPos.y * invScreenSize.y * 2.0,
        0.0, 1.0);
    return o;
}
