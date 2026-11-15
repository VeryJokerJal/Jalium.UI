using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSharedVerticalTests
{
    public static IEnumerable<object[]> GlyphCases()
    {
        foreach (var mode in new[] { "vertical-rl", "vertical-lr", "sideways-rl", "sideways-lr" })
        foreach (var backend in new[] { RenderBackend.Software, RenderBackend.Metal })
        foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Impeller })
        foreach (bool authored in new[] { false, true })
        foreach (string orientation in new[] { "mixed", "upright", "sideways", "combine", "wrap", "edge" })
        foreach (string direction in new[] { "ltr", "rtl" }) yield return [mode, backend, authored, orientation, direction, engine];
    }

    [Theory]
    [MemberData(nameof(GlyphCases))]
    public void SharedGlyphsMatchNativeParagraphAtLiteralPhysicalPlacement(string mode, RenderBackend backend, bool authored, string orientation, string direction, RenderingEngine engine)
    {
        if (backend == RenderBackend.Metal && Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_CHILD") != "1")
        {
            var command = System.Text.Json.JsonSerializer.Deserialize<string[]>(Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_COMMAND") ?? "[]")!;
            Assert.True(command.Length >= 2, "Metal pixel cases require the main-thread native-surface runner.");
            var start = new System.Diagnostics.ProcessStartInfo(command[0]) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in command.Skip(1)) start.ArgumentList.Add(argument);
            start.ArgumentList.Add(mode); start.ArgumentList.Add(authored.ToString());
            start.ArgumentList.Add(orientation); start.ArgumentList.Add(direction); start.ArgumentList.Add(engine.ToString());
            start.Environment["JALIUM_CSS_METAL_CHILD"] = "1";
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(90000)) { process.Kill(entireProcessTree: true); Assert.Fail("Metal native-surface runner exceeded 90 seconds."); }
            string receipt = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
            Assert.True(process.ExitCode == 0, receipt); Assert.Contains("BACKEND Metal", receipt); Assert.Contains("ENGINE " + engine, receipt);
            return;
        }
        using var context = RenderContext.GetOrCreateCurrent(backend);
        // AppKit window creation and destruction are confined to a child
        // executable's actual main thread, never an xUnit worker thread.
        using var surface = backend == RenderBackend.Metal
            ? new NativePlatformWindow("Jalium CSS shared vertical pixel validation", 1640, 680, 220, 220, 1, 0) : null;
        surface?.Show(activate: false);
        Console.WriteLine($"BACKEND {context.Backend}; nativeSurface={surface is not null}; mode={mode}; authored={authored}");
        bool combine = orientation == "combine", wrap = orientation == "wrap", edge = orientation == "edge";
        double physicalHeight = wrap ? 96 : 180;
        string source = wrap ? "天地玄黄宇宙洪荒" : combine ? "漢12字" : "漢Aאב";
        string firstPart = wrap ? "天地玄黄" : combine ? "漢" : "漢A", lastPart = wrap ? "宇宙洪荒" : combine ? "12字" : "אב";
        FrameworkElement host;
        if (authored)
        {
            var text = new TextBlock(); var firstRun = new Run(firstPart); text.Inlines.Add(firstRun);
            if (edge) Css.SetStyle(firstRun, "display:inline;border:1px solid transparent;");
            var span = new Span(new Run(lastPart)); text.Inlines.Add(span);
            Css.SetStyle(span, edge ? "display:inline;border:1px solid transparent;" : "display:inline;"); host = text;
        }
        else
        {
            var panel = new StackPanel();
            foreach (string part in new[] { firstPart, lastPart })
            {
                var text = new TextBlock { Text = part }; Css.SetStyle(text, edge ? "display:inline;border:1px solid transparent;" : "display:inline;"); panel.Children.Add(text);
            }
            host = panel;
        }
        Css.SetStyle(host, $"display:block;width:180px;height:{physicalHeight}px;writing-mode:{mode};text-orientation:{(combine || wrap || edge ? "mixed" : orientation)};text-combine-upright:{(combine ? "digits 2" : "none")};direction:{direction};text-autospace:no-autospace;white-space:{(wrap ? "pre-wrap" : "pre")};font-family:'Hiragino Sans';font-size:32px;line-height:40px;color:black;");
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            host.Measure(new Size(180, physicalHeight)); host.Arrange(new Rect(0, 0, 180, physicalHeight)); host.UpdateLayout();
        }
        NativeTextParagraph.Span[] spans = [new(source, "Hiragino Sans", 32, 400, 0, Colors.Black)];
        bool vertical = mode is "vertical-rl" or "vertical-lr" && orientation != "sideways";
        bool upright = vertical && orientation == "upright";
        var flow = direction == "rtl" && !upright ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        using var native = edge ? NativeTextParagraph.TryCreateAdvanced(spans, "Hiragino Sans", 32, 1000, 40, flow,
            Enumerable.Range(0, source.Length).Select(i => new NativeMethods.ParagraphAdvanceUnit { TextPosition = (uint)i, Length = 1,
                Leading = i is 0 or 2 ? 1 : 0, Trailing = i is 1 or 3 ? 1 : 0 }).ToArray(),
            contextPaint: true, verticalForms: vertical, uprightForms: upright)
            : NativeTextParagraph.TryCreate(spans, "Hiragino Sans", 32, wrap ? 96 : 1000, 40,
            TextAlignment.Left, flow, noWrap: !wrap, verticalForms: vertical, uprightForms: upright, combined: vertical && combine
                ? [new NativeMethods.ParagraphCombineUnit { TextPosition = 1, Length = 2, Em = 32 }] : null);
        Assert.NotNull(native); var rows = native.Lines;
        Assert.Equal(wrap ? 3 : 1, rows.Length);
        var font = TextMeasurement.GetFontMetrics("Hiragino Sans", 32, 400, 0);
        double baseline = vertical ? 20 : font.Ascent + (40 - font.Ascent - font.Descent) / 2;
        // The reference has one native line and literal spec-derived placement.
        // It does not use shared fragments, slot allocation or writing-axis helpers.
        var reference = Pixels(dc =>
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, 180, physicalHeight)));
            for (int column = 0; column < rows.Length; column++)
            {
                var row = rows[column];
                var matrix = mode switch
                {
                    "vertical-lr" => new Matrix(0, 1, -1, 0, 40 * (column + 1), 0),
                    "sideways-lr" => new Matrix(0, -1, 1, 0, 40 * column, physicalHeight),
                    _ => new Matrix(0, 1, -1, 0, 180 - 40 * column, 0),
                };
                dc.PushTransform(new MatrixTransform(matrix));
                dc.DrawText(new FormattedText(source, "Hiragino Sans", 32)
                {
                    Foreground = Brushes.Black, PlatformTextLine = row.ForSource((int)row.Metrics.Line.TextPosition, (int)row.Metrics.Line.Length, Colors.Black, 1),
                }, new Point(flow == FlowDirection.RightToLeft ? physicalHeight - row.Metrics.Line.Width : 0, baseline - row.Metrics.Baseline));
                dc.Pop();
            }
            dc.Pop();
        });
        var actual = Pixels(host.Render);
        Save($"{mode}-{orientation}-{direction}-{backend}-{authored}-{engine}-actual.png", actual);
        Save($"{mode}-{orientation}-{direction}-{backend}-{authored}-{engine}-reference.png", reference);
        if (!authored && actual.Where((_, i) => i % 4 == 3).All(value => value == 0))
        {
            var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); host.Render(recorder);
            var frame = recorder.Commit();
            Save($"{mode}-{orientation}-{direction}-{backend}-{authored}-{engine}-whole-frame.png", Pixels(dc => DrawingReplayer.Replay(frame, dc)));
            var properties = new[] { "HasCssInlineFragments", "IsCssInlineBox", "UsesCssTextBody", "IsVerticalTextLayout", "TextWritingMode", "PhysicalTextContentSize", "TextContentSize", "TextContentOffset" };
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var sources = ((StackPanel)host).Children.OfType<TextBlock>().Select(text => new {
                text.Text, Size = text.RenderSize.ToString(), Bounds = text.VisualBounds.ToString(), Matrix = text.GetRenderMatrix().ToString(),
                Clip = typeof(TextBlock).GetMethod("GetRenderClip", flags)!.Invoke(text, null)?.ToString(),
                State = properties.ToDictionary(name => name, name => typeof(TextBlock).GetProperty(name, flags)?.GetValue(text)?.ToString())
            }).ToArray();
            var folder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR");
            if (folder is not null) File.WriteAllText(Path.Combine(folder, $"{mode}-{backend}-diagnostic.json"),
                System.Text.Json.JsonSerializer.Serialize(new { frame.IsFullyRecordable, Bounds = frame.Bounds?.ToString(), Sources = sources }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        Assert.Equal(reference, actual);

        byte[] Pixels(Action<DrawingContext> draw)
        {
            using var target = surface is null ? context.CreateOffscreenRenderTarget(220, 220)
                : context.CreateRenderTarget(surface.GetSurface(), 220, 220);
            target.SetRenderingEngine(engine);
            Assert.Equal(engine, target.RenderingEngine);
            Console.WriteLine($"ENGINE {target.RenderingEngine}");
            target.SetDpi(96, 96);
            target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
            using (var dc = new RenderTargetDrawingContext(target, context)) { draw(dc); dc.Close(); }
            target.EndDraw(); var pixels = new byte[220 * 220 * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 220 * 4, out _, out _)); return pixels;
        }
        static void Save(string name, byte[] pixels)
        {
            var folder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR");
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);
            var bitmap = BitmapSource.Create(220, 220, 96, 96, PixelFormats.Bgra32, null, pixels, 220 * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(folder, name)); encoder.Save(stream);
        }
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    [InlineData("sideways-rl")]
    [InlineData("sideways-lr")]
    public void FirstArrangeAndResizeUseTheCurrentPhysicalExtent(string mode)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var first = new TextBlock { Text = "天地", IsTextSelectionEnabled = true };
        var second = new TextBlock { Text = "玄黄", IsTextSelectionEnabled = true };
        var panel = new StackPanel(); panel.Children.Add(first); panel.Children.Add(second);
        Css.SetStyle(first, "display:inline;"); Css.SetStyle(second, "display:inline;");
        Css.SetStyle(panel, $"display:block;writing-mode:{mode};font-family:'Hiragino Sans';font-size:24px;line-height:32px;white-space:pre;padding:7px 11px 13px 17px;border:2px solid black;");
        Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
        foreach (var extent in new[] { new Size(160, 180), new Size(244, 212), new Size(160, 180) })
        {
            // One arrange is intentional: a second pass hid the stale RenderSize bug.
            panel.Measure(extent); panel.Arrange(new Rect(extent));
            foreach (var text in new[] { first, second })
            foreach (int at in new[] { 0, 1 })
            {
                var local = Assert.Single(text.DocumentTextBounds(at, 1));
                var a = text.GetRenderMatrix().Transform(local.TopLeft);
                var b = text.GetRenderMatrix().Transform(local.BottomRight);
                Assert.InRange(Math.Min(a.X, b.X), 18, extent.Width - 12);
                Assert.InRange(Math.Min(a.Y, b.Y), 8, extent.Height - 14);
                Assert.Contains(text.DocumentCharacterFromPoint(new(local.Left + local.Width / 2, local.Top + local.Height / 3)), new[] { at, at + 1 });
            }
        }
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    [InlineData("sideways-rl")]
    [InlineData("sideways-lr")]
    public void ReplacedInlineKeepsPhysicalDimensionsAndOriginalSource(string mode)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var original = new Border { Width = 14, Height = 22, Background = Brushes.Red };
        var host = new TextBlock { IsTextSelectionEnabled = true };
        host.Inlines.Add(new Run("漢")); host.Inlines.Add(new InlineUIContainer(original)); host.Inlines.Add(new Run("字"));
        Css.SetStyle(host, $"display:block;width:160px;height:160px;writing-mode:{mode};text-orientation:mixed;font-family:'Hiragino Sans';font-size:24px;line-height:40px;white-space:pre;");
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            host.Measure(new(160, 160)); host.Arrange(new Rect(0, 0, 160, 160));
        }
        Assert.Equal(new Size(14, 22), original.RenderSize);
        var box = Assert.Single(host.DocumentTextBounds(1, 1));
        Assert.Equal(14, box.Width, 3); Assert.Equal(22, box.Height, 3);
        Assert.Contains(host.DocumentCharacterFromPoint(new(box.Left + box.Width / 2, box.Top + box.Height / 3)), new[] { 1, 2 });
        Assert.InRange(box.Left, 0, 146); Assert.InRange(box.Top, 0, 138);
        host.Select(0, 3); Assert.Equal("漢\ufffc字", host.SelectedText);
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    [InlineData("sideways-rl")]
    [InlineData("sideways-lr")]
    public void OrthogonalInlineRetainsItsOwnHorizontalTextLayout(string mode)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var before = new TextBlock { Text = "漢" }; var after = new TextBlock { Text = "字" };
        var horizontal = new TextBlock { Text = "AB", IsTextSelectionEnabled = true };
        var host = new StackPanel(); host.Children.Add(before); host.Children.Add(horizontal); host.Children.Add(after);
        Css.SetStyle(before, "display:inline;"); Css.SetStyle(after, "display:inline;");
        Css.SetStyle(horizontal, "display:inline;writing-mode:horizontal-tb;width:64px;height:32px;");
        Css.SetStyle(host, $"display:block;width:160px;height:160px;writing-mode:{mode};font-family:'Hiragino Sans';font-size:24px;line-height:32px;white-space:pre;");
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            host.Measure(new(160, 160)); host.Arrange(new Rect(0, 0, 160, 160));
        }
        var a = Assert.Single(horizontal.DocumentTextBounds(0, 1)); var b = Assert.Single(horizontal.DocumentTextBounds(1, 1));
        Assert.Equal(a.Top, b.Top, 3); Assert.True(b.Left > a.Left, $"{mode}: {a} then {b}");
        Assert.Equal(new Size(64, 32), horizontal.RenderSize);
        horizontal.SelectAll(); Assert.Equal("AB", horizontal.SelectedText);
    }

    [Theory]
    [InlineData("vertical-rl", " ", "normal", false)]
    [InlineData("vertical-lr", "   ", "normal", true)]
    [InlineData("vertical-rl", " 12 ", "normal", true)]
    [InlineData("vertical-lr", " 12 ", "pre", false)]
    [InlineData("vertical-rl", "ß\r\nAB", "normal", true)]
    [InlineData("vertical-lr", "ß\r\nAB", "pre", false)]
    public void SharedCompositionIsAtomicAcrossWhitespaceAndDisplayExpansion(string mode, string source, string white, bool authored)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        FrameworkElement host; TextBlock selection; int offset;
        if (authored)
        {
            var text = new TextBlock { IsTextSelectionEnabled = true };
            text.Inlines.Add(new Run("漢")); var span = new Span(new Run(source)); text.Inlines.Add(span); text.Inlines.Add(new Run("字"));
            Css.SetStyle(span, "display:inline;text-combine-upright:all;text-transform:uppercase;");
            host = selection = text; offset = 1;
        }
        else
        {
            var panel = new StackPanel(); var first = new TextBlock { Text = "漢" }; var last = new TextBlock { Text = "字" };
            selection = new TextBlock { Text = source, IsTextSelectionEnabled = true };
            Css.SetStyle(first, "display:inline;"); Css.SetStyle(last, "display:inline;");
            Css.SetStyle(selection, "display:inline;text-combine-upright:all;text-transform:uppercase;");
            panel.Children.Add(first); panel.Children.Add(selection); panel.Children.Add(last); host = panel; offset = 0;
        }
        Css.SetStyle(host, $"display:block;width:160px;height:50px;writing-mode:{mode};font-family:'Hiragino Sans';font-size:32px;line-height:40px;white-space:{white};text-autospace:no-autospace;");
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            host.Measure(new(160, 50)); host.Arrange(new Rect(0, 0, 160, 50));
        }
        var whole = Assert.Single(selection.DocumentTextBounds(offset, source.Length));
        Assert.Equal(32, whole.Height, 3);
        for (int i = 0; i < source.Length; i++)
        {
            var part = Assert.Single(selection.DocumentTextBounds(offset + i, 1));
            Assert.Equal(whole, part);
        }
        Assert.Contains(selection.DocumentCharacterFromPoint(new(whole.Left + whole.Width / 2, whole.Top + 8)), new[] { offset, offset + source.Length });
        Assert.Contains(selection.DocumentCharacterFromPoint(new(whole.Left + whole.Width / 2, whole.Bottom - 8)), new[] { offset, offset + source.Length });
        selection.Select(offset, source.Length); Assert.Equal(source, selection.SelectedText);
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public void DecoratedProjectionDoesNotCombinePartialInheritedDigitSequences(string mode)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var host = new TextBlock { IsTextSelectionEnabled = true };
        var original = new Run("12"); var span = new Span(new Run("34"));
        host.Inlines.Add(original); host.Inlines.Add(span);
        Css.SetStyle(span, "display:inline;border:1px solid blue;");
        Set("digits 4");
        Assert.Equal(128, host.DocumentTextBounds(0, 4).Sum(rect => rect.Height), 3);
        Css.SetStyle(span, "display:inline;border:1px solid blue;text-combine-upright:digits 4;");
        Set("none");
        Assert.Equal(32, Assert.Single(host.DocumentTextBounds(2, 2)).Height, 3);
        Assert.Same(original, host.Inlines[0]); Assert.Same(span, host.Inlines[1]);
        host.SelectAll(); Assert.Equal("1234", host.SelectedText);
        void Set(string combine)
        {
            Css.SetStyle(host, $"display:block;width:180px;height:180px;writing-mode:{mode};text-orientation:upright;font-family:'Hiragino Sans';font-size:32px;line-height:40px;text-combine-upright:{combine};text-autospace:no-autospace;");
            for (int i = 0; i < 3; i++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
                host.Measure(new(180, 180)); host.Arrange(new Rect(0, 0, 180, 180));
            }
        }
    }

    [Theory]
    [InlineData(RenderBackend.Software)]
    [InlineData(RenderBackend.Metal)]
    public void VerticalRtlSoftLineBreaksKeepWholeNativeCaretCells(RenderBackend backend)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        const string source = "天地玄\u2028黄宇宙\u2028洪荒";
        using var paragraph = NativeTextParagraph.TryCreateVertical([new(source, "Hiragino Sans", 32, 400, 0, Colors.Black)],
            "Hiragino Sans", 32, 1000000, 40, TextAlignment.Left, FlowDirection.RightToLeft, noWrap: true);
        Assert.NotNull(paragraph); Assert.Equal(3, paragraph.Lines.Length);
        foreach (var row in paragraph.Lines)
        for (int i = 0; i < row.Metrics.Line.Length; i++)
        {
            int position = (int)row.Metrics.Line.TextPosition + i;
            Assert.Equal(i * 32, paragraph.Caret(row.Index, position, false).X, 3);
            Assert.Equal((i + 1) * 32, paragraph.Caret(row.Index, position + 1, true).X, 3);
            var box = Assert.Single(paragraph.Selection(row.Index, position, 1));
            Assert.Equal(i * 32, box.X, 3); Assert.Equal(32, box.Width, 3);
        }
    }

    [Theory]
    [InlineData("vertical-rl", false)]
    [InlineData("vertical-lr", false)]
    [InlineData("sideways-rl", false)]
    [InlineData("sideways-lr", false)]
    [InlineData("vertical-rl", true)]
    [InlineData("vertical-lr", true)]
    [InlineData("sideways-rl", true)]
    [InlineData("sideways-lr", true)]
    public void SharedSourcesFollowInlineAndBlockAxesAndRestoreAfterStyleChanges(string mode, bool authoredInlines)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        FrameworkElement host;
        TextBlock[] sources;
        if (authoredInlines)
        {
            var text = new TextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
            text.Inlines.Add(new Run("天地玄黄"));
            var marked = new Span(new Run("宇宙洪荒"));
            text.Inlines.Add(marked);
            Css.SetStyle(marked, "display:inline;border:1px solid #2859aa;background:#e8eef7;");
            host = text; sources = [text];
        }
        else
        {
            var first = new TextBlock { Text = "天地玄黄", IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
            var last = new TextBlock { Text = "宇宙洪荒", IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
            var panel = new StackPanel(); panel.Children.Add(first); panel.Children.Add(last);
            Css.SetStyle(first, "display:inline;"); Css.SetStyle(last, "display:inline;");
            host = panel; sources = [first, last];
        }
        SetMode("horizontal-tb"); var horizontal = Cell(0);
        for (int pass = 0; pass < 2; pass++)
        {
            SetMode(mode);
            var first = Cell(0); var second = Cell(1);
            Assert.Equal(first.Left, second.Left, 3);
            Assert.True(mode == "sideways-lr" ? second.Top < first.Top : second.Top > first.Top,
                $"{mode}, authored={authoredInlines}: {first} then {second}");
            var last = Cell(7);
            Assert.True(mode is "vertical-lr" or "sideways-lr" ? last.Left > first.Left : last.Left < first.Left,
                $"{mode}: column progression {first} to {last}");
            for (int i = 0; i < 8; i++)
            {
                var (source, index) = Source(i);
                var box = Assert.Single(source.DocumentTextBounds(index, 1));
                var point = new Point(box.Left + box.Width / 2, box.Top + box.Height / 3);
                Assert.Contains(source.DocumentCharacterFromPoint(point), new[] { index, index + 1 });
            }
            if (authoredInlines)
            {
                sources[0].Select(1, 5); Assert.Equal("地玄黄宇宙", sources[0].SelectedText);
                sources[0].Select(0, 0);
            }
            SetMode("horizontal-tb"); Assert.Equal(horizontal, Cell(0));
        }

        (TextBlock Text, int Index) Source(int index) => authoredInlines ? (sources[0], index) : (sources[index / 4], index % 4);
        Rect Cell(int index)
        {
            var (text, local) = Source(index);
            var box = Assert.Single(text.DocumentTextBounds(local, 1));
            Assert.True(host.GetRenderMatrix().TryInvert(out var inverse));
            var transform = Matrix.Multiply(text.GetRenderMatrix(), inverse);
            var a = transform.Transform(box.TopLeft); var b = transform.Transform(box.BottomRight);
            return new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        }
        void SetMode(string value)
        {
            Css.SetStyle(host, $"display:block;width:160px;height:96px;writing-mode:{value};text-orientation:mixed;font-family:'PingFang SC';font-size:24px;line-height:32px;");
            for (int i = 0; i < 3; i++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue();
                CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
                host.Measure(new Size(160, 96)); host.Arrange(new Rect(0, 0, 160, 96));
            }
        }
    }
}
