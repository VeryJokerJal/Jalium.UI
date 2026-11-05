using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssNestedInlineTests
{
    private const string LongText = "嵌套行内容器中的原始文字继续换行以后仍然可以选择和更新";

    [Fact]
    public void BorderContentSharesItsOuterParagraphWithoutReparenting()
    {
        var lead = Text("前置"); var body = Text(LongText); var tail = Text("结尾"); var border = InlineBorder(body);
        var root = Root(lead, border, tail); Layout(root, 138);
        Assert.True(Lines(body, root).Count > 2); Assert.Equal(Lines(lead, root)[0].Baseline, Lines(body, root)[0].Baseline, 4);
        Assert.Equal(Lines(body, root)[^1].Baseline, Lines(tail, root)[0].Baseline, 4);
        Assert.Same(body, border.Child); Assert.Same(border, body.VisualParent); Assert.Equal(3, root.Children.Count);
        Assert.Equal(lead.Text + body.Text + tail.Text, Draw(root));
        CheckPoints(root, body);
    }

    [Fact]
    public void MultipleNestedContainersPayTheirEdgesOnceAndShareAllLineBoxes()
    {
        var a = Text("甲乙"); var b = Text(LongText); var c = Text("收尾"); var inner = InlineBorder(b, "padding: 0 5px; border: 1px solid #126542;");
        var middle = InlinePanel(a, inner, c); Css.SetStyle(middle, "display: inline; padding: 0 3px; border: 2px solid #126542;");
        var outer = InlineBorder(middle, "padding: 0 7px; border: 1px solid #126542;"); var lead = Text("前"); var tail = Text("后");
        var root = Root(lead, outer, tail); Layout(root, 1000);
        Assert.Equal(Lines(lead, root)[0].EndX + 13, Lines(a, root)[0].StartX, 4);
        Assert.Equal(Lines(a, root)[0].EndX + 6, Lines(b, root)[0].StartX, 4);
        Assert.Equal(Lines(b, root)[^1].EndX + 6, Lines(c, root)[0].StartX, 4);
        Assert.Equal(Lines(c, root)[0].EndX + 13, Lines(tail, root)[0].StartX, 4);
        Layout(root, 145); Assert.True(Lines(b, root).Count > 2);
        Assert.Equal(lead.Text + a.Text + b.Text + c.Text + tail.Text, Draw(root)); CheckPoints(root, a, b, c);
    }

    [Theory]
    [InlineData("slice", false)]
    [InlineData("clone", true)]
    public void DecorationBreakReservesBothSidesOfEachClonedLine(string mode, bool clone)
    {
        var text = Text(LongText); var frame = InlineBorder(text, $"padding: 0 8px; border: 2px solid #126542; margin: 0 3px; box-decoration-break: {mode};");
        var root = Root(frame); Layout(root, 111); var lines = Lines(text, root); Assert.True(lines.Count > 2);
        Assert.Equal(13, lines[0].StartX, 4);
        foreach (var line in lines.Skip(1)) Assert.Equal(clone ? 13 : 0, line.StartX, 4);
        Assert.True(lines.All(line => line.EndX <= 111 - (clone ? 13 : 0) + .001));
        var paint = Paint(root); Assert.True(paint.Geometries.Count >= lines.Count * 2);
        CheckPoints(root, text);
    }

    [Fact]
    public void InlineDimensionsDoNotReplaceTheSharedTextAllocation()
    {
        var text = Text(LongText); var frame = InlineBorder(text, "width: 900px; height: 800px; min-width: 700px; min-height: 600px; max-width: 710px;");
        var root = Root(frame); Layout(root, 120); Assert.True(Lines(text, root).Count > 1); Assert.True(frame.ActualWidth < 150); Assert.True(frame.ActualHeight < 200);
        Css.SetStyle(frame, "display: inline-block; width: 200px; height: 70px;"); Layout(root, 120);
        Assert.Equal(200, frame.ActualWidth, 4); Assert.Equal(70, frame.ActualHeight, 4);
        Css.SetStyle(frame, "display: inline;"); Layout(root, 120); Assert.True(Lines(text, root).Count > 1); Assert.True(frame.ActualWidth <= 120);
    }

    [Fact]
    public void AWordDoesNotAcquireBreaksAtNestedContainerBoundaries()
    {
        var a = Text("Hel"); var b = Text("lo"); var c = Text("World"); var frame = InlineBorder(b); var root = Root(a, frame, c);
        Layout(root, 1000); var natural = Lines(c, root)[0].EndX; Layout(root, natural * .7);
        Assert.Single(Lines(a, root)); Assert.Single(Lines(b, root)); Assert.Single(Lines(c, root));
        Assert.Equal(Lines(a, root)[0].Baseline, Lines(c, root)[0].Baseline, 4); Assert.Equal(natural, Lines(c, root)[0].EndX, 4);
        Assert.Equal("HelloWorld", Draw(root));
    }

    [Fact]
    public void OriginalRichTextAndGraphemeSelectionSurviveNestedWrappingAndUpdates()
    {
        var text = Text(""); var run = new Run("👩‍💻e\u0301原始富文本继续换行") { Foreground = Brushes.Blue, FontSize = 18 }; text.Inlines.Add(run);
        var frame = InlineBorder(text, "padding: 0 3px; overflow-wrap: anywhere;"); var root = Root(Text("前置"), frame, Text("尾")); Layout(root, 90);
        Assert.Same(run, Assert.Single(text.Inlines)); Assert.True(Lines(text, root).Count > 1); text.Select(0, 5); Assert.Equal("👩‍💻", text.SelectedText);
        CheckPoints(root, text);
        run.Text = "更新以后仍然保留同一个Run对象"; Layout(root, 90); Assert.Same(run, Assert.Single(text.Inlines)); Assert.Contains(run.Text, Draw(root));
    }

    [Fact]
    public void FragmentEnvelopesDoNotStealSiblingOrParentHits()
    {
        var lead = Text("前置文字"); var body = Text(LongText); var tail = Text("最后"); var frame = InlineBorder(body, "background: #dff6e9; border: 1px solid #126542; padding: 0 5px;");
        var root = Root(lead, frame, tail); Layout(root, 144); CheckPoints(root, lead, body, tail);
        var p = new Point(Lines(lead, root)[0].StartX + 1, Lines(lead, root)[0].Baseline - 2);
        Assert.Same(lead, root.InputHitTest(p)); Assert.Same(lead, VisualTreeHelper.HitTest(root, p)?.VisualHit);
        var found = new List<DependencyObject>(); VisualTreeHelper.HitTest(root, null, hit => { found.Add(hit.VisualHit); return HitTestResultBehavior.Continue; }, new PointHitTestParameters(p));
        Assert.DoesNotContain(frame, found);
    }

    [Fact]
    public void NestedParagraphHitsUseLocalCoordinatesBelowBlockHeadingsAndPaddedRows()
    {
        var root = new StackPanel(); Css.SetClass(root, "root"); var sources = new List<TextBlock>();
        for (var i = 0; i < 3; i++)
        {
            var row = new StackPanel(); Css.SetClass(row, "row"); root.Children.Add(row);
            var lead = Text("外层文字："); var body = Text(LongText); var tail = Text("尾声"); sources.AddRange([lead, body, tail]);
            row.Children.Add(lead); row.Children.Add(InlineBorder(InlinePanel(InlineBorder(body, "padding: 0 3px; border-left: 1px solid green; border-right: 1px solid green;")), "padding: 0 5px; border: 1px solid green; border-radius: 4px; background: #def0e5;")); row.Children.Add(tail);
        }
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".root { display: block; padding: 20px; font-size: 16px; } .root::before { content: '标题'; display: block; margin-bottom: 18px; } .row { display: flow-root; padding: 14px; margin-bottom: 18px; border: 1px solid green; line-height: 1.85; } .row::before { content: '行标题'; display: block; font-size: 18px; margin-bottom: 8px; }"));
        Layout(root, 375); CheckPoints(root, sources.ToArray());
    }

    [Fact]
    public void NestedVerticalAlignmentMovesTheWholeSubtree()
    {
        var lead = Text("基线"); var body = Text("提升"); var frame = InlineBorder(body, "vertical-align: 7px; padding: 10px 3px;"); var tail = Text("返回"); var root = Root(lead, frame, tail);
        Layout(root, 1000); var outer = Lines(lead, root)[0]; var inner = Lines(body, root)[0];
        Assert.Equal(outer.Baseline - 7, inner.Baseline, 4); Assert.Equal(outer.Baseline, Lines(tail, root)[0].Baseline, 4);
        Css.SetStyle(body, "display: inline; vertical-align: 4px;"); Layout(root, 1000);
        Assert.Equal(Lines(lead, root)[0].Baseline - 11, Lines(body, root)[0].Baseline, 4);
    }

    [Theory]
    [InlineData("top")]
    [InlineData("bottom")]
    [InlineData("middle")]
    public void NestedAlignmentUsesTheDescendantMetrics(string alignment)
    {
        var small = Text("小"); var large = Text("大字"); large.ClearValue(TextBlock.FontSizeProperty); Css.SetStyle(large, "display: inline; font-size: 32px;");
        var frame = InlineBorder(large, $"vertical-align: {alignment}; font-size: 12px;"); var root = Root(small, frame, Text("后")); Layout(root, 250);
        Assert.True(root.DesiredSize.Height >= 32); CheckPoints(root, small, large); Assert.Equal("小大字后", Draw(root));
    }

    [Fact]
    public void VerticalPaddingDoesNotIncreaseEveryLineBox()
    {
        var text = Text(LongText); var frame = InlineBorder(text); var root = Root(frame); Layout(root, 100); var normal = Lines(text, root);
        Css.SetStyle(frame, "display: inline; padding: 30px 0; border-top: 2px solid green; border-bottom: 2px solid green;"); Layout(root, 100); var padded = Lines(text, root);
        Assert.Equal(normal.Count, padded.Count);
        for (var i = 1; i < normal.Count; i++) Assert.Equal(normal[i].Baseline - normal[0].Baseline, padded[i].Baseline - padded[0].Baseline, 4);
    }

    [Fact]
    public void IntrinsicProbesAndWidthReplayRestoreEveryNestedSource()
    {
        var body = Text(LongText); var frame = InlineBorder(body, "padding: 0 5px; border: 1px solid green;"); var tail = Text("尾声"); var root = Root(Text("前"), frame, tail);
        Layout(root, 103); var expected = Lines(body, root).ToArray(); var box = new CssLayoutBox(root, 0); box.MeasureIntrinsicWidth();
        Assert.True(box.MinContent.Width < box.MaxContent.Width);
        foreach (var width in new[] { 184d, 103d, 184d, 103d }) { Layout(root, width); CheckPoints(root, body, tail); Assert.Equal("前" + body.Text + tail.Text, Draw(root)); }
        Assert.Equal(expected, Lines(body, root));
    }

    [Fact]
    public void ReparentingAnInlineSubtreeReleasesItsOldParagraph()
    {
        var body = Text(LongText); var frame = InlineBorder(body); var first = Root(Text("旧"), frame); Layout(first, 90);
        first.Children.Remove(frame); var second = Root(Text("新"), frame); Layout(second, 180);
        Assert.Equal("新" + body.Text, Draw(second)); Assert.Equal("旧", Draw(first)); CheckPoints(second, body);
        Css.SetStyle(frame, "display: none;"); Layout(second, 180); Assert.Equal("新", Draw(second));
        Css.SetStyle(frame, "display: inline;"); Layout(second, 180); Assert.Equal("新" + body.Text, Draw(second)); CheckPoints(second, body);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public void NestedFloatsShareTheOuterExclusionSpace(string side)
    {
        var floating = new Border { Background = Brushes.Green }; Css.SetStyle(floating, $"float: {side}; width: 34px; height: 48px; margin: 0 3px;");
        var body = Text(LongText); var container = InlinePanel(floating, body); var tail = Text("外部继续环绕"); var root = Root(Text("前"), container, tail);
        Css.SetStyle(root, "display: flow-root; font-size: 16px;"); Layout(root, 150); var fp = floating.TransformToAncestor(root);
        var obstacle = new Rect(fp.X, fp.Y, floating.ActualWidth, floating.ActualHeight);
        foreach (var text in new[] { body, tail }) foreach (var line in Lines(text, root))
            if (line.Baseline > obstacle.Top && line.Baseline - 15 < obstacle.Bottom)
                Assert.True(line.EndX <= obstacle.Left + .01 || line.StartX >= obstacle.Right - .01);
        Assert.True(floating.ActualWidth > 0); Assert.True(root.DesiredSize.Height >= obstacle.Bottom); CheckPoints(root, body, tail);
        var original = Lines(body, root).ToArray(); var box = new CssLayoutBox(root, 0); box.MeasureIntrinsicWidth(); Layout(root, 230); Layout(root, 150);
        Assert.Equal(original, Lines(body, root)); CheckPoints(root, body, tail);
    }

    [Fact]
    public void StylesheetInlineFlowHostsActivateNestedFloatsAfterTreeAttachment()
    {
        var floating = new Border { Child = new TextBlock { Text = "浮动" }, Background = Brushes.Green }; Css.SetClass(floating, "float");
        var body = Text(LongText); var inner = new Border { Child = body }; Css.SetClass(inner, "inner");
        var content = new StackPanel(); Css.SetClass(content, "content"); content.Children.Add(floating); content.Children.Add(inner);
        var outer = new Border { Child = content }; Css.SetClass(outer, "outer"); var root = Root(Text("前"), outer, Text("后"));
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".outer, .content, .inner { display: inline; } .float { float: left; width: 35px; height: 45px; margin: 0 5px; }"));
        Layout(root, 140); Assert.True(content.IsCssInlineFrame); Assert.True(body.IsCssInlineBox); CheckPoints(root, body);
        Assert.True(Lines(body, root).Count > 2); Assert.InRange(floating.ActualWidth, 34.99, 35.01);
    }

    [Fact]
    public void InlineTextWithItsOwnPseudoElementsJoinsTheOuterParagraph()
    {
        var body = Text(LongText); body.Name = "nested";
        var sheet = CssStyleSheet.Parse("#nested::before { content: '【'; display: inline; } #nested::after { content: '】'; display: inline; }");
        var lead = Text("前置"); var tail = Text("尾"); var root = Root(lead, body, tail); Css.GetStyleSheets(root).Add(sheet); Layout(root, 130);
        Assert.True(body.IsCssInlineFrame); Assert.True(Lines(body, root).Count > 2); Assert.Equal(lead.Text + "【" + body.Text + "】" + tail.Text, Draw(root));
        Assert.Equal(Lines(lead, root)[0].Baseline, Lines(body, root)[0].Baseline, 4); CheckPoints(root, lead, body, tail);
        body.Select(2, 4); Assert.Equal(body.Text.Substring(2, 4), body.SelectedText);
    }

    [Fact]
    public void EmptyDecoratedInlinePreservesSpacingWithoutAddingTextOrLogicalChildren()
    {
        var empty = InlinePanel(); Css.SetStyle(empty, "display: inline; padding: 0 4px; border: 1px solid green;");
        var a = Text("甲"); var b = Text("乙"); var root = Root(a, empty, b); Layout(root, 100);
        Assert.Equal(Lines(a, root)[0].EndX + 10, Lines(b, root)[0].StartX, 4); Assert.Empty(empty.Children); Assert.Equal("甲乙", Draw(root));
        Assert.Equal(Lines(a, root)[0].Baseline, Lines(b, root)[0].Baseline, 4);
    }

    [Fact]
    public void EmptyUndecoratedInlineTreesDoNotCreateBlankLines()
    {
        var empty = InlinePanel(InlinePanel()); var root = Root(empty); Layout(root, 150);
        Assert.Equal(0, root.DesiredSize.Height, 4); Assert.Empty(Draw(root));
        Css.SetStyle(empty, "display: inline; padding: 0 5px; border: 1px solid green;"); Layout(root, 150);
        Assert.True(root.DesiredSize.Height > 0); Assert.Equal(12, empty.ActualWidth, 4);
    }

    [Fact]
    public void PercentagePaddingUsesTheOuterContainingBlock()
    {
        var body = Text("正文"); var frame = InlineBorder(body, "padding: 0 10%;"); var root = Root(Text("前"), frame, Text("后")); Layout(root, 300);
        Assert.Equal(Lines((TextBlock)root.Children[0], root)[0].EndX + 30, Lines(body, root)[0].StartX, 4);
    }

    [Fact]
    public void PercentageBackgroundClipsRestoreTheirBasisAfterIntrinsicProbes()
    {
        var body = Text(LongText); Css.SetStyle(body, "display: inline; padding: 0 3%; background: #e2f0e8; background-clip: content-box; box-decoration-break: clone;");
        var frame = InlineBorder(body, "padding: 0 7%; border: 1px solid green; background: #d3eadc; background-clip: content-box; box-decoration-break: clone;");
        var root = Root(Text("前"), frame, Text("后")); Layout(root, 150); var original = Paint(root).Clips.ToArray(); Assert.NotEmpty(original);
        new CssLayoutBox(root, 0).MeasureIntrinsicWidth(); Layout(root, 260); Layout(root, 150);
        Assert.Equal(original, Paint(root).Clips); CheckPoints(root, body);
    }

    [Fact]
    public void RelativeInlineOffsetsMoveDescendantsWithoutChangingFlow()
    {
        var body = Text(LongText); var frame = InlineBorder(body); var tail = Text("尾"); var root = Root(Text("前"), frame, tail); Layout(root, 130);
        var before = Lines(body, root).ToArray(); var tailBefore = Lines(tail, root).ToArray();
        Css.SetStyle(frame, "display: inline; position: relative; left: 6px; top: 4px;"); Layout(root, 130);
        var after = Lines(body, root); Assert.Equal(before.Length, after.Count);
        for (var i = 0; i < after.Count; i++) { Assert.Equal(before[i].StartX + 6, after[i].StartX, 4); Assert.Equal(before[i].Baseline + 4, after[i].Baseline, 4); }
        Assert.Equal(tailBefore, Lines(tail, root)); CheckPoints(root, body);
    }

    [Fact]
    public void HardBreaksKeepNestedFrameEdgesAndSourceOffsets()
    {
        var body = Text("甲乙\n丙丁\n戊己"); Css.SetStyle(body, "display: inline; white-space: pre-wrap;"); var frame = InlineBorder(body, "padding: 0 5px; border: 1px solid green;");
        var root = Root(Text("前"), frame, Text("后")); Layout(root, 200);
        Assert.Equal(3, Lines(body, root).Count); Assert.Equal(0, Lines(body, root)[1].StartX, 4); CheckPoints(root, body);
    }

    [Fact]
    public void ContentPresenterCanExposeAnInlineContentTree()
    {
        var body = Text(LongText); var presenter = new ContentPresenter { Content = body }; Css.SetStyle(presenter, "display: inline;");
        var tail = Text("后"); var root = Root(Text("前"), presenter, tail); Layout(root, 120);
        Assert.True(Lines(body, root).Count > 2); Assert.True(Lines(body, root)[^1].Baseline <= Lines(tail, root)[0].Baseline);
        Assert.True(Lines(tail, root)[0].Baseline == Lines(body, root)[^1].Baseline || Lines(body, root)[^1].EndX + (Lines(tail, root)[0].EndX - Lines(tail, root)[0].StartX) > 120);
        Assert.Same(body, presenter.Content); CheckPoints(root, body);
    }

    [Fact]
    public void NestedTextKeepsTabOrderFocusAndItsOriginalAutomationName()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var a = Text("第一段"); var b = Text(LongText); var c = Text("第三段");
        foreach (var text in new[] { a, b, c }) { text.Focusable = true; KeyboardNavigation.SetIsTabStop(text, true); }
        var frame = InlineBorder(InlinePanel(b), "padding: 0 5px;"); var root = Root(a, frame, c);
        var window = new DisplayedTestWindow { Content = root, Width = 180, Height = 300, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            Layout(window, 180); Assert.True(a.Focus()); Assert.True(KeyboardNavigation.MoveFocus(a)); Assert.Same(b, Keyboard.FocusedElement);
            Assert.Equal(b.Text, b.GetAutomationPeer()!.GetName());
            Css.SetStyle(frame, "display: inline; padding: 0 8px; box-decoration-break: clone;"); Layout(window, 180); Assert.Same(b, Keyboard.FocusedElement);
            Assert.True(KeyboardNavigation.MoveFocus(b)); Assert.Same(c, Keyboard.FocusedElement);
            Assert.True(KeyboardNavigation.MoveFocus(c, reverse: true)); Assert.Same(b, Keyboard.FocusedElement);
            Assert.True(KeyboardNavigation.MoveFocus(b, reverse: true)); Assert.Same(a, Keyboard.FocusedElement);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(b); i++) Assert.Null(((UIElement)VisualTreeHelper.GetChild(b, i)!).GetAutomationPeer());
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Theory]
    [InlineData("slice", false)]
    [InlineData("clone", true)]
    public void RightToLeftFramesKeepTheirPhysicalEdgesAndSourceHits(string mode, bool clone)
    {
        var body = Text(LongText); var frame = InlineBorder(body, $"padding: 0 6px; border: 1px solid green; box-decoration-break: {mode};"); var lead = Text("前"); var tail = Text("后");
        var root = Root(lead, frame, tail); Css.SetStyle(root, "display: block; direction: rtl; font-size: 16px;"); Layout(root, 125);
        var lines = Lines(body, root); Assert.True(lines.Count > 2);
        if (Jalium.UI.Interop.RenderContext.Current is { IsValid: true })
            Assert.True(Lines(lead, root)[0].EndX <= lines[0].StartX + .001); // CJK is bidi class L.
        else Assert.True(lines[0].EndX < Lines(lead, root)[0].StartX);
        Assert.Equal(125 - (clone ? 7 : 0), lines[1].EndX, 4); CheckPoints(root, lead, body, tail); Assert.Equal(lead.Text + body.Text + tail.Text, Draw(root));
    }

    [Fact]
    public void WhitespaceCollapsesAcrossNestedSourcesAndEmptyFrames()
    {
        var a = Text("a "); var b = Text("  b "); var c = Text(" c"); var frame = InlineBorder(InlinePanel(InlinePanel(), b)); var root = Root(a, frame, c);
        Layout(root, 180); Assert.Equal("a b c", Draw(root)); Assert.Equal("  b ", b.Text); CheckPoints(root, b);
    }

    [Fact]
    public void NativeSelfDrawingSubclassesKeepTheirOwnPaintAndAtomicContent()
    {
        var body = Text(LongText); var custom = new CustomPanel(); custom.Children.Add(body); Css.SetStyle(custom, "display: inline; width: 95px;");
        var root = Root(Text("前"), custom, Text("后")); Layout(root, 150); Paint(root);
        Assert.False(custom.IsCssInlineFrame); Assert.True(custom.Paints > 0); Assert.Equal(95, custom.ActualWidth, 4); Assert.Same(body, Assert.Single(custom.Children));
    }

    private sealed class CustomPanel : StackPanel
    {
        internal int Paints;
        protected override void OnRender(DrawingContext context) { base.OnRender(context); Paints++; context.DrawRectangle(Brushes.Red, null, new Rect(2, 2, 5, 5)); }
    }

    private static TextBlock Text(string value)
    {
        var text = new TextBlock { Text = value, FontSize = 16, Foreground = Brushes.Black, IsTextSelectionEnabled = true };
        Css.SetStyle(text, "display: inline;"); return text;
    }
    private static Border InlineBorder(UIElement child, string style = "")
    {
        var border = new Border { Child = child }; Css.SetStyle(border, "display: inline; " + style); return border;
    }
    private static StackPanel InlinePanel(params UIElement[] children)
    {
        var panel = new StackPanel(); foreach (var child in children) panel.Children.Add(child); Css.SetStyle(panel, "display: inline;"); return panel;
    }
    private static StackPanel Root(params UIElement[] children)
    {
        var panel = new StackPanel(); foreach (var child in children) panel.Children.Add(child); Css.SetStyle(panel, "display: block; font-size: 16px;"); return panel;
    }
    private static void Layout(FrameworkElement root, double width)
    {
        for (var i = 0; i < 5; i++) { CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); root.Measure(new(width, 1000)); root.Arrange(new(0, 0, width, Math.Max(1, root.DesiredSize.Height))); root.UpdateLayout(); }
    }
    private static IReadOnlyList<(double StartX, double EndX, double Baseline)> Lines(TextBlock text, FrameworkElement root)
    {
        var offset = text.TransformToAncestor(root);
        return text.GetSourceRangeSegments(0, text.Text.Length).Select(line => (line.StartX + offset.X, line.EndX + offset.X, line.Baseline + offset.Y)).ToArray();
    }
    private static void CheckPoints(FrameworkElement root, params TextBlock[] sources)
    {
        foreach (var text in sources)
        {
            var offset = text.TransformToAncestor(root);
            foreach (var line in Lines(text, root))
            {
                if (line.EndX - line.StartX < .1) continue;
                var point = new Point((line.StartX + line.EndX) / 2, line.Baseline - 2);
                var hit = root.InputHitTest(point);
                Assert.True(ReferenceEquals(text, hit), $"Expected {text.Text}, hit {(hit as TextBlock)?.Text} at {point}; lines={string.Join(';', Lines(text, root))}");
                Assert.Same(text, root.HitTest(point)?.VisualHit);
                var local = new Point(point.X - offset.X, point.Y - offset.Y);
                Assert.Same(text, text.HitTest(local)?.VisualHit);
                var index = text.ContentStart.GetOffsetToPosition(text.GetPositionFromPoint(local, true)!);
                Assert.InRange(index, 0, text.Text.Length);
                var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(text.Text);
                var start = boundaries.Last(boundary => boundary <= Math.Min(index, text.Text.Length - 1));
                var end = boundaries.FirstOrDefault(boundary => boundary > start, text.Text.Length);
                text.Select(start, end - start); Assert.Equal(text.Text.Substring(start, end - start), text.SelectedText);
            }
        }
    }
    private static Capture Paint(FrameworkElement root) { var dc = new Capture(); root.Render(dc); return dc; }
    private static string Draw(FrameworkElement root) => string.Concat(Paint(root).Text);
    private sealed class Capture : DrawingContextAdapter, IOffsetDrawingContext
    {
        public Point Offset { get; set; }
        internal readonly List<string> Text = [];
        internal readonly List<Rect> Geometries = [];
        internal readonly List<Rect> Clips = [];
        public override void DrawText(FormattedText text, Point origin) => Text.Add(text.Text);
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) => Geometries.Add(geometry.Bounds);
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawImage(ImageSource image, Rect rect) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
        public override void Close() { }
        public override void PushClip(Geometry clip) => Clips.Add(clip.Bounds);
        public override void PushOpacity(double opacity) { }
        public override void PushTransform(Transform transform) { }
        public override void Pop() { }
    }
}
