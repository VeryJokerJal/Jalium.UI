using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    /// <summary>Maps CSS property names appearing in transition-property to framework DP names.</summary>
    private static readonly Dictionary<string, string> s_transitionTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["background-color"] = "Background",
        ["background"] = "Background",
        ["color"] = "Foreground",
        ["opacity"] = "Opacity",
        ["width"] = "Width",
        ["height"] = "Height",
        ["min-width"] = "MinWidth",
        ["min-height"] = "MinHeight",
        ["max-width"] = "MaxWidth",
        ["max-height"] = "MaxHeight",
        ["margin"] = "Margin",
        ["padding"] = "Padding",
        ["border-color"] = "BorderBrush",
        ["border-width"] = "BorderThickness",
        ["border-radius"] = "CornerRadius",
        ["transform"] = "RenderTransform",
        ["box-shadow"] = "Effect",
        ["filter"] = "Effect",
        ["font-size"] = "FontSize",
        ["visibility"] = "Visibility",
        ["outline-color"] = "OutlineBrush",
        ["outline-width"] = "OutlineThickness",
        ["outline-offset"] = "OutlineOffset",
        ["outline"] = "OutlineBrush",
    };

    internal static bool TryGetTransitionTargetName(string cssName, out string dpName)
    {
        if (cssName.Equals("unicode-bidi", StringComparison.OrdinalIgnoreCase) || cssName.Equals("direction", StringComparison.OrdinalIgnoreCase)) { dpName = ""; return false; }
        var descriptor = CssPropertyRegistry.LookupForCompile(cssName.ToLowerInvariant(), out var canonical);
        if (descriptor?.TransitionTargetDpName is { } registeredTarget)
        {
            dpName = registeredTarget;
            return true;
        }
        if (s_transitionTargets.TryGetValue(canonical, out dpName!))
        {
            return true;
        }

        return CssKebabCase.TryToPascal(canonical, out dpName!);
    }

    private static void RegisterTransformAndTransition()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "transform-box",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = CssTransformReferenceBox.ValueProperty,
            TransitionTargetDpName = CssTransformReferenceBox.ValueProperty.Name,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                var box = ident.ToString().ToLowerInvariant() switch
                {
                    "content-box" => CssTransformBox.ContentBox,
                    "border-box" => CssTransformBox.BorderBox,
                    "fill-box" => CssTransformBox.FillBox,
                    "stroke-box" => CssTransformBox.StrokeBox,
                    "view-box" => CssTransformBox.ViewBox,
                    _ => (CssTransformBox?)null,
                };
                return box is { } value
                    ? new CssImmediateValue(CssTransformReferenceBox.ValueProperty, value) : null;
            },
        });

        RegisterLonghand("transform", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            var text = reader.Remaining.ToString();
            if (!CssTransformParser.TryParseTransformList(ref reader, out var transform))
            {
                return null;
            }

            var relativeLengths = CssTransformParser.RelativeLengths(text);
            // Compiled inline declarations are cached across dispatchers.
            // Absolute transforms must be immutable just like contextual ones.
            if (relativeLengths.Length == 0 && transform?.CanFreeze == true)
                transform.Freeze();
            return relativeLengths.Length > 0 ? new CssContextTransformValue(text, relativeLengths)
                : new CssImmediateValue(UIElement.RenderTransformProperty, transform);
        });

        RegisterLonghand("transform-origin", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!TryReadTransformOrigin(ref reader, out var position, out var zOffset) || !reader.AtEnd)
            {
                return null;
            }

            return new CssTransformOriginValue(position, zOffset);
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "perspective",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = CssTransform3D.PerspectiveProperty,
            TransitionTargetDpName = CssTransform3D.PerspectiveProperty.Name,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                var probe = reader;
                if (probe.TryReadIdent(out var ident))
                {
                    if (ident.Equals("none", StringComparison.OrdinalIgnoreCase) && probe.AtEnd)
                    {
                        reader = probe;
                        return new CssImmediateValue(CssTransform3D.PerspectiveProperty, double.NaN);
                    }
                    return null;
                }

                if (!reader.TryReadLength(out var length) || !reader.AtEnd ||
                    length.UsesPercent || !length.IsLengthUnit ||
                    (length.Unit == CssUnit.None && length.Value != 0))
                {
                    return null;
                }

                if (length.IsAbsolute)
                {
                    var px = length.ToPxAbsolute();
                    if (!double.IsFinite(px) || px < 0) return null;
                    return new CssImmediateValue(CssTransform3D.PerspectiveProperty, px);
                }

                return new CssPerspectiveValue(length);
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "perspective-origin",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = CssTransform3D.PerspectiveOriginProperty,
            TransitionTargetDpName = CssTransform3D.PerspectiveOriginProperty.Name,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!TryReadBackgroundPosition(ref reader, out var position, maxTokens: 4) || !reader.AtEnd)
                {
                    return null;
                }

                return new CssPerspectiveOriginValue(position);
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "transform-style",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = CssTransform3D.TransformStyleProperty,
            TransitionTargetDpName = CssTransform3D.TransformStyleProperty.Name,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                var style = ident.ToString().ToLowerInvariant() switch
                {
                    "flat" => CssTransformStyle.Flat,
                    "preserve-3d" => CssTransformStyle.Preserve3D,
                    _ => (CssTransformStyle?)null,
                };
                return style is { } value
                    ? new CssImmediateValue(CssTransform3D.TransformStyleProperty, value) : null;
            },
        });

        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "backface-visibility",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = CssTransform3D.BackfaceVisibilityProperty,
            TransitionTargetDpName = CssTransform3D.BackfaceVisibilityProperty.Name,
            Parse = (ref CssTokenReader reader, CssCompileContext ctx_) =>
            {
                if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
                var visibility = ident.ToString().ToLowerInvariant() switch
                {
                    "visible" => CssBackfaceVisibility.Visible,
                    "hidden" => CssBackfaceVisibility.Hidden,
                    _ => (CssBackfaceVisibility?)null,
                };
                return visibility is { } value
                    ? new CssImmediateValue(CssTransform3D.BackfaceVisibilityProperty, value) : null;
            },
        });
    }

    private static bool TryReadTransformOrigin(
        ref CssTokenReader reader,
        out CssBackgroundPosition position,
        out CssLength? zOffset)
    {
        position = default;
        zOffset = null;
        var probe = reader;
        var tokens = new BackgroundPositionToken[3];
        var count = 0;
        while (TryReadBackgroundPositionToken(ref probe, out var token))
        {
            if (count == 3)
                return false;
            tokens[count++] = token;
        }

        if (count is 1 or 2)
        {
            if (!TryBuildBackgroundPosition(tokens.AsSpan(0, count), out position))
                return false;
            reader = probe;
            return true;
        }

        if (count == 3)
        {
            var third = tokens[2];
            if (third.Kind != BackgroundPositionTokenKind.Length ||
                third.Length.UsesPercent ||
                !third.Length.IsLengthUnit ||
                (third.Length.Unit == CssUnit.None && third.Length.Value != 0))
            {
                return false;
            }

            if (!TryBuildBackgroundPosition(tokens.AsSpan(0, 2), out position))
                return false;

            zOffset = third.Length;
            reader = probe;
            return true;
        }

        return false;
    }
}
