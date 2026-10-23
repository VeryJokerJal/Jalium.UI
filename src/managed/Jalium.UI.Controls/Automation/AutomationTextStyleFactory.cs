using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Automation;

internal static class AutomationTextStyleFactory
{
    internal static AutomationTextStyle Control(Control owner, Brush foreground, int alignment = 0) => new(
        owner.FontFamily?.GetRenderingSource(owner) ?? FrameworkElement.DefaultFontFamilyName,
        owner.FontSize, owner.FontWeight.ToOpenTypeWeight(), owner.FontStyle.ToOpenTypeStyle(),
        Color(foreground), Alignment: alignment, Direction: owner.FlowDirection == FlowDirection.RightToLeft ? 1 : 0,
        Language: owner.Language.IetfLanguageTag);

    internal static uint? Color(Brush? brush) => brush is SolidColorBrush solid ? Color(solid.Color, solid.Opacity) : null;
    internal static uint Color(Color color, double opacity) =>
        (uint)((byte)Math.Clamp(Math.Round(color.A * Math.Clamp(opacity, 0, 1)), 0, 255) << 24)
        | (uint)color.R << 16 | (uint)color.G << 8 | color.B;

    internal static uint? InlineBackground(Inline? element)
    {
        uint? foreground = null;
        for (Inline? source = element; source is not null; source = source.Parent as Inline)
        {
            if (foreground is { } opaque && opaque >> 24 == 255) return opaque;
            if (source.Background is not { } brush) continue;
            if (Color(brush) is not { } background)
            {
                // A gradient under translucent foreground cannot be represented
                // by one attributed-string color. Invisible layers add nothing.
                if (brush.Opacity <= 0) continue;
                return null;
            }
            foreground = foreground is { } top ? Over(top, background) : background;
        }
        return foreground;

        static uint Over(uint top, uint bottom)
        {
            int a = (int)(top >> 24), b = (int)(bottom >> 24);
            int coverage = a * 255 + b * (255 - a);
            if (coverage == 0) return top;
            uint channel(int shift) => (uint)Math.Round(((top >> shift & 255) * a * 255d +
                (bottom >> shift & 255) * b * (255d - a)) / coverage);
            return (uint)Math.Round(coverage / 255d) << 24 | channel(16) << 16 | channel(8) << 8 | channel(0);
        }
    }

    private sealed record InlinePathState(string? ParentPath, string Path);
    private sealed class InlinePathCache { internal volatile InlinePathState? State; }
    private static readonly ConditionalWeakTable<Span, InlinePathCache> InlinePaths = new();

    internal static string? InlineContext(Inline? element) => element?.Parent is Span parent ? Path(parent, 0) : null;

    private static string? Path(Span span, int depth)
    {
        if (depth == 64) return null;
        string? prefix = null;
        if (span.Parent is Span parent && (prefix = Path(parent, depth + 1)) is null) return null;
        var cache = InlinePaths.GetValue(span, static _ => new());
        var state = cache.State;
        if (state is not null && state.ParentPath == prefix) return state.Path;
        string identity = WritingToolsDocumentFragment.OriginFor(span);
        string value = prefix is null ? identity : prefix + "/" + identity;
        // Only strings are retained. A cached path cannot keep an old parent or
        // document alive, and a changed ancestor invalidates descendants lazily.
        cache.State = new(prefix, value);
        return value;
    }

    internal static AutomationTextStyle Decorations(AutomationTextStyle style, TextElement? element)
    {
        // Drawing adds ancestor lines even when a child specifies CSS `none`.
        // The closest painted line supplies the representable color/style.
        for (var source = element; source != null; source = source.Parent)
        {
            bool css = source.GetEffectiveValueLayer(TextElement.TextDecorationsProperty) is
                DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState;
            if (css)
            {
                var line = CssTextDecorationProperties.Line(source);
                int type = CssTextDecorationProperties.Style(source) switch
                { CssTextDecorationStyle.Double => 9, CssTextDecorationStyle.Dotted => 0x101, CssTextDecorationStyle.Dashed => 0x201, _ => 1 };
                uint? color = Color(CssTextDecorationProperties.Color(source) ?? source.GetEffectiveForeground());
                if (style.Underline == 0 && (line & CssTextDecorationLine.Underline) != 0) style = style with { Underline = type, UnderlineColor = color };
                if (style.Strikethrough == 0 && (line & CssTextDecorationLine.LineThrough) != 0) style = style with { Strikethrough = type, StrikethroughColor = color };
            }
            else if (source.TextDecorations is { } decorations)
                foreach (var decoration in decorations)
                {
                    uint? color = Color(decoration.Pen?.Brush ?? source.GetEffectiveForeground());
                    if (style.Underline == 0 && decoration.Location == TextDecorationLocation.Underline) style = style with { Underline = 1, UnderlineColor = color };
                    if (style.Strikethrough == 0 && decoration.Location == TextDecorationLocation.Strikethrough) style = style with { Strikethrough = 1, StrikethroughColor = color };
                }
        }
        return style;
    }
}
