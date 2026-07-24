#ifndef JALIUM_CONTINUOUS_CORNER_HLSLI
#define JALIUM_CONTINUOUS_CORNER_HLSLI

// Canonical shape helpers shared by D3D12 and Vulkan.
//
// BorderShape.SuperEllipse is a continuous-corner rectangle: each corner is a
// local quarter Lame curve bounded by its CornerRadius, while the four sides
// remain straight. This is the useful UI-control interpretation of an Apple-
// style squircle. Applying one Lame equation to a complete, highly-eccentric
// control makes the top and bottom bow across most of its width.

float JaliumSanitizeContinuousExponent(float n)
{
    // The comparisons also reject NaN. Keep the range conservative so pow()
    // remains stable on both FXC/SM5 and DXC/SPIR-V implementations.
    return (n >= 2.0f && n <= 16.0f) ? n : 4.0f;
}

float JaliumSdBox(float2 p, float2 halfSize)
{
    float2 d = abs(p) - max(halfSize, float2(0.0f, 0.0f));
    return min(max(d.x, d.y), 0.0f) + length(max(d, 0.0f));
}

float2 JaliumSelectCornerRadius(float2 p, float4 radiiX, float4 radiiY)
{
    // Public/native order is TL, TR, BR, BL. +Y points down in both renderers.
    float2 radius = float2(0.0f, 0.0f);
    if (p.y < 0.0f)
        radius = (p.x < 0.0f) ? float2(radiiX.x, radiiY.x)
                              : float2(radiiX.y, radiiY.y);
    else
        radius = (p.x >= 0.0f) ? float2(radiiX.z, radiiY.z)
                               : float2(radiiX.w, radiiY.w);
    return radius;
}

float2 JaliumClampContinuousCornerRadius(float2 radius, float2 halfSize)
{
    return min(max(radius, float2(0.0f, 0.0f)),
               max(halfSize, float2(0.0f, 0.0f)));
}

// Gradient-normalized implicit distance for one local quarter-superellipse.
// The zero contour is exact. On either axis it joins the neighbouring straight
// edge with the same tangent (and, for n > 2, zero curvature), avoiding the
// circular corner's visible curvature discontinuity.
float JaliumSdContinuousCornerRect(
    float2 p,
    float2 halfSize,
    float4 radiiX,
    float4 radiiY,
    float exponent)
{
    const float2 b = max(halfSize, float2(0.0f, 0.0f));
    const float2 radius = JaliumClampContinuousCornerRadius(
        JaliumSelectCornerRadius(p, radiiX, radiiY), b);

    float distance = JaliumSdBox(p, b);

    // A zero radius is an intentionally sharp corner.
    if (radius.x > 0.0001f && radius.y > 0.0001f)
    {
        const float2 ap = abs(p);
        const float2 q = ap - (b - radius);

        // Only the corner quadrant uses the Lame curve. Everywhere else the
        // nearest boundary is one of the rectangle's exact straight edges.
        if (q.x <= 0.0f || q.y <= 0.0f)
        {
            distance = max(ap.x - b.x, ap.y - b.y);
        }
        else
        {
            const float n = JaliumSanitizeContinuousExponent(exponent);
            const float2 u = max(q / radius, float2(0.0f, 0.0f));
            const float lp = pow(pow(u.x, n) + pow(u.y, n), 1.0f / n);
            const float lpPower = pow(max(lp, 0.0001f), n - 1.0f);
            const float2 gradient = float2(
                pow(u.x, n - 1.0f) / (radius.x * lpPower),
                pow(u.y, n - 1.0f) / (radius.y * lpPower));
            distance = (lp - 1.0f) / max(length(gradient), 0.0001f);
        }
    }
    return distance;
}

float JaliumSdContinuousCornerRectUniform(
    float2 p,
    float2 halfSize,
    float radius,
    float exponent)
{
    const float4 r = max(radius, 0.0f).xxxx;
    return JaliumSdContinuousCornerRect(p, halfSize, r, r, exponent);
}

float2 JaliumContinuousCornerNormal(
    float2 p,
    float2 halfSize,
    float4 radiiX,
    float4 radiiY,
    float exponent)
{
    const float2 b = max(halfSize, float2(0.0f, 0.0f));
    const float2 radius = JaliumClampContinuousCornerRadius(
        JaliumSelectCornerRadius(p, radiiX, radiiY), b);
    const float2 ap = abs(p);

    // Central strips and sharp corners use the nearest straight box edge.
    const float2 edgeDistance = ap - b;
    float2 result = (edgeDistance.x > edgeDistance.y)
        ? float2((p.x < 0.0f) ? -1.0f : 1.0f, 0.0f)
        : float2(0.0f, (p.y < 0.0f) ? -1.0f : 1.0f);

    if (radius.x > 0.0001f && radius.y > 0.0001f)
    {
        const float2 q = ap - (b - radius);
        if (q.x > 0.0f && q.y > 0.0f)
        {
            const float n = JaliumSanitizeContinuousExponent(exponent);
            const float2 u = max(q / radius, float2(0.0f, 0.0f));
            float2 normal = float2(
                sign(p.x) * pow(u.x, n - 1.0f) / radius.x,
                sign(p.y) * pow(u.y, n - 1.0f) / radius.y);
            result = normal / max(length(normal), 0.0001f);
        }
    }
    return result;
}

// Direction-invariant screen-space footprint for any signed distance field.
// HLSL fwidth() is |ddx| + |ddy| (the L1 norm), so its value grows from 1 on
// an axis-aligned edge to sqrt(2) on a 45-degree edge. That wider ramp makes
// rounded corners look heavier than their straight edges. The L2 norm keeps a
// unit SDF at one physical pixel regardless of edge direction while retaining
// the correct footprint under transforms.
float JaliumSdfAaWidth(float signedDistance)
{
    const float2 screenGradient = float2(
        ddx(signedDistance),
        ddy(signedDistance));
    return max(length(screenGradient), 0.0001f);
}

// Analytic screen-space footprint of the boundary normal. This uses an L2 norm
// and the exact local normal, so mirrored corners do not receive different AA
// widths merely because they occupy opposite 2x2 quads.
float JaliumContinuousCornerAaWidth(
    float2 p,
    float2 halfSize,
    float4 radiiX,
    float4 radiiY,
    float exponent)
{
    const float2 normal = JaliumContinuousCornerNormal(
        p, halfSize, radiiX, radiiY, exponent);
    const float2 screenGradient = float2(
        dot(normal, ddx(p)),
        dot(normal, ddy(p)));
    return max(length(screenGradient), 0.0001f);
}

// A complete-bounds superellipse remains available for true ellipse primitives
// (shape type 2). It is deliberately separate from BorderShape.SuperEllipse.
float JaliumSdFullSuperellipse(float2 p, float2 halfSize, float exponent)
{
    const float2 b = max(halfSize, float2(0.0001f, 0.0001f));
    const float n = JaliumSanitizeContinuousExponent(exponent);
    const float2 u = abs(p) / b;
    const float lp = pow(pow(u.x, n) + pow(u.y, n), 1.0f / n);
    const float lpPower = pow(max(lp, 0.0001f), n - 1.0f);
    const float2 gradient = float2(
        pow(u.x, n - 1.0f) / (b.x * lpPower),
        pow(u.y, n - 1.0f) / (b.y * lpPower));
    return (lp - 1.0f) / max(length(gradient), 0.0001f);
}

float JaliumFullSuperellipseAaWidth(float2 p, float2 halfSize, float exponent)
{
    const float2 b = max(halfSize, float2(0.0001f, 0.0001f));
    const float n = JaliumSanitizeContinuousExponent(exponent);
    const float2 u = abs(p) / b;
    float2 normal = float2(
        sign(p.x) * pow(u.x, n - 1.0f) / b.x,
        sign(p.y) * pow(u.y, n - 1.0f) / b.y);
    normal /= max(length(normal), 0.0001f);

    const float2 screenGradient = float2(
        dot(normal, ddx(p)),
        dot(normal, ddy(p)));
    return max(length(screenGradient), 0.0001f);
}

#endif
