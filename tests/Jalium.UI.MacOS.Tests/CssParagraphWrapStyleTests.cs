using System.Globalization;
using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssParagraphWrapStyleTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();
    internal const string Words = "MMMM MMMM MMMM MMMM MMMM MMMM M";
    internal static readonly string[] Paths = ["native", "block", "rich-native", "rich-block", "shared", "nested"];

    public static IEnumerable<object[]> HitCases()
    {
        foreach (var path in Paths.Take(4))
        foreach (var words in new[] { "M M", "M M M M", "M M\tM M", "春风 山林 春风" })
        foreach (var method in new[] { "inter-word", "inter-character" })
        foreach (var indent in new[] { "0px", "10%" }) yield return [path, words, method, indent];
    }

    [Theory] [MemberData(nameof(HitCases))]
    public void ProjectedColumnsAcceptOrdinaryHitsOnTheirPaintedLastGlyphs(string path, string row, string method, string indent)
    {
        var root = new StackPanel(); var text = new TextBlock { IsTextSelectionEnabled = true };
        var words = string.Join('\n', Enumerable.Repeat(row, 4));
        var runs = new[] { new Run(words[..2]), new Run(words[2..]) };
        if (path.StartsWith("rich")) foreach (var run in runs) text.Inlines.Add(run); else text.Text = words;
        root.Children.Add(text);
        Css.SetStyle(root, $"display:block;width:205px;column-count:2;column-gap:17px;font-family:'Courier New';font-size:16px;line-height:24px;white-space:pre;tab-size:40px;text-align:justify-all;text-justify:{method};text-indent:{indent} each-line;");
        if (path.EndsWith("block")) Css.SetStyle(text, "display:block;");
        WrapStyleFixture.Layout(root, 205);
        for (var line = 0; line < 4; line++)
        {
            var index = line * (row.Length + 1) + row.Length - 1;
            var range = Assert.Single(text.GetSourceRangeSegments(index, 1));
            var hit = Assert.IsType<TextPointer>(text.GetPositionFromPoint(new(range.StartX + .2, range.Baseline - 2), false));
            Assert.Equal(index, text.ContentStart.GetOffsetToPosition(hit));
        }
        // Preserved unbreakable text can itself overflow into the column gap.
        // Only a genuinely empty gap must reject an ordinary hit.
        if (text.GetSourceRangeSegments(0, row.Length).Max(range => range.EndX) < 101)
            Assert.Null(text.GetPositionFromPoint(new(102, 5), false));
        text.SelectAll(); Assert.Equal(words, text.SelectedText);
        if (path.StartsWith("rich")) Assert.Equal(runs, text.Inlines.Cast<Run>());
        Assert.Same(text, Assert.Single(root.Children));
    }

    public static IEnumerable<object[]> WrapCases()
    {
        foreach (var path in Paths)
        foreach (var style in new[] { "balance", "pretty", "avoid-short-last-line" }) yield return [path, style];
    }

    [Theory] [MemberData(nameof(WrapCases))]
    public void ParagraphWrapStylesImproveRaggednessAndKeepGreedyLineCount(string path, string style)
    {
        using var f = new WrapStyleFixture(path, Words); f.SetStyle("auto"); f.Layout();
        var greedy = f.Lines(); Assert.Equal(3, greedy.Length);
        f.SetStyle(style); f.Layout(); var result = f.Lines();
        Assert.Equal(greedy.Length, result.Length);
        Assert.True(Variance(result) < Variance(greedy) - 10, $"{path}/{style}: {string.Join(',', result.Select(line => line.Width))}");
        Assert.True(result[^1].Width > greedy[^1].Width + 10);
        Assert.All(result, line => Assert.InRange(line.Width, 0, 150.1));
        f.AssertSourceAndHits();
    }

    [Theory] [MemberData(nameof(WrapCases))]
    public void ForcedBreakGroupsBalanceIndependentlyAndRetainEmptyLines(string path, string style)
    {
        using var f = new WrapStyleFixture(path, Words + "\n\n" + Words + "\n");
        f.SetStyle("auto", extra: "white-space:pre-line;"); f.Layout(); var greedy = f.Lines();
        f.SetStyle(style, extra: "white-space:pre-line;"); f.Layout(); var result = f.Lines();
        Assert.Equal(6, result.Length); Assert.Equal(greedy.Length, result.Length);
        Assert.All(result.Take(3), line => Assert.True(line.End <= Words.Length));
        Assert.All(result.Skip(3), line => Assert.True(line.Start >= Words.Length + 2));
        Assert.InRange(result[3].Baseline - result[2].Baseline, 47.9, 48.1);
        Assert.True(Variance(result.Take(3).ToArray()) < Variance(greedy.Take(3).ToArray()) - 10);
        Assert.True(Variance(result.Skip(3).ToArray()) < Variance(greedy.Skip(3).ToArray()) - 10);
        f.AssertSourceAndHits();
    }

    [Theory] [MemberData(nameof(WrapCases))]
    public void ResizeStyleClearingAndNativeNoWrapCyclesReuseTheSameSources(string path, string style)
    {
        using var f = new WrapStyleFixture(path, Words); f.SetStyle("auto"); f.Layout(); var greedy = f.Lines();
        f.SetStyle(style); f.Layout(); var balanced = f.Lines();
        foreach (var width in new[] { 90, 240, 150 })
        {
            f.SetStyle("auto", width); f.Layout(); var count = f.Lines().Length;
            f.SetStyle(style, width); f.Layout(); Assert.Equal(count, f.Lines().Length); f.AssertSourceAndHits();
        }
        Assert.Equal(balanced, f.Lines());
        f.Root.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap); f.Layout(); Assert.Single(f.Lines());
        f.Root.ClearValue(TextBlock.TextWrappingProperty); f.Layout(); Assert.Equal(balanced, f.Lines());
        Css.SetStyle(f.Root, Css.GetStyle(f.Root).Replace($"text-wrap-style:{style};", "")); f.Layout(); Assert.Equal(greedy, f.Lines());
        f.SetStyle("stable"); f.Layout(); Assert.Equal(greedy, f.Lines());
        f.SetStyle(style, extra: "text-wrap:nowrap;"); f.Layout(); Assert.Single(f.Lines());
        f.SetStyle("auto", extra: $"text-wrap:wrap {style};"); f.Layout(); Assert.Equal(balanced, f.Lines());
        f.AssertSourceAndHits();
    }

    public static IEnumerable<object[]> BoundaryCases()
    {
        foreach (var path in Paths)
        foreach (var words in new[] { "MMMM", Words, "春风吹过山林，也吹过河岸。我们沿着小路慢慢走。", "Ma\u0301 Ma\u0301 Ma\u0301 Ma\u0301 Ma\u0301 Ma\u0301 M", "M👩‍👩‍👧‍👦 M🇨🇳 M👩‍👩‍👧‍👦 M🇨🇳 M", "MM\u00a0MM MM\u2060MM MM MM M", "MMMMMMMMMMMMMMMMMMMMMMMMMMMMMM" })
        foreach (var width in new[] { 90, 150, 220 }) yield return [path, words, width];
    }

    [Theory] [MemberData(nameof(BoundaryCases))]
    public void BalancePreservesLineCountGraphemesAndExistingBreakRules(string path, string words, double width)
    {
        using var f = new WrapStyleFixture(path, words);
        f.SetStyle("auto", width); f.Layout(); var greedy = f.Lines();
        f.SetStyle("balance", width); f.Layout(); var result = f.Lines();
        Assert.Equal(greedy.Length, result.Length);
        Assert.True(Variance(result) <= Variance(greedy) + .5);
        var boundaries = StringInfo.ParseCombiningCharacters(words).Append(words.Length).ToArray();
        Assert.All(result, line => { Assert.Contains(line.Start, boundaries); Assert.Contains(line.End, boundaries); });
        f.AssertSourceAndHits();
    }

    public static IEnumerable<object[]> WhiteSpaceCases()
    {
        foreach (var path in Paths)
        foreach (var mode in new[] { "normal", "pre-line", "pre-wrap", "break-spaces" })
        foreach (var indent in new[] { "10%", "10% hanging", "10% each-line" }) yield return [path, mode, indent];
    }

    [Theory] [MemberData(nameof(WhiteSpaceCases))]
    public void TabsTrailingSpacesAndPercentIndentKeepTheirRealContainingBlock(string path, string mode, string indent)
    {
        using var f = new WrapStyleFixture(path, "MM MM\tMM MM MM MM M   ");
        var extra = $"white-space:{mode};tab-size:40px;text-indent:{indent};";
        f.SetStyle("auto", extra: extra); f.Layout(); var greedy = f.Lines();
        f.SetStyle("balance", extra: extra); f.Layout(); var result = f.Lines();
        Assert.Equal(greedy.Length, result.Length);
        var hanging = indent.Contains("hanging");
        for (var i = 0; i < result.Length; i++)
            Assert.InRange(result[i].Left, (hanging && i > 0 || !hanging && i == 0 ? 15 : 0) - .08,
                (hanging && i > 0 || !hanging && i == 0 ? 15 : 0) + .08);
        f.AssertSourceAndHits();
    }

    [Theory] [InlineData("shared", false)] [InlineData("shared", true)] [InlineData("nested", false)] [InlineData("nested", true)]
    public void InlineEdgeProbesDoNotConsumeSliceOrCloneEdges(string path, bool clone)
    {
        using var f = new WrapStyleFixture(path, Words);
        if (path == "nested")
            Css.SetStyle(Assert.IsType<Border>(Assert.Single(Assert.IsType<StackPanel>(f.Root).Children)),
                $"display:inline;padding:0 5%;border:1px solid #146342;box-decoration-break:{(clone ? "clone" : "slice")};");
        else foreach (var source in f.Sources)
            Css.SetStyle(source, $"display:inline;padding:0 5%;border:1px solid #146342;box-decoration-break:{(clone ? "clone" : "slice")};");
        f.SetStyle("auto"); f.Layout(); var greedy = f.Lines();
        f.SetStyle("balance"); f.Layout(); var result = f.Lines();
        Assert.Equal(greedy.Length, result.Length);
        Assert.InRange(result[0].Left, 8.42, 8.58);
        if (clone) Assert.All(result, line => Assert.InRange(line.Left, 8.42, 8.58));
        for (var pass = 0; pass < 3; pass++) { f.Layout(); Assert.Equal(result, f.Lines()); }
        f.AssertSourceAndHits();
    }

    public static IEnumerable<object[]> FloatCases()
    {
        foreach (var path in new[] { "shared", "nested" })
        foreach (var side in new[] { "left", "right" })
        foreach (var middle in new[] { false, true })
        foreach (var shape in new[] { false, true }) yield return [path, side, middle, shape];
    }

    [Theory] [MemberData(nameof(FloatCases))]
    public void FloatAndShapeProbesRestoreExclusionsAndTheFinalSourceAnchors(string path, string side, bool middle, bool shape)
    {
        using var f = new WrapStyleFixture(path, Words + " " + Words);
        var root = Assert.IsType<StackPanel>(f.Root);
        var parent = path == "nested" ? Assert.IsType<StackPanel>(Assert.IsType<Border>(Assert.Single(root.Children)).Child) : root;
        var floating = new Border(); Css.SetStyle(floating, $"float:{side};width:30px;height:48px;" + (shape ? "shape-outside:circle(50%);" : ""));
        parent.Children.Insert(middle ? 1 : 0, floating);
        f.SetStyle("auto", 180, "display:flow-root;"); f.Layout(); var greedy = f.Lines();
        f.SetStyle("balance", 180, "display:flow-root;"); f.Layout(); var result = f.Lines();
        Assert.Equal(greedy.Length, result.Length);
        var location = floating.TransformToAncestor(root);
        Assert.True(double.IsFinite(location.X + location.Y));
        for (var pass = 0; pass < 3; pass++)
        { f.Layout(); Assert.Equal(result, f.Lines()); Assert.Equal(location, floating.TransformToAncestor(root)); }
        f.SetStyle("auto", 180, "display:flow-root;"); f.Layout(); Assert.Equal(greedy, f.Lines());
        Assert.Same(floating, parent.Children[middle ? 1 : 0]); f.AssertSourceAndHits();
    }

    [Theory] [MemberData(nameof(WrapCases))]
    public void IntrinsicAndPreformattedNoWrapRemainUnchanged(string path, string style)
    {
        using var f = new WrapStyleFixture(path, Words);
        foreach (var extra in new[] { "width:max-content;", "width:min-content;", "white-space:pre;" })
        {
            f.SetStyle("auto", extra: extra); f.Layout(); var before = f.Lines();
            f.SetStyle(style, extra: extra); f.Layout(); Assert.Equal(before, f.Lines());
        }
        f.AssertSourceAndHits();
    }

    [Theory] [InlineData("block")] [InlineData("rich-block")] [InlineData("shared")] [InlineData("nested")]
    public void LongParagraphBudgetUsesTheExistingGreedyLayout(string path)
    {
        using var f = new WrapStyleFixture(path, string.Join(' ', Enumerable.Repeat("MMMM", 40)));
        f.SetStyle("auto", 80); f.Layout(); var before = f.Lines(); Assert.True(before.Length > 10);
        f.SetStyle("balance", 80); f.Layout(); Assert.Equal(before, f.Lines());
        f.AssertSourceAndHits();
    }

    [Theory] [InlineData("shared")] [InlineData("nested")]
    public void InlineWrapStyleOverridesDoNotReplaceTheirBlockParagraphStrategy(string path)
    {
        using var f = new WrapStyleFixture(path, Words); f.SetStyle("balance"); f.Layout(); var before = f.Lines();
        foreach (var source in f.Sources) Css.SetStyle(source, "display:inline;text-wrap-style:stable;");
        f.Layout(); Assert.Equal(before, f.Lines()); f.AssertSourceAndHits();
    }

    [Theory] [InlineData("left")] [InlineData("right")]
    public void AtomicInlineMetricsSurviveProbingAndStyleRestoration(string alignment)
    {
        using var f = new WrapStyleFixture("shared", Words);
        var root = Assert.IsType<StackPanel>(f.Root); var box = new Border();
        Css.SetStyle(box, "display:inline-block;width:18px;height:36px;vertical-align:middle;"); root.Children.Insert(1, box);
        f.SetStyle("auto", extra: $"text-align:{alignment};"); f.Layout(); var greedy = f.Lines();
        f.SetStyle("balance", extra: $"text-align:{alignment};"); f.Layout(); var balanced = f.Lines();
        Assert.Equal(greedy.Length, balanced.Length); var at = box.TransformToAncestor(root);
        for (var i = 0; i < 3; i++) { f.Layout(); Assert.Equal(balanced, f.Lines()); Assert.Equal(at, box.TransformToAncestor(root)); }
        f.SetStyle("auto", extra: $"text-align:{alignment};"); f.Layout(); Assert.Equal(greedy, f.Lines());
        Assert.Same(box, root.Children[1]); f.AssertSourceAndHits();
    }

    [Fact]
    public void GeneratedPrefixAndSuffixUseTheSameBalancedParagraphAsTheOriginalBody()
    {
        var body = Words[5..^2]; var root = new TextBlock { Text = body, IsTextSelectionEnabled = true };
        Css.SetStyle(root, "display:block;width:150px;font-family:'Courier New';font-size:16px;line-height:24px;white-space:normal;");
        Css.SetStyleSheet(root, "TextBlock::before{content:'MMMM ';display:inline;}TextBlock::after{content:' M';display:inline;}");
        WrapStyleFixture.Layout(root, 400); var greedy = root.GetSourceRangeSegments(0, body.Length).ToArray(); Assert.Equal(2, greedy.Length);
        Css.SetStyle(root, Css.GetStyle(root) + "text-wrap:balance;"); WrapStyleFixture.Layout(root, 400);
        Assert.Equal(3, root.GetSourceRangeSegments(0, body.Length).Count);
        root.SelectAll(); Assert.Equal(body, root.SelectedText); Assert.Equal(body, root.Text);
        Css.SetStyle(root, Css.GetStyle(root) + "text-wrap:auto;"); WrapStyleFixture.Layout(root, 400);
        Assert.Equal(greedy, root.GetSourceRangeSegments(0, body.Length));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void DocumentWrapStyleChangesRefreshTheExistingParagraphProxy(bool columns)
    {
        var run = new Run(Words); var paragraph = new Paragraph(run);
        var document = new FlowDocument(paragraph) { FontFamily = "Courier New", FontSize = 16, PagePadding = new(0), PageWidth = columns ? 300 : 150, PageHeight = 400 };
        Css.SetStyleSheet(document, "FlowDocument{line-height:24px;}Paragraph{margin:0;padding:0;" + (columns ? "column-count:2;column-gap:0;" : "") + "}");
        using var body = new FlowDocumentPresentation(); body.SetDocument(document); WrapStyleFixture.Layout(body, columns ? 300 : 150);
        var proxy = Assert.Single(body.Paragraphs); var greedy = proxy.GetSourceRangeSegments(0, proxy.Text.Length).ToArray();
        Css.SetStyle(paragraph, "text-wrap:balance;"); WrapStyleFixture.Layout(body, columns ? 300 : 150);
        Assert.Same(proxy, Assert.Single(body.Paragraphs)); var balanced = proxy.GetSourceRangeSegments(0, proxy.Text.Length).ToArray();
        Assert.Equal(greedy.Length, balanced.Length); Assert.True(balanced[^1].EndX - balanced[^1].StartX > greedy[^1].EndX - greedy[^1].StartX + 10);
        Css.SetStyle(paragraph, ""); WrapStyleFixture.Layout(body, columns ? 300 : 150); Assert.Equal(greedy, proxy.GetSourceRangeSegments(0, proxy.Text.Length));
        Assert.Same(run, Assert.Single(paragraph.Inlines)); Assert.Equal(Words, run.Text);
    }

    internal static double Variance(WrapLine[] lines)
    { var average = lines.Average(line => line.Width); return lines.Sum(line => Math.Pow(line.Width - average, 2)); }
}

internal readonly record struct WrapLine(double Left, double Right, double Baseline, int Start, int End)
{
    internal double Width => Right - Left;
}

internal sealed class WrapStyleFixture : IDisposable
{
    internal FrameworkElement Root { get; }
    internal readonly List<TextBlock> Sources = [];
    internal readonly List<Run> Runs = [];
    internal readonly string Path, Words;
    internal WrapStyleFixture(string path, string words)
    {
        Path = path; Words = words;
        TextBlock Text(string value) { var text = new TextBlock { Text = value, IsTextSelectionEnabled = true }; Sources.Add(text); return text; }
        var split = StringInfo.ParseCombiningCharacters(words).Where(index => index <= 7).DefaultIfEmpty(words.Length).Last();
        if (path is "native" or "block" or "rich-native" or "rich-block")
        {
            var text = Text(path.StartsWith("rich") ? "" : words); Root = text;
            if (path.StartsWith("rich")) foreach (var value in new[] { words[..split], words[split..] })
            { var run = new Run(value); Runs.Add(run); text.Inlines.Add(run); }
        }
        else
        {
            var root = new StackPanel(); Root = root; Panel parent = root;
            if (path == "nested")
            {
                var frame = new Border(); Css.SetStyle(frame, "display:inline;"); root.Children.Add(frame);
                var inner = new StackPanel(); Css.SetStyle(inner, "display:inline;"); frame.Child = inner; parent = inner;
            }
            foreach (var value in new[] { words[..split], words[split..] })
            { var text = Text(value); Css.SetStyle(text, "display:inline;"); parent.Children.Add(text); }
        }
    }
    internal void SetStyle(string style, double width = 150, string extra = "") => Css.SetStyle(Root,
        (Path is "native" or "rich-native" ? "" : "display:block;") +
        $"font-family:'Courier New';font-size:16px;line-height:24px;white-space:normal;text-autospace:no-autospace;text-align:start;width:{width.ToString(CultureInfo.InvariantCulture)}px;text-wrap-style:{style};" + extra);
    internal void Layout(double width = 400) => Layout(Root, width);
    internal static void Layout(FrameworkElement root, double width)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new(width, 2000)); root.Arrange(new(0, 0, width, root.DesiredSize.Height)); root.UpdateLayout();
        }
    }
    internal WrapLine[] Lines()
    {
        var offset = 0; var characters = new List<WrapLine>();
        foreach (var text in Sources)
        {
            var origin = text.TransformToAncestor(Root);
            var starts = StringInfo.ParseCombiningCharacters(text.Text).Append(text.Text.Length).ToArray();
            foreach (var (start, end) in starts.Zip(starts.Skip(1)))
            {
                if (text.Text.AsSpan(start, end - start).Trim().IsEmpty) continue;
                foreach (var range in text.GetSourceRangeSegments(start, end - start))
                    characters.Add(new(range.StartX + origin.X, range.EndX + origin.X, range.Baseline + origin.Y, offset + start, offset + end));
            }
            offset += text.Text.Length;
        }
        return characters.GroupBy(line => Math.Round(line.Baseline, 4)).OrderBy(group => group.Key)
            .Select(group => new WrapLine(group.Min(line => line.Left), group.Max(line => line.Right), group.First().Baseline,
                group.Min(line => line.Start), group.Max(line => line.End))).ToArray();
    }
    internal void AssertSourceAndHits()
    {
        Assert.Equal(Words, string.Concat(Sources.Select(text => text.Text)));
        if (Runs.Count > 0) Assert.Equal(Runs, Sources[0].Inlines.Cast<Run>());
        foreach (var text in Sources)
        {
            text.SelectAll(); Assert.Equal(text.Text, text.SelectedText); text.Select(0, 0);
            var starts = StringInfo.ParseCombiningCharacters(text.Text).Append(text.Text.Length).ToArray();
            foreach (var (start, end) in starts.Zip(starts.Skip(1)))
            {
                if (text.Text.AsSpan(start, end - start).Trim().IsEmpty) continue;
                var ranges = text.GetSourceRangeSegments(start, end - start);
                if (ranges.Count == 0 && CharUnicodeInfo.GetUnicodeCategory(text.Text, start) == UnicodeCategory.Format) continue;
                var range = Assert.Single(ranges);
                var point = new Point(range.StartX + .2, range.Baseline - 2);
                // Native TextBlock clips unbreakable overflow at its content
                // edge. Outside that allocation only snapped hits are valid.
                var outside = point.X < 0 || point.X >= text.ActualWidth || point.Y < 0 || point.Y >= text.ActualHeight;
                var hit = Assert.IsType<TextPointer>(text.GetPositionFromPoint(point, outside));
                Assert.Equal(start, text.ContentStart.GetOffsetToPosition(hit));
            }
        }
    }
    public void Dispose() { }
}
