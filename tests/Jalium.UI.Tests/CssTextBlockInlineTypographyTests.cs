using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTextBlockInlineTypographyTests
{
    [Fact]
    public void CssRunFontControlsMeasuredSizeAndDrawnGlyphs()
    {
        var first = new Run("MM");
        var second = new Run("MM") { Foreground = Brushes.Red };
        var block = new TextBlock(first) { FontSize = 14, Foreground = Brushes.Black };
        block.Inlines.Add(second);
        Css.SetStyle(second, "font: italic 700 30px Consolas; font-stretch: expanded");

        var commands = RenderText(block);
        Assert.Equal(2, commands.Length);
        Assert.Equal(14, commands[0].Text.FontSize);
        Assert.Equal(30, commands[1].Text.FontSize);
        Assert.Equal(700, commands[1].Text.FontWeight);
        Assert.Equal(FontStyles.Italic.ToOpenTypeStyle(), commands[1].Text.FontStyle);
        Assert.Equal(FontStretches.Expanded.ToOpenTypeStretch(), commands[1].Text.FontStretch);
        Assert.Equal("Consolas", commands[1].Text.FontFamily);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(commands[1].Text.Foreground).Color);
        Assert.True(commands[1].X > commands[0].X + 10);
        var largeSize = block.DesiredSize;

        Css.SetStyle(second, "font-size: 14px; font-weight: 400; font-style: normal");
        RenderText(block);
        Assert.True(block.DesiredSize.Width < largeSize.Width - 10);
        Assert.True(block.DesiredSize.Height < largeSize.Height - 5);
    }

    [Fact]
    public void SpanTypographyFlowsToRunAndLocalRunFontWins()
    {
        var run = new Run("MMMM");
        var span = new Span(run);
        var block = new TextBlock(span) { FontSize = 14, Foreground = Brushes.Black };
        Css.SetStyle(span, "font-size: 28px; font-weight: 700; font-style: italic");

        var inherited = Assert.Single(RenderText(block));
        Assert.Equal(28, inherited.Text.FontSize);
        Assert.Equal(700, inherited.Text.FontWeight);
        Assert.Equal(FontStyles.Italic.ToOpenTypeStyle(), inherited.Text.FontStyle);

        run.FontSize = 18;
        Assert.Equal(18, Assert.Single(RenderText(block)).Text.FontSize);
        run.ClearValue(TextElement.FontSizeProperty);
        Assert.Equal(28, Assert.Single(RenderText(block)).Text.FontSize);
    }

    [Fact]
    public void StyledRunWidthDeterminesWrappingAndLinePositions()
    {
        var first = new Run("MM ");
        var second = new Run("MMMM");
        var block = new TextBlock(first)
        {
            FontSize = 14, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap,
        };
        block.Inlines.Add(second);
        Css.SetStyle(second, "font-size: 30px");

        var commands = RenderText(block, 75, 200);
        Assert.True(commands.Length >= 3);
        Assert.Contains(commands, command => command.Text.FontSize == 30);
        Assert.True(commands.Max(command => command.Y) > commands.Min(command => command.Y) + 15);

        Css.SetStyle(second, "font-size: 14px");
        var smaller = RenderText(block, 75, 200);
        Assert.True(smaller.Select(command => command.Y).Distinct().Count() <
                    commands.Select(command => command.Y).Distinct().Count());
    }

    [Fact]
    public void SelectionAndHitTestingUseStyledRunAdvances()
    {
        var first = new Run("MM");
        var second = new Run("MM");
        var block = new TextBlock(first) { FontSize = 14, Foreground = Brushes.Black };
        block.Inlines.Add(second);
        Css.SetStyle(second, "font-size: 30px");
        block.Measure(new Size(300, 100));
        block.Arrange(new Rect(0, 0, 300, 100));
        block.Select(2, 2);

        var context = new TextDrawingContext();
        block.Render(context);
        var styled = context.Texts.Single(item => item.Text.FontSize == 30);
        var selection = Assert.Single(context.Rectangles);
        Assert.InRange(Math.Abs(selection.X - styled.X), 0, 1);

        var pointer = block.GetPositionFromPoint(
            new Point(selection.X + selection.Width * 0.75, styled.Y + 5), true);
        Assert.NotNull(pointer);
        Assert.InRange(pointer.Offset, 3, 4);

        Css.SetStyle(second, "font-size: 14px");
        block.Measure(new Size(300, 100));
        block.Arrange(new Rect(0, 0, 300, 100));
        var smallerContext = new TextDrawingContext();
        block.Render(smallerContext);
        Assert.True(Assert.Single(smallerContext.Rectangles).Width < selection.Width - 15);
    }

    private static (FormattedText Text, double X, double Y)[] RenderText(
        TextBlock block, double width = 400, double height = 200)
    {
        block.Measure(new Size(width, height));
        block.Arrange(new Rect(0, 0, width, height));
        var context = new TextDrawingContext();
        block.Render(context);
        return context.Texts.ToArray();
    }

    private sealed class TextDrawingContext : DrawingContextAdapter
    {
        public List<(FormattedText Text, double X, double Y)> Texts { get; } = [];
        public List<Rect> Rectangles { get; } = [];

        public override void DrawText(FormattedText formattedText, Point origin)
            => Texts.Add((formattedText, origin.X, origin.Y));
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
            => Rectangles.Add(rectangle);
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle,
            double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center,
            double radiusX, double radiusY) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect,
            CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
