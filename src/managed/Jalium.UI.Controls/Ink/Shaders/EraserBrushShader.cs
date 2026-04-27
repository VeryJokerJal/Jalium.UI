namespace Jalium.UI.Controls.Ink.Shaders;

/// <summary>
/// Eraser brush — same geometry as Round, but blend mode is
/// <see cref="BrushBlendMode.Erase"/> so the dispatch subtracts from
/// the ink layer instead of adding to it. InkCanvas routes the
/// EraseByStroke / EraseByPoint editing modes through this shader.
/// </summary>
public sealed class EraserBrushShader : BrushShader
{
    public static readonly EraserBrushShader Instance = new();
    private EraserBrushShader() { }

    public override string ShaderKey => "jalium.brush.eraser.v1";

    /// <summary>Erase blend: output alpha reduces destination alpha.</summary>
    public override BrushBlendMode BlendMode => BrushBlendMode.Erase;

    public override string BrushMainHlsl => @"
float4 BrushMain(float2 px)
{
    float2 r = SdfPolyline(px);
    float halfW = HalfWidthAt(r.y);
    float cov = StrokeCoverage(r.x, halfW);
    if (cov <= 0) discard;
    // Output alpha drives the amount erased per pixel; rgb ignored by
    // the Erase blend state.
    return float4(0, 0, 0, cov);
}
";
}
