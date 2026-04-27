namespace Jalium.UI.Controls.Ink.Shaders;

/// <summary>
/// Maps the legacy <see cref="BrushType"/> enum onto the built-in
/// <see cref="BrushShader"/> singletons. Application code that wants a
/// custom pixel-shader brush should instead set
/// <c>DrawingAttributes.BrushShader</c> directly to its own subclass.
/// </summary>
public static class BrushShaderRegistry
{
    /// <summary>
    /// Returns the built-in shader registered for <paramref name="type"/>.
    /// Never null — unknown values fall back to <see cref="RoundBrushShader"/>.
    /// </summary>
    public static BrushShader GetBuiltIn(BrushType type) => type switch
    {
        BrushType.Round       => RoundBrushShader.Instance,
        BrushType.Pen         => PenBrushShader.Instance,
        BrushType.Marker      => MarkerBrushShader.Instance,
        BrushType.Calligraphy => CalligraphyBrushShader.Instance,
        BrushType.Airbrush    => AirbrushShader.Instance,
        BrushType.Crayon      => CrayonBrushShader.Instance,
        BrushType.Pencil      => PencilBrushShader.Instance,
        BrushType.Oil         => OilBrushShader.Instance,
        BrushType.Watercolor  => WatercolorBrushShader.Instance,
        _                     => RoundBrushShader.Instance,
    };
}
