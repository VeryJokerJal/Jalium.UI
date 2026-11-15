using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssInlineFloatTests
{
    private const string LongText = "甲乙丙丁戊己庚辛壬癸子丑寅卯辰巳午未申酉戌亥甲乙丙丁戊己庚辛壬癸子丑寅卯辰巳午未申酉戌亥甲乙丙丁戊己庚辛壬癸子丑寅卯辰巳午未申酉戌亥";

    [Fact]
    public void LinesUseTheLeftFloatBandThenRecoverTheFullContainingWidth()
    {
        var floating = Float("left", 40, 45); var text = Text(LongText); var root = Root(floating, text); Layout(root, 150);
        var lines = Lines(text); Assert.True(lines.Count > 4); Assert.Equal(40, lines[0].StartX, 4);
        NoOverlap(text, floating);
        Assert.Contains(lines, line => Top(text, line.Baseline) >= floating.VisualBounds.Bottom && Math.Abs(line.StartX) < .01);
        Assert.Equal(LongText, Draw(root)); Assert.Same(root, VisualTreeHelper.GetParent(text));
    }

    [Fact]
    public void RightFloatsReduceLineEndsOnlyWhileTheirVerticalRangeIsActive()
    {
        var floating = Float("right", 40, 45); var text = Text(LongText); var root = Root(floating, text); Layout(root, 160);
        var lines = Lines(text); Assert.Equal(120, floating.VisualBounds.X, 4); Assert.Equal(0, lines[0].StartX, 4);
        NoOverlap(text, floating);
        Assert.Contains(lines, line => Top(text, line.Baseline) >= floating.VisualBounds.Bottom && line.EndX > floating.VisualBounds.X);
    }

    [Fact]
    public void LeftAndRightFloatsCanExpireOnDifferentLines()
    {
        var left = Float("left", 40, 60); var right = Float("right", 30, 20); var text = Text(LongText); var root = Root(left, right, text); Layout(root, 160);
        var lines = Lines(text); Assert.Equal(40, lines[0].StartX, 4); Assert.True(lines[0].EndX <= right.VisualBounds.X + .01);
        NoOverlap(text, left); NoOverlap(text, right);
        Assert.Contains(lines, line => Top(text, line.Baseline) >= right.VisualBounds.Bottom && Top(text, line.Baseline) < left.VisualBounds.Bottom && line.EndX > right.VisualBounds.X);
        Assert.Contains(lines, line => Top(text, line.Baseline) >= left.VisualBounds.Bottom && Math.Abs(line.StartX) < .01);
    }

    [Fact]
    public void AnUnbreakableWordMovesBelowAFloatInsteadOfOverlappingIt()
    {
        var text = Text("HelloWorldHello"); var root = Root(text); Layout(root, 1000); var natural = Lines(text)[0].EndX;
        var floating = Float("left", 50, 45); root.Children.Insert(0, floating); Layout(root, natural + 20);
        var line = Assert.Single(Lines(text)); Assert.True(Top(text, line.Baseline) >= floating.VisualBounds.Bottom - .01); Assert.Equal(0, line.StartX, 4);
        NoOverlap(text, floating);
    }

    [Fact]
    public void EmergencyWrappingStillFitsTheAvailableFloatBand()
    {
        var floating = Float("left", 65, 45); var text = Text("ABCDEFGHIJKLMNOPQRSTUVWXYZ"); var root = Root(floating, text);
        Css.SetStyle(text, "display: inline; overflow-wrap: anywhere;"); Layout(root, 90);
        Assert.True(Lines(text).Count > 3); Assert.Equal(65, Lines(text)[0].StartX, 4); NoOverlap(text, floating);
    }

    [Fact]
    public void AFloatEncounteredAfterWrappedTextDoesNotRiseAboveItsSourceLine()
    {
        var first = Text(LongText[..42]); var floating = Float("left", 40, 45); var second = Text(LongText); var root = Root(first, floating, second); Layout(root, 150);
        Assert.True(Lines(first).Count > 1); Assert.True(floating.VisualBounds.Y >= Top(first, Lines(first)[^1].Baseline) - .01);
        NoOverlap(first, floating); NoOverlap(second, floating);
        Assert.Equal(first.Text + second.Text, Draw(root));
    }

    [Fact]
    public void AFloatInsideACrossSourceWordDoesNotSplitTheWordOrPullTheFloatUp()
    {
        var first = Text("prefix hello"); var floating = Float("left", 40, 40); var second = Text("world tail"); var root = Root(first, floating, second); Layout(root, 115);
        var a = first.GetSourceRangeSegments(7, 5); var b = second.GetSourceRangeSegments(0, 5);
        Assert.Single(a); Assert.Single(b); Assert.Equal(a[0].Baseline + first.VisualBounds.Y, b[0].Baseline + second.VisualBounds.Y, 4);
        Assert.True(floating.VisualBounds.Y >= Top(first, a[0].Baseline + first.VisualBounds.Y) - .01);
        NoOverlap(first, floating); NoOverlap(second, floating);
    }

    [Fact]
    public void AFontSizeIncreaseRechecksTheWholeLineHeightAgainstFloats()
    {
        var first = Text("甲"); var floating = Float("left", 145, 45); var second = Text(""); second.Inlines.Add(new Run("乙丙丁戊己庚辛") { FontSize = 40 });
        var root = Root(first, floating, second); Layout(root, 150);
        Assert.True(floating.VisualBounds.Y >= first.VisualBounds.Bottom - .01);
        Assert.True(second.VisualBounds.Y >= floating.VisualBounds.Bottom - .01);
        Assert.Equal("甲乙丙丁戊己庚辛", Draw(root));
    }

    [Fact]
    public void ClearBlocksStartBelowFloatsAndSplitTheFollowingInlineParagraph()
    {
        var floating = Float("left", 40, 65); var first = Text("前"); var block = new Border { Height = 8 }; var last = Text("后");
        Css.SetStyle(block, "display: block; clear: both;"); var root = Root(floating, first, block, last); Layout(root, 160);
        Assert.True(block.VisualBounds.Y >= floating.VisualBounds.Bottom - .01); Assert.True(last.VisualBounds.Y >= block.VisualBounds.Bottom - .01);
        Assert.True(Lines(first)[0].Baseline < Lines(last)[0].Baseline);
    }

    [Theory]
    [InlineData("block", true)]
    [InlineData("flow-root", false)]
    public void ADescendantSharesExclusionsOnlyWhenItSharesTheFormattingContext(string display, bool shares)
    {
        var floating = Float("left", 50, 60); var text = Text(LongText); var child = Root(text); Css.SetStyle(child, "display: " + display + ";");
        var root = Root(floating, child); Layout(root, 160);
        if (shares) { Assert.Equal(0, child.VisualBounds.X, 4); Assert.Equal(50, Lines(text)[0].StartX, 4); Assert.Contains(Lines(text), line => Top(text, line.Baseline) >= 60 && line.StartX < .01); }
        else { Assert.Equal(50, child.VisualBounds.X, 4); Assert.Equal(0, Lines(text)[0].StartX, 4); Assert.InRange(child.ActualWidth, 0, 110.01); }
    }

    [Fact]
    public void TextBlockBodyAndItsInlineTailWrapAroundItsFloatedPseudo()
    {
        var text = Text(LongText); Css.SetClass(text, "text");
        var sheet = CssStyleSheet.Parse(".text { display: block; } .text::before { content: '侧栏'; float: left; width: 40px; height: 45px; } .text::after { content: '尾注'; }");
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(text).Add(sheet); Layout(text, 160);
        var slots = CssGeneratedContent.Get(text)!; Assert.Equal(40, text.GetSourceRangeSegments(0, text.Text.Length)[0].StartX, 4);
        var body = text.GetSourceRangeSegments(0, text.Text.Length); var tail = slots.After!.GetSourceRangeSegments(0, 2)[0];
        Assert.Equal(body[^1].Baseline, tail.Baseline + slots.After.VisualBounds.Y, 4);
        Assert.Equal("侧栏" + text.Text + "尾注", Draw(text)); text.Select(2, 5); Assert.Equal(text.Text.Substring(2, 5), text.SelectedText);
    }

    [Fact]
    public void ResizingChangingFloatsAndReplayingCachesRestoresTheActualLineBands()
    {
        var floating = Float("left", 40, 45); var first = Text(LongText[..22]); var second = Text(LongText); var root = Root(floating, first, second);
        Layout(root, 150); var expected = Lines(second).ToArray(); Layout(root, 240); Layout(root, 150); Assert.Equal(expected, Lines(second));
        Css.SetStyle(floating, "float: right; width: 60px; height: 70px;"); Layout(root, 150); NoOverlap(first, floating); NoOverlap(second, floating);
        Css.SetStyle(floating, "float: none; display: none;"); Layout(root, 150); Assert.Equal(0, Lines(first)[0].StartX, 4);
        Assert.Equal(first.Text + second.Text, Draw(root));
    }

    [Fact]
    public void FloatMarginsAndOwnerInsetsAreIncludedOnceInTheAvailableBand()
    {
        var floating = Float("left", 40, 45); Css.SetStyle(floating, "float: left; width: 40px; height: 45px; margin: 3px 7px 5px 2px;");
        var text = Text(LongText); var root = Root(floating, text); Css.SetStyle(root, "display: flow-root; padding: 6px; border: 2px solid black; font-size: 16px;"); Layout(root, 180);
        Assert.Equal(10, floating.VisualBounds.X, 4); Assert.Equal(11, floating.VisualBounds.Y, 4);
        Assert.Equal(floating.VisualBounds.Right + 7, Lines(text)[0].StartX, 4); NoOverlap(text, floating);
        Assert.Contains(Lines(text), line => Top(text, line.Baseline) >= floating.VisualBounds.Bottom + 5 && Math.Abs(line.StartX - 8) < .01);
    }

    [Theory]
    [InlineData("right")]
    [InlineData("center")]
    public void TextAlignmentUsesEachCurrentExclusionBand(string alignment)
    {
        var floating = Float("left", 40, 45); var text = Text("甲乙"); var root = Root(floating, text);
        Css.SetStyle(root, "display: flow-root; font-size: 16px; text-align: " + alignment + ";"); Layout(root, 160);
        var line = Assert.Single(Lines(text));
        if (alignment == "right") Assert.Equal(160, line.EndX, 4); else Assert.Equal(200, line.StartX + line.EndX, 4);
        NoOverlap(text, floating);
    }

    [Fact]
    public void DirectionalFloatsResolveAgainstTheParentDirection()
    {
        var floating = Float("inline-start", 40, 45); var text = Text(LongText); var root = Root(floating, text); root.FlowDirection = FlowDirection.RightToLeft; Layout(root, 160);
        Assert.Equal(120, floating.VisualBounds.X, 4); Assert.Equal(120, Lines(text)[0].EndX, 4); NoOverlap(text, floating);
        Assert.Contains(Lines(text), line => Top(text, line.Baseline) >= 45 && Math.Abs(line.EndX - 160) < .01);
    }

    [Fact]
    public void PointHitsAndSelectionFollowTheFragmentsOnBothSidesOfTheFloatBottom()
    {
        var floating = Float("left", 45, 50); var first = Text(LongText[..22]); var second = Text(LongText); var root = Root(floating, first, second); Layout(root, 160);
        foreach (var text in new[] { first, second }) foreach (var line in Lines(text))
        {
            var point = new Point(line.StartX + (line.EndX - line.StartX) * .2, line.Baseline - 2);
            Assert.Same(text, root.InputHitTest(point)); Assert.Same(text, VisualTreeHelper.HitTest(root, point)?.VisualHit);
            var local = new Point(point.X - text.VisualBounds.X, point.Y - text.VisualBounds.Y);
            var pointer = text.GetPositionFromPoint(local, true)!; var index = text.ContentStart.GetOffsetToPosition(pointer);
            Assert.InRange(index, 0, text.Text.Length - 1); text.Select(index, 1); Assert.Equal(text.Text.Substring(index, 1), text.SelectedText);
            Assert.Equal(line.Baseline, text.GetSourceRangeSegments(index, 1)[0].Baseline + text.VisualBounds.Y, 4);
        }
        Assert.Same(floating, root.InputHitTest(new Point(floating.VisualBounds.X + 5, floating.VisualBounds.Y + 5)));
    }

    [Fact]
    public void SameWidthTextUpdatesKeepTheirNewSourceRangesAndFloatExclusions()
    {
        var floating = Float("left", 45, 50); var first = Text("甲乙丙丁戊己"); var second = Text(LongText); var root = Root(floating, first, second); Layout(root, 160);
        foreach (var value in new[] { "庚辛壬癸子丑", "甲乙丙丁戊己", "庚辛壬癸子丑" })
        {
            first.Text = value; Layout(root, 160); Assert.Equal(value + second.Text, Draw(root)); NoOverlap(first, floating); NoOverlap(second, floating);
        }
    }

    [Fact]
    public void OversizedFloatsMakeProgressAndTextStartsBelowTheirBottom()
    {
        var floating = Float("left", 240, 45); var text = Text(LongText); var root = Root(floating, text); Layout(root, 100);
        Assert.Equal(240, floating.ActualWidth, 4); Assert.True(Top(text, Lines(text)[0].Baseline) >= floating.VisualBounds.Bottom - .01);
        Assert.Equal(0, Lines(text)[0].StartX, 4); NoOverlap(text, floating);
    }

    [Fact]
    public void FloatsAfterAHardBreakStartOnTheFollowingLine()
    {
        var first = Text("甲\n"); var floating = Float("left", 40, 45); var second = Text(LongText); var root = Root(first, floating, second);
        Css.SetStyle(root, "display: flow-root; font-size: 16px; white-space: pre-wrap;"); Layout(root, 160);
        Assert.True(floating.VisualBounds.Y >= first.VisualBounds.Bottom - .01); Assert.True(Lines(second)[0].Baseline > Lines(first)[0].Baseline);
        NoOverlap(second, floating);
    }

    [Fact]
    public void MinAndMaxContentContributionsAreSymmetricForLeftAndRightFloats()
    {
        var text = Text("HelloWorld"); var root = Root(text); Layout(root, 1000); var wordWidth = Lines(text)[0].EndX;
        var floating = Float("left", 40, 45); root.Children.Insert(0, floating); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
        var left = new CssLayoutBox(root, 0); left.MeasureIntrinsicWidth();
        Css.SetStyle(floating, "float: right; width: 40px; height: 45px;"); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
        var right = new CssLayoutBox(root, 0); right.MeasureIntrinsicWidth();
        Assert.Equal(wordWidth + 40, left.MaxContent.Width, 3); Assert.Equal(left.MaxContent.Width, right.MaxContent.Width, 3);
        Assert.Equal(left.MinContent.Width, right.MinContent.Width, 3); Assert.True(right.MinContent.Width >= Math.Max(40, wordWidth) - .01);
        Layout(root, 160); NoOverlap(text, floating);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public void FloatOnlyContentStillContributesItsWholeUnconstrainedRow(string side)
    {
        var root = Root(Float(side, 40, 45), Float(side, 30, 40));
        CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
        var box = new CssLayoutBox(root, 0); box.MeasureIntrinsicWidth();
        Assert.Equal(70, box.MaxContent.Width, 3); Assert.Equal(40, box.MinContent.Width, 3);
    }

    [Fact]
    public void OriginalTextAndInlinePseudosAlsoFollowBorrowedAncestorFloats()
    {
        var floating = Float("left", 50, 65); var text = Text(LongText); Css.SetClass(text, "text");
        var root = Root(floating, text); Css.SetStyle(text, "display: block; padding: 4px;");
        var sheet = CssStyleSheet.Parse(".text::before { content: '前'; } .text::after { content: '后'; }");
        Css.GetStyleSheets(root).Add(sheet); Layout(root, 160);
        Assert.Equal(0, text.VisualBounds.X, 4); Assert.True(Lines(text)[0].StartX > 50); NoOverlap(text, floating);
        Assert.Contains(Lines(text), line => Top(text, line.Baseline) >= floating.VisualBounds.Bottom && Math.Abs(line.StartX - 4) < .01);
        var after = CssGeneratedContent.Get(text)!.After!;
        Assert.Equal(after.VisualBounds.Y + after.GetLastBaselineOffset(after.ActualHeight), text.GetLastBaselineOffset(text.ActualHeight), 4);
        Assert.Equal("前" + text.Text + "后", Draw(root));
    }

    [Theory]
    [InlineData("border")]
    [InlineData("content")]
    [InlineData("text")]
    public void GenericBlockHostsExportTheirFloatsToFollowingSiblingText(string kind)
    {
        FrameworkElement host; FrameworkElement floating;
        if (kind == "text")
        {
            var owner = Text("前"); Css.SetClass(owner, "owner"); host = owner;
            var sheet = CssStyleSheet.Parse(".owner::before { content: ''; float: left; width: 40px; height: 70px; }");
            Css.GetStyleSheets(owner).Add(sheet); Css.SetStyle(owner, "display: block;");
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); floating = CssGeneratedContent.Get(owner)!.Before!;
        }
        else
        {
            floating = Float("left", 40, 70);
            host = kind == "border" ? new Border { Child = floating } : new ContentControl { Content = floating };
            Css.SetStyle(host, "display: block;");
        }
        var text = Text(LongText); var root = Root(host, text); Css.SetStyle(host, "display: block;"); Layout(root, 160);
        Assert.Equal(40, Lines(text)[0].StartX, 4); NoOverlap(text, floating);
        Assert.Contains(Lines(text), line => Top(text, line.Baseline) >= 70 && line.StartX < .01);
        Assert.Equal(CssFloatSide.Left, CssDisplayLayout.FlowFor(host).ExportedFloats[0].Side);
    }

    private static TextBlock Text(string value) => new() { Text = value, FontSize = 16, Foreground = Brushes.Black, IsTextSelectionEnabled = true, VerticalAlignment = VerticalAlignment.Top };
    private static Border Float(string side, double width, double height)
    { var element = new Border(); Css.SetStyle(element, $"float: {side}; width: {width}px; height: {height}px;"); return element; }
    private static StackPanel Root(params UIElement[] elements)
    { var root = new StackPanel(); foreach (var element in elements) { root.Children.Add(element); if (element is TextBlock) Css.SetStyle(element, "display: inline;"); } Css.SetStyle(root, "display: flow-root; font-size: 16px;"); return root; }
    private static void Layout(FrameworkElement root, double width)
    { for (var i = 0; i < 5; i++) { CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); root.Measure(new Size(width, 1200)); root.Arrange(new Rect(0, 0, width, Math.Max(1, root.DesiredSize.Height))); root.UpdateLayout(); } }
    private static IReadOnlyList<(double StartX, double EndX, double Baseline)> Lines(TextBlock text) => text.GetSourceRangeSegments(0, text.Text.Length)
        .Select(line => (line.StartX + text.VisualBounds.X, line.EndX + text.VisualBounds.X, line.Baseline + text.VisualBounds.Y)).ToArray();
    private static double Top(TextBlock text, double baseline) => baseline - CssFlowProperties.LineMetrics(text).Baseline;
    private static void NoOverlap(TextBlock text, FrameworkElement floating)
    {
        var metrics = CssFlowProperties.LineMetrics(text); var box = floating.VisualBounds;
        foreach (var line in Lines(text))
        {
            var top = line.Baseline - metrics.Baseline;
            if (top + metrics.Height <= box.Y + .01 || top >= box.Bottom - .01) continue;
            Assert.True(line.EndX <= box.X + .01 || line.StartX >= box.Right - .01,
                $"Text [{line.StartX}, {line.EndX}] at y={top} overlaps float {box}");
        }
    }
    private static string Draw(FrameworkElement root) { var dc = new Capture(); root.Render(dc); return string.Concat(dc.Text); }
    private sealed class Capture : DrawingContextAdapter, IOffsetDrawingContext
    {
        public Point Offset { get; set; }
        internal readonly List<string> Text = [];
        public override void DrawText(FormattedText text, Point origin) => Text.Add(text.Text);
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource source, Rect rect) { }
        public override void PushClip(Geometry clip) { }
        public override void PushOpacity(double opacity) { }
        public override void PushTransform(Transform transform) { }
        public override void Pop() { }
        public override void Close() { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
    }
}
