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
public sealed class CssComplexShapingFontCombinationsTests : IDisposable
{
    private readonly CssNativeFontResource _selectable = Load(MacOSTestAssets.PathTo("FontFeaturePriority/selectable-variants.otf"));
    private readonly CssNativeFontResource _fancy = Load(MacOSTestAssets.PathTo("FontNumeric/FontWithFancyFeatures.otf"));
    private readonly CssNativeFontResource _noPetite = Load(MacOSTestAssets.PathTo("FontCapsFallback/no-petite.otf"));
    private readonly CssNativeFontResource _primary = Load(CssFontSizeAdjustTests.Fixture("Primary"));
    private readonly CssNativeFontResource _secondary = Load(CssFontSizeAdjustTests.Fixture("Secondary"));
    private readonly CssNativeFontResource _variable = Load(CssFontVariationSettingsTests.Fixture);

    private static CssNativeFontResource Load(string path)
        => CssNativeFontResource.Create(File.ReadAllBytes(path)) ?? throw new InvalidOperationException("Failed to load font fixture: " + path);

    public void Dispose()
    {
        _variable.Dispose();
        _secondary.Dispose();
        _primary.Dispose();
        _noPetite.Dispose();
        _fancy.Dispose();
        _selectable.Dispose();
    }

    public static IEnumerable<object[]> BidiCombinationCases()
    {
        foreach (string host in new[] { "standalone", "runs", "inline-box", "shared" })
            foreach (bool rtl in new[] { false, true })
                foreach (string mode in new[] { "isolate", "bidi-override", "plaintext" })
                    yield return [host, rtl, mode];
    }

    [Theory]
    [MemberData(nameof(BidiCombinationCases))]
    public void BidiParagraphPreservesNativePositionCapsAndWidthAcrossRunsInlineBoxesAndSharedSources(
        string host, bool rtl, string mode)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string prefix = "אב ", middle = "x", suffix = " a";
        string familyList = $"'{_selectable.Family}', 'DejaVu Sans'";
        string baseStyle = $"display:block;width:280px;height:120px;box-sizing:border-box;padding:8px;font-family:{familyList};font-size:32px;line-height:48px;white-space:nowrap;direction:{(rtl ? "rtl" : "ltr")};unicode-bidi:{mode};font-width:83.2%;font-style:oblique 14deg;letter-spacing:2px;word-spacing:3px;text-autospace:no-autospace;color:#1d3557";
        string midStyle = $"unicode-bidi:{mode};font-variant-position:super";
        string endStyle = $"unicode-bidi:{mode};font-variant-caps:small-caps";

        FrameworkElement root;
        TextBlock[] blocks;
        if (host == "shared")
        {
            var panel = new StackPanel();
            var a = new TextBlock { Text = prefix, IsTextSelectionEnabled = true };
            var b = new TextBlock { Text = middle, IsTextSelectionEnabled = true };
            var c = new TextBlock { Text = suffix, IsTextSelectionEnabled = true };
            panel.Children.Add(a);
            panel.Children.Add(b);
            panel.Children.Add(c);
            Css.SetStyle(panel, baseStyle);
            Css.SetStyle(a, "display:inline");
            Css.SetStyle(b, "display:inline;" + midStyle);
            Css.SetStyle(c, "display:inline;" + endStyle);
            root = panel;
            blocks = [a, b, c];
        }
        else if (host == "inline-box")
        {
            var text = new TextBlock { IsTextSelectionEnabled = true };
            var runA = new Run(prefix);
            var spanB = new Span(new Run(middle));
            var spanC = new Span(new Run(suffix));
            text.Inlines.Add(runA);
            text.Inlines.Add(spanB);
            text.Inlines.Add(spanC);
            Css.SetStyle(text, baseStyle);
            Css.SetStyle(spanB, midStyle + ";padding-left:4px;padding-right:4px;border-left:1px solid #e63946;border-right:1px solid #2a9d8f");
            Css.SetStyle(spanC, endStyle);
            root = text;
            blocks = [text];
        }
        else if (host == "runs")
        {
            var text = new TextBlock { IsTextSelectionEnabled = true };
            var runA = new Run(prefix);
            var runB = new Run(middle);
            var runC = new Run(suffix);
            text.Inlines.Add(runA);
            text.Inlines.Add(runB);
            text.Inlines.Add(runC);
            Css.SetStyle(text, baseStyle);
            Css.SetStyle(runB, midStyle);
            Css.SetStyle(runC, endStyle);
            root = text;
            blocks = [text];
        }
        else
        {
            var text = new TextBlock { Text = middle, IsTextSelectionEnabled = true };
            Css.SetStyle(text, baseStyle + ";" + midStyle);
            root = text;
            blocks = [text];
        }

        Layout(root, 280, 120);

        // Verify that format controls inserted by unicode-bidi do not force synthetic position fallback (mode == 2).
        // In selectable-variants.otf, 'x' with 'sups' 1 and 'a' with 'smcp' 1 both map to 'b' (512/1024 = 0.5em = 16px at 32px),
        // whereas synthetic superscript fallback would scale 'x' (32px) by 0.58 -> 18.56px, and unshaped 'a' would be 32px.
        using var refFormatX = context.CreateTextFormat(_selectable.Family, 32, 400, 0, 83.2f);
        Assert.Equal(JaliumResult.Ok, refFormatX.SetFontFeatures([new(FontFeature.TagOf("sups"), 1)]));
        double expectedXAdvance = refFormatX.MeasureText("x", 10000, 1000).Width;
        Assert.Equal(16.0, expectedXAdvance, 2);

        using var refFormatA = context.CreateTextFormat(_selectable.Family, 32, 400, 0, 83.2f);
        Assert.Equal(JaliumResult.Ok, refFormatA.SetFontFeatures([new(FontFeature.TagOf("smcp"), 1)]));
        double expectedAAdvance = refFormatA.MeasureText("a", 10000, 1000).Width;
        Assert.Equal(16.0, expectedAAdvance, 2);

        var xSegments = host is "shared" or "standalone"
            ? blocks[host == "shared" ? 1 : 0].GetSourceRangeSegments(0, 1)
            : blocks[0].GetSourceRangeSegments(prefix.Length, 1);
        Assert.Single(xSegments);
        double actualXWidth = Math.Abs(xSegments[0].EndX - xSegments[0].StartX);
        Assert.InRange(actualXWidth, expectedXAdvance - 0.1, expectedXAdvance + 2.1);

        if (host != "standalone")
        {
            var aSegments = host == "shared"
                ? blocks[2].GetSourceRangeSegments(1, 1)
                : blocks[0].GetSourceRangeSegments(prefix.Length + middle.Length + 1, 1);
            Assert.Single(aSegments);
            double actualAWidth = Math.Abs(aSegments[0].EndX - aSegments[0].StartX);
            Assert.InRange(actualAWidth, expectedAAdvance - 0.1, expectedAAdvance + 2.1);
        }

        // Verify hit-testing, selection, and source preservation across all blocks.
        foreach (var block in blocks)
        {
            for (int i = 0; i < block.Text.Length; i++)
            {
                var segs = block.GetSourceRangeSegments(i, 1);
                Assert.NotEmpty(segs);
                foreach (var seg in segs.Where(s => Math.Abs(s.EndX - s.StartX) > 0.5))
                {
                    var hit = Assert.IsType<TextPointer>(block.GetPositionFromPoint(new((seg.StartX + seg.EndX) / 2, seg.Baseline - 2), false));
                    int offset = block.ContentStart.GetOffsetToPosition(hit);
                    Assert.InRange(offset, i, i + 1);
                }
            }
            string before = block.Text;
            block.SelectAll();
            Assert.Equal(before, block.SelectedText);
            Assert.Equal(before, block.Text);
            block.ClearSelection();
        }

        // Verify worker-thread / whole-frame DrawingRecorder & DrawingReplayer fidelity.
        var directPixels = Pixels(root.Render, context, 280, 120);
        var recorder = new DrawingRecorder();
        recorder.BindWholeFrame();
        root.Render(recorder);
        var recorded = recorder.Commit();
        Assert.True(recorded.IsFullyRecordable);

        byte[] replayedPixels = null!;
        var worker = new Thread(() =>
        {
            using var workerContext = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
            replayedPixels = Pixels(dc => DrawingReplayer.Replay(recorded, dc), workerContext, 280, 120);
        });
        worker.Start();
        worker.Join();
        Assert.Equal(directPixels, replayedPixels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SynthesizedSmallCapsAndPositionComposeWithBidiDisplayMapAndFontFaceMetrics(bool rtl)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        // Primary.ttf has 'x' and '0' (advance 600/1000 = 0.6em), lacks 'A' and 'X'.
        // Secondary.ttf has 'A', 'X', 'x', '0' at 0.5 scale (advance 300/1000 = 0.3em).
        // With size-adjust: 150% on Primary and size-adjust: 200% on Secondary:
        // - Primary at 32px -> used size 48px (advance 0.6 * 48 = 28.8px)
        // - Secondary at 32px -> used size 64px (advance 0.3 * 64 = 19.2px)
        // With font-variant-caps: small-caps on Secondary ('xA'):
        // - 'x' is lowercase and Secondary has no 'smcp' table, so 'x' is synthesized to 'X' at 0.8 * 64 = 51.2px (advance 0.3 * 51.2 = 15.36px).
        // - 'A' is uppercase, remains at 64px (advance 0.3 * 64 = 19.2px).
        var plan = new CssFontRenderingPlan(0, 0,
        [
            new(_primary.Family, 400, [new('0', '0')], false, false) { Metrics = new(1.5, 1.0, 0.5, 0.2) },
            new(_secondary.Family, 400, [new(0, 0x10ffff)], false, false) { Metrics = new(2.0, 0.8, 0.4, 0.1) }
        ]).Encode();

        var box = new TextBlock { Text = "0xA", FontFamily = plan, FontSize = 32, IsTextSelectionEnabled = true };
        Css.SetStyle(box, $"display:block;width:240px;height:100px;padding:6px;direction:{(rtl ? "rtl" : "ltr")};unicode-bidi:isolate;font-variant-caps:small-caps;font-width:87.5%;text-autospace:no-autospace");
        Layout(box, 240, 100);

        var seg0 = Assert.Single(box.GetSourceRangeSegments(0, 1));
        var segX = Assert.Single(box.GetSourceRangeSegments(1, 1));
        var segA = Assert.Single(box.GetSourceRangeSegments(2, 1));

        Assert.Equal(28.8, Math.Abs(seg0.EndX - seg0.StartX), 2);
        Assert.Equal(15.36, Math.Abs(segX.EndX - segX.StartX), 2);
        Assert.Equal(19.2, Math.Abs(segA.EndX - segA.StartX), 2);
        Assert.Equal(seg0.Baseline, segX.Baseline, 3);
        Assert.Equal(segX.Baseline, segA.Baseline, 3);

        box.SelectAll();
        Assert.Equal("0xA", box.SelectedText);
        Assert.Equal("0xA", box.Text);
    }

    [Theory]
    [InlineData("inline-box")]
    [InlineData("first-line")]
    [InlineData("flow-document")]
    public void InlineBoxesFirstLineAndDocumentViewsPreserveStretchObliqueOpticalSizingAndFontFaceScope(string mode)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        string sheet = "@font-face{font-family:DocVarFace;src:local('SF Pro');font-width:50% 200%;size-adjust:125%;ascent-override:90%;descent-override:30%;line-gap-override:10%}";
        const string sample = "MMMMMM";

        using var expectedNarrow = context.CreateTextFormat("SF Pro", 32, 400, FontStyles.Oblique.ToOpenTypeStyle(), 83.2f);
        Assert.True(expectedNarrow.TrySetFontFaceMetrics(new(1.25, 0.9, 0.3, 0.1)));
        Assert.True(expectedNarrow.TrySetFontOpticalVariations([new(FontFeature.TagOf("wdth"), 75)], 1));
        double widthNarrow = expectedNarrow.MeasureText(sample, 10000, 1000).Width;

        using var expectedWide = context.CreateTextFormat("SF Pro", 32, 400, FontStyles.Oblique.ToOpenTypeStyle(), 83.2f);
        Assert.True(expectedWide.TrySetFontFaceMetrics(new(1.25, 0.9, 0.3, 0.1)));
        Assert.True(expectedWide.TrySetFontOpticalVariations([new(FontFeature.TagOf("wdth"), 125)], 2));
        double widthWide = expectedWide.MeasureText(sample, 10000, 1000).Width;
        Assert.True(widthWide > widthNarrow + 10.0);

        if (mode == "flow-document")
        {
            var run = new Run(sample);
            var span = new Span(run);
            var paragraph = new Paragraph(span);
            var doc = new FlowDocument(paragraph) { PagePadding = new(0) };
            using var presentation = new FlowDocumentPresentation();
            presentation.SetDocument(doc);
            Css.SetStyleSheet(doc, sheet);

            foreach (var (optical, wdth) in new[] { ("auto", 75), ("none", 125), ("auto", 75) })
            {
                Css.SetStyle(doc, $"font-family:DocVarFace;font-size:32px;font-width:83.2%;font-style:oblique 15deg;font-optical-sizing:{optical};font-variation-settings:'wdth' {wdth};text-autospace:no-autospace");
                WaitForFonts(doc, presentation, 400, 120);

                Assert.Equal(83.2, CssFontStretchValue.Computed(CssNode.Get(run)), 5);
                var computedStyle = CssFontStyleValue.Computed(CssNode.Get(run));
                Assert.Equal(FontStyles.Oblique, computedStyle.Native);
                Assert.Equal(15.0, computedStyle.AngleDegrees ?? 0, 5);
                Assert.Same(span, run.Parent);
                Assert.Equal(sample + "\n", doc.GetText());
            }
            return;
        }

        var block = new TextBlock { IsTextSelectionEnabled = true };
        var innerRun = new Run(sample);
        var innerSpan = new Span(innerRun);
        block.Inlines.Add(innerSpan);
        string pseudo = mode == "first-line" ? "TextBlock::first-line{color:#2a9d8f}" : "";
        Css.SetStyleSheet(block, sheet + pseudo);
        string spanExtra = mode == "inline-box" ? "padding-left:6px;border-left:2px solid #e63946;" : "";

        foreach (var (optical, wdth) in new[] { ("auto", 75), ("none", 125), ("auto", 75) })
        {
            Css.SetStyle(block, $"display:block;width:400px;font-family:DocVarFace;font-size:32px;font-width:83.2%;font-style:oblique 15deg;font-optical-sizing:{optical};font-variation-settings:'wdth' {wdth};text-autospace:no-autospace");
            Css.SetStyle(innerSpan, spanExtra + $"font-width:83.2%;font-style:oblique 15deg;font-optical-sizing:{optical};font-variation-settings:'wdth' {wdth}");
            WaitForFonts(block, block, 400, 120);

            Assert.True(block.HasCssInlineBoxes);
            var segs = block.GetSourceRangeSegments(0, sample.Length);
            Assert.NotEmpty(segs);
            double actualWidth = segs.Sum(s => Math.Abs(s.EndX - s.StartX));
            Assert.Equal(wdth == 75 ? widthNarrow : widthWide, actualWidth, 2);

            block.SelectAll();
            Assert.Equal(sample, block.SelectedText);
            Assert.Equal(sample, innerRun.Text);
            Assert.Same(innerSpan, innerRun.Parent);
        }
    }

    [Theory]
    [InlineData(false, 1.0)]
    [InlineData(true, 1.0)]
    [InlineData(false, 2.0)]
    [InlineData(true, 2.0)]
    public void ComplexScriptsCursiveJoiningLigaturesCombiningMarksAndDynamicEditsMaintainInvariantsAcrossDpi(
        bool rtl, double dpi)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        // Combine Arabic cursive joining ("مرحبا"), Latin ligature ("ffi"), combining mark cluster ("a\u0301"),
        // German sharp-s ("ß"), and Hebrew ("אב") across multiple styled Runs with non-normal font-width and small-caps.
        var block = new TextBlock { IsTextSelectionEnabled = true };
        block.SetRootDpi(new DpiScale(dpi, dpi));
        var runArabic1 = new Run("مر");
        var runArabic2 = new Run("حبا ");
        var runLigature = new Run("ffi ");
        var runCombining = new Run("a\u0301ß ");
        var runHebrew = new Run("אב");
        block.Inlines.Add(runArabic1);
        block.Inlines.Add(runArabic2);
        block.Inlines.Add(runLigature);
        block.Inlines.Add(runCombining);
        block.Inlines.Add(runHebrew);

        string style = $"display:block;width:360px;height:140px;padding:8px;font-family:Seravek, Arial;font-size:26px;line-height:40px;direction:{(rtl ? "rtl" : "ltr")};unicode-bidi:isolate;font-width:87.5%;font-kerning:normal;font-variant-ligatures:common-ligatures;letter-spacing:1.5px;word-spacing:2.5px;text-autospace:normal";
        Css.SetStyle(block, style);
        Css.SetStyle(runCombining, "font-variant-caps:small-caps;font-variant-position:super");
        Layout(block, 360, 140);

        string expectedText = "مرحبا ffi a\u0301ß אב";
        Assert.Equal(expectedText, block.Text);

        // Verify Arabic cursive joining across runArabic1 ("مر") and runArabic2 ("حبا ") is not broken by letter-spacing:
        // Every grapheme cluster must map back to valid source range segments and hit-test to cluster boundaries.
        var boundaries = GraphemeClusters.GetBoundaries(expectedText);
        double baseline = block.GetSourceRangeSegments(0, 1)[0].Baseline;
        for (int i = 0; i + 1 < boundaries.Length; i++)
        {
            int start = boundaries[i], end = boundaries[i + 1];
            var segments = block.GetSourceRangeSegments(start, end - start);
            Assert.NotEmpty(segments);
            Assert.All(segments, s => Assert.Equal(baseline, s.Baseline, 3));
            foreach (var seg in segments.Where(s => Math.Abs(s.EndX - s.StartX) > 0.5))
            {
                var hit = Assert.IsType<TextPointer>(block.GetPositionFromPoint(new((seg.StartX + seg.EndX) / 2, seg.Baseline - 2), false));
                int index = block.ContentStart.GetOffsetToPosition(hit);
                Assert.InRange(index, start, end);
            }
        }

        // Verify dynamic text insertion and deletion update layout and restore exact pixels when reverted.
        var beforePixels = Pixels(block.Render, context, 360, 140);
        runCombining.Text = "a\u0301ßXYZ ";
        Layout(block, 360, 140);
        var mutatedPixels = Pixels(block.Render, context, 360, 140);
        Assert.NotEqual(beforePixels, mutatedPixels);
        block.SelectAll();
        Assert.Equal("مرحبا ffi a\u0301ßXYZ אב", block.SelectedText);
        block.ClearSelection();

        runCombining.Text = "a\u0301ß ";
        Layout(block, 360, 140);
        var restoredPixels = Pixels(block.Render, context, 360, 140);
        Assert.Equal(beforePixels, restoredPixels);
        block.SelectAll();
        Assert.Equal(expectedText, block.SelectedText);
        block.ClearSelection();
        Assert.Same(runArabic1, block.Inlines[0]);
        Assert.Same(runCombining, block.Inlines[3]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisplayMapTextTransformAndAutoHyphenationComposeWithCapsPositionAndStretch(bool rtl)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var block = new TextBlock
        {
            Text = "Zucker aßb",
            Language = XmlLanguage.GetLanguage("de-1901"),
            IsTextSelectionEnabled = true
        };
        Css.SetStyle(block, $"display:block;width:92px;height:160px;padding:6px;font-family:DejaVu Sans;font-size:20px;line-height:30px;direction:{(rtl ? "rtl" : "ltr")};unicode-bidi:isolate;font-width:85%;font-variant-caps:small-caps;hyphens:auto;hyphenate-character:'-';hyphenate-limit-chars:4 2 2;letter-spacing:1px");
        Layout(block, 92, 160);

        // "Zucker" under de-1901 hyphenates as "Zuk-\nker", and "aßb" on the next line has synthesized small-caps 'ß' -> "SS".
        var lines = block.GetDocumentLines(92);
        Assert.True(lines.Count >= 2, "Expected 'Zucker aßb' to wrap across at least 2 lines.");

        var boundaries = GraphemeClusters.GetBoundaries(block.Text);
        for (int i = 0; i + 1 < boundaries.Length; i++)
        {
            int a = boundaries[i], b = boundaries[i + 1];
            if (char.IsWhiteSpace(block.Text[a])) continue;
            var segs = block.GetSourceRangeSegments(a, b - a);
            Assert.NotEmpty(segs);
        }
        block.SelectAll();
        Assert.Equal("Zucker aßb", block.SelectedText);
        Assert.Equal("Zucker aßb", block.Text);
    }

    [Fact]
    public void ExportT3AcceptanceScreenshotsWhenRequested()
    {
        var dir = Environment.GetEnvironmentVariable("JALIUM_CSS_ACCEPTANCE_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);

        // Panel 1: Bidi + Caps + Position + Width + @font-face Metric Overrides
        var panel1 = new StackPanel();
        Css.SetStyle(panel1, "display:block;width:540px;height:355px;padding:12px;background-color:#f8f9fa;box-sizing:border-box");
        (string Title, string TextA, string StyleA, string TextB, string StyleB, string TextC, string StyleC, string RowStyle)[] rows =
        [
            ("1. RTL Isolate + Native sups/smcp + 83.2% Width",
             "אב ", "",
             "123", "font-variant-position:super;color:#d62828",
             " Aaß", "font-variant-caps:small-caps;color:#2a9d8f",
             "direction:rtl;unicode-bidi:isolate;font-family:Seravek;font-size:22px;line-height:32px;font-width:83.2%;letter-spacing:1.5px"),
            ("2. Synthesized Small-Caps (ß→SS) + Bidi Override",
             "AB ", "",
             "aßi", "font-variant-caps:small-caps;color:#d62828",
             " אב", "color:#1d3557",
             "direction:ltr;unicode-bidi:bidi-override;font-family:Arial;font-size:22px;line-height:32px;font-width:88%;letter-spacing:1px"),
            ("3. Arabic Cursive Joining + Ligatures + Oblique 14deg",
             "مر", "color:#d62828",
             "حبا ", "color:#2a9d8f",
             "ffi a\u0301", "font-variant-ligatures:common-ligatures;color:#1d3557",
             "direction:rtl;unicode-bidi:isolate;font-family:Seravek,Geeza Pro;font-size:22px;line-height:32px;font-style:oblique 14deg;letter-spacing:1px"),
            ("4. de-1901 Auto-Hyphenation (Zuk-ker) + Small-Caps + 85% Width",
             "Zucker ", "color:#d62828",
             "aßb ", "font-variant-caps:small-caps;color:#2a9d8f",
             "x\u00ADy", "font-variant-position:super;color:#1d3557",
             "width:52px;direction:ltr;font-family:DejaVu Sans;font-size:20px;line-height:26px;font-width:85%;hyphens:auto;hyphenate-character:'-';hyphenate-limit-chars:4 2 2")
        ];
        foreach (var row in rows)
        {
            var card = new StackPanel { Language = XmlLanguage.GetLanguage("de-1901") };
            Css.SetStyle(card, "display:block;margin-bottom:8px;padding:6px 10px;background-color:#ffffff;border:1px solid #dee2e6");
            var title = new TextBlock { Text = row.Title };
            Css.SetStyle(title, "display:block;font-family:DejaVu Sans;font-size:11px;line-height:15px;color:#6c757d;margin-bottom:2px");
            var content = new TextBlock { Language = XmlLanguage.GetLanguage("de-1901") };
            var rA = new Run(row.TextA);
            var rB = new Run(row.TextB);
            var rC = new Run(row.TextC);
            content.Inlines.Add(rA);
            content.Inlines.Add(rB);
            content.Inlines.Add(rC);
            Css.SetStyle(content, "display:block;color:#1d3557;" + row.RowStyle);
            if (!string.IsNullOrEmpty(row.StyleA)) Css.SetStyle(rA, row.StyleA);
            if (!string.IsNullOrEmpty(row.StyleB)) Css.SetStyle(rB, row.StyleB);
            if (!string.IsNullOrEmpty(row.StyleC)) Css.SetStyle(rC, row.StyleC);
            card.Children.Add(title);
            card.Children.Add(content);
            panel1.Children.Add(card);
        }
        Layout(panel1, 540, 355);
        SavePng(Path.Combine(dir, "t3-bidi-caps-position-width-and-metrics.png"), Pixels(panel1.Render, context, 540, 355), 540, 355);

        // Panel 2: Inline Boxes + Optical Sizing + @font-face Metric Overrides + Shared Sources
        var panel2 = new StackPanel();
        Css.SetStyle(panel2, "display:block;width:540px;height:260px;padding:12px;background-color:#f8f9fa;box-sizing:border-box");

        var boxCard1 = new StackPanel();
        Css.SetStyle(boxCard1, "display:block;margin-bottom:10px;padding:8px 10px;background-color:#ffffff;border:1px solid #dee2e6");
        var boxHeader1 = new TextBlock { Text = "Inline Boxes (Border/Padding) + Bidi + 83.2% Width + Super/Small-Caps" };
        Css.SetStyle(boxHeader1, "display:block;font-family:DejaVu Sans;font-size:11px;line-height:15px;color:#6c757d;margin-bottom:4px");
        var inlineBoxText = new TextBlock();
        var ibRun1 = new Run("אב ");
        var ibSpan2 = new Span(new Run("123"));
        var ibSpan3 = new Span(new Run(" Aaß"));
        inlineBoxText.Inlines.Add(ibRun1);
        inlineBoxText.Inlines.Add(ibSpan2);
        inlineBoxText.Inlines.Add(ibSpan3);
        Css.SetStyle(inlineBoxText, "display:block;font-family:Seravek;font-size:24px;line-height:36px;direction:rtl;unicode-bidi:isolate;font-width:83.2%;color:#1d3557");
        Css.SetStyle(ibSpan2, "font-variant-position:super;padding:0 6px;border-left:2px solid #d62828;border-right:2px solid #2a9d8f;background-color:#fff3cd;color:#d62828");
        Css.SetStyle(ibSpan3, "font-variant-caps:small-caps;padding:0 4px;border-bottom:2px solid #1d3557;color:#2a9d8f");
        boxCard1.Children.Add(boxHeader1);
        boxCard1.Children.Add(inlineBoxText);
        panel2.Children.Add(boxCard1);

        var boxCard2 = new StackPanel();
        Css.SetStyle(boxCard2, "display:block;padding:8px 10px;background-color:#ffffff;border:1px solid #dee2e6");
        var boxHeader2 = new TextBlock { Text = "Shared Inline Siblings + @font-face Metric Overrides (Primary 150% / Secondary 200%)" };
        Css.SetStyle(boxHeader2, "display:block;font-family:DejaVu Sans;font-size:11px;line-height:15px;color:#6c757d;margin-bottom:4px");
        string plan = new CssFontRenderingPlan(0, 0,
        [
            new(_primary.Family, 400, [new('0', '0')], false, false) { Metrics = new(1.5, 1.0, 0.5, 0.2) },
            new(_secondary.Family, 400, [new(0, 0x10ffff)], false, false) { Metrics = new(2.0, 0.8, 0.4, 0.1) }
        ]).Encode();
        var sharedText = new TextBlock { Text = "0xA0xA", FontFamily = plan, FontSize = 28 };
        Css.SetStyle(sharedText, "display:block;font-variant-caps:small-caps;line-height:44px;letter-spacing:2px;color:#1d3557");
        boxCard2.Children.Add(boxHeader2);
        boxCard2.Children.Add(sharedText);
        panel2.Children.Add(boxCard2);

        Layout(panel2, 540, 260);
        SavePng(Path.Combine(dir, "t3-inline-boxes-complex-scripts-and-document-view.png"), Pixels(panel2.Render, context, 540, 260), 540, 260);
    }

    private static void WaitForFonts(DependencyObject fontOwner, FrameworkElement layoutRoot, double width, double height)
    {
        Evaluate(fontOwner);
        Layout(layoutRoot, width, height);
#pragma warning disable xUnit1031
        Css.WaitForFontsAsync(fontOwner).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        CssFontFaces.FlushNotifications(fontOwner.Dispatcher);
        Evaluate(fontOwner);
        Layout(layoutRoot, width, height);
        Assert.Empty(Css.GetFontLoadErrors(fontOwner));
    }

    private static void Evaluate(DependencyObject owner)
    {
        CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner));
        CssEvaluationScheduler.FlushIfPending(owner.Dispatcher);
    }

    private static void Layout(FrameworkElement root, double width, double height)
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

    private static byte[] Pixels(Action<DrawingContext> paint, RenderContext context, int width, int height)
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

    private static void SavePng(string path, byte[] bgra, int width, int height)
    {
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
