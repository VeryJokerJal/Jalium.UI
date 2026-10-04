using System.Globalization;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Compares retained foreground and background colors, including symbolic currentColor.</summary>
internal static class CssColorStyleQuery
{
    private readonly record struct ComputedColor(Color Color, string? Expression = null);

    internal static bool IsSupported(string name)
        => name is "color" or "background-color";

    internal static bool Matches(string name, string? text, CssNode container, CssLengthContext lengths)
    {
        var actual = Actual(name, container);
        if (actual is null) return false;
        var initial = Initial(name);
        if (text is null) return !Equal(actual.Value, initial);
        var keyword = CssPropertyMetadata.WideKeyword(text);
        if (keyword is "revert" or "revert-layer") return false;
        if (keyword == "initial" || keyword == "unset" && name == "background-color")
            return Equal(actual.Value, initial);
        if (keyword is "inherit" or "unset")
        {
            var parent = CssMatcher.CssAncestor(container);
            return Equal(actual.Value, parent is null ? initial : Actual(name, parent) ?? initial);
        }

        var reader = new CssTokenReader(text, new CssNumericReadContext(lengths));
        if (!CssColorParser.TryParseContextual(ref reader, out var color,
                out var current, out var deferred) || !reader.AtEnd) return false;
        ComputedColor expected;
        if (current || deferred is not null)
            expected = new(default, current ? "currentcolor" : deferred);
        else expected = new(color);
        return Equal(actual.Value, expected);
    }

    private static ComputedColor Initial(string name)
        => new(name == "color" ? Colors.Black : Color.FromArgb(0, 0, 0, 0));

    private static ComputedColor? Actual(string name, CssNode node)
    {
        if (name == "color")
        {
            var foreground = CssDependencyPropertyLookup.Find(node.Target.GetType(), "Foreground");
            var foregroundLayer = foreground is null ? null : node.Target.GetEffectiveValueLayer(foreground);
            var nativeOverride = foreground is not null &&
                (node.Target.HasLocalOrAnimatedValue(foreground) ||
                 foregroundLayer is not null and not (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState));
            if (!nativeOverride && node.CssRuntimeState?.ContextualColors?.TryGetValue(name, out var contextualColor) == true)
            {
                node.CssRuntimeState.QueryContainer?.ObserveStyleBrush(name, null);
                return new(default, contextualColor.Expression);
            }
            if (!nativeOverride && foregroundLayer is null && CssMatcher.CssAncestor(node) is { } parent &&
                Actual(name, parent) is { Expression: not null } inherited)
            {
                node.CssRuntimeState?.QueryContainer?.ObserveStyleBrush(name, null);
                return inherited;
            }
            var brush = foreground is null ? null : node.GetValue(foreground) as Brush;
            node.CssRuntimeState?.QueryContainer?.ObserveStyleBrush(name, brush);
            return FromBrush(brush);
        }

        var background = CssDependencyPropertyLookup.Find(node.Target.GetType(), "Background");
        if (background is not null &&
            (node.Target.HasLocalOrAnimatedValue(background) ||
             node.Target.GetEffectiveValueLayer(background) is { } layer &&
             layer is not (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState)))
        {
            var brush = node.GetValue(background) as Brush;
            node.CssRuntimeState?.QueryContainer?.ObserveStyleBrush(name, brush);
            return FromBrush(brush, Color.FromArgb(0, 0, 0, 0));
        }
        if (node.CssRuntimeState?.ContextualColors?.TryGetValue(name, out var contextual) == true)
        {
            node.CssRuntimeState.QueryContainer?.ObserveStyleBrush(name, null);
            return new(default, contextual.Expression);
        }
        var paint = CssBackgroundPaintProperties.Get(node.Target)?.Color;
        node.CssRuntimeState?.QueryContainer?.ObserveStyleBrush(name, paint);
        return FromBrush(paint, Color.FromArgb(0, 0, 0, 0));
    }

    private static ComputedColor? FromBrush(Brush? brush, Color? missing = null)
        => brush is SolidColorBrush solid ? new(solid.Color) :
            brush is null ? new(missing ?? Colors.Black) : null;

    private static bool Equal(ComputedColor a, ComputedColor b)
    {
        if (a.Expression is null || b.Expression is null)
            return a.Expression is null && b.Expression is null && a.Color == b.Color;
        return Tokens(a.Expression).SequenceEqual(Tokens(b.Expression), StringComparer.Ordinal);
    }

    private static List<string> Tokens(string expression)
    {
        var reader = new CssTokenReader(CssParser.StripComments(expression));
        var tokens = new List<string>();
        while (!reader.AtEnd)
        {
            var remaining = reader.Remaining;
            if (reader.TryReadString(out var quoted))
            {
                tokens.Add("string:" + quoted.ToString());
                continue;
            }
            if ((char.IsAsciiDigit(remaining[0]) || remaining[0] is '+' or '-' or '.') &&
                reader.TryReadNumber(out var number, out var unit))
            {
                tokens.Add("number:" + number.ToString("R", CultureInfo.InvariantCulture) + ":" + unit);
                continue;
            }
            var position = 0;
            if (CssSyntax.ReadIdentifier(remaining, ref position, out var ident))
            {
                var function = position < remaining.Length && remaining[position] == '(';
                tokens.Add((function ? "function:" : "ident:") + ident.ToString().ToLowerInvariant());
                reader = new CssTokenReader(remaining[(position + (function ? 1 : 0))..]);
                continue;
            }
            tokens.Add("token:" + remaining[0]);
            reader = new CssTokenReader(remaining[1..]);
        }
        return tokens;
    }
}
