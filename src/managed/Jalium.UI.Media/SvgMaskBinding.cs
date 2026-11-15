using System.Xml.Linq;
using Jalium.UI.Interop;
using Jalium.UI.Styling;

namespace Jalium.UI.Media;

/// <summary>A referenced mask is resolved in the referencing element's user space.</summary>
internal sealed record SvgMaskBinding(SvgParser.MaskDefinition? Definition, Rect ObjectBounds, Size Viewport, CssMaskMode Mode = CssMaskMode.MatchSource)
{
    private static readonly double[] Linear = Enumerable.Range(0, 256).Select(value => value / 255d <= .04045
        ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4)).ToArray();
    [ThreadStatic] private static HashSet<XElement>? t_active;
    internal bool Luminance { get; } = (Mode == CssMaskMode.MatchSource ? Definition?.ModeFor(Viewport) ?? CssMaskMode.Luminance : Mode) == CssMaskMode.Luminance;
    internal bool LinearRgb { get; } = Definition?.LinearRgbFor(Viewport) == true;

    internal IDisposable? Enter()
    {
        if (Definition is null) return null;
        var active = t_active ??= [];
        if (active.Count >= 32 || !active.Add(Definition.Identity)) return null;
        return new Scope(active, Definition.Identity);
    }
    private sealed class Scope(HashSet<XElement> active, XElement element) : IDisposable
    { public void Dispose() => active.Remove(element); }

    internal byte[]? Rasterize(Rect bounds, int width, int height, ICollection<ImageSource>? sources = null)
    {
        using var scope = Enter(); if (scope is null) return null;
        var drawing = Definition!.Resolve(ObjectBounds, Viewport);
        return drawing is null ? null : SoftwareVectorRasterizer.Rasterize(drawing, width, height, bounds, sources);
    }
    internal double Alpha(byte r, byte g, byte b, byte a)
    {
        if (!Luminance) return a / 255d;
        double red = r / 255d, green = g / 255d, blue = b / 255d;
        if (LinearRgb) { red = Linear[r]; green = Linear[g]; blue = Linear[b]; }
        return (.2125 * red + .7154 * green + .0721 * blue) * a / 255;
    }
}
