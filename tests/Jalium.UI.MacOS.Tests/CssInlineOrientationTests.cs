using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssInlineOrientationTests
{
    public static IEnumerable<object[]> NativeCases()
    {
        foreach (var backend in new[] { RenderBackend.Software, RenderBackend.Metal })
        foreach (var family in new[] { "Arial", "Hiragino Sans", "Times New Roman", "Georgia" })
        foreach (var direction in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            yield return [backend, family, direction];
    }

    [Theory, MemberData(nameof(NativeCases))]
    public void NativeRangesKeepIndependentTypographyAndUprightBidi(RenderBackend backend, string family, FlowDirection direction)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        NativeTextParagraph.Span[] spans = [new("漢", family, 32, 400, 0, Colors.Black, TextOrientation: 0),
            new("ABאב", family, 32, 400, 0, Colors.Black, TextOrientation: 1),
            new("漢", family, 32, 400, 0, Colors.Black, TextOrientation: 2)];
        using var paragraph = NativeTextParagraph.TryCreate(spans, family, 32, 1000, 40,
            TextAlignment.Left, direction, noWrap: true, verticalForms: true);
        Assert.NotNull(paragraph);
        Assert.Equal("漢ABאב漢", paragraph.Text);
        Assert.Equal(192, Assert.Single(paragraph.Lines).Metrics.Line.Width, 2);
        var cells = Enumerable.Range(1, 4).Select(i => Assert.Single(paragraph.Selection(0, i, 1))).ToArray();
        Assert.All(cells, cell => Assert.Equal(32, cell.Width, 3));
        for (int i = 1; i < cells.Length; i++) Assert.Equal(32, cells[i].X - cells[i - 1].X, 3);
        for (int i = 0; i < cells.Length; i++)
            Assert.Equal((uint)(i + 1), paragraph.HitTest(0, cells[i].X + 4).TextPosition);
    }

    public static IEnumerable<object[]> ConsumerCases()
    {
        foreach (string mode in new[] { "vertical-rl", "vertical-lr" })
        foreach (string root in new[] { "mixed", "upright", "sideways" })
        foreach (bool decorated in new[] { false, true }) yield return [mode, root, decorated];
    }

    [Theory, MemberData(nameof(ConsumerCases))]
    public void OriginalInlinesKeepOverridesSelectionAndDynamicGeometry(string mode, string root, bool decorated)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = new TextBlock { IsTextSelectionEnabled = true };
        var first = new Run("漢"); var upright = new Run("AB"); var last = new Run("漢");
        var sideways = new Span(last);
        text.Inlines.Add(first); text.Inlines.Add(upright); text.Inlines.Add(sideways);
        Css.SetStyle(text, $"writing-mode:{mode};text-orientation:{root};white-space:pre;font-family:Arial;font-size:32px;line-height:40px;text-autospace:no-autospace;direction:ltr;");
        Css.SetStyle(upright, "text-orientation:upright;" + (decorated ? "display:inline;border:1px solid transparent;" : ""));
        Css.SetStyle(sideways, "text-orientation:sideways;" + (decorated ? "display:inline;border:1px solid transparent;" : ""));
        Layout(text);
        Assert.Same(first, text.Inlines[0]); Assert.Same(upright, text.Inlines[1]); Assert.Same(last, sideways.Inlines[0]);
        var a = Assert.Single(text.DocumentTextBounds(1, 1));
        var b = Assert.Single(text.DocumentTextBounds(2, 1));
        Assert.Equal(32, a.Height, 2); Assert.Equal(32, b.Top - a.Top, 2);
        Assert.Contains(text.DocumentCharacterFromPoint(new Point(a.Left + a.Width / 2, a.Top + 4)), new[] { 1, 2 });
        text.Select(1, 2); Assert.Equal("AB", text.SelectedText);
        Css.SetStyle(upright, "text-orientation:sideways;" + (decorated ? "display:inline;border:1px solid transparent;" : ""));
        Layout(text);
        Assert.True(Assert.Single(text.DocumentTextBounds(1, 2)).Height < 63);
        Css.SetStyle(upright, "text-orientation:upright;" + (decorated ? "display:inline;border:1px solid transparent;" : ""));
        Layout(text);
        Assert.Equal(a, Assert.Single(text.DocumentTextBounds(1, 1)));
        text.SelectAll(); Assert.Equal("漢AB漢", text.SelectedText);
    }

    [Fact]
    public void FormatOrientationCapabilityRejectsInvalidValues()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        Assert.Equal(JaliumResult.InvalidArgument, (JaliumResult)NativeMethods.TextFormatSetOrientation(0, 0, 0));
        using var format = TextMeasurement.CreateTextFormatFromFamilyList(context, "Arial", 32, 400, 0);
        Assert.NotNull(format);
        Assert.False(format.TrySetTextOrientation(-1)); Assert.False(format.TrySetTextOrientation(3));
        Assert.True(format.TrySetTextOrientation(0)); Assert.True(format.TrySetTextOrientation(1)); Assert.True(format.TrySetTextOrientation(2));
    }

    private static void Layout(TextBlock text)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            text.Measure(new Size(180, 300)); text.Arrange(new Rect(0, 0, 180, 300)); text.UpdateLayout();
        }
    }

    [Theory]
    [InlineData("e\u0301", false)]
    [InlineData("👩‍💻", false)]
    [InlineData("اب", false)]
    [InlineData("12", true)]
    public void NativeClustersAndCombinedCellsWrapWithoutLosingSource(string middle, bool combine)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        NativeTextParagraph.Span[] spans = [new("漢", "Arial", 32, 400, 0, Colors.Black, TextOrientation: 0),
            new(middle, "Arial", 32, 400, 0, Colors.Black, TextOrientation: 1),
            new("字", "Arial", 32, 400, 0, Colors.Black, TextOrientation: 2)];
        using var paragraph = NativeTextParagraph.TryCreate(spans, "Arial", 32, 64, 40, TextAlignment.Left,
            FlowDirection.LeftToRight, verticalForms: true, combined: combine
                ? [new NativeMethods.ParagraphCombineUnit { TextPosition = 1, Length = (uint)middle.Length, Em = 32 }] : null);
        Assert.NotNull(paragraph); Assert.Equal("漢" + middle + "字", paragraph.Text);
        int[] boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(paragraph.Text).Append(paragraph.Text.Length).ToArray();
        foreach (var row in paragraph.Lines)
        {
            int first = (int)row.Metrics.Line.TextPosition, last = first + (int)row.Metrics.Line.Length;
            Assert.Contains(first, boundaries); Assert.Contains(last, boundaries);
            if (combine) Assert.NotEqual(2, last);
            Assert.InRange(row.Metrics.Line.Width, 31.99, 64.01);
        }
        Assert.Equal(paragraph.Text.Length, paragraph.Lines.Sum(row => (int)row.Metrics.Line.Length));
    }

    [Fact]
    public void UprightRangeRetainsStrongLtrAroundExplicitControls()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        NativeTextParagraph.Span[] spans = [new("漢", "Arial", 32, 400, 0, Colors.Black, TextOrientation: 0),
            new("\u2067אב\u2069", "Arial", 32, 400, 0, Colors.Black, TextOrientation: 1),
            new("字", "Arial", 32, 400, 0, Colors.Black, TextOrientation: 2)];
        using var paragraph = NativeTextParagraph.TryCreate(spans, "Arial", 32, 1000, 40,
            TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true, verticalForms: true);
        Assert.NotNull(paragraph); Assert.Equal("漢\u2067אב\u2069字", paragraph.Text);
        Assert.Equal(128, Assert.Single(paragraph.Lines).Metrics.Line.Width, 2);
        var a = Assert.Single(paragraph.Selection(0, 2, 1)); var b = Assert.Single(paragraph.Selection(0, 3, 1));
        Assert.Equal(32, a.Width, 2); Assert.Equal(32, b.X - a.X, 2);
    }

    [Theory]
    [InlineData("horizontal-tb")]
    [InlineData("sideways-rl")]
    [InlineData("sideways-lr")]
    public void HorizontalTypographyIgnoresLocalOrientation(string mode)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = new TextBlock(); var run = new Run("ABאב漢"); text.Inlines.Add(run);
        Css.SetStyle(text, $"writing-mode:{mode};font-family:Arial;font-size:32px;line-height:40px;white-space:pre;text-autospace:no-autospace;");
        Layout(text); var original = Enumerable.Range(0, 5).Select(i => text.DocumentTextBounds(i, 1).ToArray()).ToArray();
        foreach (string orientation in new[] { "upright", "sideways", "mixed" })
        {
            Css.SetStyle(run, "text-orientation:" + orientation); Layout(text);
            for (int i = 0; i < 5; i++) Assert.Equal(original[i], text.DocumentTextBounds(i, 1));
        }
    }

    [Theory]
    [InlineData("vertical-rl", "mixed")]
    [InlineData("vertical-rl", "upright")]
    [InlineData("vertical-rl", "sideways")]
    [InlineData("vertical-lr", "mixed")]
    [InlineData("vertical-lr", "upright")]
    [InlineData("vertical-lr", "sideways")]
    public void OriginalControlsRetainLocalSelectionsAndRtlInheritance(string mode, string root)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var panel = new StackPanel();
        var first = new TextBlock { Text = "AB", IsTextSelectionEnabled = true };
        var last = new TextBlock { Text = "אב漢", IsTextSelectionEnabled = true };
        panel.Children.Add(first); panel.Children.Add(last);
        Css.SetStyle(first, "display:inline;text-orientation:upright;"); Css.SetStyle(last, "display:inline;text-orientation:sideways;");
        Css.SetStyle(panel, $"writing-mode:{mode};text-orientation:{root};direction:rtl;font-family:Arial;font-size:32px;line-height:40px;white-space:pre;text-autospace:no-autospace;");
        Arrange();
        Assert.Equal(FlowDirection.RightToLeft, first.FlowDirection); Assert.Equal(FlowDirection.LeftToRight, CssWritingModeProperties.GetUsedFlowDirection(first));
        var a = Assert.Single(first.DocumentTextBounds(0, 1)); var b = Assert.Single(first.DocumentTextBounds(1, 1));
        Assert.Equal(32, a.Height, 2); Assert.Equal(32, b.Top - a.Top, 2);
        first.SelectAll(); Assert.Equal("AB", first.SelectedText); last.Select(1, 2); Assert.Equal("ב漢", last.SelectedText);
        Assert.Contains(first.DocumentCharacterFromPoint(new Point(a.Left + a.Width / 2, a.Top + 4)), new[] { 0, 1 });
        Assert.Same(first, panel.Children[0]); Assert.Same(last, panel.Children[1]);
        Css.SetStyle(first, "display:inline;text-orientation:sideways;"); Arrange();
        Assert.True(Assert.Single(first.DocumentTextBounds(0, 2)).Height < 63);
        Assert.Equal("AB", first.SelectedText);
        void Arrange()
        {
            for (int i = 0; i < 3; i++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
                panel.Measure(new Size(180, 300)); panel.Arrange(new Rect(0, 0, 180, 300)); panel.UpdateLayout();
            }
        }
    }

    public static IEnumerable<object[]> PixelCases()
    {
        foreach (string mode in new[] { "vertical-rl", "vertical-lr" })
        foreach (string root in new[] { "mixed", "upright", "sideways" })
        foreach (string shape in new[] { "runs", "decorated", "controls" })
        {
            yield return [mode, root, shape, RenderBackend.Software, RenderingEngine.Impeller];
            yield return [mode, root, shape, RenderBackend.Metal, RenderingEngine.Impeller];
            yield return [mode, root, shape, RenderBackend.Metal, RenderingEngine.Vello];
        }
    }

    [Theory, MemberData(nameof(PixelCases))]
    public void RangePixelsMatchNativeReference(string mode, string root, string shape, RenderBackend backend, RenderingEngine engine)
    {
        if (backend == RenderBackend.Metal && Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_CHILD") != "1")
        {
            string command = Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_COMMAND") ?? throw new InvalidOperationException("Metal main-thread runner is required.");
            var args = System.Text.Json.JsonSerializer.Deserialize<string[]>(command)!;
            var start = new System.Diagnostics.ProcessStartInfo(args[0]) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in args.Skip(1)) start.ArgumentList.Add(arg);
            foreach (string arg in new[] { "range", mode, root, shape, engine.ToString() }) start.ArgumentList.Add(arg);
            start.Environment["JALIUM_CSS_METAL_CHILD"] = "1";
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(90000)) { process.Kill(entireProcessTree: true); Assert.Fail("Metal range runner exceeded 90 seconds."); }
            string receipt = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
            Assert.True(process.ExitCode == 0, receipt); Assert.Contains("ENGINE " + engine, receipt); Assert.Contains("BACKEND Metal", receipt);
            return;
        }
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var surface = backend == RenderBackend.Metal ? new NativePlatformWindow("Jalium CSS inline orientation pixels", 1640, 680, 240, 240, 1, 0) : null;
        surface?.Show(activate: false);
        Console.WriteLine("BACKEND " + context.Backend);
        const string source = "漢ABאב漢";
        FrameworkElement host;
        if (shape == "controls")
        {
            var panel = new StackPanel();
            foreach (var (part, orientation) in new[] { ("漢", "mixed"), ("ABאב", "upright"), ("漢", "sideways") })
            {
                var text = new TextBlock { Text = part };
                Css.SetStyle(text, $"display:inline;text-orientation:{orientation};"); panel.Children.Add(text);
            }
            host = panel;
        }
        else
        {
            var text = new TextBlock(); var first = new Run("漢"); var upright = new Span(new Run("ABאב")); var last = new Span(new Run("漢"));
            text.Inlines.Add(first); text.Inlines.Add(upright); text.Inlines.Add(last);
            string decoration = shape == "decorated" ? "display:inline;background:transparent;" : "";
            Css.SetStyle(first, "text-orientation:mixed;" + decoration);
            Css.SetStyle(upright, "text-orientation:upright;" + decoration);
            Css.SetStyle(last, "text-orientation:sideways;" + decoration); host = text;
        }
        Css.SetStyle(host, $"display:block;width:180px;height:200px;writing-mode:{mode};text-orientation:{root};font-family:'Hiragino Sans';font-size:32px;line-height:40px;white-space:pre;text-autospace:no-autospace;color:black;direction:ltr;");
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            host.Measure(new Size(180, 200)); host.Arrange(new Rect(0, 0, 180, 200)); host.UpdateLayout();
        }
        NativeTextParagraph.Span[] spans = [new("漢", "Hiragino Sans", 32, 400, 0, Colors.Black, TextOrientation: 0),
            new("ABאב", "Hiragino Sans", 32, 400, 0, Colors.Black, TextOrientation: 1),
            new("漢", "Hiragino Sans", 32, 400, 0, Colors.Black, TextOrientation: 2)];
        using var native = NativeTextParagraph.TryCreate(spans, "Hiragino Sans", 32, 1000, 40,
            TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true, textOptionsOwner: host, verticalForms: true);
        Assert.NotNull(native); var row = Assert.Single(native.Lines);
        Assert.Equal(192, row.Metrics.Line.Width, 2);
        var font = TextMeasurement.GetFontMetrics("Hiragino Sans", 32, 400, 0);
        double baseline = root == "sideways" ? font.Ascent + (40 - font.Ascent - font.Descent) / 2 : 20;
        var reference = Pixels(dc =>
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, 180, 200)));
            dc.PushTransform(new MatrixTransform(new Matrix(0, 1, -1, 0, mode == "vertical-lr" ? 40 : 180, 0)));
            dc.DrawText(new FormattedText(source, "Hiragino Sans", 32) { Foreground = Brushes.Black,
                PlatformTextLine = row.ForSource(0, source.Length, Colors.Black, 1) }, new Point(0, baseline - row.Metrics.Baseline));
            dc.Pop(); dc.Pop();
        });
        var actual = Pixels(host.Render);
        Assert.Contains(actual.Where((_, i) => i % 4 == 3), alpha => alpha > 0);
        Save("actual", actual); Save("reference", reference); Assert.Equal(reference, actual);

        byte[] Pixels(Action<DrawingContext> draw)
        {
            using var target = surface is null ? context.CreateOffscreenRenderTarget(240, 240) : context.CreateRenderTarget(surface.GetSurface(), 240, 240);
            target.SetRenderingEngine(engine); Assert.Equal(engine, target.RenderingEngine); Console.WriteLine("ENGINE " + target.RenderingEngine);
            target.SetDpi(96, 96); target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
            using (var dc = new RenderTargetDrawingContext(target, context)) { draw(dc); dc.Close(); }
            target.EndDraw(); var pixels = new byte[240 * 240 * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 240 * 4, out _, out _)); return pixels;
        }
        void Save(string kind, byte[] pixels)
        {
            var folder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR"); if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);
            var bitmap = BitmapSource.Create(240, 240, 96, 96, PixelFormats.Bgra32, null, pixels, 240 * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(folder, $"range-{mode}-{root}-{shape}-{backend}-{engine}-{kind}.png")); encoder.Save(stream);
        }
    }
}
