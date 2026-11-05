using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssHangingPunctuationTests
{
    [Theory]
    [InlineData("none", true)]
    [InlineData("first", true)]
    [InlineData("last", true)]
    [InlineData("force-end", true)]
    [InlineData("allow-end", true)]
    [InlineData("last first force-end", true)]
    [InlineData("allow-end first last", true)]
    [InlineData("", false)]
    [InlineData("first first", false)]
    [InlineData("none last", false)]
    [InlineData("allow-end force-end", false)]
    [InlineData("first unknown", false)]
    public void ParsesTheCssTextGrammar(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("hanging-punctuation", value));

    [Fact]
    public void FirstHangsAnOpeningQuoteAndKeepsSourceInteraction()
    {
        var block = Block("“Hello");
        Css.SetStyle(block, "hanging-punctuation:none; white-space:nowrap");
        var baseline = Width(block);
        var quoteWidth = Width(Block("“")) - 2;
        block.Select(0, 1);
        Css.SetStyle(block, "hanging-punctuation:first; white-space:nowrap");
        Assert.InRange(baseline - Width(block), quoteWidth - 0.6, quoteWidth + 0.6);
        var draw = Render(block, 150);
        var quote = Assert.Single(draw.Texts, item => item.Text.Contains('“'));
        Assert.InRange(quote.X, -quoteWidth - 0.6, -quoteWidth + 0.6);
        Assert.Equal("“", block.SelectedText);
        var selected = Assert.Single(draw.Rectangles);
        Assert.InRange(selected.X, quote.X - 0.6, quote.X + 0.6);
        Assert.Equal(0, block.GetPositionFromPoint(
            new Point(quote.X + quoteWidth / 4, quote.Y + 5), true)?.Offset);
        Assert.True(draw.ClipRects.Any(rect => rect.X < 0));
    }

    [Fact]
    public void LastHangsClosingQuoteButOnlyOnTheFinalFormattedLine()
    {
        var block = Block("Hello”");
        Css.SetStyle(block, "hanging-punctuation:none; white-space:nowrap; text-align:right");
        var baseline = Width(block);
        var without = Render(block, 150);
        Css.SetStyle(block, "hanging-punctuation:last; white-space:nowrap; text-align:right");
        var quoteWidth = Width(Block("”")) - 2;
        Assert.InRange(baseline - Width(block), quoteWidth - 0.6, quoteWidth + 0.6);
        var with = Render(block, 150);
        Assert.InRange(with.Texts[0].X - without.Texts[0].X,
            quoteWidth - 0.6, quoteWidth + 0.6);

        block.Text = "Hello”\nWorld";
        Css.SetStyle(block, "hanging-punctuation:none; white-space:pre");
        var multiBaseline = Width(block);
        Css.SetStyle(block, "hanging-punctuation:last; white-space:pre");
        Assert.InRange(Width(block) - multiBaseline, -0.5, 0.5);
    }

    [Fact]
    public void ForceEndHangsACommaWhileAllowEndHangsOnlyOverflow()
    {
        var block = Block("Hello,");
        Css.SetStyle(block, "hanging-punctuation:none; white-space:normal");
        var baseline = Width(block);
        var commaWidth = Width(Block(",")) - 2;
        Css.SetStyle(block, "hanging-punctuation:force-end; white-space:normal");
        Assert.InRange(baseline - Width(block), commaWidth - 0.6, commaWidth + 0.6);

        Css.SetStyle(block, "hanging-punctuation:allow-end; white-space:normal");
        Assert.InRange(Width(block) - baseline, -0.5, 0.5);
        var constrained = Render(block, baseline - commaWidth / 2);
        Assert.Single(constrained.Texts.Select(item => item.Y).Distinct());
        Assert.True(constrained.ClipRects.Any(rect =>
            rect.Width > baseline - commaWidth / 2));
    }

    [Fact]
    public void AllowEndExcludesTheCommaFromMinContentButKeepsItInMaxContent()
    {
        var block = Block("A,");
        Css.SetStyle(block, "white-space:normal; hanging-punctuation:none");
        var maximum = Width(block);
        block.Measure(new Size(0, 400));
        var minimum = block.DesiredSize.Width;
        var commaWidth = Width(Block(",")) - 2;

        Css.SetStyle(block, "white-space:normal; hanging-punctuation:allow-end");
        Assert.InRange(Width(block) - maximum, -0.5, 0.5);
        block.Measure(new Size(0, 400));
        Assert.InRange(minimum - block.DesiredSize.Width,
            commaWidth - 0.6, commaWidth + 0.6);
    }

    [Fact]
    public void ARunCanControlTheLineEnd()
    {
        var span = new Span(new Run("Hello"));
        var comma = new Run(",");
        span.Inlines.Add(comma);
        var block = new TextBlock(span) { FontSize = 20, Foreground = Brushes.Black };
        Css.SetStyle(block, "hanging-punctuation:none; white-space:nowrap");
        var baseline = Width(block);
        Css.SetStyle(comma, "hanging-punctuation:force-end");
        Assert.True(Width(block) < baseline - 1);
        Css.SetStyle(comma, "hanging-punctuation:none");
        Assert.InRange(Width(block) - baseline, -0.5, 0.5);
    }

    [Fact]
    public void ASpanCanHangAtItsLastLineBeforeFollowingText()
    {
        var closing = new Span(new Run("Hello”"));
        var block = new TextBlock { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(closing);
        block.Inlines.Add(new LineBreak());
        block.Inlines.Add(new Run("A"));
        Css.SetStyle(block, "white-space:pre; hanging-punctuation:none");
        var baseline = Width(block);
        var quoteWidth = Width(Block("”")) - 2;

        Css.SetStyle(closing, "hanging-punctuation:last");
        Assert.InRange(baseline - Width(block), quoteWidth - 0.6, quoteWidth + 0.6);
        Css.SetStyle(closing, "hanging-punctuation:none");
        Assert.InRange(Width(block) - baseline, -0.5, 0.5);
    }

    [Fact]
    public void ASpanCanHangAtItsFirstLineAfterEarlierText()
    {
        var opening = new Span(new Run("“Hello"));
        var block = new TextBlock { FontSize = 20, Foreground = Brushes.Black };
        block.Inlines.Add(new Run("A"));
        block.Inlines.Add(new LineBreak());
        block.Inlines.Add(opening);
        Css.SetStyle(block, "white-space:pre; hanging-punctuation:none");
        var baseline = Width(block);
        var quoteWidth = Width(Block("“")) - 2;

        Css.SetStyle(opening, "hanging-punctuation:first");
        Assert.InRange(baseline - Width(block), quoteWidth - 0.6, quoteWidth + 0.6);
        Css.SetStyle(opening, "hanging-punctuation:none");
        Assert.InRange(Width(block) - baseline, -0.5, 0.5);
    }

    [Theory]
    [InlineData("“AB", "first")]
    [InlineData("AB”", "last")]
    public void HangingEdgeCanKeepAnOtherwiseWrappedLineTogether(
        string text, string mode)
    {
        var block = Block(text);
        Css.SetStyle(block,
            "hanging-punctuation:none; white-space:normal; overflow-wrap:anywhere");
        var width = Width(Block("AB"));
        var ordinary = Render(block, width);
        Assert.True(ordinary.Texts.Select(item => item.Y).Distinct().Count() > 1);
        Css.SetStyle(block,
            $"hanging-punctuation:{mode}; white-space:normal; overflow-wrap:anywhere");
        var hanging = Render(block, width);
        Assert.Single(hanging.Texts.Select(item => item.Y).Distinct());
    }

    [Theory]
    [InlineData("A,", "force-end", ',')]
    [InlineData("中、", "force-end", '、')]
    [InlineData("A.", "force-end", '.')]
    [InlineData("A!", "force-end", '\0')]
    public void ForceEndUsesOnlySpecifiedStopAndCommaCharacters(
        string source, string mode, char expected)
    {
        var block = Block(source);
        Css.SetStyle(block, "hanging-punctuation:none; white-space:nowrap");
        var baseline = Width(block);
        Css.SetStyle(block, $"hanging-punctuation:{mode}; white-space:nowrap");
        var difference = baseline - Width(block);
        if (expected == '\0') Assert.InRange(difference, -0.5, 0.5);
        else Assert.True(difference > 1);
    }

    [Fact]
    public void ForceEndKeepsAnIdeographicCommaWithThePrecedingText()
    {
        var block = Block("中、文");
        var width = Width(Block("中")) + 1;
        Css.SetStyle(block, "hanging-punctuation:force-end; white-space:normal");
        var draw = Render(block, width);
        var firstRow = draw.Texts.Min(item => item.Y);
        Assert.Contains(draw.Texts, item => item.Y == firstRow && item.Text.Contains('、'));
        Assert.Contains(draw.Texts, item => item.Y > firstRow && item.Text.Contains('文'));
    }

    [Fact]
    public void ForceEndContributesToJustificationAndSelection()
    {
        var block = Block("中文、");
        block.Select(2, 1);
        Css.SetStyle(block,
            "hanging-punctuation:none; text-align:justify-all; " +
            "text-justify:inter-character; white-space:nowrap");
        var ordinary = Render(block, 100);
        Css.SetStyle(block,
            "hanging-punctuation:force-end; text-align:justify-all; " +
            "text-justify:inter-character; white-space:nowrap");
        var hanging = Render(block, 100);
        var ordinaryLast = Assert.Single(ordinary.Texts, item => item.Text.Contains('、'));
        var hangingLast = Assert.Single(hanging.Texts, item => item.Text.Contains('、'));
        Assert.True(hangingLast.X > ordinaryLast.X + 1);
        Assert.Equal("、", block.SelectedText);
        Assert.Single(hanging.Rectangles);
    }

    [Fact]
    public void HangingCommaIsMeasuredAfterCaseExpansion()
    {
        var block = Block("ß,");
        Css.SetStyle(block,
            "hanging-punctuation:none; text-transform:uppercase; " +
            "white-space:normal; overflow-wrap:anywhere");
        var baseline = Width(block);
        var commaWidth = Width(Block(",")) - 2;
        Css.SetStyle(block,
            "hanging-punctuation:force-end; text-transform:uppercase; " +
            "white-space:normal; overflow-wrap:anywhere");
        Assert.InRange(baseline - Width(block), commaWidth - 0.6, commaWidth + 0.6);
        var draw = Render(block, 150);
        Assert.Equal("SS,", string.Concat(draw.Texts.Select(item => item.Text)));
        Assert.Equal("ß,", block.Text);
    }

    [Fact]
    public void InheritedModeRelayoutsWhenItsAncestorChanges()
    {
        var parent = new StackPanel();
        var block = Block("Hello,");
        parent.Children.Add(block);
        Css.SetStyle(block, "white-space:nowrap");
        var baseline = Width(block);
        Css.SetStyle(parent, "hanging-punctuation:force-end");
        Assert.True(Width(block) < baseline - 1);
        Css.SetStyle(parent, "hanging-punctuation:none");
        Assert.InRange(Width(block) - baseline, -0.5, 0.5);
    }

    [Fact]
    public void OneVisibleQuoteCannotHangTwiceAcrossCollapsedTrailingSpace()
    {
        var block = Block("“ ");
        Css.SetStyle(block, "white-space:normal; hanging-punctuation:first");
        var firstOnly = Width(block);
        Css.SetStyle(block, "white-space:normal; hanging-punctuation:first last");
        Assert.InRange(Width(block) - firstOnly, -0.5, 0.5);
    }

    private static TextBlock Block(string text) => new()
    {
        Text = text, FontSize = 20, Foreground = Brushes.Black,
    };

    private static double Width(TextBlock block)
    {
        block.Measure(new Size(double.PositiveInfinity, 400));
        return block.DesiredSize.Width;
    }

    private static Capture Render(TextBlock block, double width)
    {
        block.Measure(new Size(width, 400));
        block.Arrange(new Rect(0, 0, width, 400));
        var capture = new Capture();
        block.Render(capture);
        return capture;
    }

    private sealed class Capture : DrawingContextAdapter
    {
        public List<(string Text, double X, double Y)> Texts { get; } = [];
        public List<Rect> Rectangles { get; } = [];
        public List<Rect> ClipRects { get; } = [];
        public override void DrawText(FormattedText text, Point origin)
            => Texts.Add((text.Text, origin.X, origin.Y));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
            => Rectangles.Add(rectangle);
        public override void PushClip(Geometry geometry)
        {
            if (geometry is RectangleGeometry rectangle) ClipRects.Add(rectangle.Rect);
        }
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
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
