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
    float4 borderColor;
    float4 cornerRadius;
    float  borderWidth;
    float  opacity;
    uint   gradientType;
    uint   stopCount;
    float4 gradGeom;
    float4 stop01PosR;
    float4 stop01AG;
    float4 stop12BA;
    float4 stop23GB;
    float4 stop3Color;
    float4 _pad;
};

StructuredBuffer<Instance> instances : register(t0);

cbuffer InstanceOffset : register(b1)
{
    uint baseInstanceOffset;
};

struct VsOutput
{
    float4 clipPos      : SV_Position;
    float2 localPos     : TEXCOORD0;
    float2 rectSize     : TEXCOORD1;
    float4 cornerRadius : TEXCOORD2;
    float4 fillColor    : COLOR0;
    float4 borderColor  : COLOR1;
    float  borderWidth  : TEXCOORD3;
    nointerpolation uint  gradientType : TEXCOORD4;
    nointerpolation uint  stopCount    : TEXCOORD5;
    nointerpolation float4 gradGeom    : TEXCOORD6;
    nointerpolation float4 stop01PosR  : TEXCOORD7;
    nointerpolation float4 stop01AG    : TEXCOORD8;
    nointerpolation float4 stop12BA    : TEXCOORD9;
    nointerpolation float4 stop23GB    : TEXCOORD10;
    nointerpolation float4 stop3Color  : TEXCOORD11;
    nointerpolation float2 shapeParams : TEXCOORD12; // x = shapeType, y = shapeN
};

VsOutput main(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    static const float2 corners[6] = {
        float2(0, 0), float2(1, 0), float2(1, 1),
        float2(0, 0), float2(1, 1), float2(0, 1)
    };

    Instance inst = instances[instanceId + baseInstanceOffset];
    float2 corner = corners[vertexId];

    float2 expand = float2(1.0, 1.0);
    float2 pixelPos = inst.position - expand + corner * (inst.size + expand * 2.0);

    VsOutput o;
    o.clipPos = float4(
        pixelPos.x * invScreenSize.x * 2.0 - 1.0,
        1.0 - pixelPos.y * invScreenSize.y * 2.0,
        0.0, 1.0);

    o.localPos     = corner * (inst.size + expand * 2.0) - expand;
    o.rectSize     = inst.size;
    o.cornerRadius = inst.cornerRadius;
    o.fillColor    = inst.fillColor * inst.opacity;
    o.borderColor  = inst.borderColor * inst.opacity;
    o.borderWidth  = inst.borderWidth;

    o.gradientType = inst.gradientType;
    o.stopCount    = inst.stopCount;
    // Linear gradient: gradGeom = (startX, startY, endX, endY) — all positions, subtract origin.
    // Radial gradient: gradGeom = (centerX, centerY, radiusX, radiusY) — only center is a position;
    //   radius is an absolute size and must NOT have the position subtracted.
    if (inst.gradientType == 2)
        o.gradGeom = float4(inst.gradGeom.xy - inst.position, inst.gradGeom.zw);
    else
        o.gradGeom = inst.gradGeom - float4(inst.position, inst.position);

    float op = inst.opacity;
    o.stop01PosR  = float4(inst.stop01PosR.x,       inst.stop01PosR.yzw * op);
    o.stop01AG    = float4(inst.stop01AG.x * op,     inst.stop01AG.y,            inst.stop01AG.zw * op);
    o.stop12BA    = float4(inst.stop12BA.xy * op,    inst.stop12BA.z,            inst.stop12BA.w * op);
    o.stop23GB    = float4(inst.stop23GB.xy * op,    inst.stop23GB.z * op,       inst.stop23GB.w);
    o.stop3Color  = inst.stop3Color * op;
    o.shapeParams = inst._pad.xy; // shapeType, shapeN

    return o;
}
