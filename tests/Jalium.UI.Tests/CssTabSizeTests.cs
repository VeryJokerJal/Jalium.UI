using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTabSizeTests
{
    [Theory]
    [InlineData("0", true)]
    [InlineData("2.5", true)]
    [InlineData("40px", true)]
    [InlineData("1em", true)]
    [InlineData("calc(1em + 2px)", true)]
    [InlineData("-1", false)]
    [InlineData("-1px", false)]
    [InlineData("10%", false)]
    [InlineData("auto", false)]
    [InlineData("2 3", false)]
    public void SupportsTabSizeGrammar(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("tab-size", value));

    [Fact]
    public void PreservedTabsAdvanceToContentEdgeStopsAndKeepSourceOffsets()
    {
        var block = Block("A\tB\tC");
        Css.SetStyle(block, "white-space:pre; tab-size:40px");
        var plain = Render(block, 200);
        Assert.InRange(XOf(plain, "B"), 39.5, 40.5);
        Assert.InRange(XOf(plain, "C"), 79.5, 80.5);
        Assert.Equal("A\tB\tC", block.Text);

        Css.SetStyle(block, "white-space:pre; text-indent:10px; tab-size:40px");
        var indented = Render(block, 200);
        Assert.InRange(XOf(indented, "B"), 39.5, 40.5);

        block.Select(2, 1);
        var selected = Render(block, 200);
        Assert.InRange(Assert.Single(selected.Rectangles).X, 39, 41);
        Assert.Equal("B", block.SelectedText);
        Assert.InRange(block.GetPositionFromPoint(new Point(43, 5), true)!.Offset, 2, 3);
    }

    [Fact]
    public void ZeroLengthAndWhitespaceCollapsingDoNotPaintTabs()
    {
        var block = Block("A\tB");
        Css.SetStyle(block, "white-space:pre; tab-size:0");
        Assert.InRange(XOf(Render(block, 200), "B") -
            (MeasureWidth(Block("A")) - 2), -0.5, 0.5);

        Css.SetStyle(block, "white-space:normal; tab-size:80px");
        Assert.InRange(MeasureWidth(block) - MeasureWidth(Block("A B")), -0.5, 0.5);
        Assert.Equal("A\tB", block.Text);
    }

    [Fact]
    public void NumericSizeUsesBlockSpaceAdvanceIncludingWordSpacing()
    {
        var block = Block("\tB");
        Css.SetStyle(block, "white-space:pre; tab-size:4; word-spacing:normal");
        var natural = XOf(Render(block, 200), "B");
        Css.SetStyle(block, "white-space:pre; tab-size:4; word-spacing:10px");
        var spaced = XOf(Render(block, 200), "B");
        Assert.InRange(spaced - natural, 39.5, 40.5);

        Css.SetStyle(block, "white-space:pre; tab-size:4; word-spacing:50%");
        Assert.InRange(XOf(Render(block, 200), "B") - spaced, -0.5, 0.5);

        Css.SetStyle(block, "white-space:pre; tab-size:40px; word-spacing:10px");
        Assert.InRange(XOf(Render(block, 200), "B"), 39.5, 40.5);

        Css.SetStyle(block, "white-space:pre; tab-size:calc(1em + 2px)");
        Assert.InRange(XOf(Render(block, 200), "B"), 21.5, 22.5);

        Css.SetStyle(block, "white-space:pre; tab-size:initial");
        var initial = XOf(Render(block, 200), "B");
        Css.SetStyle(block, "white-space:pre; tab-size:8");
        Assert.InRange(XOf(Render(block, 200), "B") - initial, -0.5, 0.5);
    }

    [Fact]
    public void SmallStopsSkipUntilTheMinimumHalfChAdvance()
    {
        var block = Block("\tB");
        Css.SetStyle(block, "white-space:pre; tab-size:4px");
        var halfCh = (MeasureWidth(Block("0")) - 2) / 2;
        var expected = 4 * Math.Max(1, Math.Ceiling(halfCh / 4));
        Assert.InRange(XOf(Render(block, 200), "B") - expected, -0.5, 0.5);
    }

    [Fact]
    public void TabSizeAffectsWrappingAndRespondsToInheritanceAndRunOverride()
    {
        var parent = new StackPanel();
        var first = new Run("A\t");
        var block = new TextBlock(first) { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(new Run("B"));
        parent.Children.Add(block);
        Css.SetStyle(parent, "tab-size:40px");
        Css.SetStyle(block, "white-space:pre-wrap");
        Assert.InRange(XOf(Render(block, 200), "B"), 39.5, 40.5);

        Css.SetStyle(first, "tab-size:60px");
        Assert.InRange(XOf(Render(block, 200), "B"), 59.5, 60.5);

        Css.SetStyle(first, "tab-size:inherit");
        Css.SetStyle(parent, "tab-size:30px");
        CssEvaluationScheduler.FlushIfPending(block.Dispatcher);
        var inherited = Render(block, 200);
        Assert.Equal(30, ((CssTabSize)parent.GetValue(CssFlowProperties.TabSizeProperty)!).Value);
        Assert.Equal(30, ((CssTabSize)block.GetValue(CssFlowProperties.TabSizeProperty)!).Value);
        Assert.Equal(30, ((CssTabSize)first.GetValue(CssFlowProperties.TabSizeProperty)!).Value);
        Assert.InRange(XOf(inherited, "B"), 29.5, 30.5);

        var wrapped = Render(block, 25);
        var a = Assert.Single(wrapped.Texts.Where(item => item.Text == "A"));
        var b = Assert.Single(wrapped.Texts.Where(item => item.Text == "B"));
        Assert.True(b.Y > a.Y);
    }

    [Theory]
    [InlineData("center", 200, 0)]
    [InlineData("right", 180, 0)]
    [InlineData("center", 200, 10)]
    [InlineData("right", 180, 10)]
    public void AlignedLinesKeepTabsOnTheBlockContentStops(
        string alignment, double width, double leftPadding)
    {
        var block = Block("A\tB\tC");
        Css.SetStyle(block, $"white-space:pre; tab-size:40px; " +
            $"text-align:{alignment}; padding-left:{leftPadding}px");

        var draw = Render(block, width);
        var b = XOf(draw, "B");
        var c = XOf(draw, "C");
        Assert.InRange((b - leftPadding) - Math.Round((b - leftPadding) / 40) * 40,
            -0.5, 0.5);
        Assert.InRange((c - leftPadding) - Math.Round((c - leftPadding) / 40) * 40,
            -0.5, 0.5);
        Assert.True(c > b);

        block.Select(2, 1);
        var selected = Render(block, width);
        Assert.InRange(Assert.Single(selected.Rectangles).X - XOf(selected, "B"),
            -0.5, 0.5);
        Assert.Equal(2, block.GetPositionFromPoint(new Point(b + 2, 5), true)?.Offset);
    }

    [Fact]
    public void AlignedTabLineDecorationAndBreakSelectionReachThePaintedEnd()
    {
        var block = Block("A\tB\nC");
        Css.SetStyle(block, "white-space:pre; tab-size:40px; text-align:right; " +
            "text-decoration:underline solid red 2px");
        block.Select(0, 4);

        var draw = Render(block, 180);
        var paintedEnd = XOf(draw, "B") + MeasureWidth(Block("B")) - 2;
        Assert.Contains(draw.Lines, line =>
            Math.Abs(line.EndX - paintedEnd) < 0.6);
        Assert.Contains(draw.Rectangles, rectangle =>
            rectangle.Width < 10 && Math.Abs(rectangle.X - paintedEnd) < 0.6);
    }

    private static TextBlock Block(string text) => new()
    {
        Text = text, FontSize = 20, Foreground = Brushes.Black,
    };

    private static double MeasureWidth(TextBlock block)
    {
        block.Measure(new Size(double.PositiveInfinity, 400));
        return block.DesiredSize.Width;
    }

    private static double XOf(TextDrawingContext context, string text)
        => Assert.Single(context.Texts.Where(item => item.Text == text)).X;

    private static TextDrawingContext Render(TextBlock block, double width)
    {
        block.Measure(new Size(width, 400));
        block.Arrange(new Rect(0, 0, width, 400));
        var context = new TextDrawingContext();
        block.Render(context);
        return context;
    }

    private sealed class TextDrawingContext : DrawingContextAdapter
    {
        public List<(string Text, double X, double Y)> Texts { get; } = [];
        public List<Rect> Rectangles { get; } = [];
        public List<(double StartX, double EndX)> Lines { get; } = [];
        public override void DrawText(FormattedText formattedText, Point origin)
            => Texts.Add((formattedText.Text, origin.X, origin.Y));
        public override void DrawLine(Pen pen, Point point0, Point point1)
            => Lines.Add((point0.X, point1.X));
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
