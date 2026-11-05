using Jalium.UI.Styling;

namespace Jalium.UI.Media;

internal static class SvgPaintOrder
{
    internal static bool TryParse(string value, out bool strokeFirst)
    {
        var valid = TryOrder(value, out var order);
        strokeFirst = valid && Array.IndexOf(order, 2) < Array.IndexOf(order, 1);
        return valid;
    }

    private static bool TryOrder(string value, out int[] order)
    {
        order = [1, 2, 4];
        var operations = new List<int>(3);
        var reader = new CssTokenReader(value);
        var count = 0;
        var seen = 0;
        while (reader.TryReadIdent(out var keyword))
        {
            if (keyword.Equals("normal", StringComparison.OrdinalIgnoreCase))
                return count == 0 && reader.AtEnd;
            var bit = keyword.ToString().ToLowerInvariant() switch
            {
                "fill" => 1,
                "stroke" => 2,
                "markers" => 4,
                _ => 0,
            };
            if (bit == 0 || (seen & bit) != 0) return false;
            seen |= bit;
            operations.Add(bit);
            count++;
        }
        if (count == 0 || !reader.AtEnd) return false;
        // Unspecified operations follow in their normal fill/stroke/markers order.
        foreach (var bit in new[] { 1, 2, 4 })
            if ((seen & bit) == 0) operations.Add(bit);
        order = operations.ToArray();
        return true;
    }

    internal static Drawing Create(Brush? fill, Pen? stroke, Geometry geometry, string? order, Drawing? markers = null, bool nonScalingStroke = false)
    {
        if (markers is not null)
        {
            if (!TryOrder(order ?? "normal", out var operations)) operations = [1, 2, 4];
            var composed = new DrawingGroup();
            foreach (var operation in operations)
            {
                if (operation == 1 && fill is not null) composed.Children.Add(new GeometryDrawing(fill, null, geometry));
                if (operation == 2 && stroke is not null) composed.Children.Add(new GeometryDrawing(null, stroke, geometry) { SvgNonScalingStroke = nonScalingStroke });
                if (operation == 4) composed.Children.Add(markers);
            }
            return composed;
        }
        if (fill is null || stroke is null || !TryParse(order ?? "normal", out var strokeFirst) || !strokeFirst)
            return new GeometryDrawing(fill, stroke, geometry) { SvgNonScalingStroke = nonScalingStroke };
        // Existing drawing operations preserve this order on direct, raster and
        // recorded paths. The enclosing element applies opacity/clip/mask once.
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(null, stroke, geometry) { SvgNonScalingStroke = nonScalingStroke });
        drawing.Children.Add(new GeometryDrawing(fill, null, geometry));
        return drawing;
    }
}
