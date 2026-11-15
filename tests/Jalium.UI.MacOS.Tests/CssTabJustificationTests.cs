using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssTabJustificationTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    internal static readonly string[] Samples = ["M M\tM M", "M M\tM M\tM M", "\tM M", "MM\t\tM M", "M\u0301M\tM\u0301 M", "M M\t春风 山林"];
    public static IEnumerable<object[]> Cases()
    {
        foreach (var path in CssSharedJustificationTests.Paths)
        foreach (var method in new[] { "auto", "inter-word", "inter-character" })
        for (var sample = 0; sample < Samples.Length; sample++)
        foreach (var last in new[] { false, true }) yield return [path, method, sample, last];
    }

    [Theory] [MemberData(nameof(Cases))]
    public void LastPreservedTabAnchorsTheWholeLineBeforeSuffixJustification(string path, string method, int sample, bool last)
    {
        using var fixture = new SharedJustificationFixture(path, Samples[sample], method, last);
        Css.SetStyle(fixture.Root, Style(path, method, last)); fixture.Layout();
        var expected = Expected(Samples[sample], method, last, 200, 40);
        var actual = fixture.CharacterStarts(); Assert.Equal(expected.Starts.Length, actual.Length);
        for (var index = 0; index < actual.Length; index++) Near(expected.Starts[index], actual[index]);
        Near(expected.End, fixture.LastEnd()); fixture.AssertSource(); fixture.AssertHitPositions();
    }

    public static IEnumerable<object[]> SizeCases()
    {
        foreach (var path in CssSharedJustificationTests.Paths)
        foreach (var tab in new[] { "0", "4", "40px" })
        foreach (var mode in new[] { "pre", "pre-wrap", "break-spaces" })
        foreach (var method in new[] { "inter-word", "inter-character" }) yield return [path, tab, mode, method];
    }

    [Theory] [MemberData(nameof(SizeCases))]
    public void NumericLengthAndZeroTabsKeepTheSameRulerInPreservedWhitespaceModes(string path, string tab, string mode, string method)
    {
        using var fixture = new SharedJustificationFixture(path, Samples[0], method, true);
        Css.SetStyle(fixture.Root, Style(path, method, true, tab, mode)); fixture.Layout();
        var step = tab == "4" ? 4 * Measure(" ") : tab == "0" ? 0 : 40;
        var expected = Expected(Samples[0], method, true, 200, step);
        AssertCoordinates(fixture, expected, zeroTabs: step == 0); fixture.AssertSource();
        if (step > 0) fixture.AssertHitPositions();
    }

    [Theory]
    [InlineData("native")] [InlineData("block")] [InlineData("rich-native")]
    [InlineData("rich-block")] [InlineData("shared")] [InlineData("nested")]
    public void ResizeTabStyleIntrinsicAndNativeOverrideCyclesRestoreTheOriginalSources(string path)
    {
        using var fixture = new SharedJustificationFixture(path, Samples[1], "inter-word", true);
        foreach (var (width, tab) in new[] { (160d, 40d), (240d, 32d), (200d, 40d) })
        {
            Css.SetStyle(fixture.Root, Style(path, "inter-word", true, tab.ToString(CultureInfo.InvariantCulture) + "px", width: width)); fixture.Layout();
            AssertCoordinates(fixture, Expected(Samples[1], "inter-word", true, width, tab));
        }
        var justified = fixture.CharacterStarts();
        fixture.Root.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Left); fixture.Layout();
        AssertCoordinates(fixture, Expected(Samples[1], "inter-word", false, 200, 40));
        fixture.Root.ClearValue(TextBlock.TextAlignmentProperty); fixture.Layout(); Assert.Equal(justified, fixture.CharacterStarts());
        Css.SetStyle(fixture.Root, Style(path, "inter-word", true) + "width:max-content;"); fixture.Layout();
        Near(Expected(Samples[1], "inter-word", false, 200, 40).End, fixture.LastEnd());
        Css.SetStyle(fixture.Root, Style(path, "none", true)); fixture.Layout(); var disabled = fixture.CharacterStarts();
        Css.SetStyle(fixture.Root, Style(path, "inter-word", true)); fixture.Layout(); Assert.Equal(justified, fixture.CharacterStarts());
        Css.SetStyle(fixture.Root, Style(path, "none", true)); fixture.Layout(); Assert.Equal(disabled, fixture.CharacterStarts());
        fixture.AssertSource(); fixture.AssertHitPositions();
    }

    public static IEnumerable<object[]> IndentCases()
    {
        foreach (var path in CssSharedJustificationTests.Paths)
        foreach (var indent in new[] { -12d, 12d }) yield return [path, indent];
    }
    [Theory] [MemberData(nameof(IndentCases))]
    public void IndentationUsesTheBlockRulerRatherThanMovingTheTabStops(string path, double indent)
    {
        using var fixture = new SharedJustificationFixture(path, Samples[0], "inter-word", true);
        Css.SetStyle(fixture.Root, Style(path, "inter-word", true) + $"text-indent:{indent.ToString(CultureInfo.InvariantCulture)}px;"); fixture.Layout();
        AssertCoordinates(fixture, Expected(Samples[0], "inter-word", true, 200, 40, indent)); fixture.AssertSource(); fixture.AssertHitPositions();
    }

    [Theory]
    [InlineData("native")] [InlineData("block")] [InlineData("rich-native")]
    [InlineData("rich-block")] [InlineData("shared")] [InlineData("nested")]
    public void OriginalInlineTabSizeChangesRefreshTheExpandedSuffix(string path)
    {
        using var fixture = new SharedJustificationFixture(path, Samples[0], "inter-word", true);
        Css.SetStyle(fixture.Root, Style(path, "inter-word", true));
        DependencyObject target = fixture.Runs.Count > 0 ? fixture.Runs[^1] : fixture.Sources[^1];
        foreach (var size in new[] { 60d, 32d, 40d })
        {
            Css.SetStyle(target, (target is TextBlock && !ReferenceEquals(target, fixture.Root) ? "display:inline;" : "") +
                (ReferenceEquals(target, fixture.Root) ? Style(path, "inter-word", true) : "") + $"tab-size:{size.ToString(CultureInfo.InvariantCulture)}px;");
            fixture.Layout(); AssertCoordinates(fixture, Expected(Samples[0], "inter-word", true, 200, size)); fixture.AssertSource(); fixture.AssertHitPositions();
        }
    }

    public static IEnumerable<object[]> BreakCases()
    {
        foreach (var path in CssSharedJustificationTests.Paths)
        foreach (var forced in new[] { false, true }) foreach (var last in new[] { false, true }) yield return [path, forced, last];
    }
    [Theory] [MemberData(nameof(BreakCases))]
    public void SoftForcedAndFinalLineRulesApplyAfterTheLastTab(string path, bool forced, bool last)
    {
        var words = Samples[0] + (forced ? "\nM M" : " MMMMMM");
        using var fixture = new SharedJustificationFixture(path, words, "inter-word", last);
        var width = forced ? 200 : 100;
        Css.SetStyle(fixture.Root, Style(path, "inter-word", last, mode: "pre-wrap", width: width)); fixture.Layout();
        var letters = fixture.Sources.SelectMany(source => StringInfo.ParseCombiningCharacters(source.Text).Where(index => source.Text[index] == 'M').Select(index =>
        {
            var segment = Assert.Single(source.GetSourceRangeSegments(index, 1)); var at = source.TransformToAncestor(fixture.Root);
            return (Left: at.X + segment.StartX, Right: at.X + segment.EndX, Baseline: Math.Round(at.Y + segment.Baseline, 3));
        })).GroupBy(letter => letter.Baseline).OrderBy(group => group.Key).ToArray();
        Assert.Equal(2, letters.Length);
        var expected = Expected(Samples[0], "inter-word", !forced || last, width, 40);
        Near(expected.Starts[0], letters[0].First().Left); Near(expected.Starts[2], letters[0].Skip(1).First().Left);
        Near(expected.End, letters[0].Max(letter => letter.Right)); fixture.AssertSource();
    }

    [Theory] [InlineData("shared", "left")] [InlineData("nested", "left")]
    [InlineData("shared", "right")] [InlineData("nested", "right")]
    public void AFloatBandKeepsTheTabsAnchoredToTheBlockContentEdge(string path, string side)
    {
        using var fixture = new SharedJustificationFixture(path, Samples[0], "inter-word", true);
        var floating = new Border(); Css.SetStyle(floating, $"float:{side};width:40px;height:100px;");
        Assert.IsType<StackPanel>(fixture.Root).Children.Insert(0, floating);
        Css.SetStyle(fixture.Root, Style(path, "inter-word", true).Replace("display:block;", "display:flow-root;")); fixture.Layout();
        AssertCoordinates(fixture, Expected(Samples[0], "inter-word", true, side == "left" ? 200 : 160, 40, side == "left" ? 40 : 0));
        fixture.AssertSource(); fixture.AssertHitPositions();
    }

    [Theory] [InlineData("rich-native")] [InlineData("rich-block")] [InlineData("shared")] [InlineData("nested")]
    public void DifferentOriginalTabSizesShareOneBlockRulerAcrossThreeSources(string path)
    {
        var words = new[] { "M M\t", "M M\t", "M M" };
        var runs = new List<Run>(); var sources = new List<TextBlock>();
        FrameworkElement root; DependencyObject target;
        if (path.StartsWith("rich"))
        {
            var text = new TextBlock { IsTextSelectionEnabled = true }; root = text; sources.Add(text);
            foreach (var word in words) { var run = new Run(word); runs.Add(run); text.Inlines.Add(run); }
            target = runs[1];
        }
        else
        {
            var panel = new StackPanel(); root = panel; Panel parent = panel;
            if (path == "nested")
            {
                var frame = new Border(); Css.SetStyle(frame, "display:inline;"); panel.Children.Add(frame);
                var inner = new StackPanel(); Css.SetStyle(inner, "display:inline;"); frame.Child = inner; parent = inner;
            }
            foreach (var word in words)
            { var text = new TextBlock { Text = word, IsTextSelectionEnabled = true }; Css.SetStyle(text, "display:inline;"); parent.Children.Add(text); sources.Add(text); }
            target = sources[1];
        }
        Css.SetStyle(root, Style(path, "inter-word", true));
        foreach (var step in new[] { 60, 32, 60 })
        {
            Css.SetStyle(target, (target is TextBlock ? "display:inline;" : "") + $"tab-size:{step}px;");
            CssSharedJustificationTests.Layout(root, 200);
            var firstInThird = path.StartsWith("rich") ? Assert.Single(sources[0].GetSourceRangeSegments(words[0].Length + words[1].Length, 1)) : Assert.Single(sources[2].GetSourceRangeSegments(0, 1));
            Near(step == 60 ? 120 : 96, firstInThird.StartX + sources[^1].TransformToAncestor(root).X);
            Near(200, sources[^1].GetSourceRangeSegments(0, sources[^1].Text.Length)[^1].EndX + sources[^1].TransformToAncestor(root).X);
            Assert.Equal(string.Concat(words), string.Concat(sources.Select(source => source.Text)));
            if (runs.Count > 0) Assert.Equal(runs, sources[0].Inlines.Cast<Run>());
        }
    }

    [Theory] [InlineData(20d)] [InlineData(38d)]
    public void AtomicBoxesBeforeATabRetainTheirSizeAndDoNotAcquireExpansion(double boxWidth)
    {
        var first = new TextBlock { Text = "M" }; var tail = new TextBlock { Text = "\tM M" }; var box = new Border();
        var root = new StackPanel(); root.Children.Add(first); root.Children.Add(box); root.Children.Add(tail);
        foreach (var source in new[] { first, tail }) Css.SetStyle(source, "display:inline;");
        Css.SetStyle(box, $"display:inline-block;width:{boxWidth.ToString(CultureInfo.InvariantCulture)}px;height:20px;");
        Css.SetStyle(root, Style("shared", "inter-word", true)); CssSharedJustificationTests.Layout(root, 200);
        Near(boxWidth, box.ActualWidth); Near(Measure("M"), box.TransformToAncestor(root).X);
        Near(boxWidth == 20 ? 40 : 80, Assert.Single(tail.GetSourceRangeSegments(1, 1)).StartX + tail.TransformToAncestor(root).X);
        Near(200, tail.GetSourceRangeSegments(0, tail.Text.Length)[^1].EndX + tail.TransformToAncestor(root).X);
        Assert.Same(box, root.Children[1]); Assert.Equal("\tM M", tail.Text);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void DocumentColumnsAndOriginalRunChangesKeepTheSameTabCoordinates(bool columns)
    {
        var words = Samples[0] + "\n" + Samples[0] + "\n" + Samples[0] + "\n" + Samples[0];
        var run = new Run(words); var paragraph = new Paragraph(run);
        var document = new FlowDocument(paragraph) { FontFamily = "Courier New", FontSize = 16, PagePadding = new(0), PageWidth = 205, PageHeight = 400 };
        Css.SetStyleSheet(document, "FlowDocument{text-align:justify-all;text-justify:inter-word;line-height:24px;white-space:pre-wrap;tab-size:40px;}Paragraph{margin:0;padding:0;" + (columns ? "column-count:2;column-gap:17px;" : "") + "}");
        using var body = new FlowDocumentPresentation(); body.SetDocument(document); CssSharedJustificationTests.Layout(body, 205);
        var proxy = Assert.Single(body.Paragraphs); var pitch = columns ? 111 : 0; var width = columns ? 94 : 205;
        for (var line = 0; line < 4; line++)
        {
            var offset = line * (Samples[0].Length + 1); var x = columns && line >= 2 ? pitch : 0;
            Near(x + 40, Assert.Single(proxy.GetSourceRangeSegments(offset + 4, 1)).StartX);
            Near(x + width, Assert.Single(proxy.GetSourceRangeSegments(offset + Samples[0].Length - 1, 1)).EndX);
        }
        Css.SetStyle(run, "tab-size:32px;"); CssSharedJustificationTests.Layout(body, 205);
        // The next32px stop is less than0.5ch beyond the preceding text.
        Near(64, Assert.Single(proxy.GetSourceRangeSegments(4, 1)).StartX);
        Css.SetStyle(run, ""); CssSharedJustificationTests.Layout(body, 205); Near(40, Assert.Single(proxy.GetSourceRangeSegments(4, 1)).StartX);
        Assert.Same(run, Assert.Single(paragraph.Inlines)); Assert.Equal(words, run.Text);
    }

    public static IEnumerable<object[]> ColumnCases()
    {
        foreach (var path in CssSharedJustificationTests.Paths)
        foreach (var alignment in new[] { "left", "justify-all" })
        foreach (var indent in new[] { 0, 4 }) yield return [path, alignment, indent];
    }
    [Theory] [MemberData(nameof(ColumnCases))]
    public void ColumnProjectionPreservesTheRulerAndOriginalSourceRanges(string path, string alignment, int indent)
    {
        var words = string.Join('\n', Enumerable.Repeat(Samples[0], 4));
        using var fixture = new SharedJustificationFixture(path, words, "inter-word", true);
        Css.SetStyle(fixture.Root, Style(path, "inter-word", true, width: 94) + $"text-align:{alignment};text-indent:{indent}px each-line;");
        var columns = new StackPanel(); columns.Children.Add(fixture.Root);
        Css.SetStyle(columns, "display:block;width:205px;column-count:2;column-gap:17px;");
        CssSharedJustificationTests.Layout(columns, 205);
        for (var line = 0; line < 4; line++)
        {
            var offset = line * (Samples[0].Length + 1); var left = line < 2 ? 0 : 111;
            Near(left + indent, Boundary(offset, false)); Near(left + 40, Boundary(offset + 4, false));
            Near(left + (alignment == "left" ? 40 + Measure("M M") : 94), Boundary(offset + 6, true));
        }
        fixture.AssertSource(); AssertVisibleHitPositions(fixture);
        double Boundary(int position, bool end)
        {
            foreach (var source in fixture.Sources)
            {
                if (position < source.Text.Length)
                {
                    var range = Assert.Single(source.GetSourceRangeSegments(position, 1));
                    return source.TransformToAncestor(columns).X + (end ? range.EndX : range.StartX);
                }
                position -= source.Text.Length;
            }
            throw new InvalidOperationException("Original source position missing");
        }
    }

    public static IEnumerable<object[]> EmojiCases()
    {
        foreach (var path in CssSharedJustificationTests.Paths)
        foreach (var text in new[] { "MM\tM👩‍👩‍👧‍👦M", "MM\tM🇨🇳M" }) yield return [path, text];
    }
    [Theory] [MemberData(nameof(EmojiCases))]
    public void EmojiSuffixesRemainSingleUnitsAfterThePreservedTab(string path, string words)
    {
        using var fixture = new SharedJustificationFixture(path, words, "inter-character", true);
        Css.SetStyle(fixture.Root, Style(path, "inter-character", true)); fixture.Layout();
        AssertCoordinates(fixture, Expected(words, "inter-character", true, 200, 40)); fixture.AssertSource(); fixture.AssertHitPositions();
    }

    public static IEnumerable<object[]> SpacingCases()
    {
        foreach (var path in CssSharedJustificationTests.Paths)
        foreach (var spacing in new[] { "letter-spacing:1px;", "word-spacing:2px;", "letter-spacing:1px;word-spacing:2px;" })
        foreach (var tab in new[] { "4", "40px" })
        foreach (var method in new[] { "inter-word", "inter-character" }) yield return [path, spacing, tab, method];
    }
    [Theory] [MemberData(nameof(SpacingCases))]
    public void LetterAndWordSpacingKeepTheNaturalPrefixAndExpandOnlyTheSuffix(string path, string spacing, string tab, string method)
    {
        using var fixture = new SharedJustificationFixture(path, Samples[0], method, true);
        Css.SetStyle(fixture.Root, Style(path, method, true, tab) + spacing + "text-align:left;"); fixture.Layout();
        var natural = fixture.CharacterStarts(); var naturalEnd = fixture.LastEnd();
        Css.SetStyle(fixture.Root, Style(path, method, true, tab) + spacing); fixture.Layout();
        var actual = fixture.CharacterStarts(); var extra = 200 - naturalEnd;
        for (var index = 0; index < natural.Length; index++)
        {
            var added = index <= 4 ? 0 : method == "inter-word" ? index == 6 ? extra : 0 : (index - 4) * extra / 2;
            Near(natural[index] + added, actual[index]);
        }
        Near(200, fixture.LastEnd()); fixture.AssertSource(); fixture.AssertHitPositions();
    }

    public static IEnumerable<object[]> FontMetricCases()
    {
        foreach (var path in new[] { "rich-native", "rich-block", "shared", "nested" })
        foreach (var prefix in new[] { "", "MMM" }) foreach (var size in new[] { 8, 32 })
        foreach (var tab in new[] { "4", "40px" }) yield return [path, prefix, size, tab];
    }
    [Theory] [MemberData(nameof(FontMetricCases))]
    public void NumericTabsUseTheBlockFontButTheMinimumAdvanceUsesTheTabFont(string path, string prefix, int size, string tab)
    {
        var rich = path.StartsWith("rich"); var runs = new List<Run>(); var sources = new List<TextBlock>();
        FrameworkElement root; DependencyObject tabSource; TextBlock tail;
        if (rich)
        {
            var text = new TextBlock(); root = tail = text;
            foreach (var value in new[] { prefix, "\t", "M M" }) { var run = new Run(value); runs.Add(run); text.Inlines.Add(run); }
            Css.SetStyle(runs[0], "font-size:18px;"); tabSource = runs[1];
        }
        else
        {
            var panel = new StackPanel(); root = panel; Panel parent = panel;
            if (path == "nested")
            {
                var frame = new Border(); Css.SetStyle(frame, "display:inline;"); panel.Children.Add(frame);
                var inner = new StackPanel(); Css.SetStyle(inner, "display:inline;"); frame.Child = inner; parent = inner;
            }
            foreach (var value in new[] { prefix, "\t", "M M" })
            { var text = new TextBlock { Text = value }; Css.SetStyle(text, "display:inline;"); parent.Children.Add(text); sources.Add(text); }
            Css.SetStyle(sources[0], "display:inline;font-size:18px;"); tabSource = sources[1]; tail = sources[2];
        }
        Css.SetStyle(root, Style(path, "inter-word", true));
        foreach (var fontSize in new[] { size, size == 8 ? 32 : 8, size })
        {
            Css.SetStyle(tabSource, (tabSource is TextBlock ? "display:inline;" : "") + $"font-size:{fontSize}px;tab-size:{tab};");
            CssSharedJustificationTests.Layout(root, 200);
            var step = tab == "4" ? 4 * Measure(" ") : 40;
            var prefixAdvance = MeasureAt(prefix, 18); var minimum = MeasureAt("0", fontSize) / 2;
            var stop = (Math.Floor(prefixAdvance / step) + 1) * step;
            while (stop - prefixAdvance < minimum - .000001) stop += step;
            var start = rich ? prefix.Length + 1 : 0; var segment = Assert.Single(tail.GetSourceRangeSegments(start, 1));
            Near(stop, segment.StartX + tail.TransformToAncestor(root).X);
            Near(200, tail.GetSourceRangeSegments(0, tail.Text.Length)[^1].EndX + tail.TransformToAncestor(root).X);
            Assert.Equal(prefix + "\tM M", rich ? tail.Text : string.Concat(sources.Select(source => source.Text)));
            if (rich) Assert.Equal(runs, tail.Inlines.Cast<Run>());
        }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void NumericTabSpacingComesFromTheBlockEvenWhenTheInlineFontAndSpacingDiffer(bool spaced)
    {
        var root = new StackPanel(); var tab = new TextBlock { Text = "\t" }; var box = new Border(); root.Children.Add(tab); root.Children.Add(box);
        Css.SetStyle(root, Style("shared", "inter-word", false) + "text-align:left;" + (spaced ? "letter-spacing:1px;word-spacing:2px;" : ""));
        Css.SetStyle(tab, "display:inline;font-size:32px;tab-size:4;letter-spacing:5px;word-spacing:14px;");
        Css.SetStyle(box, "display:inline-block;width:20px;height:20px;"); CssSharedJustificationTests.Layout(root, 200);
        Near(4 * (Measure(" ") + (spaced ? 3 : 0)), box.TransformToAncestor(root).X); Near(20, box.ActualWidth); Assert.Equal("\t", tab.Text);
    }

    private static double MeasureAt(string text, double size)
    {
        if (text.Length == 0) return 0;
        var formatted = new Jalium.UI.Media.FormattedText(text, "Courier New", size); Assert.True(TextMeasurement.MeasureText(formatted));
        return formatted.WidthIncludingTrailingWhitespace;
    }

    private static void AssertCoordinates(SharedJustificationFixture fixture, (double[] Starts, double End) expected, bool zeroTabs = false)
    {
        if (zeroTabs)
        {
            var glyph = 0;
            foreach (var source in fixture.Sources)
            {
                var boundaries = StringInfo.ParseCombiningCharacters(source.Text).Append(source.Text.Length).ToArray();
                foreach (var pair in boundaries.Zip(boundaries.Skip(1)))
                {
                    var segments = source.GetSourceRangeSegments(pair.First, pair.Second - pair.First);
                    if (source.Text[pair.First] == '\t')
                    { Assert.Empty(segments); source.Select(pair.First, 1); Assert.Equal("\t", source.SelectedText); source.Select(0, 0); }
                    else
                    {
                        var segment = Assert.Single(segments); Near(expected.Starts[glyph], segment.StartX + source.TransformToAncestor(fixture.Root).X);
                        var pointer = source.GetPositionFromPoint(new(segment.StartX + .2, segment.Baseline - 2), true);
                        Assert.NotNull(pointer); Assert.Equal(pair.First, source.ContentStart.GetOffsetToPosition(pointer));
                    }
                    glyph++;
                }
            }
            Assert.Equal(expected.Starts.Length, glyph); Near(expected.End, fixture.LastEnd()); return;
        }
        var actual = fixture.CharacterStarts(); Assert.Equal(expected.Starts.Length, actual.Length);
        for (var index = 0; index < actual.Length; index++) Near(expected.Starts[index], actual[index]);
        Near(expected.End, fixture.LastEnd());
    }

    private static void AssertVisibleHitPositions(SharedJustificationFixture fixture)
    {
        foreach (var source in fixture.Sources)
        {
            var boundaries = StringInfo.ParseCombiningCharacters(source.Text).Append(source.Text.Length).ToArray();
            foreach (var pair in boundaries.Zip(boundaries.Skip(1)))
            {
                var value = source.Text[pair.First..pair.Second];
                if (value is "\n" or "\r" or "\r\n") continue;
                var segment = Assert.Single(source.GetSourceRangeSegments(pair.First, pair.Second - pair.First));
                var pointer = source.GetPositionFromPoint(new(segment.StartX + .2, segment.Baseline - 2), true);
                Assert.NotNull(pointer); Assert.Equal(pair.First, source.ContentStart.GetOffsetToPosition(pointer));
            }
        }
    }

    internal static string Style(string path, string method, bool last, string tab = "40px", string mode = "pre", double width = 200) =>
        SharedJustificationFixture.Style(path, method, last, width) + $"white-space:{mode};tab-size:{tab};";

    // Font advances are measured independently. Stops are prescribed multiples
    // of40px; no Jalium private tab or justification helper supplies the oracle.
    internal static (double[] Starts, double End) Expected(string words, string method, bool last, double width, double step, double origin = 0)
    {
        var starts = StringInfo.ParseCombiningCharacters(words).Append(words.Length).ToArray();
        var glyphs = starts.Zip(starts.Skip(1)).Select(pair => words[pair.First..pair.Second]).ToArray();
        var positions = new double[glyphs.Length]; var cursor = origin;
        for (var index = 0; index < glyphs.Length; index++)
        {
            positions[index] = cursor;
            if (glyphs[index] == "\t")
            {
                if (step <= 0) continue;
                var target = (Math.Floor(cursor / step) + 1) * step;
                while (target - cursor < Measure("0") / 2 - .000001) target += step;
                cursor = target;
            }
            else cursor += Measure(glyphs[index]);
        }
        if (!last) return (positions, cursor);
        var lastTab = Array.LastIndexOf(glyphs, "\t");
        var opportunities = new bool[glyphs.Length];
        for (var index = lastTab + 1; index < glyphs.Length - 1; index++)
        {
            opportunities[index] = method == "inter-character" ||
                method is "auto" or "inter-word" && glyphs[index] == " " ||
                method == "auto" && !string.IsNullOrWhiteSpace(glyphs[index]) && !string.IsNullOrWhiteSpace(glyphs[index + 1]) &&
                (glyphs[index][0] >= 0x4e00 || glyphs[index + 1][0] >= 0x4e00);
        }
        var count = opportunities.Count(value => value);
        Assert.True(count > 0, "This oracle describes expandable suffixes; fallback alignment has separate coverage.");
        var extra = (width - cursor) / count; var added = 0d;
        for (var index = 0; index < positions.Length; index++)
        {
            positions[index] += added;
            if (opportunities[index]) added += extra;
        }
        return (positions, cursor + added);
    }
    internal static double Measure(string text) => CssSharedJustificationTests.Measure(text);
    internal static void Near(double expected, double actual) => CssSharedJustificationTests.Near(expected, actual);
}
