using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSharedInlineTests
{
    [Fact]
    public void SiblingTextSourcesShareLinesAndContinueAtTheirLastFragment()
    {
        var a = Text("甲乙丙丁戊己庚辛"); var b = Text("壬癸子丑寅卯辰巳午未"); var c = Text("尾");
        var root = Root(a, b, c); Layout(root, 1000);
        var natural = Segments(a)[0].EndX;
        Layout(root, natural * .93);
        var first = Segments(a); var body = Segments(b); var tail = Assert.Single(Segments(c));
        Assert.True(first.Count > 1); Assert.True(body.Count > 1);
        Assert.Equal(first[^1].Baseline, body[0].Baseline, 4);
        Assert.Equal(first[^1].EndX, body[0].StartX, 4);
        Assert.Equal(body[^1].Baseline, tail.Baseline, 4); Assert.Equal(body[^1].EndX, tail.StartX, 4);
        Assert.Equal(a.Text + b.Text + c.Text, Draw(root));
        Assert.Equal(new[] { a, b, c }, root.Children.Cast<TextBlock>());
        Assert.Same(root, VisualTreeHelper.GetParent(b)); Assert.Same(root, LogicalTreeHelper.GetParent(b));
    }

    [Fact]
    public void SourceBoundariesNeitherSplitWordsNorCombiningClusters()
    {
        var a = Text("hel"); var b = Text("lo world"); var root = Root(a, b); Layout(root, 1000);
        var width = Segments(a)[0].EndX; Layout(root, width + 1);
        Assert.Equal(Segments(a)[0].Baseline, Segments(b)[0].Baseline, 4);
        Assert.True(Segments(b)[0].EndX > width + 1); Assert.Equal(2, Segments(b).Count);
        a.Text = "e"; b.Text = "\u0301👩‍💻甲乙";
        Css.SetStyle(root, "display: block; overflow-wrap: anywhere;"); Layout(root, 1);
        Assert.Equal(Segments(a)[0].Baseline, Segments(b)[0].Baseline, 4);
        Assert.Contains("👩‍💻", Draw(root));
    }

    [Fact]
    public void CollapsingSpaceSpansSiblingsWithoutChangingTheirOriginalText()
    {
        var a = Text("  甲  "); var b = Text("  乙  "); var c = Text(" 丙  ");
        var root = Root(a, b, c); Layout(root, 300);
        Assert.Equal("甲 乙 丙", Draw(root));
        a.SelectAll(); b.SelectAll(); Assert.Equal("  甲  ", a.SelectedText); Assert.Equal("  乙  ", b.SelectedText);
    }

    [Fact]
    public void HardBreaksResetTheSharedLineWithoutResettingSourceOffsets()
    {
        var a = Text("甲\n乙"); var b = Text("丙丁"); var root = Root(a, b);
        Css.SetStyle(root, "display: block; white-space: pre-wrap;"); Layout(root, 300);
        var first = Segments(a); var second = Assert.Single(Segments(b)); Assert.Equal(2, first.Count);
        Assert.Equal(first[^1].Baseline, second.Baseline, 4); Assert.Equal(first[^1].EndX, second.StartX, 4);
        var point = new Point(first[1].StartX - a.VisualBounds.X + .1, first[1].Baseline - a.VisualBounds.Y - 2);
        Assert.Equal(2, a.ContentStart.GetOffsetToPosition(a.GetPositionFromPoint(point, true)!));
    }

    [Fact]
    public void RichInlineInstancesAndSelectionRemainOwnedByEachOriginalTextBlock()
    {
        var a = Text("前置文字"); var b = Text(""); var run = new Run("原始富文本换行以后仍可选择") { Foreground = Brushes.Blue, FontSize = 18 };
        b.Inlines.Add(run); var root = Root(a, b); Layout(root, 110);
        Assert.Same(run, Assert.Single(b.Inlines)); var lines = Segments(b); Assert.True(lines.Count > 1);
        var point = new Point(lines[1].StartX - b.VisualBounds.X + .1, lines[1].Baseline - b.VisualBounds.Y - 2);
        var index = b.ContentStart.GetOffsetToPosition(b.GetPositionFromPoint(point, true)!);
        Assert.InRange(index, 1, b.Text.Length - 1); b.Select(index, 2); Assert.Equal(b.Text.Substring(index, 2), b.SelectedText);
        a.Select(0, 1); Assert.Equal("前", a.SelectedText);
        run.Text = "更新以后依然是同一个对象"; Layout(root, 110);
        Assert.Same(run, Assert.Single(b.Inlines)); Assert.Contains(run.Text, Draw(root));
    }

    [Fact]
    public void AFragmentUnionDoesNotStealItsSiblingsPointerHits()
    {
        var a = Text("甲乙丙丁"); var b = Text("壬癸子丑寅卯辰巳午未申酉"); var root = Root(a, b);
        Layout(root, 1000); var width = Segments(a)[0].EndX * 1.6; Layout(root, width);
        Assert.True(Segments(b).Count > 1);
        var line = Segments(a)[0]; var point = new Point((line.StartX + line.EndX) / 2, line.Baseline - 2);
        Assert.True(b.VisualBounds.Contains(point));
        Assert.Same(a, root.InputHitTest(point)); Assert.Same(a, root.HitTest(point)?.VisualHit);
        Assert.Same(a, VisualTreeHelper.HitTest(root, point)?.VisualHit);
        DependencyObject? hit = null;
        VisualTreeHelper.HitTest(root, _ => HitTestFilterBehavior.Continue, result => { hit = result.VisualHit; return HitTestResultBehavior.Stop; }, new PointHitTestParameters(point));
        Assert.Same(a, hit);
        var body = Segments(b)[^1]; Assert.Same(b, root.InputHitTest(new Point((body.StartX + body.EndX) / 2, body.Baseline - 2)));
    }

    [Theory]
    [InlineData("slice")]
    [InlineData("clone")]
    public void DecorationsFragmentAndOwnerInsetsAreAppliedExactlyOnce(string mode)
    {
        var a = Text("前"); var b = Text("甲乙丙丁戊己庚辛壬癸"); var c = Text("后"); var root = Root(a, b, c);
        Css.SetStyle(root, "display: block; padding: 7px; border: 2px solid black;");
        Css.SetStyle(b, $"display: inline; padding: 2px 4px; border: 1px solid green; background: lightgreen; box-decoration-break: {mode};");
        Layout(root, 1000); var natural = Segments(b)[0].EndX - Segments(b)[0].StartX;
        Layout(root, natural * .7 + 18); var lines = Segments(b); Assert.True(lines.Count > 1);
        Assert.Equal(9, Segments(a)[0].StartX, 4);
        Assert.Equal(Segments(a)[0].EndX + 5, lines[0].StartX, 4);
        Assert.Equal(mode == "clone" ? 14 : 9, lines[1].StartX, 4);
        var capture = new Capture(); root.Render(capture);
        Assert.Equal(lines.Count, capture.Rectangles.Count(static item => item.Brush is SolidColorBrush b && b.Color == Colors.LightGreen));
        Assert.Equal(a.Text + b.Text + c.Text, string.Concat(capture.Text));
    }

    [Fact]
    public void NonReplacedInlineSizesDoNotOverrideTheSharedLineGeometry()
    {
        var a = Text("甲"); var b = Text("乙"); var root = Root(a, b);
        Css.SetStyle(a, "display: inline; width: 500px; height: 200px; min-width: 600px; min-height: 300px;"); Layout(root, 200);
        Assert.InRange(a.ActualWidth, 1, 50); Assert.InRange(a.ActualHeight, 1, 50);
        Assert.Equal(Segments(a)[0].EndX, Segments(b)[0].StartX, 4);
    }

    [Fact]
    public void WidthReplaysAndIntrinsicMeasuresRestoreTheCorrectCachedFragments()
    {
        var a = Text("甲乙丙丁戊己庚辛"); var b = Text("壬癸子丑寅卯辰巳午未"); var root = Root(a, b);
        Layout(root, 1000); var cell = Segments(a)[0].EndX / a.Text.Length; var narrow = cell * 6.5;
        Layout(root, narrow); var expected = Segments(b).ToArray(); var bounds = b.VisualBounds;
        Layout(root, cell * 24); Assert.True(Segments(b).Count < expected.Length, $"narrow={expected.Length}, wide={Segments(b).Count}");
        root.Measure(new Size(0, 500)); root.Measure(new Size(double.PositiveInfinity, 500));
        Layout(root, narrow); Assert.Equal(expected, Segments(b)); Assert.Equal(bounds, b.VisualBounds);
        Assert.Equal(a.Text + b.Text, Draw(root));
    }

    [Fact]
    public void EqualWidthSourceChangesAndPaddingChangesInvalidateTheWholeParagraph()
    {
        var a = Text("甲乙"); var b = Text("丙丁戊己庚辛壬癸"); var c = Text("尾"); var root = Root(a, b, c); Layout(root, 100);
        foreach (var content in new[] { "子丑寅卯辰巳午未", "丙丁戊己庚辛壬癸", "子丑寅卯辰巳午未" })
        { b.Text = content; Layout(root, 100); Assert.Equal(a.Text + content + c.Text, Draw(root)); }
        var x = Segments(b)[0].StartX;
        Css.SetStyle(a, "display: inline; padding-left: 11px;"); Layout(root, 100);
        Assert.Equal(x + 11, Segments(b)[0].StartX, 4);
        Css.SetStyle(a, "display: inline;"); Layout(root, 100); Assert.Equal(x, Segments(b)[0].StartX, 4);
    }

    [Fact]
    public void BlockSiblingsSeparateParagraphsAndKeepTheirMarginsAndBaselines()
    {
        var a = Text("甲"); var block = new Border { Height = 20 }; var b = Text("乙"); var c = Text("丙");
        var root = Root(a, block, b, c); Css.SetStyle(block, "display: block; margin-top: 7px; margin-bottom: 9px;"); Layout(root, 200);
        var first = Segments(a)[0]; var second = Segments(b)[0];
        Assert.Equal(a.VisualBounds.Bottom + 7, block.VisualBounds.Y, 4);
        Assert.Equal(block.VisualBounds.Bottom + 9, b.VisualBounds.Y, 4);
        Assert.Equal(second.Baseline, Segments(c)[0].Baseline, 4);
        Assert.True(CssDisplayLayout.TryGetBaseline(root, root.ActualHeight, false, out var baseline)); Assert.Equal(first.Baseline, baseline, 4);
        Assert.True(CssDisplayLayout.TryGetBaseline(root, root.ActualHeight, true, out baseline)); Assert.Equal(second.Baseline, baseline, 4);
    }

    [Fact]
    public void AtomicControlsRetainTheirNativeChildAndSingleBox()
    {
        var a = Text("前"); var body = Text("按钮"); var atomic = new Border { Child = body, Width = 45, Height = 30 }; var b = Text("后正文换行继续");
        Css.SetStyle(atomic, "display: inline-block; vertical-align: middle;"); var root = Root(a, atomic, b); Layout(root, 100);
        Assert.Equal(45, atomic.ActualWidth, 4); Assert.Equal(30, atomic.ActualHeight, 4); Assert.Same(body, atomic.Child);
        Assert.Same(atomic, VisualTreeHelper.GetParent(body)); Assert.True(atomic.VisualBounds.X >= Segments(a)[0].EndX);
        Assert.Contains("前按钮后正文换行继续", Draw(root));
    }

    [Theory]
    [InlineData("center")]
    [InlineData("right")]
    public void AlignmentMovesTheCompleteSharedLine(string alignment)
    {
        var a = Text("甲"); var b = Text("乙"); var root = Root(a, b); Css.SetStyle(root, $"display: block; text-align: {alignment};"); Layout(root, 240);
        var first = Segments(a)[0]; var second = Segments(b)[0]; Assert.True(first.StartX > 50);
        Assert.Equal(first.EndX, second.StartX, 4); Assert.Equal(first.Baseline, second.Baseline, 4);
        if (alignment == "right") Assert.Equal(240, second.EndX, 4);
    }

    [Fact]
    public void RightToLeftDirectionPlacesSiblingsFromTheRightEdge()
    {
        var a = Text("甲"); var b = Text("乙"); var root = Root(a, b); root.FlowDirection = FlowDirection.RightToLeft; Layout(root, 240);
        var first = Segments(a)[0]; var second = Segments(b)[0];
        Assert.Equal(240, first.EndX, 4); Assert.Equal(second.EndX, first.StartX, 4);
    }

    [Fact]
    public void ReparentingAndDisplaySwitchesReleaseTheBorrowedFragmentLayout()
    {
        var a = Text("甲乙丙"); var b = Text("丁戊己庚辛壬癸"); var root = Root(a, b); Layout(root, 100); Assert.True(b.IsCssInlineBox);
        Css.SetStyle(b, "display: inline-block; width: 90px;"); Layout(root, 100); Assert.False(b.IsCssInlineBox); Assert.Equal(90, b.ActualWidth, 4);
        Css.SetStyle(b, "display: inline;"); Layout(root, 100); Assert.True(b.IsCssInlineBox);
        Css.SetStyle(root, "display: flex;"); Layout(root, 240); Assert.False(b.IsCssInlineBox);
        Css.SetStyle(root, "display: block;"); Layout(root, 100); Assert.True(b.IsCssInlineBox);
        root.Children.Remove(b); var native = new Border { Child = b }; Css.SetStyle(b, "display: block;"); Layout(native, 200);
        Assert.False(b.IsCssInlineBox); Assert.Equal(0, Segments(b)[0].StartX, 4); Assert.Equal(b.Text, Draw(native));
        Assert.InRange(VisualTreeHelper.GetDescendantBounds(b).Width, 0, 200.01);
    }

    [Fact]
    public void GeneratedHostSiblingsShareTheSameParagraphAsOriginalChildren()
    {
        var a = Text("原始正文"); var b = Text("继续排版"); var root = Root(a, b); Css.SetClass(root, "host");
        var sheet = CssStyleSheet.Parse(".host::before { content: '前'; } .host::after { content: '后'; }");
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(root).Add(sheet); Layout(root, 300);
        var slots = CssGeneratedContent.Get(root)!;
        Assert.Equal(Segments(slots.Before!)[0].EndX, Segments(a)[0].StartX, 4);
        Assert.Equal(Segments(b)[0].EndX, Segments(slots.After!)[0].StartX, 4);
        Assert.Equal("前原始正文继续排版后", Draw(root)); Assert.Equal(2, root.Children.Count);
    }

    [Fact]
    public void TogglingCloneRecomputesEveryContinuationEdge()
    {
        var a = Text("甲"); var b = Text("乙丙丁戊己庚辛壬癸子丑寅"); var root = Root(a, b);
        Css.SetStyle(b, "display: inline; padding: 0 7px; box-decoration-break: slice;"); Layout(root, 85);
        Assert.True(Segments(b).Count > 1); Assert.Equal(0, Segments(b)[1].StartX, 4);
        Css.SetStyle(b, "display: inline; padding: 0 7px; box-decoration-break: clone;"); Layout(root, 85);
        Assert.Equal(7, Segments(b)[1].StartX, 4);
        Css.SetStyle(b, "display: inline; padding: 0 7px; box-decoration-break: slice;"); Layout(root, 85);
        Assert.Equal(0, Segments(b)[1].StartX, 4); Assert.Equal(a.Text + b.Text, Draw(root));
    }

    [Fact]
    public void PercentageInlineEdgesResolveAgainstTheOriginalContainingBlock()
    {
        var a = Text("甲"); var b = Text("乙"); var root = Root(a, b);
        Css.SetStyle(a, "display: inline; padding-left: 10%; margin-right: 5%;"); Layout(root, 200);
        Assert.Equal(20, Segments(a)[0].StartX, 4); Assert.Equal(Segments(a)[0].EndX + 10, Segments(b)[0].StartX, 4);
        Layout(root, 300); Assert.Equal(30, Segments(a)[0].StartX, 4); Assert.Equal(Segments(a)[0].EndX + 15, Segments(b)[0].StartX, 4);
    }

    [Fact]
    public void RelativePositionMovesAllSourceFragmentsWithoutReflowingItsSibling()
    {
        var a = Text("甲乙丙丁戊己庚辛"); var b = Text("壬癸子丑寅卯"); var root = Root(a, b); Layout(root, 90);
        var first = Segments(a).ToArray(); var second = Segments(b).ToArray();
        Css.SetStyle(a, "display: inline; position: relative; left: 7px; top: 3px;"); Layout(root, 90);
        Assert.Equal(second, Segments(b)); var moved = Segments(a); Assert.Equal(first.Length, moved.Count);
        for (var i = 0; i < first.Length; i++) { Assert.Equal(first[i].StartX + 7, moved[i].StartX, 4); Assert.Equal(first[i].Baseline + 3, moved[i].Baseline, 4); }
    }

    [Fact]
    public void NoWrapAndDisplayNoneDoNotLeaveBorrowedFragmentsBehind()
    {
        var a = Text("甲乙丙丁戊己庚辛"); var b = Text("壬癸子丑寅卯"); var root = Root(a, b);
        Css.SetStyle(root, "display: block; white-space: nowrap;"); Layout(root, 70);
        Assert.Single(Segments(a)); Assert.Single(Segments(b)); Assert.Equal(Segments(a)[0].EndX, Segments(b)[0].StartX, 4);
        Css.SetStyle(b, "display: none;"); Layout(root, 70); Assert.DoesNotContain(b.Text, Draw(root));
        Css.SetStyle(root, "display: block;"); Css.SetStyle(b, "display: inline;"); Layout(root, 70);
        Assert.True(Segments(a).Count > 1); Assert.Equal(a.Text + b.Text, Draw(root));
    }

    [Fact]
    public void GroupAlignmentAndLinePaddingMoveAllSourcesTogetherAcrossCacheReplays()
    {
        var a = Text("甲乙"); var b = Text("丙丁"); var root = Root(a, b);
        Css.SetStyle(root, "display: block; text-group-align: center; line-padding: 3px;"); Layout(root, 240);
        var first = Segments(a)[0]; var second = Segments(b)[0];
        Assert.True(first.StartX > 50); Assert.Equal(first.EndX, second.StartX, 4); Assert.Equal(240, first.StartX + second.EndX, 4);
        Layout(root, 300); Layout(root, 240); Assert.Equal(first, Segments(a)[0]); Assert.Equal(second, Segments(b)[0]);
    }

    [Fact]
    public void FirstLineAndForcedBreakIndentApplyToTheSharedParagraph()
    {
        var a = Text("甲\n乙"); var b = Text("丙丁"); var root = Root(a, b);
        Css.SetStyle(root, "display: block; white-space: pre-wrap; text-indent: 17px each-line;"); Layout(root, 240);
        var lines = Segments(a); Assert.Equal(2, lines.Count); Assert.Equal(17, lines[0].StartX, 4); Assert.Equal(17, lines[1].StartX, 4);
        Assert.Equal(lines[1].EndX, Segments(b)[0].StartX, 4);
    }

    [Fact]
    public void NativeLayoutTransformsKeepTheirOriginalAtomicMeasurement()
    {
        var a = Text("甲乙丙丁戊己"); var b = Text("庚辛壬癸"); var root = Root(a, b);
        a.LayoutTransform = new RotateTransform(15); Layout(root, 90);
        Assert.False(a.IsCssInlineBox); Assert.True(b.IsCssInlineBox); Assert.True(a.ActualWidth > 0); Assert.Contains(a.Text + b.Text, Draw(root));
    }

    [Fact]
    public void EmptyInlineSourcesDoNotIntroduceAnExtraLine()
    {
        var a = Text(""); var b = Text("甲乙"); var c = Text(""); var root = Root(a, b, c); Layout(root, 200);
        Assert.Equal(b.VisualBounds.Bottom, root.DesiredSize.Height, 4); Assert.Equal("甲乙", Draw(root));
        Assert.NotSame(c, root.InputHitTest(new Point(0, 0)));
        Assert.NotSame(c, VisualTreeHelper.HitTest(root, new Point(0, 0))?.VisualHit);
        b.Text = ""; Layout(root, 200); Assert.Equal(0, root.DesiredSize.Height);
    }

    [Fact]
    public void OriginalTextSourcesKeepTheirKeyboardFocusAndAutomationNames()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus(); var a = Text("第一段"); var b = Text("第二段换行内容"); a.Focusable = b.Focusable = true;
        KeyboardNavigation.SetIsTabStop(a, true); KeyboardNavigation.SetIsTabStop(b, true);
        var root = Root(a, b); var window = new DisplayedTestWindow { Content = root, Width = 240, Height = 240, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            Layout(window, 240); Assert.True(a.Focus()); Assert.True(KeyboardNavigation.MoveFocus(a)); Assert.Same(b, Keyboard.FocusedElement);
            Assert.Equal(b.Text, b.GetAutomationPeer()!.GetName()); a.Text = "修改前段内容"; Layout(window, 240); Assert.Same(b, Keyboard.FocusedElement);
            Assert.True(KeyboardNavigation.MoveFocus(b, reverse: true)); Assert.Same(a, Keyboard.FocusedElement);
            foreach (var text in new[] { a, b }) for (var i = 0; i < VisualTreeHelper.GetChildrenCount(text); i++)
                Assert.Null(((UIElement)VisualTreeHelper.GetChild(text, i)!).GetAutomationPeer());
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void OnlyTheInnermostSourceAtEachLineEdgeSuppliesLinePadding()
    {
        var a = Text("甲乙"); var b = Text("丙丁"); var root = Root(a, b);
        Css.SetStyle(root, "display: block; line-padding: 3px;"); Css.SetStyle(b, "display: inline; line-padding: 9px;"); Layout(root, 200);
        Assert.Equal(3, Segments(a)[0].StartX, 4); Assert.Equal(Segments(a)[0].EndX, Segments(b)[0].StartX, 4);
        Assert.Equal(Segments(b)[0].EndX + 9, b.VisualBounds.Right, 4);
        Assert.Equal(Segments(a)[0].EndX, a.VisualBounds.Right, 4);
    }

    [Fact]
    public void RightToLeftLinePaddingIsAppliedAtThePhysicalLineEdges()
    {
        var a = Text("甲"); var b = Text("乙"); var root = Root(a, b); root.FlowDirection = FlowDirection.RightToLeft;
        Css.SetStyle(root, "display: block; line-padding: 3px;"); Layout(root, 240);
        Assert.Equal(237, Segments(a)[0].EndX, 4); Assert.Equal(Segments(a)[0].StartX, Segments(b)[0].EndX, 4);
        Assert.Equal(b.VisualBounds.X + 3, Segments(b)[0].StartX, 4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForcedBreakAfterAnotherSourceKeepsOnlyTheRealLineEndPadding(bool atomic)
    {
        UIElement a = atomic ? new Border { Width = 20, Height = 20 } : Text("甲");
        var b = Text("\n乙"); var root = Root(a, b);
        if (atomic) Css.SetStyle(a, "display: inline-block;");
        Css.SetStyle(root, "display: block; white-space: pre-wrap; line-padding: 3px;"); Layout(root, 240);
        Assert.Single(Segments(b)); Assert.Equal(3, Segments(b)[0].StartX, 4); Assert.True(b.VisualBounds.Bottom > a.VisualBounds.Bottom);
        if (!atomic) Assert.Equal(Segments((TextBlock)a)[0].EndX, ((TextBlock)a).VisualBounds.Right, 4);
    }

    [Fact]
    public void IntrinsicWidthsMeasureWordsAcrossTheSiblingBoundary()
    {
        var a = Text("hel"); var b = Text("lo world"); var root = Root(a, b);
        Css.SetStyle(root, "display: block; padding: 5px;"); Layout(root, 1000);
        var first = Assert.Single(a.GetSourceRangeSegments(0, 3)); var continuation = Assert.Single(b.GetSourceRangeSegments(0, 2));
        var word = Assert.Single(b.GetSourceRangeSegments(3, 5));
        var expected = Math.Max(first.EndX - first.StartX + continuation.EndX - continuation.StartX, word.EndX - word.StartX) + 10;
        root.Measure(new Size(0, 500));
        var box = new CssLayoutBox(root, 0); box.MeasureIntrinsicWidth();
        Assert.Equal(expected, box.MinContent.Width, 3); Assert.True(box.MaxContent.Width > box.MinContent.Width);
        Layout(root, 1000); Assert.Equal("hello world", Draw(root));
    }

    [Theory]
    [InlineData("break-word", false)]
    [InlineData("anywhere", true)]
    public void IntrinsicProbeDoesNotReuseTheNormalEmergencyWrappingSnapshot(string wrap, bool fragments)
    {
        var a = Text("abcdefghij"); var b = Text("klmnopqrst"); var root = Root(a, b);
        Css.SetStyle(root, $"display: block; overflow-wrap: {wrap};"); Layout(root, 1000);
        var natural = Segments(b)[0].EndX; root.Measure(new Size(0, 500));
        var box = new CssLayoutBox(root, 0); box.MeasureIntrinsicWidth();
        Assert.True(box.MinContent.Width > 0);
        if (fragments) Assert.True(box.MinContent.Width < natural / 2); else Assert.Equal(natural, box.MinContent.Width, 3);
        Assert.Equal(natural, box.MaxContent.Width, 3);
        Layout(root, 1000); Assert.Equal(a.Text + b.Text, Draw(root));
    }

    [Fact]
    public void NoWrapTextDoesNotPreventBreakingAroundAnAdjacentAtomicBox()
    {
        var a = Text("甲乙"); var atomic = new Border { Width = 12, Height = 12 }; var b = Text("丙丁");
        var root = Root(a, atomic, b); Css.SetStyle(atomic, "display: inline-block;");
        Css.SetStyle(a, "display: inline; white-space: nowrap;"); Css.SetStyle(b, "display: inline; white-space: nowrap;"); Layout(root, 1000);
        var width = Segments(a)[0].EndX + 1; Layout(root, width);
        Assert.Single(Segments(a)); Assert.Single(Segments(b)); Assert.True(atomic.VisualBounds.Y >= a.VisualBounds.Bottom);
        Css.SetStyle(root, "display: block; white-space: nowrap;"); Layout(root, width);
        Assert.True(atomic.VisualBounds.Y < a.VisualBounds.Bottom); Assert.Equal(Segments(a)[0].Baseline, Segments(b)[0].Baseline, 4);
    }

    [Fact]
    public void AnEmergencyWrappingSiblingDoesNotSplitANoWrapTextSource()
    {
        var a = Text("ABCDEFGHIJK"); var b = Text("正文继续换行"); var root = Root(a, b);
        Css.SetStyle(a, "display: inline; white-space: nowrap; overflow-wrap: anywhere;"); Layout(root, 25);
        Assert.Single(Segments(a)); Assert.True(Segments(a)[0].EndX > 25); Assert.True(Segments(b).Count > 1);
    }

    [Fact]
    public void ALongUnbrokenSiblingSequenceKeepsItsWholeWordIntrinsicWidth()
    {
        var a = Text(new string('x', 12000)); var b = Text("tail"); var root = Root(a, b);
        CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
        var box = new CssLayoutBox(root, 0); box.MeasureIntrinsicWidth();
        Assert.True(box.MinContent.Width > 10000); Assert.Equal(box.MinContent.Width, box.MaxContent.Width, 3);
        Layout(root, 200); Assert.Single(Segments(a)); Assert.Single(Segments(b));
        Assert.Equal(Segments(a)[0].EndX, Segments(b)[0].StartX, 4);
        Assert.Equal(Segments(b)[0].EndX, box.MaxContent.Width, 3);
    }

    private static TextBlock Text(string value) => new() { Text = value, FontSize = 16, Foreground = Brushes.Black, IsTextSelectionEnabled = true, VerticalAlignment = VerticalAlignment.Top };
    private static Panel Root(params UIElement[] elements)
    {
        var root = new StackPanel(); foreach (var element in elements) { root.Children.Add(element); if (element is TextBlock) Css.SetStyle(element, "display: inline;"); }
        Css.SetStyle(root, "display: block;"); return root;
    }
    private static void Layout(FrameworkElement root, double width)
    {
        for (var i = 0; i < 5; i++) { CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); root.Measure(new Size(width, 1000)); root.Arrange(new Rect(0, 0, width, Math.Max(1, root.DesiredSize.Height))); root.UpdateLayout(); }
    }
    private static IReadOnlyList<(double StartX, double EndX, double Baseline)> Segments(TextBlock text) =>
        text.GetSourceRangeSegments(0, text.Text.Length).Select(line => (line.StartX + text.VisualBounds.X, line.EndX + text.VisualBounds.X, line.Baseline + text.VisualBounds.Y)).ToArray();
    private static string Draw(FrameworkElement root) { var dc = new Capture(); root.Render(dc); return string.Concat(dc.Text); }
    private sealed class Capture : DrawingContextAdapter, IOffsetDrawingContext
    {
        public Point Offset { get; set; }
        internal readonly List<string> Text = [];
        internal readonly List<(Brush? Brush, Rect Rect)> Rectangles = [];
        public override void DrawText(FormattedText text, Point origin) => Text.Add(text.Text);
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) => Rectangles.Add((brush, rect));
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
