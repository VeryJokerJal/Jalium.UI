#include "rounded_clip.hlsli"
#include "../../jalium.native.core/shaders/continuous_corner.hlsli"

struct PsInput
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
    nointerpolation float4 shapeParams : TEXCOORD12; // x=shapeType, y=shapeN, z=shadowMode, w=paintMode
    nointerpolation float  shadowSigma : TEXCOORD13; // gaussian sigma (screen px), used when shadowMode>0.5
};

float sdRoundedBox(float2 p, float2 b, float4 r)
{
    r.xy = (p.x > 0.0) ? r.xy : r.wz;
    r.x  = (p.y > 0.0) ? r.x  : r.y;
    float2 q = abs(p) - b + r.x;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
}

// erf approximation (Abramowitz & Stegun 7.1.26); max abs error ~1.5e-7 on [-6,6] << 1/255.
// Drives the analytic Gaussian falloff for soft drop-shadow / outer-glow, replacing both
// the fragile offscreen compute-blur round-trip and the N-layer concentric-rect banding.
float erf_approx(float x)
{
    float s  = sign(x);
    float ax = abs(x);
    float t  = 1.0 / (1.0 + 0.3275911 * ax);
    float poly = t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429))));
    float y  = 1.0 - poly * exp(-ax * ax);
    return s * y;
}

float4 SampleGradient(PsInput input, float t)
{
    t = saturate(t);
    uint count = input.stopCount;
    if (count == 0) return float4(0, 0, 0, 0);

    float  pos[4];
    float4 col[4];

    pos[0] = input.stop01PosR.x;
    col[0] = float4(input.stop01PosR.yzw, input.stop01AG.x);
    pos[1] = input.stop01AG.y;
    col[1] = float4(input.stop01AG.zw, input.stop12BA.xy);
    pos[2] = input.stop12BA.z;
    col[2] = float4(input.stop12BA.w, input.stop23GB.xyz);
    pos[3] = input.stop23GB.w;
    col[3] = input.stop3Color;

    if (t <= pos[0] || count == 1) return col[0];
    if (t >= pos[count - 1]) return col[count - 1];

    [unroll]
    for (uint i = 0; i < 3; i++)
    {
        if (i + 1 < count && t >= pos[i] && t <= pos[i + 1])
        {
            float range = pos[i + 1] - pos[i];
            float local = (range > 0.0) ? (t - pos[i]) / range : 0.0;
            return lerp(col[i], col[i + 1], local);
        }
    }
    return col[count - 1];
}

float4 main(PsInput input) : SV_Target
{
    // 不在 main 入口 discard：rounded clip 现在以 alpha coverage 形式
    // 应用在末尾，让圆角带 1-pixel smoothstep 抗锯齿。
    float clipCoverage = RoundedClipCoverage(input.clipPos.xy);

    float2 halfSize = input.rectSize * 0.5;
    float2 p = input.localPos - halfSize;

    float maxR = min(halfSize.x, halfSize.y);
    float4 cornerRadii = min(max(input.cornerRadius, 0.0), maxR);
    float4 r = float4(
        cornerRadii.z,
        cornerRadii.y,
        cornerRadii.x,
        cornerRadii.w);

    float dist;
    if (input.shapeParams.z > 0.5)
        dist = sdRoundedBox(p, halfSize, r);            // shadow forced to rounded-box SDF (orthogonal to SuperEllipse)
    else if (input.shapeParams.x > 1.5)
        dist = JaliumSdFullSuperellipse(p, halfSize, input.shapeParams.y); // internal true ellipse/full Lam? primitive
    else if (input.shapeParams.x > 0.5)
        dist = JaliumSdContinuousCornerRect(
            p, halfSize, cornerRadii, cornerRadii, input.shapeParams.y);
    else
        dist = sdRoundedBox(p, halfSize, r);

    // Soft drop-shadow / outer-glow: analytic Gaussian falloff from the rounded-rect SDF.
    // coverage = 0.5*(1 - erf(dist/(sqrt(2)*sigma))) -> single draw, single over-blend,
    // continuous with NO concentric-ring banding (vs. the old N-layer equal-alpha stack).
    // Reuses the normal fill path's fillColor (already *opacity in VS, premultiplied in
    // AddSdfRect); only the coverage term differs, so a colored glow stays its own hue.
    if (input.shapeParams.z > 0.5)
    {
        float sigma = max(input.shadowSigma, 0.5);
        float cov   = 0.5 + 0.5 * erf_approx(-dist / (1.4142135 * sigma));
        float4 outc = input.fillColor * cov;
        outc *= clipCoverage;
        if (outc.a < 1.0 / 255.0) discard;
        return outc;
    }

    float aa;
    if (input.shapeParams.x > 1.5 && input.shapeParams.z <= 0.5)
        aa = JaliumFullSuperellipseAaWidth(p, halfSize, input.shapeParams.y);
    else if (input.shapeParams.x > 0.5 && input.shapeParams.z <= 0.5)
        aa = JaliumContinuousCornerAaWidth(
            p, halfSize, cornerRadii, cornerRadii, input.shapeParams.y);
    else
        aa = JaliumSdfAaWidth(dist);
    float fillAlpha = 1.0 - smoothstep(-aa * 0.5, aa * 0.5, dist);

    float4 fill;
    if (input.gradientType == 1)
    {
        float2 start = input.gradGeom.xy;
        float2 end_  = input.gradGeom.zw;
        float2 dir   = end_ - start;
        float lenSq  = dot(dir, dir);
        float t = (lenSq > 0.0) ? dot(input.localPos - start, dir) / lenSq : 0.0;
        fill = SampleGradient(input, t);
        fill.rgb *= fill.a;
    }
    else if (input.gradientType == 2)
    {
        float2 center = input.gradGeom.xy;
        float2 radius = input.gradGeom.zw;
        float2 d = (input.localPos - center) / max(radius, float2(0.001, 0.001));
        float t = length(d);
        fill = SampleGradient(input, t);
        fill.rgb *= fill.a;
    }
    else
    {
        fill = input.fillColor;
    }

    float4 color;
    if (input.borderWidth > 0.0)
    {
        // Native stroke instances transport the outer bounds/radii. Rebuild the
        // original centre line once, then evaluate both stroke edges from that
        // single distance field. Subtracting independently antialiased outer and
        // inner masks makes a 1px ring optically thicker at rounded corners.
        const float halfStroke = input.borderWidth * 0.5;
        const float2 centerHalf =
            max(halfSize - halfStroke, float2(0.0001, 0.0001));
        const float centerMaxR = min(centerHalf.x, centerHalf.y);
        const float4 centerCornerRadii = min(
            max(cornerRadii - halfStroke, 0.0),
            centerMaxR);
        const float4 centerR = float4(
            centerCornerRadii.z,
            centerCornerRadii.y,
            centerCornerRadii.x,
            centerCornerRadii.w);

        float centerDist;
        float centerAa;
        if (input.shapeParams.x > 1.5)
        {
            centerDist = JaliumSdFullSuperellipse(
                p, centerHalf, input.shapeParams.y);
            centerAa = JaliumFullSuperellipseAaWidth(
                p, centerHalf, input.shapeParams.y);
        }
        else if (input.shapeParams.x > 0.5)
        {
            centerDist = JaliumSdContinuousCornerRect(
                p,
                centerHalf,
                centerCornerRadii,
                centerCornerRadii,
                input.shapeParams.y);
            centerAa = JaliumContinuousCornerAaWidth(
                p,
                centerHalf,
                centerCornerRadii,
                centerCornerRadii,
                input.shapeParams.y);
        }
        else
        {
            centerDist = sdRoundedBox(p, centerHalf, centerR);
            centerAa = JaliumSdfAaWidth(centerDist);
        }

        const float strokeDistance = abs(centerDist) - halfStroke;
        const float borderMask =
            1.0 - smoothstep(
                -centerAa * 0.5,
                centerAa * 0.5,
                strokeDistance);
        const float innerDistance = centerDist + halfStroke;
        const float fillMask =
            1.0 - smoothstep(
                -centerAa * 0.5,
                centerAa * 0.5,
                innerDistance);

        // paintMode=1 is an outline-only gradient. It keeps gradient outlines
        // on the same analytic band instead of approximating them with a path.
        color = (input.shapeParams.w > 0.5)
            ? fill * borderMask
            : fill * fillMask + input.borderColor * borderMask;
        color.a = max(color.a, 0.0);
    }
    else
    {
        color = fill * fillAlpha;
    }

    color *= clipCoverage;
    if (color.a < 1.0 / 255.0) discard;
    return color;
}
