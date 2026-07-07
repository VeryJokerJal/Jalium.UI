// Liquid glass — vertex shader.
//
// Ported to match the D3D12 authority (kLiquidGlassVS in d3d12_shader_source.h).
// The quad is expanded by a fixed padding on every side so the pixel shader can
// draw the outer drop shadow ring and the fusion bridge bleed OUTSIDE the glass
// rect (both live up to ~24 px beyond the panel edge). The interpolated
// screenPos is the on-screen pixel coordinate the FS evaluates all SDFs in — it
// is NOT a 0..1 quad-local uv anymore (the FS reconstructs uv from screenPos and
// the blur-texel step, exactly like D3D12's baseUV = pixelCoord * blurInvSize).

struct PushConstants
{
    float4 rect;          // x, y, w, h (screen px)
    float4 glassInfo1;    // cornerRadius, blurRadius, texelStepX, texelStepY
    float4 glassInfo2;    // refractionAmount, chromaticAberration, refractionHeight, shapeType
    float4 tintColor;     // r, g, b, tintOpacity
    float4 lightInfo;     // lightX, lightY, highlightOpacity, fallbackFloor
    float2 screenSize;    // viewport px
    float2 shapeExtra;    // shapeN, vibrancy
    float4 quadPoint01;   // rotated-quad corners 0,1
    float4 quadPoint23;   // rotated-quad corners 2,3
    float2 geometryFlags; // x > 0.5 => use rotated quad points
    float2 shadowInfo01;  // shadowOffset, shadowRadius
    float2 shadowInfo23;  // shadowOpacity, neighborCount
    float2 fusionInfo;    // fusionRadius, _pad
    float4 n0Rect;
    float4 n1Rect;
    float4 n2Rect;
    float4 n3Rect;
    float4 neighborRadii;
};

[[vk::push_constant]]
PushConstants gPushConstants;

struct VsOutput
{
    float4 position  : SV_Position;
    float2 screenPos : TEXCOORD0;   // on-screen pixel position
};

VsOutput main(uint vertexId : SV_VertexID)
{
    const float2 corners[6] = {
        float2(0.0f, 0.0f),
        float2(1.0f, 0.0f),
        float2(1.0f, 1.0f),
        float2(0.0f, 0.0f),
        float2(1.0f, 1.0f),
        float2(0.0f, 1.0f)
    };

    const float2 corner = corners[vertexId];

    // Expand the axis-aligned quad by padding on all sides (matches D3D12).
    const float padding = 32.0f;
    float2 pixelPosition = gPushConstants.rect.xy - padding +
                           corner * (gPushConstants.rect.zw + padding * 2.0f);

    // Rotated / skewed transform path: the caller supplies the four screen-space
    // corners directly. No padding expansion here (the CPU already baked the
    // transform); the outer shadow ring is not modelled for rotated panels.
    if (gPushConstants.geometryFlags.x > 0.5f) {
        const float2 quadPoints[4] = {
            gPushConstants.quadPoint01.xy,
            gPushConstants.quadPoint01.zw,
            gPushConstants.quadPoint23.xy,
            gPushConstants.quadPoint23.zw
        };
        const uint indices[6] = { 0, 1, 2, 0, 2, 3 };
        pixelPosition = quadPoints[indices[vertexId]];
    }

    const float2 screenSize = max(gPushConstants.screenSize, float2(1.0f, 1.0f));

    VsOutput output;
    output.position = float4(
        pixelPosition.x / screenSize.x * 2.0f - 1.0f,
        1.0f - pixelPosition.y / screenSize.y * 2.0f,
        0.0f,
        1.0f);
    output.screenPos = pixelPosition;
    return output;
}
