using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>
/// Parses linear-gradient()/radial-gradient() into gradient brushes. CSS angles (0deg = up,
/// clockwise) are converted to relative start/end points; stops with omitted positions are
/// distributed per the CSS interpolation rules and monotonically clamped.
/// </summary>
internal static class CssGradientParser
{
    public static bool TryParseGradientFunction(ReadOnlySpan<char> name, ref CssTokenReader args, out Brush? brush)
    {
        if (name.Equals("linear-gradient", StringComparison.OrdinalIgnoreCase))
        {
            brush = ParseLinear(ref args);
            return brush is not null;
        }

        if (name.Equals("radial-gradient", StringComparison.OrdinalIgnoreCase))
        {
            brush = ParseRadial(ref args);
            return brush is not null;
        }

        brush = null;
        return false;
    }

    private static LinearGradientBrush? ParseLinear(ref CssTokenReader args)
    {
        var angle = 180.0; // CSS default: "to bottom".
        var probe = args;
        if (probe.TryReadNumber(out var angleValue, out var angleUnit) &&
            CssUnitConversion.TryToDegrees(angleValue, angleUnit, out var parsedAngle) &&
            angleUnit is CssUnit.Deg or CssUnit.Rad or CssUnit.Grad or CssUnit.Turn)
        {
            if (!probe.TryReadComma())
            {
                return null;
            }

            angle = parsedAngle;
            args = probe;
        }
        else
        {
            probe = args;
            if (probe.TryReadIdent(out var to) && to.Equals("to", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseSideOrCorner(ref probe, out angle) || !probe.TryReadComma())
                {
                    return null;
                }

                args = probe;
            }
        }

        var stops = ParseStops(ref args);
        if (stops is null)
        {
            return null;
        }

        // CSS angle (0 = up, clockwise) → relative endpoints on the unit box (screen y grows down).
        var radians = angle * Math.PI / 180.0;
        var dirX = Math.Sin(radians);
        var dirY = -Math.Cos(radians);
        var brush = new LinearGradientBrush(stops)
        {
            StartPoint = new Point(0.5 - dirX / 2.0, 0.5 - dirY / 2.0),
            EndPoint = new Point(0.5 + dirX / 2.0, 0.5 + dirY / 2.0),
        };
        return brush;
    }

    private static bool TryParseSideOrCorner(ref CssTokenReader reader, out double angle)
    {
        var vertical = 0; // -1 = top, 1 = bottom
        var horizontal = 0; // -1 = left, 1 = right
        for (var i = 0; i < 2; i++)
        {
            var probe = reader;
            if (!probe.TryReadIdent(out var ident))
            {
                break;
            }

            if (ident.Equals("top", StringComparison.OrdinalIgnoreCase) && vertical == 0)
            {
                vertical = -1;
            }
            else if (ident.Equals("bottom", StringComparison.OrdinalIgnoreCase) && vertical == 0)
            {
                vertical = 1;
            }
            else if (ident.Equals("left", StringComparison.OrdinalIgnoreCase) && horizontal == 0)
            {
                horizontal = -1;
            }
            else if (ident.Equals("right", StringComparison.OrdinalIgnoreCase) && horizontal == 0)
            {
                horizontal = 1;
            }
            else
            {
                break;
            }

            reader = probe;
        }

        // Corner angles use the 45° approximation (exact CSS corner semantics need box size).
        angle = (vertical, horizontal) switch
        {
            (-1, 0) => 0,
            (1, 0) => 180,
            (0, -1) => 270,
            (0, 1) => 90,
            (-1, 1) => 45,
            (1, 1) => 135,
            (1, -1) => 225,
            (-1, -1) => 315,
            _ => double.NaN,
        };
        return !double.IsNaN(angle);
    }

    private static RadialGradientBrush? ParseRadial(ref CssTokenReader args)
    {
        var brush = new RadialGradientBrush();
        var isCircle = false;
        var sawPrelude = false;

        // Optional prelude: [circle|ellipse|<size keyword>]* [at <position>]?, then a comma.
        var probe = args;
        while (true)
        {
            var beforeIdent = probe;
            if (!probe.TryReadIdent(out var ident))
            {
                break;
            }

            if (ident.Equals("circle", StringComparison.OrdinalIgnoreCase))
            {
                isCircle = true;
                sawPrelude = true;
            }
            else if (ident.Equals("ellipse", StringComparison.OrdinalIgnoreCase))
            {
                sawPrelude = true;
            }
            else if (ident.Equals("closest-side", StringComparison.OrdinalIgnoreCase) ||
                     ident.Equals("closest-corner", StringComparison.OrdinalIgnoreCase) ||
                     ident.Equals("farthest-side", StringComparison.OrdinalIgnoreCase) ||
                     ident.Equals("farthest-corner", StringComparison.OrdinalIgnoreCase))
            {
                CssDiagnostics.Report(
                    "background-image", CssDiagnosticReason.LossyConversion, null,
                    $"radial-gradient size '{ident.ToString()}' is approximated by the default radius");
                sawPrelude = true;
            }
            else if (ident.Equals("at", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParsePosition(ref probe, out var center))
                {
                    return null;
                }

                brush.Center = center;
                brush.GradientOrigin = center;
                sawPrelude = true;
                break;
            }
            else
            {
                // Not a prelude keyword — it is the first stop's color name; rewind past it.
                probe = beforeIdent;
                break;
            }
        }

        if (sawPrelude)
        {
            if (!probe.TryReadComma())
            {
                return null;
            }

            args = probe;
        }

        if (isCircle)
        {
            brush.RadiusX = 0.5;
            brush.RadiusY = 0.5;
        }

        var stops = ParseStops(ref args);
        if (stops is null)
        {
            return null;
        }

        foreach (var stop in stops)
        {
            brush.GradientStops.Add(stop);
        }

        return brush;
    }

    private static bool TryParsePosition(ref CssTokenReader reader, out Point position)
    {
        var x = 0.5;
        var y = 0.5;
        var component = 0;
        while (component < 2)
        {
            var probe = reader;
            if (probe.TryReadIdent(out var ident))
            {
                if (ident.Equals("left", StringComparison.OrdinalIgnoreCase)) { x = 0; }
                else if (ident.Equals("right", StringComparison.OrdinalIgnoreCase)) { x = 1; }
                else if (ident.Equals("top", StringComparison.OrdinalIgnoreCase)) { y = 0; }
                else if (ident.Equals("bottom", StringComparison.OrdinalIgnoreCase)) { y = 1; }
                else if (ident.Equals("center", StringComparison.OrdinalIgnoreCase)) { }
                else { break; }

                reader = probe;
                component++;
                continue;
            }

            if (probe.TryReadNumber(out var value, out var unit) && unit == CssUnit.Percent)
            {
                if (component == 0)
                {
                    x = value / 100.0;
                }
                else
                {
                    y = value / 100.0;
                }

                reader = probe;
                component++;
                continue;
            }

            break;
        }

        position = new Point(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
        return component > 0;
    }

    /// <summary>Parses the comma-separated color-stop list, interpolating omitted positions.</summary>
    private static GradientStopCollection? ParseStops(ref CssTokenReader args)
    {
        var colors = new List<Color>();
        var positions = new List<double>(); // NaN = omitted
        while (true)
        {
            if (!CssColorParser.TryParse(ref args, out var color, out var isCurrentColor) || isCurrentColor)
            {
                return null;
            }

            if (args.TryReadNumber(out var pos, out var posUnit) && posUnit == CssUnit.Percent)
            {
                colors.Add(color);
                positions.Add(pos / 100.0);

                // Double-position stop ("red 20% 40%") expands into two identical stops.
                var probe = args;
                if (probe.TryReadNumber(out var pos2, out var pos2Unit) && pos2Unit == CssUnit.Percent)
                {
                    colors.Add(color);
                    positions.Add(pos2 / 100.0);
                    args = probe;
                }
            }
            else
            {
                colors.Add(color);
                positions.Add(double.NaN);
            }

            if (!args.TryReadComma())
            {
                break;
            }
        }

        if (!args.AtEnd || colors.Count < 2)
        {
            return null;
        }

        // CSS position resolution: first defaults to 0, last to 1, interior omitted runs are
        // evenly distributed between the neighboring known positions; clamp to be monotonic.
        if (double.IsNaN(positions[0]))
        {
            positions[0] = 0;
        }

        if (double.IsNaN(positions[^1]))
        {
            positions[^1] = 1;
        }

        var lastKnown = 0;
        for (var i = 1; i < positions.Count; i++)
        {
            if (double.IsNaN(positions[i]))
            {
                continue;
            }

            var gap = i - lastKnown;
            if (gap > 1)
            {
                var step = (positions[i] - positions[lastKnown]) / gap;
                for (var j = 1; j < gap; j++)
                {
                    positions[lastKnown + j] = positions[lastKnown] + step * j;
                }
            }

            lastKnown = i;
        }

        var running = 0.0;
        var stops = new GradientStopCollection();
        for (var i = 0; i < colors.Count; i++)
        {
            running = Math.Max(running, positions[i]);
            stops.Add(new GradientStop(colors[i], running));
        }

        return stops;
    }
}
