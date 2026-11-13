using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTextGroupAlignTests
{
    [Theory]
    [InlineData("none", true)]
    [InlineData("start", true)]
    [InlineData("end", true)]
    [InlineData("left", true)]
    [InlineData("right", true)]
    [InlineData("center", true)]
    [InlineData("auto", false)]
    [InlineData("left right", false)]
    [InlineData("justify", false)]
    public void SupportsOnlyTheDefinedKeywords(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("text-group-align", value));

    [Fact]
    public void LinesShareTheGroupPositionWhileRetainingTextAlignment()
    {
        const double width = 260;
        var block = Block("short\nmuch longer");
        var longWidth = MeasureWidth(Block("much longer"));
        var shortWidth = MeasureWidth(Block("short"));
        var remaining = width - longWidth;

        Css.SetStyle(block, "text-align:left; text-group-align:center");
        var centered = Render(block, width).Texts;
        Assert.Equal(2, centered.Count);
        Assert.InRange(centered[0].X, remaining / 2 - 1, remaining / 2 + 1);
        Assert.Equal(centered[0].X, centered[1].X, 1);

        Css.SetStyle(block, "text-align:right; text-group-align:center");
        var rightAligned = Render(block, width).Texts;
        Assert.InRange(rightAligned[0].X,
            remaining / 2 + longWidth - shortWidth - 1,
            remaining / 2 + longWidth - shortWidth + 1);
        Assert.InRange(rightAligned[1].X, remaining / 2 - 1, remaining / 2 + 1);

        Css.SetStyle(block, "text-align:left; text-group-align:right");
        var groupRight = Render(block, width).Texts;
        Assert.Equal(centered[0].X * 2, groupRight[0].X, 1);
        Assert.Equal(groupRight[0].X, groupRight[1].X, 1);
    }

    [Fact]
    public void StartAndEndFollowDirectionButLeftAndRightDoNot()
    {
        var block = Block("short\nmuch longer");
        const double width = 260;

        double FirstX(string direction, string group)
        {
            Css.SetStyle(block,
                $"direction:{direction}; text-align:left; text-group-align:{group}");
            return Render(block, width).Texts[0].X;
        }

        var left = FirstX("ltr", "left");
        var right = FirstX("ltr", "right");
        Assert.True(right > left + 20);
        Assert.Equal(left, FirstX("ltr", "start"), 1);
        Assert.Equal(right, FirstX("ltr", "end"), 1);
        Assert.Equal(right, FirstX("rtl", "start"), 1);
        Assert.Equal(left, FirstX("rtl", "end"), 1);
        Assert.Equal(left, FirstX("rtl", "left"), 1);
        Assert.Equal(right, FirstX("rtl", "right"), 1);
    }

    [Fact]
    public void GroupAlignmentIsNotInheritedAndCssWideKeywordsResetIt()
    {
        var parent = new StackPanel();
        var block = Block("short\nmuch longer");
        parent.Children.Add(block);
        Css.SetStyle(parent, "text-group-align:center");
        Assert.InRange(Render(block, 260).Texts[0].X, 0, 1);

        Css.SetStyle(block, "text-group-align:inherit");
        Assert.True(Render(block, 260).Texts[0].X > 20);
        Css.SetStyle(block, "text-group-align:unset");
        Assert.InRange(Render(block, 260).Texts[0].X, 0, 1);
        Css.SetStyle(block, "text-group-align:right; all:initial");
        Assert.InRange(Render(block, 260).Texts[0].X, 0, 1);
    }

    [Fact]
    public void TextIndentKeepsItsOwnLineOffsetInsideTheAlignedGroup()
    {
        var block = Block("a\nlonger");
        Css.SetStyle(block,
            "white-space:pre; text-align:left; text-indent:40px; text-group-align:center");

        var lines = Render(block, 260).Texts;
        Assert.Equal(2, lines.Count);
        Assert.True(lines[1].X > 20);
        Assert.InRange(lines[0].X - lines[1].X, 39, 41);
    }

    [Fact]
    public void GroupWidthAlsoBoundsJustificationAndSelection()
    {
        var block = Block("one two\nthree four five");
        block.Select(4, 3);
        const double width = 260;

        Css.SetStyle(block, "text-align:justify-all; text-group-align:none");
        var full = Render(block, width);
        var fullTwo = Assert.Single(full.Texts, item => item.Text == "two");

        Css.SetStyle(block, "text-align:justify-all; text-group-align:center");
        var grouped = Render(block, width);
        var groupedTwo = Assert.Single(grouped.Texts, item => item.Text == "two");
        Assert.True(groupedTwo.X < fullTwo.X - 20);
        var selection = Assert.Single(grouped.Rectangles);
        Assert.InRange(selection.X, groupedTwo.X - 1, groupedTwo.X + 1);
        Assert.Equal(4, block.GetPositionFromPoint(
            new Point(groupedTwo.X + 2, groupedTwo.Y + 5), true)?.Offset);
    }

    [Fact]
    public void OrdinaryFlowAlignsAtomicInlineLineBoxesAsAGroup()
    {
        var panel = new StackPanel();
        var boxes = new Border[3];
        for (var i = 0; i < boxes.Length; i++)
        {
            boxes[i] = new Border();
            panel.Children.Add(boxes[i]);
            Css.SetStyle(boxes[i], "display:inline-block; width:100px; height:20px");
        }
        Css.SetStyle(panel,
            "display:flow-root; line-height:0; text-align:left; text-group-align:center");
        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);

        panel.Measure(new Size(220, 100));
        panel.Arrange(new Rect(0, 0, 220, 100));
        Assert.Equal(10, boxes[0].VisualBounds.X, 1);
        Assert.Equal(110, boxes[1].VisualBounds.X, 1);
        Assert.Equal(10, boxes[2].VisualBounds.X, 1);

        Css.SetStyle(panel,
            "display:flow-root; line-height:0; text-align:right; text-group-align:center");
        panel.Measure(new Size(220, 100));
        panel.Arrange(new Rect(0, 0, 220, 100));
        Assert.Equal(10, boxes[0].VisualBounds.X, 1);
        Assert.Equal(110, boxes[1].VisualBounds.X, 1);
        Assert.Equal(110, boxes[2].VisualBounds.X, 1);
    }

    [Fact]
    public void SeparateInlineRunsShareOneGroupAcrossAnInterveningBlock()
    {
        var panel = new StackPanel();
        var first = new Border();
        var second = new Border();
        var separator = new Border();
        var third = new Border();
        panel.Children.Add(first);
        panel.Children.Add(second);
        panel.Children.Add(separator);
        panel.Children.Add(third);
        foreach (var box in new[] { first, second, third })
            Css.SetStyle(box, "display:inline-block; width:100px; height:20px; vertical-align:top");
        Css.SetStyle(separator, "display:block; width:0px; height:10px");
        Css.SetStyle(panel,
            "display:flow-root; line-height:0; text-align:left; text-group-align:center");
        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);

        panel.Measure(new Size(220, 100));
        panel.Arrange(new Rect(0, 0, 220, 100));
        Assert.Equal(10, first.VisualBounds.X, 1);
        Assert.Equal(110, second.VisualBounds.X, 1);
        Assert.Equal(10, third.VisualBounds.X, 1);
        Assert.True(third.VisualBounds.Y > first.VisualBounds.Y);

        Css.SetStyle(panel,
            "display:flow-root; line-height:0; text-align:left; text-group-align:right");
        panel.Measure(new Size(220, 100));
        panel.Arrange(new Rect(0, 0, 220, 100));
        Assert.Equal(20, first.VisualBounds.X, 1);
        Assert.Equal(120, second.VisualBounds.X, 1);
        Assert.Equal(20, third.VisualBounds.X, 1);
    }

    [Fact]
    public void SeparateInlineRunsShareOneGroupBesideAFloat()
    {
        var panel = new StackPanel();
        var floating = new Border();
        var first = new Border();
        var second = new Border();
        var separator = new Border();
        var third = new Border();
        foreach (var child in new[] { floating, first, second, separator, third })
            panel.Children.Add(child);
        Css.SetStyle(floating, "float:left; width:80px; height:80px");
        foreach (var box in new[] { first, second, third })
            Css.SetStyle(box, "display:inline-block; width:90px; height:20px; vertical-align:top");
        Css.SetStyle(separator, "display:block; width:0px; height:10px");
        Css.SetStyle(panel,
            "display:flow-root; line-height:0; text-align:left; text-group-align:center");
        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);

        panel.Measure(new Size(300, 100));
        panel.Arrange(new Rect(0, 0, 300, 100));
        Assert.Equal(0, floating.VisualBounds.X, 1);
        Assert.Equal(100, first.VisualBounds.X, 1);
        Assert.Equal(190, second.VisualBounds.X, 1);
        Assert.Equal(100, third.VisualBounds.X, 1);
        Assert.True(third.VisualBounds.Y > first.VisualBounds.Y);
    }

    [Fact]
    public void DescendantBlocksShareTheGroupButIndependentFormattingContextsDoNot()
    {
        var root = new StackPanel();
        var firstBlock = new StackPanel();
        var middleBlock = new StackPanel();
        var nestedBlock = new StackPanel();
        var independent = new StackPanel();
        var direct = new Border();
        root.Children.Add(direct);
        root.Children.Add(firstBlock);
        root.Children.Add(middleBlock);
        root.Children.Add(independent);
        middleBlock.Children.Add(nestedBlock);
        var first = new Border();
        var second = new Border();
        var third = new Border();
        var excluded = new Border();
        firstBlock.Children.Add(first);
        firstBlock.Children.Add(second);
        nestedBlock.Children.Add(third);
        independent.Children.Add(excluded);
        Css.SetStyle(root,
            "display:flow-root; line-height:0; text-align:left; text-group-align:center");
        foreach (var block in new[] { firstBlock, middleBlock, nestedBlock })
            Css.SetStyle(block, "display:block; line-height:0; text-align:left");
        Css.SetStyle(independent, "display:flow-root; line-height:0; text-align:left");
        foreach (var box in new[] { direct, first, second, third })
            Css.SetStyle(box, "display:inline-block; width:100px; height:20px; vertical-align:top");
        Css.SetStyle(excluded,
            "display:inline-block; width:210px; height:20px; vertical-align:top");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        root.Measure(new Size(220, 100));
        root.Arrange(new Rect(0, 0, 220, 100));
        Assert.Equal(10, direct.VisualBounds.X, 1);
        Assert.Equal(10, first.VisualBounds.X, 1);
        Assert.Equal(110, second.VisualBounds.X, 1);
        Assert.Equal(10, third.VisualBounds.X, 1);
        Assert.Equal(0, excluded.VisualBounds.X, 1);

        Css.SetStyle(root,
            "display:flow-root; line-height:0; text-align:left; text-group-align:none");
        root.Measure(new Size(220, 100));
        root.Arrange(new Rect(0, 0, 220, 100));
        Assert.Equal(0, direct.VisualBounds.X, 1);
        Assert.Equal(0, first.VisualBounds.X, 1);
        Assert.Equal(0, third.VisualBounds.X, 1);

        Css.SetStyle(root,
            "display:flow-root; line-height:0; text-align:left; text-group-align:center");
        root.Measure(new Size(220, 100));
        root.Arrange(new Rect(0, 0, 220, 100));
        Assert.Equal(10, first.VisualBounds.X, 1);
        Assert.Equal(10, third.VisualBounds.X, 1);

        root.Measure(new Size(230, 100));
        root.Arrange(new Rect(0, 0, 230, 100));
        Assert.Equal(15, first.VisualBounds.X, 1);
        Assert.Equal(15, third.VisualBounds.X, 1);
        root.Arrange(new Rect(0, 0, 230, 100));
        Assert.Equal(15, third.VisualBounds.X, 1);
    }

    [Fact]
    public void DescendantBlockLinesShareFloatBandsAndUpdateWithGroupAlignment()
    {
        var root = new StackPanel();
        var floating = new Border();
        var firstBlock = new StackPanel();
        var secondBlock = new StackPanel();
        root.Children.Add(floating);
        root.Children.Add(firstBlock);
        root.Children.Add(secondBlock);
        var first = new Border();
        var second = new Border();
        var third = new Border();
        firstBlock.Children.Add(first);
        firstBlock.Children.Add(second);
        secondBlock.Children.Add(third);
        Css.SetStyle(floating, "float:left; width:80px; height:80px");
        foreach (var block in new[] { firstBlock, secondBlock })
            Css.SetStyle(block, "display:block; line-height:0; text-align:left");
        foreach (var box in new[] { first, second, third })
            Css.SetStyle(box, "display:inline-block; width:90px; height:20px; vertical-align:top");
        Css.SetStyle(root,
            "display:flow-root; line-height:0; text-align:left; text-group-align:center");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        root.Measure(new Size(300, 100));
        root.Arrange(new Rect(0, 0, 300, 100));
        Assert.Equal(0, floating.VisualBounds.X, 1);
        Assert.Equal(100, first.VisualBounds.X, 1);
        Assert.Equal(190, second.VisualBounds.X, 1);
        Assert.Equal(100, third.VisualBounds.X, 1);

        Css.SetStyle(root,
            "display:flow-root; line-height:0; text-align:left; text-group-align:right");
        root.Measure(new Size(300, 100));
        root.Arrange(new Rect(0, 0, 300, 100));
        Assert.Equal(120, first.VisualBounds.X, 1);
        Assert.Equal(210, second.VisualBounds.X, 1);
        Assert.Equal(120, third.VisualBounds.X, 1);
    }

    [Fact]
    public void FloatBandsContributeTheirRemainingSpaceToTheGroup()
    {
        var panel = new StackPanel();
        var floating = new Border();
        panel.Children.Add(floating);
        Css.SetStyle(floating, "float:left; width:80px; height:40px");
        var boxes = new Border[3];
        for (var i = 0; i < boxes.Length; i++)
        {
            boxes[i] = new Border();
            panel.Children.Add(boxes[i]);
            Css.SetStyle(boxes[i],
                "display:inline-block; width:90px; height:20px; vertical-align:top");
        }
        Css.SetStyle(panel,
            "display:flow-root; line-height:0; text-align:left; " +
            "text-align-last:right; text-group-align:center");
        CssEvaluationScheduler.FlushIfPending(panel.Dispatcher);

        panel.Measure(new Size(300, 100));
        panel.Arrange(new Rect(0, 0, 300, 100));
        Assert.Equal(0, floating.VisualBounds.X, 1);
        Assert.Equal(100, boxes[0].VisualBounds.X, 1);
        Assert.Equal(190, boxes[1].VisualBounds.X, 1);
        Assert.Equal(190, boxes[2].VisualBounds.X, 1);

        Css.SetStyle(floating, "float:right; width:80px; height:40px");
        Css.SetStyle(panel,
            "display:flow-root; line-height:0; text-align:left; text-group-align:right");
        panel.Measure(new Size(300, 100));
        panel.Arrange(new Rect(0, 0, 300, 100));
        Assert.Equal(220, floating.VisualBounds.X, 1);
        Assert.Equal(40, boxes[0].VisualBounds.X, 1);
        Assert.Equal(130, boxes[1].VisualBounds.X, 1);
        Assert.Equal(40, boxes[2].VisualBounds.X, 1);
    }

    private static TextBlock Block(string text) => new()
    {
        Text = text, FontSize = 20, Foreground = Brushes.Black,
        TextWrapping = TextWrapping.NoWrap,
    };

    private static double MeasureWidth(TextBlock block)
    {
        block.Measure(new Size(double.PositiveInfinity, 100));
        return block.DesiredSize.Width;
    }

    private static CaptureContext Render(TextBlock block, double width)
    {
        block.Measure(new Size(width, 100));
        block.Arrange(new Rect(0, 0, width, 100));
        var context = new CaptureContext();
        block.Render(context);
        return context;
    }

    private sealed class CaptureContext : DrawingContextAdapter
    {
        public List<(string Text, double X, double Y)> Texts { get; } = [];
        public List<Rect> Rectangles { get; } = [];
        public override void DrawText(FormattedText text, Point origin)
            => Texts.Add((text.Text, origin.X, origin.Y));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
            => Rectangles.Add(rectangle);
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle,
            double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center,
            double radiusX, double radiusY) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect,
            CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
