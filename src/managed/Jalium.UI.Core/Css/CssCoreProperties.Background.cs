using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    private static void RegisterBackgroundAndBorders()
    {
        RegisterLonghand("background-color", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!CssColorParser.TryParseContextual(ref reader, out var color, out var isCurrentColor, out var deferred) ||
                !reader.AtEnd)
            {
                return null;
            }

            if (deferred is not null) return new CssContextualColorValue("background-color", deferred);
            if (isCurrentColor) return new CssCurrentColorValue("background-color");

            var brush = FreezeIfPossible(new SolidColorBrush(color));
            return new CssSlotActionValue(slots => slots.SetBackgroundColor(brush));
        });

        RegisterLonghand("background-image", (ref CssTokenReader reader, CssCompileContext context) =>
            ParseBackgroundImage(ref reader, context));

        RegisterShorthand("background",
            (ref CssTokenReader reader, CssCompileContext context, List<CssCompiledDeclaration> output) =>
        {
            var images = new List<Brush?>();
            var sizes = new List<CssBackgroundSize>();
            var repeats = new List<CssBackgroundRepeat>();
            var positions = new List<CssBackgroundPosition>();
            var origins = new List<CssBackgroundBox>();
            var clips = new List<CssBackgroundBox>();
            CssCompiledValue? colorValue = null;
            do
            {
                if (!reader.TryReadUntilTopLevelComma(out var segment) || segment.IsEmpty)
                    return false;
                var layer = new CssTokenReader(segment, reader.NumericContext);
                Brush? image = null;
                var size = new CssBackgroundSize(CssBackgroundSizeMode.Auto);
                var repeat = CssBackgroundRepeat.Both;
                var position = CssBackgroundPosition.Initial;
                var origin = CssBackgroundBox.Padding;
                var clip = CssBackgroundBox.Border;
                var hasImage = false;
                var hasSize = false;
                var hasRepeat = false;
                var hasPosition = false;
                var boxCount = 0;
                var sawAnything = false;
                while (!layer.AtEnd)
                {
                    var probe = layer;
                    if (CssColorParser.TryParseContextual(ref probe, out var color, out var isCurrentColor, out var deferred))
                    {
                        if (colorValue is not null) return false;
                        layer = probe;
                        var brush = FreezeIfPossible(new SolidColorBrush(color));
                        colorValue = deferred is not null ? new CssContextualColorValue("background-color", deferred) :
                            isCurrentColor ? new CssCurrentColorValue("background-color") :
                            new CssSlotActionValue(slots => slots.SetBackgroundColor(brush));
                        sawAnything = true;
                        continue;
                    }

                    probe = layer;
                    if (TryReadBackgroundImage(ref probe, context, out var parsedImage))
                    {
                        if (hasImage) return false;
                        layer = probe;
                        image = parsedImage;
                        hasImage = true;
                        sawAnything = true;
                        continue;
                    }

                    if (layer.TryReadSlash())
                    {
                        if (!hasPosition || hasSize || !TryReadBackgroundSize(ref layer, out size)) return false;
                        hasSize = true;
                        sawAnything = true;
                        continue;
                    }

                    probe = layer;
                    if (TryReadBackgroundRepeat(ref probe, out var parsedRepeat))
                    {
                        if (hasRepeat) return false;
                        layer = probe;
                        repeat = parsedRepeat;
                        hasRepeat = true;
                        sawAnything = true;
                        continue;
                    }

                    probe = layer;
                    if (TryReadBackgroundPosition(ref probe, out var parsedPosition))
                    {
                        if (hasPosition || hasSize) return false;
                        layer = probe;
                        position = parsedPosition;
                        hasPosition = true;
                        sawAnything = true;
                        continue;
                    }

                    probe = layer;
                    if (TryReadBackgroundBox(ref probe, out var parsedBox))
                    {
                        if (++boxCount > 2) return false;
                        layer = probe;
                        if (boxCount == 1) origin = clip = parsedBox;
                        else clip = parsedBox;
                        sawAnything = true;
                        continue;
                    }

                    probe = layer;
                    if (probe.TryReadIdent(out var ident))
                    {
                        if (!IsIgnoredBackgroundKeyword(ident)) return false;
                        // Background attachment still has no native mapping.
                        CssDiagnostics.Report(
                            "background", CssDiagnosticReason.LossyConversion, null,
                            $"background attachment '{ident.ToString()}' is ignored");
                        layer = probe;
                        sawAnything = true;
                        continue;
                    }

                    return false;
                }

                if (!sawAnything) return false;
                images.Add(image); // An omitted image is the `none` layer.
                sizes.Add(size);
                repeats.Add(repeat);
                positions.Add(position);
                origins.Add(origin);
                clips.Add(clip);
                if (!reader.TryReadComma()) break;
                if (colorValue is not null || reader.AtEnd) return false;
            } while (true);

            output.Add(new CssCompiledDeclaration("background-image", new CssBackgroundImagesValue(images), false));
            output.Add(new CssCompiledDeclaration("background-size",
                new CssBackgroundSizesValue(sizes.ToArray()), false));
            var repeatList = repeats.ToArray();
            output.Add(new CssCompiledDeclaration("background-repeat",
                new CssSlotActionValue(slots => slots.SetBackgroundRepeats(repeatList)), false));
            output.Add(new CssCompiledDeclaration("background-position",
                new CssBackgroundPositionsValue(positions.ToArray()), false));
            var originList = origins.ToArray();
            output.Add(new CssCompiledDeclaration("background-origin",
                new CssSlotActionValue(slots => slots.SetBackgroundOrigins(originList)), false));
            var clipList = clips.ToArray();
            output.Add(new CssCompiledDeclaration("background-clip",
                new CssSlotActionValue(slots => slots.SetBackgroundClips(clipList)), false));
            output.Add(new CssCompiledDeclaration("background-color", colorValue ??
                new CssSlotActionValue(static slots => slots.SetBackgroundColor(null)), false));
            return true;
        });

        RegisterLonghand("background-size", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            var sizes = new List<CssBackgroundSize>();
            do
            {
                if (!reader.TryReadUntilTopLevelComma(out var segment) || segment.IsEmpty)
                    return null;
                var layer = new CssTokenReader(segment, reader.NumericContext);
                if (!TryReadBackgroundSize(ref layer, out var size) || !layer.AtEnd)
                    return null;
                sizes.Add(size);
                if (!reader.TryReadComma()) break;
                if (reader.AtEnd) return null;
            } while (true);

            return new CssBackgroundSizesValue(sizes.ToArray());
        });

        RegisterLonghand("background-repeat", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            var repeats = new List<CssBackgroundRepeat>();
            do
            {
                if (!reader.TryReadUntilTopLevelComma(out var segment) || segment.IsEmpty)
                    return null;
                var layer = new CssTokenReader(segment, reader.NumericContext);
                if (!TryReadBackgroundRepeat(ref layer, out var repeat) || !layer.AtEnd)
                    return null;
                repeats.Add(repeat);
                if (!reader.TryReadComma()) break;
                if (reader.AtEnd) return null;
            } while (true);

            var repeatList = repeats.ToArray();
            return new CssSlotActionValue(slots => slots.SetBackgroundRepeats(repeatList));
        });

        RegisterLonghand("background-position", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            var positions = new List<CssBackgroundPosition>();
            do
            {
                if (!reader.TryReadUntilTopLevelComma(out var segment) || segment.IsEmpty)
                    return null;
                var layer = new CssTokenReader(segment, reader.NumericContext);
                if (!TryReadBackgroundPosition(ref layer, out var position) || !layer.AtEnd)
                    return null;
                positions.Add(position);
                if (!reader.TryReadComma()) break;
                if (reader.AtEnd) return null;
            } while (true);

            return new CssBackgroundPositionsValue(positions.ToArray());
        });

        RegisterLonghand("background-origin", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            var boxes = new List<CssBackgroundBox>();
            do
            {
                if (!reader.TryReadUntilTopLevelComma(out var segment) || segment.IsEmpty)
                    return null;
                var layer = new CssTokenReader(segment, reader.NumericContext);
                if (!TryReadBackgroundBox(ref layer, out var box) || !layer.AtEnd)
                    return null;
                boxes.Add(box);
                if (!reader.TryReadComma()) break;
                if (reader.AtEnd) return null;
            } while (true);

            var origins = boxes.ToArray();
            return new CssSlotActionValue(slots => slots.SetBackgroundOrigins(origins));
        });

        RegisterLonghand("background-clip", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            var boxes = new List<CssBackgroundBox>();
            do
            {
                if (!reader.TryReadUntilTopLevelComma(out var segment) || segment.IsEmpty)
                    return null;
                var layer = new CssTokenReader(segment, reader.NumericContext);
                if (!TryReadBackgroundBox(ref layer, out var box) || !layer.AtEnd)
                    return null;
                boxes.Add(box);
                if (!reader.TryReadComma()) break;
                if (reader.AtEnd) return null;
            } while (true);

            var clips = boxes.ToArray();
            return new CssSlotActionValue(slots => slots.SetBackgroundClips(clips));
        });

        RegisterLonghand("color", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!CssColorParser.TryParseContextual(ref reader, out var color, out var isCurrentColor, out var deferred) ||
                !reader.AtEnd)
            {
                return null;
            }

            return deferred is not null ? new CssContextualColorValue("color", deferred) :
                isCurrentColor ? new CssContextualColorValue("color", "currentcolor") :
                new CssNamedValue("color", "Foreground", FreezeIfPossible(new SolidColorBrush(color)));
        });

        RegisterBorder();
    }

    private static CssCompiledValue? ParseBackgroundImage(ref CssTokenReader reader, CssCompileContext context)
    {
        var images = new List<Brush?>();
        do
        {
            if (!reader.TryReadUntilTopLevelComma(out var segment) || segment.IsEmpty)
                return null;
            var layer = new CssTokenReader(segment, reader.NumericContext);
            if (!TryReadBackgroundImage(ref layer, context, out var image) || !layer.AtEnd)
                return null;
            images.Add(image);
            if (!reader.TryReadComma()) break;
            if (reader.AtEnd) return null;
        } while (true);

        return new CssBackgroundImagesValue(images);
    }

    private static bool TryReadBackgroundSize(ref CssTokenReader reader, out CssBackgroundSize size)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            if (ident.Equals("cover", StringComparison.OrdinalIgnoreCase))
            {
                size = new(CssBackgroundSizeMode.Cover);
                reader = probe;
                return true;
            }
            if (ident.Equals("contain", StringComparison.OrdinalIgnoreCase))
            {
                size = new(CssBackgroundSizeMode.Contain);
                reader = probe;
                return true;
            }
        }

        probe = reader;
        if (!TryReadBackgroundSizeComponent(ref probe, out var width))
        {
            size = default;
            return false;
        }

        var second = probe;
        var hasHeight = TryReadBackgroundSizeComponent(ref second, out var height);
        if (hasHeight) probe = second;

        size = width is null && height is null
            ? new(CssBackgroundSizeMode.Auto)
            : width is { Unit: CssUnit.Percent, Value: 100 } &&
              height is { Unit: CssUnit.Percent, Value: 100 }
                ? new(CssBackgroundSizeMode.Fill)
                : new(width, height);
        reader = probe;
        return true;
    }

    private static bool TryReadBackgroundSizeComponent(ref CssTokenReader reader, out CssLength? component)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident) && ident.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            component = null;
            reader = probe;
            return true;
        }

        probe = reader;
        if (probe.TryReadLength(out var length) &&
            (length.Expression is not null || length.Value >= 0) &&
            (length.Unit != CssUnit.None || length.Value == 0))
        {
            component = length;
            reader = probe;
            return true;
        }

        component = default;
        return false;
    }

    private enum BackgroundPositionTokenKind { Length, Left, Right, Top, Bottom, Center }

    private readonly record struct BackgroundPositionToken(BackgroundPositionTokenKind Kind, CssLength Length = default)
    {
        internal bool Horizontal => Kind is BackgroundPositionTokenKind.Left or BackgroundPositionTokenKind.Right;
        internal bool Vertical => Kind is BackgroundPositionTokenKind.Top or BackgroundPositionTokenKind.Bottom;
        internal bool Edge => Horizontal || Vertical;
    }

    private readonly record struct BackgroundPositionGroup(BackgroundPositionToken Token, CssLength? Offset);

    private static bool TryReadBackgroundPosition(ref CssTokenReader reader,
        out CssBackgroundPosition position, int maxTokens = 4)
    {
        position = default;
        var probe = reader;
        var tokens = new BackgroundPositionToken[4];
        var count = 0;
        while (TryReadBackgroundPositionToken(ref probe, out var token))
        {
            if (count == maxTokens)
            {
                position = default;
                return false;
            }
            tokens[count++] = token;
        }

        if (count == 0 || !TryBuildBackgroundPosition(tokens.AsSpan(0, count), out position))
            return false;
        reader = probe;
        return true;
    }

    private static bool TryReadBackgroundPositionToken(ref CssTokenReader reader, out BackgroundPositionToken token)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            var kind = ident.Equals("left", StringComparison.OrdinalIgnoreCase) ? BackgroundPositionTokenKind.Left
                : ident.Equals("right", StringComparison.OrdinalIgnoreCase) ? BackgroundPositionTokenKind.Right
                : ident.Equals("top", StringComparison.OrdinalIgnoreCase) ? BackgroundPositionTokenKind.Top
                : ident.Equals("bottom", StringComparison.OrdinalIgnoreCase) ? BackgroundPositionTokenKind.Bottom
                : ident.Equals("center", StringComparison.OrdinalIgnoreCase) ? BackgroundPositionTokenKind.Center
                : (BackgroundPositionTokenKind)(-1);
            if ((int)kind >= 0)
            {
                token = new(kind);
                reader = probe;
                return true;
            }
        }

        probe = reader;
        if (probe.TryReadLength(out var length) && (length.Unit != CssUnit.None || length.Value == 0))
        {
            token = new(BackgroundPositionTokenKind.Length, length);
            reader = probe;
            return true;
        }
        token = default;
        return false;
    }

    private static CssBackgroundPositionAxis PositionAxis(BackgroundPositionToken token, CssLength? offset = null)
    {
        if (offset is { } length) return new(length,
            token.Kind is BackgroundPositionTokenKind.Right or BackgroundPositionTokenKind.Bottom);
        return token.Kind switch
        {
            BackgroundPositionTokenKind.Right or BackgroundPositionTokenKind.Bottom =>
                new(new CssLength(0, CssUnit.Percent), true),
            BackgroundPositionTokenKind.Center => new(new CssLength(50, CssUnit.Percent)),
            BackgroundPositionTokenKind.Length => new(token.Length),
            _ => new(new CssLength(0, CssUnit.Percent)),
        };
    }

    private static bool TryBuildBackgroundPosition(ReadOnlySpan<BackgroundPositionToken> tokens,
        out CssBackgroundPosition position)
    {
        position = default;
        var center = PositionAxis(new(BackgroundPositionTokenKind.Center));
        CssBackgroundPositionAxis x, y;
        if (tokens.Length == 1)
        {
            x = tokens[0].Vertical ? center : PositionAxis(tokens[0]);
            y = tokens[0].Vertical ? PositionAxis(tokens[0]) : center;
        }
        else if (tokens.Length == 2)
        {
            var first = tokens[0];
            var second = tokens[1];
            if (first.Vertical && (second.Horizontal || second.Kind == BackgroundPositionTokenKind.Center))
            {
                x = PositionAxis(second);
                y = PositionAxis(first);
            }
            else if (first.Horizontal && (second.Vertical || second.Kind == BackgroundPositionTokenKind.Center))
            {
                x = PositionAxis(first);
                y = PositionAxis(second);
            }
            else if (first.Kind == BackgroundPositionTokenKind.Center && second.Edge)
            {
                x = second.Horizontal ? PositionAxis(second) : center;
                y = second.Vertical ? PositionAxis(second) : center;
            }
            else if (!first.Vertical && !second.Horizontal)
            {
                x = PositionAxis(first);
                y = PositionAxis(second);
            }
            else return false;
        }
        else
        {
            var groups = new BackgroundPositionGroup[2];
            var groupCount = 0;
            for (var i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (groupCount == groups.Length || token.Kind == BackgroundPositionTokenKind.Length)
                    return false;
                CssLength? offset = null;
                if (i + 1 < tokens.Length && tokens[i + 1].Kind == BackgroundPositionTokenKind.Length)
                {
                    if (!token.Edge) return false;
                    offset = tokens[++i].Length;
                }
                groups[groupCount++] = new(token, offset);
            }
            if (groupCount != 2) return false;
            var first = groups[0];
            var second = groups[1];
            if (first.Token.Horizontal &&
                (second.Token.Vertical || second.Token.Kind == BackgroundPositionTokenKind.Center))
            {
                x = PositionAxis(first.Token, first.Offset);
                y = PositionAxis(second.Token, second.Offset);
            }
            else if (first.Token.Vertical &&
                     (second.Token.Horizontal || second.Token.Kind == BackgroundPositionTokenKind.Center))
            {
                x = PositionAxis(second.Token, second.Offset);
                y = PositionAxis(first.Token, first.Offset);
            }
            else if (first.Token.Kind == BackgroundPositionTokenKind.Center && second.Token.Horizontal)
            {
                x = PositionAxis(second.Token, second.Offset);
                y = center;
            }
            else if (first.Token.Kind == BackgroundPositionTokenKind.Center && second.Token.Vertical)
            {
                x = center;
                y = PositionAxis(second.Token, second.Offset);
            }
            else return false;
        }

        position = new(x, y, CssLengthContext.Default);
        return true;
    }

    private static bool TryReadBackgroundRepeat(ref CssTokenReader reader, out CssBackgroundRepeat repeat)
    {
        var probe = reader;
        if (!probe.TryReadIdent(out var first))
        {
            repeat = default;
            return false;
        }

        if (first.Equals("repeat-x", StringComparison.OrdinalIgnoreCase))
            repeat = new(CssBackgroundRepeatMode.Repeat, CssBackgroundRepeatMode.NoRepeat);
        else if (first.Equals("repeat-y", StringComparison.OrdinalIgnoreCase))
            repeat = new(CssBackgroundRepeatMode.NoRepeat, CssBackgroundRepeatMode.Repeat);
        else if (TryReadBackgroundRepeatMode(first, out var mode))
            repeat = new(mode, mode);
        else
        {
            repeat = default;
            return false;
        }

        if (!first.Equals("repeat-x", StringComparison.OrdinalIgnoreCase) &&
            !first.Equals("repeat-y", StringComparison.OrdinalIgnoreCase))
        {
            var secondProbe = probe;
            if (secondProbe.TryReadIdent(out var second) &&
                TryReadBackgroundRepeatMode(second, out var secondMode))
            {
                repeat = new(repeat.X, secondMode);
                probe = secondProbe;
            }
        }

        reader = probe;
        return true;
    }

    private static bool TryReadBackgroundRepeatMode(ReadOnlySpan<char> ident,
        out CssBackgroundRepeatMode mode)
    {
        if (ident.Equals("repeat", StringComparison.OrdinalIgnoreCase))
            mode = CssBackgroundRepeatMode.Repeat;
        else if (ident.Equals("no-repeat", StringComparison.OrdinalIgnoreCase))
            mode = CssBackgroundRepeatMode.NoRepeat;
        else if (ident.Equals("space", StringComparison.OrdinalIgnoreCase))
            mode = CssBackgroundRepeatMode.Space;
        else if (ident.Equals("round", StringComparison.OrdinalIgnoreCase))
            mode = CssBackgroundRepeatMode.Round;
        else
        {
            mode = default;
            return false;
        }
        return true;
    }

    private static bool TryReadBackgroundBox(ref CssTokenReader reader, out CssBackgroundBox box)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            if (ident.Equals("border-box", StringComparison.OrdinalIgnoreCase))
                box = CssBackgroundBox.Border;
            else if (ident.Equals("padding-box", StringComparison.OrdinalIgnoreCase))
                box = CssBackgroundBox.Padding;
            else if (ident.Equals("content-box", StringComparison.OrdinalIgnoreCase))
                box = CssBackgroundBox.Content;
            else
            {
                box = default;
                return false;
            }
            reader = probe;
            return true;
        }
        box = default;
        return false;
    }

    private static bool IsIgnoredBackgroundKeyword(ReadOnlySpan<char> ident)
        => ident.Equals("scroll", StringComparison.OrdinalIgnoreCase) ||
           ident.Equals("fixed", StringComparison.OrdinalIgnoreCase) ||
           ident.Equals("local", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadBackgroundImage(ref CssTokenReader reader, CssCompileContext context, out Brush? brush)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident) && ident.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            reader = probe;
            brush = null;
            return true;
        }

        probe = reader;
        if (probe.TryReadFunction(out var fn, out var args))
        {
            if (CssGradientParser.TryParseGradientFunction(fn, ref args, out brush))
            {
                reader = probe;
                return true;
            }
            if (fn.Equals("url", StringComparison.OrdinalIgnoreCase) &&
                TryCreateImageBrush(ref args, context, out brush))
            {
                reader = probe;
                return true;
            }
        }

        brush = null;
        return false;
    }

    private static bool TryCreateImageBrush(ref CssTokenReader args, CssCompileContext context, out Brush? brush)
    {
        brush = null;
        string url;
        if (args.TryReadString(out var quoted))
        {
            if (!args.AtEnd) return false;
            url = quoted;
        }
        else
        {
            url = args.Remaining.ToString().Trim();
        }

        if (url.Length == 0)
        {
            return false;
        }

        try
        {
            var uri = context.BaseUri is not null
                ? new Uri(context.BaseUri, url)
                : new Uri(url, UriKind.RelativeOrAbsolute);
            var source = new BitmapImage(uri);
            brush = new ImageBrush(source) { Stretch = Stretch.Fill };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RegisterBorder()
    {
        RegisterShorthand("border-width",
            (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
        {
            CssLengthBuffer buffer = default;
            Span<CssLength> parts = buffer;
            var count = 0;
            while (!reader.AtEnd)
            {
                if (count == 4 || !TryReadBorderWidth(ref reader, out parts[count]))
                {
                    return false;
                }

                count++;
            }

            if (count == 0)
            {
                return false;
            }

            var (top, right, bottom, left) = count switch
            {
                1 => (parts[0], parts[0], parts[0], parts[0]),
                2 => (parts[0], parts[1], parts[0], parts[1]),
                3 => (parts[0], parts[1], parts[2], parts[1]),
                _ => (parts[0], parts[1], parts[2], parts[3]),
            };

            output.Add(new CssCompiledDeclaration("border-top-width", new CssSlotThicknessEdge(CssSlot.BorderWidth, 1, top), false));
            output.Add(new CssCompiledDeclaration("border-right-width", new CssSlotThicknessEdge(CssSlot.BorderWidth, 2, right), false));
            output.Add(new CssCompiledDeclaration("border-bottom-width", new CssSlotThicknessEdge(CssSlot.BorderWidth, 3, bottom), false));
            output.Add(new CssCompiledDeclaration("border-left-width", new CssSlotThicknessEdge(CssSlot.BorderWidth, 0, left), false));
            return true;
        });

        RegisterBorderWidthEdge("border-left-width", 0);
        RegisterBorderWidthEdge("border-top-width", 1);
        RegisterBorderWidthEdge("border-right-width", 2);
        RegisterBorderWidthEdge("border-bottom-width", 3);
        RegisterLogicalBorderWidths();

        RegisterShorthand("border-color",
            (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
        {
            var parts = new CssBorderColorPart[4];
            var count = 0;
            while (!reader.AtEnd)
            {
                if (count == 4 || !TryReadBorderColor(ref reader, out parts[count])) return false;
                count++;
            }
            if (count == 0) return false;
            var top = parts[0];
            var right = count >= 2 ? parts[1] : top;
            var bottom = count >= 3 ? parts[2] : top;
            var left = count == 4 ? parts[3] : right;
            AddBorderColors(output, top, right, bottom, left);
            return true;
        });

        foreach (var name in new[] { "border-top-color", "border-right-color", "border-bottom-color", "border-left-color" })
            RegisterLonghand(name, (ref CssTokenReader reader, CssCompileContext ctx_) =>
                TryReadBorderColor(ref reader, out var part) && reader.AtEnd ? MakeBorderColorValue(name, part) : null);

        RegisterLogicalBorderColors();

        RegisterShorthand("border-style",
            (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
        {
            var parts = new CssBorderLineStyle[4];
            var count = 0;
            while (!reader.AtEnd)
            {
                if (count == 4 || !TryReadBorderStyle(ref reader, out parts[count])) return false;
                count++;
            }
            if (count == 0) return false;
            var top = parts[0];
            var right = count >= 2 ? parts[1] : top;
            var bottom = count >= 3 ? parts[2] : top;
            var left = count == 4 ? parts[3] : right;
            AddBorderStyles(output, top, right, bottom, left);
            return true;
        });

        foreach (var name in new[] { "border-top-style", "border-right-style", "border-bottom-style", "border-left-style" })
            RegisterLonghand(name, (ref CssTokenReader reader, CssCompileContext ctx_) =>
                TryReadBorderStyle(ref reader, out var style) && reader.AtEnd
                    ? new CssBorderStyleEdge(name, style) : null);

        RegisterLogicalBorderStyles();

        RegisterShorthand("border",
            (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
        {
            if (!TryReadBorderComponents(ref reader, out var parts)) return false;
            foreach (var side in new[] { "top", "right", "bottom", "left" })
                AddBorderSide(output, side, parts);
            return true;
        });

        foreach (var side in new[] { "top", "right", "bottom", "left",
                     "inline-start", "inline-end", "block-start", "block-end" })
        {
            RegisterShorthand($"border-{side}",
                (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
                {
                    if (!TryReadBorderComponents(ref reader, out var parts)) return false;
                    AddBorderSide(output, side, parts);
                    return true;
                });
        }

        foreach (var axis in new[] { "inline", "block" })
        {
            RegisterShorthand($"border-{axis}",
                (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
                {
                    if (!TryReadBorderComponents(ref reader, out var parts)) return false;
                    AddBorderSide(output, $"{axis}-start", parts);
                    AddBorderSide(output, $"{axis}-end", parts);
                    return true;
                });
        }

        RegisterBorderRadius();
    }

    private readonly record struct CssBorderColorPart(Color Color, bool CurrentColor, string? Deferred);

    private readonly record struct CssBorderComponents(
        CssLength Width, CssBorderColorPart Color, CssBorderLineStyle Style);

    private static bool TryReadBorderComponents(ref CssTokenReader reader, out CssBorderComponents parts)
    {
        CssLength? width = null;
        CssBorderColorPart? color = null;
        CssBorderLineStyle? style = null;
        var sawAnything = false;
        while (!reader.AtEnd)
        {
            var probe = reader;
            if (TryReadBorderWidth(ref probe, out var w))
            {
                if (width is not null) { parts = default; return false; }
                reader = probe;
                width = w;
            }
            else
            {
                probe = reader;
                if (TryReadBorderStyle(ref probe, out var s))
                {
                    if (style is not null) { parts = default; return false; }
                    reader = probe;
                    style = s;
                }
                else
                {
                    probe = reader;
                    if (!TryReadBorderColor(ref probe, out var c) || color is not null)
                    {
                        parts = default;
                        return false;
                    }
                    reader = probe;
                    color = c;
                }
            }
            sawAnything = true;
        }
        parts = new(width ?? new CssLength(3, CssUnit.Px),
            color ?? new CssBorderColorPart(default, true, null),
            style ?? CssBorderLineStyle.None);
        return sawAnything;
    }

    private static void AddBorderSide(List<CssCompiledDeclaration> output,
        string side, CssBorderComponents parts)
    {
        var width = $"border-{side}-width";
        var color = $"border-{side}-color";
        var style = $"border-{side}-style";
        output.Add(new(width, new CssLogicalThicknessEdge(CssSlot.BorderWidth, width, parts.Width), false));
        output.Add(new(color, MakeBorderColorValue(color, parts.Color), false));
        output.Add(new(style, new CssBorderStyleEdge(style, parts.Style), false));
    }

    private static bool TryReadBorderColor(ref CssTokenReader reader, out CssBorderColorPart part)
    {
        if (CssColorParser.TryParseContextual(ref reader, out var color, out var currentColor, out var deferred))
        {
            part = new(color, currentColor, deferred);
            return true;
        }
        part = default;
        return false;
    }

    private static CssCompiledValue MakeBorderColorValue(string name, CssBorderColorPart part)
        => part.Deferred is not null ? new CssContextualColorValue(name, part.Deferred) :
            part.CurrentColor ? new CssCurrentColorValue(name) :
            new CssBorderColorEdge(name, (Brush)FreezeIfPossible(new SolidColorBrush(part.Color)));

    private static void AddBorderColors(List<CssCompiledDeclaration> output,
        CssBorderColorPart top, CssBorderColorPart right, CssBorderColorPart bottom, CssBorderColorPart left)
    {
        output.Add(new("border-top-color", MakeBorderColorValue("border-top-color", top), false));
        output.Add(new("border-right-color", MakeBorderColorValue("border-right-color", right), false));
        output.Add(new("border-bottom-color", MakeBorderColorValue("border-bottom-color", bottom), false));
        output.Add(new("border-left-color", MakeBorderColorValue("border-left-color", left), false));
    }

    private static void RegisterLogicalBorderColors()
    {
        foreach (var axis in new[] { "inline", "block" })
        {
            var startName = $"border-{axis}-start-color";
            var endName = $"border-{axis}-end-color";
            RegisterShorthand($"border-{axis}-color",
                (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
                {
                    if (!TryReadBorderColor(ref reader, out var start)) return false;
                    var end = start;
                    if (!reader.AtEnd && !TryReadBorderColor(ref reader, out end)) return false;
                    if (!reader.AtEnd) return false;
                    output.Add(new(startName, MakeBorderColorValue(startName, start), false));
                    output.Add(new(endName, MakeBorderColorValue(endName, end), false));
                    return true;
                });
            foreach (var name in new[] { startName, endName })
                RegisterLonghand(name, (ref CssTokenReader reader, CssCompileContext ctx_) =>
                    TryReadBorderColor(ref reader, out var part) && reader.AtEnd ? MakeBorderColorValue(name, part) : null);
        }
    }

    private static bool TryReadBorderStyle(ref CssTokenReader reader, out CssBorderLineStyle style)
    {
        var probe = reader;
        if (!probe.TryReadIdent(out var ident)) { style = default; return false; }
        style = ident.ToString().ToLowerInvariant() switch
        {
            "none" => CssBorderLineStyle.None,
            "hidden" => CssBorderLineStyle.Hidden,
            "solid" => CssBorderLineStyle.Solid,
            "dotted" => CssBorderLineStyle.Dotted,
            "dashed" => CssBorderLineStyle.Dashed,
            "double" => CssBorderLineStyle.Double,
            "groove" => CssBorderLineStyle.Groove,
            "ridge" => CssBorderLineStyle.Ridge,
            "inset" => CssBorderLineStyle.Inset,
            "outset" => CssBorderLineStyle.Outset,
            _ => (CssBorderLineStyle)byte.MaxValue,
        };
        if (style == (CssBorderLineStyle)byte.MaxValue) return false;
        reader = probe;
        return true;
    }

    private static void AddBorderStyles(List<CssCompiledDeclaration> output,
        CssBorderLineStyle top, CssBorderLineStyle right, CssBorderLineStyle bottom, CssBorderLineStyle left)
    {
        output.Add(new("border-top-style", new CssBorderStyleEdge("border-top-style", top), false));
        output.Add(new("border-right-style", new CssBorderStyleEdge("border-right-style", right), false));
        output.Add(new("border-bottom-style", new CssBorderStyleEdge("border-bottom-style", bottom), false));
        output.Add(new("border-left-style", new CssBorderStyleEdge("border-left-style", left), false));
    }

    private static void RegisterLogicalBorderStyles()
    {
        foreach (var axis in new[] { "inline", "block" })
        {
            var startName = $"border-{axis}-start-style";
            var endName = $"border-{axis}-end-style";
            RegisterShorthand($"border-{axis}-style",
                (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
                {
                    if (!TryReadBorderStyle(ref reader, out var start)) return false;
                    var end = start;
                    if (!reader.AtEnd && !TryReadBorderStyle(ref reader, out end)) return false;
                    if (!reader.AtEnd) return false;
                    output.Add(new(startName, new CssBorderStyleEdge(startName, start), false));
                    output.Add(new(endName, new CssBorderStyleEdge(endName, end), false));
                    return true;
                });
            foreach (var name in new[] { startName, endName })
                RegisterLonghand(name, (ref CssTokenReader reader, CssCompileContext ctx_) =>
                    TryReadBorderStyle(ref reader, out var style) && reader.AtEnd
                        ? new CssBorderStyleEdge(name, style) : null);
        }
    }

    private static void RegisterBorderWidthEdge(string name, int edge)
        => RegisterLonghand(name, (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!TryReadBorderWidth(ref reader, out var length) || !reader.AtEnd)
            {
                return null;
            }

            return new CssSlotThicknessEdge(CssSlot.BorderWidth, edge, length);
        });

    private static void RegisterLogicalBorderWidths()
    {
        foreach (var axis in new[] { "inline", "block" })
        {
            var shorthand = $"border-{axis}-width";
            var startName = $"border-{axis}-start-width";
            var endName = $"border-{axis}-end-width";
            RegisterShorthand(shorthand,
                (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
                {
                    if (!TryReadBorderWidth(ref reader, out var start)) return false;
                    var end = start;
                    if (!reader.AtEnd && !TryReadBorderWidth(ref reader, out end)) return false;
                    if (!reader.AtEnd) return false;
                    output.Add(new(startName, new CssLogicalThicknessEdge(CssSlot.BorderWidth, startName, start), false));
                    output.Add(new(endName, new CssLogicalThicknessEdge(CssSlot.BorderWidth, endName, end), false));
                    return true;
                });
            foreach (var name in new[] { startName, endName })
                RegisterLonghand(name, (ref CssTokenReader reader, CssCompileContext ctx_) =>
                    TryReadBorderWidth(ref reader, out var length) && reader.AtEnd
                        ? new CssLogicalThicknessEdge(CssSlot.BorderWidth, name, length) : null);
        }
    }

    private static bool TryReadBorderWidth(ref CssTokenReader reader, out CssLength length)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            double px;
            if (ident.Equals("thin", StringComparison.OrdinalIgnoreCase)) { px = 1; }
            else if (ident.Equals("medium", StringComparison.OrdinalIgnoreCase)) { px = 3; }
            else if (ident.Equals("thick", StringComparison.OrdinalIgnoreCase)) { px = 5; }
            else
            {
                length = default;
                return false;
            }

            length = new CssLength(px, CssUnit.Px);
            reader = probe;
            return true;
        }

        probe = reader;
        if (!probe.TryReadLength(out length) || length.UsesPercent ||
            length.Expression is null && (length.Value < 0 || length.Unit == CssUnit.None && length.Value != 0))
            return false;
        reader = probe;
        return true;
    }

    private static void RegisterBorderRadius()
    {
        RegisterShorthand("border-radius",
            (ref CssTokenReader reader, CssCompileContext ctx_, List<CssCompiledDeclaration> output) =>
        {
            CssLengthBuffer horizontal = default, vertical = default;
            if (!ReadCornerAxis(ref reader, horizontal, out var xCount)) return false;
            var yCount = xCount;
            if (reader.TryReadSlash())
            {
                if (!ReadCornerAxis(ref reader, vertical, out yCount)) return false;
            }
            else vertical = horizontal;
            if (!reader.AtEnd) return false;

            string[] names = ["border-top-left-radius", "border-top-right-radius", "border-bottom-right-radius", "border-bottom-left-radius"];
            for (var corner = 0; corner < 4; corner++)
                output.Add(new(names[corner], new CssSlotCorner(corner,
                    CornerAxisValue(horizontal, xCount, corner), CornerAxisValue(vertical, yCount, corner)), false));
            return true;
        });

        RegisterCornerLonghand("border-top-left-radius", 0);
        RegisterCornerLonghand("border-top-right-radius", 1);
        RegisterCornerLonghand("border-bottom-right-radius", 2);
        RegisterCornerLonghand("border-bottom-left-radius", 3);
        foreach (var name in new[]
        {
            "border-start-start-radius", "border-start-end-radius",
            "border-end-start-radius", "border-end-end-radius",
        })
            RegisterLogicalCornerLonghand(name);
    }

    private static void RegisterLogicalCornerLonghand(string name)
        => RegisterLonghand(name, (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!ReadCornerLength(ref reader, out var x)) return null;
            var y = x;
            if (!reader.AtEnd && !ReadCornerLength(ref reader, out y)) return null;
            return reader.AtEnd ? new CssSlotCorner(name, x, y) : null;
        });

    private static void RegisterCornerLonghand(string name, int corner)
        => RegisterLonghand(name, (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!ReadCornerLength(ref reader, out var x)) return null;
            var y = x;
            if (!reader.AtEnd && !ReadCornerLength(ref reader, out y)) return null;
            return reader.AtEnd ? new CssSlotCorner(corner, x, y) : null;
        });

    private static bool ReadCornerLength(ref CssTokenReader reader, out CssLength length)
        => reader.TryReadLength(out length) && (length.Expression is not null ||
            double.IsFinite(length.Value) && length.Value >= 0 && (length.Unit != CssUnit.None || length.Value == 0));

    private static bool ReadCornerAxis(ref CssTokenReader reader, scoped Span<CssLength> parts, out int count)
    {
        count = 0;
        while (count < 4)
        {
            var probe = reader;
            if (!ReadCornerLength(ref probe, out var value)) break;
            parts[count++] = value;
            reader = probe;
        }
        return count > 0;
    }

    private static CssLength CornerAxisValue(ReadOnlySpan<CssLength> parts, int count, int corner)
        => parts[corner < count ? corner : corner == 3 && count > 1 ? 1 : 0];

    private sealed class CssSlotCorner : CssCompiledValue
    {
        private readonly int _corner;
        private readonly string? _logicalName;
        private readonly CssLength _x, _y;

        public CssSlotCorner(int corner, CssLength x, CssLength y)
        {
            _corner = corner;
            _x = x;
            _y = y;
        }

        public CssSlotCorner(string logicalName, CssLength x, CssLength y)
            : this(-1, x, y) => _logicalName = logicalName;

        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            var corner = _logicalName is null ? _corner :
                CssLogicalBoxEdges.Corner(_logicalName, context.Slots.LogicalRightToLeft);
            context.Slots.SetCorner(corner, CssComputedCorner.Create(_x, _y, context.Lengths));
            return true;
        }
    }
}
