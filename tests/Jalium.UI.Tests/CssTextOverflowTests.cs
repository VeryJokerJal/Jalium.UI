using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTextOverflowTests
{
    [Fact]
    public void EllipsisFitsTheVisibleLineWithoutChangingSourceText()
    {
        var block = new TextBlock
        {
            Text = "MMMMMMMMMM", Width = 60, FontSize = 20, Foreground = Brushes.Black,
        };
        Css.SetStyle(block, "white-space: nowrap; text-overflow: ellipsis");

        var drawn = Assert.Single(RenderText(block, 60));
        Assert.EndsWith("…", drawn.Text.Text);
        Assert.True(drawn.Text.Text.Length < block.Text.Length);
        Assert.Equal("MMMMMMMMMM", block.Text);
        Assert.InRange(block.DesiredSize.Width, 0, 60);

        block.Width = 300;
        Assert.Equal(block.Text, Assert.Single(RenderText(block, 300)).Text.Text);
        block.Width = 60;
        Css.SetStyle(block, "white-space: nowrap; text-overflow: clip");
        Assert.Equal(block.Text, Assert.Single(RenderText(block, 60)).Text.Text);

        block.TextTrimming = TextTrimming.None;
        Css.SetStyle(block, "white-space: nowrap; text-overflow: ellipsis");
        Assert.Equal(block.Text, Assert.Single(RenderText(block, 60)).Text.Text);
        block.ClearValue(TextBlock.TextTrimmingProperty);
        Assert.EndsWith("…", Assert.Single(RenderText(block, 60)).Text.Text);
    }

    [Fact]
    public void MixedFontRunColorsAndStylesTheEllipsis()
    {
        var first = new Run("MM") { Foreground = Brushes.Red };
        var second = new Run("MMMMMMMM") { Foreground = Brushes.Blue };
        var block = new TextBlock(first)
        {
            Width = 90, FontSize = 14, Foreground = Brushes.Black,
        };
        block.Inlines.Add(second);
        Css.SetStyle(second, "font-size: 30px; font-weight: 700");
        Css.SetStyle(block, "white-space: nowrap; text-overflow: ellipsis");

        var drawn = RenderText(block, 90);
        var ellipsis = Assert.Single(drawn.Where(item => item.Text.Text == "…"));
        Assert.Equal(30, ellipsis.Text.FontSize);
        Assert.Equal(700, ellipsis.Text.FontWeight);
        Assert.Equal(Colors.Blue,
            Assert.IsType<SolidColorBrush>(ellipsis.Text.Foreground).Color);
        Assert.InRange(ellipsis.X, 0, 90);
        Assert.Contains(drawn, item => item.Text.Text.Contains('M'));
    }

    [Fact]
    public void EllipsisPreservesInlineForegroundWithoutFontChanges()
    {
        var first = new Run("MM") { Foreground = Brushes.Red };
        var second = new Run("MMMMMMMM") { Foreground = Brushes.Blue };
        var block = new TextBlock(first)
        {
            Width = 75, FontSize = 20, Foreground = Brushes.Black,
        };
        block.Inlines.Add(second);
        Css.SetStyle(block, "white-space: nowrap; text-overflow: ellipsis");

        var drawn = RenderText(block, 75);
        var ellipsis = Assert.Single(drawn.Where(item => item.Text.Text == "…"));
        Assert.Equal(Colors.Blue,
            Assert.IsType<SolidColorBrush>(ellipsis.Text.Foreground).Color);
    }

    [Fact]
    public void EllipsisDoesNotSplitAnEmojiGrapheme()
    {
        const string family = "👨‍👩‍👧‍👦";
        var block = new TextBlock
        {
            Text = family + "MMMM", Width = 70, FontSize = 20,
            Foreground = Brushes.Black,
        };
        Css.SetStyle(block, "white-space: nowrap; text-overflow: ellipsis");

        var drawn = Assert.Single(RenderText(block, 70)).Text.Text;
        Assert.EndsWith("…", drawn);
        if (drawn.Contains("👨")) Assert.StartsWith(family, drawn);
    }

    [Fact]
    public void HiddenTextDoesNotExtendSelectionOrHitTestingPastTheEllipsis()
    {
        var block = new TextBlock
        {
            Text = "MMMMMMMMMM", Width = 60, FontSize = 20,
            Foreground = Brushes.Black,
        };
        Css.SetStyle(block, "white-space: nowrap; text-overflow: ellipsis");
        block.Measure(new Size(60, 80));
        block.Arrange(new Rect(0, 0, 60, 80));
        block.SelectAll();

        var context = new TextDrawingContext();
        block.Render(context);
        var displayed = Assert.Single(context.Texts).Text.Text;
        var highlight = Assert.Single(context.Rectangles);
        Assert.True(highlight.Right < block.RenderSize.Width);
        var pointer = block.GetPositionFromPoint(new Point(59, 40), true);
        Assert.NotNull(pointer);
        Assert.Equal(displayed.Length - 1, pointer.Offset);
    }

    [Fact]
    public void WordEllipsisEndsAtAWordBoundary()
    {
        var block = new TextBlock
        {
            Text = "alpha beta gamma", Width = 110, FontSize = 20,
            Foreground = Brushes.Black, TextTrimming = TextTrimming.WordEllipsis,
        };

        var displayed = Assert.Single(RenderText(block, 110)).Text.Text;
        Assert.EndsWith("…", displayed);
        Assert.True(displayed.Length < block.Text.Length);
        var prefix = displayed[..^1];
        Assert.StartsWith(prefix, block.Text);
        Assert.True(prefix.Length == 0 || char.IsWhiteSpace(block.Text[prefix.Length]));
    }

    private static (FormattedText Text, double X)[] RenderText(TextBlock block, double width)
    {
        block.Measure(new Size(width, 80));
        block.Arrange(new Rect(0, 0, width, 80));
        var context = new TextDrawingContext();
        block.Render(context);
        return context.Texts.ToArray();
    }

    private sealed class TextDrawingContext : DrawingContextAdapter
    {
        public List<(FormattedText Text, double X)> Texts { get; } = [];
        public List<Rect> Rectangles { get; } = [];

        public override void DrawText(FormattedText formattedText, Point origin)
            => Texts.Add((formattedText, origin.X));
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
