using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssUnicodeBidi { Normal, Embed, Isolate, BidiOverride, IsolateOverride, Plaintext }

internal static class CssUnicodeBidiProperties
{
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssUnicodeBidi", typeof(CssUnicodeBidi), typeof(CssUnicodeBidiProperties),
        new PropertyMetadata(CssUnicodeBidi.Normal, Changed));
    internal static CssUnicodeBidi Value(DependencyObject owner) => (CssUnicodeBidi)owner.GetValue(ValueProperty)!;
    internal static CssCompiledValue? Parse(ref CssTokenReader reader)
    {
        if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
        var value = ident.ToString().ToLowerInvariant() switch
        {
            "normal" => CssUnicodeBidi.Normal, "embed" => CssUnicodeBidi.Embed,
            "isolate" => CssUnicodeBidi.Isolate, "bidi-override" => CssUnicodeBidi.BidiOverride,
            "isolate-override" => CssUnicodeBidi.IsolateOverride, "plaintext" => CssUnicodeBidi.Plaintext,
            _ => (CssUnicodeBidi?)null,
        };
        return value is { } mode ? new CssImmediateValue(ValueProperty, mode) : null;
    }
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs _)
    {
        DependencyObject? owner = target;
        while (owner is FrameworkContentElement content) owner = content.Parent;
        if (owner is Visual visual) CssFlowProperties.InvalidateInheritedTextLayout(visual);
        if (owner is Visual { VisualParent: UIElement parent }) parent.InvalidateMeasure();
    }

    internal static (string Open, string Close) Controls(CssUnicodeBidi value, FlowDirection direction, bool inline)
    {
        bool rtl = direction == FlowDirection.RightToLeft;
        return value switch
        {
            CssUnicodeBidi.Embed when inline => (rtl ? "\u202B" : "\u202A", "\u202C"),
            CssUnicodeBidi.Isolate when inline => (rtl ? "\u2067" : "\u2066", "\u2069"),
            CssUnicodeBidi.BidiOverride => (rtl ? "\u202E" : "\u202D", "\u202C"),
            CssUnicodeBidi.IsolateOverride when inline => (rtl ? "\u2068\u202E" : "\u2068\u202D", "\u202C\u2069"),
            CssUnicodeBidi.IsolateOverride => (rtl ? "\u202E" : "\u202D", "\u202C"),
            CssUnicodeBidi.Plaintext when inline => ("\u2068", "\u2069"),
            _ => ("", ""),
        };
    }
}
