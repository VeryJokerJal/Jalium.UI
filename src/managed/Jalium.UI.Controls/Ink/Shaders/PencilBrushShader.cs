namespace Jalium.UI.Controls.Ink.Shaders;

/// <summary>
/// Pencil brush — fine graphite grain, narrow mark, subtle variance.
/// Narrower than Round for the same width setting; grain is finer than
/// Crayon and edges break up less.
/// </summary>
public sealed class PencilBrushShader : BrushShader
{
    public static readonly PencilBrushShader Instance = new();
    private PencilBrushShader() { }

    public override string ShaderKey => "jalium.brush.pencil.v1";

    public override string BrushMainHlsl => @"
float4 BrushMain(float2 px)
{
    float2 r = SdfPolyline(px);
    // Pencil marks are narrower than nominal width
    float halfW = HalfWidthAt(r.y) * 0.6;
    if (r.x > halfW * 1.1) discard;

    float cov = StrokeCoverage(r.x, halfW);
    if (cov <= 0) discard;

    // Fine graphite grain
    float fine = Hash21(floor(px), 0u);
    float med  = Hash21(floor(px * 0.5), 3u);
    float grain = 0.75 + 0.25 * (0.7 * fine + 0.3 * med);

    float a = cov * grain * 0.85;
    float4 c = StrokeColor;
    c.rgb *= a;
    c.a   *= a;
    return c;
}
";
}
