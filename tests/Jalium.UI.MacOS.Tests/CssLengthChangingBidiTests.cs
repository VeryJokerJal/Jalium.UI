using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Automation.Peers;
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
public sealed class CssLengthChangingBidiTests
{
    public sealed record Mapping(string Source, string Display, string Transform, string Language, int[] Offsets);
    public static readonly Mapping[] Mappings =
    [
        new("ASSB", "ASSB", "none", "en", [0, 1, 2, 3, 4]),
        new("aßb", "ASSB", "uppercase", "en", [0, 1, 3, 4]),
        new("İß", "i\u0307ß", "lowercase", "en", [0, 2, 3]),
        new("ﬃA", "FFIA", "uppercase", "en", [0, 3, 4]),
        new("I\u0301", "i\u0307\u0301", "lowercase", "lt", [0, 2, 3]),
        new("I\u0307", "i", "lowercase", "tr", [0, 1, 1]),
        new("i\u0307\u0301", "I\u0301", "uppercase", "lt", [0, 1, 1, 2]),
        new("ßi\u0307", "SSI", "uppercase", "lt", [0, 2, 3, 3]),
        new("𐐀", "𐐨", "lowercase", "en", [0, 0, 2]),
    ];

    public static IEnumerable<object[]> Cases()
    {
        for (int mapping = 0; mapping < Mappings.Length; mapping++)
            foreach (string host in new[] { "standalone", "runs", "shared" })
                foreach (bool rtl in new[] { false, true })
                    foreach (int letter in new[] { 0, 2, -1 }) yield return [mapping, host, rtl, letter];
    }

    [Theory] [MemberData(nameof(Cases))]
    public void DisplayMappingKeepsWholeParagraphPixelsAndOriginalSourceGeometry(int mapping, string host, bool rtl, int letter)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var item = Mappings[mapping]; const string prefix = "אב ", suffix = " ג";
        string source = prefix + item.Source + suffix, display = prefix + item.Display + suffix;
        FrameworkElement root; TextBlock[] texts;
        var language = XmlLanguage.GetLanguage(item.Language);
        if (host == "shared")
        {
            var panel = new StackPanel { Language = language }; root = panel;
            texts = new[] { prefix, item.Source, suffix }.Select(value => new TextBlock { Text = value, IsTextSelectionEnabled = true }).ToArray();
            foreach (var text in texts) { panel.Children.Add(text); Css.SetStyle(text, "display:inline"); }
        }
        else
        {
            var text = new TextBlock { Language = language, IsTextSelectionEnabled = true }; root = text; texts = [text];
            if (host == "runs")
            {
                var span = new Span(); span.Inlines.Add(new Run(item.Source));
                text.Inlines.Add(new Run(prefix)); text.Inlines.Add(span); text.Inlines.Add(new Run(suffix));
            }
            else text.Text = source;
        }
        string style = Style(item.Transform, rtl, letter, host == "shared"); Css.SetStyle(root, style); Layout(root);
        var first = texts[0]; string family = first.FontFamily.GetRenderingSource(first);
        using var reference = NativeTextParagraph.TryCreateAdvanced([new(display, family, 24, 400, 0, Colors.Black)], family, 24,
            10000000, 1, rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            new CssInputTextSpacing(letter, 3).Units(display), contextPaint: true, textOptionsOwner: first);
        Assert.NotNull(reference); var native = Assert.Single(reference.Lines);
        var firstOffset = ReferenceEquals(first, root) ? default : first.TransformToAncestor(root);
        double baseline = firstOffset.Y + Assert.Single(first.GetSourceRangeSegments(0, 1)).Baseline;
        double left = (rtl ? 312 - native.Metrics.Line.Width : 8) - native.Metrics.Line.X;
        var brush = Assert.IsType<SolidColorBrush>(first.Foreground);
        var expected = Pixels(dc => dc.DrawText(new FormattedText(display, family, 24)
        {
            Foreground = brush,
            PlatformTextLine = native.ForSource(0, display.Length, brush.Color, 1)
                .WithBrushLineBox(native.Metrics.Baseline - (baseline - 8), 40)
        }, new(left, baseline - native.Metrics.Baseline)), context);
        Assert.Equal(expected, Pixels(root.Render, context));

        var offsets = Enumerable.Range(0, prefix.Length).Concat(item.Offsets.Select(value => prefix.Length + value))
            .Concat(Enumerable.Range(1, suffix.Length).Select(value => prefix.Length + item.Display.Length + value)).ToArray();
        Assert.Equal(source.Length + 1, offsets.Length);
        int rawStart = 0;
        foreach (var text in texts)
        {
            var origin = ReferenceEquals(text, root) ? default : text.TransformToAncestor(root);
            var boundaries = GraphemeClusters.GetBoundaries(text.Text);
            for (int i = 0; i + 1 < boundaries.Length; i++)
            {
                int a = boundaries[i], b = boundaries[i + 1], nativeFirst = offsets[rawStart + a], nativeEnd = offsets[rawStart + b];
                var wanted = reference.Selection(0, nativeFirst, nativeEnd - nativeFirst); var actual = text.GetSourceRangeSegments(a, b - a);
                Assert.NotEmpty(wanted); Assert.NotEmpty(actual);
                Near(left + wanted.Min(r => r.X), origin.X + actual.Min(r => Math.Min(r.StartX, r.EndX)));
                Near(wanted.Sum(r => (double)r.Width), actual.Sum(r => Math.Abs(r.EndX - r.StartX)));
                Assert.All(actual, r => Near(baseline, origin.Y + r.Baseline));
                foreach (var rect in actual.Where(r => Math.Abs(r.EndX - r.StartX) > .01))
                    foreach (double fraction in new[] { .2, .8 })
                    {
                        var hit = Assert.IsType<TextPointer>(text.GetPositionFromPoint(new(rect.StartX + (rect.EndX - rect.StartX) * fraction, rect.Baseline - 2), false));
                        int index = text.ContentStart.GetOffsetToPosition(hit);
                        Assert.True(index == a || index == b, $"A source cluster {a}..{b} returned interior caret {index}.");
                    }
            }
            text.SelectAll(); Assert.Equal(text.Text, text.SelectedText); text.ClearSelection(); rawStart += text.Text.Length;
        }
        Assert.Equal(source, string.Concat(texts.Select(text => text.Text)));
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); root.Render(recorder); var frame = recorder.Commit();
        Assert.True(frame.IsFullyRecordable);
        Css.SetStyle(root, Style("none", rtl, letter, host == "shared")); Layout(root);
        Assert.Equal(expected, Pixels(dc => DrawingReplayer.Replay(frame, dc), context));
        Css.SetStyle(root, style); Layout(root); Assert.Equal(expected, Pixels(root.Render, context));
    }

    [Theory]
    [InlineData(1, false, 0)] [InlineData(1, true, 0)]
    [InlineData(1, false, 2)] [InlineData(1, true, 2)]
    [InlineData(2, false, 0)] [InlineData(2, true, 0)]
    [InlineData(2, false, 2)] [InlineData(2, true, 2)]
    [InlineData(7, false, 0)] [InlineData(7, true, 0)]
    [InlineData(7, false, 2)] [InlineData(7, true, 2)]
    public void WritingToolsMasksTheDisplayedGlyphsOwnedByTheOriginalSource(int mapping, bool rtl, int letter)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var item = Mappings[mapping]; const string prefix = "אב ", suffix = " ג";
        string sourceText = prefix + item.Source + suffix, display = prefix + item.Display + suffix;
        var owner = new TextBox { Text = sourceText };
        var text = new PresentationTextBlock(owner) { Text = sourceText, Language = XmlLanguage.GetLanguage(item.Language) };
        Css.SetStyle(text, Style(item.Transform, rtl, letter, false)); Layout(text);
        string family = text.FontFamily.GetRenderingSource(text);
        using var reference = NativeTextParagraph.TryCreateAdvanced([new(display, family, 24, 400, 0, Colors.Black)], family, 24,
            10000000, 1, rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            new CssInputTextSpacing(letter, 3).Units(display), contextPaint: true, textOptionsOwner: text);
        Assert.NotNull(reference); var native = Assert.Single(reference.Lines);
        double baseline = Assert.Single(text.GetSourceRangeSegments(0, 1)).Baseline;
        double left = (rtl ? 312 - native.Metrics.Line.Width : 8) - native.Metrics.Line.X;
        int selected = mapping == 1 ? 1 : 0;
        byte[] Expected(byte visibility) => Pixels(dc => dc.DrawText(new FormattedText(display, family, 24)
        {
            Foreground = text.Foreground,
            PlatformTextLine = native.ForSource(0, display.Length, Assert.IsType<SolidColorBrush>(text.Foreground).Color, 1)
                .WithBrushLineBox(native.Metrics.Baseline - (baseline - 8), 40),
            GlyphVisibility = visibility,
            GlyphRanges = [(uint)(prefix.Length + item.Offsets[selected]), (uint)(item.Offsets[selected + 1] - item.Offsets[selected])]
        }, new(left, baseline - native.Metrics.Baseline)), context);
        var source = ((AutomationTextProvider)owner.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!).Source;
        var original = Pixels(text.Render, context); Assert.Equal(Expected(0), original);
        var expected = Expected(2);
        MacOSWritingToolsPresentation.Prepare(owner, source, "display-map", prefix.Length + selected, 1, 0, () => true);
        RecordedDrawing frame;
        try
        {
            Assert.Equal(expected, Pixels(text.Render, context)); Assert.NotEqual(original, expected);
            var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); text.Render(recorder); frame = recorder.Commit();
            Assert.True(frame.IsFullyRecordable); Assert.Equal(sourceText, source.Text); Assert.Equal(sourceText, text.Text);
        }
        finally { MacOSWritingToolsPresentation.Finish(owner, "display-map"); }
        Assert.Equal(original, Pixels(text.Render, context));
        Assert.Equal(expected, Pixels(dc => DrawingReplayer.Replay(frame, dc), context));
    }

    // Document projections expose service offsets independently of their visual
    // TextBlocks. This isolates that contract while exercising native pixels.
    private sealed class PresentationTextBlock(UIElement owner) : TextBlock
    {
        protected override bool ParticipatesInRenderCache => false;
        internal override UIElement? DocumentWritingToolsOwner => owner;
        internal override IEnumerable<(int Start, int Length)> DocumentWritingToolsRanges(int start, int length)
            => [(start, length)];
    }

    public static IEnumerable<object[]> CompoundCases()
    {
        foreach (string scene in new[] { "wrapped", "tabs", "pure-ltr-tabs", "justify", "pure-ltr", "collapse", "hyphen", "gradient", "scope", "colored" })
            foreach (string host in new[] { "standalone", "runs", "shared" })
                foreach (bool rtl in new[] { false, true }) yield return [scene, host, rtl];
    }

    [Theory] [MemberData(nameof(CompoundCases))]
    public void MappedTextComposesWithWrappingTabsAutospaceAndJustification(string scene, string host, bool rtl)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var (source, display, extra, width) = scene switch
        {
            "wrapped" => ("aßb aßb aßb", "ASSB ASSB ASSB", "", 120),
            "tabs" or "pure-ltr-tabs" => ("中ßA\t文", "中SSA\t文", "tab-size:3;text-autospace:normal;letter-spacing:2px", 320),
            "justify" => ("中ßA 文 ﬃB 甲", "中SSA 文 FFIB 甲", "text-autospace:normal;text-align:justify;text-align-last:justify;text-justify:inter-character;letter-spacing:1px", 320),
            "pure-ltr" => ("aßb ﬃA", "ASSB FFIA", "text-align:justify;text-align-last:justify;text-justify:inter-character", 320),
            "collapse" => ("  aßb \t   İ   ", "  ASSB \t   İ   ", "white-space:normal", 320),
            "hyphen" => ("aßb\u00ADcdef", "ASSB\u00ADCDEF", "hyphens:manual;hyphenate-character:'-';hyphenate-limit-chars:1 1 1", 120),
            "scope" or "colored" => ("aßb ﬃA", "ASSB FFIA", "letter-spacing:2px", 320),
            _ => ("中aßb ﬃA 文", "中ASSB FFIA 文", "text-autospace:normal;letter-spacing:2px", 320),
        };
        string prefix = scene.StartsWith("pure-ltr", StringComparison.Ordinal) ? "AV " : "אב ", suffix = scene.StartsWith("pure-ltr", StringComparison.Ordinal) ? " Z" : " ג";
        var actual = Build(source, "uppercase", out var actualTexts); var reference = Build(display, "none", out _);
        var expected = Pixels(reference.Render, context); Assert.Equal(expected, Pixels(actual.Render, context));
        if (scene is "justify" or "pure-ltr")
        {
            var edges = actualTexts.SelectMany(text =>
            {
                double x = ReferenceEquals(text, actual) ? 0 : text.TransformToAncestor(actual).X;
                return text.GetSourceRangeSegments(0, text.Text.Length).Select(r => (Left: x + Math.Min(r.StartX, r.EndX), Right: x + Math.Max(r.StartX, r.EndX)));
            }).ToArray();
            Near(8, edges.Min(r => r.Left)); Near(width - 8, edges.Max(r => r.Right));
        }
        Assert.Equal(prefix + source + suffix, string.Concat(actualTexts.Select(t => t.Text)));
        if (scene is "wrapped" or "hyphen")
            Assert.True(actualTexts.SelectMany(t => t.GetSourceRangeSegments(0, t.Text.Length)
                .Select(r => (ReferenceEquals(t, actual) ? 0 : t.TransformToAncestor(actual).Y) + r.Baseline)).Distinct().Count() > 1);
        foreach (var text in actualTexts)
        {
            var ranges = text.GetSourceRangeSegments(0, text.Text.Length); Assert.NotEmpty(ranges);
            foreach (var rect in ranges.Where(r => Math.Abs(r.EndX - r.StartX) > .01))
            {
                var position = Assert.IsType<TextPointer>(text.GetPositionFromPoint(new((rect.StartX + rect.EndX) / 2, rect.Baseline - 2), false));
                Assert.Contains(text.ContentStart.GetOffsetToPosition(position), GraphemeClusters.GetBoundaries(text.Text));
            }
            text.SelectAll(); Assert.Equal(text.Text, text.SelectedText); text.ClearSelection();
        }
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); actual.Render(recorder); var frame = recorder.Commit();
        Assert.True(frame.IsFullyRecordable); Assert.Equal(expected, Pixels(dc => DrawingReplayer.Replay(frame, dc), context));

        FrameworkElement Build(string value, string transform, out TextBlock[] texts)
        {
            FrameworkElement root;
            if (host == "shared")
            {
                var panel = new StackPanel(); root = panel;
                texts = new[] { prefix, value, suffix }.Select(s => new TextBlock { Text = s, IsTextSelectionEnabled = true }).ToArray();
                for (int i = 0; i < texts.Length; i++)
                {
                    Css.SetStyle(texts[i], "display:inline");
                    if (scene == "scope" && i == 1) Css.SetStyle(texts[i], "display:inline;unicode-bidi:isolate-override;direction:rtl");
                    if (scene == "colored" && i == 1) { texts[i].FontSize = 30; texts[i].FontWeight = FontWeights.Bold; texts[i].Foreground = Brushes.Blue; }
                    if (scene == "tabs" && i == 1)
                    { var frame = new Border { Child = texts[i] }; Css.SetStyle(frame, "display:inline;padding:0 3px"); panel.Children.Add(frame); }
                    else panel.Children.Add(texts[i]);
                }
            }
            else
            {
                var text = new TextBlock { IsTextSelectionEnabled = true }; root = text; texts = [text];
                if (host == "runs")
                {
                    text.Inlines.Add(new Run(prefix)); var span = new Span(); span.Inlines.Add(new Run(value));
                    if (scene == "scope") Css.SetStyle(span, "unicode-bidi:isolate-override;direction:rtl");
                    if (scene == "colored") { span.FontSize = 30; span.FontWeight = FontWeights.Bold; span.Foreground = Brushes.Blue; }
                    text.Inlines.Add(span); text.Inlines.Add(new Run(suffix));
                }
                else text.Text = prefix + value + suffix;
            }
            Css.SetStyle(root, Style(transform, rtl, 0, host == "shared") + ";width:" + width + "px;" + extra);
            if (host == "standalone" && scene == "scope") Css.SetStyle(root, Style(transform, true, 2, false) + ";unicode-bidi:isolate-override");
            if (host == "standalone" && scene == "colored") { texts[0].FontSize = 30; texts[0].FontWeight = FontWeights.Bold; texts[0].Foreground = Brushes.Blue; }
            if (scene == "gradient") foreach (var text in texts) text.Foreground = new LinearGradientBrush(Colors.Red, Colors.Blue, 0);
            Layout(root, width); return root;
        }
    }

    [Theory]
    [InlineData("standalone", false, "word-break:break-all")]
    [InlineData("standalone", true, "word-break:break-all")]
    [InlineData("runs", false, "line-break:anywhere")]
    [InlineData("runs", true, "line-break:anywhere")]
    [InlineData("shared", false, "overflow-wrap:anywhere")]
    [InlineData("shared", true, "overflow-wrap:anywhere")]
    public void SplitDisplaySlicesAcrossLinesMatchLiteralPixelsAndMultiLineSourceGeometry(string host, bool rtl, string wrapRule)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source = "MßM", display = "MSSM";
        const int width = 56;
        var actual = BuildSplit(source, "uppercase", out var actualTexts);
        var reference = BuildSplit(display, "none", out var refTexts);
        var expected = Pixels(reference.Render, context);
        var actualPixels = Pixels(actual.Render, context);
        SaveScreenshotIfRequested($"t1-1-split-slices-{host}-{(rtl ? "rtl" : "ltr")}.png", actualPixels);
        Assert.NotEqual(Pixels(_ => { }, context), expected);
        Assert.Equal(expected, actualPixels);

        var refFirstS = Assert.Single(refTexts[host == "shared" ? 1 : 0].GetSourceRangeSegments(host == "shared" ? 0 : 1, 1));
        var refSecondS = Assert.Single(refTexts[host == "shared" ? 1 : 0].GetSourceRangeSegments(host == "shared" ? 1 : 2, 1));
        Assert.True(refSecondS.Baseline > refFirstS.Baseline + 10, "Literal reference 'SS' must wrap across two lines at width 56px.");

        var target = actualTexts[host == "shared" ? 1 : 0];
        int sharpSIndex = host == "shared" ? 0 : 1;
        var segments = target.GetSourceRangeSegments(sharpSIndex, 1);
        Assert.Equal(2, segments.Count);
        Near(refFirstS.Baseline, segments[0].Baseline);
        Near(refSecondS.Baseline, segments[1].Baseline);
        Near(Math.Abs(refFirstS.EndX - refFirstS.StartX), Math.Abs(segments[0].EndX - segments[0].StartX));
        Near(Math.Abs(refSecondS.EndX - refSecondS.StartX), Math.Abs(segments[1].EndX - segments[1].StartX));

        var bounds = target.DocumentTextBounds(sharpSIndex, 1);
        Assert.Equal(2, bounds.Count);
        Assert.True(bounds[1].Top > bounds[0].Top + 10);

        foreach (var seg in segments)
        {
            var hit = Assert.IsType<TextPointer>(target.GetPositionFromPoint(new((seg.StartX + seg.EndX) / 2, seg.Baseline - 2), false));
            int offset = target.ContentStart.GetOffsetToPosition(hit);
            Assert.True(offset == sharpSIndex || offset == sharpSIndex + 1);
        }

        Assert.Equal(source, string.Concat(actualTexts.Select(t => t.Text)));

        // Three-grapheme expansion (ﬃ -> FFI) also splits cleanly across lines and preserves source ownership.
        {
            const string ligatureSource = "MﬃM", ligatureDisplay = "MFFIM";
            var ligatureActual = BuildSplit(ligatureSource, "uppercase", out var ligActualTexts);
            var ligatureRef = BuildSplit(ligatureDisplay, "none", out _);
            var ligExpected = Pixels(ligatureRef.Render, context);
            Assert.Equal(ligExpected, Pixels(ligatureActual.Render, context));
            var ligTarget = ligActualTexts[host == "shared" ? 1 : 0];
            int ligIndex = host == "shared" ? 0 : 1;
            var ligSegments = ligTarget.GetSourceRangeSegments(ligIndex, 1);
            Assert.Equal(2, ligSegments.Count);
            Assert.True(ligSegments[1].Baseline > ligSegments[0].Baseline + 10);
            Assert.Equal(ligatureSource, string.Concat(ligActualTexts.Select(t => t.Text)));
        }

        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); actual.Render(recorder); var frame = recorder.Commit();
        Assert.True(frame.IsFullyRecordable);
        var replayed = Pixels(dc => DrawingReplayer.Replay(frame, dc), context, worker: true);
        Assert.Equal(expected, replayed);

        // Single-grapheme expansion (İ -> i\u0307) and surrogate pair (𐐀 -> 𐐨) must never split internal code units across lines.
        foreach (var (unsplitSource, unsplitTransform, charIndex, charLen) in new[] { ("MİB", "lowercase", 1, 1), ("M𐐀B", "lowercase", 1, 2) })
        {
            var unsplit = new TextBlock { Text = unsplitSource, IsTextSelectionEnabled = true };
            Css.SetStyle(unsplit, Style(unsplitTransform, rtl, 0, false) + $";width:{width}px;{wrapRule}");
            Layout(unsplit, width);
            Assert.Single(unsplit.GetSourceRangeSegments(charIndex, charLen));
        }

        FrameworkElement BuildSplit(string middle, string transform, out TextBlock[] texts)
        {
            string s0 = middle[..1], s1 = middle[1..^1], s2 = middle[^1..];
            FrameworkElement root;
            if (host == "shared")
            {
                var panel = new StackPanel(); root = panel;
                texts = new[] { s0, s1, s2 }.Select(s => new TextBlock { Text = s, IsTextSelectionEnabled = true }).ToArray();
                foreach (var t in texts) { panel.Children.Add(t); Css.SetStyle(t, "display:inline"); }
            }
            else
            {
                var text = new TextBlock { IsTextSelectionEnabled = true }; root = text; texts = [text];
                if (host == "runs")
                {
                    text.Inlines.Add(new Run(s0));
                    var span = new Span(); span.Inlines.Add(new Run(s1));
                    text.Inlines.Add(span); text.Inlines.Add(new Run(s2));
                }
                else text.Text = middle;
            }
            Css.SetStyle(root, Style(transform, rtl, 0, host == "shared") + $";width:{width}px;{wrapRule}");
            Layout(root, width);
            return root;
        }
    }

    [Theory]
    [InlineData("standalone", false, "aßb", "ASSB", 68)]
    [InlineData("standalone", true, "אב aßb", "אב ASSB", 118)]
    [InlineData("runs", false, "aﬃb", "AFFIB", 82)]
    [InlineData("runs", true, "אב aﬃb", "אב AFFIB", 132)]
    public void TextOverflowEllipsisMatchesLiteralTransformedTextAndPreservesSourceOwnership(string host, bool rtl, string source, string display, int width)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var actual = BuildTrimmed(source, "uppercase", out var actualText);
        var reference = BuildTrimmed(display, "none", out _);
        var expected = Pixels(reference.Render, context);
        var actualPixels = Pixels(actual.Render, context);
        SaveScreenshotIfRequested($"t1-2-text-overflow-{host}-{(rtl ? "rtl" : "ltr")}.png", actualPixels);
        Assert.NotEqual(Pixels(_ => { }, context), expected);
        Assert.Equal(expected, actualPixels);
        Assert.Equal(source, actualText.Text);
        actualText.SelectAll(); Assert.Equal(source, actualText.SelectedText); actualText.ClearSelection();

        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); actual.Render(recorder); var frame = recorder.Commit();
        Assert.True(frame.IsFullyRecordable);
        Assert.Equal(expected, Pixels(dc => DrawingReplayer.Replay(frame, dc), context));

        FrameworkElement BuildTrimmed(string value, string transform, out TextBlock text)
        {
            text = new TextBlock { IsTextSelectionEnabled = true };
            if (host == "runs")
            {
                text.Inlines.Add(new Run(value[..1]));
                var span = new Span(); span.Inlines.Add(new Run(value[1..]));
                text.Inlines.Add(span);
            }
            else text.Text = value;
            Css.SetStyle(text, Style(transform, rtl, 1, false) + $";width:{width}px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;text-autospace:normal");
            Layout(text, width);
            return text;
        }
    }

    [Theory]
    [InlineData("standalone", false, "ellipsis")]
    [InlineData("standalone", true, "ellipsis")]
    [InlineData("native-tabbed", false, "'… 更多'")]
    [InlineData("runs", false, "'… 更多'")]
    [InlineData("runs", true, "'… 更多'")]
    [InlineData("shared", false, "ellipsis")]
    [InlineData("shared", true, "'… 更多'")]
    public void LineClampAndBlockEllipsisMatchLiteralTransformedTextAndKeepGeneratedMarkerIndependent(string host, bool rtl, string marker)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        string source = host == "native-tabbed" ? "aß\tﬃA\nbß\tﬃB\ncß\tﬃC" : "אב aßb ﬃA\nגaßb ﬃA\nדaßb ﬃA";
        string display = host == "native-tabbed" ? "ASS\tFFIA\nBSS\tFFIB\nCSS\tFFIC" : "אב ASSB FFIA\nגASSB FFIA\nדASSB FFIA";
        var actual = BuildClamped(source, "uppercase", out var actualTexts);
        var reference = BuildClamped(display, "none", out _);
        var expected = Pixels(reference.Render, context);
        var actualPixels = Pixels(actual.Render, context);
        SaveScreenshotIfRequested($"t1-2-line-clamp-{host}-{(rtl ? "rtl" : "ltr")}.png", actualPixels);
        Assert.NotEqual(Pixels(_ => { }, context), expected);
        Assert.Equal(expected, actualPixels);
        Assert.Equal(source, string.Concat(actualTexts.Select(t => t.Text)));
        foreach (var t in actualTexts)
        {
            t.SelectAll(); Assert.Equal(t.Text, t.SelectedText); t.ClearSelection();
        }
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); actual.Render(recorder); var frame = recorder.Commit();
        Assert.True(frame.IsFullyRecordable);
        Assert.Equal(expected, Pixels(dc => DrawingReplayer.Replay(frame, dc), context));

        FrameworkElement BuildClamped(string value, string transform, out TextBlock[] texts)
        {
            FrameworkElement root;
            if (host == "native-tabbed")
            {
                var panel = new StackPanel(); root = panel;
                var text = new TextBlock { Text = value, IsTextSelectionEnabled = true };
                panel.Children.Add(text);
                texts = [text];
                Css.SetStyle(root, Style(transform, rtl, 0, true) + $";width:220px;tab-size:4;line-clamp:2 {marker}");
                Layout(root, 220);
                return root;
            }
            if (host == "shared")
            {
                var panel = new StackPanel(); root = panel;
                var parts = value.Split('\n');
                texts = new[]
                {
                    new TextBlock { Text = parts[0] + "\n", IsTextSelectionEnabled = true },
                    new TextBlock { Text = parts[1] + "\n", IsTextSelectionEnabled = true },
                    new TextBlock { Text = parts[2], IsTextSelectionEnabled = true },
                };
                foreach (var t in texts) { panel.Children.Add(t); Css.SetStyle(t, "display:inline"); }
            }
            else
            {
                var text = new TextBlock { IsTextSelectionEnabled = true }; root = text; texts = [text];
                if (host == "runs")
                {
                    var parts = value.Split('\n');
                    text.Inlines.Add(new Run(parts[0] + "\n"));
                    var span = new Span(); span.Inlines.Add(new Run(parts[1] + "\n"));
                    text.Inlines.Add(span); text.Inlines.Add(new Run(parts[2]));
                }
                else text.Text = value;
            }
            Css.SetStyle(root, Style(transform, rtl, 1, true) + $";width:220px;line-clamp:2 {marker};text-autospace:normal");
            Layout(root, 220);
            return root;
        }
    }

    [Theory]
    [InlineData("first-letter", "en", "capitalize", "ßeta gamma", "Ss", "eta Gamma")]
    [InlineData("first-letter", "tr", "uppercase", "istanbul izmir", "İ", "STANBUL İZMİR")]
    [InlineData("first-letter", "lt", "uppercase", "i\u0307\u0301ra\u0161as", "I\u0301", "RA\u0160AS")]
    [InlineData("first-line", "en", "capitalize", "ßeta gamma\ndelta", "Sseta Gamma\n", "Delta")]
    [InlineData("first-line", "tr", "lowercase", "I\u0307STANBUL\nI\u0307ZMİR", "istanbul\n", "izmir")]
    public void FirstLetterAndFirstLineProjectionsPreserveContextualAndLocaleLengthChangingTransforms(
        string pseudo, string lang, string transform, string source, string expectedHead, string expectedTail)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var language = XmlLanguage.GetLanguage(lang);
        var actual = new TextBlock { Text = source, Language = language, IsTextSelectionEnabled = true };
        Css.SetClass(actual, "target");
        string baseRule = $".target {{ display:block;width:300px;height:140px;box-sizing:border-box;padding:8px;font-family:DejaVu Sans;font-size:24px;line-height:36px;white-space:pre-line;direction:ltr;color:#1f2937;text-transform:{transform}; }}";
        string pseudoRule = pseudo == "first-letter"
            ? ".target::first-letter { color:#d83253;font-size:28px; }"
            : ".target::first-line { color:#d83253;font-size:28px; }";
        Css.GetStyleSheets(actual).Add(CssStyleSheet.Parse(baseRule + " " + pseudoRule));
        Layout(actual, 300);

        var reference = new TextBlock { Language = language, IsTextSelectionEnabled = true };
        Css.SetClass(reference, "ref");
        string refRule = ".ref { display:block;width:300px;height:140px;box-sizing:border-box;padding:8px;font-family:DejaVu Sans;font-size:24px;line-height:36px;white-space:pre-line;direction:ltr;color:#1f2937;text-transform:none; }";
        if (pseudo == "first-line")
        {
            reference.Text = expectedHead + expectedTail;
            refRule += " .ref::first-line { color:#d83253;font-size:28px; }";
            Css.GetStyleSheets(reference).Add(CssStyleSheet.Parse(refRule));
        }
        else
        {
            var headRun = new Run(expectedHead);
            var tailRun = new Run(expectedTail);
            reference.Inlines.Add(headRun);
            reference.Inlines.Add(tailRun);
            Css.GetStyleSheets(reference).Add(CssStyleSheet.Parse(refRule));
            Css.SetStyle(headRun, "display:inline;color:#d83253;font-size:28px;");
            Css.SetStyle(tailRun, "display:inline;color:#1f2937;font-size:24px;");
        }
        Layout(reference, 300);

        var expected = Pixels(reference.Render, context);
        var actualPixels = Pixels(actual.Render, context);
        SaveScreenshotIfRequested($"t1-2-{pseudo}-{lang}-{transform}.png", actualPixels);
        Assert.NotEqual(Pixels(_ => { }, context), expected);
        Assert.Equal(expected, actualPixels);
        Assert.Equal(source, actual.Text);
        actual.SelectAll(); Assert.Equal(source, actual.SelectedText); actual.ClearSelection();
    }

    [Theory]
    [InlineData("standalone", "inter-character")]
    [InlineData("standalone", "inter-word")]
    [InlineData("runs", "inter-character")]
    [InlineData("runs", "inter-word")]
    [InlineData("shared", "inter-character")]
    [InlineData("shared", "inter-word")]
    public void OrdinaryLtrPreservedTabsAndJustificationExpandMappedSuffixWhileKeepingTabRulerAndSourceCarets(string host, string justifyMethod)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source = "aß\tﬃb cßd", display = "ASS\tFFIB CSSD";
        const int width = 320;
        var actual = BuildTabbed(source, "uppercase", true, out var actualTexts);
        var unexpanded = BuildTabbed(source, "uppercase", false, out var unexpandedTexts);
        var reference = BuildTabbed(display, "none", true, out _);

        var expected = Pixels(reference.Render, context);
        var actualPixels = Pixels(actual.Render, context);
        SaveScreenshotIfRequested($"t1-3-tabs-justify-{host}-{justifyMethod}.png", actualPixels);
        Assert.NotEqual(Pixels(_ => { }, context), expected);
        Assert.Equal(expected, actualPixels);

        // Tab ruler before and at the last tab must not shift under justification.
        var postTabTarget = actualTexts[host == "shared" ? 1 : 0];
        var postTabUnexpanded = unexpandedTexts[host == "shared" ? 1 : 0];
        int postTabIndex = host == "shared" ? 0 : 3;
        double actualPostTabX = (ReferenceEquals(postTabTarget, actual) ? 0 : postTabTarget.TransformToAncestor(actual).X)
            + Assert.Single(postTabTarget.GetSourceRangeSegments(postTabIndex, 1)).StartX;
        double unexpandedPostTabX = (ReferenceEquals(postTabUnexpanded, unexpanded) ? 0 : postTabUnexpanded.TransformToAncestor(unexpanded).X)
            + Assert.Single(postTabUnexpanded.GetSourceRangeSegments(postTabIndex, 1)).StartX;
        Near(unexpandedPostTabX, actualPostTabX);

        // Suffix after the last tab must expand to the right content edge (width - 8).
        var lastText = actualTexts[^1];
        double rightEdge = (ReferenceEquals(lastText, actual) ? 0 : lastText.TransformToAncestor(actual).X)
            + lastText.GetSourceRangeSegments(0, lastText.Text.Length).Max(r => Math.Max(r.StartX, r.EndX));
        Near(width - 8, rightEdge);

        foreach (var text in actualTexts)
        {
            var boundaries = GraphemeClusters.GetBoundaries(text.Text);
            foreach (var rect in text.GetSourceRangeSegments(0, text.Text.Length).Where(r => Math.Abs(r.EndX - r.StartX) > .01))
            {
                var pos = Assert.IsType<TextPointer>(text.GetPositionFromPoint(new((rect.StartX + rect.EndX) / 2, rect.Baseline - 2), false));
                Assert.Contains(text.ContentStart.GetOffsetToPosition(pos), boundaries);
            }
            text.SelectAll(); Assert.Equal(text.Text, text.SelectedText); text.ClearSelection();
        }

        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); actual.Render(recorder); var frame = recorder.Commit();
        Assert.True(frame.IsFullyRecordable);
        Assert.Equal(expected, Pixels(dc => DrawingReplayer.Replay(frame, dc), context, worker: true));

        FrameworkElement BuildTabbed(string value, string transform, bool justify, out TextBlock[] texts)
        {
            int tabPos = value.IndexOf('\t');
            string beforeAndTab = value[..(tabPos + 1)], afterTab = value[(tabPos + 1)..];
            FrameworkElement root;
            if (host == "shared")
            {
                var panel = new StackPanel(); root = panel;
                texts = new[] { beforeAndTab, afterTab }.Select(s => new TextBlock { Text = s, IsTextSelectionEnabled = true }).ToArray();
                foreach (var t in texts) { panel.Children.Add(t); Css.SetStyle(t, "display:inline"); }
            }
            else
            {
                var text = new TextBlock { IsTextSelectionEnabled = true }; root = text; texts = [text];
                if (host == "runs")
                {
                    text.Inlines.Add(new Run(beforeAndTab));
                    var span = new Span(); span.Inlines.Add(new Run(afterTab));
                    text.Inlines.Add(span);
                }
                else text.Text = value;
            }
            string align = justify ? $"text-align:justify;text-align-last:justify;text-justify:{justifyMethod}" : "text-align:left";
            Css.SetStyle(root, Style(transform, false, 0, host == "shared") + $";width:{width}px;tab-size:4;{align}");
            Layout(root, width);
            return root;
        }
    }

    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, .001);
    private static string Style(string transform, bool rtl, int letter, bool shared)
        => (shared ? "display:block;" : "") + "width:320px;height:180px;box-sizing:border-box;padding:8px;font-family:DejaVu Sans;font-size:24px;line-height:40px;white-space:pre-wrap;color:#d83253;text-align:start;direction:" +
            (rtl ? "rtl" : "ltr") + ";text-transform:" + transform + ";letter-spacing:" + letter.ToString(CultureInfo.InvariantCulture) + "px;word-spacing:3px";
    private static void Layout(FrameworkElement root, int width = 320)
    { for (int i = 0; i < 5; i++) { CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); Dispatcher.GetForCurrentThread().ProcessQueue(); root.Measure(new(width, 180)); root.Arrange(new(0, 0, width, 180)); root.UpdateLayout(); } }
    private static byte[] Pixels(Action<DrawingContext> paint, RenderContext context, bool worker = false)
    {
        using var target = context.CreateOffscreenRenderTarget(336, 180); target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
        Exception? failure = null;
        void Draw()
        {
            try { using var dc = new RenderTargetDrawingContext(target, context); paint(dc); dc.Close(); }
            catch (Exception ex) { failure = ex; }
        }
        try { if (worker) { var thread = new Thread(Draw) { IsBackground = true }; thread.Start(); thread.Join(); } else Draw(); }
        finally { target.EndDraw(); }
        if (failure is not null) throw failure;
        var pixels = new byte[336 * 180 * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 336 * 4, out _, out _)); return pixels;
    }
    private static void SaveScreenshotIfRequested(string fileName, byte[] pixels)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_T1_SCREENSHOT_DIR") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(336, 180, 96, 96, PixelFormats.Bgra32, null, pixels, 336 * 4)));
        using var stream = File.Create(Path.Combine(dir, fileName));
        encoder.Save(stream);
    }
}
