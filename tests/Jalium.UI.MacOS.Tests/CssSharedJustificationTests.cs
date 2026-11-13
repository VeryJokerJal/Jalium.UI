using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSharedJustificationTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public CssSharedJustificationTests() => AccessTextTemplateTestHelper.Register();
    public void Dispose() => _context.Dispose();
    internal static readonly string[] Paths = ["native", "block", "rich-native", "rich-block", "shared", "nested"];
    internal static readonly string[] Methods = ["auto", "inter-word", "inter-character", "none"];
    internal static readonly string[] Samples = ["MM MM MM", "春风吹过山林", "M\u0301M M\u0301M"];
    public static IEnumerable<object[]> UniformCases()
    {
        foreach (var path in Paths) foreach (var method in Methods)
        for (var sample = 0; sample < Samples.Length; sample++) foreach (var last in new[] { false, true })
            yield return [path, method, sample, last];
    }

    [Theory] [MemberData(nameof(UniformCases))]
    public void WholeParagraphDistributesSpaceAcrossOriginalSources(string path, string method, int sample, bool last)
    {
        using var f = new SharedJustificationFixture(path, Samples[sample], method, last); f.Layout();
        var expected = Expected(Samples[sample], method, last, 160);
        var actual = f.CharacterStarts(); Assert.Equal(expected.Starts.Length, actual.Length);
        for (var i = 0; i < actual.Length; i++) Near(expected.Starts[i], actual[i]);
        Near(expected.End, f.LastEnd()); f.AssertSource(); f.AssertHitPositions();
    }

    [Theory]
    [InlineData("native")] [InlineData("block")] [InlineData("rich-native")]
    [InlineData("rich-block")] [InlineData("shared")] [InlineData("nested")]
    public void StyleResizeIntrinsicAndNativeOverrideCyclesRestoreTheOriginalCoordinates(string path)
    {
        using var f = new SharedJustificationFixture(path, Samples[0], "inter-word", true); f.Layout();
        var first = f.CharacterStarts();
        foreach (var width in new[] { 120d, 200d, 160d })
        {
            Css.SetStyle(f.Root, SharedJustificationFixture.Style(path, "inter-word", true, width)); f.Layout();
            Near(width, f.LastEnd());
            Css.SetStyle(f.Root, SharedJustificationFixture.Style(path, "inter-word", true, width) + "width:max-content;"); f.Layout();
            Near(Measure(Samples[0]), f.LastEnd());
        }
        Css.SetStyle(f.Root, SharedJustificationFixture.Style(path, "inter-word", true)); f.Layout();
        f.Root.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Left); f.Layout(); Near(0, f.CharacterStarts()[0]); Near(Measure(Samples[0]), f.LastEnd());
        f.Root.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right); f.Layout(); Near(160 - Measure(Samples[0]), f.CharacterStarts()[0]); Near(160, f.LastEnd());
        f.Root.ClearValue(TextBlock.TextAlignmentProperty); f.Layout(); Assert.Equal(first, f.CharacterStarts());
        f.Root.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Justify); f.Layout(); Near(Measure(Samples[0]), f.LastEnd());
        f.Root.ClearValue(TextBlock.TextAlignmentProperty); f.Layout(); Assert.Equal(first, f.CharacterStarts()); f.AssertSource();
    }

    public static IEnumerable<object[]> BreakCases()
    {
        foreach (var path in Paths) foreach (var forced in new[] { false, true }) foreach (var last in new[] { false, true }) yield return [path, forced, last];
    }
    [Theory] [MemberData(nameof(BreakCases))]
    public void SoftForcedAndFinalLinesUseParagraphLevelAlignment(string path, bool forced, bool last)
    {
        using var f = new SharedJustificationFixture(path, forced ? "MM MM\nMM MM" : "MM MM MM MM", "inter-word", last);
        Css.SetStyle(f.Root, SharedJustificationFixture.Style(path, "inter-word", last, 68) + "white-space:pre-line;"); f.Layout();
        var lines = f.Sources.SelectMany(source => source.GetSourceRangeSegments(0, source.Text.Length).Select(segment =>
        {
            var at = source.TransformToAncestor(f.Root);
            return (Left: segment.StartX + at.X, Right: segment.EndX + at.X, Baseline: Math.Round(segment.Baseline + at.Y, 3));
        })).GroupBy(segment => segment.Baseline).OrderBy(group => group.Key).ToArray();
        Assert.Equal(2, lines.Length);
        Near(!forced || last ? 68 : Measure("MM MM"), lines[0].Max(segment => segment.Right));
        Near(last ? 68 : Measure("MM MM"), lines[1].Max(segment => segment.Right)); f.AssertSource();
    }

    [Theory]
    [InlineData("native")] [InlineData("block")] [InlineData("rich-native")]
    [InlineData("rich-block")] [InlineData("shared")] [InlineData("nested")]
    public void NoBreakSpacesExpandButZeroAdvanceSeparatorsCannotAcquireJustificationSpace(string path)
    {
        using var f = new SharedJustificationFixture(path, "MM\u00a0MM\u00a0MM", "inter-word", true); f.Layout();
        Near(160, f.LastEnd()); Near(160 - Measure("MM"), f.CharacterStarts()[^2]);
        using var zero = new SharedJustificationFixture(path, Samples[0], "inter-word", true);
        Css.SetStyle(zero.Root, Css.GetStyle(zero.Root) + "word-spacing:-16px;"); zero.Layout();
        var natural = Measure("MMMMMM");
        var source = zero.Sources[0];
        Near((160 - natural) / 2, source.GetSourceRangeSegments(0, 1)[0].StartX + source.TransformToAncestor(zero.Root).X);
        Near((160 + natural) / 2, zero.LastEnd()); zero.AssertSource();
    }

    [Theory] [InlineData("rich-native")] [InlineData("rich-block")] [InlineData("shared")] [InlineData("nested")]
    public void InlineMethodChangesAndCommonAncestorBoundariesRefreshTheSameSources(string path)
    {
        using var f = new SharedJustificationFixture(path, Samples[0], "inter-word", true); f.Layout(); Near(160, f.LastEnd());
        var targets = f.Runs.Count > 0 ? f.Runs.Cast<DependencyObject>().ToArray() : f.Sources.Cast<DependencyObject>().ToArray();
        foreach (var target in targets) Css.SetStyle(target, (target is TextBlock ? "display:inline;" : "") + "text-justify:none;");
        f.Layout(); Near((160 + Measure(Samples[0])) / 2, f.LastEnd());
        Css.SetStyle(f.Root, SharedJustificationFixture.Style(path, "inter-character", true)); f.Layout();
        // Only the seam is governed by the common ancestor. Internal opportunities
        // remain disabled by each original source's explicit none declaration.
        Near(160, f.LastEnd()); Near(Measure("M"), f.CharacterStarts()[1]);
        foreach (var target in targets) Css.SetStyle(target, target is TextBlock ? "display:inline;" : "");
        Css.SetStyle(f.Root, SharedJustificationFixture.Style(path, "inter-word", true)); f.Layout(); Near(160, f.LastEnd()); f.AssertSource();
    }

    [Fact]
    public void ConsecutiveAtomicInlinesFormOneJustificationUnit()
    {
        var root = new StackPanel(); var first = new TextBlock { Text = "M" }; var last = new TextBlock { Text = "M" };
        var a = new Border(); var b = new Border(); root.Children.Add(first); root.Children.Add(a); root.Children.Add(b); root.Children.Add(last);
        Css.SetStyle(root, SharedJustificationFixture.Style("shared", "inter-character", true));
        foreach (var text in new[] { first, last }) Css.SetStyle(text, "display:inline;");
        foreach (var box in new[] { a, b }) Css.SetStyle(box, "display:inline-block;width:20px;height:20px;");
        Layout(root, 200); var width = Measure("M"); var extra = (160 - 2 * width - 40) / 2;
        Near(width + extra, a.TransformToAncestor(root).X); Near(width + extra + 20, b.TransformToAncestor(root).X);
        Near(160 - width, last.GetSourceRangeSegments(0, 1)[0].StartX + last.TransformToAncestor(root).X);
    }

    [Theory] [InlineData("shared", "left")] [InlineData("nested", "left")] [InlineData("shared", "right")] [InlineData("nested", "right")]
    public void FloatBandsDistributeOnlyTheirAvailableInlineWidth(string path, string side)
    {
        using var f = new SharedJustificationFixture(path, Samples[0], "inter-word", true);
        var floating = new Border(); Css.SetStyle(floating, $"float:{side};width:40px;height:100px;"); Assert.IsType<StackPanel>(f.Root).Children.Insert(0, floating);
        Css.SetStyle(f.Root, Css.GetStyle(f.Root).Replace("display:block;", "display:flow-root;")); f.Layout();
        var expected = Expected(Samples[0], "inter-word", true, 120).Starts; var actual = f.CharacterStarts(); var left = side == "left" ? 40 : 0;
        for (var index = 0; index < expected.Length; index++) Near(left + expected[index], actual[index]);
        Near(left + 120, f.LastEnd()); f.AssertSource();
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void DocumentRunMethodChangesReachTheExistingParagraphProxy(bool columns)
    {
        var run = new Run(Samples[0]); var paragraph = new Paragraph(run);
        var document = new FlowDocument(paragraph) { FontFamily = "Courier New", FontSize = 16, PagePadding = new(0), PageWidth = 160, PageHeight = 400 };
        Css.SetStyleSheet(document, "FlowDocument{text-align:justify-all;text-justify:inter-word;line-height:24px;}Paragraph{margin:0;padding:0;" + (columns ? "column-count:2;column-gap:0;" : "") + "}");
        using var body = new FlowDocumentPresentation(); body.SetDocument(document); Layout(body, 160);
        var proxy = Assert.Single(body.Paragraphs); var available = columns ? 80 : 160;
        Near(available, proxy.GetSourceRangeSegments(0, proxy.Text.Length)[^1].EndX);
        Css.SetStyle(run, "text-justify:none;"); Layout(body, 160);
        Near((available + Measure(Samples[0])) / 2, proxy.GetSourceRangeSegments(0, proxy.Text.Length)[^1].EndX);
        Css.SetStyle(run, ""); Layout(body, 160); Near(available, proxy.GetSourceRangeSegments(0, proxy.Text.Length)[^1].EndX);
        Assert.Same(run, Assert.Single(paragraph.Inlines));
    }

    [Theory] [InlineData("shared", false)] [InlineData("shared", true)] [InlineData("nested", false)] [InlineData("nested", true)]
    public void InternalSourceSeamsUseTheParagraphClipAndRetainRasterCoverage(string path, bool justify)
    {
        using var f = new SharedJustificationFixture(path, Samples[0], "inter-word", justify); f.Layout();
        var first = f.Sources[0];
        var clip = (RectangleGeometry)typeof(TextBlock).GetMethod("GetRenderClip", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(first, null)!;
        Near(160, clip.Rect.Right + first.TransformToAncestor(f.Root).X);
        Assert.True(first.GetExtraDirtyPadding() >= 1);
        first.ClipToBounds = true; f.Layout();
        var explicitClip = Assert.IsType<RectangleGeometry>(first.GetLayoutClip()); Near(first.RenderSize.Width, explicitClip.Rect.Width);
    }

    public static IEnumerable<object[]> ClusterCases()
    {
        foreach (var path in Paths) foreach (var words in new[] { "M👩‍👩‍👧‍👦M", "M🇨🇳M" }) yield return [path, words];
    }
    [Theory] [MemberData(nameof(ClusterCases))]
    public void EmojiSequencesRemainSingleUnitsForSpacingSelectionAndHitTesting(string path, string words)
    {
        using var f = new SharedJustificationFixture(path, words, "inter-character", true); f.Layout();
        var expected = Expected(words, "inter-character", true, 160); var actual = f.CharacterStarts(); Assert.Equal(3, actual.Length);
        for (var i = 0; i < actual.Length; i++) Near(expected.Starts[i], actual[i]);
        Near(160, f.LastEnd()); f.AssertSource(); f.AssertHitPositions();
    }

    public static IEnumerable<object[]> CursiveCases()
    {
        foreach (var path in Paths) foreach (var words in new[] { "سلام", "ابت", "ᠮᠣᠩ" }) yield return [path, words];
    }
    [Theory] [MemberData(nameof(CursiveCases))]
    public void CursiveScriptRunsAcquireNoInternalJustificationGaps(string path, string words)
    {
        using var f = new SharedJustificationFixture(path, words, "inter-character", true);
        Css.SetStyle(f.Root, SharedJustificationFixture.Style(path, "inter-character", true) + "text-align:start;"); f.Layout();
        var before = Extent(); var natural = before.Right - before.Left;
        Css.SetStyle(f.Root, SharedJustificationFixture.Style(path, "inter-character", true)); f.Layout(); var after = Extent();
        Near(natural, after.Right - after.Left); Near((160 - natural) / 2, after.Left); f.AssertSource();
        (double Left, double Right) Extent()
        {
            var ranges = f.Sources.SelectMany(text => text.GetSourceRangeSegments(0, text.Text.Length).Select(segment =>
                (Left: segment.StartX + text.TransformToAncestor(f.Root).X, Right: segment.EndX + text.TransformToAncestor(f.Root).X))).ToArray();
            return (ranges.Min(value => value.Left), ranges.Max(value => value.Right));
        }
    }

    internal static void Layout(FrameworkElement root, double width)
    {
        for (var pass = 0; pass < 4; pass++)
        { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); root.Measure(new(width, 1000)); root.Arrange(new(0, 0, width, root.DesiredSize.Height)); root.UpdateLayout(); }
    }

    // Independent monospace/font-width oracle. Test content has no kerning pairs,
    // punctuation rules or cross-font shaping; Unicode graphemes stay indivisible.
    internal static (double[] Starts, double End) Expected(string words, string method, bool last, double width)
    {
        var boundaries = StringInfo.ParseCombiningCharacters(words).Append(words.Length).ToArray();
        var clusters = boundaries.Zip(boundaries.Skip(1)).Select(pair => words[pair.First..pair.Second]).ToArray();
        var advances = clusters.Select(Measure).ToArray();
        var after = new bool[clusters.Length];
        for (var i = 0; i < clusters.Length; i++)
            after[i] = method switch
            {
                "inter-character" => i + 1 < clusters.Length,
                "inter-word" => clusters[i] == " ",
                "auto" => clusters[i] == " " || i + 1 < clusters.Length && words[0] == '春',
                _ => false,
            };
        var count = after.Count(value => value); var natural = advances.Sum();
        var extra = last && count > 0 ? Math.Max(0, width - natural) / count : 0;
        var x = last && count == 0 ? (width - natural) / 2 : 0;
        var starts = new List<double>();
        // CSS Text applies inter-character expansion half on each side. A
        // source cell starts at the midpoint of the preceding virtual gap;
        // the glyph itself still starts after that entire gap.
        for (var i = 0; i < clusters.Length; i++)
        {
            bool precedingCharacterGap = i > 0 && after[i - 1] &&
                (method == "inter-character" || method == "auto" && words[0] == '春');
            starts.Add(x - (precedingCharacterGap ? extra / 2 : 0)); x += advances[i] + (after[i] ? extra : 0);
        }
        return (starts.ToArray(), x);
    }
    internal static double Measure(string words)
    {
        var text = new FormattedText(words, "Courier New", 16); Assert.True(TextMeasurement.MeasureText(text));
        return text.WidthIncludingTrailingWhitespace;
    }
    internal static void Near(double expected, double actual) => Assert.InRange(actual, expected - .07, expected + .07);
}

internal sealed class SharedJustificationFixture : IDisposable
{
    internal FrameworkElement Root { get; }
    internal readonly List<TextBlock> Sources = [];
    internal readonly List<Run> Runs = [];
    private readonly string _words;
    internal SharedJustificationFixture(string path, string words, string method, bool last, bool omitRootStyle = false)
    {
        _words = words;
        var boundaries = StringInfo.ParseCombiningCharacters(words);
        var split = boundaries.Length > 2 ? boundaries[2] : words.Length;
        var chunks = new[] { words[..split], words[split..] };
        TextBlock Text(string value)
        { var result = new TextBlock { Text = value, IsTextSelectionEnabled = true }; Sources.Add(result); return result; }
        if (path is "native" or "block" or "rich-native" or "rich-block")
        {
            var rich = path.StartsWith("rich"); var text = Text(rich ? "" : words); Root = text;
            if (rich) foreach (var chunk in chunks)
            { var run = new Run(chunk); Runs.Add(run); text.Inlines.Add(run); }
        }
        else
        {
            var root = new StackPanel(); Root = root;
            Panel parent = root;
            if (path == "nested")
            {
                var frame = new Border(); Css.SetStyle(frame, "display:inline;"); root.Children.Add(frame);
                var inner = new StackPanel(); Css.SetStyle(inner, "display:inline;"); frame.Child = inner; parent = inner;
            }
            foreach (var chunk in chunks)
            { var text = Text(chunk); Css.SetStyle(text, "display:inline;"); parent.Children.Add(text); }
        }
        if (!omitRootStyle) Css.SetStyle(Root, Style(path, method, last));
    }
    internal static string Style(string path, string method, bool last, double width = 160) =>
        (path is "native" or "rich-native" ? "" : "display:block;") +
        $"font-family:'Courier New';font-size:16px;line-height:24px;white-space:normal;width:{width.ToString(CultureInfo.InvariantCulture)}px;text-autospace:no-autospace;text-align:justify;text-align-last:{(last ? "justify" : "auto")};text-justify:{method};";
    internal void Layout()
    {
        for (var pass = 0; pass < 4; pass++)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            Root.Measure(new(200, 1000)); Root.Arrange(new(0, 0, 200, Root.DesiredSize.Height)); Root.UpdateLayout();
        }
    }
    internal double[] CharacterStarts() => Sources.SelectMany(text =>
    {
        var boundaries = StringInfo.ParseCombiningCharacters(text.Text).Append(text.Text.Length).ToArray();
        var offset = text.TransformToAncestor(Root).X;
        return boundaries.Zip(boundaries.Skip(1)).Select(pair => Assert.Single(text.GetSourceRangeSegments(pair.First, pair.Second - pair.First)).StartX + offset);
    }).ToArray();
    internal double LastEnd()
    { var text = Sources[^1]; return text.GetSourceRangeSegments(0, text.Text.Length)[^1].EndX + text.TransformToAncestor(Root).X; }
    internal void AssertSource()
    {
        Assert.Equal(_words, string.Concat(Sources.Select(source => source.Text)));
        if (Runs.Count > 0) Assert.Equal(Runs, Sources[0].Inlines.Cast<Run>());
        foreach (var text in Sources) { text.SelectAll(); Assert.Equal(text.Text, text.SelectedText); text.Select(0, 0); }
    }
    internal void AssertHitPositions()
    {
        foreach (var text in Sources)
        {
            var boundaries = StringInfo.ParseCombiningCharacters(text.Text).Append(text.Text.Length).ToArray();
            foreach (var pair in boundaries.Zip(boundaries.Skip(1)))
            {
                var segment = Assert.Single(text.GetSourceRangeSegments(pair.First, pair.Second - pair.First));
                var point = new Point(segment.StartX + Math.Min(.2, (segment.EndX - segment.StartX) / 4), segment.Baseline - 2);
                var pointer = Assert.IsType<TextPointer>(text.GetPositionFromPoint(point, true));
                Assert.Equal(pair.First, text.ContentStart.GetOffsetToPosition(pointer));
            }
        }
    }
    public void Dispose() { }
}
