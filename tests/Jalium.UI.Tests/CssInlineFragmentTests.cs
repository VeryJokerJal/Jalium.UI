using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssInlineFragmentTests
{
    [Fact]
    public void BeforeBodyAndAfterContinueOnTheSameLineAndFragmentAcrossLines()
    {
        var text = Text("正文跨行保留原始文本与选择偏移", 100);
        Add(text, ".text::before { content: '前置'; } .text::after { content: '尾注'; }"); Layout(text);
        var before = CssGeneratedContent.Get(text)!.Before!; var after = CssGeneratedContent.Get(text)!.After!;
        var head = before.GetSourceRangeSegments(0, before.Text.Length);
        var body = text.GetSourceRangeSegments(0, text.Text.Length);
        var tail = after.GetSourceRangeSegments(0, after.Text.Length);
        Assert.True(body.Count > 1); Assert.Equal(head[0].Baseline + before.VisualBounds.Y, body[0].Baseline, 4);
        Assert.Equal(head[0].EndX + before.VisualBounds.X, body[0].StartX, 4);
        Assert.Equal(0, body[1].StartX, 4);
        Assert.Equal(body[^1].Baseline, tail[0].Baseline + after.VisualBounds.Y, 4);
        Assert.True(tail[0].StartX + after.VisualBounds.X >= body[^1].EndX - .01);
        Assert.Equal("前置" + text.Text + "尾注", string.Concat(Draw(text).Select(static d => d.Text)));
    }

    [Fact]
    public void LongGeneratedTextDoesNotReserveAnAtomicRectangleBeforeTheBody()
    {
        var text = Text("正文", 1000);
        Add(text, ".text::before { content: '一二三四五六七八'; } .text::after { content: ''; }"); Layout(text);
        var before = CssGeneratedContent.Get(text)!.Before!;
        var natural = Assert.Single(before.GetSourceRangeSegments(0, before.Text.Length));
        text.Width = (natural.EndX - natural.StartX) * .75 + .1; Layout(text);
        var head = before.GetSourceRangeSegments(0, before.Text.Length);
        var body = Assert.Single(text.GetSourceRangeSegments(0, text.Text.Length));
        Assert.True(head.Count > 1);
        Assert.Equal(head[^1].Baseline + before.VisualBounds.Y, body.Baseline, 4);
        Assert.True(body.StartX > 0);
    }

    [Fact]
    public void SourceBoundariesDoNotIntroduceWordBreaks()
    {
        var text = Text("lo world", 28);
        Add(text, ".text::before { content: 'hel'; } .text::after { content: ''; }"); Layout(text);
        var before = CssGeneratedContent.Get(text)!.Before!;
        var head = Assert.Single(before.GetSourceRangeSegments(0, before.Text.Length));
        var body = text.GetSourceRangeSegments(0, text.Text.Length);
        Assert.Equal(head.Baseline + before.VisualBounds.Y, body[0].Baseline, 4);
        Assert.True(body[0].StartX > 0); Assert.True(body[0].EndX > 28);
        Assert.True(body[1].Baseline > body[0].Baseline);
    }

    [Fact]
    public void CollapsibleWhitespaceIsResolvedAcrossAllThreeSources()
    {
        var text = Text("  文  ", 180);
        Add(text, ".text::before { content: '前  '; } .text::after { content: '  后'; }"); Layout(text);
        Assert.Equal("前 文 后", string.Concat(Draw(text).Select(static d => d.Text)));
        Assert.Equal("  文  ", text.Text); text.SelectAll(); Assert.Equal("  文  ", text.SelectedText);
    }

    [Fact]
    public void HardBreaksAndLaterSourcesShareTheFollowingLine()
    {
        var text = Text("甲\n乙", 160);
        Add(text, ".text { white-space: pre-wrap; } .text::before { content: '前'; } .text::after { content: '后'; }"); Layout(text);
        var body = text.GetSourceRangeSegments(0, text.Text.Length); Assert.Equal(2, body.Count);
        var after = CssGeneratedContent.Get(text)!.After!;
        var tail = Assert.Single(after.GetSourceRangeSegments(0, after.Text.Length));
        Assert.Equal(body[1].Baseline, tail.Baseline + after.VisualBounds.Y, 4);
        var point = new Point(body[1].StartX + .1, body[1].Baseline - 2);
        var pointer = text.GetPositionFromPoint(point, true)!;
        Assert.Equal(2, text.ContentStart.GetOffsetToPosition(pointer));
    }

    [Fact]
    public void RichInlinesRemainOriginalWhileTheirRangesSpanParagraphLines()
    {
        var text = Text("", 100); var run = new Run("一二三四五六七八九") { FontSize = 18, Foreground = Brushes.Blue };
        text.Inlines.Add(run); Add(text, ".text::before { content: '前'; } .text::after { content: '后'; }"); Layout(text);
        Assert.Same(run, Assert.Single(text.Inlines));
        var segments = text.GetSourceRangeSegments(0, text.Text.Length); Assert.True(segments.Count > 1);
        var p = text.GetPositionFromPoint(new Point(segments[1].StartX + .1, segments[1].Baseline - 2), true)!;
        var index = text.ContentStart.GetOffsetToPosition(p); Assert.InRange(index, 1, text.Text.Length - 1);
        var length = Math.Min(2, text.Text.Length - index);
        text.Select(index, length); Assert.Equal(text.Text.Substring(index, length), text.SelectedText);
        run.Text = "修改正文后仍使用同一实例"; Layout(text); Assert.Same(run, text.Inlines[0]);
        Assert.Equal(run.Text, text.Text); Assert.Contains("修改", string.Concat(Draw(text).Select(static d => d.Text)));
    }

    [Fact]
    public void ResizingAndRemovingGenerationRestoreNormalTextLayout()
    {
        var text = Text("一二三四五六七八九十", 80);
        var sheet = Add(text, ".text::before { content: '前'; } .text::after { content: '后'; }"); Layout(text);
        var narrow = text.DesiredSize.Height;
        text.Width = 190; Layout(text); Assert.True(text.DesiredSize.Height < narrow);
        Assert.True(text.GetSourceRangeSegments(0, text.Text.Length)[0].StartX > 0);
        Css.GetStyleSheets(text).Remove(sheet); Layout(text);
        Assert.Equal(0, text.GetSourceRangeSegments(0, text.Text.Length)[0].StartX, 4);
        Assert.Equal(text.Text, string.Concat(Draw(text).Select(static d => d.Text)));
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    public void ABlockPseudoSplitsParagraphsWhileTheOtherPseudoContinuesInline(string block)
    {
        var text = Text("正文", 180);
        Add(text, $".text::before {{ content: '前'; }} .text::after {{ content: '后'; }} .text::{block} {{ display: block; height: 20px; }}"); Layout(text);
        var body = Assert.Single(text.GetSourceRangeSegments(0, text.Text.Length));
        var slots = CssGeneratedContent.Get(text)!;
        var inline = block == "before" ? slots.After! : slots.Before!;
        var inlineLine = Assert.Single(inline.GetSourceRangeSegments(0, inline.Text.Length));
        Assert.Equal(body.Baseline, inlineLine.Baseline + inline.VisualBounds.Y, 4);
        if (block == "before") Assert.True(body.Baseline > 20);
        else Assert.True(slots.After!.VisualBounds.Y >= body.Baseline);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    public void InlineBlockPseudosRemainAtomicItemsInsideTheSharedLine(string atomic)
    {
        var text = Text("正文", 180);
        Add(text, $".text::before {{ content: '前'; }} .text::after {{ content: '后'; }} .text::{atomic} {{ display: inline-block; width: 30px; height: 28px; }}"); Layout(text);
        var slots = CssGeneratedContent.Get(text)!; var box = atomic == "before" ? slots.Before! : slots.After!;
        Assert.Equal(30, box.ActualWidth, 4); Assert.Equal(28, box.ActualHeight, 4);
        var body = Assert.Single(text.GetSourceRangeSegments(0, text.Text.Length));
        Assert.True(body.Baseline < text.DesiredSize.Height);
        Assert.Equal("前正文后", string.Concat(Draw(text).Select(static d => d.Text)));
        if (atomic == "before") Assert.True(body.StartX >= 30); else Assert.True(box.VisualBounds.X >= body.EndX);
    }

    [Theory]
    [InlineData("center")]
    [InlineData("right")]
    [InlineData("end")]
    public void TextAlignmentMovesTheWholeLineTogether(string alignment)
    {
        var text = Text("文", 180);
        Add(text, $".text {{ text-align: {alignment}; }} .text::before {{ content: '前'; }} .text::after {{ content: '后'; }}"); Layout(text);
        var body = Assert.Single(text.GetSourceRangeSegments(0, 1)); var slots = CssGeneratedContent.Get(text)!;
        var before = Assert.Single(slots.Before!.GetSourceRangeSegments(0, 1));
        Assert.True(before.StartX + slots.Before.VisualBounds.X > 40);
        Assert.Equal(before.EndX + slots.Before.VisualBounds.X, body.StartX, 4);
        var after = Assert.Single(slots.After!.GetSourceRangeSegments(0, 1));
        Assert.Equal(body.EndX, after.StartX + slots.After.VisualBounds.X, 4);
    }

    [Fact]
    public void EmergencyWrapKeepsEmojiAndCrossSourceCombiningClustersTogether()
    {
        var text = Text("\u0301👩‍💻甲乙", 4);
        Add(text, ".text { overflow-wrap: anywhere; } .text::before { content: 'e'; } .text::after { content: ''; }"); Layout(text);
        var before = CssGeneratedContent.Get(text)!.Before!;
        var first = before.GetSourceRangeSegments(0, 1)[0];
        var combining = text.GetSourceRangeSegments(0, 1);
        if (combining.Count > 0) Assert.Equal(first.Baseline + before.VisualBounds.Y, combining[0].Baseline, 4);
        var painted = Draw(text).Select(static d => d.Text).ToArray();
        Assert.Contains(painted, static value => value.Contains("👩‍💻", StringComparison.Ordinal));
        Assert.DoesNotContain(painted, static value => value == "👩" || value == "💻" || value == "‍");
    }

    [Fact]
    public void SoftHyphensArePaintedOnlyWhenTheSharedParagraphBreaksThere()
    {
        var text = Text("co\u00adoperate", 48);
        Add(text, ".text::before { content: 'A '; } .text::after { content: ''; }"); Layout(text);
        Assert.Contains(Draw(text), static d => d.Text.Contains('-', StringComparison.Ordinal) || d.Text.Contains('‐', StringComparison.Ordinal));
        text.Width = 240; Layout(text);
        Assert.DoesNotContain(Draw(text), static d => d.Text.Contains('-', StringComparison.Ordinal) || d.Text.Contains('‐', StringComparison.Ordinal));
        Assert.Equal("co\u00adoperate", text.Text);
    }

    [Theory]
    [InlineData("slice")]
    [InlineData("clone")]
    public void InlineBackgroundsAndBordersFollowTheirFragments(string decoration)
    {
        var text = Text("正文", 1000);
        Add(text, $".text::before {{ content: '一二三四五六七八'; padding: 2px 4px; border: 1px solid green; background: lightgreen; box-decoration-break: {decoration}; }} .text::after {{ content: '后'; }}"); Layout(text);
        var before = CssGeneratedContent.Get(text)!.Before!;
        var natural = Assert.Single(before.GetSourceRangeSegments(0, before.Text.Length));
        text.Width = (natural.EndX - natural.StartX) * .7 + 10; Layout(text);
        var segments = before.GetSourceRangeSegments(0, before.Text.Length); Assert.True(segments.Count > 1);
        var dc = new Capture(); text.Render(dc);
        Assert.Equal(segments.Count, dc.Rectangles.Count(static r => r.Brush is SolidColorBrush b && b.Color == Colors.LightGreen));
        Assert.All(dc.Rectangles.Where(static r => r.Brush is SolidColorBrush b && b.Color == Colors.LightGreen),
            rect => Assert.True(rect.Rect.Width <= text.Width + .01));
    }

    [Fact]
    public void GeneratedGlyphsOutsideTheBodyDoNotProduceNonsnappingBodyPositions()
    {
        var text = Text("正文跨过多行后的内容", 100);
        Add(text, ".text::before { content: '前置'; } .text::after { content: '尾'; }"); Layout(text);
        var slots = CssGeneratedContent.Get(text)!;
        var head = slots.Before!.GetSourceRangeSegments(0, slots.Before.Text.Length)[0];
        Assert.Null(text.GetPositionFromPoint(new Point(slots.Before.VisualBounds.X + head.StartX + 1,
            slots.Before.VisualBounds.Y + head.Baseline - 2), false));
        var body = text.GetSourceRangeSegments(0, text.Text.Length)[0];
        Assert.NotNull(text.GetPositionFromPoint(new Point(body.StartX + 1, body.Baseline - 2), false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoWrapAppliesAcrossSourceBoundaries(bool native)
    {
        var text = Text("有空 格的正文", 32);
        if (native) text.TextWrapping = TextWrapping.NoWrap;
        Add(text, (native ? "" : ".text { white-space: nowrap; }") +
            ".text::before { content: '前 文 '; } .text::after { content: ' 尾 注'; }"); Layout(text);
        Assert.Single(text.GetSourceRangeSegments(0, text.Text.Length));
        var slots = CssGeneratedContent.Get(text)!;
        Assert.Single(slots.Before!.GetSourceRangeSegments(0, slots.Before.Text.Length));
        Assert.Single(slots.After!.GetSourceRangeSegments(0, slots.After.Text.Length));
        Assert.True(slots.After.VisualBounds.Right > 32);
    }

    [Fact]
    public void EmptyInlineGenerationDoesNotCreateAnEmptyLine()
    {
        var text = Text("", 120);
        Add(text, ".text::before, .text::after { content: ''; }"); Layout(text);
        Assert.Equal(0, text.DesiredSize.Height);
        Css.SetStyle(CssGeneratedContent.Get(text)!.Before!, "padding: 2px;"); Layout(text);
        Assert.True(text.DesiredSize.Height > 0);
    }

    [Fact]
    public void InlineVerticalAlignmentUsesTheSharedLineBaseline()
    {
        var text = Text("正文", 180);
        Add(text, ".text::before { content: '上标'; font-size: 10px; vertical-align: super; } .text::after { content: '后'; }"); Layout(text);
        var body = text.GetSourceRangeSegments(0, text.Text.Length)[0];
        var before = CssGeneratedContent.Get(text)!.Before!;
        var head = before.GetSourceRangeSegments(0, before.Text.Length)[0];
        Assert.True(head.Baseline + before.VisualBounds.Y < body.Baseline);
    }

    [Fact]
    public void RightToLeftBaseDirectionPlacesInlineSourcesFromTheRightEdge()
    {
        var text = Text("文", 180); text.FlowDirection = FlowDirection.RightToLeft;
        Add(text, ".text::before { content: '前'; } .text::after { content: '后'; }"); Layout(text);
        var slots = CssGeneratedContent.Get(text)!;
        var body = text.GetSourceRangeSegments(0, 1)[0];
        Assert.True(slots.Before!.VisualBounds.X >= body.EndX);
        Assert.True(slots.After!.VisualBounds.Right <= body.StartX + .01);
        Assert.InRange(slots.Before.VisualBounds.Right, 179, 180.01);
    }

    [Fact]
    public void NonReplacedInlineDimensionsDoNotOverrideTheFragmentGeometry()
    {
        var text = Text("正文", 160);
        Add(text, ".text::before { content: '前置'; width: 500px; height: 200px; min-width: 600px; min-height: 300px; } .text::after { content: '尾'; }"); Layout(text);
        var before = CssGeneratedContent.Get(text)!.Before!;
        Assert.InRange(before.ActualWidth, 1, 80); Assert.InRange(before.ActualHeight, 1, 40);
        var body = Assert.Single(text.GetSourceRangeSegments(0, text.Text.Length));
        Assert.Equal(before.VisualBounds.Right, body.StartX, 4);
    }

    [Fact]
    public void PercentagePaddingUsesTheContainingBlockBeforeFragmentation()
    {
        var text = Text("文", 200);
        Add(text, ".text::before { content: '前'; padding-left: 10%; } .text::after { content: '后'; }"); Layout(text);
        var before = CssGeneratedContent.Get(text)!.Before!;
        Assert.Equal(20, before.GetSourceRangeSegments(0, 1)[0].StartX, 4);
    }

    [Fact]
    public void ALongUnbreakableTokenOverflowsWithoutBecomingThousandsOfFragments()
    {
        var text = Text(new string('x', 12000), 100);
        Add(text, ".text::before { content: 'A'; } .text::after { content: 'Z'; }"); Layout(text);
        var segment = Assert.Single(text.GetSourceRangeSegments(0, text.Text.Length));
        Assert.True(segment.EndX > 10000); Assert.Equal(12000, text.Text.Length);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    public void ExportedBaselinesIncludeBlockPseudoContent(string block)
    {
        var text = Text("文", 160);
        Add(text, $".text::before {{ content: '前'; }} .text::after {{ content: '后'; }} .text::{block} {{ display: block; font-size: 20px; }}"); Layout(text);
        var slots = CssGeneratedContent.Get(text)!;
        if (block == "before") Assert.Equal(slots.Before!.VisualBounds.Y + slots.Before.GetFirstBaselineOffset(slots.Before.ActualHeight),
            text.GetFirstBaselineOffset(text.ActualHeight), 4);
        else Assert.Equal(slots.After!.VisualBounds.Y + slots.After.GetLastBaselineOffset(slots.After.ActualHeight),
            text.GetLastBaselineOffset(text.ActualHeight), 4);
    }

    [Fact]
    public void RightToLeftSliceKeepsTheStartEdgeOnTheFirstFragmentAndEndEdgeOnTheLast()
    {
        var text = Text("文", 1000); text.FlowDirection = FlowDirection.RightToLeft;
        Add(text, ".text::before { content: '一二三四五六七八'; padding-left: 10px; padding-right: 2px; background: lightgreen; } .text::after { content: '后'; }"); Layout(text);
        var before = CssGeneratedContent.Get(text)!.Before!;
        var natural = Assert.Single(before.GetSourceRangeSegments(0, before.Text.Length));
        text.Width = (natural.EndX - natural.StartX) * .7 + 12; Layout(text);
        var segments = before.GetSourceRangeSegments(0, before.Text.Length); Assert.True(segments.Count > 1);
        var dc = new Capture(); text.Render(dc);
        var backgrounds = dc.Rectangles.Where(static r => r.Brush is SolidColorBrush b && b.Color == Colors.LightGreen).ToArray();
        Assert.Equal(segments.Count, backgrounds.Length);
        Assert.Equal(0, segments[0].StartX - backgrounds[0].Rect.X, 4);
        Assert.Equal(10, segments[^1].StartX - backgrounds[^1].Rect.X, 4);
    }

    [Fact]
    public void EqualWidthGeneratedUpdatesRebuildTheirSharedLinePositions()
    {
        var text = Text("甲乙", 1000); text.Tag = "丙丁戊己庚";
        Add(text, ".text::before { content: '前'; } .text::after { content: attr(Tag); }"); Layout(text);
        var before = CssGeneratedContent.Get(text)!.Before!;
        var natural = Assert.Single(before.GetSourceRangeSegments(0, 1));
        text.Width = (natural.EndX - natural.StartX) * 4.5; Layout(text);
        var after = CssGeneratedContent.Get(text)!.After!;
        foreach (var value in new[] { "辛壬癸子丑", "丙丁戊己庚", "辛壬癸子丑" })
        {
            text.Tag = value; Layout(text);
            var body = Assert.Single(text.GetSourceRangeSegments(0, text.Text.Length));
            var tail = after.GetSourceRangeSegments(0, value.Length); Assert.True(tail.Count > 1);
            Assert.Equal(value, after.Text);
            Assert.Equal(body.EndX, tail[0].StartX + after.VisualBounds.X, 4);
            Assert.Equal(body.Baseline, tail[0].Baseline + after.VisualBounds.Y, 4);
        }
    }

    private static TextBlock Text(string content, double width)
    { var text = new TextBlock { Text = content, Width = width, FontSize = 16, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Top, IsTextSelectionEnabled = true }; Css.SetClass(text, "text"); return text; }
    private static CssStyleSheet Add(TextBlock text, string css) { var sheet = CssStyleSheet.Parse(css); Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(text).Add(sheet); return sheet; }
    private static void Layout(FrameworkElement root)
    { for (var i = 0; i < 5; i++) { CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); root.Measure(new Size(260, 400)); root.Arrange(new Rect(0, 0, 260, 400)); root.UpdateLayout(); } }
    private static List<(string Text, Point Origin)> Draw(FrameworkElement root) { var dc = new Capture(); root.Render(dc); return dc.Text; }
    private sealed class Capture : DrawingContextAdapter, IOffsetDrawingContext
    {
        public Point Offset { get; set; }
        internal readonly List<(string Text, Point Origin)> Text = [];
        internal readonly List<(Brush? Brush, Rect Rect)> Rectangles = [];
        public override void DrawText(FormattedText text, Point origin) => Text.Add((text.Text, new(origin.X + Offset.X, origin.Y + Offset.Y)));
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
