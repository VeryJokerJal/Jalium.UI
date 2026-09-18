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
        RegisterLonghand("transform", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            var text = reader.Remaining.ToString();
            if (!CssTransformParser.TryParseTransformList(ref reader, out var transform))
            {
                return null;
            }

            var relativeLengths = CssTransformParser.RelativeLengths(text);
            return relativeLengths.Length > 0 ? new CssContextTransformValue(text, relativeLengths)
                : new CssImmediateValue(UIElement.RenderTransformProperty, transform);
        });

        RegisterLonghand("transform-origin", (ref CssTokenReader reader, CssCompileContext ctx_) =>
        {
            if (!CssTransformParser.TryParseTransformOrigin(ref reader, out var origin) || !reader.AtEnd)
            {
                return null;
            }

            return new CssImmediateValue(UIElement.RenderTransformOriginProperty, origin);
        });

    }
}
