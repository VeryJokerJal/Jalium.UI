namespace Jalium.UI.Controls.Ink.Shaders;

/// <summary>
/// Calligraphy brush — elliptical nib whose effective width varies with
/// the direction of travel. Gives italic / flat-nib calligraphic strokes.
/// </summary>
public sealed class CalligraphyBrushShader : BrushShader
{
    public static readonly CalligraphyBrushShader Instance = new();
    private CalligraphyBrushShader() { }

    public override string ShaderKey => "jalium.brush.calligraphy.v1";

    public override string BrushMainHlsl => @"
float4 BrushMain(float2 px)
{
    // Union-SDF: score each segment by how much it would paint this pixel
    // and keep the arc/tangent from the winner. Self-intersecting strokes
    // stop flipping arc between candidates at crossings.
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
    float2 bestTangent = float2(1, 0);
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

        // Estimate coverage at this segment: nib is elliptical, scored
        // against the minor-axis (perpendicular) radius — same quantity
        // the final coverage formula uses.
        float scaleEst    = TaperScale(arc);
        float minorEst    = StrokeHeight * 0.3 * scaleEst;
        float covEst      = saturate(minorEst - r.x + 0.5);

        if (covEst > bestCov)
        {
            bestCov     = covEst;
            bestArc     = arc;
            float2 d    = b - a;
            bestTangent = d / max(len, 1e-4);
        }
        bestDist = min(bestDist, r.x);
        accum   += len;
    }

    float scale = TaperScale(bestArc);
    float minorHalf = StrokeHeight * 0.3 * scale;  // nib is flat — 30% of height
    float majorHalf = StrokeWidth  * 0.5 * scale;
    if (bestDist > StrokeWidth * scale + 0.5) discard;

    // Simple elliptical coverage using the perpendicular distance. The
    // tangent-aligned major axis falls out naturally because SdfSegment's
    // distance is orthogonal to the segment.
    float normDist = bestDist / max(minorHalf, 1e-4);
    float cov = saturate(1.0 - normDist) * majorHalf;  // blend along length
    cov = saturate(cov + (minorHalf - bestDist));
    if (cov <= 0) discard;

    return StrokeColor * cov;
}
";
}
