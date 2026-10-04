using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    private static void RegisterClipPath()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "clip-path",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = CssClipPathProperties.ValueProperty,
            Parse = static (ref CssTokenReader reader, CssCompileContext context) => ParseClipPath(ref reader, context),
        });
    }

    private static CssCompiledValue? ParseClipPath(ref CssTokenReader reader, CssCompileContext context)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var none) && none.Equals("none", StringComparison.OrdinalIgnoreCase) && probe.AtEnd)
        {
            reader = probe;
            return new CssImmediateValue(CssClipPathProperties.ValueProperty, CssClipPathValue.None);
        }

        probe = reader;
        if (probe.TryReadFunction(out var sourceName, out var sourceArgs) &&
            sourceName.Equals("url", StringComparison.OrdinalIgnoreCase))
        {
            if (!probe.AtEnd || !TryReadClipSource(ref sourceArgs, context, out var source))
                return null;
            reader = probe;
            return new CssClipPathUrlCompiledValue(source);
        }

        var box = CssClipBox.Border;
        var hasBox = false;
        probe = reader;
        if (TryReadClipBox(ref probe, out var leadingBox))
        {
            box = leadingBox;
            hasBox = true;
            if (probe.AtEnd)
            {
                reader = probe;
                return new CssClipPathCompiledValue(CssClipShape.Box, box, []);
            }
        }
        else probe = reader;

        if (!probe.TryReadFunction(out var name, out var args)) return null;
        var shape = name.ToString().ToLowerInvariant() switch
        {
            "inset" => CssClipShape.Inset,
            "xywh" => CssClipShape.Xywh,
            "rect" => CssClipShape.Rect,
            "circle" => CssClipShape.Circle,
            "ellipse" => CssClipShape.Ellipse,
            "polygon" => CssClipShape.Polygon,
            "path" => CssClipShape.Path,
            "shape" => CssClipShape.Shape,
            _ => CssClipShape.None,
        };
        if (shape == CssClipShape.None) return null;

        if (!hasBox && TryReadClipBox(ref probe, out var trailingBox)) box = trailingBox;
        if (!probe.AtEnd) return null;

        if (shape == CssClipShape.Shape)
        {
            var syntax = CssShapePathParser.Parse(ref args);
            if (syntax is null || !args.AtEnd) return null;
            reader = probe;
            return new CssShapeClipPathCompiledValue(box, syntax);
        }

        var radius = CssClipRadius.Length;
        CssLength[]? roundRadii = null;
        var centerXFromFarEdge = false;
        var centerYFromFarEdge = false;
        var polygonFillRule = Jalium.UI.Media.FillRule.Nonzero;
        CssLength? polygonRoundRadius = null;
        PathGeometry? svgPath = null;
        CssLength[]? lengths;
        if (shape == CssClipShape.Path)
        {
            svgPath = ReadPath(ref args);
            lengths = svgPath is null ? null : [];
        }
        else
        {
            lengths = shape switch
            {
                CssClipShape.Inset or CssClipShape.Xywh or CssClipShape.Rect =>
                    ReadRectangular(ref args, shape, out roundRadii),
                CssClipShape.Polygon => ReadPolygon(ref args, out polygonFillRule, out polygonRoundRadius),
                _ => ReadRadial(ref args, shape, out radius, out centerXFromFarEdge, out centerYFromFarEdge),
            };
        }
        if (lengths is null || !args.AtEnd) return null;
        reader = probe;
        return new CssClipPathCompiledValue(shape, box, lengths,
            shape is CssClipShape.Inset or CssClipShape.Xywh or CssClipShape.Rect
                ? CssClipRadius.Length : radius, roundRadii,
            centerXFromFarEdge, centerYFromFarEdge, polygonFillRule, polygonRoundRadius, svgPath);
    }

    private static bool TryReadClipSource(ref CssTokenReader args, CssCompileContext context,
        out CssClipPathUrlResource source)
    {
        source = null!;
        string reference;
        if (args.TryReadString(out var quoted))
        {
            if (!args.AtEnd) return false;
            reference = quoted;
        }
        else
        {
            var raw = args.Remaining.Trim();
            if (raw.IsEmpty) return false;
            var decoded = new System.Text.StringBuilder(raw.Length);
            for (var index = 0; index < raw.Length;)
            {
                if (raw[index] == '\\')
                {
                    if (!CssSyntax.ReadEscape(raw, ref index, out var escaped)) return false;
                    decoded.Append(escaped);
                    continue;
                }
                var character = raw[index++];
                if (char.IsWhiteSpace(character) || character < 0x20 ||
                    character is '(' or ')' or '\'' or '"') return false;
                decoded.Append(character);
            }
            reference = decoded.ToString();
        }
        if (reference.Length == 0) return false;

        try
        {
            var separator = reference.IndexOf('#');
            var resourceReference = separator < 0 ? reference : reference[..separator];
            var fragment = separator < 0 ? "" : Uri.UnescapeDataString(reference[(separator + 1)..]);
            Uri? uri = null;
            if (resourceReference.Length > 0)
                uri = context.BaseUri is { } baseUri
                    ? CssStyleSheet.ResolveReference(baseUri, resourceReference)
                    : new Uri(resourceReference, UriKind.RelativeOrAbsolute);
            source = new CssClipPathUrlResource(uri, fragment,
                context.ResourceResolver ?? CssStyleSheet.DefaultResolver);
            return true;
        }
        catch (UriFormatException) { return false; }
    }

    private static PathGeometry? ReadPath(ref CssTokenReader args)
    {
        var fillRule = FillRule.Nonzero;
        var probe = args;
        if (probe.TryReadIdent(out var keyword) &&
            (keyword.Equals("evenodd", StringComparison.OrdinalIgnoreCase) ||
             keyword.Equals("nonzero", StringComparison.OrdinalIgnoreCase)))
        {
            fillRule = keyword.Equals("evenodd", StringComparison.OrdinalIgnoreCase)
                ? FillRule.EvenOdd : FillRule.Nonzero;
            if (!probe.TryReadDelimiter(',')) return null;
            args = probe;
        }
        if (!args.TryReadString(out var data) || !args.AtEnd) return null;

        try
        {
            var geometry = PathMarkupParser.ParseSvgPathData(data);
            geometry.FillRule = fillRule;
            foreach (var figure in geometry.Figures) figure.IsClosed = true;
            geometry.Freeze();
            return geometry;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static CssLength[]? ReadPolygon(ref CssTokenReader args,
        out Jalium.UI.Media.FillRule fillRule, out CssLength? roundRadius)
    {
        fillRule = Jalium.UI.Media.FillRule.Nonzero;
        roundRadius = null;
        var hasOptions = false;
        var probe = args;
        if (probe.TryReadIdent(out var keyword) &&
            (keyword.Equals("evenodd", StringComparison.OrdinalIgnoreCase) ||
             keyword.Equals("nonzero", StringComparison.OrdinalIgnoreCase)))
        {
            fillRule = keyword.Equals("evenodd", StringComparison.OrdinalIgnoreCase)
                ? Jalium.UI.Media.FillRule.EvenOdd : Jalium.UI.Media.FillRule.Nonzero;
            args = probe;
            hasOptions = true;
        }

        probe = args;
        if (probe.TryReadIdent(out keyword) && keyword.Equals("round", StringComparison.OrdinalIgnoreCase))
        {
            args = probe;
            if (!TryReadClipLength(ref args, out var parsedRadius) || parsedRadius.UsesPercent ||
                parsedRadius.Expression is null && parsedRadius.Value < 0) return null;
            roundRadius = parsedRadius;
            hasOptions = true;
        }

        if (hasOptions && !args.TryReadDelimiter(',')) return null;
        var vertices = new List<CssLength>();
        while (true)
        {
            if (!TryReadClipLength(ref args, out var x) || !TryReadClipLength(ref args, out var y))
                return null;
            vertices.Add(x);
            vertices.Add(y);
            if (args.AtEnd) return [.. vertices];
            if (!args.TryReadDelimiter(',') || args.AtEnd) return null;
        }
    }

    private static bool TryReadClipBox(ref CssTokenReader reader, out CssClipBox box)
    {
        var probe = reader;
        box = CssClipBox.Border;
        if (!probe.TryReadIdent(out var ident)) return false;
        var mapped = ident.ToString().ToLowerInvariant() switch
        {
            "border-box" or "stroke-box" or "view-box" => CssClipBox.Border,
            "padding-box" => CssClipBox.Padding,
            "content-box" or "fill-box" => CssClipBox.Content,
            "margin-box" => CssClipBox.Margin,
            _ => (CssClipBox?)null,
        };
        if (mapped is null) return false;
        box = mapped.Value;
        reader = probe;
        return true;
    }

    private static CssLength[]? ReadRectangular(ref CssTokenReader args, CssClipShape shape,
        out CssLength[]? roundRadii)
    {
        roundRadii = null;
        var sides = new List<CssLength>(4);
        while (!args.AtEnd)
        {
            var probe = args;
            if (probe.TryReadIdent(out var keyword) && keyword.Equals("round", StringComparison.OrdinalIgnoreCase))
                break;
            if (sides.Count == 4) return null;
            CssLength side;
            probe = args;
            if (shape == CssClipShape.Rect && probe.TryReadIdent(out keyword) &&
                keyword.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                side = new CssLength(sides.Count is 1 or 2 ? 100 : 0, CssUnit.Percent);
                args = probe;
            }
            else if (!TryReadClipLength(ref args, out side)) return null;
            if (shape == CssClipShape.Xywh && sides.Count >= 2 &&
                side.Expression is null && side.Value < 0) return null;
            sides.Add(side);
        }
        if (sides.Count == 0 || shape != CssClipShape.Inset && sides.Count != 4) return null;

        if (!args.AtEnd)
        {
            if (!args.TryReadIdent(out var keyword) || !keyword.Equals("round", StringComparison.OrdinalIgnoreCase))
                return null;
            var horizontal = ReadClipRadiusList(ref args);
            if (horizontal is null) return null;
            var vertical = args.TryReadDelimiter('/') ? ReadClipRadiusList(ref args) : horizontal;
            if (vertical is null || !args.AtEnd) return null;
            roundRadii = [.. horizontal, .. vertical];
        }

        return shape == CssClipShape.Inset ? ExpandFour(sides) : [.. sides];
    }

    private static CssLength[]? ReadClipRadiusList(ref CssTokenReader args)
    {
        var parts = new List<CssLength>(4);
        while (!args.AtEnd)
        {
            if (args.TryPeekChar(out var c) && c == '/') break;
            if (parts.Count == 4 || !TryReadClipLength(ref args, out var radius) ||
                radius.Expression is null && radius.Value < 0) return null;
            parts.Add(radius);
        }
        return parts.Count == 0 ? null : ExpandFour(parts);
    }

    private static CssLength[] ExpandFour(List<CssLength> sides)
        => sides.Count switch
        {
            1 => [sides[0], sides[0], sides[0], sides[0]],
            2 => [sides[0], sides[1], sides[0], sides[1]],
            3 => [sides[0], sides[1], sides[2], sides[1]],
            _ => [sides[0], sides[1], sides[2], sides[3]],
        };

    private static CssLength[]? ReadRadial(ref CssTokenReader args, CssClipShape shape,
        out CssClipRadius radius, out bool centerXFromFarEdge, out bool centerYFromFarEdge)
    {
        radius = CssClipRadius.ClosestSide;
        centerXFromFarEdge = false;
        centerYFromFarEdge = false;
        var centerX = new CssLength(50, CssUnit.Percent);
        var centerY = centerX;
        var rx = default(CssLength);
        var ry = default(CssLength);

        var probe = args;
        if (probe.TryReadIdent(out var keyword) &&
            (keyword.Equals("closest-side", StringComparison.OrdinalIgnoreCase) ||
             keyword.Equals("farthest-side", StringComparison.OrdinalIgnoreCase)))
        {
            radius = keyword.Equals("closest-side", StringComparison.OrdinalIgnoreCase)
                ? CssClipRadius.ClosestSide : CssClipRadius.FarthestSide;
            args = probe;
        }
        else
        {
            probe = args;
            if (TryReadClipLength(ref probe, out rx))
            {
                if (rx.Expression is null && rx.Value < 0) return null;
                radius = CssClipRadius.Length;
                if (shape == CssClipShape.Ellipse &&
                    (!TryReadClipLength(ref probe, out ry) || ry.Expression is null && ry.Value < 0)) return null;
                args = probe;
            }
        }

        if (!args.AtEnd)
        {
            if (!args.TryReadIdent(out var at) || !at.Equals("at", StringComparison.OrdinalIgnoreCase)) return null;
            if (!TryReadBasicShapePosition(ref args, out var position)) return null;
            centerX = position.X.Offset;
            centerY = position.Y.Offset;
            centerXFromFarEdge = position.X.FromFarEdge;
            centerYFromFarEdge = position.Y.FromFarEdge;
        }
        if (!args.AtEnd) return null;
        return shape == CssClipShape.Circle
            ? [centerX, centerY, rx]
            : [centerX, centerY, rx, ry];
    }

    private static bool TryReadBasicShapePosition(ref CssTokenReader args,
        out Jalium.UI.Media.CssBackgroundPosition position)
    {
        var probe = args;
        var tokens = new BackgroundPositionToken[4];
        var count = 0;
        while (TryReadBackgroundPositionToken(ref probe, out var token))
        {
            if (count == tokens.Length) { position = default; return false; }
            tokens[count++] = token;
        }
        // Generic <position> admits one, two, or four components. The three-value
        // variant belongs to background-position and is ambiguous in basic shapes.
        position = default;
        if (count is 0 or 3 || !TryBuildBackgroundPosition(tokens.AsSpan(0, count), out position))
            return false;
        args = probe;
        return true;
    }

    private static bool TryReadClipLength(ref CssTokenReader reader, out CssLength length)
    {
        var probe = reader;
        if (!probe.TryReadLength(out length) ||
            length.Expression is null && length.Unit == CssUnit.None && length.Value != 0)
            return false;
        reader = probe;
        return true;
    }
}
