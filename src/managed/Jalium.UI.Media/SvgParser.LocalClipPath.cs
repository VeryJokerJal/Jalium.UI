using System.Xml.Linq;

namespace Jalium.UI.Media;

internal static partial class SvgParser
{
    private static PathGeometry? ResolveLocalClipPath(XElement element,
        Dictionary<string, XElement> defs, Rect objectBounds)
    {
        var id = ReadLocalClipId(GetResolvedAttribute(element, "clip-path"));
        if (id is null || !defs.TryGetValue(id, out var clipPath) ||
            clipPath.Name.LocalName != "clipPath" || element.Document?.Root is not { } root)
            return null;
        var definition = CreateClipPathDefinition(clipPath, root);
        var bounds = objectBounds.IsEmpty ? new Rect(0, 0, 0, 0) : objectBounds;
        try
        {
            return definition.Resolve(bounds, new Size(t_viewportWidth, t_viewportHeight));
        }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? ReadLocalClipId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var source = value.Trim();
        if (!source.StartsWith("url(", StringComparison.OrdinalIgnoreCase) ||
            !source.EndsWith(')')) return null;
        source = source[4..^1].Trim().Trim('\'', '"');
        if (!source.StartsWith('#') || source.Length == 1 ||
            source.AsSpan(1).Contains('#')) return null;
        return Uri.UnescapeDataString(source[1..]);
    }

    private static Rect SvgObjectBounds(Drawing drawing)
    {
        if (drawing is GeometryDrawing geometry)
            return geometry.Geometry?.Bounds ?? Rect.Empty;
        if (drawing is not DrawingGroup group) return drawing.Bounds;

        var bounds = Rect.Empty;
        foreach (var child in group.Children)
            bounds = Rect.Union(bounds, SvgObjectBounds(child));
        if (bounds.IsEmpty || group.Transform is null) return bounds;

        var matrix = group.Transform.Value;
        var topLeft = matrix.Transform(new Point(bounds.Left, bounds.Top));
        var topRight = matrix.Transform(new Point(bounds.Right, bounds.Top));
        var bottomLeft = matrix.Transform(new Point(bounds.Left, bounds.Bottom));
        var bottomRight = matrix.Transform(new Point(bounds.Right, bounds.Bottom));
        var left = Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomLeft.X, bottomRight.X));
        var top = Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomLeft.Y, bottomRight.Y));
        var right = Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomLeft.X, bottomRight.X));
        var bottom = Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomLeft.Y, bottomRight.Y));
        return new Rect(left, top, right - left, bottom - top);
    }
}
