namespace Jalium.UI.Controls.Ink.Shaders;

/// <summary>
/// Pen brush — uniform capsule like Round but always ignores pressure
/// (ink-pen look: consistent line weight regardless of input device).
/// </summary>
public sealed class PenBrushShader : BrushShader
{
    public static readonly PenBrushShader Instance = new();
    private PenBrushShader() { }

    public override string ShaderKey => "jalium.brush.pen.v1";

    public override string BrushMainHlsl => @"
float4 BrushMain(float2 px)
{
    float2 r = SdfPolyline(px);
    // Pen intentionally uses half-width without pressure modulation —
    // same shape as Round but with a flat nib feel.
    float halfW = StrokeWidth * 0.5 * TaperScale(r.y);
    float cov = StrokeCoverage(r.x, halfW);
    if (cov <= 0) discard;
    return StrokeColor * cov;
}
";
}
