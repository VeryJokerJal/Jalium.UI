using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed partial class CssRubyBoxLineTests
{
    public static IEnumerable<object[]> AnnotationHeights()
    {
        foreach (var mode in new[] { "horizontal-tb", "vertical-rl", "vertical-lr", "sideways-rl", "sideways-lr" })
        foreach (var merge in new[] { "separate", "merge", "auto" })
        foreach (var height in new[] { "0", "96px", "7" })
            yield return [mode, merge, height];
    }

    [Theory, MemberData(nameof(AnnotationHeights))]
    public void AnnotationLineHeightDoesNotChangeItsFontBoxOrRubyLine(string mode, string merge, string height)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var scene = Create(mode, merge, 80);
        Css.SetStyle(scene.Note, NoteStyle + "line-height:" + height + ";");
        Layout(scene.Root, mode);
        var font = TextMeasurement.GetFontMetrics("Hiragino Sans", 16, 400, 0);
        var bounds = Bounds(scene.Root, scene.Note);
        double cross = mode == "horizontal-tb" ? bounds.Height : bounds.Width;
        Assert.Equal(font.Ascent + font.Descent, cross, 3);
        Assert.Equal(80, Cross(scene.Root.DesiredSize, mode), 3);
        Assert.Equal(height == "96px" ? 96 : height == "7" ? 112 : 0, scene.Note.LineHeight, 3);
        Assert.Same(scene.Root, scene.Note.VisualParent);
        scene.Note.Select(0, 1); Assert.Equal("と", scene.Note.SelectedText);
    }

    [Theory]
    [InlineData("separate")]
    [InlineData("merge")]
    [InlineData("auto")]
    public void InterCharacterAnnotationsKeepTheirOwnInlineBlockLineHeight(string merge)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var scene = Create("horizontal-tb", merge, 80);
        Css.SetStyle(scene.Root, RootStyle("horizontal-tb", merge, 80) + "ruby-position:inter-character;");
        Css.SetStyle(scene.Note, NoteStyle + "line-height:48px;");
        Layout(scene.Root, "horizontal-tb");
        Assert.Equal(48, scene.Note.RenderSize.Width, 3);
        Assert.Equal(48, scene.Note.LineHeight, 3);
        Assert.NotEmpty(scene.Note.DocumentTextBounds(0, 2));
    }

    [Theory]
    [InlineData("separate", "ltr", "center")] [InlineData("merge", "ltr", "center")] [InlineData("auto", "ltr", "center")]
    [InlineData("separate", "rtl", "center")] [InlineData("merge", "rtl", "center")] [InlineData("auto", "rtl", "center")]
    [InlineData("separate", "ltr", "start")] [InlineData("merge", "ltr", "start")] [InlineData("auto", "ltr", "start")]
    [InlineData("separate", "rtl", "start")] [InlineData("merge", "rtl", "start")] [InlineData("auto", "rtl", "start")]
    public void SideInlineBlockContributesItsFullHeightWhenTheBaseFontAreaExceedsTheLine(string merge, string direction, string align)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var scene = Create("horizontal-tb", merge, 40); scene.Note.Text = "ㄓㄨ";
        Css.SetStyle(scene.Root, RootStyle("horizontal-tb", merge, 40) + $"font-family:'PingFang TC';ruby-position:inter-character;direction:{direction};ruby-align:{align};");
        Css.SetStyle(scene.Note, NoteStyle + "line-height:20px;text-orientation:upright;");
        Layout(scene.Root, "horizontal-tb");
        var font = TextMeasurement.GetFontMetrics("PingFang TC", 32, 400, 0);
        Assert.True(font.Ascent + font.Descent > 40);
        Assert.Equal(font.Ascent + font.Descent, scene.Root.DesiredSize.Height, 3);
        Assert.Equal(0, Bounds(scene.Root, scene.Base).Top, 3);
        Assert.Equal(0, scene.Note.GetRenderMatrix().OffsetY, 3);
    }

    public static IEnumerable<object[]> InternalBoxCases()
    {
        foreach (var mode in new[] { "horizontal-tb", "vertical-rl", "vertical-lr", "sideways-rl", "sideways-lr" })
        foreach (var merge in new[] { "separate", "merge", "auto" })
        foreach (bool annotation in new[] { false, true }) yield return [mode, merge, annotation];
    }

    [Theory, MemberData(nameof(InternalBoxCases))]
    public void InlineRubySizesDoNotReplaceItsContentOrRewriteAuthorValues(string mode, string merge, bool annotation)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var scene = Create(mode, merge, 80); var target = annotation ? scene.Note : scene.Base;
        Css.SetStyle(target, $"display:{(annotation ? "ruby-text" : "ruby-base")};font-size:{(annotation ? 16 : 32)}px;" +
            "width:190px;height:170px;min-width:180px;min-height:160px;max-width:200px;max-height:180px;");
        Layout(scene.Root, mode);
        Assert.Equal(32, mode == "horizontal-tb" ? scene.Root.DesiredSize.Width : scene.Root.DesiredSize.Height, 3);
        Assert.Equal(80, Cross(scene.Root.DesiredSize, mode), 3);
        Assert.Equal(190, target.Width); Assert.Equal(170, target.Height);
        Assert.Equal(180, target.MinWidth); Assert.Equal(160, target.MinHeight);
    }

    [Theory, MemberData(nameof(InternalBoxCases))]
    public void StructuralRubyContainersIgnoreBoxEdgesAndSizes(string mode, string merge, bool annotation)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var scene = Create(mode, merge, 80); var container = new StackPanel(); var child = annotation ? scene.Note : scene.Base;
        scene.Root.Children.Remove(child); container.Children.Add(child);
        scene.Root.Children.Insert(annotation ? 1 : 0, container);
        Css.SetStyle(container, $"display:{(annotation ? "ruby-text-container" : "ruby-base-container")};" +
            "margin:37px;padding:23px;border:7px solid red;width:190px;height:170px;min-width:180px;min-height:160px;");
        Layout(scene.Root, mode);
        Assert.Equal(32, mode == "horizontal-tb" ? scene.Root.DesiredSize.Width : scene.Root.DesiredSize.Height, 3);
        Assert.Equal(80, Cross(scene.Root.DesiredSize, mode), 3);
        var actual = Bounds(scene.Root, child);
        Assert.InRange(mode == "horizontal-tb" ? actual.Left : mode == "sideways-lr" ? 240 - actual.Bottom : actual.Top, -.001, .001);
        Assert.Equal(new Thickness(37), container.Margin);
        var font = TextMeasurement.GetFontMetrics("Hiragino Sans", annotation ? 16 : 32, 400, 0);
        Assert.Equal(font.Ascent + font.Descent, Cross(container.RenderSize, mode), 3);
        Assert.Equal(32, mode == "horizontal-tb" ? container.RenderSize.Width : container.RenderSize.Height, 3);
        Assert.Same(container, child.VisualParent); Assert.Same(scene.Root, container.VisualParent);
        // The outer Ruby formatter initially allocates a CSS border box. A
        // later native LayoutManager arrange uses that saved rectangle without
        // the temporary parent-box scope and must not reapply author margins.
        var before = Bounds(scene.Root, child);
        var slot = container.PreviousFinalRect;
        container.InvalidateMeasure(); container.Measure(new Size(240, 240));
        container.InvalidateArrange(); container.Arrange(slot);
        Assert.Equal(before, Bounds(scene.Root, child));
    }

    public static IEnumerable<object[]> ThreeLines()
    {
        foreach (var mode in new[] { "horizontal-tb", "vertical-rl", "vertical-lr", "sideways-rl", "sideways-lr" })
        foreach (var merge in new[] { "separate", "merge", "auto" })
        foreach (double height in new[] { 0d, 40, 60, 80 })
        foreach (var position in new[] { "over", "under" }) yield return [mode, merge, height, position];
    }

    [Theory, MemberData(nameof(ThreeLines))]
    public void ThreeIdenticalRubyLinesUseOnlyNecessaryLeadingAndKeepOriginalRanges(string mode, string merge, double height, string position)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel(); var bases = new List<TextBlock>(); var notes = new List<TextBlock>();
        for (int i = 0; i < 3; i++)
        {
            var basis = new TextBlock { Text = "東", FontSize = 32 }; var note = new TextBlock { Text = "とう", FontSize = 16 };
            bases.Add(basis); notes.Add(note); root.Children.Add(basis); root.Children.Add(note);
            Css.SetStyle(basis, "display:ruby-base;font-size:32px;"); Css.SetStyle(note, NoteStyle + "line-height:190px;");
        }
        Css.SetStyle(root, RootStyle(mode, merge, height) + $"white-space:pre-wrap;ruby-position:{position};");
        var available = mode == "horizontal-tb" ? new Size(34, 300) : new Size(300, 34);
        Layout(root, available);
        var baseFont = TextMeasurement.GetFontMetrics("Hiragino Sans", 32, 400, 0);
        var noteFont = TextMeasurement.GetFontMetrics("Hiragino Sans", 16, 400, 0);
        double b = baseFont.Ascent + baseFont.Descent, n = noteFont.Ascent + noteFont.Descent;
        double advance = Math.Max(height, b + n), extra = Math.Max(0, b + n - height);
        double baseTop = (height - b) / 2 + (position == "over" ? extra : 0);
        Assert.Equal(advance * 3, Cross(root.DesiredSize, mode), 3);
        for (int i = 0; i < 3; i++)
        {
            var basis = Bounds(root, bases[i]); var note = Bounds(root, notes[i]);
            double expectedBase = i * advance + baseTop;
            double expectedNote = expectedBase + (position == "over" ? -n : b);
            // vertical-rl columns advance from the right; vertical-lr mirrors
            // the line's cross axis so that over stays on the physical right.
            double actualBase = mode == "horizontal-tb" ? basis.Top : mode is "vertical-rl" or "sideways-rl" ? 300 - basis.Right : basis.Left;
            double actualNote = mode == "horizontal-tb" ? note.Top : mode is "vertical-rl" or "sideways-rl" ? 300 - note.Right : note.Left;
            if (mode == "vertical-lr")
            {
                expectedBase = (i + 1) * advance - baseTop - b;
                expectedNote = (i + 1) * advance - (baseTop + (position == "over" ? -n : b)) - n;
            }
            Assert.Equal(expectedBase, actualBase, 3); Assert.Equal(expectedNote, actualNote, 3);
            notes[i].Select(1, 1); Assert.Equal("う", notes[i].SelectedText);
            var local = Assert.Single(notes[i].DocumentTextBounds(1, 1));
            Assert.InRange(notes[i].DocumentCharacterFromPoint(local.Center), 1, 2);
            Assert.Same(root, bases[i].VisualParent); Assert.Same(root, notes[i].VisualParent);
        }
    }

    [Fact]
    public void RetainedSourceMetricsFollowReparentingAndAnIndependentInlineBlockKeepsItsLineHeight()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var first = Create("horizontal-tb", "separate", 80);
        Css.SetStyle(first.Note, NoteStyle + "line-height:96px;"); Layout(first.Root, "horizontal-tb");
        Assert.Equal(16, Bounds(first.Root, first.Note).Height, 3);
        var ordinary = new StackPanel(); first.Root.Children.Remove(first.Note); ordinary.Children.Add(first.Note);
        Css.SetStyle(first.Note, "display:inline;font-size:16px;line-height:96px;");
        Layout(ordinary, "horizontal-tb"); Assert.False(CssRubyBoxMetrics.IsFontArea(first.Note));
        Assert.Equal(96, Bounds(ordinary, first.Note).Height, 3);
        var second = new StackPanel(); second.Children.Add(ordinary);
        Css.SetStyle(second, RootStyle("horizontal-tb", "separate", 80)); Css.SetStyle(ordinary, "display:ruby-base;");
        Layout(second, "horizontal-tb"); Assert.Equal(16, Bounds(second, first.Note).Height, 3);
        Css.SetStyle(first.Note, "display:inline-block;font-size:16px;line-height:48px;");
        Layout(second, "horizontal-tb"); Assert.Equal(48, Bounds(second, first.Note).Height, 3);
        Assert.Equal(48, first.Note.LineHeight, 3); Assert.Same(ordinary, first.Note.VisualParent);
    }

    [Theory]
    [InlineData("separate", false)] [InlineData("merge", false)] [InlineData("auto", false)]
    [InlineData("separate", true)] [InlineData("merge", true)] [InlineData("auto", true)]
    public void NativeLocalWidthKeepsItsExistingPrecedenceWhileCssWidthIsIgnored(string merge, bool annotation)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var scene = Create("horizontal-tb", merge, 80); var target = annotation ? scene.Note : scene.Base;
        Css.SetStyle(target, $"display:{(annotation ? "ruby-text" : "ruby-base")};font-size:{(annotation ? 16 : 32)}px;width:190px;");
        target.Width = 72; Layout(scene.Root, "horizontal-tb");
        Assert.Equal(72, target.RenderSize.Width, 3); Assert.Equal(72, scene.Root.DesiredSize.Width, 3);
        target.ClearValue(FrameworkElement.WidthProperty); Layout(scene.Root, "horizontal-tb");
        Assert.Equal(32, scene.Root.DesiredSize.Width, 3); Assert.Equal(190, target.Width);
    }

    public static IEnumerable<object[]> MixedAnnotationFonts()
    {
        foreach (var mode in new[] { "horizontal-tb", "vertical-rl", "vertical-lr", "sideways-rl", "sideways-lr" })
        foreach (var merge in new[] { "separate", "merge", "auto" })
        foreach (double size in new[] { 8d, 24 }) yield return [mode, merge, size];
    }

    [Theory, MemberData(nameof(MixedAnnotationFonts))]
    public void AnAnnotationLevelHasOneBaselineAcrossDifferentFontSizes(string mode, string merge, double size)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel(); var row = new StackPanel();
        var first = new TextBlock { Text = "とう", FontSize = 16 }; var last = new TextBlock { Text = "きょ", FontSize = size };
        foreach (var text in new[] { "東", "京" })
        {
            var basis = new TextBlock { Text = text, FontSize = 32 }; root.Children.Add(basis);
            Css.SetStyle(basis, "display:ruby-base;font-size:32px;");
        }
        row.Children.Add(first); row.Children.Add(last); root.Children.Add(row);
        Css.SetStyle(row, "display:ruby-text-container;");
        Css.SetStyle(first, NoteStyle); Css.SetStyle(last, $"display:ruby-text;font-size:{size}px;");
        Css.SetStyle(root, RootStyle(mode, merge, 80)); Layout(root, mode);
        var a = Bounds(root, first); var b = Bounds(root, last);
        Assert.Equal(Baseline(a, 16), Baseline(b, size), 3);
        Assert.Equal(80, Cross(root.DesiredSize, mode), 3);
        double Baseline(Rect bounds, double fontSize)
        {
            if (mode is "vertical-rl" or "vertical-lr") return bounds.Left + bounds.Width / 2;
            double start = mode == "horizontal-tb" ? bounds.Top : mode == "sideways-rl" ? 240 - bounds.Right : bounds.Left;
            return start + TextMeasurement.GetFontMetrics("Hiragino Sans", fontSize, 400, 0).Ascent;
        }
    }

    private const string NoteStyle = "display:ruby-text;font-size:16px;";
    private static string RootStyle(string mode, string merge, double height) =>
        $"display:ruby;writing-mode:{mode};ruby-merge:{merge};ruby-position:over;ruby-align:center;font-family:'Hiragino Sans';font-size:32px;line-height:{height}px;white-space:pre;text-autospace:no-autospace;";
    private static (StackPanel Root, TextBlock Base, TextBlock Note) Create(string mode, string merge, double height)
    {
        var root = new StackPanel(); var basis = new TextBlock { Text = "東", FontSize = 32 }; var note = new TextBlock { Text = "とう", FontSize = 16 };
        root.Children.Add(basis); root.Children.Add(note);
        Css.SetStyle(basis, "display:ruby-base;font-size:32px;"); Css.SetStyle(note, NoteStyle);
        Css.SetStyle(root, RootStyle(mode, merge, height));
        return (root, basis, note);
    }
    private static void Layout(FrameworkElement root, string mode)
        => Layout(root, new Size(240, 240));
    private static void Layout(FrameworkElement root, Size available)
    {
        root.InvalidateMeasure();
        for (int pass = 0; pass < 3; pass++)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue();
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(available); root.Arrange(new Rect(new Point(), available));
            root.UpdateLayout();
        }
    }
    private static double Cross(Size size, string mode) => mode == "horizontal-tb" ? size.Height : size.Width;
    private static Rect Bounds(FrameworkElement root, TextBlock text)
    {
        Assert.True(root.GetRenderMatrix().TryInvert(out var inverse));
        var transform = Matrix.Multiply(text.GetRenderMatrix(), inverse); var result = Rect.Empty;
        foreach (var box in text.DocumentTextBounds(0, text.Text.Length))
            foreach (var point in new[] { box.TopLeft, box.TopRight, box.BottomLeft, box.BottomRight })
            {
                var mapped = transform.Transform(point);
                result.Union(new Rect(mapped.X, mapped.Y, .000000001, .000000001));
            }
        return result;
    }
}
