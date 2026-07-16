// Layout must mirror the C++ SolidRectPushConstants struct AND the vertex
// shader's PushConstants block in lockstep — Vulkan requires all stages
// that consume the same push-constant range to declare the same block.
struct PushConstants
{
    float4 rect;
    float4 color;
    float2 screenSize;
    // (effectMode, effectParameter), retaining the historical shadowParams name:
    // > 0.5 = analytic erf shadow with sigma, < -0.5 = analytic SuperEllipse
    // fill with exponent n, 0 = ordinary SolidRect.
    float2 shadowParams;
    float4 roundedClipRect;
    float2 roundedClipRadius;
    // clipFlags:
    //   .x > 0.5 → outer rounded clip enabled.
    //   .x > 1.5 → outer clip is INVERSE (PushRoundedRectClipExclude):
    //              keep 1 - coverage, masking the rect's INTERIOR.
    //   .y > 0.5 → inner rounded clip enabled (border path).
    // Per-corner mode is signalled implicitly by perCornerRadiusX/Y
    // being non-zero (sum > 0). Callers that want uniform radii leave
    // those fields zero and the shader uses roundedClipRadius.
    float2 clipFlags;
    // Also carries exact SuperEllipse bounds when shadowParams.x < -0.5; the
    // outer roundedClipRect then remains free for an ancestor clip.
    float4 innerRoundedClipRect;
    float2 innerRoundedClipRadius;
    float2 padding2;
    // quadPoint01/23 are vertex-shader-only (custom-quad geometry); declared
    // here purely so the push-constant block layout matches across stages.
    // geometryFlags:
    //   .x > 0.5 → custom oriented quad (vertex shader only).
    //   .y > 0.5 → rotated / skewed LOCAL-space rounded coverage (fragment
    //              shader path at the top of main()). On that path the LOCAL
    //              geometry is carried in the inner rounded-clip slots
    //              (innerRoundedClipRect / innerPerCornerRadiusX / ...Y), which
    //              are otherwise unused there, so the block does not grow.
    float4 quadPoint01;
    float4 quadPoint23;
    float2 geometryFlags;
    float2 padding3;
    // Per-corner radii for clipFlags.z mode. Order: (TL, TR, BR, BL).
    float4 perCornerRadiusX;
    float4 perCornerRadiusY;
    // Per-corner radii for the INNER rounded clip (a border stroke's inside
    // edge). Order: (TL, TR, BR, BL). When the sum of these is > 0 the inner
    // clip branch switches from the uniform CoverageRoundRect to per-corner so
    // a Border with non-uniform CornerRadius gets an inner edge whose corners
    // match its outer edge. Without it the inner hole is uniformly rounded from
    // the largest corner and eats the stroke band at the square corners.
    // Declared float4 (NOT float[4]) so the 16-byte stride matches the C++
    // tightly-packed float[4] — the same trick perCornerRadiusX/Y rely on.
    float4 innerPerCornerRadiusX;
    float4 innerPerCornerRadiusY;
};

[[vk::push_constant]]
PushConstants gPushConstants;

struct PsInput
{
    float4 position : SV_Position;
    float4 color : COLOR0;
    // Pre-transform (LOCAL) position from the vertex shader, measured from the
    // rotated rounded-rect's OUTER top-left corner. Read ONLY when
    // geometryFlags.y > 0.5 (the rotated / skewed local-space rounded path).
    float2 localPos : TEXCOORD0;
};

// Signed distance to a rounded box with independent per-corner radii, evaluated
// in LOCAL (pre-transform) space. Ported byte-for-byte from D3D12
// sdf_rect.ps.hlsl:31-37 so a rotated Jalium border matches the D3D12 backend.
//   p  : point relative to the box CENTRE.
//   b  : box half-size.
//   r  : per-corner radii pre-repacked to (BR, TR, TL, BL) — see the call site,
//        which applies the same float4(cr.z, cr.y, cr.x, cr.w) shuffle D3D12 uses
//        (cr in TL,TR,BR,BL order). +y is DOWN, matching screen space.
float sdRoundedBoxLocal(float2 p, float2 b, float4 r)
{
    r.xy = (p.x > 0.0f) ? r.xy : r.wz;
    r.x  = (p.y > 0.0f) ? r.x  : r.y;
    float2 q = abs(p) - b + r.x;
    return min(max(q.x, q.y), 0.0f) + length(max(q, 0.0f)) - r.x;
}

// Axis-aligned SuperEllipse signed pseudo-distance. This is kept byte-for-byte
// equivalent to D3D12 sdf_rect.ps.hlsl so Border.Shape=SuperEllipse has the same
// silhouette and derivative-based edge coverage on both backends.
float sdSuperEllipseRect(float2 p, float2 halfSize, float n)
{
    float2 q = abs(p) / max(halfSize, float2(0.001f, 0.001f));
    float d = pow(pow(q.x, n) + pow(q.y, n), 1.0f / n) - 1.0f;
    return d * min(halfSize.x, halfSize.y);
}

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

    // Do not return before evaluating the distance-field derivative below.
    // Fragments just outside the expanded quad still run as helper lanes for
    // fwidth().  Discarding only one side's helper lane makes the derivative
    // undefined and can remove that side's outer AA sample at fractional X/Y.
    // The draw quad is already bounded to a 1 px apron, and the SDF naturally
    // resolves farther samples to zero, so an AABB reject is unnecessary.

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
    // but invisible at typical UI radii. This matches D3D12's
    // sdSuperEllipseRect exactly: d * min(halfSize).
    const float radiusScale = min(rx, ry);
    const float pixelDist = signedDist * radiusScale;
    // AA width from the DISTANCE FIELD's own screen-space derivative —
    // exactly D3D12 sdf_rect's `aa = max(fwidth(dist), 0.0001)`. The previous
    // constant max(fwidth(pixel), 0.5) ramp ignored the min-axis scaling of
    // pixelDist, so elliptical edges anti-aliased with a direction-dependent
    // mismatch against D3D12 (widest at the long-axis ends — the residual
    // ellipse-scene diff after the geometry fixes was exactly this ring).
    const float aa = max(fwidth(pixelDist), 0.0001f);
    return 1.0f - smoothstep(-aa * 0.5f, aa * 0.5f, pixelDist);
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
    // Clamp each corner to half its OWN axis (rx ≤ halfW, ry ≤ halfH) — the
    // per-axis generalisation appropriate to this elliptical-corner SDF, which
    // supports independent rx/ry. Conceptually analogous to D3D12 sdRoundedBox's
    // scalar r = min(r, maxR=min(halfW,halfH)) but NOT byte-identical: D3D12
    // forces a circular corner, here a corner may stay elliptical on a
    // non-square rect. The uniform CoverageRoundRect self-clamps via its anchor
    // logic, but the per-corner path feeds rx/ry straight through, so a
    // pill-shaped Border (corner ≈ min(w,h)/2, plus the outer pass's
    // +halfStroke) could otherwise over-round and cross the opposite corner's
    // anchor. A no-op for every real caller (managed pre-clamps each corner to
    // min(w,h)/2); this only fires defensively on out-of-contract input.
    const float halfW = max((rect.z - rect.x) * 0.5f, 0.0f);
    const float halfH = max((rect.w - rect.y) * 0.5f, 0.0f);
    rx = min(rx, halfW);
    ry = min(ry, halfH);
    return CoverageRoundRect(pixel, rect, float2(rx, ry));
}

// erf approximation (Abramowitz & Stegun 7.1.26); max abs error ~1.5e-7 on
// [-6,6] << 1/255. Ported byte-for-byte from D3D12 sdf_rect.ps.hlsl so the
// analytic drop-shadow gaussian falloff matches the D3D12 backend exactly.
float erf_approx(float x)
{
    float s  = sign(x);
    float ax = abs(x);
    float t  = 1.0f / (1.0f + 0.3275911f * ax);
    float poly = t * (0.254829592f + t * (-0.284496736f + t * (1.421413741f + t * (-1.453152027f + t * 1.061405429f))));
    float y  = 1.0f - poly * exp(-ax * ax);
    return s * y;
}

// Signed distance to an axis-aligned rounded box (uniform radius), CENTRE-relative.
// +y is DOWN (screen space). Matches D3D12 sdRoundedBox for the uniform-corner
// case the drop shadow uses (DrawGlowLayers passed a single averaged radius).
float sdRoundBoxUniform(float2 p, float2 halfSize, float radius)
{
    float rr = min(radius, min(halfSize.x, halfSize.y));
    float2 q = abs(p) - halfSize + rr;
    return min(max(q.x, q.y), 0.0f) + length(max(q, 0.0f)) - rr;
}

float4 main(PsInput input) : SV_Target
{
    // Analytic SuperEllipse fill (shadowParams.x < -0.5). The command stores
    // the exact shape bounds in innerRoundedClipRect and exponent n in
    // shadowParams.y; its draw quad has a 1px apron so the outside half of this
    // smooth coverage ramp is not clipped by triangle top-left raster rules.
    // Keep every helper lane alive through fwidth(), just like rounded clips.
    if (gPushConstants.shadowParams.x < -0.5f) {
        const float2 rc0 = gPushConstants.innerRoundedClipRect.xy;
        const float2 rc1 = gPushConstants.innerRoundedClipRect.zw;
        const float2 halfSize = (rc1 - rc0) * 0.5f;
        const float2 center = (rc0 + rc1) * 0.5f;
        const float exponent = max(gPushConstants.shadowParams.y, 2.0f);
        const float dist = sdSuperEllipseRect(input.position.xy - center, halfSize, exponent);
        const float aa = max(fwidth(dist), 0.0001f);
        float coverage = 1.0f - smoothstep(-aa * 0.5f, aa * 0.5f, dist);

        // roundedClipRect remains independent and carries an ancestor rounded
        // include/exclude clip. Preserve the old FilledPolygon behaviour and
        // D3D12 parity by multiplying that coverage into the shape instead of
        // degrading the ancestor to its rectangular scissor.
        if (gPushConstants.clipFlags.x > 0.5f) {
            const float perCornerSum =
                  dot(gPushConstants.perCornerRadiusX, float4(1.0f, 1.0f, 1.0f, 1.0f))
                + dot(gPushConstants.perCornerRadiusY, float4(1.0f, 1.0f, 1.0f, 1.0f));
            float clipCoverage;
            if (perCornerSum > 0.001f) {
                clipCoverage = CoveragePerCornerRoundRect(input.position.xy,
                                                           gPushConstants.roundedClipRect,
                                                           gPushConstants.perCornerRadiusX,
                                                           gPushConstants.perCornerRadiusY);
            } else {
                clipCoverage = CoverageRoundRect(input.position.xy,
                                                  gPushConstants.roundedClipRect,
                                                  gPushConstants.roundedClipRadius);
            }
            if (gPushConstants.clipFlags.x > 1.5f) {
                clipCoverage = 1.0f - clipCoverage;
            }
            coverage *= clipCoverage;
        }

        if (coverage <= 0.0f) discard;
        return float4(input.color.rgb, input.color.a * coverage);
    }

    // Analytic erf drop shadow (shadowParams.x > 0.5): a single over-blend with a
    // continuous gaussian falloff of the shadow rect's SDF, replacing the N-layer
    // concentric-rect halo (D3D12 DrawDropShadowEffect parity). The shadow rect +
    // radius ride in roundedClipRect / roundedClipRadius; the colour (already
    // premultiplied fillA*opacity on the CPU) is in `color`. This blend pipeline
    // is NON-premultiplied (SRC_ALPHA / ONE_MINUS_SRC_ALPHA), so scale only alpha
    // by the erf coverage — the same convention the rest of this shader uses.
    if (gPushConstants.shadowParams.x > 0.5f) {
        const float2 rc0 = gPushConstants.roundedClipRect.xy;   // left, top
        const float2 rc1 = gPushConstants.roundedClipRect.zw;   // right, bottom
        const float2 halfSize = (rc1 - rc0) * 0.5f;
        const float2 center   = (rc0 + rc1) * 0.5f;
        const float2 p = input.position.xy - center;
        const float radius = max(gPushConstants.roundedClipRadius.x, 0.0f);
        const float dist = sdRoundBoxUniform(p, halfSize, radius);
        const float sigma = max(gPushConstants.shadowParams.y, 0.5f);
        // coverage = 0.5*(1 - erf(dist/(sqrt(2)*sigma)))  (byte-for-byte D3D12).
        const float cov = 0.5f + 0.5f * erf_approx(-dist / (1.4142135f * sigma));
        const float outA = input.color.a * cov;
        if (outA < 1.0f / 255.0f) discard;
        return float4(input.color.rgb, outA);
    }

    float coverage = 1.0f;

    // ---------------------------------------------------------------------
    // Rotated / skewed rounded rect & fill (geometryFlags.y > 0.5).
    //
    // The axis-aligned paths below evaluate their SDF against the SCREEN-space
    // SV_Position; that is wrong once the quad is rotated/skewed, because the
    // rect is no longer axis-aligned in screen space. Instead, evaluate the
    // rounded-rect SDF in LOCAL (pre-transform) space using the interpolated
    // input.localPos, exactly like D3D12 sdf_rect.ps.hlsl. fwidth() on the LOCAL
    // SDF still measures the on-screen pixel footprint, so AA stays a ~1px ramp
    // on screen — do NOT pre-transform localPos here.
    //
    // The local geometry is carried in the (otherwise-free on this path) inner
    // rounded-clip slots, so the push-constant block does not grow:
    //   innerRoundedClipRect = (localOuterW, localOuterH, localInnerW, localInnerH)
    //   innerPerCornerRadiusX = LOCAL OUTER per-corner radii (TL, TR, BR, BL)
    //   innerPerCornerRadiusY = LOCAL INNER per-corner radii (TL, TR, BR, BL)
    // A FILL leaves localInnerW/H == 0 (no inner subtraction); a STROKE supplies
    // the inner band so the outer-minus-inner difference is the ring.
    if (gPushConstants.geometryFlags.y > 0.5f) {
        const float2 halfOuter = gPushConstants.innerRoundedClipRect.xy * 0.5f;
        const float2 p = input.localPos - halfOuter;

        // Repack TL,TR,BR,BL -> (BR,TR,TL,BL) for sdRoundedBoxLocal, then clamp
        // each corner to the box half-extent — identical to D3D12 sdf_rect
        // (r = min(r, min(halfSize.x, halfSize.y))).
        const float maxROuter = min(halfOuter.x, halfOuter.y);
        float4 rOuter = float4(gPushConstants.innerPerCornerRadiusX.z,
                               gPushConstants.innerPerCornerRadiusX.y,
                               gPushConstants.innerPerCornerRadiusX.x,
                               gPushConstants.innerPerCornerRadiusX.w);
        rOuter = min(rOuter, maxROuter);

        const float distOuter = sdRoundedBoxLocal(p, halfOuter, rOuter);
        const float aaOuter = max(fwidth(distOuter), 0.0001f);
        float cov = 1.0f - smoothstep(-aaOuter * 0.5f, aaOuter * 0.5f, distOuter);

        // Inner subtraction (stroke band). Only when an inner rect was supplied;
        // a fill leaves localInnerW/H == 0 and keeps the full outer coverage.
        const float2 innerExtent = gPushConstants.innerRoundedClipRect.zw;
        if (innerExtent.x > 0.0f && innerExtent.y > 0.0f) {
            const float2 halfInner = innerExtent * 0.5f;
            const float maxRInner = min(halfInner.x, halfInner.y);
            float4 rInner = float4(gPushConstants.innerPerCornerRadiusY.z,
                                   gPushConstants.innerPerCornerRadiusY.y,
                                   gPushConstants.innerPerCornerRadiusY.x,
                                   gPushConstants.innerPerCornerRadiusY.w);
            rInner = min(rInner, maxRInner);
            // Inner rect shares the OUTER centre (centred model), so reuse p.
            const float distInner = sdRoundedBoxLocal(p, halfInner, rInner);
            const float aaInner = max(fwidth(distInner), 0.0001f);
            const float covInner = 1.0f - smoothstep(-aaInner * 0.5f, aaInner * 0.5f, distInner);
            cov = saturate(cov - covInner);
        }

        if (cov <= 0.0f) discard;
        // Non-premultiplied blend (SRC_ALPHA / ONE_MINUS_SRC_ALPHA): scale only
        // alpha by coverage, matching the axis path's tail return.
        return float4(input.color.rgb, input.color.a * cov);
    }

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
        // clipFlags.x == 2 → INVERSE outer clip (an ancestor
        // PushRoundedRectClipExclude): keep the OUTSIDE of the rect, so the
        // AA ramp flips with the mask (matches D3D12's inverse rounded clip).
        if (gPushConstants.clipFlags.x > 1.5f) {
            coverage = 1.0f - coverage;
        }
    }

    // Inner rounded clip used by border strokes (the "hole" in the middle
    // of a thick rounded-rect outline). Subtract its coverage from the
    // outer coverage so the stroke band is smoothly anti-aliased on both
    // its inner and outer edges. Without the inner half, thick borders
    // would have a hard inner-edge staircase even after this change.
    if (gPushConstants.clipFlags.y > 0.5f) {
        // Per-corner inner edge is implicit, mirroring the outer per-corner
        // gate above: any non-zero inner per-corner radius switches the inner
        // subtraction to CoveragePerCornerRoundRect so each corner's inner arc
        // matches that corner's outer arc. Gate on the INNER sum specifically —
        // a per-corner stroke whose every corner ≤ halfStroke clamps all inner
        // radii to 0 (inner sum 0) and must fall back to the uniform
        // innerRoundedClipRadius (itself 0 in that case → an all-sharp inner
        // hole). Uniform strokes leave the inner per-corner fields zero and so
        // keep the uniform path unchanged.
        const float innerPerCornerSum =
              dot(gPushConstants.innerPerCornerRadiusX, float4(1.0f, 1.0f, 1.0f, 1.0f))
            + dot(gPushConstants.innerPerCornerRadiusY, float4(1.0f, 1.0f, 1.0f, 1.0f));
        float innerCov;
        if (innerPerCornerSum > 0.001f) {
            innerCov = CoveragePerCornerRoundRect(input.position.xy,
                                                  gPushConstants.innerRoundedClipRect,
                                                  gPushConstants.innerPerCornerRadiusX,
                                                  gPushConstants.innerPerCornerRadiusY);
        } else {
            innerCov = CoverageRoundRect(input.position.xy,
                                         gPushConstants.innerRoundedClipRect,
                                         gPushConstants.innerRoundedClipRadius);
        }
        coverage = saturate(coverage - innerCov);
    }

    // Keep every helper lane alive until all outer/inner fwidth operations
    // have completed.  A derivative evaluated after a neighbouring lane was
    // discarded is undefined and can make one edge lose its AA sample.
    if (coverage <= 0.0f) discard;

    // The SolidRect pipeline uses non-premultiplied alpha blending
    // (SRC_ALPHA / ONE_MINUS_SRC_ALPHA). Scale only the alpha channel by
    // the SDF coverage — the blend will multiply RGB by alpha itself.
    // Scaling RGB here too would double-multiply at the AA edge and turn
    // anti-aliased rounded corners visibly darker.
    return float4(input.color.rgb, input.color.a * coverage);
}
