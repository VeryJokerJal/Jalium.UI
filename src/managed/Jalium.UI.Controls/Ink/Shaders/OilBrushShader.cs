namespace Jalium.UI.Controls.Ink.Shaders;

/// <summary>
/// Oil brush — thick paint strokes with directional bristle streaks.
/// Anisotropic noise aligned with the stroke tangent plus subtle color
/// variance give the impression of layered pigment.
/// </summary>
public sealed class OilBrushShader : BrushShader
{
    public static readonly OilBrushShader Instance = new();
    private OilBrushShader() { }

    public override string ShaderKey => "jalium.brush.oil.v1";

    public override string BrushMainHlsl => @"
float4 BrushMain(float2 px)
{
    // Union-SDF: pass 1 computes total arc length so each segment's arc
    // is normalized by actual length (not point index, which skews on
    // non-uniform samples). Pass 2 scores segments by the coverage they
    // would produce, selecting the dominating one so self-intersecting
    // strokes don't flip arc/tangent between candidates and get cut at
    // crossings.
    float totalLen = 0;
    [loop]
    for (uint i = 0; i + 1 < PointCount; ++i)
    {
        StrokePoint pa = StrokePoints[i];
        StrokePoint pb = StrokePoints[i + 1];
        totalLen += length(float2(pb.x - pa.x, pb.y - pa.y));
    }
    float invLen = (totalLen > 1e-6) ? (1.0 / totalLen) : 0.0;

    float bestDist = 1e20;
    float bestArc  = 0;
    float2 tangent = float2(1, 0);
    float2 closest = px;
    float bestCov  = -1;
    float accum    = 0;

    [loop]
    for (uint j = 0; j + 1 < PointCount; ++j)
    {
        StrokePoint pa = StrokePoints[j];
        StrokePoint pb = StrokePoints[j + 1];
        float2 a   = float2(pa.x, pa.y);
        float2 b   = float2(pb.x, pb.y);
        float  len = length(b - a);
        float2 r   = SdfSegment(px, a, b);
        float  arc = saturate((accum + r.y * len) * invLen);

        float halfWEst = StrokeWidth * 0.5;
        if (IgnorePressure == 0)
        {
            float p = lerp(pa.pressure, pb.pressure, r.y);
            halfWEst *= p;
        }
        halfWEst *= TaperScale(arc);
        float covEst = saturate(halfWEst - r.x + 0.5);

        if (covEst > bestCov)
        {
            bestCov = covEst;
            bestArc = arc;
            float2 d = b - a;
            tangent = d / max(len, 1e-4);
            closest = a + d * r.y;
        }
        bestDist = min(bestDist, r.x);
        accum   += len;
    }

    float halfW = HalfWidthAt(bestArc);
    if (bestDist > halfW) discard;

    float cov = StrokeCoverage(bestDist, halfW);

    // Local frame: u = along-tangent, v = perpendicular
    float2 normal = float2(-tangent.y, tangent.x);
    float2 local  = float2(dot(px - closest, tangent), dot(px - closest, normal));

    // Bristle streaks: sine pattern perpendicular to stroke + noise.
    // Frequency scales with inverse pixel size so the bristle count is
    // roughly constant per stroke width.
    float bristle = 0.5 + 0.5 * sin(local.y * 6.28318 * 0.35);
    float streakNoise = Hash21(floor(local.y * 2.0), 0u);
    bristle *= 0.6 + 0.4 * streakNoise;

    // Color variance: shift hue slightly per pixel for pigment feel.
    float  colorJitter = Hash21(floor(px * 0.75), 2u);
    float3 tint        = StrokeColor.rgb * lerp(0.85, 1.1, colorJitter);

    float a = cov * (0.65 + 0.35 * bristle);
    float4 c = float4(tint * a, StrokeColor.a * a);
    return c;
}
";
}
