using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssWordBreakTests
{
    [Theory]
    [InlineData("normal", true)]
    [InlineData("keep-all", true)]
    [InlineData("break-all", true)]
    [InlineData("break-word", true)]
    [InlineData("anywhere", false)]
    public void SupportsTheWordBreakGrammar(string value, bool expected)
        => Assert.Equal(expected, Css.Supports("word-break", value));

    [Fact]
    public void BreakAllAddsLetterBreaksBeforeOverflowAndFillsTheLine()
    {
        var block = Block("alpha betagamma");
        Css.SetStyle(block, "word-break:normal; overflow-wrap:normal");
        var normal = Render(block, 120);

        Css.SetStyle(block, "word-break:break-all; overflow-wrap:normal");
        var breakAll = Render(block, 120);

        Assert.True(breakAll[0].Length > normal[0].Length,
            $"normal='{normal[0]}', break-all='{breakAll[0]}'");
        Assert.Equal(block.Text, string.Concat(breakAll));
    }

    [Fact]
    public void BreakAllPreservesExtendedGraphemeClusters()
    {
        var block = Block("e\u0301bcdef");
        Css.SetStyle(block, "word-break:break-all");

        var lines = Render(block, 5);
        Assert.Equal("e\u0301", lines[0]);
        Assert.Equal(block.Text, string.Concat(lines));
    }

    [Fact]
    public void KeepAllPrefersTheWordSeparatorOverImplicitCjkBreaks()
    {
        var block = Block("中文 汉字更多");
        Css.SetStyle(block, "word-break:normal; overflow-wrap:normal");
        var normal = Render(block, 80);

        Css.SetStyle(block, "word-break:keep-all; overflow-wrap:normal");
        var keepAll = Render(block, 80);

        Assert.Equal("中文 ", keepAll[0]);
        Assert.True(normal[0].Length > keepAll[0].Length,
            $"normal='{normal[0]}', keep-all='{keepAll[0]}'");
    }

    [Fact]
    public void KeepAllCanRelaxAtUsedWidthButPreservesItsIntrinsicWordWidth()
    {
        var block = Block("你好世界");
        Css.SetStyle(block, "word-break:normal; overflow-wrap:normal");
        block.Measure(new Size(0, 200));
        var normalIntrinsic = block.DesiredSize.Width;

        Css.SetStyle(block, "word-break:keep-all; overflow-wrap:normal");
        block.Measure(new Size(0, 200));
        var keepAllIntrinsic = block.DesiredSize.Width;
        var usedLines = Render(block, 45);

        Assert.True(keepAllIntrinsic > normalIntrinsic + 30);
        Assert.True(usedLines.Length > 1);
        Assert.Equal(block.Text, string.Concat(usedLines));
    }

    [Fact]
    public void BreakWordForcesEmergencyBreaksEvenWhenOverflowWrapIsNormal()
    {
        var block = Block("abcdefghijklmno");
        Css.SetStyle(block, "word-break:normal; overflow-wrap:normal");
        Assert.Single(Render(block, 55));

        Css.SetStyle(block, "word-break:break-word; overflow-wrap:normal");
        block.Measure(new Size(0, 200));
        var intrinsic = block.DesiredSize.Width;
        var lines = Render(block, 55);

        Assert.True(lines.Length > 1);
        Assert.Equal(block.Text, string.Concat(lines));
        Assert.InRange(intrinsic, 1, 45);
    }

    [Fact]
    public void InheritedChangeAndInitialResetUpdateTheChild()
    {
        var parent = new StackPanel();
        var block = Block("abcdefghijklmno");
        parent.Children.Add(block);
        Css.SetStyle(parent, "word-break:normal");
        Assert.Single(Render(block, 55));

        Css.SetStyle(parent, "word-break:break-all");
        Assert.True(Render(block, 55).Length > 1);

        Css.SetStyle(block, "word-break:initial");
        Assert.Single(Render(block, 55));
    }

    [Fact]
    public void NativeNoWrapStopsCssWordBreak()
    {
        var block = Block("abcdefghijklmno");
        block.TextWrapping = TextWrapping.NoWrap;
        Css.SetStyle(block, "word-break:break-all");

        Assert.Single(Render(block, 55));
    }

    private static TextBlock Block(string text) => new()
    {
        Text = text, FontSize = 20, Foreground = Brushes.Black,
    };

    private static string[] Render(TextBlock block, double width)
    {
        block.Measure(new Size(width, 400));
        block.Arrange(new Rect(0, 0, width, 400));
        var context = new TextDrawingContext();
        block.Render(context);
        return context.Texts.ToArray();
    }

    private sealed class TextDrawingContext : DrawingContextAdapter
    {
        public List<string> Texts { get; } = [];
        public override void DrawText(FormattedText formattedText, Point origin)
            => Texts.Add(formattedText.Text);
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
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
