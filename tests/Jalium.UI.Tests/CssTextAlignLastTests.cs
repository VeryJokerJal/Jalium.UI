using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTextAlignLastTests
{
    [Fact]
    public void LastLineAlignmentAffectsWrappedAndForcedFinalLines()
    {
        var wrapped = Block("one two three four five", TextWrapping.Wrap);
        Css.SetStyle(wrapped, "text-align:left; text-align-last:right");
        var wrappedDraw = Render(wrapped, 120);
        var rows = wrappedDraw.Texts.GroupBy(item => item.Y).ToArray();
        Assert.True(rows.Length >= 2);
        Assert.InRange(rows[0].First().X, 0, 1);
        Assert.True(rows[^1].First().X > 10);

        var forced = Block("one two\nthree four", TextWrapping.NoWrap);
        Css.SetStyle(forced, "text-align:left; text-align-last:right");
        var forcedDraw = Render(forced, 200);
        Assert.Equal(2, forcedDraw.Texts.Count);
        Assert.All(forcedDraw.Texts, item => Assert.True(item.X > 20));
    }

    [Fact]
    public void ShorthandResetsLastAlignmentAndLaterLonghandCanOverrideIt()
    {
        var block = Block("abc", TextWrapping.NoWrap);
        Css.SetStyle(block, "text-align-last:right; text-align:left");
        Assert.InRange(Assert.Single(Render(block, 180).Texts).X, 0, 1);

        Css.SetStyle(block, "text-align:left; text-align-last:right");
        Assert.InRange(Assert.Single(Render(block, 180).Texts).X, 100, 180);
    }

    [Theory]
    [InlineData("text-align:justify-all")]
    [InlineData("text-align:justify; text-align-last:justify")]
    public void JustifyingTheLastLineMovesItsWordsSelectionAndHitTarget(string style)
    {
        var block = Block("one two\nfour five", TextWrapping.NoWrap);
        Css.SetStyle(block, style);
        var fiveStart = block.Text.LastIndexOf("five", StringComparison.Ordinal);
        block.Select(fiveStart, 4);

        var draw = Render(block, 120);
        var lastY = draw.Texts.Max(item => item.Y);
        var lastRow = draw.Texts.Where(item => item.Y == lastY).ToArray();
        Assert.True(lastRow.Length >= 2);
        var five = Assert.Single(lastRow, item => item.Text.Text == "five");
        var natural = Block(block.Text, TextWrapping.NoWrap);
        Css.SetStyle(natural, "text-align:justify; text-align-last:auto");
        natural.Select(fiveStart, 4);
        var naturalFiveX = Assert.Single(Render(natural, 120).Rectangles).X;
        Assert.True(five.X > naturalFiveX + 10);
        var selection = Assert.Single(draw.Rectangles);
        Assert.InRange(selection.X, five.X - 1, five.X + 1);
        var four = Assert.Single(lastRow, item => item.Text.Text == "four");
        var prior = block.GetPositionFromPoint(new Point(four.X + 5, four.Y + 5), true);
        var hit = block.GetPositionFromPoint(new Point(five.X + 5, five.Y + 5), true);
        Assert.NotNull(prior);
        Assert.NotNull(hit);
        Assert.True(hit.Offset > prior.Offset);
    }

    [Fact]
    public void MatchParentResolvesTheParentsLogicalSideBeforeChildDirection()
    {
        var parent = new StackPanel();
        var matched = Block("abc", TextWrapping.NoWrap);
        var inherited = Block("abc", TextWrapping.NoWrap);
        parent.Children.Add(matched);
        parent.Children.Add(inherited);
        Css.SetStyle(parent, "direction:rtl; text-align:start");
        Css.SetStyle(matched, "direction:ltr; text-align:match-parent");
        Css.SetStyle(inherited, "direction:ltr; text-align:inherit");

        Assert.Equal(TextAlignment.Right, matched.TextAlignment);
        Assert.InRange(Assert.Single(Render(matched, 180).Texts).X, 100, 180);
        Assert.InRange(Assert.Single(Render(inherited, 180).Texts).X, 0, 1);

        Css.SetStyle(parent, "direction:ltr; text-align:start");
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        Assert.Equal(TextAlignment.Left, matched.TextAlignment);
        Assert.InRange(Assert.Single(Render(matched, 180).Texts).X, 0, 1);
    }

    [Fact]
    public void LastMatchParentUsesTheParentsDirection()
    {
        var parent = new StackPanel();
        var block = Block("abc", TextWrapping.NoWrap);
        parent.Children.Add(block);
        Css.SetStyle(parent, "direction:rtl; text-align-last:start");
        Css.SetStyle(block, "direction:ltr; text-align:left; text-align-last:match-parent");

        Assert.InRange(Assert.Single(Render(block, 180).Texts).X, 100, 180);
    }

    [Fact]
    public void LastStartInheritsSymbolicallyAcrossOppositeDirections()
    {
        var parent = new StackPanel();
        var block = Block("abc", TextWrapping.NoWrap);
        parent.Children.Add(block);
        Css.SetStyle(parent, "direction:rtl; text-align-last:start");
        Css.SetStyle(block, "direction:ltr; text-align-all:left");

        Assert.InRange(Assert.Single(Render(block, 180).Texts).X, 0, 1);
        Css.SetStyle(block, "direction:rtl; text-align-all:left");
        Assert.InRange(Assert.Single(Render(block, 180).Texts).X, 100, 180);
    }

    [Fact]
    public void ShorthandWideKeywordsApplyToBothAlignmentLonghands()
    {
        var parent = new StackPanel();
        var block = Block("abc", TextWrapping.NoWrap);
        parent.Children.Add(block);
        Css.SetStyle(parent, "text-align:right; text-align-last:center");
        Css.SetStyle(block, "text-align:inherit");
        Assert.Equal(TextAlignment.Right, block.TextAlignment);
        Assert.InRange(Assert.Single(Render(block, 180).Texts).X, 40, 110);

        Css.SetStyle(block, "direction:rtl; text-align:initial");
        Assert.Equal(TextAlignment.Right, block.TextAlignment);
        Assert.InRange(Assert.Single(Render(block, 180).Texts).X, 100, 180);
    }

    [Fact]
    public void NativeLocalAlignmentWinsOverCssLastLineAlignment()
    {
        var block = Block("abc", TextWrapping.NoWrap);
        block.TextAlignment = TextAlignment.Center;
        Css.SetStyle(block, "text-align:right; text-align-last:left");

        Assert.InRange(Assert.Single(Render(block, 180).Texts).X, 40, 110);
    }

    [Fact]
    public void AutoAlignsTheLastJustifiedLineToLogicalStartInRtl()
    {
        var block = Block("one two\nabc", TextWrapping.NoWrap);
        Css.SetStyle(block, "direction:rtl; text-align:justify");

        var draw = Render(block, 180);
        var last = Assert.Single(draw.Texts, item => item.Text.Text == "abc");
        Assert.InRange(last.X, 100, 180);
    }

    [Fact]
    public void TextBoxAppliesPositionalLastAlignmentToUnwrappedLines()
    {
        var box = new TextBox
        {
            Text = "abc\ndef", FontSize = 20, Foreground = Brushes.Black,
            TextWrapping = TextWrapping.NoWrap,
        };
        Css.SetStyle(box, "text-align:left; text-align-last:right");
        box.Measure(new Size(180, 80));
        box.Arrange(new Rect(0, 0, 180, 80));
        var context = new TextDrawingContext();
        box.Render(context);

        Assert.Equal(2, context.Texts.Count);
        Assert.All(context.Texts, item => Assert.InRange(item.X, 100, 180));
    }

    [Fact]
    public void OrdinaryFlowPlacesItsLastAtomicInlineOnTheRequestedSide()
    {
        var root = new StackPanel();
        Css.SetStyle(root, "display:flow-root; line-height:0; text-align:left; text-align-last:right");
        var items = new Border[3];
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = new Border();
            root.Children.Add(items[i]);
            Css.SetStyle(items[i], "display:inline-block; width:100px; height:20px");
        }
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(220, 100));
        root.Arrange(new Rect(0, 0, 220, 100));

        Assert.Equal(0, items[0].VisualBounds.X, 6);
        Assert.Equal(100, items[1].VisualBounds.X, 6);
        Assert.Equal(120, items[2].VisualBounds.X, 6);
    }

    private static TextBlock Block(string text, TextWrapping wrapping) => new()
    {
        Text = text, TextWrapping = wrapping, FontSize = 20, Foreground = Brushes.Black,
    };

    private static TextDrawingContext Render(TextBlock block, double width)
    {
        block.Measure(new Size(width, 180));
        block.Arrange(new Rect(0, 0, width, 180));
        var context = new TextDrawingContext();
        block.Render(context);
        return context;
    }

    private sealed class TextDrawingContext : DrawingContextAdapter
    {
        public List<(FormattedText Text, double X, double Y)> Texts { get; } = [];
        public List<Rect> Rectangles { get; } = [];

        public override void DrawText(FormattedText formattedText, Point origin)
            => Texts.Add((formattedText, origin.X, origin.Y));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
            => Rectangles.Add(rectangle);
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
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
