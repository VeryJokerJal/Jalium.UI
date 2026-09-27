using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>
/// Parses linear/radial and repeating gradients into gradient brushes. CSS angles (0deg = up,
/// clockwise) are converted to relative start/end points; stops with omitted positions are
/// distributed per the CSS interpolation rules and monotonically clamped.
/// </summary>
internal static class CssGradientParser
{
    private enum RadialExtent { ClosestSide, ClosestCorner, FarthestSide, FarthestCorner }

    public static bool TryParseGradientFunction(ReadOnlySpan<char> name, ref CssTokenReader args, out Brush? brush)
    {
        var source = args.Remaining.ToString();
        var provisional = new CssLengthContext(14, 14, 14, 100, 100);
        var conic = name.Equals("conic-gradient", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("repeating-conic-gradient", StringComparison.OrdinalIgnoreCase);
        var dependsOnCurrentColor = false;
        if (!TryParseResolved(name, ref args, provisional, conic ? 1 : 100, conic ? 1 : 100, out brush))
        {
            if (!CssColorParser.TrySubstituteCurrentColor(source, Colors.Black, out var substituted, out dependsOnCurrentColor) ||
                !dependsOnCurrentColor) return false;
            var replacement = new CssTokenReader(substituted);
            if (!TryParseResolved(name, ref replacement, provisional, conic ? 1 : 100, conic ? 1 : 100, out brush))
                return false;
        }
        if (brush is not null)
            brush.CssGradientLayout = new CssGradientLayout(name.ToString(), source, provisional, dependsOnCurrentColor);
        return true;
    }

    internal static bool TryParseResolved(ReadOnlySpan<char> name, ref CssTokenReader args,
        in CssLengthContext context, double width, double height, out Brush? brush)
    {
        if (name.Equals("linear-gradient", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("repeating-linear-gradient", StringComparison.OrdinalIgnoreCase))
        {
            brush = ParseLinear(ref args, context, width, height,
                name.Equals("repeating-linear-gradient", StringComparison.OrdinalIgnoreCase));
            return brush is not null;
        }

        if (name.Equals("radial-gradient", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("repeating-radial-gradient", StringComparison.OrdinalIgnoreCase))
        {
            brush = ParseRadial(ref args, context, width, height,
                name.Equals("repeating-radial-gradient", StringComparison.OrdinalIgnoreCase));
            return brush is not null;
        }

        if (name.Equals("conic-gradient", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("repeating-conic-gradient", StringComparison.OrdinalIgnoreCase))
        {
            brush = CssConicGradientParser.Parse(ref args, context, width, height,
                name.Equals("repeating-conic-gradient", StringComparison.OrdinalIgnoreCase));
            return brush is not null;
        }

        brush = null;
        return false;
    }

    private static LinearGradientBrush? ParseLinear(ref CssTokenReader args,
        in CssLengthContext context, double width, double height, bool repeating)
    {
        var angle = 180.0; // CSS default: "to bottom".
        var probe = args;
        CssColorParser.CssInterpolationMethod? interpolation = null;
        var sawPrelude = false;
        if (TryParseInterpolation(ref probe, out var firstSpace))
        {
            interpolation = firstSpace;
            sawPrelude = true;
        }
        var directionProbe = probe;
        if (directionProbe.TryReadNumber(out var angleValue, out var angleUnit) &&
            CssUnitConversion.TryToDegrees(angleValue, angleUnit, out var parsedAngle) &&
            angleUnit is CssUnit.Deg or CssUnit.Rad or CssUnit.Grad or CssUnit.Turn)
        {
            angle = parsedAngle;
            probe = directionProbe;
            sawPrelude = true;
        }
        else
        {
            directionProbe = probe;
            if (directionProbe.TryReadIdent(out var to) && to.Equals("to", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseSideOrCorner(ref directionProbe, out angle, width, height)) return null;
                probe = directionProbe;
                sawPrelude = true;
            }
        }
        if (interpolation is null && TryParseInterpolation(ref probe, out var secondSpace))
        {
            interpolation = secondSpace;
            sawPrelude = true;
        }
        if (sawPrelude)
        {
            if (!probe.TryReadComma()) return null;
            args = probe;
        }

        // A CSS gradient line passes through the box center. Its endpoints are
        // the projections of the opposite corners onto that direction.
        var radians = angle * Math.PI / 180.0;
        var dirX = Math.Sin(radians);
        var dirY = -Math.Cos(radians);
        var lineLength = Math.Abs(width * dirX) + Math.Abs(height * dirY);
        if (!(lineLength > 0) || !double.IsFinite(lineLength)) return null;
        var stops = ParseStops(ref args, interpolation, context, lineLength);
        if (stops is null)
        {
            return null;
        }
        if (repeating) stops = RepeatStops(stops, lineLength, 1);

        var brush = new LinearGradientBrush(stops)
        {
            StartPoint = new Point(0.5 - dirX * lineLength / (2 * width),
                0.5 - dirY * lineLength / (2 * height)),
            EndPoint = new Point(0.5 + dirX * lineLength / (2 * width),
                0.5 + dirY * lineLength / (2 * height)),
        };
        return brush;
    }

    private static bool TryParseInterpolation(ref CssTokenReader reader,
        out CssColorParser.CssInterpolationMethod method)
        => CssColorParser.TryReadInterpolationMethod(ref reader, out method);

    private static bool TryParseSideOrCorner(ref CssTokenReader reader, out double angle,
        double width, double height)
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

        // A corner keyword points perpendicular to the line through its two
        // neighboring corners; the angle therefore depends on box aspect ratio.
        angle = (vertical, horizontal) switch
        {
            (-1, 0) => 0,
            (1, 0) => 180,
            (0, -1) => 270,
            (0, 1) => 90,
            (-1, 1) or (1, 1) or (1, -1) or (-1, -1) =>
                (Math.Atan2(horizontal * height, -vertical * width) * 180 / Math.PI + 360) % 360,
            _ => double.NaN,
        };
        return !double.IsNaN(angle);
    }

    private static RadialGradientBrush? ParseRadial(ref CssTokenReader args,
        in CssLengthContext context, double width, double height, bool repeating)
    {
        var brush = new RadialGradientBrush();
        bool? shapeIsCircle = null;
        var extent = RadialExtent.FarthestCorner;
        var hasExtent = false;
        CssLength? firstRadius = null, secondRadius = null;
        var hasPosition = false;
        var sawPrelude = false;
        CssColorParser.CssInterpolationMethod? interpolation = null;

        // Optional shape/extent or explicit radii, followed by an optional
        // position and/or color-interpolation method before the stop list.
        var probe = args;
        while (true)
        {
            var beforeIdent = probe;
            if (!probe.TryReadIdent(out var ident))
            {
                probe = beforeIdent;
                if (!probe.TryReadLength(out var size)) break;
                if (hasPosition || hasExtent || secondRadius is not null || size.Unit == CssUnit.None && size.Value != 0)
                    return null;
                if (firstRadius is null) firstRadius = size;
                else secondRadius = size;
                sawPrelude = true;
                continue;
            }

            if (ident.Equals("circle", StringComparison.OrdinalIgnoreCase))
            {
                if (hasPosition || shapeIsCircle is not null) return null;
                shapeIsCircle = true;
                sawPrelude = true;
            }
            else if (ident.Equals("ellipse", StringComparison.OrdinalIgnoreCase))
            {
                if (hasPosition || shapeIsCircle is not null) return null;
                shapeIsCircle = false;
                sawPrelude = true;
            }
            else if (ident.Equals("closest-side", StringComparison.OrdinalIgnoreCase) ||
                     ident.Equals("closest-corner", StringComparison.OrdinalIgnoreCase) ||
                     ident.Equals("farthest-side", StringComparison.OrdinalIgnoreCase) ||
                     ident.Equals("farthest-corner", StringComparison.OrdinalIgnoreCase))
            {
                if (hasPosition || hasExtent || firstRadius is not null) return null;
                extent = ident.ToString().ToLowerInvariant() switch
                {
                    "closest-side" => RadialExtent.ClosestSide,
                    "closest-corner" => RadialExtent.ClosestCorner,
                    "farthest-side" => RadialExtent.FarthestSide,
                    _ => RadialExtent.FarthestCorner,
                };
                hasExtent = true;
                sawPrelude = true;
            }
            else if (ident.Equals("at", StringComparison.OrdinalIgnoreCase))
            {
                if (hasPosition || !TryParsePosition(ref probe, context, width, height, out var center))
                {
                    return null;
                }

                brush.Center = center;
                brush.GradientOrigin = center;
                hasPosition = true;
                sawPrelude = true;
                continue;
            }
            else if (ident.Equals("in", StringComparison.OrdinalIgnoreCase))
            {
                probe = beforeIdent;
                if (interpolation is not null || !TryParseInterpolation(ref probe, out var space)) return null;
                interpolation = space;
                sawPrelude = true;
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

        var isCircle = shapeIsCircle ?? (firstRadius is not null && secondRadius is null);
        if (isCircle && secondRadius is not null || !isCircle && firstRadius is not null && secondRadius is null)
            return null;

        var centerX = brush.Center.X * width;
        var centerY = brush.Center.Y * height;
        var nearX = Math.Min(Math.Abs(centerX), Math.Abs(width - centerX));
        var nearY = Math.Min(Math.Abs(centerY), Math.Abs(height - centerY));
        var farX = Math.Max(Math.Abs(centerX), Math.Abs(width - centerX));
        var farY = Math.Max(Math.Abs(centerY), Math.Abs(height - centerY));
        double radiusX, radiusY;
        if (firstRadius is { } specified)
        {
            var circleBasis = Math.Sqrt(width * width + height * height) / Math.Sqrt(2);
            if (!TryResolveRadialSize(specified, context, isCircle ? circleBasis : width, out radiusX))
                return null;
            if (isCircle) radiusY = radiusX;
            else if (secondRadius is not { } second ||
                !TryResolveRadialSize(second, context, height, out radiusY)) return null;
        }
        else if (isCircle)
        {
            radiusX = extent switch
            {
                RadialExtent.ClosestSide => Math.Min(nearX, nearY),
                RadialExtent.FarthestSide => Math.Max(farX, farY),
                RadialExtent.ClosestCorner => Math.Sqrt(nearX * nearX + nearY * nearY),
                _ => Math.Sqrt(farX * farX + farY * farY),
            };
            radiusY = radiusX;
        }
        else
        {
            var nearest = extent is RadialExtent.ClosestSide or RadialExtent.ClosestCorner;
            radiusX = nearest ? nearX : farX;
            radiusY = nearest ? nearY : farY;
            if (extent is RadialExtent.ClosestCorner or RadialExtent.FarthestCorner)
            {
                var cornerX = nearest ? nearX : farX;
                var cornerY = nearest ? nearY : farY;
                var factor = Math.Sqrt(Math.Pow(cornerX / Math.Max(radiusX, 1e-6), 2) +
                    Math.Pow(cornerY / Math.Max(radiusY, 1e-6), 2));
                radiusX *= factor;
                radiusY *= factor;
            }
        }
        if (!double.IsFinite(radiusX) || !double.IsFinite(radiusY)) return null;
        brush.RadiusX = Math.Max(radiusX, 1e-6) / width;
        brush.RadiusY = Math.Max(radiusY, 1e-6) / height;

        // CSS radial stop lengths use the rightward gradient line to the ending
        // ellipse; this brush's rightward radius is the same paint-space line.
        var lineLength = Math.Max(radiusX, 1e-6);
        var stops = ParseStops(ref args, interpolation, context, lineLength);
        if (stops is null)
        {
            return null;
        }

        if (repeating)
        {
            // A radial brush clamps outside its ending ellipse. Extend that
            // ellipse through the farthest painted corner, scaling the stop
            // offsets back so each authored period keeps its physical size.
            var coverage = 1.0;
            foreach (var x in new[] { 0.0, width })
            foreach (var y in new[] { 0.0, height })
                coverage = Math.Max(coverage, Math.Sqrt(
                    Math.Pow((x - centerX) / Math.Max(radiusX, 1e-6), 2) +
                    Math.Pow((y - centerY) / Math.Max(radiusY, 1e-6), 2)));
            if (!double.IsFinite(coverage)) return null;
            stops = RepeatStops(stops, lineLength, coverage);
            brush.RadiusX *= coverage;
            brush.RadiusY *= coverage;
        }

        foreach (var stop in stops)
        {
            brush.GradientStops.Add(stop);
        }

        return brush;
    }

    internal static GradientStopCollection RepeatStops(GradientStopCollection source,
        double lineLength, double coverage)
    {
        var first = source[0].Offset;
        var last = source[^1].Offset;
        var period = last - first;
        // A cycle smaller than a CSS pixel cannot be represented reliably by
        // the native gradient ramp. CSS Images specifies its average color.
        if (!(period > 0) || period * lineLength < 1)
            return AverageStops(source, period > 0);

        var firstCycle = Math.Floor(-last / period);
        var lastCycle = Math.Ceiling((coverage - first) / period);
        var cycles = lastCycle - firstCycle + 1;
        // Native gradient stops have a 64K bound. Averaging is preferable to
        // truncating the ramp and painting a false solid-colored tail.
        if (!double.IsFinite(cycles) || cycles * source.Count > 65532 ||
            firstCycle <= (double)long.MinValue || lastCycle >= (double)long.MaxValue)
            return AverageStops(source, true);

        var expanded = new GradientStopCollection
        {
            new(RepeatedColorAt(source, first, period, 0), 0),
        };
        for (var cycle = (long)firstCycle; cycle <= (long)lastCycle; cycle++)
        {
            var shift = cycle * period;
            foreach (var stop in source)
            {
                var position = stop.Offset + shift;
                if (position > 0 && position < coverage)
                    expanded.Add(new GradientStop(stop.Color, position / coverage));
            }
        }
        expanded.Add(new GradientStop(RepeatedColorAt(source, first, period, coverage), 1));
        return expanded;
    }

    private static Color RepeatedColorAt(GradientStopCollection source, double first,
        double period, double position)
    {
        var phase = first + ((position - first) % period + period) % period;
        var color = source[0].Color;
        for (var i = 1; i < source.Count; i++)
        {
            var left = source[i - 1];
            var right = source[i];
            if (phase < right.Offset)
            {
                var fraction = (float)((phase - left.Offset) / (right.Offset - left.Offset));
                return Color.FromScRgb(
                    left.Color.ScA + (right.Color.ScA - left.Color.ScA) * fraction,
                    left.Color.ScR + (right.Color.ScR - left.Color.ScR) * fraction,
                    left.Color.ScG + (right.Color.ScG - left.Color.ScG) * fraction,
                    left.Color.ScB + (right.Color.ScB - left.Color.ScB) * fraction);
            }
            color = right.Color;
        }
        return color;
    }

    private static GradientStopCollection AverageStops(GradientStopCollection source, bool positioned)
    {
        double alpha = 0, red = 0, green = 0, blue = 0;
        var length = positioned ? source[^1].Offset - source[0].Offset : source.Count - 1;
        for (var i = 1; i < source.Count; i++)
        {
            var segment = positioned ? source[i].Offset - source[i - 1].Offset : 1;
            if (segment <= 0) continue;
            var weight = segment / (2 * length);
            Accumulate(source[i - 1].Color, weight);
            Accumulate(source[i].Color, weight);
        }
        var average = Color.FromArgb(
            ToByte(alpha), ToByte(alpha > 0 ? red / alpha : 0),
            ToByte(alpha > 0 ? green / alpha : 0), ToByte(alpha > 0 ? blue / alpha : 0));
        return new GradientStopCollection { new(average, 0), new(average, 1) };

        void Accumulate(Color color, double weight)
        {
            var opacity = color.A / 255.0;
            alpha += opacity * weight;
            red += color.R / 255.0 * opacity * weight;
            green += color.G / 255.0 * opacity * weight;
            blue += color.B / 255.0 * opacity * weight;
        }

        static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
    }

    private static bool TryResolveRadialSize(CssLength length, in CssLengthContext context,
        double percentageBasis, out double pixels) =>
        TryResolveGradientLength(length, context, percentageBasis, out pixels) && pixels >= 0;

    private static bool TryResolveGradientLength(CssLength length, in CssLengthContext context,
        double percentageBasis, out double pixels)
    {
        pixels = double.NaN;
        if (length.Unit == CssUnit.None && length.Value != 0) return false;
        length.ObserveContainerDependencies(context);
        if (length.Unit == CssUnit.Percent) pixels = length.Value * percentageBasis / 100;
        else if (length.Unit == CssUnit.Expression)
        {
            if (length.Expression is null || !length.Expression.TryEvaluate(context, percentageBasis, out pixels))
                return false;
        }
        else if (!length.TryResolve(context, CssPercentBasis.NotSupported, out pixels)) return false;
        return double.IsFinite(pixels);
    }

    internal static bool TryParsePosition(ref CssTokenReader reader, in CssLengthContext context,
        double width, double height, out Point position)
    {
        var x = 0.5; var y = 0.5;
        var xSet = false; var ySet = false; var parsed = false;
        for (var component = 0; component < 4; component++)
        {
            var probe = reader;
            if (probe.TryReadIdent(out var ident))
            {
                var name = ident.ToString().ToLowerInvariant();
                if (name is "left" or "right")
                {
                    if (xSet) { position = default; return false; }
                    var offset = probe;
                    if (offset.TryReadLength(out var offsetLength))
                    {
                        if (!TryResolveGradientLength(offsetLength, context, width, out var pixels))
                        { position = default; return false; }
                        x = name == "left" ? pixels / width : 1 - pixels / width;
                        probe = offset;
                    }
                    else x = name == "left" ? 0 : 1;
                    xSet = true;
                }
                else if (name is "top" or "bottom")
                {
                    if (ySet) { position = default; return false; }
                    var offset = probe;
                    if (offset.TryReadLength(out var offsetLength))
                    {
                        if (!TryResolveGradientLength(offsetLength, context, height, out var pixels))
                        { position = default; return false; }
                        y = name == "top" ? pixels / height : 1 - pixels / height;
                        probe = offset;
                    }
                    else y = name == "top" ? 0 : 1;
                    ySet = true;
                }
                else if (name == "center")
                {
                    if (xSet && ySet) { position = default; return false; }
                    if (xSet) ySet = true;
                    else if (ySet) xSet = true;
                    else
                    {
                        // center left/right is the same as left/right center.
                        var next = probe;
                        if (next.TryReadIdent(out var following) &&
                            (following.Equals("left", StringComparison.OrdinalIgnoreCase) ||
                             following.Equals("right", StringComparison.OrdinalIgnoreCase))) ySet = true;
                        else xSet = true;
                    }
                }
                else break;
                reader = probe; parsed = true;
                continue;
            }

            probe = reader;
            if (probe.TryReadLength(out var length))
            {
                if (!xSet)
                {
                    if (!TryResolveGradientLength(length, context, width, out var pixels))
                    { position = default; return false; }
                    x = pixels / width; xSet = true;
                }
                else if (!ySet)
                {
                    if (!TryResolveGradientLength(length, context, height, out var pixels))
                    { position = default; return false; }
                    y = pixels / height; ySet = true;
                }
                else { position = default; return false; }
                reader = probe; parsed = true;
                continue;
            }

            break;
        }

        position = new Point(x, y);
        return parsed;
    }

    /// <summary>Parses the comma-separated color-stop list, interpolating omitted positions.</summary>
    internal static GradientStopCollection? ParseStops(ref CssTokenReader args,
        CssColorParser.CssInterpolationMethod? interpolation,
        in CssLengthContext context, double lineLength, bool angular = false)
    {
        var colors = new List<CssColorParser.CssColorData>();
        var positions = new List<double>(); // NaN = omitted
        var hints = new List<double>(); // one entry per color interval; NaN = no hint
        while (true)
        {
            if (!CssColorParser.TryParseWithData(ref args, out _, out var isCurrentColor, out var color) || isCurrentColor)
            {
                return null;
            }

            if (!TryReadStopPosition(ref args, context, lineLength, angular, out var pos, out var hasPosition))
                return null;
            if (hasPosition)
            {
                colors.Add(color);
                positions.Add(pos);

                // Double-position stop ("red 20% 40%") expands into two identical stops.
                var probe = args;
                if (!TryReadStopPosition(ref probe, context, lineLength, angular, out var pos2, out var hasSecond))
                    return null;
                if (hasSecond)
                {
                    colors.Add(color);
                    positions.Add(pos2);
                    hints.Add(double.NaN); // the two positions form separate stops
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
            var hintProbe = args;
            if (!TryReadStopPosition(ref hintProbe, context, lineLength, angular, out var hint, out var hasHint))
                return null;
            if (hasHint && hintProbe.TryReadComma())
            {
                hints.Add(hint);
                args = hintProbe;
            }
            else hints.Add(double.NaN);
        }

        if (!args.AtEnd || colors.Count == 0 || hints.Count != colors.Count - 1)
        {
            return null;
        }
        // A one-stop gradient is the solid color across its entire line,
        // regardless of an authored stop position or interpolation method.
        if (colors.Count == 1)
        {
            return new GradientStopCollection
            {
                new(colors[0].Rendered, 0), new(colors[0].Rendered, 1),
            };
        }
        // The native brush ABI accepts at most 64K stops. Allow one extra
        // boundary on each side of an interior stop with missing components.
        if (colors.Count > 32768) return null;
        var stopBudget = Math.Max(8192L, colors.Count * 2L);
        var maxDepth = 0;
        while (maxDepth < 7 && 2L * colors.Count + (colors.Count - 1L) * (1L << (maxDepth + 1)) <= stopBudget)
            maxDepth++;

        // Fix up the ordered list of stops AND hints. A hint may advance a later
        // authored stop, and can anchor an omitted stop's distributed position.
        if (double.IsNaN(positions[0]))
        {
            positions[0] = 0;
        }

        if (double.IsNaN(positions[^1]))
        {
            positions[^1] = 1;
        }

        var ordered = new List<double>();
        var owners = new List<(bool Hint, int Index)>();
        for (var i = 0; i < colors.Count; i++)
        {
            ordered.Add(positions[i]); owners.Add((false, i));
            if (i < hints.Count && !double.IsNaN(hints[i]))
            {
                ordered.Add(hints[i]); owners.Add((true, i));
            }
        }
        var previous = ordered[0];
        for (var i = 1; i < ordered.Count; i++)
        {
            if (double.IsNaN(ordered[i])) continue;
            previous = ordered[i] = Math.Max(previous, ordered[i]);
        }
        var lastKnown = 0;
        for (var i = 1; i < ordered.Count; i++)
        {
            if (double.IsNaN(ordered[i])) continue;

            var gap = i - lastKnown;
            if (gap > 1)
            {
                var step = (ordered[i] - ordered[lastKnown]) / gap;
                for (var j = 1; j < gap; j++)
                    ordered[lastKnown + j] = ordered[lastKnown] + step * j;
            }

            lastKnown = i;
        }
        for (var i = 0; i < ordered.Count; i++)
        {
            var (isHint, index) = owners[i];
            if (isHint) hints[index] = ordered[i];
            else positions[index] = ordered[i];
        }
        var method = interpolation ?? new CssColorParser.CssInterpolationMethod(colors.All(static color => color.Legacy)
            ? CssColorParser.CssInterpolationSpace.Srgb
            : CssColorParser.CssInterpolationSpace.Oklab);
        var stops = new GradientStopCollection();
        stops.Add(new GradientStop(colors[0].Rendered, positions[0]));
        for (var i = 1; i < colors.Count; i++)
        {
            if (positions[i] > positions[i - 1])
            {
                var effectiveStart = CssColorParser.Interpolate(colors[i - 1], colors[i], method, 0);
                var effectiveEnd = CssColorParser.Interpolate(colors[i - 1], colors[i], method, 1);
                if (stops[^1].Color.ToArgb() != effectiveStart.ToArgb())
                    stops.Add(new GradientStop(effectiveStart, positions[i - 1]));
                var hint = double.IsNaN(hints[i - 1]) ? double.NaN :
                    (hints[i - 1] - positions[i - 1]) / (positions[i] - positions[i - 1]);
                AddInterpolatedStops(stops, colors[i - 1], colors[i], method,
                    positions[i - 1], positions[i], 0, effectiveStart,
                    1, effectiveEnd, hint, 0, maxDepth);
                if (i == colors.Count - 1 && effectiveEnd.ToArgb() != colors[i].Rendered.ToArgb())
                    stops.Add(new GradientStop(colors[i].Rendered, positions[i]));
            }
            else stops.Add(new GradientStop(colors[i].Rendered, positions[i]));
        }
        return stops;
    }

    private static bool TryReadStopPosition(ref CssTokenReader reader,
        in CssLengthContext context, double lineLength, bool angular, out double position, out bool read)
    {
        position = double.NaN;
        read = false;
        var probe = reader;
        if (angular)
        {
            if (!probe.TryReadNumber(out var value, out var unit)) return true;
            if (!double.IsFinite(value)) return false;
            if (unit == CssUnit.Percent) position = value / 100;
            else if (unit == CssUnit.None && value == 0) position = 0;
            else if (unit is CssUnit.Deg or CssUnit.Rad or CssUnit.Grad or CssUnit.Turn &&
                     CssUnitConversion.TryToDegrees(value, unit, out var degrees))
                position = degrees / 360;
            else return false;
            if (!double.IsFinite(position)) return false;
            reader = probe;
            read = true;
            return true;
        }
        if (!probe.TryReadLength(out var length)) return true;
        if (length.Unit == CssUnit.None && length.Value != 0) return false;
        length.ObserveContainerDependencies(context);
        double pixels;
        if (length.Unit == CssUnit.Percent) pixels = length.Value * lineLength / 100;
        else if (length.Unit == CssUnit.Expression)
        {
            if (length.Expression is null || !length.Expression.TryEvaluate(context, lineLength, out pixels))
                return false;
        }
        else if (!length.TryResolve(context, CssPercentBasis.NotSupported, out pixels)) return false;
        if (!double.IsFinite(pixels) || !(lineLength > 0)) return false;
        position = pixels / lineLength;
        if (!double.IsFinite(position)) return false;
        reader = probe;
        read = true;
        return true;
    }

    private static void AddInterpolatedStops(GradientStopCollection stops,
        CssColorParser.CssColorData from, CssColorParser.CssColorData to,
        CssColorParser.CssInterpolationMethod method, double start, double end,
        double left, Color leftColor, double right, Color rightColor,
        double hint, int depth, int maxDepth)
    {
        var middle = (left + right) * 0.5;
        var actual = CssColorParser.Interpolate(from, to, method, HintWeight(middle, hint));
        var gamma = Color.FromArgb((byte)((leftColor.A + rightColor.A + 1) / 2),
            (byte)((leftColor.R + rightColor.R + 1) / 2),
            (byte)((leftColor.G + rightColor.G + 1) / 2),
            (byte)((leftColor.B + rightColor.B + 1) / 2));
        var linear = Color.FromScRgb((leftColor.ScA + rightColor.ScA) * 0.5f,
            (leftColor.ScR + rightColor.ScR) * 0.5f,
            (leftColor.ScG + rightColor.ScG) * 0.5f,
            (leftColor.ScB + rightColor.ScB) * 0.5f);
        static int Error(Color a, Color b) => Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)),
            Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A)));
        if (depth < maxDepth && (Error(actual, gamma) > 1 || Error(actual, linear) > 1))
        {
            AddInterpolatedStops(stops, from, to, method, start, end,
                left, leftColor, middle, actual, hint, depth + 1, maxDepth);
            AddInterpolatedStops(stops, from, to, method, start, end,
                middle, actual, right, rightColor, hint, depth + 1, maxDepth);
        }
        else stops.Add(new GradientStop(rightColor, start + (end - start) * right));
    }

    private static double HintWeight(double position, double hint)
    {
        if (double.IsNaN(hint) || Math.Abs(hint - .5) < 1e-12) return position;
        if (position <= 0) return 0;
        if (position >= 1) return 1;
        // The limits at 0 and 1 put the halfway color immediately after the
        // first stop or immediately before the second stop, respectively.
        if (hint <= 0) return 1;
        if (hint >= 1) return 0;
        return Math.Pow(position, Math.Log(.5) / Math.Log(hint));
    }
}
