namespace Jalium.UI.Controls.Ink.Shaders;

/// <summary>
/// Crayon brush — rough, chalky texture. Uses multi-octave hash noise
/// inside the stroke body to modulate alpha; edges get a jittery break-up
/// instead of a clean AA boundary.
/// </summary>
public sealed class CrayonBrushShader : BrushShader
{
    public static readonly CrayonBrushShader Instance = new();
    private CrayonBrushShader() { }

    public override string ShaderKey => "jalium.brush.crayon.v1";

    public override string BrushMainHlsl => @"
float4 BrushMain(float2 px)
{
    float2 r = SdfPolyline(px);
    float halfW = HalfWidthAt(r.y);
    if (r.x > halfW * 1.15) discard;

    float cov = StrokeCoverage(r.x, halfW);
    if (cov <= 0) discard;

    // Coarse noise for clumps of wax, fine noise for grain.
    float coarse = Hash21(floor(px * 0.25), 0u);
    float fine   = Hash21(floor(px), 1u);
    float grain  = 0.55 + 0.45 * (0.6 * coarse + 0.4 * fine);

    // Edge break-up: within the outer 30% of the stroke, multiply coverage
    // by a jitter mask so the silhouette is ragged.
    float edgeT = saturate((r.x - halfW * 0.7) / (halfW * 0.3));
    float break_mask = lerp(1.0, 0.2 + 0.8 * fine, edgeT);

    float a = cov * grain * break_mask;
    float4 c = StrokeColor;
    c.rgb *= a;
    c.a   *= a;
    return c;
}
";
}
