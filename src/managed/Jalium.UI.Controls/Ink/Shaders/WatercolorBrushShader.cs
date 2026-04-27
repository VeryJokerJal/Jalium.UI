namespace Jalium.UI.Controls.Ink.Shaders;

/// <summary>
/// Watercolor brush — soft wet-on-wet edges. Core stroke is painted at
/// low alpha; outer halo decays smoothly past the nominal width so
/// overlapping strokes bleed into each other under the Additive blend
/// mode (repeated passes build up saturation).
/// </summary>
public sealed class WatercolorBrushShader : BrushShader
{
    public static readonly WatercolorBrushShader Instance = new();
    private WatercolorBrushShader() { }

    public override string ShaderKey => "jalium.brush.watercolor.v1";

    /// <summary>Additive so edges bleed and overlaps concentrate.</summary>
    public override BrushBlendMode BlendMode => BrushBlendMode.Additive;

    public override string BrushMainHlsl => @"
float4 BrushMain(float2 px)
{
    float2 r = SdfPolyline(px);
    float halfW = HalfWidthAt(r.y);
    float outerHalf = halfW * 1.8;  // soft halo
    if (r.x > outerHalf) discard;

    // Two-zone falloff: solid-ish core (< halfW) + long tail to outerHalf.
    float coreT = saturate(r.x / halfW);
    float haloT = saturate((r.x - halfW) / max(outerHalf - halfW, 1.0));
    float core  = 1.0 - coreT * coreT;
    float halo  = pow(1.0 - haloT, 2.2);

    // Wet-blob variance along edges: hash-perturb the falloff so edges
    // aren't perfectly smooth.
    float n = Hash21(floor(px * 0.6), 0u);
    float edge = lerp(0.75, 1.0, n);

    // Composition: core contributes ~15% alpha, halo ~5%. Repeated passes
    // accumulate under the additive blend mode.
    float a = (core * 0.18 + halo * 0.06) * edge;
    if (a <= 0) discard;

    float4 c = StrokeColor;
    c.rgb *= a;
    c.a   *= a;
    return c;
}
";
}
