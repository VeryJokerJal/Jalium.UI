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
    uint4  gradientStopRef; // x=stopOffset
    float4 _pad;        // x=shapeType, y=shapeN, z=paintMode, w unused
    float4 xform0;      // m11, m12, m21, m22  (per-instance 2x3 affine)
    float4 xform1;      // dx, dy, _, _
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
    nointerpolation uint   stopOffset   : TEXCOORD7;
    nointerpolation float4 shapeParams : TEXCOORD8;  // x=shapeType, y=shapeN, z=shadowMode, w=paintMode
    nointerpolation float  shadowSigma : TEXCOORD9;  // gaussian sigma (screen px)
    nointerpolation float  gradientOpacity : TEXCOORD10;
    nointerpolation float  aaScale     : TEXCOORD11; // coverage-band widening for rotated/skewed instances
};

VsOutput main(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    static const float2 corners[6] = {
        float2(0, 0), float2(1, 0), float2(1, 1),
        float2(0, 0), float2(1, 1), float2(0, 1)
    };

    Instance inst = instances[instanceId + baseInstanceOffset];
    float2 corner = corners[vertexId];

    // Per-instance 2x3 affine — applied to the quad corners here so rotation /
    // negative-diagonal (180 deg) / skew render as a correctly oriented quad
    // instead of the legacy axis-aligned + sign-stripped-scale one. For an
    // un-transformed element this is identity and the output is bit-identical.
    float m11 = inst.xform0.x, m12 = inst.xform0.y;
    float m21 = inst.xform0.z, m22 = inst.xform0.w;
    float2 xfT = inst.xform1.xy;

    // 1px AA headroom, grown in the PRE-transform (local) frame by the inverse
    // on-screen scale of each axis so the smoothstep band still spans ~1px on
    // screen after the affine (the PS fwidth() normalizes the band WIDTH; this
    // only keeps the geometric quad big enough to contain it). Identity =>
    // sx=sy=1 => expand=(1,1) => the legacy 1px screen expand exactly.
    float sx = length(float2(m11, m12));
    float sy = length(float2(m21, m22));
    // Soft shadow (shadowMode>0.5) needs the quad grown by 3*sigma+1px so the Gaussian
    // tail isn't clipped by the rect edge; non-shadow keeps a constant 1px, so expand
    // is bit-identical to the legacy path (sigma carried PRE-transform / local px).
    float shadowMode  = inst.xform1.z;
    float shadowSigma = inst.xform1.w;
    float pad = (shadowMode > 0.5) ? (3.0 * shadowSigma + 1.0) : 1.0;
    float2 expand = float2(pad / max(sx, 1e-4), pad / max(sy, 1e-4));

    float2 localPos = corner * (inst.size + expand * 2.0) - expand;
    float2 localPt  = inst.position + localPos;
    float2 pixelPos = float2(localPt.x * m11 + localPt.y * m21 + xfT.x,
                             localPt.x * m12 + localPt.y * m22 + xfT.y);

    VsOutput o;
    o.clipPos = float4(
        pixelPos.x * invScreenSize.x * 2.0 - 1.0,
        1.0 - pixelPos.y * invScreenSize.y * 2.0,
        0.0, 1.0);

    o.localPos     = localPos;
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

    o.stopOffset = inst.gradientStopRef.x;
    o.shapeParams = float4(inst._pad.xy, shadowMode, inst._pad.z);
    o.shadowSigma = shadowSigma;                      // gaussian sigma (screen px)
    o.gradientOpacity = inst.opacity;

    // A 1-pixel box filter is the exact coverage for a straight edge, but a
    // SHALLOW rotation lands its whole ramp inside one pixel row and leaves a
    // 1/tan(theta)-long stair (14px at 4 deg) that reads as a jagged edge even
    // though every covered pixel is analytically correct. Widening the band to
    // 1.4px on rotated / skewed instances spreads that ramp over two rows and
    // dissolves the stair. Axis-aligned instances (m12 == m21 == 0) keep
    // aaScale == 1.0, so their output stays bit-identical to the legacy path.
    // shear == |sin(theta)| for a pure rotation; *16 saturates by ~3.6 deg, and
    // the ramp below that keeps a sub-degree transform from stepping visibly.
    float shear = max(abs(m12) / max(sx, 1e-4), abs(m21) / max(sy, 1e-4));
    o.aaScale = 1.0 + 0.4 * saturate(shear * 16.0);

    return o;
}
