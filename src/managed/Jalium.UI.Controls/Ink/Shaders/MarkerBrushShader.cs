namespace Jalium.UI.Controls.Ink.Shaders;

/// <summary>
/// Marker brush — wide, flat, semi-transparent. Re-tracing the same
/// region darkens the ink (additive blend) giving a highlighter feel.
/// </summary>
public sealed class MarkerBrushShader : BrushShader
{
    public static readonly MarkerBrushShader Instance = new();
    private MarkerBrushShader() { }

    public override string ShaderKey => "jalium.brush.marker.v1";

    /// <summary>Additive so overlapping strokes accumulate color.</summary>
    public override BrushBlendMode BlendMode => BrushBlendMode.Additive;

    public override string BrushMainHlsl => @"
float4 BrushMain(float2 px)
{
    float2 r = SdfPolyline(px);
    // Marker: 25% wider than the nominal stroke, hard edges.
    float halfW = StrokeWidth * 0.625 * TaperScale(r.y);
    float cov = StrokeCoverage(r.x, halfW);
    if (cov <= 0) discard;
    // Clamp alpha to 0.35 so overlaps can build up to ~0.7 instead of saturating
    // on a single pass.
    float4 tint = StrokeColor;
    tint.a *= 0.4;
    tint.rgb *= 0.4;
    return tint * cov;
}
";
}
