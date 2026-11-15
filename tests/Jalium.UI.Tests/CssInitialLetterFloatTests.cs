using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class CssInitialLetterFloatTests
{
    private const string BodyStyle = "display:block;font-family:Georgia;font-size:16px;line-height:24px;color:black;";
    private const string Tail = "Ordinary text continues around the initial letter and each floating box. It returns to the full line width after all exclusions end.";
    private static StackPanel Root(bool rtl, params UIElement[] children)
    {
        var root = new StackPanel { FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
        Css.SetStyle(root, BodyStyle); foreach (var child in children) root.Children.Add(child); return root;
    }
    private static TextBlock Head(string extra = "")
    {
        var text = new TextBlock { Text = "A", IsTextSelectionEnabled = true };
        Css.SetStyle(text, "display:inline;initial-letter:3;initial-letter-wrap:none;color:green;" + extra); return text;
    }
    private static TextBlock Text(string value)
    {
        var text = new TextBlock { Text = value, IsTextSelectionEnabled = true };
        Css.SetStyle(text, "display:inline;white-space:pre-line;"); return text;
    }
    private static Border Float(int width, int height, string extra = "")
    {
        var box = new Border();
        Css.SetStyle(box, $"float:inline-start;width:{width}px;height:{height}px;background:blue;" + extra); return box;
    }
    private static void Layout(FrameworkElement root, int width = 312)
    {
        for (var pass = 0; pass < 6; pass++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new(width, 700)); root.Arrange(new(0, 0, width, 700)); root.UpdateLayout();
        }
        Assert.True(root.IsMeasureValid); Assert.True(root.IsArrangeValid);
    }
    private static Rect Initial(TextBlock head, Visual root)
    {
        var allocation = Assert.IsType<CssInlineBoxAllocation>(head.CssInlineBox);
        var rect = Assert.Single(allocation.Fragments).Rect;
        var offset = head.TransformToAncestor(root);
        return new(rect.X + offset.X, rect.Y + offset.Y, rect.Width, rect.Height);
    }
    private static void NoOverlap(Rect initial, Border floating)
    {
        var rect = floating.VisualBounds;
        var width = Math.Min(initial.Right, rect.Right) - Math.Max(initial.Left, rect.Left);
        var height = Math.Min(initial.Bottom, rect.Bottom) - Math.Max(initial.Top, rect.Top);
        Assert.False(width > .001 && height > .001, $"Initial {initial} overlaps float {rect}");
    }

    [Theory]
    [InlineData(false, false, false)] [InlineData(false, true, false)]
    [InlineData(true, false, false)] [InlineData(true, true, false)]
    [InlineData(false, false, true)] [InlineData(false, true, true)]
    [InlineData(true, false, true)] [InlineData(true, true, true)]
    public void AnOriginatingLineStartFloatMovesOutboardOfTheInitial(bool rtl, bool before, bool clear)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var head = Head(); var floating = Float(40, 50, clear ? "clear:both;" : "");
        var root = before ? Root(rtl, floating, head, Text("brief " + Tail)) : Root(rtl, head, floating, Text("brief " + Tail));
        Layout(root); var rect = Initial(head, root); NoOverlap(rect, floating);
        Assert.Equal(0, floating.VisualBounds.Y, 3);
        Assert.Equal(rtl ? 272 : 0, floating.VisualBounds.X, 3);
        Assert.True(rtl ? rect.Right <= 272.001 : rect.Left >= 39.999);
    }

    [Theory]
    [InlineData(false, 312)] [InlineData(true, 312)]
    [InlineData(false, 160)] [InlineData(true, 160)]
    public void InitialAvoidanceChecksFloatsAcrossTheWholeInitialHeight(bool rtl, int width)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var first = Float(40, 50); var second = Float(width == 312 ? 80 : 120, 50, "clear:inline-start;");
        var head = Head(); var root = Root(rtl, first, second, head, Text("brief " + Tail));
        Layout(root, width); var rect = Initial(head, root); NoOverlap(rect, first); NoOverlap(rect, second);
        if (width == 312) Assert.True(rtl ? rect.Right <= width - 80 + .001 : rect.Left >= 80 - .001);
        else Assert.True(rect.Top >= 100, $"Initial and its originating line must move below both floats: {rect}");
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void AStartFloatOnALaterLineClearsTheSunkInitial(bool rtl, bool soft)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var head = Head(); var floating = Float(40, 35);
        var prefix = Text(soft ? "The first ordinary line continues through another short line " : "brief\n");
        var root = Root(rtl, head, prefix, floating, Text(Tail)); Layout(root);
        var rect = Initial(head, root); NoOverlap(rect, floating);
        Assert.True(floating.VisualBounds.Y >= rect.Bottom - .001, $"Later start float {floating.VisualBounds} did not clear initial {rect}");
        head.SelectAll(); Assert.Equal("A", head.SelectedText); Assert.Equal(16, head.FontSize);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InitialsAvoidFloatMarginBoxesEvenWhenTextCanUseTheirShape(bool rtl)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var floating = Float(80, 80, "shape-outside:circle(20px at 40px 40px);");
        var head = Head(); var root = Root(rtl, floating, head, Text(Tail)); Layout(root);
        var rect = Initial(head, root); NoOverlap(rect, floating);
        Assert.True(rtl ? rect.Right <= 232.001 : rect.Left >= 79.999);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InitialMovesBelowAWideMarginBoxEvenWhenItsShapeLeavesRoom(bool rtl)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var floating = Float(280, 80, "shape-outside:circle(10px at 140px 40px);");
        var head = Head(); var root = Root(rtl, floating, head, Text(Tail)); Layout(root);
        var rect = Initial(head, root); NoOverlap(rect, floating);
        Assert.True(rect.Top >= 80, $"Initial must move below the float margin box: {rect}");
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InitialAndFloatAvoidEachOthersMargins(bool rtl)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var floating = Float(80, 80, "margin:5px 10px 15px 6px;");
        var head = Head("margin:4px 6px 8px;"); var root = Root(rtl, floating, head, Text(Tail)); Layout(root);
        var rect = Initial(head, root);
        Assert.True(rtl ? rect.Right + 6 <= floating.VisualBounds.Left - 6 + .001
            : rect.Left - 6 >= floating.VisualBounds.Right + 10 - .001);
        NoOverlap(rect, floating);
    }

    [Theory]
    [InlineData(false, 1)] [InlineData(true, 1)]
    [InlineData(false, 2)] [InlineData(true, 2)]
    public void RaisedInitialsCheckTheWholeBoxAfterTheirOriginatingLineMoves(bool rtl, int sink)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var first = Float(40, 50); var second = Float(80, 50, "clear:inline-start;");
        var head = Head($"initial-letter:3 {sink};"); var root = Root(rtl, first, second, head, Text(Tail)); Layout(root);
        var rect = Initial(head, root); NoOverlap(rect, first); NoOverlap(rect, second);
        Assert.True(rtl ? rect.Right <= 232.001 : rect.Left >= 79.999);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AnAtomicInitialAvoidsTheFloatsAcrossItsEntireBox(bool rtl)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var first = Float(40, 50); var second = Float(80, 50, "clear:inline-start;");
        var head = new Border(); Css.SetStyle(head, "display:inline-block;initial-letter:3;initial-letter-wrap:none;width:60px;background:green;");
        var body = Text(Tail); var root = Root(rtl, first, second, head, body); Layout(root);
        var rect = head.VisualBounds; NoOverlap(rect, first); NoOverlap(rect, second);
        Assert.True(rtl ? rect.Right <= 232.001 : rect.Left >= 79.999);
    }

    [Theory]
    [InlineData(false, 1)] [InlineData(true, 1)]
    [InlineData(false, 2)] [InlineData(true, 2)]
    public void RaisedInitialsReplanTheTextAfterMovingTheirFirstLine(bool rtl, int sink)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var first = Float(40, 50); var second = Float(80, 50, "clear:inline-start;");
        var head = Head($"initial-letter:3 {sink};"); var body = Text("abc def ghi jkl mno pqr stu vwx yz abc def ghi jkl");
        var root = Root(rtl, first, second, head, body); Layout(root, 200);
        var offset = body.TransformToAncestor(root);
        foreach (var line in body.GetDocumentLines(body.RenderSize.Width))
        {
            foreach (var rect in body.DocumentTextBounds(line.Start, line.End - line.Start))
            {
                var left = offset.X + rect.Left; var right = offset.X + rect.Right;
                Assert.True(left >= -.001 && right <= 200.001, $"Text line escaped its replanned float band: {left}..{right}");
            }
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AStartFloatInTheFollowingBlockClearsTheExportedInitial(bool rtl)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var head = Head(); var first = Root(rtl, head, Text("brief"));
        var floating = Float(40, 35); var second = Root(rtl, floating, Text(Tail));
        var root = Root(rtl, first, second); Layout(root);
        var rect = Initial(head, root); var delta = floating.TransformToAncestor(root);
        Assert.True(delta.Y >= rect.Bottom - .001, $"Following-block float did not clear exported initial: {delta.Y} < {rect.Bottom}");
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EachWrappedInitialFragmentAvoidsFloatsAcrossItsWholeHeight(bool rtl)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var first = Float(40, 50); var second = Float(80, 60, "clear:inline-start;"); var third = Float(120, 50, "clear:inline-start;");
        var head = Head("width:90px;"); head.Text = "A A";
        var root = Root(rtl, first, second, third, head, Text(Tail)); Layout(root, 400);
        Assert.True(head.CssInlineBox!.Fragments.Length >= 2);
        var offset = head.TransformToAncestor(root);
        foreach (var fragment in head.CssInlineBox.Fragments)
        {
            var local = fragment.Rect; var rect = new Rect(local.X + offset.X, local.Y + offset.Y, local.Width, local.Height);
            NoOverlap(rect, first); NoOverlap(rect, second); NoOverlap(rect, third);
        }
    }
}
