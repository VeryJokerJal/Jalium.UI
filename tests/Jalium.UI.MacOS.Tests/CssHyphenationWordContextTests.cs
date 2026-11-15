using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssHyphenationWordContextTests
{
    private static readonly string[][] Partitions =
    [
        ["ABC\u00AD", "DEFG"],
        ["A", "BC\u00AD", "DE", "FG"],
        ["AB", "C\u00ADD", "EFG"],
        ["ABC", "\u00AD", "DEFG"],
    ];

    public static IEnumerable<object[]> PositiveCases()
    {
        for (int partition = 0; partition < Partitions.Length; partition++)
            foreach (string direction in new[] { "ordinary", "ltr", "rtl", "plaintext" })
                foreach (string limits in new[] { "1 1 1", "7 3 4" })
                    yield return [partition, direction, limits];
    }

    [Theory] [MemberData(nameof(PositiveCases))]
    public void WordLimitsIgnoreOriginalTextNodeBoundaries(int partition, string direction, string limits)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        string[] pieces = Partitions[partition]; var root = new StackPanel();
        var sources = pieces.Select(text => new TextBlock { Text = text, IsTextSelectionEnabled = true }).ToArray();
        foreach (var source in sources) { root.Children.Add(source); Css.SetStyle(source, "display:inline"); }
        string style = Style(direction, limits); Css.SetStyle(root, style); Layout(root);
        string family = sources[0].FontFamily.GetRenderingSource(sources[0]);
        const string literal = "ABC-\u2028DEFG";
        var units = Enumerable.Range(0, literal.Length).Select(i => new NativeMethods.ParagraphAdvanceUnit
        { TextPosition = (uint)i, Length = 1, Flags = literal[i] == '\u2028' ? 1u : 0u }).ToArray();
        using var reference = NativeTextParagraph.TryCreateAdvanced([new(literal, family, 22, 400, 0, Colors.Black)], family, 22,
            10000000, 1, root.FlowDirection, units, contextPaint: true, naturalDirection: direction == "plaintext", textOptionsOwner: sources[0]);
        Assert.NotNull(reference); Assert.Equal(2, reference.Lines.Length);
        var first = sources[0]; double baseline = first.TransformToAncestor(root).Y + Assert.Single(first.GetSourceRangeSegments(0, 1)).Baseline;
        var ink = Assert.IsType<SolidColorBrush>(first.Foreground);
        double Left(int row) => (direction == "rtl" ? 68 - reference.Lines[row].Metrics.Line.Width : 4) - reference.Lines[row].Metrics.Line.X;
        var expected = Pixels(dc =>
        {
            for (int row = 0; row < 2; row++)
            {
                var line = reference.Lines[row]; int start = (int)line.Metrics.Line.TextPosition, end = start + (int)line.Metrics.Line.Length;
                while (end > start && literal[end - 1] == '\u2028') end--;
                var text = new FormattedText(literal, family, 22)
                {
                    Foreground = ink,
                    PlatformTextLine = line.ForSource(start, end - start, ink.Color, 1).WithBrushLineBox(line.Metrics.Baseline - (baseline - 4), 40)
                };
                text.ApplyTextOptionsFrom(first); dc.DrawText(text, new(Left(row), baseline + row * 40 - line.Metrics.Baseline));
            }
        }, context);
        Assert.Equal(expected, Pixels(root.Render, context));
        int rawStart = 0;
        foreach (var source in sources)
        {
            var offset = source.TransformToAncestor(root);
            for (int i = 0; i < source.Text.Length; i++)
            {
                int raw = rawStart + i, row = raw <= 3 ? 0 : 1, at = raw < 4 ? raw : raw + 1;
                var wanted = reference.Selection((uint)row, at, 1); var actual = source.GetSourceRangeSegments(i, 1);
                Assert.NotEmpty(wanted); Assert.NotEmpty(actual);
                Assert.Equal(Left(row) + wanted.Min(r => r.X), offset.X + actual.Min(r => Math.Min(r.StartX, r.EndX)), 3);
                Assert.Equal(wanted.Sum(r => (double)r.Width), actual.Sum(r => Math.Abs(r.EndX - r.StartX)), 3);
                Assert.Equal(baseline + row * 40, offset.Y + actual[0].Baseline, 3);
                foreach (var rect in actual.Where(r => Math.Abs(r.EndX - r.StartX) > .01))
                {
                    var hit = Assert.IsType<TextPointer>(source.GetPositionFromPoint(new((rect.StartX + rect.EndX) / 2, rect.Baseline - 2), false));
                    Assert.InRange(source.ContentStart.GetOffsetToPosition(hit), i, i + 1);
                }
            }
            source.SelectAll(); Assert.Equal(source.Text, source.SelectedText); source.ClearSelection(); rawStart += source.Text.Length;
        }
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); root.Render(recorder); var frame = recorder.Commit(); Assert.True(frame.IsFullyRecordable);
        Css.SetStyle(root, Style(direction, "8 1 1")); Layout(root);
        Assert.Single(sources.SelectMany(s => s.GetSourceRangeSegments(0, s.Text.Length).Select(r => s.TransformToAncestor(root).Y + r.Baseline)).Distinct());
        Assert.Equal(expected, Pixels(dc => DrawingReplayer.Replay(frame, dc), context));
        Css.SetStyle(root, style); Layout(root); Assert.Equal(expected, Pixels(root.Render, context));
        for (int i = 0; i < sources.Length; i++) { Assert.Same(sources[i], root.Children[i]); Assert.Equal(pieces[i], sources[i].Text); }
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        for (int partition = 0; partition < Partitions.Length; partition++)
            foreach (string direction in new[] { "ordinary", "rtl" })
                foreach (string limits in new[] { "8 1 1", "1 4 1", "1 1 5" }) yield return [partition, direction, limits];
    }

    [Theory] [MemberData(nameof(NegativeCases))]
    public void InsufficientWholeWordOrSideCountsDoNotHyphenate(int partition, string direction, string limits)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel(); var sources = Partitions[partition].Select(text => new TextBlock { Text = text }).ToArray();
        foreach (var source in sources) { root.Children.Add(source); Css.SetStyle(source, "display:inline"); }
        Css.SetStyle(root, Style(direction, limits)); Layout(root);
        var baselines = sources.SelectMany(s => s.GetSourceRangeSegments(0, s.Text.Length).Select(r => s.TransformToAncestor(root).Y + r.Baseline)).Distinct();
        Assert.Single(baselines);
        foreach (var source in sources)
        {
            int shy = source.Text.IndexOf('\u00AD');
            if (shy >= 0) Assert.All(source.GetSourceRangeSegments(shy, 1), r => Assert.Equal(0, Math.Abs(r.EndX - r.StartX), 3));
        }
    }

    [Theory]
    [InlineData("AB\u0301C", "7 3 4", true)] [InlineData("AB\u0301C", "1 4 1", false)]
    [InlineData("AB'C", "7 3 4", true)] [InlineData("AB'C", "1 4 1", false)]
    [InlineData("AB_C", "7 3 4", true)] [InlineData("AB_C", "1 4 1", false)]
    [InlineData("𐐀BC", "7 3 4", true)] [InlineData("𐐀BC", "1 4 1", false)]
    [InlineData("AB\u093EC", "8 4 4", true)] [InlineData("AB\u093EC", "9 1 1", false)]
    [InlineData("AB\u20DDC", "8 4 4", true)] [InlineData("AB\u20DDC", "9 1 1", false)]
    public void UnicodeLimitsCountScalarsAndOnlyExcludeNonspacingMarksAndPunctuation(string prefix, string limits, bool allowed)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        int firstScalar = char.IsHighSurrogate(prefix[0]) ? 2 : 1;
        var root = new StackPanel(); var a = new TextBlock { Text = prefix[..firstScalar] };
        var b = new TextBlock { Text = prefix[firstScalar..] + "\u00AD" }; var c = new TextBlock { Text = "DEFG" };
        foreach (var text in new[] { a, b, c }) { root.Children.Add(text); Css.SetStyle(text, "display:inline"); }
        Css.SetStyle(root, Style("ltr", limits)); Layout(root);
        AssertConditionalGlyph(b, b.Text.Length - 1, allowed);
        Assert.Equal(allowed ? 2 : 1, new[] { a, b, c }.SelectMany(text => text.GetSourceRangeSegments(0, text.Text.Length)
            .Where(r => Math.Abs(r.EndX - r.StartX) > .01).Select(r => text.TransformToAncestor(root).Y + r.Baseline)).Distinct().Count());
    }

    [Theory]
    [InlineData("atomic")] [InlineData("block")] [InlineData("space")] [InlineData("newline")]
    public void ActualWordSeparatorsDoNotLendCharactersToTheNextWord(string separator)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel(); var before = new TextBlock { Text = "AB" };
        var marker = new TextBlock { Text = (separator == "space" ? " " : separator == "newline" ? "\n" : "") + "C\u00AD" };
        var after = new TextBlock { Text = "DEFG" }; root.Children.Add(before);
        if (separator == "atomic") { var atom = new Border(); root.Children.Add(atom); Css.SetStyle(atom, "display:inline-block;width:0;height:0"); }
        root.Children.Add(marker); root.Children.Add(after);
        Css.SetStyle(root, Style("ltr", "7 3 4")); Css.SetStyle(before, separator == "block" ? "display:block" : "display:inline");
        Css.SetStyle(marker, "display:inline" + (separator == "newline" ? ";white-space:pre-wrap" : "")); Css.SetStyle(after, "display:inline"); Layout(root);
        AssertConditionalGlyph(marker, marker.Text.Length - 1, false);
    }

    [Theory] [InlineData("empty-inline", true)] [InlineData("padded-inline", true)]
    [InlineData("display-none", false)] [InlineData("absolute", false)]
    public void InlineStructureAndOutOfFlowContentDoNotCreateWordCharacters(string kind, bool allowed)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel(); var a = new TextBlock { Text = "AB" }; var b = new TextBlock { Text = "C\u00AD" }; var c = new TextBlock { Text = "DEFG" };
        Css.SetStyle(root, Style("ltr", allowed ? "7 3 4" : "8 1 1"));
        if (kind == "padded-inline")
        {
            var frame = new StackPanel(); root.Children.Add(frame); Css.SetStyle(frame, "display:inline;padding:0 1px"); frame.Children.Add(a); frame.Children.Add(b);
        }
        else
        {
            root.Children.Add(a);
            var middle = new TextBlock { Text = kind == "empty-inline" ? "" : "EXTRA" }; root.Children.Add(middle);
            Css.SetStyle(middle, kind == "display-none" ? "display:none" : kind == "absolute" ? "display:inline;position:absolute;left:0;top:0" : "display:inline");
            root.Children.Add(b);
        }
        root.Children.Add(c); foreach (var text in new[] { a, b, c }) Css.SetStyle(text, "display:inline"); Layout(root);
        AssertConditionalGlyph(b, 1, allowed);
    }

    [Theory] [InlineData("hyphens:none", false)] [InlineData("hyphenate-limit-chars:8 1 1", false)]
    [InlineData("hyphens:manual;hyphenate-limit-chars:7 3 4", true)]
    public void OnlyTheConditionalHyphenOwnerSuppliesItsRules(string ownStyle, bool allowed)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel(); var a = new TextBlock { Text = "ABC" }; var b = new TextBlock { Text = "\u00AD" }; var c = new TextBlock { Text = "DEFG" };
        Css.SetStyle(root, Style("ltr", "7 3 4"));
        foreach (var text in new[] { a, b, c }) { root.Children.Add(text); Css.SetStyle(text, "display:inline"); }
        Css.SetStyle(a, "display:inline;hyphens:none;hyphenate-limit-chars:99 99 99"); Css.SetStyle(c, "display:inline;hyphens:none;hyphenate-limit-chars:99 99 99");
        Css.SetStyle(b, "display:inline;" + ownStyle); Layout(root); AssertConditionalGlyph(b, 0, allowed);
        Css.SetStyle(b, "display:inline;hyphens:manual;hyphenate-limit-chars:7 3 4"); Layout(root); AssertConditionalGlyph(b, 0, true);
    }

    [Fact]
    public void OriginalRunsRemainAttachedWhileTheirSharedWordReflows()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel(); var text = new TextBlock { IsTextSelectionEnabled = true };
        var a = new Run("AB"); var b = new Run("C\u00AD"); var c = new Run("DEFG"); text.Inlines.Add(a); text.Inlines.Add(b); text.Inlines.Add(c); root.Children.Add(text);
        Css.SetStyle(root, Style("rtl", "7 3 4")); Css.SetStyle(text, "display:inline");
        Css.SetStyle(a, "display:inline;color:blue"); Css.SetStyle(b, "display:inline;hyphenate-limit-chars:7 3 4"); Css.SetStyle(c, "display:inline;color:green"); Layout(root);
        AssertConditionalGlyph(text, 3, true); text.SelectAll(); Assert.Equal("ABC\u00ADDEFG", text.SelectedText); text.ClearSelection();
        c.Text = "D"; Layout(root); AssertConditionalGlyph(text, 3, false);
        c.Text = "DEFG"; Layout(root); AssertConditionalGlyph(text, 3, true);
        Assert.Same(a, text.Inlines[0]); Assert.Same(b, text.Inlines[1]); Assert.Same(c, text.Inlines[2]);
    }

    public static IEnumerable<object[]> NestedParagraphCases()
    {
        foreach (string direction in new[] { "ordinary", "ltr", "rtl", "plaintext" })
            foreach (bool allowed in new[] { false, true })
                foreach (bool tab in new[] { false, true }) yield return [direction, allowed, tab];
    }

    [Theory] [MemberData(nameof(NestedParagraphCases))]
    public void NestedBlocksAndForcedBreaksRetainIndependentLines(string direction, bool allowed, bool tab)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root = new StackPanel(); var frame = new StackPanel(); root.Children.Add(frame);
        var a = new TextBlock { Text = "ABC\u00AD" }; var b = new TextBlock { Text = "DEFG" };
        var block = new Border(); var after = new TextBlock { Text = tab ? "甲\tA\n乙" : "甲\n乙", IsTextSelectionEnabled = true };
        foreach (var child in new UIElement[] { a, b, block, after }) frame.Children.Add(child);
        Css.SetStyle(root, Style(direction, allowed ? "7 3 4" : "8 1 1"));
        foreach (var element in new FrameworkElement[] { frame, a, b }) Css.SetStyle(element, "display:inline");
        Css.SetStyle(block, "display:block;height:15px"); Css.SetStyle(after, "display:inline;white-space:pre-wrap;tab-size:2");
        Layout(root); AssertConditionalGlyph(a, 3, allowed);
        var ranges = after.GetSourceRangeSegments(0, after.Text.Length).Where(r => Math.Abs(r.EndX - r.StartX) > .01).ToArray();
        Assert.Equal(2, ranges.Select(r => r.Baseline).Distinct().Count());
        var offset = after.TransformToAncestor(root);
        Assert.True(ranges.Min(r => offset.Y + r.Baseline) > block.TransformToAncestor(root).Y + block.ActualHeight);
        foreach (int i in new[] { 0, after.Text.Length - 1 })
            foreach (var rect in after.GetSourceRangeSegments(i, 1))
            {
                var hit = Assert.IsType<TextPointer>(after.GetPositionFromPoint(new((rect.StartX + rect.EndX) / 2, rect.Baseline - 2), false));
                Assert.InRange(after.ContentStart.GetOffsetToPosition(hit), i, i + 1);
            }
        after.SelectAll(); Assert.Equal(after.Text, after.SelectedText); after.ClearSelection();
        var expected = Pixels(root.Render, context);
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); root.Render(recorder); var drawing = recorder.Commit();
        Assert.True(drawing.IsFullyRecordable); Assert.Equal(expected, Pixels(dc => DrawingReplayer.Replay(drawing, dc), context));
        Assert.Same(block, frame.Children[2]); Assert.Same(frame, block.VisualParent);
    }

    [Theory]
    [InlineData("sibling-textblocks", false)]
    [InlineData("spans", false)]
    [InlineData("padded-inline", false)]
    [InlineData("empty-inline", false)]
    [InlineData("display-none", false)]
    [InlineData("absolute", false)]
    [InlineData("float", false)]
    [InlineData("inline-block", true)]
    [InlineData("block", true)]
    [InlineData("newline", true)]
    public void CrossNodeTextTransformCapitalizeRespectsWordContextAndBoundaries(string mode, bool restartsWord)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string baseCss = "display:block;width:160px;height:90px;box-sizing:border-box;padding:4px;font-family:DejaVu Sans;font-size:22px;line-height:36px;white-space:normal;color:#1d3557";

        StackPanel BuildScene(string transformCss, string firstText, string secondText)
        {
            var root = new StackPanel();
            Css.SetStyle(root, baseCss + ";" + transformCss);
            if (mode == "spans")
            {
                var tb = new TextBlock();
                var s1 = new Span(new Run(firstText));
                var s2 = new Span(new Run(secondText));
                Css.SetStyle(s1, "display:inline;padding:0");
                Css.SetStyle(s2, "display:inline;padding:0");
                tb.Inlines.Add(s1);
                tb.Inlines.Add(s2);
                Css.SetStyle(tb, "display:inline");
                root.Children.Add(tb);
            }
            else if (mode == "padded-inline")
            {
                var wrap = new StackPanel();
                Css.SetStyle(wrap, "display:inline;padding:0 2px");
                var a = new TextBlock { Text = firstText };
                var b = new TextBlock { Text = secondText };
                Css.SetStyle(a, "display:inline");
                Css.SetStyle(b, "display:inline");
                wrap.Children.Add(a);
                root.Children.Add(wrap);
                root.Children.Add(b);
            }
            else
            {
                var a = new TextBlock { Text = firstText };
                var b = new TextBlock { Text = (mode == "newline" ? "\n" : "") + secondText };
                Css.SetStyle(a, mode == "block" ? "display:block" : "display:inline");
                Css.SetStyle(b, "display:inline" + (mode == "newline" ? ";white-space:pre-wrap" : ""));
                root.Children.Add(a);
                if (mode is "empty-inline" or "display-none" or "absolute" or "float" or "inline-block")
                {
                    UIElement mid = mode == "inline-block"
                        ? new Border()
                        : new TextBlock { Text = mode == "empty-inline" ? "" : "EXTRA" };
                    var midCss = mode switch
                    {
                        "empty-inline" => "display:inline",
                        "display-none" => "display:none",
                        "absolute" => "display:inline;position:absolute;left:120px;top:60px;opacity:0",
                        "float" => "display:inline;float:right;width:1px;height:1px;overflow:hidden;opacity:0",
                        "inline-block" => "display:inline-block;width:2px;height:2px",
                        _ => "display:inline"
                    };
                    Css.SetStyle((FrameworkElement)mid, midCss);
                    root.Children.Add(mid);
                }
                root.Children.Add(b);
            }
            LayoutCustom(root, 160, 90);
            return root;
        }

        var actual = BuildScene("text-transform:capitalize", "he", "llo");
        var expectedSingleWord = BuildScene("text-transform:none", "He", "llo");
        var expectedRestartedWord = BuildScene("text-transform:none", "He", "Llo");

        var actualPixels = PixelsCustom(actual.Render, context, 160, 90);
        var singleWordPixels = PixelsCustom(expectedSingleWord.Render, context, 160, 90);
        var restartedPixels = PixelsCustom(expectedRestartedWord.Render, context, 160, 90);

        Assert.NotEqual(singleWordPixels, restartedPixels);
        if (restartsWord)
            Assert.Equal(restartedPixels, actualPixels);
        else
            Assert.Equal(singleWordPixels, actualPixels);
    }

    [Fact]
    public void CrossNodeGreekFinalSigmaLocaleCasingAndDynamicInvalidationWork()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string baseCss = "display:block;width:160px;height:90px;box-sizing:border-box;padding:4px;font-family:DejaVu Sans;font-size:22px;line-height:36px;white-space:normal;color:#1d3557";

        // 1. Greek final sigma across inline TextBlocks: "ΟΣ" + "ΟΣ" -> "οσ" + "ος"
        var greekRoot = new StackPanel();
        var g1 = new TextBlock { Text = "ΟΣ" };
        var gMid = new TextBlock { Text = "HIDDEN" };
        var g2 = new TextBlock { Text = "ΟΣ" };
        greekRoot.Children.Add(g1);
        greekRoot.Children.Add(gMid);
        greekRoot.Children.Add(g2);
        Css.SetStyle(greekRoot, baseCss + ";text-transform:lowercase");
        Css.SetStyle(g1, "display:inline");
        Css.SetStyle(gMid, "display:none");
        Css.SetStyle(g2, "display:inline");
        LayoutCustom(greekRoot, 160, 90);

        var greekRef = new StackPanel();
        var gr1 = new TextBlock { Text = "οσ" };
        var gr2 = new TextBlock { Text = "ος" };
        greekRef.Children.Add(gr1);
        greekRef.Children.Add(gr2);
        Css.SetStyle(greekRef, baseCss);
        Css.SetStyle(gr1, "display:inline");
        Css.SetStyle(gr2, "display:inline");
        LayoutCustom(greekRef, 160, 90);

        var greekBrokenRef = new StackPanel();
        var gb1 = new TextBlock { Text = "ος" };
        var gb2 = new TextBlock { Text = "ος" };
        greekBrokenRef.Children.Add(gb1);
        greekBrokenRef.Children.Add(gb2);
        Css.SetStyle(greekBrokenRef, baseCss);
        Css.SetStyle(gb1, "display:inline");
        Css.SetStyle(gb2, "display:inline");
        LayoutCustom(greekBrokenRef, 160, 90);

        var greekActual = PixelsCustom(greekRoot.Render, context, 160, 90);
        var greekExpected = PixelsCustom(greekRef.Render, context, 160, 90);
        var greekBroken = PixelsCustom(greekBrokenRef.Render, context, 160, 90);
        Assert.NotEqual(greekExpected, greekBroken);
        Assert.Equal(greekExpected, greekActual);

        // Dynamic invalidation: removing g2's text makes g1 word-final ("ος")
        g2.Text = "";
        LayoutCustom(greekRoot, 160, 90);
        var greekSingleRef = new StackPanel();
        var gs1 = new TextBlock { Text = "ος" };
        greekSingleRef.Children.Add(gs1);
        Css.SetStyle(greekSingleRef, baseCss);
        Css.SetStyle(gs1, "display:inline");
        LayoutCustom(greekSingleRef, 160, 90);
        Assert.Equal(
            PixelsCustom(greekSingleRef.Render, context, 160, 90),
            PixelsCustom(greekRoot.Render, context, 160, 90));

        // Restoring g2's text restores g1 to medial "οσ"
        g2.Text = "ΟΣ";
        LayoutCustom(greekRoot, 160, 90);
        Assert.Equal(greekExpected, PixelsCustom(greekRoot.Render, context, 160, 90));

        // 2. Per-node language preservation + shared word context:
        // <TextBlock Language="tr">i</TextBlock><TextBlock Language="en">i</TextBlock> with capitalize -> "İi"
        var trRoot = new StackPanel();
        var t1 = new TextBlock { Text = "i", Language = XmlLanguage.GetLanguage("tr") };
        var t2 = new TextBlock { Text = "i", Language = XmlLanguage.GetLanguage("en") };
        trRoot.Children.Add(t1);
        trRoot.Children.Add(t2);
        Css.SetStyle(trRoot, baseCss + ";text-transform:capitalize");
        Css.SetStyle(t1, "display:inline");
        Css.SetStyle(t2, "display:inline");
        LayoutCustom(trRoot, 160, 90);

        var trRef = new StackPanel();
        var trRef1 = new TextBlock { Text = "\u0130", Language = XmlLanguage.GetLanguage("tr") };
        var trRef2 = new TextBlock { Text = "i", Language = XmlLanguage.GetLanguage("en") };
        trRef.Children.Add(trRef1);
        trRef.Children.Add(trRef2);
        Css.SetStyle(trRef, baseCss);
        Css.SetStyle(trRef1, "display:inline");
        Css.SetStyle(trRef2, "display:inline");
        LayoutCustom(trRef, 160, 90);

        Assert.Equal(
            PixelsCustom(trRef.Render, context, 160, 90),
            PixelsCustom(trRoot.Render, context, 160, 90));

        // Dynamic preceding change: if t1 becomes "i ", t2 is now word-initial ("I")
        t1.Text = "i ";
        LayoutCustom(trRoot, 160, 90);
        trRef1.Text = "\u0130 ";
        trRef2.Text = "I";
        LayoutCustom(trRef, 160, 90);
        Assert.Equal(
            PixelsCustom(trRef.Render, context, 160, 90),
            PixelsCustom(trRoot.Render, context, 160, 90));
    }

    [Theory]
    [InlineData("en-US", "hypermodernization", "hy", "permodernization")]
    [InlineData("de-1996", "Dampfschifffahrt", "Dampf", "schifffahrt")]
    [InlineData("fr", "internationalisation", "inter", "nationalisation")]
    [InlineData("es", "electroencefalografista", "elec", "troencefalografista")]
    public void AutoHyphenationDictionaryBreaksSingleAndCrossNodeWords(
        string lang, string fullWord, string firstPart, string secondPart)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string css = "display:block;width:110px;height:180px;box-sizing:border-box;padding:4px;font-family:DejaVu Sans;font-size:18px;line-height:32px;white-space:normal;color:#2b2d42;hyphens:auto;hyphenate-character:'-';hyphenate-limit-chars:5 2 2";

        // Single-host TextBlock
        var single = new TextBlock
        {
            Text = fullWord,
            Language = XmlLanguage.GetLanguage(lang),
            IsTextSelectionEnabled = true
        };
        Css.SetStyle(single, css);
        LayoutCustom(single, 110, 180);
        var singleBaselines = single.GetSourceRangeSegments(0, single.Text.Length)
            .Where(r => Math.Abs(r.EndX - r.StartX) > 0.01)
            .Select(r => Math.Round(r.Baseline, 2))
            .Distinct()
            .ToArray();
        Assert.True(singleBaselines.Length >= 2, $"Expected '{fullWord}' ({lang}) to hyphenate across >= 2 lines, got {singleBaselines.Length}.");
        Assert.Equal(fullWord, single.Text);
        single.SelectAll();
        Assert.Equal(fullWord, single.SelectedText);
        single.ClearSelection();

        // Cross-node inline TextBlocks
        var root = new StackPanel { Language = XmlLanguage.GetLanguage(lang) };
        var a = new TextBlock { Text = firstPart, Language = XmlLanguage.GetLanguage(lang) };
        var b = new TextBlock { Text = secondPart, Language = XmlLanguage.GetLanguage(lang) };
        root.Children.Add(a);
        root.Children.Add(b);
        Css.SetStyle(root, css);
        Css.SetStyle(a, "display:inline");
        Css.SetStyle(b, "display:inline");
        LayoutCustom(root, 110, 180);
        var crossBaselines = new[] { a, b }
            .SelectMany(s => s.GetSourceRangeSegments(0, s.Text.Length)
                .Where(r => Math.Abs(r.EndX - r.StartX) > 0.01)
                .Select(r => Math.Round(s.TransformToAncestor(root).Y + r.Baseline, 2)))
            .Distinct()
            .ToArray();
        Assert.Equal(singleBaselines.Length, crossBaselines.Length);

        // When hyphens:manual, no break occurs without \u00AD
        Css.SetStyle(single, css.Replace("hyphens:auto", "hyphens:manual"));
        LayoutCustom(single, 110, 180);
        var manualBaselines = single.GetSourceRangeSegments(0, single.Text.Length)
            .Where(r => Math.Abs(r.EndX - r.StartX) > 0.01)
            .Select(r => Math.Round(r.Baseline, 2))
            .Distinct()
            .ToArray();
        Assert.Single(manualBaselines);
    }

    [Theory]
    [InlineData("de-1901", "Zucker", "Zuc", "ker", "Zuk-", "ker", "Zuc-")]
    [InlineData("nl", "reëel", "re", "ëel", "re-", "eel", "ëel")]
    [InlineData("nl", "cafeetje", "cafee", "tje", "café-", "tje", "cafee-")]
    [InlineData("hu", "asszony", "ass", "zony", "asz-", "szony", "ass-")]
    [InlineData("ca", "paral·lel", "paral·", "lel", "paral-", "lel", "paral·-")]
    public async Task AutoHyphenationAppliesLanguageSpecificSpellingChangesAndPreservesSourceMapping(
        string lang, string fullWord, string splitA, string splitB,
        string expectedLine1, string expectedLine2, string naiveWrongLine)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        int width = fullWord == "reëel" ? 44 : 68;
        string boxCss = $"display:block;width:{width}px;height:120px;box-sizing:border-box;padding:4px;font-family:DejaVu Sans;font-size:20px;line-height:36px;white-space:normal;color:#d83253";

        // 1. Single-host TextBlock with hyphens:auto
        var single = new TextBlock
        {
            Text = fullWord,
            Language = XmlLanguage.GetLanguage(lang),
            IsTextSelectionEnabled = true
        };
        Css.SetStyle(single, boxCss + ";hyphens:auto;hyphenate-character:'-';hyphenate-limit-chars:4 2 2");
        LayoutCustom(single, width, 120);

        // Reference TextBlock with literal expected broken lines
        var expectedRef = new TextBlock
        {
            Text = expectedLine1 + "\n" + expectedLine2,
            Language = XmlLanguage.GetLanguage(lang)
        };
        Css.SetStyle(expectedRef, boxCss + ";white-space:pre-line;hyphens:none");
        LayoutCustom(expectedRef, width, 120);

        // Naive wrong TextBlock without spelling change
        var wrongText = naiveWrongLine.EndsWith('-')
            ? naiveWrongLine + "\n" + expectedLine2
            : expectedLine1 + "\n" + naiveWrongLine;
        var wrongRef = new TextBlock
        {
            Text = wrongText,
            Language = XmlLanguage.GetLanguage(lang)
        };
        Css.SetStyle(wrongRef, boxCss + ";white-space:pre-line;hyphens:none");
        LayoutCustom(wrongRef, width, 120);

        var singlePixels = PixelsCustom(single.Render, context, width, 120);
        var expectedPixels = PixelsCustom(expectedRef.Render, context, width, 120);
        var wrongPixels = PixelsCustom(wrongRef.Render, context, width, 120);

        Assert.NotEqual(expectedPixels, wrongPixels);
        Assert.Equal(expectedPixels, singlePixels);

        // Verify source text and selection are unmutated
        Assert.Equal(fullWord, single.Text);
        single.SelectAll();
        Assert.Equal(fullWord, single.SelectedText);
        single.ClearSelection();

        // Verify 2 distinct baselines and valid hit-testing
        var segments = single.GetSourceRangeSegments(0, single.Text.Length)
            .Where(r => Math.Abs(r.EndX - r.StartX) > 0.01)
            .ToArray();
        Assert.Equal(2, segments.Select(r => Math.Round(r.Baseline, 2)).Distinct().Count());
        foreach (int idx in new[] { 0, single.Text.Length - 1 })
        {
            var seg = single.GetSourceRangeSegments(idx, 1).First(r => Math.Abs(r.EndX - r.StartX) > 0.01);
            var hit = Assert.IsType<TextPointer>(single.GetPositionFromPoint(new((seg.StartX + seg.EndX) / 2, seg.Baseline - 2), false));
            Assert.InRange(single.ContentStart.GetOffsetToPosition(hit), 0, single.Text.Length);
        }

        // Verify worker-thread replay of recorded drawing
        var recorder = new DrawingRecorder();
        recorder.BindWholeFrame();
        single.Render(recorder);
        var recorded = recorder.Commit();
        Assert.True(recorded.IsFullyRecordable);
        var replayedPixels = await Task.Run(() =>
        {
            var workerCtx = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
            return PixelsCustom(dc => DrawingReplayer.Replay(recorded, dc), workerCtx, width, 120);
        });
        Assert.Equal(singlePixels, replayedPixels);

        // 2. When wide enough, the word does NOT break and preserves its normal spelling
        Css.SetStyle(single, boxCss.Replace($"width:{width}px", "width:220px") + ";hyphens:auto;hyphenate-character:'-';hyphenate-limit-chars:4 2 2");
        LayoutCustom(single, 220, 120);
        var unbrokenRef = new TextBlock { Text = fullWord, Language = XmlLanguage.GetLanguage(lang) };
        Css.SetStyle(unbrokenRef, boxCss.Replace($"width:{width}px", "width:220px") + ";hyphens:none");
        LayoutCustom(unbrokenRef, 220, 120);
        Assert.Equal(
            PixelsCustom(unbrokenRef.Render, context, 220, 120),
            PixelsCustom(single.Render, context, 220, 120));

        // 3. Cross-node inline TextBlocks with hyphens:auto
        var crossRoot = new StackPanel { Language = XmlLanguage.GetLanguage(lang) };
        var partA = new TextBlock { Text = splitA, Language = XmlLanguage.GetLanguage(lang) };
        var partB = new TextBlock { Text = splitB, Language = XmlLanguage.GetLanguage(lang) };
        crossRoot.Children.Add(partA);
        crossRoot.Children.Add(partB);
        Css.SetStyle(crossRoot, boxCss + ";hyphens:auto;hyphenate-character:'-';hyphenate-limit-chars:4 2 2");
        Css.SetStyle(partA, "display:inline");
        Css.SetStyle(partB, "display:inline");
        LayoutCustom(crossRoot, width, 120);

        Assert.Equal(expectedPixels, PixelsCustom(crossRoot.Render, context, width, 120));
        Assert.Equal(splitA, partA.Text);
        Assert.Equal(splitB, partB.Text);
    }

    [Fact]
    public void ExplicitSoftHyphenOverridesAutoDictionaryAndFallbackHandlesMissingOrCorruptDictionaries()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string boxCss = "display:block;width:68px;height:120px;box-sizing:border-box;padding:4px;font-family:DejaVu Sans;font-size:20px;line-height:36px;white-space:normal;color:#1d3557;hyphens:auto;hyphenate-character:'-';hyphenate-limit-chars:4 2 2";

        // 1. Explicit \u00AD in "Zuck\u00ADer" overrides de-1901 dictionary "Zuk-ker" break!
        var explicitShy = new TextBlock
        {
            Text = "Zuck\u00ADer",
            Language = XmlLanguage.GetLanguage("de-1901")
        };
        Css.SetStyle(explicitShy, boxCss);
        LayoutCustom(explicitShy, 68, 120);

        var explicitRef = new TextBlock
        {
            Text = "Zuck-\ner",
            Language = XmlLanguage.GetLanguage("de-1901")
        };
        Css.SetStyle(explicitRef, boxCss + ";white-space:pre-line;hyphens:none");
        LayoutCustom(explicitRef, 68, 120);
        var explicitRefPixels = PixelsCustom(explicitRef.Render, context, 68, 120);
        var explicitShyPixels = PixelsCustom(explicitShy.Render, context, 68, 120);
        Assert.Equal(explicitRefPixels, explicitShyPixels);

        // 2. Corrupt dictionary SHA-256 safely falls back to null without throwing
        CssHyphenator.RegisterTestDictionary(new CssHyphenationData.RawDictionary(
            "zz-corrupt", ["zz-corrupt"], "corrupt.pat.txt", "https://example.invalid/corrupt", "MIT",
            "0000000000000000000000000000000000000000000000000000000000000000",
            2, 2, ["a1b"], []));
        try
        {
            Assert.Null(CssHyphenator.ResolveDictionary("zz-corrupt"));
            Assert.Null(CssHyphenator.ResolveDictionary("xx-YY"));

            // 3. Dynamic Language switching updates breaks and spelling changes immediately
            var dynamicTb = new TextBlock
            {
                Text = "Zucker",
                Language = XmlLanguage.GetLanguage("xx-YY")
            };
            Css.SetStyle(dynamicTb, boxCss);
            LayoutCustom(dynamicTb, 68, 120);

            // Unknown language -> no automatic hyphenation -> 1 line
            Assert.Single(dynamicTb.GetSourceRangeSegments(0, dynamicTb.Text.Length)
                .Where(r => Math.Abs(r.EndX - r.StartX) > 0.01)
                .Select(r => Math.Round(r.Baseline, 2))
                .Distinct());

            // Switch to de-1901 -> breaks as "Zuk-" / "ker" (2 lines)
            dynamicTb.Language = XmlLanguage.GetLanguage("de-1901");
            LayoutCustom(dynamicTb, 68, 120);
            Assert.Equal(2, dynamicTb.GetSourceRangeSegments(0, dynamicTb.Text.Length)
                .Where(r => Math.Abs(r.EndX - r.StartX) > 0.01)
                .Select(r => Math.Round(r.Baseline, 2))
                .Distinct()
                .Count());

            // Switch to zz-corrupt -> falls back safely to 1 line
            dynamicTb.Language = XmlLanguage.GetLanguage("zz-corrupt");
            LayoutCustom(dynamicTb, 68, 120);
            Assert.Single(dynamicTb.GetSourceRangeSegments(0, dynamicTb.Text.Length)
                .Where(r => Math.Abs(r.EndX - r.StartX) > 0.01)
                .Select(r => Math.Round(r.Baseline, 2))
                .Distinct());
        }
        finally
        {
            CssHyphenator.ResetTestDictionaries();
        }
    }

    [Fact]
    public void GeneratedBeforeAndAfterParticipateInCrossNodeTextTransformAndAutoHyphenationWithoutMutatingSource()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);

        // 1. Greek ("el") final sigma across ::before ("ΟΣ") + TextBlock ("Ο") + ::after ("Σ!") -> "οσος!"
        const string greekCss = "display:block;width:160px;height:60px;box-sizing:border-box;padding:4px;font-family:DejaVu Sans;font-size:22px;line-height:36px;white-space:normal;color:#1d3557;text-transform:lowercase";
        var greekHost = new TextBlock
        {
            Text = "Ο",
            Language = XmlLanguage.GetLanguage("el")
        };
        Css.SetClass(greekHost, "greek");
        Css.SetStyle(greekHost, greekCss);
        Css.SetStyleSheet(greekHost, ".greek::before{content:'ΟΣ';display:inline;}.greek::after{content:'Σ!';display:inline;}");
        LayoutCustom(greekHost, 160, 60);

        var greekExpected = new TextBlock
        {
            Text = "ο",
            Language = XmlLanguage.GetLanguage("el")
        };
        Css.SetClass(greekExpected, "greek-ref");
        Css.SetStyle(greekExpected, greekCss + ";text-transform:none");
        Css.SetStyleSheet(greekExpected, ".greek-ref::before{content:'οσ';display:inline;}.greek-ref::after{content:'ς!';display:inline;}");
        LayoutCustom(greekExpected, 160, 60);

        var greekBroken = new TextBlock
        {
            Text = "ο",
            Language = XmlLanguage.GetLanguage("el")
        };
        Css.SetClass(greekBroken, "greek-broken");
        Css.SetStyle(greekBroken, greekCss + ";text-transform:none");
        Css.SetStyleSheet(greekBroken, ".greek-broken::before{content:'ος';display:inline;}.greek-broken::after{content:'σ!';display:inline;}");
        LayoutCustom(greekBroken, 160, 60);

        var greekExpectedPixels = PixelsCustom(greekExpected.Render, context, 160, 60);
        var greekBrokenPixels = PixelsCustom(greekBroken.Render, context, 160, 60);
        var greekActualPixels = PixelsCustom(greekHost.Render, context, 160, 60);
        Assert.NotEqual(greekExpectedPixels, greekBrokenPixels);
        Assert.Equal(greekExpectedPixels, greekActualPixels);
        Assert.Equal("Ο", greekHost.Text);

        // 2. Auto hyphenation across TextBlock ("Zuc") + ::after ("ker") and ::before ("Zuc") + TextBlock ("ker") with hyphenate-limit-chars: 6 2 2
        const string hyphenCss = "display:block;width:68px;height:120px;box-sizing:border-box;padding:4px;font-family:DejaVu Sans;font-size:20px;line-height:36px;white-space:normal;color:#1d3557;hyphens:auto;hyphenate-character:'-';hyphenate-limit-chars:6 2 2";
        var hyphenExpected = new TextBlock
        {
            Text = "Zuk-\nker",
            Language = XmlLanguage.GetLanguage("de-1901")
        };
        Css.SetStyle(hyphenExpected, hyphenCss + ";white-space:pre-line;hyphens:none");
        LayoutCustom(hyphenExpected, 68, 120);
        var hyphenExpectedPixels = PixelsCustom(hyphenExpected.Render, context, 68, 120);

        var hyphenAfterHost = new TextBlock
        {
            Text = "Zuc",
            Language = XmlLanguage.GetLanguage("de-1901")
        };
        Css.SetClass(hyphenAfterHost, "zucker-after");
        Css.SetStyle(hyphenAfterHost, hyphenCss);
        Css.SetStyleSheet(hyphenAfterHost, ".zucker-after::after{content:'ker';display:inline;hyphens:auto;hyphenate-character:'-';hyphenate-limit-chars:6 2 2;}");
        LayoutCustom(hyphenAfterHost, 68, 120);
        Assert.Equal(hyphenExpectedPixels, PixelsCustom(hyphenAfterHost.Render, context, 68, 120));
        Assert.Equal("Zuc", hyphenAfterHost.Text);

        var hyphenBeforeHost = new TextBlock
        {
            Text = "ker",
            Language = XmlLanguage.GetLanguage("de-1901")
        };
        Css.SetClass(hyphenBeforeHost, "zucker-before");
        Css.SetStyle(hyphenBeforeHost, hyphenCss);
        Css.SetStyleSheet(hyphenBeforeHost, ".zucker-before::before{content:'Zuc';display:inline;hyphens:auto;hyphenate-character:'-';hyphenate-limit-chars:6 2 2;}");
        LayoutCustom(hyphenBeforeHost, 68, 120);
        Assert.Equal(hyphenExpectedPixels, PixelsCustom(hyphenBeforeHost.Render, context, 68, 120));
        Assert.Equal("ker", hyphenBeforeHost.Text);
    }

    [Fact]
    public void AutoHyphenationRespectsLimitLinesLimitLastLimitZoneAndCustomHyphenateCharacter()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string baseCss = "display:block;width:68px;height:160px;box-sizing:border-box;padding:4px;font-family:DejaVu Sans;font-size:20px;line-height:36px;white-space:normal;color:#1d3557;hyphens:auto;hyphenate-limit-chars:4 2 2";

        // 1. Custom hyphenate-character ('·') with de-1901 spelling change ("Zucker" -> "Zuk·\nker")
        var customCharActual = new TextBlock { Text = "Zucker", Language = XmlLanguage.GetLanguage("de-1901") };
        Css.SetStyle(customCharActual, baseCss + ";hyphenate-character:'·'");
        LayoutCustom(customCharActual, 68, 160);

        var customCharExpected = new TextBlock { Text = "Zuk·\nker", Language = XmlLanguage.GetLanguage("de-1901") };
        Css.SetStyle(customCharExpected, baseCss + ";white-space:pre-line;hyphens:none");
        LayoutCustom(customCharExpected, 68, 160);
        Assert.Equal(PixelsCustom(customCharExpected.Render, context, 68, 160), PixelsCustom(customCharActual.Render, context, 68, 160));

        // 2. hyphenate-limit-last: always suppresses hyphenation on the last line before paragraph end
        var limitLastAlways = new TextBlock { Text = "Zucker", Language = XmlLanguage.GetLanguage("de-1901") };
        Css.SetStyle(limitLastAlways, baseCss + ";hyphenate-character:'-';hyphenate-limit-last:always");
        LayoutCustom(limitLastAlways, 68, 160);
        Assert.Single(limitLastAlways.GetSourceRangeSegments(0, limitLastAlways.Text.Length)
            .Where(r => Math.Abs(r.EndX - r.StartX) > 0.01)
            .Select(r => Math.Round(r.Baseline, 2))
            .Distinct());

        // 3. hyphenate-limit-zone: 60px suppresses hyphenation when word begins inside the zone
        var limitZone = new TextBlock { Text = "Zucker", Language = XmlLanguage.GetLanguage("de-1901") };
        Css.SetStyle(limitZone, baseCss + ";hyphenate-character:'-';hyphenate-limit-zone:60px");
        LayoutCustom(limitZone, 68, 160);
        Assert.Single(limitZone.GetSourceRangeSegments(0, limitZone.Text.Length)
            .Where(r => Math.Abs(r.EndX - r.StartX) > 0.01)
            .Select(r => Math.Round(r.Baseline, 2))
            .Distinct());

        // 4. hyphenate-limit-lines: 1 and 2 limit consecutive hyphenated lines compared to no-limit
        const string linesCss = "display:block;width:60px;height:180px;box-sizing:border-box;padding:4px;font-family:DejaVu Sans;font-size:20px;line-height:36px;white-space:normal;color:#1d3557;hyphens:auto;hyphenate-limit-chars:4 2 2;hyphenate-character:'-'";
        int CountLines(string limitRule)
        {
            var tb = new TextBlock { Text = "Silbentrennung", Language = XmlLanguage.GetLanguage("de-1996") };
            Css.SetStyle(tb, linesCss + ";hyphenate-limit-lines:" + limitRule);
            LayoutCustom(tb, 60, 180);
            return tb.GetSourceRangeSegments(0, tb.Text.Length)
                .Where(r => Math.Abs(r.EndX - r.StartX) > 0.01)
                .Select(r => Math.Round(r.Baseline, 2))
                .Distinct()
                .Count();
        }

        Assert.Equal(4, CountLines("no-limit"));
        Assert.Equal(3, CountLines("2"));
        Assert.Equal(2, CountLines("1"));
    }

    [Fact]
    public void ExportT2AcceptanceScreenshotsWhenRequested()
    {
        var dir = Environment.GetEnvironmentVariable("JALIUM_CSS_ACCEPTANCE_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);

        // Panel 1: Cross-node language & word context
        var panel1 = new StackPanel();
        Css.SetStyle(panel1, "display:block;width:520px;height:280px;padding:12px;background-color:#f8f9fa;box-sizing:border-box");
        (string Label, string Lang, string Mode, string PartA, string PartB, string PartC)[] rows1 =
        [
            ("el lowercase: ΛΟΓ|ΟΣ", "el", "lowercase", "ΛΟΓ", "ΟΣ", ""),
            ("lt lowercase: Į\u0307|A", "lt", "lowercase", "Į", "\u0307A", ""),
            ("tr uppercase: i|stanbul", "tr", "uppercase", "i", "stanbul", ""),
            ("nl capitalize: i|jsvrij", "nl", "capitalize", "i", "jsvrij", ""),
            ("en capitalize: super-|cali|fragilistic", "en-US", "capitalize", "super-", "cali", "fragilistic")
        ];
        foreach (var row in rows1)
        {
            var lineBox = new StackPanel { Language = XmlLanguage.GetLanguage(row.Lang) };
            Css.SetStyle(lineBox, $"display:block;margin-bottom:8px;padding:6px 10px;background-color:#ffffff;border:1px solid #dee2e6;font-family:DejaVu Sans;font-size:18px;line-height:28px;color:#1d3557;text-transform:{row.Mode}");
            var prefix = new TextBlock { Text = row.Label + "  →  ", Language = XmlLanguage.GetLanguage("en-US") };
            Css.SetStyle(prefix, "display:inline;color:#495057;text-transform:none;font-size:15px");
            var a = new TextBlock { Text = row.PartA, Language = XmlLanguage.GetLanguage(row.Lang) };
            var b = new TextBlock { Text = row.PartB, Language = XmlLanguage.GetLanguage(row.Lang) };
            Css.SetStyle(a, "display:inline;color:#d62828");
            Css.SetStyle(b, "display:inline;color:#1d3557");
            lineBox.Children.Add(prefix);
            lineBox.Children.Add(a);
            lineBox.Children.Add(b);
            if (!string.IsNullOrEmpty(row.PartC))
            {
                var c = new TextBlock { Text = row.PartC, Language = XmlLanguage.GetLanguage(row.Lang) };
                Css.SetStyle(c, "display:inline;color:#2a9d8f");
                lineBox.Children.Add(c);
            }
            panel1.Children.Add(lineBox);
        }
        LayoutCustom(panel1, 520, 280);
        SavePng(Path.Combine(dir, "t2-cross-node-text-transform-and-word-context.png"), PixelsCustom(panel1.Render, context, 520, 280), 520, 280);

        // Panel 2: Automatic hyphenation & orthographic spelling changes
        var panel2 = new StackPanel();
        Css.SetStyle(panel2, "display:flex;flex-direction:row;flex-wrap:wrap;gap:10px;width:540px;height:280px;padding:12px;background-color:#f8f9fa;box-sizing:border-box");
        (string Title, string Lang, string Word, int CardWidth, int BodyWidth)[] cards =
        [
            ("de-1901 (ck→k-k)", "de-1901", "Zucker", 118, 56),
            ("nl (ë→e-e)", "nl", "reëel", 118, 38),
            ("nl (ee→é-)", "nl", "cafeetje", 118, 56),
            ("hu (ssz→sz-sz)", "hu", "asszony", 118, 56),
            ("ca (l·l→l-l)", "ca", "paral·lel", 118, 58),
            ("de-1996", "de-1996", "Silbentrennung", 118, 66),
            ("en-US", "en-US", "hyphenation", 118, 66),
            ("fr", "fr", "typographie", 118, 66)
        ];
        foreach (var card in cards)
        {
            var box = new StackPanel { Language = XmlLanguage.GetLanguage(card.Lang) };
            Css.SetStyle(box, $"display:block;width:{card.CardWidth}px;height:122px;padding:6px;background-color:#ffffff;border:1px solid #dee2e6;box-sizing:border-box");
            var header = new TextBlock { Text = card.Title, Language = XmlLanguage.GetLanguage("en-US") };
            Css.SetStyle(header, "display:block;font-family:DejaVu Sans;font-size:11px;line-height:15px;color:#6c757d;margin-bottom:4px;hyphens:none");
            var body = new TextBlock { Text = card.Word, Language = XmlLanguage.GetLanguage(card.Lang) };
            Css.SetStyle(body, $"display:block;width:{card.BodyWidth}px;font-family:DejaVu Sans;font-size:18px;line-height:24px;color:#1d3557;hyphens:auto;hyphenate-character:'-';hyphenate-limit-chars:4 2 2");
            box.Children.Add(header);
            box.Children.Add(body);
            panel2.Children.Add(box);
        }
        LayoutCustom(panel2, 540, 280);
        SavePng(Path.Combine(dir, "t2-auto-hyphenation-and-spelling-changes.png"), PixelsCustom(panel2.Render, context, 540, 280), 540, 280);
    }

    private static void SavePng(string path, byte[] bgra, int width, int height)
    {
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void LayoutCustom(FrameworkElement root, double width, double height)
    {
        for (int i = 0; i < 4; i++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            Dispatcher.GetForCurrentThread().ProcessQueue();
            root.Measure(new(width, height));
            root.Arrange(new(0, 0, width, height));
            root.UpdateLayout();
        }
    }

    private static byte[] PixelsCustom(Action<DrawingContext> paint, RenderContext context, int width, int height)
    {
        using var target = context.CreateOffscreenRenderTarget(width, height);
        target.RequestReadback();
        target.BeginDraw();
        target.Clear(0, 0, 0, 0);
        using (var dc = new RenderTargetDrawingContext(target, context))
        {
            paint(dc);
            dc.Close();
        }
        target.EndDraw();
        var pixels = new byte[width * height * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, (uint)(width * 4), out _, out _));
        return pixels;
    }

    private static void AssertConditionalGlyph(TextBlock source, int index, bool visible)
    {
        var ranges = source.GetSourceRangeSegments(index, 1);
        if (visible) Assert.True(ranges.Any(r => Math.Abs(r.EndX - r.StartX) > .01), "The conditional hyphen must own visible glyph geometry.");
        else Assert.All(ranges, r => Assert.Equal(0, Math.Abs(r.EndX - r.StartX), 3));
    }

    private static string Style(string direction, string limits)
        => "display:block;width:72px;height:180px;box-sizing:border-box;padding:4px;font-family:DejaVu Sans;font-size:22px;line-height:40px;white-space:normal;color:#d83253;hyphens:manual;hyphenate-character:'-';hyphenate-limit-chars:" + limits +
            (direction == "ordinary" ? "" : direction == "plaintext" ? ";direction:ltr;unicode-bidi:plaintext" : ";direction:" + direction);
    private static void Layout(FrameworkElement root)
    { for (int i = 0; i < 3; i++) { Dispatcher.GetForCurrentThread().ProcessQueue(); root.Measure(new(72, 180)); root.Arrange(new(0, 0, 72, 180)); root.UpdateLayout(); } }
    private static byte[] Pixels(Action<DrawingContext> paint, RenderContext context)
    {
        using var target = context.CreateOffscreenRenderTarget(160, 180); target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
        using (var dc = new RenderTargetDrawingContext(target, context)) { paint(dc); dc.Close(); } target.EndDraw(); var pixels = new byte[160 * 180 * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 160 * 4, out _, out _)); return pixels;
    }
}
