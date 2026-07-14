using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildTextDisplaySection() => Section(
        "Text & Icons",
        "Read-only text, rich markdown, and the icon element family.",
        Card("TextBlock", BuildTextBlockDemo()),
        Card("Label", new Label { Content = "Account name" }),
        Card("Markdown", BuildMarkdownDemo(), width: 360),
        Card("FontIcon", BuildFontIconRow()),
        Card("SymbolIcon", BuildSymbolIconRow()),
        Card("PathIcon", BuildPathIconRow()));

    private static UIElement BuildTextBlockDemo()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(new TextBlock
        {
            Text = "Headline text",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary
        });
        panel.Children.Add(new TextBlock
        {
            Text = "A secondary supporting line that wraps across the available width when the content is long.",
            FontSize = 13,
            Foreground = TextSecondary,
            TextWrapping = TextWrapping.Wrap
        });
        return panel;
    }

    private static UIElement BuildMarkdownDemo() => new Markdown
    {
        Text =
            "### Markdown\n" +
            "Renders **bold**, *italic*, and `inline code`.\n\n" +
            "- First item\n" +
            "- Second item\n" +
            "- Third item\n",
        Width = 320
    };

    private static UIElement BuildFontIconRow()
    {
        var family = new FontFamily("Segoe MDL2 Assets");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        // Segoe MDL2 Assets glyphs: Home, Mail, Settings, Save.
        row.Children.Add(new FontIcon { Glyph = "\ue80f", FontFamily = family, FontSize = 24, Margin = new Thickness(0, 0, 16, 0) });
        row.Children.Add(new FontIcon { Glyph = "\ue715", FontFamily = family, FontSize = 24, Margin = new Thickness(0, 0, 16, 0) });
        row.Children.Add(new FontIcon { Glyph = "\ue713", FontFamily = family, FontSize = 24, Margin = new Thickness(0, 0, 16, 0) });
        row.Children.Add(new FontIcon { Glyph = "\ue74e", FontFamily = family, FontSize = 24 });
        return row;
    }

    private static UIElement BuildSymbolIconRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new SymbolIcon(Symbol.Home) { Margin = new Thickness(0, 0, 16, 0) });
        row.Children.Add(new SymbolIcon(Symbol.Save) { Margin = new Thickness(0, 0, 16, 0) });
        row.Children.Add(new SymbolIcon(Symbol.Setting) { Margin = new Thickness(0, 0, 16, 0) });
        row.Children.Add(new SymbolIcon(Symbol.Mail));
        return row;
    }

    private static UIElement BuildPathIconRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        // Simple house outline drawn in the 0..24 coordinate box.
        row.Children.Add(new PathIcon
        {
            Data = Geometry.Parse("M 12,2 L 22,11 L 19,11 L 19,21 L 14,21 L 14,14 L 10,14 L 10,21 L 5,21 L 5,11 L 2,11 Z"),
            Width = 28,
            Height = 28,
            Margin = new Thickness(0, 0, 16, 0)
        });
        // A diamond.
        row.Children.Add(new PathIcon
        {
            Data = Geometry.Parse("M 12,2 L 22,12 L 12,22 L 2,12 Z"),
            Width = 28,
            Height = 28,
            Margin = new Thickness(0, 0, 16, 0)
        });
        // A plus / cross.
        row.Children.Add(new PathIcon
        {
            Data = Geometry.Parse("M 9,2 L 15,2 L 15,9 L 22,9 L 22,15 L 15,15 L 15,22 L 9,22 L 9,15 L 2,15 L 2,9 L 9,9 Z"),
            Width = 28,
            Height = 28,
            Margin = new Thickness(0, 0, 16, 0)
        });
        // A square ring (frame): two nested same-winding contours with the
        // default EvenOdd fill rule — the inner square must be a HOLE. Exercises
        // compound/hole rendering; under PathAntiAliasing.Analytic this used to
        // fill solid (winding-sign hole classification), now renders as a frame.
        row.Children.Add(new PathIcon
        {
            Data = Geometry.Parse("M 2,2 L 22,2 L 22,22 L 2,22 Z M 7,7 L 17,7 L 17,17 L 7,17 Z"),
            Width = 28,
            Height = 28,
            Margin = new Thickness(0, 0, 16, 0)
        });
        // A round donut: outer + inner circle, EvenOdd — the centre is a hole.
        row.Children.Add(new PathIcon
        {
            Data = Geometry.Parse("M 2,12 A 10,10 0 1 1 22,12 A 10,10 0 1 1 2,12 Z M 7,12 A 5,5 0 1 1 17,12 A 5,5 0 1 1 7,12 Z"),
            Width = 28,
            Height = 28,
            Margin = new Thickness(0, 0, 16, 0)
        });
        // A pentagram (five-point star) drawn as ONE self-intersecting figure
        // with the default EvenOdd rule — the centre pentagon must be a HOLE.
        // Exercises the single-figure self-intersection path (no MoveTo); under
        // Analytic this must render hollow, not as a solid star.
        row.Children.Add(new PathIcon
        {
            Data = Geometry.Parse("M 12,1 L 18.5,20.9 L 1.5,8.6 L 22.5,8.6 L 5.5,20.9 Z"),
            Width = 28,
            Height = 28
        });
        return row;
    }
}
