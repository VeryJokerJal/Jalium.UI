// Layout must mirror the C++ SolidRectPushConstants struct AND the vertex
// shader's PushConstants block in lockstep — Vulkan requires all stages
// that consume the same push-constant range to declare the same block.
struct PushConstants
{
    float4 rect;
    float4 color;
    float2 screenSize;
    float2 padding;
    float4 roundedClipRect;
    float2 roundedClipRadius;
    // clipFlags:
    //   .x > 0.5 → outer rounded clip enabled.
    //   .y > 0.5 → inner rounded clip enabled (border path).
    // Per-corner mode is signalled implicitly by perCornerRadiusX/Y
    // being non-zero (sum > 0). Callers that want uniform radii leave
    // those fields zero and the shader uses roundedClipRadius.
    float2 clipFlags;
    float4 innerRoundedClipRect;
    float2 innerRoundedClipRadius;
    float2 padding2;
    // The fields below are vertex-shader-only (custom-quad geometry).
    // Declared here purely so the push-constant block layout matches
    // across stages; the fragment shader never reads them.
    float4 quadPoint01;
    float4 quadPoint23;
    float2 geometryFlags;
    float2 padding3;
    // Per-corner radii for clipFlags.z mode. Order: (TL, TR, BR, BL).
    float4 perCornerRadiusX;
    float4 perCornerRadiusY;
};

[[vk::push_constant]]
PushConstants gPushConstants;

struct PsInput
{
    float4 position : SV_Position;
    float4 color : COLOR0;
};

// Coverage of a pixel against a rounded rectangle, on [0, 1].
//   ≥ 1.0 → fully inside (a clip "yes")
//     0   → fully outside (a clip "no")
//   in    (0, 1) → on the 1-px AA ramp at the edge
//
// The previous IsInsideRoundRect was a boolean test (returned 1 or 0),
// which meant every rect / rounded-rect / ellipse rendered through the
// SolidRect PSO had a hard staircase edge — circles polygonalised, big
// rounded corners showed visible aliasing. This SDF + smoothstep variant
// matches what D3D12's SdfRect shader does ([[project_d3d12_ellipse_true_sdf]]):
// signed distance to the nearest edge, then a 1-px smoothstep ramp. fwidth
// gives the screen-space pixel size so the AA is independent of zoom.
float CoverageRoundRect(float2 pixel, float4 rect, float2 radius)
{
    const float left   = rect.x;
    const float top    = rect.y;
    const float right  = rect.z;
    const float bottom = rect.w;
    const float rx = max(radius.x, 0.0f);
    const float ry = max(radius.y, 0.0f);

    // Cheap reject: more than 1 pixel outside the AABB → no contribution at all.
    if (pixel.x < left - 1.0f || pixel.y < top - 1.0f ||
        pixel.x > right + 1.0f || pixel.y > bottom + 1.0f) {
        return 0.0f;
    }

    // Square (rx == 0 || ry == 0): cheap, just an AABB SDF.
    if (rx <= 0.0f || ry <= 0.0f) {
        const float cx = clamp(pixel.x, left, right);
        const float cy = clamp(pixel.y, top, bottom);
        const float dxOut = pixel.x - cx;
        const float dyOut = pixel.y - cy;
        const float distOutside = sqrt(dxOut * dxOut + dyOut * dyOut);
        if (distOutside > 0.0f) {
            const float aa = max(fwidth(pixel.x), fwidth(pixel.y));
            return 1.0f - smoothstep(0.0f, max(aa, 0.5f), distOutside);
        }
        return 1.0f;
    }

    // Corner-aware SDF. In each corner quadrant the implicit shape is
    // an ellipse centred at the inner-corner anchor; outside the corner
    // it's just the AABB. Compute "delta from the inner-corner anchor
    // in pixel units, normalised by (rx, ry)", clamped to the outer
    // exterior. dist = length(delta) - 1: <0 inside, >0 outside.
    float anchorX;
    float anchorY;
    if (pixel.x < left + rx) anchorX = left + rx;
    else if (pixel.x > right - rx) anchorX = right - rx;
    else anchorX = pixel.x;

    if (pixel.y < top + ry) anchorY = top + ry;
    else if (pixel.y > bottom - ry) anchorY = bottom - ry;
    else anchorY = pixel.y;

    const float2 delta = float2((pixel.x - anchorX) / rx, (pixel.y - anchorY) / ry);
    const float deltaLen = length(delta);
    const float signedDist = deltaLen - 1.0f;  // signed in normalised-radius units

    // Convert back to pixel units. The corner is an ellipse, but we treat
    // it as approximately circular for the gradient by scaling by the
    // smaller radius — slightly conservative for thin elliptical corners
    // but invisible at typical UI radii.
    const float radiusScale = min(rx, ry);
    const float pixelDist = signedDist * radiusScale;
    const float aa = max(fwidth(pixel.x), fwidth(pixel.y));
    return 1.0f - smoothstep(-max(aa * 0.5f, 0.25f),
                              max(aa * 0.5f, 0.25f),
                              pixelDist);
}

// Per-corner variant — each of the four corners (TL, TR, BR, BL) can carry
// its own (rx, ry). Quadrant is selected by which side of the rect's
// centre the pixel sits in; from there we reuse CoverageRoundRect on a
// virtual single-radius rect, which is correct because each corner's
// quadrant only ever depends on its own corner's radius.
float CoveragePerCornerRoundRect(float2 pixel, float4 rect, float4 rxs, float4 rys)
{
    const float midX = (rect.x + rect.z) * 0.5f;
    const float midY = (rect.y + rect.w) * 0.5f;
    float rx, ry;
    // Order in rxs/rys matches the per-corner public API: TL, TR, BR, BL.
    if (pixel.y < midY) {
        if (pixel.x < midX) { rx = rxs.x; ry = rys.x; }       // TL
        else                { rx = rxs.y; ry = rys.y; }       // TR
    } else {
        if (pixel.x >= midX) { rx = rxs.z; ry = rys.z; }      // BR
        else                  { rx = rxs.w; ry = rys.w; }     // BL
    }
    return CoverageRoundRect(pixel, rect, float2(rx, ry));
}

float4 main(PsInput input) : SV_Target
{
    float coverage = 1.0f;

    // Outer rounded clip — typical for rounded rects & ellipses going
    // through this pipeline. Skip the test entirely when not enabled
    // (clipFlags.x ≤ 0.5) so plain rectangles pay zero AA overhead.
    if (gPushConstants.clipFlags.x > 0.5f) {
        // Per-corner mode is implicit: any non-zero entry in
        // perCornerRadiusX/Y switches us off the uniform-radius path.
        // Uniform-radius callers leave both vectors as float4(0).
        const float perCornerSum = dot(gPushConstants.perCornerRadiusX, float4(1.0f, 1.0f, 1.0f, 1.0f))
                                 + dot(gPushConstants.perCornerRadiusY, float4(1.0f, 1.0f, 1.0f, 1.0f));
        if (perCornerSum > 0.001f) {
            coverage = CoveragePerCornerRoundRect(input.position.xy,
                                                  gPushConstants.roundedClipRect,
                                                  gPushConstants.perCornerRadiusX,
                                                  gPushConstants.perCornerRadiusY);
        } else {
            coverage = CoverageRoundRect(input.position.xy,
                                         gPushConstants.roundedClipRect,
                                         gPushConstants.roundedClipRadius);
        }
        if (coverage <= 0.0f) discard;
    }

    // Inner rounded clip used by border strokes (the "hole" in the middle
    // of a thick rounded-rect outline). Subtract its coverage from the
    // outer coverage so the stroke band is smoothly anti-aliased on both
    // its inner and outer edges. Without the inner half, thick borders
    // would have a hard inner-edge staircase even after this change.
    if (gPushConstants.clipFlags.y > 0.5f) {
        const float innerCov = CoverageRoundRect(input.position.xy,
                                                 gPushConstants.innerRoundedClipRect,
                                                 gPushConstants.innerRoundedClipRadius);
        coverage = saturate(coverage - innerCov);
        if (coverage <= 0.0f) discard;
    }

    // The SolidRect pipeline uses non-premultiplied alpha blending
    // (SRC_ALPHA / ONE_MINUS_SRC_ALPHA). Scale only the alpha channel by
    // the SDF coverage — the blend will multiply RGB by alpha itself.
    // Scaling RGB here too would double-multiply at the AA edge and turn
    // anti-aliased rounded corners visibly darker.
    return float4(input.color.rgb, input.color.a * coverage);
}
