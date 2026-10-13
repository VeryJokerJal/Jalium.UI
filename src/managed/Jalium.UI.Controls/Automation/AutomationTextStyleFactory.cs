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
