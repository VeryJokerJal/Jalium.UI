using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssLineHeightLayoutTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    internal static readonly string[] Paths = ["native", "block", "rich-native", "rich-block", "shared", "nested"];
    internal static readonly (string Css, double Used)[] Values = [("0px", 0), ("8px", 8), ("16px", 16), ("24px", 24), ("48px", 48), ("1.5", 24), ("150%", 24), ("normal", double.NaN)];
    public static IEnumerable<object[]> UniformCases()
    {
        foreach (var path in Paths)
        foreach (var font in new[] { "Courier New", "PingFang SC", "Times New Roman" })
        for (var index = 0; index < Values.Length; index++)
        foreach (var count in new[] { 2, 3 }) yield return [path, font, index, count];
    }
    [Theory] [MemberData(nameof(UniformCases))]
    public void AuthoredLineHeightOwnsTheLineBoxAndDistributesHalfLeading(string path, string font, int index, int count)
    {
        var value = Values[index];
        if (double.IsNaN(value.Used)) value.Used = TextMeasurement.GetLineHeight(font, 16);
        using var f = new CssLineHeightFixture(path, font, value.Css, count);
        f.Layout(); var lines = f.Baselines(); Assert.Equal(count, lines.Length);
        var expected = Bounds(font, 16, value.Used);
        Near(expected.Above, lines[0]);
        for (var line = 1; line < count; line++) Near(value.Used, lines[line] - lines[line - 1]);
        Near(count * value.Used + f.NativeSlack, f.Root.DesiredSize.Height);
        f.AssertSource();
    }

    [Theory]
    [InlineData("native")] [InlineData("block")] [InlineData("rich-native")]
    [InlineData("rich-block")] [InlineData("shared")] [InlineData("nested")]
    public void ChangingAndRestoringLineHeightReusesOriginalContentAndRefreshesInheritedCaches(string path)
    {
        using var f = new CssLineHeightFixture(path, "Courier New", "24px", 2); f.Layout();
        var initial = f.Baselines(); var size = f.Root.DesiredSize;
        foreach (var value in new[] { "8px", "48px", "0px", "normal", "24px" })
        {
            f.SetLineHeight(value); f.Layout(); var lines = f.Baselines();
            var height = value == "normal" ? TextMeasurement.GetLineHeight("Courier New", 16) : double.Parse(value[..^2]);
            Near(height, lines[1] - lines[0]); f.AssertSource();
        }
        Assert.Equal(initial, f.Baselines()); Assert.Equal(size, f.Root.DesiredSize);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void NativeLocalValuesTakePriorityAndClearingThemRestoresCss(bool block)
    {
        using var f = new CssLineHeightFixture(block ? "block" : "native", "Courier New", "8px", 2);
        var text = Assert.IsType<TextBlock>(f.Root); text.LineHeight = 12; f.Layout();
        Near(12, f.Baselines()[1] - f.Baselines()[0]);
        text.LineStackingStrategy = LineStackingStrategy.MaxHeight; f.Layout();
        Near(TextMeasurement.GetLineHeight("Courier New", 16), f.Baselines()[1] - f.Baselines()[0]);
        text.ClearValue(TextBlock.LineStackingStrategyProperty); f.Layout(); Near(12, f.Baselines()[1] - f.Baselines()[0]);
        text.ClearValue(TextBlock.LineHeightProperty); f.Layout(); Near(8, f.Baselines()[1] - f.Baselines()[0]);
    }

    [Fact]
    public void UnstyledNativeTextKeepsItsMaximumHeightAndOpticalAllowance()
    {
        var text = new TextBlock { Text = "MMMM\nMMMM", FontFamily = "Courier New", FontSize = 16, LineHeight = 8 };
        Layout(text); var lines = text.GetSourceRangeSegments(0, text.Text.Length);
        var natural = TextMeasurement.GetLineHeight("Courier New", 16);
        Near(natural, lines[1].Baseline - lines[0].Baseline); Near(2 * natural + 2, text.DesiredSize.Height);
    }

    [Theory] [InlineData("native")] [InlineData("block")] [InlineData("shared")] [InlineData("nested")]
    public void AncestorsRetainOverflowingInkAcrossCompositionChangesAndClearItAfterRestoringLayout(string path)
    {
        using var f = new CssLineHeightFixture(path, "Courier New", "0px", 2);
        var inner = new Border { Child = f.Root }; var outer = new Border { Child = inner };
        Layout(outer); var original = outer.GetExtraDirtyPadding();
        Assert.True(original > 0);
        var move = new TranslateTransform(0, 40); f.Root.RenderTransform = move;
        Assert.True(outer.GetExtraDirtyPadding() >= original + 39);
        move.Y = 80; Assert.True(outer.GetExtraDirtyPadding() >= original + 79);
        f.Root.RenderTransform = null; Near(original, outer.GetExtraDirtyPadding());
        f.Root.RenderOffset = new(0, -60); Assert.True(outer.GetExtraDirtyPadding() >= original + 59);
        f.Root.RenderOffset = default; Near(original, outer.GetExtraDirtyPadding());
        f.SetLineHeight("64px"); Layout(outer); Near(path is "shared" or "nested" ? 1 : 0, outer.GetExtraDirtyPadding());
        f.Root.Visibility = Visibility.Collapsed; Layout(outer); Near(0, outer.GetExtraDirtyPadding());
        f.Root.Visibility = Visibility.Visible; f.SetLineHeight("0px"); Layout(outer); Near(original, outer.GetExtraDirtyPadding());
        inner.Child = null; Layout(outer); Near(0, outer.GetExtraDirtyPadding());
    }

    [Theory]
    [InlineData(false, 16)] [InlineData(false, 32)] [InlineData(false, 48)]
    [InlineData(true, 16)] [InlineData(true, 32)] [InlineData(true, 48)]
    public void EmptyInlineBoxesStillContributeTheirOwnLineHeight(bool block, int size)
    {
        var text = new TextBlock(); var empty = new Span(); Css.SetStyle(empty, $"font-size:{size}px;");
        text.Inlines.Add(new Run("M")); text.Inlines.Add(empty); text.Inlines.Add(new Run("M"));
        Css.SetStyle(text, (block ? "display:block;" : "") + "font-family:'Courier New';font-size:16px;line-height:1.5;width:200px;");
        Layout(text); var root = Bounds("Courier New", 16, 24); var span = Bounds("Courier New", size, size * 1.5);
        var above = Math.Max(root.Above, span.Above); var below = Math.Max(root.Below, span.Below);
        Near(above, Assert.Single(text.GetSourceRangeSegments(0, 2)).Baseline);
        Near(above + below + (block ? 0 : 2), text.DesiredSize.Height);
        Assert.Same(empty, text.Inlines[1]); Assert.Empty(empty.Inlines);
    }

    [Theory]
    [InlineData("0px", false)] [InlineData("8px", false)] [InlineData("24px", false)]
    [InlineData("48px", false)] [InlineData("1.5", false)] [InlineData("150%", false)]
    [InlineData("0px", true)] [InlineData("8px", true)] [InlineData("24px", true)]
    [InlineData("48px", true)] [InlineData("1.5", true)] [InlineData("150%", true)]
    public void DocumentProxiesKeepCssLineHeightAndOriginalRunInheritance(string value, bool large)
    {
        var run = new Run("MMMM\nMMMM"); if (large) run.FontSize = 32;
        var paragraph = new Paragraph(run);
        var document = new FlowDocument(paragraph) { FontFamily = "Courier New", FontSize = 16, PagePadding = new(0), PageWidth = 200, PageHeight = 300 };
        Css.SetStyleSheet(document, $"FlowDocument{{line-height:{value};white-space:pre-wrap;}}Paragraph{{margin:0;padding:0;}}");
        using var body = new FlowDocumentPresentation(); body.SetDocument(document); Layout(body);
        var source = Assert.Single(body.Paragraphs); var lines = source.GetSourceRangeSegments(0, source.Text.Length);
        var height = value is "1.5" or "150%" ? 24 : double.Parse(value[..^2]);
        var root = Bounds("Courier New", 16, height);
        var child = Bounds("Courier New", large ? 32 : 16, value == "1.5" && large ? 48 : height);
        var above = Math.Max(root.Above, child.Above); var below = Math.Max(root.Below, child.Below);
        Near(above, lines[0].Baseline); Near(above + below, lines[1].Baseline - lines[0].Baseline);
        Near(2 * (above + below), body.ExtentHeight);
        Assert.Same(run, Assert.Single(paragraph.Inlines)); Assert.Equal("MMMM\nMMMM", document.GetText().TrimEnd('\n', '\r'));
    }

    [Theory] [InlineData(24d)] [InlineData(24.125)]
    public void FractionalColumnBalancingRetainsTheExactShapedLineBottom(double height)
    {
        var breaker = new CssColumnFragmentation();
        for (var line = 0; line < 5; line++) breaker.Keeps.Add(new(line * height, (line + 1) * height));
        breaker.Paragraphs.Add(new(Enumerable.Range(0, 5).Select(i => i * height).ToArray(), Enumerable.Range(1, 5).Select(i => i * height).ToArray(), 2, 2));
        var balanced = breaker.Balance(0, 5 * height, 2, double.PositiveInfinity);
        var fragments = breaker.Breaks(0, 5 * height, balanced); Assert.Equal(2, fragments.Count);
        Assert.Equal(3 * height, fragments[0].End); Assert.Equal(5 * height, fragments[^1].End);
    }

    [Theory]
    [InlineData(false, false, 0)] [InlineData(false, false, 8)] [InlineData(false, false, 24)]
    [InlineData(true, false, 0)] [InlineData(true, false, 8)] [InlineData(true, false, 24)]
    [InlineData(false, true, 0)] [InlineData(false, true, 8)] [InlineData(false, true, 24)]
    [InlineData(true, true, 0)] [InlineData(true, true, 8)] [InlineData(true, true, 24)]
    public void DrawingAndSelectionShareTheBaselineEvenWithFallbackGlyphsAndShortLineBoxes(bool block, bool rich, int height)
    {
        var text = new TextBlock();
        if (rich) text.Inlines.Add(new Run("Ag水\ngpy") { Foreground = Brushes.Blue }); else text.Text = "Ag水\ngpy";
        Css.SetStyle(text, (block ? "display:block;" : "") + $"font-family:'Courier New';font-size:16px;line-height:{height}px;white-space:pre-wrap;width:200px;");
        Layout(text); var draw = new BaselineCapture(); text.Render(draw);
        var lines = text.GetSourceRangeSegments(0, text.Text.Length);
        Assert.Equal(2, lines.Count); Assert.Equal("Ag水gpy", string.Concat(draw.Text));
        // Autospace and fallback shaping can split one logical line into several
        // draw fragments. Each must use that logical line's selection baseline.
        for (var i = 0; i < draw.Text.Count; i++) Near(lines[draw.Text[i].Contains("gpy") ? 1 : 0].Baseline, draw.Baselines[i]);
    }

    [Theory] [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void EllipsisAndItsPrefixUseTheSelectionBaselineWithNegativeLeading(bool block, bool rich)
    {
        var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        if (rich) text.Inlines.Add(new Run("MMMMMMMM") { Foreground = Brushes.Blue }); else text.Text = "MMMMMMMM";
        Css.SetStyle(text, (block ? "display:block;" : "") + "font-family:'Courier New';font-size:16px;line-height:8px;white-space:nowrap;width:48px;");
        Layout(text); var draw = new BaselineCapture(); text.Render(draw);
        Assert.EndsWith("…", string.Concat(draw.Text));
        var baseline = Assert.Single(text.GetSourceRangeSegments(0, text.Text.Length)).Baseline;
        Assert.All(draw.Baselines, actual => Near(baseline, actual));
    }

    [Theory] [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void NormalLineHeightIncludesActualFallbackFontMetrics(bool block, bool rich)
    {
        var glyph = new FormattedText("A水", "Courier New", 16); Assert.True(TextMeasurement.MeasureText(glyph));
        var text = new TextBlock();
        if (rich) text.Inlines.Add(new Run("A水\nA水")); else text.Text = "A水\nA水";
        Css.SetStyle(text, (block ? "display:block;" : "") + "font-family:'Courier New';font-size:16px;line-height:normal;white-space:pre-wrap;width:200px;");
        Layout(text); var strut = Bounds("Courier New", 16, TextMeasurement.GetLineHeight("Courier New", 16));
        var natural = Math.Max(glyph.Height, glyph.Ascent + glyph.Descent); var half = (natural - glyph.Ascent - glyph.Descent) / 2;
        var above = Math.Max(strut.Above, glyph.Ascent + half); var below = Math.Max(strut.Below, glyph.Descent + half);
        var lines = text.GetSourceRangeSegments(0, text.Text.Length); Assert.Equal(2, lines.Count);
        Near(above, lines[0].Baseline); Near(above + below, lines[1].Baseline - lines[0].Baseline);
        Near(2 * (above + below) + (block ? 0 : 2), text.DesiredSize.Height);
    }

    private sealed class BaselineCapture : DrawingContextAdapter, IOffsetDrawingContext
    {
        public Point Offset { get; set; }
        internal readonly List<double> Baselines = [];
        internal readonly List<string> Text = [];
        public override void DrawText(FormattedText text, Point origin)
        { Text.Add(text.Text); Baselines.Add(origin.Y + Offset.Y + text.Baseline); }
        public override void DrawLine(Pen pen, Point first, Point last) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource image, Rect rectangle) { }
        public override void PushClip(Geometry clip) { }
        public override void PushOpacity(double value) { }
        public override void PushTransform(Transform value) { }
        public override void Pop() { }
        public override void Close() { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius radius) { }
    }
    private static void Layout(FrameworkElement root)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new(200, 1000)); root.Arrange(new(0, 0, 200, root.DesiredSize.Height)); root.UpdateLayout();
        }
    }

    public static IEnumerable<object[]> InheritedCases()
    {
        foreach (var path in new[] { "rich-native", "rich-block", "shared", "nested" })
        foreach (var value in new[] { "1.5", "24px", "150%" })
        foreach (var font in new[] { "Courier New", "PingFang SC" })
        foreach (var size in new[] { 8, 32, 48 }) yield return [path, value, font, size];
    }
    [Theory] [MemberData(nameof(InheritedCases))]
    public void InlineSourcesUseTheirOwnComputedLineHeightBeforeBaselineAlignment(string path, string value, string font, int size)
    {
        using var f = new CssLineHeightFixture(path, "Courier New", value, 2, font, size);
        f.Layout(); var lines = f.Baselines(); Assert.Equal(2, lines.Length);
        var root = Bounds("Courier New", 16, 24);
        var child = Bounds(font, size, value == "1.5" ? size * 1.5 : 24);
        var above = Math.Max(root.Above, child.Above); var below = Math.Max(root.Below, child.Below);
        Near(above, lines[0]); Near(above + below, lines[1] - lines[0]);
        Near(2 * (above + below) + f.NativeSlack, f.Root.DesiredSize.Height);
        f.AssertSource();
    }

    // Only first-font metrics come from the platform. The CSS2/Inline3 oracle
    // independently adds half of (used line-height - ascent - descent) to each
    // edge, including negative leading, then unions aligned inline bounds.
    internal static (double Above, double Below) Bounds(string font, double size, double height)
    {
        var m = TextMeasurement.GetFontMetrics(font, size);
        var half = (height - m.Ascent - m.Descent) / 2;
        return (m.Ascent + half, m.Descent + half);
    }
    internal static void Near(double expected, double actual) => Assert.InRange(actual, expected - .06, expected + .06);
}

internal sealed class CssLineHeightFixture : IDisposable
{
    internal FrameworkElement Root { get; }
    internal readonly List<TextBlock> Texts = [];
    internal Run? OriginalRun;
    internal double NativeSlack;
    private readonly string _original;
    private string _lineCss;

    internal CssLineHeightFixture(string path, string family, string lineHeight, int count, string? childFont = null, int childSize = 16, bool omitRootStyle = false)
    {
        _lineCss = lineHeight;
        _original = string.Join('\n', Enumerable.Repeat("MMMM", count));
        var common = $"font-family:'{family}';font-size:16px;line-height:{lineHeight};white-space:pre-wrap;width:200px;";
        TextBlock Text(string value)
        {
            var text = new TextBlock { Text = value }; Texts.Add(text); return text;
        }
        if (path is "native" or "block" or "rich-native" or "rich-block")
        {
            var rich = path.StartsWith("rich"); var text = Text(rich ? "" : _original);
            if (rich)
            {
                OriginalRun = new Run(_original); text.Inlines.Add(OriginalRun);
                if (childFont is not null) Css.SetStyle(OriginalRun, $"font-family:'{childFont}';font-size:{childSize}px;");
            }
            var block = path.EndsWith("block"); NativeSlack = block ? 0 : 2;
            Css.SetStyle(text, (block ? "display:block;" : "") + common); Root = text;
        }
        else
        {
            var root = new StackPanel(); Root = root; Css.SetStyle(root, "display:block;" + common);
            if (path == "shared" && childFont is null)
            {
                var a = Text(string.Concat(Enumerable.Repeat("MMMM\n", count - 1))); var b = Text("MMMM");
                foreach (var text in new[] { a, b }) { Css.SetStyle(text, "display:inline;"); root.Children.Add(text); }
            }
            else
            {
                var text = Text(_original); Css.SetStyle(text, "display:inline;" + (childFont is null ? "" : $"font-family:'{childFont}';font-size:{childSize}px;"));
                if (path == "nested")
                {
                    var frame = new Border { Child = text }; Css.SetStyle(frame, "display:inline;"); root.Children.Add(frame);
                }
                else root.Children.Add(text);
            }
        }
        if (omitRootStyle) Css.SetStyle(Root, "");
    }
    internal void SetLineHeight(string value)
    {
        Css.SetStyle(Root, Css.GetStyle(Root).Replace("line-height:" + _lineCss + ";", "line-height:" + value + ";")); _lineCss = value;
    }
    internal double[] Baselines() => Texts.SelectMany(t => t.GetSourceRangeSegments(0, t.Text.Length)
        .Select(s => s.Baseline + t.TransformToAncestor(Root).Y)).ToArray();
    internal void Layout()
    {
        for (var pass = 0; pass < 4; pass++)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            Root.Measure(new(200, 1000)); Root.Arrange(new(0, 0, 200, Root.DesiredSize.Height)); Root.UpdateLayout();
        }
    }
    internal void AssertSource()
    {
        Assert.Equal(_original, string.Concat(Texts.Select(t => t.Text)));
        if (OriginalRun is not null) Assert.Same(OriginalRun, Assert.Single(Texts[0].Inlines));
        foreach (var text in Texts) { text.SelectAll(); Assert.Equal(text.Text, text.SelectedText); }
    }
    public void Dispose() { }
}
