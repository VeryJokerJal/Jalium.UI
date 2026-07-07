struct PushConstants
{
    float4 rect;
    float4 color;
    float2 screenSize;
    // (shadowMode, shadowSigma) for the analytic erf drop shadow. shadowMode == 0
    // is a byte-identical no-op for every other primitive.
    float2 shadowParams;
    float4 roundedClipRect;
    float2 roundedClipRadius;
    float2 clipFlags;
    float4 innerRoundedClipRect;
    float2 innerRoundedClipRadius;
    float2 padding2;
    float4 quadPoint01;
    float4 quadPoint23;
    float2 geometryFlags;
    float2 padding3;
    // Per-corner radii (TL, TR, BR, BL). Fragment-shader-only; the
    // vertex shader declares them solely so the push-constant block
    // matches the layout the fragment stage consumes.
    float4 perCornerRadiusX;
    float4 perCornerRadiusY;
    // Inner-edge per-corner radii (TL, TR, BR, BL) for border strokes.
    // Fragment-shader-only; declared here only to keep the push-constant
    // block layout identical across stages (Vulkan requires it).
    float4 innerPerCornerRadiusX;
    float4 innerPerCornerRadiusY;
};

[[vk::push_constant]]
PushConstants gPushConstants;

struct VsOutput
{
    float4 position : SV_Position;
    float4 color : COLOR0;
    // Pre-transform (LOCAL) position, measured from the rotated rounded-rect's
    // OUTER top-left corner, used ONLY by the geometryFlags.y > 0.5 local-space
    // rounded-coverage branch in the fragment shader (rotated / skewed rounded
    // borders & fills). A normal TEXCOORD interpolant — for every other path it
    // carries a harmless value the fragment shader never reads. Mirrors the
    // D3D12 sdf_rect VS, whose PS reconstructs the SDF from an interpolated
    // local position so fwidth() yields screen-correct AA without re-transform.
    float2 localPos : TEXCOORD0;
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

    const uint indices[6] = { 0, 1, 2, 0, 2, 3 };

    float2 pixelPosition = gPushConstants.rect.xy + corners[vertexId] * gPushConstants.rect.zw;
    // Analytic erf drop shadow: grow the quad OUTWARD by 3*sigma+1px on every
    // side so the gaussian tail is not clipped by the rect edge (the fragment
    // shader evaluates the erf against the un-grown shadow rect in roundedClipRect,
    // so growing the quad only extends the shaded region, matching D3D12
    // sdf_rect's shadow VS). No effect on any other primitive (shadowMode == 0).
    if (gPushConstants.shadowParams.x > 0.5f) {
        const float pad = 3.0f * gPushConstants.shadowParams.y + 1.0f;
        pixelPosition = (gPushConstants.rect.xy - pad)
                      + corners[vertexId] * (gPushConstants.rect.zw + pad * 2.0f);
    }
    // Default LOCAL position: the corner inside the axis-aligned rect. Unused by
    // the fragment shader except on the rotated rounded path below, so its exact
    // value is immaterial for every other primitive.
    float2 localPosition = corners[vertexId] * gPushConstants.rect.zw;
    if (gPushConstants.geometryFlags.x > 0.5f) {
        const float2 quadPoints[4] = {
            gPushConstants.quadPoint01.xy,
            gPushConstants.quadPoint01.zw,
            gPushConstants.quadPoint23.xy,
            gPushConstants.quadPoint23.zw
        };

        pixelPosition = quadPoints[indices[vertexId]];

        // Rotated / skewed rounded rect & fill (geometryFlags.y > 0.5). The
        // screen-space oriented quad (quadPoints) is the AA-EXPANDED local OUTER
        // rect pushed through the affine on the CPU side. Reconstruct the
        // matching LOCAL position per vertex so the fragment shader can evaluate
        // the rounded-rect SDF in pre-transform space. The local rect dimensions
        // ride in innerRoundedClipRect.xy = (localOuterW, localOuterH); the
        // per-axis AA pad rides in roundedClipRadius.xy = (expandX, expandY)
        // (both fields are free on this path — clipFlags.x/.y are never set, so
        // the screen-space clip branches that normally consume them are inert).
        // No push-constant growth: we only re-purpose existing slots.
        if (gPushConstants.geometryFlags.y > 0.5f) {
            const float2 localOuter = gPushConstants.innerRoundedClipRect.xy;
            const float2 expand     = gPushConstants.roundedClipRadius.xy;
            // Corner order matches the CPU producer's quad winding exactly:
            // 0=TL, 1=TR, 2=BR, 3=BL, each grown OUTWARD by `expand` so the SDF
            // outer AA ramp is not clipped by the tight quad edge under rotation.
            const float2 localCorners[4] = {
                float2(-expand.x,                 -expand.y),                  // TL
                float2(localOuter.x + expand.x,   -expand.y),                  // TR
                float2(localOuter.x + expand.x,    localOuter.y + expand.y),   // BR
                float2(-expand.x,                  localOuter.y + expand.y)    // BL
            };
            localPosition = localCorners[indices[vertexId]];
        }
    }
    const float2 screenSize = max(gPushConstants.screenSize, float2(1.0f, 1.0f));

    VsOutput output;
    output.position = float4(
        pixelPosition.x / screenSize.x * 2.0f - 1.0f,
        1.0f - pixelPosition.y / screenSize.y * 2.0f,
        0.0f,
        1.0f);
    output.color = gPushConstants.color;
    output.localPos = localPosition;
    return output;
}
