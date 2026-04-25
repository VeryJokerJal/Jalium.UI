namespace Jalium.UI.Controls.Ink.Shaders;

/// <summary>
/// Airbrush — soft spray of scattered droplets along the stroke.
/// Uses per-pixel hash noise instead of CPU-generated particles so the
/// output is deterministic per (pixel, seed) and has zero per-stroke
/// allocation cost. Blend is additive so repeated passes deepen color.
/// </summary>
public sealed class AirbrushShader : BrushShader
{
    public static readonly AirbrushShader Instance = new();
    private AirbrushShader() { }

    public override string ShaderKey => "jalium.brush.airbrush.v1";

    /// <summary>Additive so re-tracing a region darkens it.</summary>
    public override BrushBlendMode BlendMode => BrushBlendMode.Additive;

    public override string BrushMainHlsl => @"
float4 BrushMain(float2 px)
{
    float2 r = SdfPolyline(px);
    // Taper shrinks both the spray radius AND the particle density so
    // the tail feathers out naturally — radius alone leaves a too-hard
    // edge when the stroke thins to zero.
    float taper = TaperScale(r.y);
    float halfW = StrokeWidth * 0.75 * taper;  // airbrush sprays wider than stroke width
    if (halfW <= 0.0 || r.x > halfW * 1.5) discard;

    // Radial falloff from stroke center — outer edge is sparser / softer.
    float t = saturate(r.x / (halfW * 1.5));
    float falloff = 1.0 - t * t;

    // Particle mask: quantize pixel to a small grid cell, hash it, only
    // light up if hash is below a density threshold that depends on the
    // falloff. Two octaves combine fine speckle with coarser clumps.
    float2 cell  = floor(px);
    float  h1    = Hash21(cell, 0u);
    float  h2    = Hash21(floor(px * 0.5), 7u);
    float  density = 0.35 * falloff * falloff * taper;
    float  hit = step(h1, density) + 0.35 * step(h2, density * 0.4);
    hit = saturate(hit);
    if (hit <= 0) discard;

    // Alpha per droplet: 15–55% of source alpha, modulated by falloff.
    float alpha = (0.15 + 0.4 * h1) * falloff * hit;
    float4 c = StrokeColor;
    c.rgb *= alpha;
    c.a   *= alpha;
    return c;
}
";
}
