namespace Jalium.UI.Controls.Ink.Shaders;

/// <summary>
/// Uniform-width round brush — capsule along the polyline, soft 1-px AA.
/// The default brush; replaces the CPU-rasterized round path from
/// <c>Stroke.BuildPathCache</c>.
/// </summary>
public sealed class RoundBrushShader : BrushShader
{
    /// <summary>Shared singleton — identical HLSL per instance, no state.</summary>
    public static readonly RoundBrushShader Instance = new();

    private RoundBrushShader() { }

    /// <inheritdoc/>
    public override string ShaderKey => "jalium.brush.round.v1";

    /// <inheritdoc/>
    public override string BrushMainHlsl => @"
float4 BrushMain(float2 px)
{
    float2 r = SdfPolyline(px);
    float halfW = HalfWidthAt(r.y);
    float cov = StrokeCoverage(r.x, halfW);
    if (cov <= 0) discard;
    return StrokeColor * cov;
}
";
}
