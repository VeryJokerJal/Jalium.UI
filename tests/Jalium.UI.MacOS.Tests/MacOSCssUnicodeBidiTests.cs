using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSCssUnicodeBidiTests
{
    [Theory]
    [InlineData("isolate", "AאבB", "AבאB", 2, 1)]
    [InlineData("plaintext", "AאבB", "AבאB", 2, 1)]
    [InlineData("bidi-override", "ACSSB", "ASSCB", 4, 4)]
    [InlineData("isolate-override", "ACSSB", "ASSCB", 4, 4)]
    public void InlineScopePixelsAndPointHitsUseTheSameSourceCluster(string scope, string source, string visual, int index, int visualIndex)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var actual = Make(""); var span = new Span(); span.Inlines.Add(new Run(source[1..^1]));
        actual.Inlines.Add(new Run(source[..1])); actual.Inlines.Add(span); actual.Inlines.Add(new Run(source[^1..]));
        Css.SetStyle(span, "unicode-bidi:" + scope + ";direction:rtl"); Layout(actual);
        var reference = Make("\u202D" + visual + "\u202C");
        Assert.Equal(Pixels(reference, context), Pixels(actual, context));
        var bounds = actual.DocumentTextBounds(index, 1); Assert.NotEmpty(bounds);
        var expected = reference.DocumentTextBounds(visualIndex + 1, 1); Assert.NotEmpty(expected);
        Assert.Equal(expected.Min(r => r.Left), bounds.Min(r => r.Left), 3);
        Assert.Equal(expected.Max(r => r.Right), bounds.Max(r => r.Right), 3);
        var rect = bounds[0];
        foreach (double fraction in new[] { .2, .8 })
        {
            int hit = actual.DocumentCharacterFromPoint(new(rect.Left + rect.Width * fraction, rect.Top + rect.Height / 2));
            Assert.True(hit == index || hit == index + 1, $"Source {index} hit returned {hit}.");
        }
        actual.SelectAll(); Assert.Equal(source, actual.SelectedText); Assert.Equal(source, actual.Text);
    }

    [Theory]
    [InlineData(100)] [InlineData(130)]
    public void PlaintextContinuationRetainsTheFirstStrongParagraphDirection(int width)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source = "אב CD EF אב";
        var actual = Make(source, width, true); Css.SetStyle(actual, "unicode-bidi:plaintext;direction:ltr"); Layout(actual, width);
        var lines = actual.GetDocumentLines(width); Assert.True(lines.Count > 1);
        var oracle = new Dictionary<string, string> { ["אב "] = " בא", ["CD EF "] = " CD EF", ["אב"] = "בא", ["אב CD "] = " CD בא", ["EF אב"] = "בא EF" };
        // The resolved paragraph is RTL even though its author's direction DP
        // stays LTR. Soft-row trailing space hangs to the physical left; an
        // ordinary LTR TextBlock with a leading space would count that space.
        string family=actual.FontFamily.GetRenderingSource(actual);var brush=(SolidColorBrush)actual.Foreground!;
        double baseline=actual.GetFirstBaselineOffset(200)-8;
        var paragraphs=lines.Select(line=>
        {
            string text="\u202D"+oracle[source[line.Start..line.End]]+"\u202C";
            return NativeTextParagraph.TryCreate([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,FlowDirection.LeftToRight,noWrap:true,textOptionsOwner:actual);
        }).ToArray();
        try
        {
            Assert.All(paragraphs,Assert.NotNull);
            var expected=Pixels(dc=>
            {
                for(int row=0;row<lines.Count;row++)
                {
                    var line=lines[row];var paragraph=paragraphs[row]!;var native=Assert.Single(paragraph.Lines);
                    double hanging=source[line.End-1]==' '?paragraph.Selection(0,1,1).Sum(rect=>(double)rect.Width):0;
                    dc.DrawText(new FormattedText(paragraph.Text,family,28){Foreground=brush,PlatformTextLine=native.ForSource(1,paragraph.Text.Length-2,brush.Color,brush.Opacity)},new(8-hanging-native.Metrics.Line.X,line.Top+baseline-native.Metrics.Baseline));
                }
            },context);
            Assert.Equal(expected,Pixels(actual,context));
        }
        finally{foreach(var paragraph in paragraphs)paragraph?.Dispose();}
        Assert.Equal(FlowDirection.LeftToRight, actual.FlowDirection);
        actual.SelectAll(); Assert.Equal(source, actual.SelectedText);
    }

    [Fact]
    public void RtlCjkParagraphExposesSourceGeometryOnEveryHardLine()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source = "甲\n乙\n丙\n丁";
        var text = Make(source); Css.SetStyle(text, "direction:rtl;font-family:PingFang SC;font-size:16px;line-height:24px;text-align:start"); Layout(text);
        foreach (int index in new[] { 0, 2, 4, 6 })
        {
            var bounds = text.DocumentTextBounds(index, 1); Assert.NotEmpty(bounds);
            Assert.True(bounds.Sum(rect => rect.Width) > 1);
            var rect = bounds[0];
            int hit = text.DocumentCharacterFromPoint(new(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2));
            Assert.InRange(hit, index, index + 1);
        }
        text.SelectAll(); Assert.Equal(source, text.SelectedText);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MixedFontAndColorScopesShareParagraphPixelsAndSourceGeometry(bool nested)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var actual = Make("");
        var runs = new[]
        {
            new Run("A") { FontFamily = new("DejaVu Sans"), FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(221,40,65)) { Opacity = .6 } },
            new Run("B") { FontFamily = new("DejaVu Serif"), FontSize = 32, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(32,82,220)) { Opacity = .55 } },
            new Run("C") { FontFamily = new("DejaVu Sans"), FontSize = 64, Foreground = new SolidColorBrush(Color.FromRgb(28,140,78)) { Opacity = .7 } },
        };
        var scope = new Span(); foreach (var run in runs) scope.Inlines.Add(run); actual.Inlines.Add(scope);
        Css.SetStyle(actual, "font-size:16px;line-height:96px;text-align:left;unicode-bidi:" + (nested ? "normal;direction:ltr" : "bidi-override;direction:rtl"));
        if (nested) Css.SetStyle(scope, "unicode-bidi:isolate-override;direction:rtl");
        Layout(actual);
        string open = nested ? "\u2068\u202E" : "\u202E", close = nested ? "\u202C\u2069" : "\u202C";
        var spans = runs.Select((run, index) => new NativeTextParagraph.Span((index == 0 ? open : "") + run.Text + (index == 2 ? close : ""),
            run.FontFamily.GetRenderingSource(run), run.FontSize, run.FontWeight.ToOpenTypeWeight(), run.FontStyle.ToOpenTypeStyle(),
            ((SolidColorBrush)run.Foreground!).Color, run.Foreground!.Opacity)).ToArray();
        using var reference = NativeTextParagraph.TryCreate(spans, actual.FontFamily.GetRenderingSource(actual), actual.FontSize,
            10000000, 96, TextAlignment.Left, actual.FlowDirection, noWrap: true, textOptionsOwner: actual);
        Assert.NotNull(reference);
        var native = reference.Lines[0]; double baseline = actual.GetFirstBaselineOffset(200);
        var text = new FormattedText(reference.Text, actual.FontFamily.GetRenderingSource(actual), actual.FontSize) { PlatformTextLine = native };
        var expected = Pixels(dc => dc.DrawText(text, new(8, baseline - native.Metrics.Baseline)), context);
        Assert.Equal(expected, Pixels(actual, context));
        for (int source = 0; source < 3; source++)
        {
            var bounds = actual.DocumentTextBounds(source, 1); Assert.NotEmpty(bounds);
            var rectangles = reference.Selection(0, open.Length + source, 1); Assert.NotEmpty(rectangles);
            Assert.Equal(8 + rectangles.Min(rect => rect.X), bounds.Min(rect => rect.Left), 3);
            Assert.Equal(8 + rectangles.Max(rect => rect.X + rect.Width), bounds.Max(rect => rect.Right), 3);
            foreach (double fraction in new[] { .2, .8 })
            {
                var rect = bounds[0]; int hit = actual.DocumentCharacterFromPoint(new(rect.Left + fraction * rect.Width, rect.Top + rect.Height / 2));
                Assert.InRange(hit, source, source + 1);
            }
        }
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); actual.Render(recorder); var recorded = recorder.Commit();
        Assert.True(recorded.IsFullyRecordable);
        ((SolidColorBrush)runs[1].Foreground!).Color = Colors.Orange;
        runs[0].FontSize = 38; runs[2].Text = "Z"; Layout(actual);
        Assert.Equal(expected, Pixels(dc => DrawingReplayer.Replay(recorded, dc), context));
        Assert.NotEqual(expected, Pixels(actual, context));
        actual.SelectAll(); Assert.Equal("ABZ", actual.SelectedText);
    }

    [Fact]
    public void ColorOnlyBoundariesKeepATranslucentLigatureAndRecordedInk()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var actual = Make(""); var red = new SolidColorBrush(Colors.Red) { Opacity = .5 };
        var blue = new SolidColorBrush(Colors.Blue) { Opacity = .5 };
        actual.Inlines.Add(new Run("f") { Foreground = red }); actual.Inlines.Add(new Run("i") { Foreground = blue });
        Css.SetStyle(actual, "font-family:Times;font-size:40px;line-height:72px;font-variant-ligatures:common-ligatures;unicode-bidi:bidi-override;direction:ltr"); Layout(actual);
        string family = actual.FontFamily.GetRenderingSource(actual);
        using var reference = NativeTextParagraph.TryCreate([new("\u202Dfi\u202C", family, 40, 400, 0, blue.Color, blue.Opacity)],
            family, 40, 10000000, 72, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true, textOptionsOwner: actual);
        Assert.NotNull(reference);
        Assert.Equal(0, reference.SourceBounds(0, 1, 1)!.Value.Width);
        Assert.True(reference.SourceBounds(0, 2, 1)!.Value.Width > 1);
        var native = reference.Lines[0]; var text = new FormattedText(reference.Text, family, 40) { PlatformTextLine = native };
        var expected = Pixels(dc => dc.DrawText(text, new(8, actual.GetFirstBaselineOffset(200) - native.Metrics.Baseline)), context);
        Assert.Equal(expected, Pixels(actual, context));
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); actual.Render(recorder); var recorded = recorder.Commit();
        blue.Color = Colors.Green;
        Assert.Equal(expected, Pixels(dc => DrawingReplayer.Replay(recorded, dc), context));
        Assert.NotEqual(expected, Pixels(actual, context));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void GradientScopesKeepFontGeometryAndRecordedPaint(bool radial, bool rootPaint)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        GradientBrush gradient = radial ? new RadialGradientBrush(Colors.Red, Colors.Blue)
            : new LinearGradientBrush(Colors.Red, Colors.Blue, 0);
        gradient.Opacity = .65; gradient.Transform = new TranslateTransform(2, 1);
        var actual = Make(""); Css.SetStyle(actual, "unicode-bidi:bidi-override;direction:rtl;line-height:96px");
        var a = new Run("A") { FontFamily = new("DejaVu Sans"), FontSize = 24 };
        var b = new Run("B") { FontFamily = new("DejaVu Serif"), FontSize = 48, Foreground = new SolidColorBrush(Colors.Green) { Opacity = .6 } };
        var c = new Run("C") { FontFamily = new("DejaVu Sans"), FontSize = 64, Foreground = gradient };
        if (rootPaint) actual.Foreground = gradient; else a.Foreground = gradient;
        actual.Inlines.Add(a); actual.Inlines.Add(b); actual.Inlines.Add(c); Layout(actual);
        var runs = new[] { a, b, c }; string family = actual.FontFamily.GetRenderingSource(actual);
        using var reference = NativeTextParagraph.TryCreate(runs.Select((run, i) => new NativeTextParagraph.Span(
                (i == 0 ? "\u202E" : "") + run.Text + (i == 2 ? "\u202C" : ""), run.FontFamily.GetRenderingSource(run),
                run.FontSize, 400, 0, Colors.White)).ToArray(), family, actual.FontSize,
            10000000, 1, TextAlignment.Left, FlowDirection.RightToLeft, noWrap: true, textOptionsOwner: actual);
        Assert.NotNull(reference); var line = reference.Lines[0];
        var expected = Pixels(dc =>
        {
            for (int i = 0; i < runs.Length; i++)
            {
                var ink = runs[i].Foreground ?? actual.Foreground!;
                var text = new FormattedText(reference.Text, family, actual.FontSize) { Foreground = ink,
                    PlatformTextLine = line.ForSource(i + 1, 1, ink is SolidColorBrush solid ? solid.Color : Colors.White, ink.Opacity)
                        .WithBrushLineBox(line.Metrics.Baseline - (actual.GetFirstBaselineOffset(200) - 8),
                            Assert.Single(actual.GetDocumentLines(460)).Height) };
                dc.DrawText(text, new(8 - line.Metrics.Line.X, actual.GetFirstBaselineOffset(200) - line.Metrics.Baseline));
            }
        }, context);
        Assert.Equal(expected, Pixels(actual, context));
        var geometry = Enumerable.Range(0, 3).Select(i => actual.DocumentTextBounds(i, 1).ToArray()).ToArray();
        Assert.True(geometry[2].Min(r => r.X) < geometry[1].Min(r => r.X));
        Assert.True(geometry[1].Min(r => r.X) < geometry[0].Min(r => r.X));
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); actual.Render(recorder); var recorded = recorder.Commit();
        Assert.True(recorded.IsFullyRecordable);
        gradient.GradientStops[0].Color = Colors.Yellow; gradient.Opacity = .25;
        ((TranslateTransform)gradient.Transform).X = 23;
        c.Text = "Z"; a.FontSize = 36; Layout(actual);
        Assert.Equal(expected, Pixels(dc => DrawingReplayer.Replay(recorded, dc), context));
        Assert.NotEqual(expected, Pixels(actual, context));
        actual.SelectAll(); Assert.Equal("ABZ", actual.SelectedText);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ParagraphGradientSamplesEveryGlyphPixel(bool radial)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var options = new TextBlock(); TextOptions.SetTextRenderingMode(options, TextRenderingMode.Grayscale);
        const string source = "\u202DWW\u202C";
        using var paragraph = NativeTextParagraph.TryCreate([new(source, "DejaVu Sans", 64, 400, 0, Colors.White)],
            "DejaVu Sans", 64, 10000000, 100, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true, textOptionsOwner: options);
        Assert.NotNull(paragraph); var line = paragraph.Lines[0];
        GradientBrush gradient = radial ? new RadialGradientBrush(Colors.Red, Colors.Blue)
            : new LinearGradientBrush(Colors.Red, Colors.Blue, new Point(0, 0), new Point(1, 0));
        gradient.Opacity = .6;
        var mask = Pixels(dc => { dc.DrawRectangle(Brushes.Black, null, new(0, 0, 480, 210));
            dc.DrawText(new FormattedText(source, "DejaVu Sans", 64) { PlatformTextLine = line }, new(8, 12)); }, context);
        var actual = Pixels(dc => { dc.DrawRectangle(Brushes.White, null, new(0, 0, 480, 210));
            dc.DrawText(new FormattedText(source, "DejaVu Sans", 64) { Foreground = gradient, PlatformTextLine = line }, new(8, 12)); }, context);
        int maximum = 0, inkPixels = 0;
        for (int py = 0; py < 210; py++) for (int px = 0; px < 480; px++)
        {
            int offset = (py * 480 + px) * 4;
            double nx = (px + .5 - 8 - line.Metrics.Line.X) / line.Metrics.Line.Width;
            double ny = (py + .5 - 12) / line.Metrics.Line.Height;
            double t = Math.Clamp(radial ? Math.Sqrt(4 * (nx - .5) * (nx - .5) + 4 * (ny - .5) * (ny - .5)) : nx, 0, 1);
            double alpha = mask[offset] / 255d * .6;
            if (alpha > .2) inkPixels++;
            double[] paint = [255 * t, 0, 255 * (1 - t)];
            for (int channel = 0; channel < 3; channel++)
                maximum = Math.Max(maximum, Math.Abs(actual[offset + channel] - (int)Math.Round(paint[channel] * alpha + 255 * (1 - alpha))));
        }
        Assert.True(inkPixels > 100, "The independent white mask has no substantial glyph ink.");
        Assert.True(maximum <= 2, $"Per-pixel gradient differs from the independent white glyph mask: {maximum}.");
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void GradientSliceFiltersSelectAWholeCrossingLigature(bool radial)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var options = new TextBlock(); TextOptions.SetTextRenderingMode(options, TextRenderingMode.Grayscale);
        const string source = "\u202Dfi\u202C";
        using var paragraph = NativeTextParagraph.TryCreate([new(source, "Times", 40, 400, 0, Colors.Blue, .5)],
            "Times", 40, 10000000, 72, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true, textOptionsOwner: options);
        Assert.NotNull(paragraph);
        Assert.Equal(0, paragraph.SourceBounds(0, 1, 1)!.Value.Width);
        Assert.True(paragraph.SourceBounds(0, 2, 1)!.Value.Width > 1);
        GradientBrush gradient = radial ? new RadialGradientBrush(Colors.Blue, Colors.Blue)
            : new LinearGradientBrush(Colors.Blue, Colors.Blue, 0);
        gradient.Opacity = .5;
        var line = paragraph.Lines[0];
        var reference = Pixels(dc => { dc.DrawRectangle(Brushes.White, null, new(0, 0, 480, 210));
            dc.DrawText(new FormattedText(source, "Times", 40) { Foreground = gradient, PlatformTextLine = line }, new(8, 12)); }, context);
        var slice = line.ForSource(2, 1, Colors.White, 1);
        foreach (int visibility in new[] { 0, 1, 2 })
        {
            var actual = Pixels(dc => { dc.DrawRectangle(Brushes.White, null, new(0, 0, 480, 210));
                dc.DrawText(new FormattedText(source, "Times", 40) { PlatformTextLine = slice,
                    Foreground = gradient, GlyphVisibility = visibility, GlyphRanges = [1, 1] }, new(8, 12)); }, context);
            if (visibility == 2) Assert.All(actual, value => Assert.Equal(255, value));
            else
            {
                int difference = actual.Zip(reference, (a, b) => Math.Abs(a - b)).Max();
                Assert.True(difference <= 2, $"Whole-glyph gradient visibility {visibility} differs by {difference}.");
            }
        }
    }

    [Fact]
    public void BidiGeometryDoesNotDependOnForegroundBrushSupport()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var actual = Make("ACSSB"); var reference = Make("ACSSB");
        foreach (var block in new[] { actual, reference }) Css.SetStyle(block, "unicode-bidi:bidi-override;direction:rtl");
        actual.Foreground = new ImageBrush(); Layout(actual); Layout(reference);
        void VerifyGeometry()
        {
            for (int i = 0; i < actual.Text.Length; i++)
            {
                var bounds = actual.DocumentTextBounds(i, 1); var expected = reference.DocumentTextBounds(i, 1);
                Assert.NotEmpty(bounds); Assert.NotEmpty(expected);
                Assert.Equal(expected.Min(r => r.Left), bounds.Min(r => r.Left), 3);
                Assert.Equal(expected.Max(r => r.Right), bounds.Max(r => r.Right), 3);
            }
        }
        VerifyGeometry();
        actual.Foreground = new LinearGradientBrush(Colors.Red, Colors.Blue, 0); Layout(actual);
        VerifyGeometry(); Assert.Contains(Pixels(actual, context), value => value > 0);
        actual.Foreground = null; Layout(actual); VerifyGeometry();
        Assert.All(Pixels(actual, context), value => Assert.Equal(0, value));
        actual.SelectAll(); Assert.Equal("ACSSB", actual.SelectedText);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void MultilineBrushCoordinatesUseEachCssRowAndIgnoreQueryOrder(bool reverseSizes, bool softWrap)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        int width = softWrap ? 60 : 460;
        (TextBlock Block, Run First, Run Last, GradientBrush Gradient) Subject()
        {
            var block = Make("", width, softWrap); var gradient = new RadialGradientBrush(Colors.Red, Colors.Blue);
            var first = new Run("A") { FontSize = reverseSizes ? 64 : 24, Foreground = gradient };
            var last = new Run("C") { FontSize = reverseSizes ? 24 : 64, Foreground = gradient };
            block.Inlines.Add(first);
            if (softWrap) block.Inlines.Add(new Run(" ") { FontSize = 24, Foreground = gradient });
            else block.Inlines.Add(new LineBreak());
            block.Inlines.Add(last);
            Css.SetStyle(block, "unicode-bidi:bidi-override;direction:rtl;line-height:96px");
            Layout(block, width); return (block, first, last, gradient);
        }
        var a = Subject(); var b = Subject();
        _ = a.Block.DocumentTextBounds(0, 1); _ = b.Block.DocumentTextBounds(2, 1);
        var expected = Pixels(a.Block, context); var actual = Pixels(b.Block, context);
        Assert.Equal(expected, actual);
        var lines = b.Block.GetDocumentLines(width); Assert.Equal(2, lines.Count);
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); b.Block.Render(recorder); var frame = recorder.Commit();
        Assert.True(frame.IsFullyRecordable);
        var boxes = new List<(uint Row, double Top, double Height)>();
        void Visit(RecordedDrawing drawing, Point offset)
        {
            foreach (var command in drawing.Commands)
            {
                if (command.Kind == DrawCommandKind.SetOffset) offset = new(command.V0, command.V1);
                else if (command.A is RecordedDrawing child) Visit(child, offset);
                else if (command.A is FormattedText text && text.PlatformTextLine is NativeTextParagraph.Line native)
                    boxes.Add((native.Index, command.V1 + offset.Y + native.BrushBounds.Y, native.BrushBounds.Height));
            }
        }
        Visit(frame, default); Assert.NotEmpty(boxes);
        foreach (var box in boxes)
        {
            Assert.Equal(lines[(int)box.Row].Top, box.Top, 3);
            Assert.Equal(lines[(int)box.Row].Height, box.Height, 3);
        }
        b.Gradient.GradientStops[0].Color = Colors.Green; b.Last.FontSize = 40; b.First.Text = "Z"; Layout(b.Block, width);
        Assert.Equal(actual, Pixels(dc => DrawingReplayer.Replay(frame, dc), context));
        Assert.NotEqual(actual, Pixels(b.Block, context));
        b.Block.SelectAll(); Assert.Equal(softWrap ? "Z C" : "Z\nC", b.Block.SelectedText);
    }

    [Theory]
    [InlineData(0)] [InlineData(8)] [InlineData(96)]
    public void BidiGradientAllowsZeroAndTightCssLineBoxes(int height)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var block = Make("WW");
        Css.SetStyle(block, $"unicode-bidi:bidi-override;direction:ltr;line-height:{height}px");
        block.Foreground = new LinearGradientBrush(Colors.Red, Colors.Blue, new Point(0, 0), new Point(1, 0)); Layout(block);
        Assert.Equal(height, Assert.Single(block.GetDocumentLines(460)).Height, 3);
        Assert.Contains(Pixels(block, context), value => value > 0);
        block.SelectAll(); Assert.Equal("WW", block.SelectedText);
    }

    [Theory]
    [InlineData(false, 8, 0, false, false)]
    [InlineData(true, 2, 5, false, false)]
    [InlineData(false, -3, -2, false, false)]
    [InlineData(false, 8, 5, true, false)]
    [InlineData(true, 2, 5, true, true)]
    [InlineData(false, -3, -2, true, true)]
    public void AuthoredBidiSpacingUsesParagraphSourceAndRecordedInk(bool nested, int letter, int word, bool hardBreak, bool gradient)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        string source = hardBreak ? "AV fi B\nCD EF" : nested ? "A\u0301🙂 B" : "AV fi אב";
        var actual = Make(nested ? "" : source);
        var scope = new Span(); if (nested) { scope.Inlines.Add(new Run(source)); actual.Inlines.Add(scope); }
        Css.SetStyle(actual, $"font-size:28px;line-height:80px;letter-spacing:{letter}px;word-spacing:{word}px;direction:{(nested ? "ltr" : "rtl")};unicode-bidi:{(nested ? "normal" : "bidi-override")}");
        if (nested) Css.SetStyle(scope, "unicode-bidi:isolate-override;direction:rtl");
        if (gradient) actual.Foreground = new RadialGradientBrush(Colors.Red, Colors.Blue) { Opacity = .65 };
        Layout(actual);
        string open = nested ? "\u2068\u202E" : "\u202E", close = nested ? "\u202C\u2069" : "\u202C";
        string projected = string.Join("\n",source.Split('\n').Select(line => open+line+close));
        var units = new CssInputTextSpacing(letter,word).Units(projected);
        for (int i = 0; i < units.Length; i++) if (projected[(int)units[i].TextPosition] == '\n') units[i].Flags = 1;
        string family = actual.FontFamily.GetRenderingSource(actual);
        using var reference = NativeTextParagraph.TryCreateAdvanced([new(projected,family,28,400,0,Colors.Black)],
            family,28,10000000,1,actual.FlowDirection,units,contextPaint:true,textOptionsOwner:actual);
        Assert.NotNull(reference);
        var lines = actual.GetDocumentLines(460); Assert.Equal(lines.Count,reference.Lines.Length);
        double baseline = actual.GetFirstBaselineOffset(200)-8;
        var expected = Pixels(dc =>
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var native = reference.Lines[i]; var brush = actual.Foreground!;
                var paint = native.ForSource((int)native.Metrics.Line.TextPosition,(int)native.Metrics.Line.Length,
                        brush is SolidColorBrush solid ? solid.Color : Colors.White,brush.Opacity)
                    .WithBrushLineBox(native.Metrics.Baseline-baseline,lines[i].Height);
                dc.DrawText(new FormattedText(projected,family,28) { Foreground=brush,PlatformTextLine=paint },
                    new(8-native.Metrics.Line.X,lines[i].Top+baseline-native.Metrics.Baseline));
            }
        },context);
        Assert.Equal(expected,Pixels(actual,context));
        var boundaries = GraphemeClusters.GetBoundaries(source);
        for (int unit = 0, row = 0; unit+1 < boundaries.Length; unit++)
        {
            int index = boundaries[unit], length = boundaries[unit+1]-index;
            if (source[index] == '\n') { row++; continue; }
            int nativeIndex = index+open.Length+row*(open.Length+close.Length);
            var bounds = actual.DocumentTextBounds(index,length); Assert.NotEmpty(bounds);
            var rectangles = reference.Selection((uint)row,nativeIndex,length); Assert.NotEmpty(rectangles);
            Assert.Equal(8+rectangles.Min(rect => rect.X)-reference.Lines[row].Metrics.Line.X,bounds.Min(rect => rect.Left),3);
            Assert.Equal(8+rectangles.Max(rect => rect.X+rect.Width)-reference.Lines[row].Metrics.Line.X,bounds.Max(rect => rect.Right),3);
            var box = bounds[0];
            Assert.InRange(actual.DocumentCharacterFromPoint(new(box.Left+box.Width*.2,box.Top+box.Height/2)),index,index+length);
        }
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); actual.Render(recorder); var frame = recorder.Commit();
        Assert.True(frame.IsFullyRecordable);
        if (actual.Foreground is RadialGradientBrush radial) radial.GradientStops[0].Color=Colors.Green;
        if (nested) ((Run)scope.Inlines[0]).Text=source.Replace('A','Z'); else actual.Text=source.Replace('A','Z');
        Layout(actual);
        Assert.Equal(expected,Pixels(dc => DrawingReplayer.Replay(frame,dc),context));
        Assert.NotEqual(expected,Pixels(actual,context));
        actual.SelectAll(); Assert.Equal(source.Replace('A','Z'),actual.SelectedText);
    }

    [Theory]
    [InlineData(2,5)] [InlineData(-3,-2)] [InlineData(0,5)]
    public void AuthoredBidiSpacingWrapHasNoTrackingOrWordAdvanceAtCollapsedEdges(int letter, int word)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string family = "DejaVu Sans Mono";
        using var natural = NativeTextParagraph.TryCreate([new("AB",family,28,400,0,Colors.Black)],family,28,
            10000000,1,TextAlignment.Left,FlowDirection.LeftToRight,noWrap:true);
        Assert.NotNull(natural); double pair = natural.Lines[0].Metrics.Line.Width+letter;
        int width = (int)Math.Ceiling(pair+16+.25);
        var actual = Make("AB AB AB",width,true);
        Css.SetStyle(actual,$"font-family:'{family}';font-size:28px;line-height:64px;white-space:normal;letter-spacing:{letter}px;word-spacing:{word}px;unicode-bidi:bidi-override;direction:rtl");
        Layout(actual,width);
        var lines = actual.GetDocumentLines(width); Assert.Equal(3,lines.Count);
        const string projected = "\u202EAB\u2028AB\u2028AB\u202C";
        var units = new CssInputTextSpacing(letter,word).Units(projected);
        for (int i=0;i<units.Length;i++)if(projected[(int)units[i].TextPosition]=='\u2028')units[i].Flags=1;
        using var reference = NativeTextParagraph.TryCreateAdvanced([new(projected,actual.FontFamily.GetRenderingSource(actual),28,400,0,Colors.Black)],
            actual.FontFamily.GetRenderingSource(actual),28,10000000,1,actual.FlowDirection,units,contextPaint:true,textOptionsOwner:actual);
        Assert.NotNull(reference); Assert.Equal(3,reference.Lines.Length);
        double baseline = actual.GetFirstBaselineOffset(200)-8;
        var expected = Pixels(dc =>
        {
            for (int row=0;row<3;row++)
            {
                var native=reference.Lines[row];
                var paint=native.ForSource((int)native.Metrics.Line.TextPosition,(int)native.Metrics.Line.Length,
                    ((SolidColorBrush)actual.Foreground!).Color,actual.Foreground.Opacity);
                dc.DrawText(new FormattedText(projected,actual.FontFamily.GetRenderingSource(actual),28) { Foreground=actual.Foreground,PlatformTextLine=paint },
                    new(8-native.Metrics.Line.X,lines[row].Top+baseline-native.Metrics.Baseline));
                Assert.Equal(pair,native.Metrics.Line.Width,3);
            }
        },context);
        Assert.Equal(expected,Pixels(actual,context));
        foreach (int index in new[]{0,3,6})
        {
            var bounds=actual.DocumentTextBounds(index,2); Assert.NotEmpty(bounds);
            Assert.Equal(pair,bounds.Max(r=>r.Right)-bounds.Min(r=>r.Left),3);
            Assert.Equal(8,bounds.Min(r=>r.Left),3);
        }
        actual.SelectAll(); Assert.Equal("AB AB AB",actual.SelectedText);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AuthoredBidiSpacingAveragesMixedFontRunValuesInVisualOrder(bool negative)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var gradient = new RadialGradientBrush(Colors.Red,Colors.Blue) { Opacity=.6 };
        var actual = Make("");
        var runs = new[] {new Run("A") {FontSize=24,Foreground=gradient},
            new Run("B") {FontFamily=new("DejaVu Serif"),FontSize=48,FontWeight=FontWeights.Bold,Foreground=Brushes.Green},
            new Run("C") {FontSize=64,Foreground=gradient}};
        foreach(var run in runs)actual.Inlines.Add(run);
        Css.SetStyle(actual,"font-size:16px;line-height:96px;letter-spacing:2px;unicode-bidi:bidi-override;direction:rtl");
        Css.SetStyle(runs[1],"letter-spacing:10px");Css.SetStyle(runs[2],"letter-spacing:"+(negative?-4:6)+"px");Layout(actual);
        var spans=runs.Select((run,i)=>new NativeTextParagraph.Span((i==0?"\u202E":"")+run.Text+(i==2?"\u202C":""),
            run.FontFamily.GetRenderingSource(run),run.FontSize,run.FontWeight.ToOpenTypeWeight(),0,Colors.Black)).ToArray();
        NativeMethods.ParagraphAdvanceUnit[] units=[new(){TextPosition=0,Length=1,Flags=1},
            new(){TextPosition=1,Length=1,Spacing=2},new(){TextPosition=2,Length=1,Spacing=10},
            new(){TextPosition=3,Length=1,Spacing=negative?-4:6},new(){TextPosition=4,Length=1,Flags=1}];
        string family=actual.FontFamily.GetRenderingSource(actual);
        using var reference=NativeTextParagraph.TryCreateAdvanced(spans,family,16,10000000,1,actual.FlowDirection,units,contextPaint:true,textOptionsOwner:actual);
        Assert.NotNull(reference);var native=Assert.Single(reference.Lines);var line=Assert.Single(actual.GetDocumentLines(460));
        double baseline=actual.GetFirstBaselineOffset(200)-8;
        var expected=Pixels(dc=>
        {
            for(int i=0;i<runs.Length;i++)
            {
                var brush=runs[i].Foreground!;var paint=native.ForSource(i+1,1,brush is SolidColorBrush solid?solid.Color:Colors.White,brush.Opacity)
                    .WithBrushLineBox(native.Metrics.Baseline-baseline,line.Height);
                dc.DrawText(new FormattedText(reference.Text,family,16){Foreground=brush,PlatformTextLine=paint},new(8-native.Metrics.Line.X,8+baseline-native.Metrics.Baseline));
            }
        },context);
        Assert.Equal(expected,Pixels(actual,context));
        for(int i=0;i<3;i++)
        {
            var bounds=actual.DocumentTextBounds(i,1);var rects=reference.Selection(0,i+1,1);Assert.NotEmpty(bounds);Assert.NotEmpty(rects);
            Assert.Equal(8+rects.Min(r=>r.X)-native.Metrics.Line.X,bounds.Min(r=>r.Left),3);
            Assert.Equal(8+rects.Max(r=>r.X+r.Width)-native.Metrics.Line.X,bounds.Max(r=>r.Right),3);
        }
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        Css.SetStyle(runs[1],"letter-spacing:20px");runs[1].FontSize=40;gradient.GradientStops[0].Color=Colors.Yellow;Layout(actual);
        Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));Assert.NotEqual(expected,Pixels(actual,context));
        actual.SelectAll();Assert.Equal("ABC",actual.SelectedText);
    }

    [Fact]
    public void AuthoredBidiSpacingPreservesEmptyHardRowsAndAuthorText()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="A\n\nB";var actual=Make(source);var reference=Make("\u202EA\u202C\n\n\u202EB\u202C");
        const string style="line-height:64px;letter-spacing:8px;word-spacing:5px;direction:rtl;unicode-bidi:";
        Css.SetStyle(actual,style+"bidi-override");Css.SetStyle(reference,style+"normal");Layout(actual);Layout(reference);
        var lines=actual.GetDocumentLines(460);Assert.Equal(3,lines.Count);Assert.Equal(lines[1].Start,lines[1].End);
        Assert.Equal(Pixels(reference,context),Pixels(actual,context));
        foreach(int index in new[]{0,3})Assert.NotEmpty(actual.DocumentTextBounds(index,1));
        actual.SelectAll();Assert.Equal(source,actual.SelectedText);
    }

    [Theory]
    [InlineData(false, false, "80px")]
    [InlineData(true, false, "80px")]
    [InlineData(false, true, "80px")]
    [InlineData(true, true, "80px")]
    [InlineData(false, false, "4")]
    [InlineData(true, false, "4")]
    [InlineData(false, false, "0")]
    [InlineData(true, false, "0")]
    [InlineData(false, false, "1px")]
    [InlineData(true, false, "1px")]
    public void AuthoredBidiTabsRetainFieldGlyphsAtIndependentBlockStops(bool rtl, bool nested, string tabSize)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source = "ABC\tDEF";
        var actual = Make(nested ? "" : source);
        var run = new Run(source); var scope = new Span();
        if (nested) { scope.Inlines.Add(run); actual.Inlines.Add(scope); }
        Css.SetStyle(actual, $"font-size:28px;line-height:64px;tab-size:{tabSize};direction:{(rtl ? "rtl" : "ltr")};unicode-bidi:{(nested ? "normal" : "bidi-override")}");
        if (nested) Css.SetStyle(scope,"unicode-bidi:isolate-override;direction:rtl");
        Layout(actual);
        string open = nested ? "\u2068\u202E" : rtl ? "\u202E" : "\u202D";
        string projected = open+source+(nested ? "\u202C\u2069" : "\u202C");
        string family = actual.FontFamily.GetRenderingSource(actual);
        using var reference = NativeTextParagraph.TryCreate([new(projected,family,28,400,0,Colors.Black)],
            family,28,10000000,1,TextAlignment.Left,actual.FlowDirection,noWrap:true,textOptionsOwner:actual);
        using var ruler = NativeTextParagraph.TryCreate([new("0 ",family,28,400,0,Colors.Black)],
            family,28,10000000,1,TextAlignment.Left,FlowDirection.LeftToRight,noWrap:true,textOptionsOwner:actual);
        Assert.NotNull(reference); Assert.NotNull(ruler);
        var native = Assert.Single(reference.Lines);
        double prefix = reference.Selection(0,open.Length,3).Sum(rect => (double)rect.Width);
        double oldTab = reference.Selection(0,open.Length+3,1).Sum(rect => (double)rect.Width);
        double halfCh = ruler.Selection(0,0,1).Sum(rect => (double)rect.Width)/2;
        double step = tabSize.EndsWith("px",StringComparison.Ordinal)
            ? double.Parse(tabSize[..^2],System.Globalization.CultureInfo.InvariantCulture)
            : double.Parse(tabSize,System.Globalization.CultureInfo.InvariantCulture)*ruler.Selection(0,1,1).Sum(rect => (double)rect.Width);
        double advance = step == 0 ? 0 : (Math.Floor(prefix/step)+1)*step-prefix;
        if (step > 0 && advance < halfCh) advance += step;
        double delta = advance-oldTab;
        var units = new CssInputTextSpacing(0,0).Units(projected);
        int tabUnit = Array.FindIndex(units, unit => unit.TextPosition == open.Length+3);
        units[tabUnit].Flags=0; units[tabUnit].Trailing=(float)delta;
        using var positioned = NativeTextParagraph.TryCreateAdvanced([new(projected,family,28,400,0,Colors.Black)],
            family,28,10000000,1,actual.FlowDirection,units,contextPaint:true,textOptionsOwner:actual);
        Assert.NotNull(positioned); var paintLine=Assert.Single(positioned.Lines);
        var line = Assert.Single(actual.GetDocumentLines(460));
        double baseline = actual.GetFirstBaselineOffset(200)-8;
        double Shift(int field) => rtl == (field == 0) ? delta : 0;
        var expected = Pixels(dc =>
        {
            // The independently calculated advance is applied before raster
            // snapping. Translating already rasterized source slices would use
            // a different subpixel phase on Software/Vulkan at scaled DPI.
            dc.DrawText(new FormattedText(projected,family,28) { Foreground=actual.Foreground,
                PlatformTextLine=paintLine.ForSource(open.Length,source.Length,((SolidColorBrush)actual.Foreground!).Color,actual.Foreground.Opacity) },
                new(8-paintLine.Metrics.Line.X,line.Top+baseline-paintLine.Metrics.Baseline));
        },context);
        Assert.Equal(expected,Pixels(actual,context));
        Assert.Equal(advance,actual.DocumentTextBounds(3,1).Sum(rect => rect.Width),3);
        for (int index=0;index<source.Length;index++)
        {
            if (index == 3) continue;
            var rectangles = reference.Selection(0,open.Length+index,1);
            var bounds = actual.DocumentTextBounds(index,1); Assert.NotEmpty(bounds);
            Assert.Equal(8+rectangles.Min(rect => rect.X)-native.Metrics.Line.X+Shift(index < 3 ? 0 : 1),bounds.Min(rect => rect.Left),3);
            Assert.Equal(rectangles.Sum(rect => (double)rect.Width),bounds.Sum(rect => rect.Width),3);
            var box = bounds[0];
            Assert.InRange(actual.DocumentCharacterFromPoint(new(box.Left+box.Width*.2,box.Top+box.Height/2)),index,index+1);
        }
        actual.SelectAll(); Assert.Equal(source,actual.SelectedText); actual.ClearSelection();
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); actual.Render(recorder); var frame = recorder.Commit();
        Assert.True(frame.IsFullyRecordable);
        if (nested) run.Text = source.Replace('A','Z'); else actual.Text = source.Replace('A','Z');
        Layout(actual);
        Assert.Equal(expected,Pixels(dc => DrawingReplayer.Replay(frame,dc),context));
        Assert.NotEqual(expected,Pixels(actual,context));
    }

    [Theory]
    [InlineData(false, 2, 5)] [InlineData(true, 2, 5)]
    [InlineData(false, 0, 5)] [InlineData(true, 0, 5)]
    [InlineData(false, -3, -2)] [InlineData(true, -3, -2)]
    public void AuthoredBidiTabGlyphStartUsesTheRulerAfterTracking(bool rtl, int letter, int word)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source = "AB\tCD";
        var actual = Make(source);
        Css.SetStyle(actual,$"font-size:28px;line-height:64px;tab-size:4;letter-spacing:{letter}px;word-spacing:{word}px;direction:{(rtl ? "rtl" : "ltr")};unicode-bidi:bidi-override");
        Layout(actual); string family = actual.FontFamily.GetRenderingSource(actual);
        string text = (rtl ? "\u202E" : "\u202D")+source+"\u202C";
        using var natural = NativeTextParagraph.TryCreate([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,actual.FlowDirection,noWrap:true,textOptionsOwner:actual);
        using var ruler = NativeTextParagraph.TryCreate([new("0 ",family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,FlowDirection.LeftToRight,noWrap:true,textOptionsOwner:actual);
        Assert.NotNull(natural); Assert.NotNull(ruler);
        double prefix = natural.Selection(0,1,2).Sum(rect => (double)rect.Width);
        double suffix = natural.Selection(0,4,2).Sum(rect => (double)rect.Width);
        double step = 4*Math.Max(0,ruler.Selection(0,1,1).Sum(rect => (double)rect.Width)+letter+word);
        double before = prefix+letter+(letter > 0 ? 2*letter : 0);
        double tick = (Math.Floor(before/step)+1)*step;
        double halfCh = ruler.Selection(0,0,1).Sum(rect => (double)rect.Width)/2;
        if (tick-before < halfCh) tick += step;
        double width = tick+suffix+letter;
        var all = actual.DocumentTextBounds(0,source.Length); Assert.NotEmpty(all);
        Assert.Equal(width,all.Max(rect => rect.Right)-all.Min(rect => rect.Left),3);
        var next = Assert.Single(actual.DocumentTextBounds(3,1));
        double pad = Math.Max(0,letter)/2d;
        double glyph = rtl ? width-(next.Right-8-pad) : next.Left-8+pad;
        Assert.Equal(tick,glyph,3);
        actual.SelectAll(); Assert.Equal(source,actual.SelectedText);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AuthoredBidiTabsKeepMixedFontsAndPerRunStopsInRecordedPaint(bool gradient)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var actual = Make("");
        var first = new Run("AB\t") { FontSize=24 };
        var second = new Run("CD\tEF") { FontSize=40, FontFamily=new("DejaVu Serif") };
        actual.Inlines.Add(first); actual.Inlines.Add(second);
        Css.SetStyle(actual,"font-size:16px;line-height:96px;direction:rtl;unicode-bidi:bidi-override");
        Css.SetStyle(first,"tab-size:80px"); Css.SetStyle(second,"tab-size:56px");
        if (gradient) actual.Foreground = new RadialGradientBrush(Colors.Red,Colors.Blue) { Opacity=.65 };
        Layout(actual);
        string family = actual.FontFamily.GetRenderingSource(actual);
        var spans = new[] {new NativeTextParagraph.Span("\u202E"+first.Text,family,24,400,0,Colors.Black),
            new NativeTextParagraph.Span(second.Text+"\u202C",second.FontFamily.GetRenderingSource(second),40,400,0,Colors.Black)};
        string text = string.Concat(spans.Select(span => span.Text));
        using var natural = NativeTextParagraph.TryCreate(spans,family,16,10000000,1,TextAlignment.Left,actual.FlowDirection,noWrap:true,textOptionsOwner:actual);
        Assert.NotNull(natural);
        var units = new CssInputTextSpacing(0,0).Units(text);
        double position = 0;
        for (int i=1;i<text.Length-1;i++)
        {
            double width = natural.Selection(0,i,1).Sum(rect => (double)rect.Width);
            if (text[i] == '\t')
            {
                double step = i == 3 ? 80 : 56;
                double advance = (Math.Floor(position/step)+1)*step-position;
                string tabFamily = i == 3 ? family : second.FontFamily.GetRenderingSource(second);
                double size = i == 3 ? 24 : 40;
                using var ruler = NativeTextParagraph.TryCreate([new("0",tabFamily,size,400,0,Colors.Black)],tabFamily,size,10000000,1,TextAlignment.Left,FlowDirection.LeftToRight,noWrap:true,textOptionsOwner:actual);
                Assert.NotNull(ruler); double halfCh = ruler.Lines[0].Metrics.Line.Width/2;
                if (advance < halfCh) advance += step;
                int unit = Array.FindIndex(units,value => value.TextPosition == i);
                units[unit].Flags=0; units[unit].Trailing=(float)(advance-width); width=advance;
            }
            position += width;
        }
        using var reference = NativeTextParagraph.TryCreateAdvanced(spans,family,16,10000000,1,actual.FlowDirection,units,contextPaint:true,textOptionsOwner:actual);
        Assert.NotNull(reference); var native = Assert.Single(reference.Lines);
        var line = Assert.Single(actual.GetDocumentLines(460)); double baseline = actual.GetFirstBaselineOffset(200)-8;
        var brush = actual.Foreground!;
        var expected = Pixels(dc => dc.DrawText(new FormattedText(text,family,16) { Foreground=brush,
            PlatformTextLine=native.ForSource(0,text.Length,brush is SolidColorBrush solid ? solid.Color : Colors.White,brush.Opacity)
                .WithBrushLineBox(native.Metrics.Baseline-baseline,line.Height) },new(8-native.Metrics.Line.X,line.Top+baseline-native.Metrics.Baseline)),context);
        Assert.Equal(expected,Pixels(actual,context));
        for (int index=0;index<8;index++)
        {
            var bounds = actual.DocumentTextBounds(index,1); var rectangles = reference.Selection(0,index+1,1);
            Assert.NotEmpty(bounds); Assert.NotEmpty(rectangles);
            Assert.Equal(8+rectangles.Min(rect => rect.X)-native.Metrics.Line.X,bounds.Min(rect => rect.Left),3);
            Assert.Equal(rectangles.Sum(rect => (double)rect.Width),bounds.Sum(rect => rect.Width),3);
        }
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); actual.Render(recorder); var frame = recorder.Commit();
        Assert.True(frame.IsFullyRecordable);
        Css.SetStyle(second,"tab-size:72px"); first.FontSize=30;
        if (brush is RadialGradientBrush radial) radial.GradientStops[0].Color=Colors.Green;
        Layout(actual);
        Assert.Equal(expected,Pixels(dc => DrawingReplayer.Replay(frame,dc),context));
        Assert.NotEqual(expected,Pixels(actual,context));
        actual.SelectAll(); Assert.Equal("AB\tCD\tEF",actual.SelectedText);
    }

    [Theory]
    [InlineData("pre", true, false)] [InlineData("pre", true, true)]
    [InlineData("pre-wrap", true, true)] [InlineData("break-spaces", true, true)]
    [InlineData("pre-wrap", false, false)] [InlineData("pre-wrap", false, true)]
    [InlineData("break-spaces", false, true)]
    public void AuthoredBidiTabsResetRulersAcrossWrappedAndEmptyRows(string mode, bool hard, bool rtl)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        string source=hard ? "AB\tCD\n\nEF\tGH" : "AB\tCD AB\tEF";
        string open=rtl ? "\u202E" : "\u202D";
        var actual=Make(source); var reference=Make(string.Join("\n",source.Split('\n').Select(row=>row.Length == 0 ? "" : open+row+"\u202C")));
        string style=$"font-size:28px;line-height:64px;tab-size:80px;white-space:{mode};direction:{(rtl ? "rtl" : "ltr")};unicode-bidi:";
        Css.SetStyle(actual,style+"bidi-override"); Css.SetStyle(reference,style+"normal");
        actual.Foreground=new LinearGradientBrush(Colors.Red,Colors.Blue,new(0,0),new(1,0)){Opacity=.65};
        reference.Foreground=new LinearGradientBrush(Colors.Red,Colors.Blue,new(0,0),new(1,0)){Opacity=.65};
        byte[] expected=[];
        foreach(int width in hard ? new[]{460,220,460} : new[]{110,220,460,110})
        {
            Css.SetStyle(actual,style+"bidi-override;width:"+width+"px");
            Css.SetStyle(reference,style+"normal;width:"+width+"px");
            Layout(actual,width); Layout(reference,width);
            var lines=actual.GetDocumentLines(width);
            Assert.Equal(reference.GetDocumentLines(width).Count,lines.Count);
            if(hard){Assert.Equal(3,lines.Count);Assert.Equal(lines[1].Start,lines[1].End);}
            else if(width == 110) Assert.True(lines.Count>1);
            expected=Pixels(reference,context); Assert.Equal(expected,Pixels(actual,context));
            foreach(int index in Enumerable.Range(0,source.Length).Reverse())
                if(source[index] is not ('\n' or '\t' or ' '))
                {
                    var boxes=actual.DocumentTextBounds(index,1);Assert.NotEmpty(boxes);
                    Assert.All(boxes,box=>{Assert.True(box.Left>=8-.001);Assert.True(box.Right<=width-8+.001);});
                    foreach(var box in boxes)
                        Assert.InRange(actual.DocumentCharacterFromPoint(new(box.Left+box.Width*.2,box.Top+box.Height/2)),index,index+1);
                }
            Assert.Equal(expected,Pixels(actual,context));
        }
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        actual.Text=source.Replace('A','Z');Layout(actual,hard ? 460 : 110);
        Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));
        Assert.NotEqual(expected,Pixels(actual,context));
        actual.SelectAll(); Assert.Equal(source.Replace('A','Z'),actual.SelectedText);
    }

    [Theory]
    [InlineData(false, 12, "left")] [InlineData(true, 12, "left")]
    [InlineData(false, -12, "left")] [InlineData(true, -12, "left")]
    [InlineData(false, 12, "right")] [InlineData(true, 12, "right")]
    [InlineData(false, 12, "center")] [InlineData(true, 12, "center")]
    public void AuthoredBidiTabStopsPrecedeIndentAndAlignmentPaint(bool rtl, int indent, string align)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="AB\tCD";
        var actual=Make(source);
        Css.SetStyle(actual,$"font-size:28px;line-height:64px;tab-size:80px;text-indent:{indent}px;text-align:{align};direction:{(rtl ? "rtl" : "ltr")};unicode-bidi:bidi-override");
        Layout(actual); string family=actual.FontFamily.GetRenderingSource(actual);
        string text=(rtl ? "\u202E" : "\u202D")+source+"\u202C";
        using var natural=NativeTextParagraph.TryCreate([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,actual.FlowDirection,noWrap:true,textOptionsOwner:actual);
        using var ruler=NativeTextParagraph.TryCreate([new("0",family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,FlowDirection.LeftToRight,noWrap:true,textOptionsOwner:actual);
        Assert.NotNull(natural); Assert.NotNull(ruler);
        double prefix=natural.Selection(0,1,2).Sum(rect=>(double)rect.Width);
        double oldTab=natural.Selection(0,3,1).Sum(rect=>(double)rect.Width);
        double position=indent+prefix, advance=(Math.Floor(position/80)+1)*80-position;
        double halfCh=ruler.Lines[0].Metrics.Line.Width/2;
        if(advance < halfCh) advance+=80;
        var units=new CssInputTextSpacing(0,0).Units(text);int tab=Array.FindIndex(units,unit=>unit.TextPosition==3);
        units[tab].Flags=0;units[tab].Trailing=(float)(advance-oldTab);
        using var reference=NativeTextParagraph.TryCreateAdvanced([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,actual.FlowDirection,units,contextPaint:true,textOptionsOwner:actual);
        Assert.NotNull(reference);var native=Assert.Single(reference.Lines);
        double width=native.Metrics.Line.Width;
        double x=8+(rtl ? 0 : indent)+Math.Max(0,444-indent-width)*(align == "right" ? 1 : align == "center" ? .5 : 0);
        var line=Assert.Single(actual.GetDocumentLines(460));double baseline=actual.GetFirstBaselineOffset(200)-8;
        var expected=Pixels(dc=>dc.DrawText(new FormattedText(text,family,28){Foreground=actual.Foreground,
            PlatformTextLine=native.ForSource(0,text.Length,((SolidColorBrush)actual.Foreground!).Color,actual.Foreground.Opacity)},
            new(x-native.Metrics.Line.X,line.Top+baseline-native.Metrics.Baseline)),context);
        Assert.Equal(expected,Pixels(actual,context));
        for(int i=0;i<source.Length;i++)
        {
            var rects=reference.Selection(0,i+1,1);var boxes=actual.DocumentTextBounds(i,1);Assert.NotEmpty(rects);Assert.NotEmpty(boxes);
            Assert.Equal(x+rects.Min(rect=>rect.X)-native.Metrics.Line.X,boxes.Min(rect=>rect.Left),3);
        }
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        actual.Text=source.Replace('A','Z');Layout(actual);
        Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));
        Assert.NotEqual(expected,Pixels(actual,context));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AuthoredBidiPlaintextTabsFollowEachParagraphDirection(bool rtl)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="אב\tCD\nAB\tגד";var actual=Make(source);
        Css.SetStyle(actual,$"font-size:28px;line-height:64px;tab-size:80px;text-align:left;direction:{(rtl ? "rtl" : "ltr")};unicode-bidi:plaintext");
        Layout(actual);string family=actual.FontFamily.GetRenderingSource(actual);
        using var natural=NativeTextParagraph.TryCreate([new(source,family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,actual.FlowDirection,naturalDirection:true,noWrap:true,textOptionsOwner:actual);
        using var ruler=NativeTextParagraph.TryCreate([new("0",family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,FlowDirection.LeftToRight,noWrap:true,textOptionsOwner:actual);
        Assert.NotNull(natural);Assert.NotNull(ruler);
        var units=new CssInputTextSpacing(0,0).Units(source);
        for(int i=0;i<units.Length;i++)
        {
            int index=(int)units[i].TextPosition;
            if(source[index] == '\n')units[i].Flags=1;
            if(source[index] != '\t')continue;
            uint row=index == 2 ? 0u : 1u;int start=index == 2 ? 0 : 6;
            double prefix=natural.Selection(row,start,2).Sum(rect=>(double)rect.Width);
            double oldTab=natural.Selection(row,index,1).Sum(rect=>(double)rect.Width);
            double advance=(Math.Floor(prefix/80)+1)*80-prefix, halfCh=ruler.Lines[0].Metrics.Line.Width/2;
            if(advance < halfCh)advance+=80;
            units[i].Flags=0;units[i].Trailing=(float)(advance-oldTab);
        }
        using var reference=NativeTextParagraph.TryCreateAdvanced([new(source,family,28,400,0,Colors.Black)],family,28,10000000,1,actual.FlowDirection,units,contextPaint:true,naturalDirection:true,textOptionsOwner:actual);
        Assert.NotNull(reference);Assert.Equal(2,reference.Lines.Length);
        var lines=actual.GetDocumentLines(460);Assert.Equal(2,lines.Count);double baseline=actual.GetFirstBaselineOffset(200)-8;
        var expected=Pixels(dc=>
        {
            for(int row=0;row<2;row++)
            {
                var native=reference.Lines[row];
                dc.DrawText(new FormattedText(source,family,28){Foreground=actual.Foreground,
                    PlatformTextLine=native.ForSource((int)native.Metrics.Line.TextPosition,(int)native.Metrics.Line.Length,((SolidColorBrush)actual.Foreground!).Color,actual.Foreground.Opacity)},
                    new(8-native.Metrics.Line.X,lines[row].Top+baseline-native.Metrics.Baseline));
            }
        },context);
        Assert.Equal(expected,Pixels(actual,context));
        for(int index=0;index<source.Length;index++)
        {
            if(source[index] == '\n')continue;uint row=index < 6 ? 0u : 1u;
            var rects=reference.Selection(row,index,1);var boxes=actual.DocumentTextBounds(index,1);Assert.NotEmpty(boxes);
            Assert.Equal(8+rects.Min(rect=>rect.X)-reference.Lines[row].Metrics.Line.X,boxes.Min(rect=>rect.Left),3);
        }
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        actual.Text=source.Replace('A','Z');Layout(actual);
        Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));
        Assert.NotEqual(expected,Pixels(actual,context));
        actual.SelectAll();Assert.Equal(source.Replace('A','Z'),actual.SelectedText);
    }

    [Theory] [InlineData("normal")] [InlineData("bidi-override")]
    public void AuthoredBidiTinyTabsSkipOnlyOneAdditionalStop(string mode)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="\t\t\tX";var actual=Make(source);var reference=Make("X");
        const string style="font-size:28px;line-height:64px;tab-size:1px;direction:ltr;unicode-bidi:";
        Css.SetStyle(actual,style+mode);Css.SetStyle(reference,style+mode+";padding-left:14px");Layout(actual);Layout(reference);
        // Compare absolute glyph origins. A translated raster draw can use a
        // different GPU interpolation path even for an integer translation.
        var expected=Pixels(reference,context);
        Assert.Equal(expected,Pixels(actual,context));
        Assert.Equal(14,Assert.Single(actual.DocumentTextBounds(3,1)).Left,3);
        actual.SelectAll();Assert.Equal(source,actual.SelectedText);actual.ClearSelection();
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        Css.SetStyle(actual,style+mode+";tab-size:2px");Layout(actual);
        Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));
        Assert.NotEqual(expected,Pixels(actual,context));
    }

    [Theory]
    [InlineData(false,"inter-word","AB CD EF",0,0,false)]
    [InlineData(true,"inter-word","AB CD EF",0,0,false)]
    [InlineData(false,"inter-word","AB CD EF",2,5,false)]
    [InlineData(true,"inter-word","AB CD EF",2,5,true)]
    [InlineData(false,"inter-word","AB CD EF",-1,3,true)]
    [InlineData(true,"inter-word","سلام سلام",0,0,false)]
    [InlineData(false,"inter-character","ABCDEF",0,0,false)]
    [InlineData(true,"inter-character","ABCDEF",0,0,true)]
    [InlineData(false,"inter-character","A🙂 B\u0301 C",0,0,false)]
    [InlineData(true,"inter-character","A🙂 B\u0301 C",0,0,true)]
    [InlineData(false,"inter-character","ffi",0,0,false)]
    [InlineData(true,"inter-character","ffi",0,0,true)]
    [InlineData(false,"auto","中文测试",0,0,true)]
    [InlineData(true,"auto","中文测试",0,0,false)]
    [InlineData(false,"none","AB CD EF",0,0,false)]
    [InlineData(true,"none","AB CD EF",0,0,true)]
    [InlineData(true,"inter-character","سلام",0,0,false)]
    public void AuthoredBidiJustificationMatchesIndependentWholeParagraphAdvances(bool rtl,string method,string source,int letter,int word,bool gradient)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var actual=Make(source);
        Css.SetStyle(actual,$"font-size:28px;line-height:64px;direction:{(rtl?"rtl":"ltr")};unicode-bidi:bidi-override;text-align:justify;text-align-last:justify;text-justify:{method};letter-spacing:{letter}px;word-spacing:{word}px"+
            (source=="ffi"?";font-feature-settings:'liga' 1":""));
        if(gradient)actual.Foreground=new LinearGradientBrush(Colors.Red,Colors.Blue,new(0,0),new(1,0)){Opacity=.65};
        Layout(actual);
        string family=actual.FontFamily.GetRenderingSource(actual),text=(rtl?"\u202E":"\u202D")+source+"\u202C";
        var units=new CssInputTextSpacing(letter,word).Units(text);
        NativeTextParagraph? Shape()=>NativeTextParagraph.TryCreateAdvanced([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,actual.FlowDirection,units,contextPaint:true,textOptionsOwner:actual);
        using var natural=Shape();Assert.NotNull(natural);double naturalWidth=Assert.Single(natural.Lines).Metrics.Line.Width;
        var active=Enumerable.Range(0,units.Length).Where(i=>(units[i].Flags&1)==0).ToArray();
        bool characters=method=="inter-character"&&source!="سلام"||method=="auto";
        var separators=active.Where(i=>text[(int)units[i].TextPosition]==' ').ToArray();
        int count=method=="none"||source=="سلام"?0:characters?active.Length-1:separators.Length;
        double extra=count==0?0:(444-naturalWidth)/count;
        if(characters&&count>0)
            for(int i=1;i<active.Length;i++)
            {units[active[i-1]].Trailing+=(float)(extra/2);units[active[i]].Leading+=(float)(extra/2);}
        else if(count>0)
            foreach(int i in separators){units[i].Leading+=(float)(extra/2);units[i].Trailing+=(float)(extra/2);}
        using var reference=Shape();Assert.NotNull(reference);var native=Assert.Single(reference.Lines);
        double origin=8+(count==0?(444-naturalWidth)/2:0);
        var line=Assert.Single(actual.GetDocumentLines(460));double baseline=actual.GetFirstBaselineOffset(200)-8;
        Brush brush=actual.Foreground!;
        var expected=Pixels(dc=>dc.DrawText(new FormattedText(text,family,28){Foreground=brush,
            PlatformTextLine=native.ForSource(1,source.Length,brush is SolidColorBrush solid?solid.Color:Colors.White,brush.Opacity)
                .WithBrushLineBox(native.Metrics.Baseline-baseline,line.Height)},new(origin-native.Metrics.Line.X,line.Top+baseline-native.Metrics.Baseline)),context);
        Assert.Equal(expected,Pixels(actual,context));
        var boundaries=GraphemeClusters.GetBoundaries(source);
        for(int i=0;i+1<boundaries.Length;i++)
        {
            int first=boundaries[i],length=boundaries[i+1]-first;
            var rectangles=reference.Selection(0,first+1,length);var boxes=actual.DocumentTextBounds(first,length);
            Assert.NotEmpty(rectangles);Assert.NotEmpty(boxes);
            Assert.Equal(origin+rectangles.Min(r=>r.X)-native.Metrics.Line.X,boxes.Min(r=>r.Left),3);
            Assert.Equal(rectangles.Sum(r=>(double)r.Width),boxes.Sum(r=>r.Width),3);
            var box=boxes[0];Assert.InRange(actual.DocumentCharacterFromPoint(new(box.Left+box.Width*.2,box.Top+box.Height/2)),first,first+length);
        }
        if(count>0)Assert.Equal(452,actual.DocumentTextBounds(0,source.Length).Max(r=>r.Right),3);
        actual.SelectAll();Assert.Equal(source,actual.SelectedText);actual.ClearSelection();
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        actual.Text=source+"Q";Layout(actual);
        Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));Assert.NotEqual(expected,Pixels(actual,context));
    }

    [Theory]
    [InlineData(false,false)] [InlineData(true,false)]
    [InlineData(false,true)] [InlineData(true,true)]
    public void AuthoredBidiJustificationPreservesWrappedAndForcedLineRules(bool rtl,bool last)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="AB CD EF GH\nIJ KL";
        var actual=Make(source);var natural=Make(source);
        string common=$"font-size:28px;line-height:52px;white-space:pre-wrap;direction:{(rtl?"rtl":"ltr")};unicode-bidi:bidi-override;text-justify:inter-word;";
        foreach(int width in new[]{170,300,170})
        {
            Css.SetStyle(actual,common+"text-align:justify;text-align-last:"+(last?"justify":"auto")+";width:"+width+"px");
            Css.SetStyle(natural,common+"text-align:start;text-align-last:start;width:"+width+"px");Layout(actual,width);Layout(natural,width);
            var lines=actual.GetDocumentLines(width);var reference=natural.GetDocumentLines(width);
            Assert.Equal(reference.Select(l=>(l.Start,l.End)),lines.Select(l=>(l.Start,l.End)));
            var beforeQueries=Pixels(actual,context);
            for(int row=0;row<lines.Count;row++)
            {
                var line=lines[row];int end=line.End;while(end>line.Start&&char.IsWhiteSpace(source[end-1]))end--;
                bool forced=line.End<source.Length&&source[line.End]=='\n';
                bool canExpand=source.AsSpan(line.Start,end-line.Start).IndexOf(' ')>=0;
                bool expand=canExpand&&(last||row+1<lines.Count&&!forced);
                var boxes=actual.DocumentTextBounds(line.Start,end-line.Start);Assert.NotEmpty(boxes);
                if(expand){Assert.Equal(8,boxes.Min(r=>r.Left),3);Assert.Equal(width-8,boxes.Max(r=>r.Right),3);}
                else
                {
                    var original=natural.DocumentTextBounds(line.Start,end-line.Start);
                    if(last&&!canExpand)
                    {
                        double used=original.Max(r=>r.Right)-original.Min(r=>r.Left);
                        Assert.Equal(8+(width-16-used)/2,boxes.Min(r=>r.Left),3);
                        Assert.Equal(used,boxes.Max(r=>r.Right)-boxes.Min(r=>r.Left),3);
                    }
                    else{Assert.Equal(original.Min(r=>r.Left),boxes.Min(r=>r.Left),3);Assert.Equal(original.Max(r=>r.Right),boxes.Max(r=>r.Right),3);}
                }
                for(int i=line.Start;i<end;i++)if(source[i]!=' ')
                {var box=Assert.Single(actual.DocumentTextBounds(i,1));Assert.InRange(actual.DocumentCharacterFromPoint(new(box.Left+box.Width*.2,box.Top+box.Height/2)),i,i+1);}
            }
            Assert.Equal(beforeQueries,Pixels(actual,context));
        }
        var expected=Pixels(actual,context);var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        actual.Text=source.Replace('A','Z');Layout(actual,170);Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));Assert.NotEqual(expected,Pixels(actual,context));
    }

    [Theory]
    [InlineData(false,"0",12)] [InlineData(true,"0",12)]
    [InlineData(false,"80px",0)] [InlineData(true,"80px",0)]
    [InlineData(false,"4",-12)] [InlineData(true,"4",-12)]
    public void AuthoredBidiJustificationKeepsTabFieldsAnchoredBeforeExpandingTheSuffix(bool rtl,string tab,int indent)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="AB CD\tEF GH";var actual=Make(source);var natural=Make(source);
        string common=$"font-size:28px;line-height:64px;white-space:pre;tab-size:{tab};text-indent:{indent}px;direction:{(rtl?"rtl":"ltr")};unicode-bidi:bidi-override;text-justify:inter-word;";
        Css.SetStyle(actual,common+"text-align:justify;text-align-last:justify");Css.SetStyle(natural,common+"text-align:start;text-align-last:start");Layout(actual);Layout(natural);
        var naturalBoxes=natural.DocumentTextBounds(0,source.Length);double naturalRight=naturalBoxes.Max(r=>r.Right);var actualBoxes=actual.DocumentTextBounds(0,source.Length);double right=actualBoxes.Max(r=>r.Right);
        for(int i=0;i<5;i++)
        {
            var before=Assert.Single(natural.DocumentTextBounds(i,1));var after=Assert.Single(actual.DocumentTextBounds(i,1));
            Assert.Equal(rtl?naturalRight-before.Left:before.Left,rtl?right-after.Left:after.Left,3);Assert.Equal(before.Width,after.Width,3);
        }
        Assert.Equal(452-(rtl?indent:0),right,3);Assert.Equal(8+(rtl?0:indent),actualBoxes.Min(r=>r.Left),3);
        var expected=Pixels(actual,context);var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        Css.SetStyle(actual,common+"text-align:start;text-align-last:start");Layout(actual);
        Assert.Equal(Pixels(natural,context),Pixels(actual,context));
        Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));
    }

    [Theory]
    [InlineData(false,false)] [InlineData(true,false)] [InlineData(false,true)] [InlineData(true,true)]
    public void AuthoredBidiJustificationKeepsMixedFontAndBrushSnapshots(bool rtl,bool gradient)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="AB CD EF";var actual=Make("");
        var first=new Run("AB "){FontSize=28};var second=new Run("CD EF"){FontFamily=new("Times"),FontSize=18,FontWeight=FontWeights.Bold,FontStyle=FontStyles.Italic};
        actual.Inlines.Add(first);actual.Inlines.Add(second);
        Css.SetStyle(actual,$"font-size:28px;line-height:64px;direction:{(rtl?"rtl":"ltr")};unicode-bidi:bidi-override;text-align:justify;text-align-last:justify;text-justify:inter-word");
        if(gradient)actual.Foreground=new RadialGradientBrush(Colors.Red,Colors.Blue){Opacity=.65};Layout(actual);
        string open=rtl?"\u202E":"\u202D",text=open+source+"\u202C",family=actual.FontFamily.GetRenderingSource(actual);
        NativeTextParagraph.Span[] spans=[new(open+first.Text,first.FontFamily.GetRenderingSource(first),28,400,0,Colors.Black),new(second.Text+"\u202C",second.FontFamily.GetRenderingSource(second),18,700,1,Colors.Black)];
        var units=new CssInputTextSpacing(0,0).Units(text);
        NativeTextParagraph? Shape()=>NativeTextParagraph.TryCreateAdvanced(spans,family,28,10000000,1,actual.FlowDirection,units,contextPaint:true,textOptionsOwner:actual);
        using var natural=Shape();Assert.NotNull(natural);double extra=(444-Assert.Single(natural.Lines).Metrics.Line.Width)/2;
        foreach(int index in new[]{3,6}){units[index].Leading+=(float)(extra/2);units[index].Trailing+=(float)(extra/2);}
        using var reference=Shape();Assert.NotNull(reference);var native=Assert.Single(reference.Lines);
        var expected=BidiJustificationReferencePixels(actual,reference,1,source.Length,context);Assert.Equal(expected,Pixels(actual,context));
        for(int i=0;i<source.Length;i++)
        {
            var rects=reference.Selection(0,i+1,1);var boxes=actual.DocumentTextBounds(i,1);Assert.NotEmpty(rects);Assert.NotEmpty(boxes);
            Assert.Equal(8+rects.Min(r=>r.X)-native.Metrics.Line.X,boxes.Min(r=>r.Left),3);Assert.Equal(rects.Sum(r=>(double)r.Width),boxes.Sum(r=>r.Width),3);
        }
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        second.FontSize=24;first.Text="AZ ";if(actual.Foreground is RadialGradientBrush brush)brush.GradientStops[0].Color=Colors.Green;Layout(actual);
        Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));Assert.NotEqual(expected,Pixels(actual,context));
        actual.SelectAll();Assert.Equal("AZ CD EF",actual.SelectedText);
    }

    [Theory]
    [InlineData(false,false)] [InlineData(true,false)] [InlineData(false,true)] [InlineData(true,true)]
    public void AuthoredBidiJustificationHonorsScopedMethodAndIsolateOwnership(bool rtl,bool disabled)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="X AB CD EF GH";var actual=Make("");var prefix=new Run("X ");var middle=new Run("AB CD");var suffix=new Run(" EF GH");var scope=new Span();scope.Inlines.Add(middle);
        actual.Inlines.Add(prefix);actual.Inlines.Add(scope);actual.Inlines.Add(suffix);
        Css.SetStyle(actual,$"font-size:28px;line-height:64px;direction:{(rtl?"rtl":"ltr")};unicode-bidi:normal;text-align:justify;text-align-last:justify;text-justify:inter-word");
        Css.SetStyle(scope,"unicode-bidi:isolate-override;direction:rtl;text-justify:"+(disabled?"none":"inter-word"));
        actual.Foreground=new LinearGradientBrush(Colors.Red,Colors.Blue,new(0,0),new(1,0)){Opacity=.65};Layout(actual);
        string text="X \u2068\u202EAB CD\u202C\u2069 EF GH",family=actual.FontFamily.GetRenderingSource(actual);var units=new CssInputTextSpacing(0,0).Units(text);
        NativeTextParagraph? Shape()=>NativeTextParagraph.TryCreateAdvanced([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,actual.FlowDirection,units,contextPaint:true,textOptionsOwner:actual);
        using var natural=Shape();Assert.NotNull(natural);double extra=(444-Assert.Single(natural.Lines).Metrics.Line.Width)/(disabled?3:4);
        foreach(int index in disabled?new[]{1,11,14}:new[]{1,6,11,14}){units[index].Leading+=(float)(extra/2);units[index].Trailing+=(float)(extra/2);}
        using var reference=Shape();Assert.NotNull(reference);var native=Assert.Single(reference.Lines);
        var expected=BidiJustificationReferencePixels(actual,reference,0,text.Length,context);Assert.Equal(expected,Pixels(actual,context));
        int[] map=[0,1,4,5,6,7,8,11,12,13,14,15,16];
        for(int i=0;i<source.Length;i++)
        {
            var rects=reference.Selection(0,map[i],1);var box=Assert.Single(actual.DocumentTextBounds(i,1));Assert.NotEmpty(rects);
            Assert.Equal(8+rects.Min(r=>r.X)-native.Metrics.Line.X,box.Left,3);Assert.Equal(rects.Sum(r=>(double)r.Width),box.Width,3);
            Assert.InRange(actual.DocumentCharacterFromPoint(new(box.Left+box.Width*.2,box.Top+box.Height/2)),i,i+1);
        }
        var parent=scope.Parent;var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        Css.SetStyle(scope,"unicode-bidi:embed;direction:ltr;text-justify:inter-word");Layout(actual);
        Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));Assert.NotEqual(expected,Pixels(actual,context));Assert.Same(parent,scope.Parent);
        actual.SelectAll();Assert.Equal(source,actual.SelectedText);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AuthoredBidiJustificationRefreshesEqualValueNativeAlignmentOverrides(bool rtl)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="AB CD EF";var actual=Make(source);var natural=Make(source);
        string common=$"font-size:28px;line-height:64px;direction:{(rtl?"rtl":"ltr")};unicode-bidi:bidi-override;text-justify:inter-word;";
        Css.SetStyle(actual,common+"text-align:justify;text-align-last:justify");Css.SetStyle(natural,common+"text-align:left;text-align-last:left");Layout(actual);Layout(natural);
        var expected=Pixels(actual,context);Assert.Equal(452,actual.DocumentTextBounds(0,source.Length).Max(r=>r.Right),3);
        for(int cycle=0;cycle<2;cycle++)
        {
            // Left is already the default native DP value; changing its value
            // source still overrides the CSS alignment and retained advances.
            actual.TextAlignment=TextAlignment.Left;Layout(actual);
            Assert.Equal(Pixels(natural,context),Pixels(actual,context));
            actual.ClearValue(TextBlock.TextAlignmentProperty);Layout(actual);
            Assert.Equal(expected,Pixels(actual,context));
        }
    }

    [Theory]
    [InlineData(false,1)]
    [InlineData(false,2)]
    [InlineData(false,8)]
    [InlineData(true,1)]
    [InlineData(true,2)]
    [InlineData(true,8)]
    public void AuthoredBidiJustificationPreservesHangingSpaces(bool rtl,int trailing)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string visible="AB CD EF";string source=visible+new string(' ',trailing);
        var actual=Make(source);
        Css.SetStyle(actual,$"font-size:28px;line-height:64px;white-space:pre-wrap;direction:{(rtl?"rtl":"ltr")};unicode-bidi:bidi-override;text-align:justify;text-align-last:justify;text-justify:inter-word");
        Layout(actual);string family=actual.FontFamily.GetRenderingSource(actual),text=(rtl?"\u202E":"\u202D")+source+"\u202C";
        var units=new CssInputTextSpacing(0,0).Units(text);
        NativeTextParagraph? Shape()=>NativeTextParagraph.TryCreateAdvanced([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,actual.FlowDirection,units,contextPaint:true,textOptionsOwner:actual);
        using var natural=Shape();Assert.NotNull(natural);var naturalBoxes=natural.Selection(0,1,visible.Length);
        double width=naturalBoxes.Max(b=>b.X+b.Width)-naturalBoxes.Min(b=>b.X),extra=(444-width)/2;
        foreach(int position in new[]{3,6})
        {
            int at=Array.FindIndex(units,unit=>unit.TextPosition==position);
            units[at].Leading+=(float)(extra/2);units[at].Trailing+=(float)(extra/2);
        }
        using var reference=Shape();Assert.NotNull(reference);var native=Assert.Single(reference.Lines);
        var boxes=reference.Selection(0,1,visible.Length);double origin=8-boxes.Min(b=>b.X);
        var line=Assert.Single(actual.GetDocumentLines(460));double baseline=actual.GetFirstBaselineOffset(200)-8;var brush=actual.Foreground!;
        var expected=Pixels(dc=>dc.DrawText(new FormattedText(text,family,28){Foreground=brush,
            PlatformTextLine=native.ForSource(1,source.Length,((SolidColorBrush)brush).Color,brush.Opacity)
                .WithBrushLineBox(native.Metrics.Baseline-baseline,line.Height)},new(origin,line.Top+baseline-native.Metrics.Baseline)),context);
        Assert.Equal(expected,Pixels(actual,context));
        for(int index=0;index<source.Length;index++)
        {
            var expectedBoxes=reference.Selection(0,index+1,1);var actualBoxes=actual.DocumentTextBounds(index,1);Assert.NotEmpty(actualBoxes);Assert.NotEmpty(expectedBoxes);
            Assert.Equal(origin+expectedBoxes.Min(b=>b.X),actualBoxes.Min(b=>b.Left),3);
            Assert.Equal(expectedBoxes.Sum(b=>(double)b.Width),actualBoxes.Sum(b=>b.Width),3);
        }
        Assert.Equal(8,actual.DocumentTextBounds(0,visible.Length).Min(b=>b.Left),3);
        Assert.Equal(452,actual.DocumentTextBounds(0,visible.Length).Max(b=>b.Right),3);
        actual.SelectAll();Assert.Equal(source,actual.SelectedText);actual.ClearSelection();
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        actual.Text=visible;Layout(actual);Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));
    }

    public static IEnumerable<object[]> BidiAutospaceCases()
    {
        foreach(bool rtl in new[]{false,true})
        foreach(string scene in new[]{"letters","digits","alpha-digits","numeric-letters","wide","grapheme","format","mixed","invalid",
            "space-insert","space-replace","nbsp-replace","punct-fr","punct-en","quotes-fr","tracking","negative"})
            yield return new object[]{rtl,scene};
    }

    [Theory]
    [MemberData(nameof(BidiAutospaceCases))]
    public void AuthoredBidiAutospaceMatchesIndependentParagraphAdvances(bool rtl,string scene)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        string source=scene switch
        {
            "digits" or "alpha-digits"=>"中12文","wide"=>"中Ａ１文","grapheme"=>"中A\u0301🙂B文",
            "format"=>"中\u2060ABC文","mixed"=>"中ABאב♥文","invalid"=>"中\uD800A文","space-insert" or "space-replace" or "tracking" or "negative"=>"中 ABC 文",
            "nbsp-replace"=>"中\u00A0ABC\u202F文","punct-fr" or "punct-en"=>"A!B:C?D","quotes-fr"=>"«AB»",_=>"中ABC文"
        };
        string mode=scene switch
        {
            "alpha-digits"=>"ideograph-alpha","numeric-letters"=>"ideograph-numeric",
            "space-replace" or "tracking" or "negative" or "nbsp-replace"=>"ideograph-alpha ideograph-numeric replace",
            "punct-fr" or "punct-en" or "quotes-fr"=>"punctuation",_=>"normal"
        };
        int letter=scene=="tracking"?2:scene=="negative"?-1:0,word=scene is "tracking" or "negative"?3:0;
        var actual=Make(source);
        if(scene.EndsWith("-fr"))actual.Language=Jalium.UI.Markup.XmlLanguage.GetLanguage("fr-FR");
        string css=$"font-size:28px;line-height:64px;white-space:pre-wrap;direction:{(rtl?"rtl":"ltr")};unicode-bidi:bidi-override;text-align:left;text-autospace:{mode};letter-spacing:{letter}px;word-spacing:{word}px";
        Css.SetStyle(actual,css);
        Assert.Equal(mode.Contains("replace",StringComparison.Ordinal)?CssTextAutospace.Normal|CssTextAutospace.Replace:
            mode=="punctuation"?CssTextAutospace.Punctuation:mode=="ideograph-alpha"?CssTextAutospace.IdeographAlpha:
            mode=="ideograph-numeric"?CssTextAutospace.IdeographNumeric:CssTextAutospace.Normal,
            (CssTextAutospace)actual.GetValue(CssFlowProperties.TextAutospaceProperty)!);
        if(scene is "letters" or "grapheme" or "tracking" or "space-replace")actual.Foreground=new LinearGradientBrush(Colors.Red,Colors.Blue,new(0,0),new(1,0)){Opacity=.65};
        Layout(actual);string family=actual.FontFamily.GetRenderingSource(actual),text=(rtl?"\u202E":"\u202D")+source+"\u202C";
        var units=new CssInputTextSpacing(letter,word).Units(text);
        using var natural=NativeTextParagraph.TryCreate([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,actual.FlowDirection,noWrap:true,textOptionsOwner:actual);
        Assert.NotNull(natural);double ic=CssTextDecorationPainter.GetSourceFontMetrics(actual,28).IdeographicAdvance/8;
        double Space(char value)
        {
            using var space=NativeTextParagraph.TryCreate([new(value.ToString(),family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,actual.FlowDirection,noWrap:true,textOptionsOwner:actual);
            Assert.NotNull(space);return Assert.Single(space.Lines).Metrics.Line.Width;
        }
        void Gap(int a,int b,double width)
        {
            int left=Array.FindIndex(units,u=>u.TextPosition==a+1),right=Array.FindIndex(units,u=>u.TextPosition==b+1);
            Assert.True(left>=0&&right>=0);units[left].Trailing+=(float)(width/2);units[right].Leading+=(float)(width/2);
        }
        if(scene is "letters" or "digits") { Gap(0,1,ic);Gap(source.Length-2,source.Length-1,ic); }
        if(scene=="grapheme") { Gap(0,1,ic);Gap(5,6,ic); }
        if(scene=="format") { Gap(0,2,ic);Gap(4,5,ic); }
        if(scene=="mixed")Gap(0,1,ic);
        if(scene=="invalid")Gap(2,3,ic);
        if(scene is "space-replace" or "tracking" or "negative")
            foreach(int position in new[]{1,5})
            {
                int at=Array.FindIndex(units,u=>u.TextPosition==position+1);
                double old=natural.Selection(0,position+1,1).Sum(rect=>(double)rect.Width);
                units[at].Leading+=(float)((ic-old)/2);units[at].Trailing+=(float)((ic-old)/2);units[at].Flags&=~4u;
            }
        if(scene=="punct-fr")
            foreach(int punctuation in new[]{1,3,5})Gap(rtl?punctuation:punctuation-1,rtl?punctuation+1:punctuation,Space(punctuation==3?'\u00A0':'\u202F'));
        if(scene=="quotes-fr"){Gap(0,1,Space('\u00A0'));Gap(2,3,Space('\u00A0'));}
        NativeTextParagraph? Shape(NativeMethods.ParagraphAdvanceUnit[] plan)=>NativeTextParagraph.TryCreateAdvanced([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,actual.FlowDirection,plan,contextPaint:true,textOptionsOwner:actual);
        using var reference=Shape(units);Assert.NotNull(reference);
        var expected=BidiAutospaceReferencePixels(actual,reference,source.Length,context);Assert.Equal(expected,Pixels(actual,context));
        var boundaries=GraphemeClusters.GetBoundaries(source);
        for(int i=0;i+1<boundaries.Length;i++)
        {
            int first=boundaries[i],length=boundaries[i+1]-first;
            var rects=reference.Selection(0,first+1,length);if(rects.Length==0||rects.All(rect=>rect.Width<.0001))continue;
            var boxes=actual.DocumentTextBounds(first,length);Assert.NotEmpty(boxes);
            Assert.Equal(8+rects.Min(rect=>rect.X)-reference.Lines[0].Metrics.Line.X,boxes.Min(box=>box.Left),3);
            Assert.Equal(rects.Sum(rect=>(double)rect.Width),boxes.Sum(box=>box.Width),3);
            var box=boxes[0];Assert.InRange(actual.DocumentCharacterFromPoint(new(box.Left+box.Width*.2,box.Top+box.Height/2)),first,first+length);
        }
        actual.SelectAll();Assert.Equal(source,actual.SelectedText);actual.ClearSelection();
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        Css.SetStyle(actual,css+";text-autospace:no-autospace");Layout(actual);
        using var unspaced=Shape(new CssInputTextSpacing(letter,word).Units(text));Assert.NotNull(unspaced);
        Assert.Equal(BidiAutospaceReferencePixels(actual,unspaced,source.Length,context),Pixels(actual,context));
        actual.Text=source+"XYZ";Layout(actual);Assert.NotEqual(expected,Pixels(actual,context));
        Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public void AuthoredBidiAutospaceRecomputesOnlyUsedRowBoundaries(bool rtl,bool replace=false)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        string source=replace?"中 A 文 B 中 C 文":"中A文B中C文";var actual=Make(source);
        string css=$"font-size:28px;line-height:32px;white-space:pre-wrap;direction:{(rtl?"rtl":"ltr")};unicode-bidi:bidi-override;text-align:left;text-autospace:{(replace?"ideograph-alpha ideograph-numeric replace":"normal")}";
        Css.SetStyle(actual,css);
        actual.Foreground=new LinearGradientBrush(Colors.Red,Colors.Blue,new(0,0),new(1,0)){Opacity=.65};
        byte[] expected=[];
        foreach(int width in new[]{70,140,70})
        {
            Css.SetStyle(actual,css+";width:"+width+"px");Layout(actual,width);Assert.Equal(width,actual.ActualWidth,3);
            int[] breaks=width==70?replace?[4,8,12]:[2,4,6]:replace?[8]:[4];var map=new int[source.Length];
            var builder=new System.Text.StringBuilder(rtl?"\u202E":"\u202D");
            for(int i=0;i<source.Length;i++){if(breaks.Contains(i))builder.Append('\u2028');map[i]=builder.Length;builder.Append(source[i]);}builder.Append('\u202C');
            string text=builder.ToString(),family=actual.FontFamily.GetRenderingSource(actual);var units=new CssInputTextSpacing(0,0).Units(text);
            for(int i=0;i<units.Length;i++)if(text[(int)units[i].TextPosition]=='\u2028')units[i].Flags=1;
            double gap=CssTextDecorationPainter.GetSourceFontMetrics(actual,28).IdeographicAdvance/8;
            if(replace)
            {
                using var original=NativeTextParagraph.TryCreate([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,actual.FlowDirection,noWrap:true,textOptionsOwner:actual);Assert.NotNull(original);
                for(int i=0;i<source.Length;i++)if(source[i]==' ')
                {
                    uint row=(uint)breaks.Count(boundary=>boundary<=i);
                    double old=original.Selection(row,map[i],1).Sum(rect=>(double)rect.Width);
                    double used=breaks.Contains(i+1)?0:gap;int at=Array.FindIndex(units,u=>u.TextPosition==map[i]);
                    units[at].Leading+=(float)((used-old)/2);units[at].Trailing+=(float)((used-old)/2);units[at].Flags&=~4u;
                }
            }
            else for(int i=0;i+1<source.Length;i++)if(!breaks.Contains(i+1))
            {
                int a=Array.FindIndex(units,u=>u.TextPosition==map[i]),b=Array.FindIndex(units,u=>u.TextPosition==map[i+1]);
                units[a].Trailing+=(float)(gap/2);units[b].Leading+=(float)(gap/2);
            }
            using var reference=NativeTextParagraph.TryCreateAdvanced([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,actual.FlowDirection,units,contextPaint:true,textOptionsOwner:actual);
            Assert.NotNull(reference);var lines=actual.GetDocumentLines(width);Assert.Equal(breaks.Append(source.Length),lines.Select(l=>l.End));Assert.Equal(lines.Count,reference.Lines.Length);
            double baseline=actual.GetFirstBaselineOffset(200)-8;var brush=actual.Foreground!;
            expected=Pixels(dc=>
            {
                for(int row=0;row<lines.Count;row++)
                {
                    var native=reference.Lines[row];
                    dc.DrawText(new FormattedText(text,family,28){Foreground=brush,PlatformTextLine=native.ForSource((int)native.Metrics.Line.TextPosition,(int)native.Metrics.Line.Length,Colors.White,brush.Opacity).WithBrushLineBox(native.Metrics.Baseline-baseline,lines[row].Height)},new(8-native.Metrics.Line.X,lines[row].Top+baseline-native.Metrics.Baseline));
                }
            },context);
            Assert.True(expected.Where((value,index)=>index%4==3).Any(value=>value>0));
            Assert.Equal(expected,Pixels(actual,context));
            for(int i=0;i<source.Length;i++)
            {
                uint row=(uint)Array.FindIndex(lines.ToArray(),line=>i>=line.Start&&i<line.End);var rects=reference.Selection(row,map[i],1);var boxes=actual.DocumentTextBounds(i,1);
                if(rects.Length==0||rects.All(rect=>rect.Width<.000001)){Assert.All(boxes,box=>Assert.InRange(box.Width,0,.001));continue;}
                Assert.NotEmpty(boxes);
                Assert.Equal(8+rects.Min(r=>r.X)-reference.Lines[row].Metrics.Line.X,boxes.Min(b=>b.Left),3);
            }
        }
        actual.SelectAll();Assert.Equal(source,actual.SelectedText);actual.ClearSelection();
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        actual.Text=source+"X";Layout(actual,70);Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));
    }

    [Theory]
    [InlineData(false,"0")]
    [InlineData(true,"0")]
    [InlineData(false,"4")]
    [InlineData(true,"4")]
    [InlineData(false,"80px")]
    [InlineData(true,"80px")]
    public void AuthoredBidiAutospaceKeepsIndependentTabStops(bool rtl,string tabSize)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="中A\t文B中C";var actual=Make(source);
        Css.SetStyle(actual,$"font-size:28px;line-height:64px;white-space:pre-wrap;tab-size:{tabSize};direction:{(rtl?"rtl":"ltr")};unicode-bidi:bidi-override;text-align:left;text-autospace:normal");Layout(actual);
        string text=(rtl?"\u202E":"\u202D")+source+"\u202C",family=actual.FontFamily.GetRenderingSource(actual);
        using var natural=NativeTextParagraph.TryCreate([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,actual.FlowDirection,noWrap:true,textOptionsOwner:actual);
        using var ruler=NativeTextParagraph.TryCreate([new("0 ",family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,FlowDirection.LeftToRight,noWrap:true,textOptionsOwner:actual);
        Assert.NotNull(natural);Assert.NotNull(ruler);double gap=CssTextDecorationPainter.GetSourceFontMetrics(actual,28).IdeographicAdvance/8;
        double prefix=natural.Selection(0,1,2).Sum(r=>(double)r.Width)+gap;
        double step=tabSize.EndsWith("px",StringComparison.Ordinal)?80:double.Parse(tabSize,System.Globalization.CultureInfo.InvariantCulture)*ruler.Selection(0,1,1).Sum(r=>(double)r.Width);
        double advance=step==0?0:(Math.Floor(prefix/step)+1)*step-prefix;
        if(step>0&&advance<ruler.Selection(0,0,1).Sum(r=>(double)r.Width)/2)advance+=step;
        var units=new CssInputTextSpacing(0,0).Units(text);
        foreach(int i in new[]{0,3,4,5})
        {
            int a=Array.FindIndex(units,u=>u.TextPosition==i+1),b=Array.FindIndex(units,u=>u.TextPosition==i+2);
            units[a].Trailing+=(float)(gap/2);units[b].Leading+=(float)(gap/2);
        }
        int tab=Array.FindIndex(units,u=>u.TextPosition==3);units[tab].Flags=0;units[tab].Trailing=(float)(advance-natural.Selection(0,3,1).Sum(r=>(double)r.Width));
        using var reference=NativeTextParagraph.TryCreateAdvanced([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,actual.FlowDirection,units,contextPaint:true,textOptionsOwner:actual);
        Assert.NotNull(reference);var expected=BidiAutospaceReferencePixels(actual,reference,source.Length,context);Assert.Equal(expected,Pixels(actual,context));
        Assert.Equal(advance,actual.DocumentTextBounds(2,1).Sum(r=>r.Width),3);
        for(int i=0;i<source.Length;i++)if(i!=2)
        {
            var rects=reference.Selection(0,i+1,1);var boxes=actual.DocumentTextBounds(i,1);Assert.NotEmpty(boxes);
            Assert.Equal(8+rects.Min(r=>r.X)-reference.Lines[0].Metrics.Line.X,boxes.Min(b=>b.Left),3);
        }
        actual.SelectAll();Assert.Equal(source,actual.SelectedText);actual.ClearSelection();
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        actual.Text=source+"X";Layout(actual);Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public void AuthoredBidiAutospaceUsesCommonOwnersAcrossFontIsolates(bool rtl,bool disabled)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var actual=Make("");var first=new Run("中");var scope=new Span{FontFamily=new FontFamily("Times"),FontSize=18,FontWeight=FontWeights.Bold,FontStyle=FontStyles.Italic};
        var runs=new[]{new Run("A"),new Run("文"),new Run("B")};foreach(var run in runs)scope.Inlines.Add(run);
        actual.Inlines.Add(first);actual.Inlines.Add(scope);actual.Inlines.Add(new Run("中"));
        Css.SetStyle(actual,$"font-size:28px;line-height:64px;white-space:pre-wrap;direction:{(rtl?"rtl":"ltr")};unicode-bidi:bidi-override;text-align:left;text-autospace:normal");
        Css.SetStyle(scope,"unicode-bidi:isolate-override;direction:rtl;text-autospace:"+(disabled?"no-autospace":"normal"));
        actual.Foreground=new LinearGradientBrush(Colors.Red,Colors.Blue,new(0,0),new(1,0)){Opacity=.65};Layout(actual);
        string text=(rtl?"\u202E":"\u202D")+"中\u2068\u202EA文B\u202C\u2069中\u202C";
        string family=actual.FontFamily.GetRenderingSource(actual),small=scope.FontFamily.GetRenderingSource(scope);
        NativeTextParagraph.Span[] spans=[new(text[..2],family,28,400,0,Colors.Black),new(text[2..9],small,18,700,2,Colors.Black),new(text[9..],family,28,400,0,Colors.Black)];
        using var natural=NativeTextParagraph.TryCreate(spans,family,28,10000000,1,TextAlignment.Left,actual.FlowDirection,noWrap:true,textOptionsOwner:actual);Assert.NotNull(natural);
        int[] map=[1,4,5,6,9];var units=new CssInputTextSpacing(0,0).Units(text);
        void Gap(int left,int right,double width)
        {
            int a=Array.FindIndex(units,u=>u.TextPosition==map[left]),b=Array.FindIndex(units,u=>u.TextPosition==map[right]);
            bool ar=natural.Caret(0,map[left],false).X>natural.Caret(0,map[left]+1,true).X,br=natural.Caret(0,map[right],false).X>natural.Caret(0,map[right]+1,true).X;
            if(ar)units[a].Leading+=(float)(width/2);else units[a].Trailing+=(float)(width/2);
            if(br)units[b].Trailing+=(float)(width/2);else units[b].Leading+=(float)(width/2);
        }
        double root=CssTextDecorationPainter.GetSourceFontMetrics(actual,28).IdeographicAdvance/8,inner=CssTextDecorationPainter.GetSourceFontMetrics(scope,28).IdeographicAdvance/8;
        if(rtl){Gap(4,3,root);Gap(1,0,root);}else{Gap(0,3,root);Gap(1,4,root);}
        if(!disabled){Gap(3,2,inner);Gap(2,1,inner);}
        using var reference=NativeTextParagraph.TryCreateAdvanced(spans,family,28,10000000,1,actual.FlowDirection,units,contextPaint:true,textOptionsOwner:actual);Assert.NotNull(reference);
        var expected=BidiAutospaceReferencePixels(actual,reference,text.Length,context,0);Assert.Equal(expected,Pixels(actual,context));
        for(int i=0;i<map.Length;i++)
        {
            var rects=reference.Selection(0,map[i],1);var boxes=actual.DocumentTextBounds(i,1);Assert.NotEmpty(boxes);
            Assert.Equal(8+rects.Min(r=>r.X)-reference.Lines[0].Metrics.Line.X,boxes.Min(b=>b.Left),3);
            Assert.Equal(rects.Sum(r=>(double)r.Width),boxes.Sum(b=>b.Width),3);
        }
        actual.SelectAll();Assert.Equal("中A文B中",actual.SelectedText);actual.ClearSelection();
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        runs[0].Text="Z";scope.FontSize=24;Layout(actual);Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public void AuthoredBidiAutospaceComposesWithIndependentJustification(bool rtl,bool characters)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        string source=characters?"中A文B":"中A 文B 中C";var actual=Make(source);
        Css.SetStyle(actual,$"font-size:28px;line-height:64px;white-space:pre-wrap;direction:{(rtl?"rtl":"ltr")};unicode-bidi:bidi-override;text-align:justify;text-align-last:justify;text-justify:{(characters?"inter-character":"inter-word")};text-autospace:normal");
        actual.Foreground=new LinearGradientBrush(Colors.Red,Colors.Blue,new(0,0),new(1,0)){Opacity=.65};Layout(actual);
        string text=(rtl?"\u202E":"\u202D")+source+"\u202C",family=actual.FontFamily.GetRenderingSource(actual);
        var units=new CssInputTextSpacing(0,0).Units(text);double gap=CssTextDecorationPainter.GetSourceFontMetrics(actual,28).IdeographicAdvance/8;
        void Add(int index,double width)
        {
            int a=Array.FindIndex(units,u=>u.TextPosition==index+1),b=Array.FindIndex(units,u=>u.TextPosition==index+2);
            units[a].Trailing+=(float)(width/2);units[b].Leading+=(float)(width/2);
        }
        foreach(int i in characters?new[]{0,1,2}:new[]{0,3,6})Add(i,gap);
        NativeTextParagraph? Shape()=>NativeTextParagraph.TryCreateAdvanced([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,actual.FlowDirection,units,contextPaint:true,textOptionsOwner:actual);
        using var natural=Shape();Assert.NotNull(natural);double extra=(444-Assert.Single(natural.Lines).Metrics.Line.Width)/(characters?3:2);
        if(characters)for(int i=0;i<3;i++)Add(i,extra);
        else foreach(int index in new[]{2,5})
        {
            int at=Array.FindIndex(units,u=>u.TextPosition==index+1);units[at].Leading+=(float)(extra/2);units[at].Trailing+=(float)(extra/2);
        }
        using var reference=Shape();Assert.NotNull(reference);var expected=BidiAutospaceReferencePixels(actual,reference,source.Length,context);
        Assert.Equal(expected,Pixels(actual,context));Assert.Equal(452,actual.DocumentTextBounds(0,source.Length).Max(b=>b.Right),3);
        for(int i=0;i<source.Length;i++)
        {
            var boxes=actual.DocumentTextBounds(i,1);var rects=reference.Selection(0,i+1,1);Assert.NotEmpty(boxes);
            Assert.Equal(8+rects.Min(r=>r.X)-reference.Lines[0].Metrics.Line.X,boxes.Min(b=>b.Left),3);
        }
        actual.SelectAll();Assert.Equal(source,actual.SelectedText);actual.ClearSelection();
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        actual.Text=source+"X";Layout(actual);Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));
    }

    public static IEnumerable<object[]> PlaintextAlignmentCases()
    {
        foreach(bool rtl in new[]{false,true})
        {
            foreach(string align in new[]{"start","end","left","right","center"})
            {yield return [rtl,"basic",align];yield return [rtl,"overflow",align];}
            foreach(string scene in new[]{"weak","empty","isolate","indent","hanging","spacing","padding"})
                foreach(string align in new[]{"start","end"})yield return [rtl,scene,align];
            foreach(string align in new[]{"start","end","justify"})yield return [rtl,"last",align];
            foreach(string align in new[]{"left","right"})yield return [rtl,"native",align];
            yield return [rtl,"default","start"];
            foreach(string align in new[]{"start","end"})yield return [rtl,"overflow-clip",align];
        }
    }

    [Theory] [MemberData(nameof(PlaintextAlignmentCases))]
    public void PlaintextLineBoxesUseIndependentParagraphDirections(bool rtl,string scene,string align)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        string source=scene switch
        {
            "spacing"=>"中A B\nאב ABC中",
            "weak"=>"123\nאב\n!", "empty"=>"\nAB\n\n", "isolate"=>"אב AB\nאב",
            "overflow" or "overflow-clip"=>"אבגדABCD\nABCאבגד", "hanging"=>"\"אב\"\n\"CD\"", _=>"AB\nאב"
        };
        bool[] directions=scene switch
        {
            "weak"=>[rtl,true,true], "empty"=>[rtl,false,false,false],
            "overflow" or "overflow-clip" or "hanging"=>[true,false], _=>[false,true]
        };
        int width=scene.StartsWith("overflow")?80:460;
        var actual=Make(source,width);
        if(scene=="default")Css.GetStyleSheets(actual).Clear();
        string style=$"width:{width}px;height:200px;padding:8px;box-sizing:border-box;font-family:DejaVu Sans;font-size:28px;line-height:40px;white-space:pre;direction:{(rtl?"rtl":"ltr")};unicode-bidi:plaintext";
        style+=scene=="default"?"":scene=="last"?$";text-align:justify;text-align-last:{align};text-justify:none":$";text-align:{(scene=="native"?"end":align)}";
        if(scene=="indent")style+=";text-indent:24px each-line";
        if(scene=="spacing")style+=";text-autospace:normal;letter-spacing:2px;word-spacing:3px";
        if(scene=="hanging")style+=";hanging-punctuation:first last";
        if(scene=="overflow-clip")style+=";overflow:hidden";
        Css.SetStyle(actual,style);
        if(scene=="native")actual.TextAlignment=align=="left"?TextAlignment.Left:TextAlignment.Right;
        if(scene=="isolate")
        {
            actual.Text="";var isolated=new Span();isolated.Inlines.Add(new Run(source[..2]));
            actual.Inlines.Add(isolated);actual.Inlines.Add(new Run(source[2..]));
            Css.SetStyle(isolated,"unicode-bidi:plaintext;direction:rtl");
        }
        if(scene=="padding")
        {
            actual.Text="";
            foreach(var (part,padding) in new[]{("A",8),("B\nא",20),("ב",4)})
            {var run=new Run(part);actual.Inlines.Add(run);Css.SetStyle(run,$"line-padding:{padding}px");}
        }
        actual.Foreground=new LinearGradientBrush(Colors.Red,Colors.Blue,new(0,0),new(1,0)){Opacity=.65};Layout(actual,width);
        string text=scene=="isolate"?"\u2068"+source[..2]+"\u2069"+source[2..]:source;
        int Native(int index)=>scene=="isolate"?index+(index<2?1:2):index;
        string family=actual.FontFamily.GetRenderingSource(actual);
        NativeMethods.ParagraphAdvanceUnit[]? advances=null;
        if(scene=="spacing")
        {
            advances=new CssInputTextSpacing(2,3).Units(text);
            foreach(int first in new[]{0,10})
            {
                int left=Array.FindIndex(advances,unit=>unit.TextPosition==first),right=Array.FindIndex(advances,unit=>unit.TextPosition==first+1);
                double gap=CssTextDecorationPainter.GetSourceFontMetrics(actual,28).IdeographicAdvance/8;
                advances[left].Trailing+=(float)(gap/2);advances[right].Leading+=(float)(gap/2);
            }
            foreach(int index in Enumerable.Range(0,advances.Length))if(text[(int)advances[index].TextPosition]=='\n')advances[index].Flags=1;
        }
        using var reference=advances is null
            ?NativeTextParagraph.TryCreate([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,actual.FlowDirection,naturalDirection:true,noWrap:true,textOptionsOwner:actual)
            :NativeTextParagraph.TryCreateAdvanced([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,actual.FlowDirection,advances,contextPaint:true,naturalDirection:true,textOptionsOwner:actual);
        Assert.NotNull(reference);var lines=actual.GetDocumentLines(width);Assert.Equal(directions.Length,lines.Count);
        double baseline=actual.GetFirstBaselineOffset(200)-8;var offsets=new double[lines.Count];
        for(int row=0;row<lines.Count;row++)
        {
            var line=lines[row];bool hasText=line.End>line.Start;double physical=hasText?reference.Lines[row].Metrics.Line.Width:0;
            double leading=scene=="hanging"&&row==0?reference.Selection((uint)row,Native(line.Start),1).Sum(rect=>(double)rect.Width):0;
            double trailing=scene=="hanging"&&row==lines.Count-1?reference.Selection((uint)row,Native(line.End-1),1).Sum(rect=>(double)rect.Width):0;
            double left=scene=="padding"?(row==0?8:4):0,rightPad=scene=="padding"?20:0;
            double indent=scene=="indent"?24:0, used=physical-leading-trailing, free=width-16-indent-used-left-rightPad;
            string resolved=scene=="last"&&align=="justify"?"center":align;
            bool right=resolved=="right"||resolved=="start"&&directions[row]||resolved=="end"&&!directions[row];
            if(free<0&&scene!="native")right=directions[row];
            offsets[row]=8+left+(directions[row]?0:indent)+(resolved=="center"&&free>=0?free/2:right?free:0)-(directions[row]?trailing:leading);
        }
        byte[] ReferencePixels()=>Pixels(dc=>
        {
            if(scene=="overflow-clip")dc.PushClip(new RectangleGeometry(new Rect(0,0,width,200)));
            try
            {
            for(int row=0;row<lines.Count;row++)
            {
                var line=lines[row];if(line.End==line.Start)continue;var native=reference.Lines[row];
                int first=Native(line.Start),end=Native(line.End);
                dc.DrawText(new FormattedText(text,family,28){Foreground=actual.Foreground,
                    PlatformTextLine=native.ForSource(first,end-first,Colors.Black,1).WithBrushLineBox(native.Metrics.Baseline-baseline,line.Height)},
                    new(offsets[row]-native.Metrics.Line.X,line.Top+baseline-native.Metrics.Baseline));
            }
            }
            finally {if(scene=="overflow-clip")dc.Pop();}
        },context);
        var expected=ReferencePixels();
        Assert.Contains(expected.Where((_,i)=>(i&3)==3),value=>value>0);
        Assert.Equal(expected,Pixels(actual,context));
        for(int row=0;row<lines.Count;row++)
        {
            var line=lines[row];
            if(line.End==line.Start)
            {
                var caret=Assert.Single(actual.DocumentTextBounds(line.Start,0));Assert.Equal(offsets[row],caret.Left,3);continue;
            }
            var native=reference.Lines[row];
            for(int index=line.Start;index<line.End;index++)
            {
                var rectangles=reference.Selection((uint)row,Native(index),Native(index+1)-Native(index));
                var bounds=actual.DocumentTextBounds(index,1);Assert.NotEmpty(bounds);Assert.NotEmpty(rectangles);
                Assert.Equal(offsets[row]+rectangles.Min(rect=>rect.X)-native.Metrics.Line.X,bounds.Min(rect=>rect.Left),3);
                foreach(var box in bounds.Where(box=>box.Width>1))
                    foreach(double fraction in new[]{.2,.8})Assert.InRange(actual.DocumentCharacterFromPoint(new(box.Left+box.Width*fraction,box.Top+box.Height/2)),index,index+1);
            }
        }
        Assert.Equal(rtl?FlowDirection.RightToLeft:FlowDirection.LeftToRight,actual.FlowDirection);
        actual.SelectAll();Assert.Equal(source,actual.SelectedText);actual.ClearSelection();
        if(scene=="weak")
        {
            double saved=offsets[0],physical=reference.Lines[0].Metrics.Line.Width;
            Css.SetStyle(actual,style.Replace(rtl?"direction:rtl":"direction:ltr",rtl?"direction:ltr":"direction:rtl"));Layout(actual,width);
            bool right=align=="start"?!rtl:rtl;offsets[0]=8+(right?width-16-physical:0);
            Assert.Equal(ReferencePixels(),Pixels(actual,context));
            Css.SetStyle(actual,style);Layout(actual,width);offsets[0]=saved;Assert.Equal(expected,Pixels(actual,context));
        }
        if(scene=="native")
        {
            var saved=offsets.ToArray();actual.ClearValue(TextBlock.TextAlignmentProperty);Layout(actual,width);
            for(int row=0;row<lines.Count;row++)offsets[row]=8+(!directions[row]?width-16-reference.Lines[row].Metrics.Line.Width:0);
            Assert.Equal(ReferencePixels(),Pixels(actual,context));
            actual.TextAlignment=align=="left"?TextAlignment.Left:TextAlignment.Right;Layout(actual,width);
            saved.CopyTo(offsets,0);Assert.Equal(expected,Pixels(actual,context));
        }
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        actual.Inlines.Clear();actual.Text=source.Replace('A','Z').Replace('א','ת');Layout(actual,width);
        Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));
        Assert.NotEqual(expected,Pixels(actual,context));
    }

    [Theory]
    [InlineData(false,"start")] [InlineData(false,"end")]
    [InlineData(true,"start")] [InlineData(true,"end")]
    public void PlaintextWrappedRowsRetainTheirParagraphAlignmentAfterResize(bool rtl,string align)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="אב CD EF אב";var actual=Make(source,100,true);
        actual.Foreground=new LinearGradientBrush(Colors.Red,Colors.Blue,new(0,0),new(1,0)){Opacity=.65};
        byte[]? expected=null;
        foreach(int width in new[]{80,130,80})
        {
            Css.SetStyle(actual,$"width:{width}px;height:200px;padding:8px;box-sizing:border-box;font-family:DejaVu Sans;font-size:28px;line-height:40px;white-space:pre-wrap;unicode-bidi:plaintext;direction:{(rtl?"rtl":"ltr")};text-align:{align}");
            Layout(actual,width);int[] breaks=width==80?[3,6,9]:[6];
            var lines=actual.GetDocumentLines(width);Assert.Equal(new[]{0}.Concat(breaks),lines.Select(line=>line.Start));
            string text=source;foreach(int index in breaks.Reverse())text=text.Insert(index,"\u2028");
            int Native(int index,bool trailing=false)=>index+breaks.Count(at=>at<index||!trailing&&at==index);
            string family=actual.FontFamily.GetRenderingSource(actual);
            using var reference=NativeTextParagraph.TryCreate([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,actual.FlowDirection,naturalDirection:true,noWrap:true,textOptionsOwner:actual);
            Assert.NotNull(reference);Assert.Equal(lines.Count,reference.Lines.Length);
            double baseline=actual.GetFirstBaselineOffset(200)-8;var offsets=new double[lines.Count];
            for(int row=0;row<lines.Count;row++)
            {
                var line=lines[row];var native=reference.Lines[row];int visibleEnd=line.End;
                while(visibleEnd>line.Start&&source[visibleEnd-1]==' ')visibleEnd--;
                double visible=reference.Selection((uint)row,Native(line.Start),Native(visibleEnd,true)-Native(line.Start)).Sum(rect=>(double)rect.Width);
                double hanging=native.Metrics.Line.Width-visible;
                offsets[row]=8+(align=="start"?width-16-visible:0)-hanging;
            }
            expected=Pixels(dc=>
            {
                for(int row=0;row<lines.Count;row++)
                {
                    var line=lines[row];var native=reference.Lines[row];int first=Native(line.Start),end=Native(line.End,true);
                    dc.DrawText(new FormattedText(text,family,28){Foreground=actual.Foreground,
                        PlatformTextLine=native.ForSource(first,end-first,Colors.Black,1).WithBrushLineBox(native.Metrics.Baseline-baseline,line.Height)},
                        new(offsets[row]-native.Metrics.Line.X,line.Top+baseline-native.Metrics.Baseline));
                }
            },context);
            Assert.Equal(expected,Pixels(actual,context));
            for(int row=0;row<lines.Count;row++)
            {
                var line=lines[row];var native=reference.Lines[row];
                for(int index=line.Start;index<line.End;index++)
                {
                    var rects=reference.Selection((uint)row,Native(index),Native(index+1,true)-Native(index));
                    var boxes=actual.DocumentTextBounds(index,1);Assert.NotEmpty(rects);Assert.NotEmpty(boxes);
                    Assert.Equal(offsets[row]+rects.Min(rect=>rect.X)-native.Metrics.Line.X,boxes.Min(box=>box.Left),3);
                }
            }
            actual.SelectAll();Assert.Equal(source,actual.SelectedText);actual.ClearSelection();Assert.Equal(rtl?FlowDirection.RightToLeft:FlowDirection.LeftToRight,actual.FlowDirection);
        }
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        actual.Text=source.Replace('C','Z');Layout(actual,80);
        Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));Assert.NotEqual(expected,Pixels(actual,context));
    }

    public static IEnumerable<object[]> UnicodeSourceSeparatorCases()
    {
        foreach(string separator in new[]{"\u2028","\u2029","\u0085"})
            foreach(bool rtl in new[]{false,true})
                foreach(string scene in new[]{"basic","end","empty","override","isolate","indent","last","spacing","trim"})
                    yield return [separator,rtl,scene];
    }

    [Theory] [MemberData(nameof(UnicodeSourceSeparatorCases))]
    public void UnicodeSourceSeparatorsPreserveLinesAndParagraphs(string separator,bool rtl,string scene)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        string source=scene=="empty"?separator+"אב"+separator+separator:scene=="trim"?"אב "+separator+" AB":"אב"+separator+"AB";
        string[] parts=source.Split(separator,StringSplitOptions.None);
        var starts=new int[parts.Length];for(int i=1;i<starts.Length;i++)starts[i]=starts[i-1]+parts[i-1].Length+separator.Length;
        bool isolated=scene=="isolate",overridden=scene=="override";
        string open=isolated?"\u2068\u202E":overridden?(rtl?"\u202E":"\u202D"):"";
        string close=isolated?"\u202C\u2069":overridden?"\u202C":"";
        // Literal controls are an independent reference. LS retains the scope;
        // paragraph separators close and reopen it without entering source space.
        var text=new System.Text.StringBuilder();var leading=new int[source.Length+1];var trailing=new int[source.Length+1];
        text.Append(open);
        for(int i=0;i<source.Length;i++)
        {
            trailing[i]=text.Length;
            bool paragraph=source[i] is '\u2029' or '\u0085';
            if(paragraph)text.Append(close);
            leading[i]=text.Length;
            if(scene!="trim"||source[i]!=' ')text.Append(source[i]);
            trailing[i+1]=text.Length;
            if(paragraph)text.Append(open);
        }
        leading[source.Length]=text.Length;text.Append(close);string projected=text.ToString();
        var actual=Make(source);
        string style=$"font-size:28px;line-height:40px;white-space:pre;direction:{(rtl?"rtl":"ltr")};unicode-bidi:{(overridden?"bidi-override":"plaintext")};text-align:{(scene=="end"?"end":"start")}";
        if(scene=="indent")style+=";text-indent:24px each-line";
        if(scene=="last")style+=";text-align:justify;text-align-last:end;text-justify:none";
        if(scene=="spacing")style+=";letter-spacing:2px;word-spacing:3px";
        if(scene=="trim")style+=";white-space:pre-line";
        Css.SetStyle(actual,style);
        if(isolated)
        {
            actual.Text="";var span=new Span();span.Inlines.Add(new Run(source));actual.Inlines.Add(span);
            Css.SetStyle(span,"unicode-bidi:isolate-override;direction:rtl");
        }
        actual.Foreground=new LinearGradientBrush(Colors.Red,Colors.Blue,new(0,0),new(1,0)){Opacity=.65};Layout(actual);
        string family=actual.FontFamily.GetRenderingSource(actual);
        NativeMethods.ParagraphAdvanceUnit[]? units=null;
        if(scene=="spacing")
        {
            units=new CssInputTextSpacing(2,3).Units(projected);
            for(int i=0;i<units.Length;i++)if(projected[(int)units[i].TextPosition] is '\u2028' or '\u2029' or '\u0085')units[i].Flags=1;
        }
        using var reference=units is null
            ?NativeTextParagraph.TryCreate([new(projected,family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,actual.FlowDirection,naturalDirection:!overridden,noWrap:true,textOptionsOwner:actual)
            :NativeTextParagraph.TryCreateAdvanced([new(projected,family,28,400,0,Colors.Black)],family,28,10000000,1,actual.FlowDirection,units,contextPaint:true,naturalDirection:true,textOptionsOwner:actual);
        Assert.NotNull(reference);var lines=actual.GetDocumentLines(460);Assert.Equal(parts.Length,lines.Count);
        double baseline=actual.GetFirstBaselineOffset(200)-8;var offsets=new double[parts.Length];bool direction=rtl;
        for(int row=0;row<parts.Length;row++)
        {
            if(!overridden&&!isolated)
            {
                if(separator=="\u2028")direction=true;
                else if(parts[row].Length>0)direction=parts[row][0]=='א';
            }
            var line=lines[row];Assert.Equal(starts[row],line.Start);Assert.Equal(starts[row]+parts[row].Length,line.End);
            Assert.Equal(8+row*40,line.Top,3);Assert.Equal(40,line.Height,3);
            double width=parts[row].Length==0?0:reference.Lines[row].Metrics.Line.Width;
            double indent=scene=="indent"?24:0;bool right=scene is "end" or "last"?!direction:direction;
            offsets[row]=8+(direction?0:indent)+(right?444-indent-width:0);
        }
        var expected=Pixels(dc=>
        {
            for(int row=0;row<parts.Length;row++)
            {
                if(parts[row].Length==0)continue;var line=lines[row];var native=reference.Lines[row];
                int first=leading[line.Start],end=trailing[line.End];
                dc.DrawText(new FormattedText(projected,family,28){Foreground=actual.Foreground,
                    PlatformTextLine=native.ForSource(first,end-first,Colors.Black,1).WithBrushLineBox(native.Metrics.Baseline-baseline,40)},
                    new(offsets[row]-native.Metrics.Line.X,line.Top+baseline-native.Metrics.Baseline));
            }
        },context);
        Assert.Contains(expected.Where((_,i)=>(i&3)==3),value=>value>0);Assert.Equal(expected,Pixels(actual,context));
        for(int row=0;row<parts.Length;row++)
        {
            var line=lines[row];
            if(parts[row].Length==0)
            {
                var caret=Assert.Single(actual.DocumentTextBounds(line.Start,0));Assert.Equal(offsets[row],caret.Left,3);Assert.Equal(line.Top,caret.Top,3);continue;
            }
            var native=reference.Lines[row];
            for(int index=line.Start;index<line.End;index++)
            {
                if(scene=="trim"&&source[index]==' ')continue;
                var rects=reference.Selection((uint)row,leading[index],trailing[index+1]-leading[index]);
                var bounds=actual.DocumentTextBounds(index,1);Assert.NotEmpty(rects);Assert.NotEmpty(bounds);
                Assert.Equal(offsets[row]+rects.Min(r=>r.X)-native.Metrics.Line.X,bounds.Min(r=>r.Left),3);
                foreach(var box in bounds.Where(b=>b.Width>1))foreach(double fraction in new[]{.2,.8})
                    Assert.InRange(actual.DocumentCharacterFromPoint(new(box.Left+box.Width*fraction,box.Top+box.Height/2)),index,index+1);
            }
        }
        actual.SelectAll();Assert.Equal(source,actual.SelectedText);actual.ClearSelection();
        Assert.Equal(rtl?FlowDirection.RightToLeft:FlowDirection.LeftToRight,actual.FlowDirection);
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        actual.Inlines.Clear();actual.Text=source.Replace('א','ת').Replace('A','Z');Layout(actual,130);
        Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));Assert.NotEqual(expected,Pixels(actual,context));
    }

    [Theory]
    [InlineData("\u2028",false)] [InlineData("\u2028",true)]
    [InlineData("\u2029",false)] [InlineData("\u2029",true)]
    [InlineData("\u0085",false)] [InlineData("\u0085",true)]
    public void SourceSeparatorsComposeWithSoftWrappingAndRepeatedResize(string separator,bool rtl)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        string source="אב CD EF"+separator+"AB CD EF";var actual=Make(source);
        actual.Foreground=new LinearGradientBrush(Colors.Red,Colors.Blue,new(0,0),new(1,0)){Opacity=.65};
        byte[]? expected=null;
        foreach(int width in new[]{80,130,80})
        {
            Css.SetStyle(actual,$"width:{width}px;font-size:28px;line-height:32px;white-space:pre-wrap;unicode-bidi:plaintext;direction:{(rtl?"rtl":"ltr")};text-align:start");
            Layout(actual,width);int[] breaks=width==80?[3,6,12,15]:[6,15];
            int[] starts=width==80?[0,3,6,9,12,15]:[0,6,9,15];
            int[] ends=width==80?[3,6,8,12,15,17]:[6,8,15,17];
            string text=source;foreach(int index in breaks.Reverse())text=text.Insert(index,"\u2028");
            int Native(int index,bool trailing=false)=>index+breaks.Count(at=>at<index||!trailing&&at==index);
            string family=actual.FontFamily.GetRenderingSource(actual);
            using var reference=NativeTextParagraph.TryCreate([new(text,family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,actual.FlowDirection,naturalDirection:true,noWrap:true,textOptionsOwner:actual);
            Assert.NotNull(reference);var lines=actual.GetDocumentLines(width);Assert.Equal(starts,lines.Select(l=>l.Start));Assert.Equal(ends,lines.Select(l=>l.End));Assert.Equal(lines.Count,reference.Lines.Length);
            double baseline=actual.GetFirstBaselineOffset(200)-8;var offsets=new double[lines.Count];
            for(int row=0;row<lines.Count;row++)
            {
                int end=ends[row];while(end>starts[row]&&source[end-1]==' ')end--;
                var native=reference.Lines[row];double visible=reference.Selection((uint)row,Native(starts[row]),Native(end,true)-Native(starts[row])).Sum(r=>(double)r.Width);
                double hanging=native.Metrics.Line.Width-visible;bool direction=starts[row]<9||separator=="\u2028";
                offsets[row]=8+(direction?width-16-visible-hanging:0);
                Assert.Equal(8+row*32,lines[row].Top,3);
            }
            expected=Pixels(dc=>
            {
                for(int row=0;row<lines.Count;row++)
                {
                    var native=reference.Lines[row];int first=Native(starts[row]),end=Native(ends[row],true);
                    dc.DrawText(new FormattedText(text,family,28){Foreground=actual.Foreground,
                        PlatformTextLine=native.ForSource(first,end-first,Colors.Black,1).WithBrushLineBox(native.Metrics.Baseline-baseline,32)},
                        new(offsets[row]-native.Metrics.Line.X,lines[row].Top+baseline-native.Metrics.Baseline));
                }
            },context);
            Assert.Equal(expected,Pixels(actual,context));
            for(int row=0;row<lines.Count;row++)for(int index=starts[row];index<ends[row];index++)
            {
                var native=reference.Lines[row];var rects=reference.Selection((uint)row,Native(index),Native(index+1,true)-Native(index));
                var boxes=actual.DocumentTextBounds(index,1);Assert.NotEmpty(rects);Assert.NotEmpty(boxes);
                Assert.Equal(offsets[row]+rects.Min(r=>r.X)-native.Metrics.Line.X,boxes.Min(r=>r.Left),3);
            }
            actual.SelectAll();Assert.Equal(source,actual.SelectedText);actual.ClearSelection();
        }
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        actual.Text="changed";Layout(actual,80);Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));
    }

    [Theory]
    [InlineData("\u2028","pre")] [InlineData("\u2029","pre")]
    [InlineData("\u0085","pre")] [InlineData("\u0085","normal")]
    public void WordSpaceTransformChecksPreservedBreaksAfterWhitespaceConversion(string separator,string whitespace)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        string source="AB\u200B"+separator+"\u200BCD",paint=whitespace=="normal"?"AB   CD":source;
        var actual=Make(source);Css.SetStyle(actual,$"font-size:28px;line-height:40px;white-space:{whitespace};word-space-transform:space;unicode-bidi:plaintext;text-align:start");
        actual.Foreground=new LinearGradientBrush(Colors.Red,Colors.Blue,new(0,0),new(1,0)){Opacity=.65};Layout(actual);
        string family=actual.FontFamily.GetRenderingSource(actual);
        using var reference=NativeTextParagraph.TryCreate([new(paint,family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,FlowDirection.LeftToRight,naturalDirection:true,noWrap:true,textOptionsOwner:actual);
        Assert.NotNull(reference);int count=whitespace=="normal"?1:2;Assert.Equal(count,actual.GetDocumentLines(460).Count);Assert.Equal(count,reference.Lines.Length);
        double baseline=actual.GetFirstBaselineOffset(200)-8;
        var expected=Pixels(dc=>
        {
            for(int row=0;row<count;row++)
            {
                var native=reference.Lines[row];int first=row==0?0:4,length=count==1?paint.Length:3;
                dc.DrawText(new FormattedText(paint,family,28){Foreground=actual.Foreground,
                    PlatformTextLine=native.ForSource(first,length,Colors.Black,1).WithBrushLineBox(native.Metrics.Baseline-baseline,40)},
                    new(8-native.Metrics.Line.X,8+row*40+baseline-native.Metrics.Baseline));
            }
        },context);
        Assert.Equal(expected,Pixels(actual,context));actual.SelectAll();Assert.Equal(source,actual.SelectedText);
    }

    public static IEnumerable<object[]> NextLineWhitespaceCases()
    {
        foreach(bool rtl in new[]{false,true})
            foreach(string whitespace in new[]{"normal","nowrap","pre","pre-wrap","pre-line","break-spaces","preserve-spaces","discard"})
                yield return [rtl,whitespace];
    }

    [Theory] [MemberData(nameof(NextLineWhitespaceCases))]
    public void UnicodeNextLineUsesTheTextSourcesWhitespaceConvention(bool rtl,string whitespace)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source="אב\u0085AB";bool discard=whitespace=="discard",space=whitespace is "normal" or "nowrap" or "preserve-spaces";
        string projected=discard?"אבAB":space?"אב AB":source;
        var actual=Make(source);string style=$"font-size:28px;line-height:40px;direction:{(rtl?"rtl":"ltr")};unicode-bidi:plaintext;text-align:start;";
        style+=whitespace is "preserve-spaces" or "discard"?$"white-space-collapse:{whitespace};text-wrap-mode:nowrap":$"white-space:{whitespace}";
        Css.SetStyle(actual,style);actual.Foreground=new LinearGradientBrush(Colors.Red,Colors.Blue,new(0,0),new(1,0)){Opacity=.65};Layout(actual);
        string family=actual.FontFamily.GetRenderingSource(actual);
        using var reference=NativeTextParagraph.TryCreate([new(projected,family,28,400,0,Colors.Black)],family,28,10000000,1,TextAlignment.Left,actual.FlowDirection,naturalDirection:true,noWrap:true,textOptionsOwner:actual);
        Assert.NotNull(reference);int count=space||discard?1:2;var lines=actual.GetDocumentLines(460);Assert.Equal(count,lines.Count);Assert.Equal(count,reference.Lines.Length);
        double baseline=actual.GetFirstBaselineOffset(200)-8;
        var expected=Pixels(dc=>
        {
            for(int row=0;row<count;row++)
            {
                var native=reference.Lines[row];int first=row==0?0:3,length=count==1?projected.Length:2;
                double x=row==0?452-native.Metrics.Line.Width:8;
                dc.DrawText(new FormattedText(projected,family,28){Foreground=actual.Foreground,
                    PlatformTextLine=native.ForSource(first,length,Colors.Black,1).WithBrushLineBox(native.Metrics.Baseline-baseline,40)},
                    new(x-native.Metrics.Line.X,8+row*40+baseline-native.Metrics.Baseline));
            }
        },context);
        Assert.Equal(expected,Pixels(actual,context));
        foreach(int index in new[]{0,1,3,4})
        {
            int row=count==1?0:index<2?0:1,nativeIndex=discard&&index>2?index-1:index;
            var native=reference.Lines[row];var rects=reference.Selection((uint)row,nativeIndex,1);var bounds=actual.DocumentTextBounds(index,1);
            Assert.NotEmpty(rects);Assert.NotEmpty(bounds);double x=row==0?452-native.Metrics.Line.Width:8;
            Assert.Equal(x+rects.Min(r=>r.X)-native.Metrics.Line.X,bounds.Min(r=>r.Left),3);
            foreach(var box in bounds.Where(b=>b.Width>1))Assert.InRange(actual.DocumentCharacterFromPoint(new(box.Left+box.Width/2,box.Top+box.Height/2)),index,index+1);
        }
        actual.SelectAll();Assert.Equal(source,actual.SelectedText);actual.ClearSelection();
        Css.SetStyle(actual,style+";white-space:pre");Layout(actual);Assert.Equal(2,actual.GetDocumentLines(460).Count);
        Css.SetStyle(actual,style);Layout(actual);Assert.Equal(expected,Pixels(actual,context));
        var recorder=new DrawingRecorder();recorder.BindWholeFrame();actual.Render(recorder);var frame=recorder.Commit();Assert.True(frame.IsFullyRecordable);
        actual.Text="changed";Layout(actual);Assert.Equal(expected,Pixels(dc=>DrawingReplayer.Replay(frame,dc),context));
    }

    private static byte[] BidiAutospaceReferencePixels(TextBlock actual,NativeTextParagraph reference,int length,RenderContext context,int first=1)
    {
        var native=Assert.Single(reference.Lines);var line=Assert.Single(actual.GetDocumentLines(460));double baseline=actual.GetFirstBaselineOffset(200)-8;var brush=actual.Foreground!;
        return Pixels(dc=>dc.DrawText(new FormattedText(reference.Text,actual.FontFamily.GetRenderingSource(actual),28){Foreground=brush,
            PlatformTextLine=native.ForSource(first,length,brush is SolidColorBrush solid?solid.Color:Colors.White,brush.Opacity)
                .WithBrushLineBox(native.Metrics.Baseline-baseline,line.Height)},new(8-native.Metrics.Line.X,line.Top+baseline-native.Metrics.Baseline)),context);
    }

    private static byte[] BidiJustificationReferencePixels(TextBlock actual,NativeTextParagraph reference,int first,int length,RenderContext context)
    {
        var native=Assert.Single(reference.Lines);var line=Assert.Single(actual.GetDocumentLines(460));double baseline=actual.GetFirstBaselineOffset(200)-8;var brush=actual.Foreground!;
        return Pixels(dc=>dc.DrawText(new FormattedText(reference.Text,actual.FontFamily.GetRenderingSource(actual),28){Foreground=brush,
            PlatformTextLine=native.ForSource(first,length,brush is SolidColorBrush solid?solid.Color:Colors.White,brush.Opacity)
                .WithBrushLineBox(native.Metrics.Baseline-baseline,line.Height)},new(8-native.Metrics.Line.X,line.Top+baseline-native.Metrics.Baseline)),context);
    }

    private static TextBlock Make(string source, int width = 460, bool wrap = false)
    {
        var block = new TextBlock { Text = source }; Css.SetClass(block, "body");
        var sheet = CssStyleSheet.Parse($".body{{width:{width}px;height:200px;padding:8px;box-sizing:border-box;font-family:DejaVu Sans;font-size:28px;color:#e83d55;white-space:{(wrap ? "pre-wrap" : "pre")};text-align:left}}");
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(block).Add(sheet); Layout(block, width); return block;
    }
    private static void Layout(TextBlock block, int width = 460)
    {
        for (int i = 0; i < 3; i++) { Dispatcher.GetForCurrentThread().ProcessQueue(); block.Measure(new(width, 200)); block.Arrange(new(0, 0, width, 200)); block.UpdateLayout(); }
    }
    private static byte[] Pixels(TextBlock block, RenderContext context)
        => Pixels(dc => block.Render(dc), context);
    private static byte[] Pixels(Action<DrawingContext> paint, RenderContext context)
    {
        using var target = context.CreateOffscreenRenderTarget(480, 210); target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
        using (var dc = new RenderTargetDrawingContext(target, context)) { paint(dc); dc.Close(); }
        target.EndDraw(); var pixels = new byte[480 * 210 * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 480 * 4, out _, out _)); return pixels;
    }
}
