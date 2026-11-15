using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssTextBlockWritingModeTests
{
    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    [InlineData("sideways-rl")]
    [InlineData("sideways-lr")]
    public void WritingModesReachActualLayoutPaintSourceRectanglesAndHits(string mode)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text("天地玄黄");
        Css.SetStyle(text, "writing-mode:horizontal-tb;white-space:pre;");
        Layout(text); var horizontal = Pixels(context, text.Render);
        var ordinarySize = Measure("horizontal-tb");
        var verticalSize = Measure(mode);
        Assert.True(verticalSize.Width < ordinarySize.Width, $"{mode}: {verticalSize} vs {ordinarySize}");
        Assert.True(verticalSize.Height > ordinarySize.Height, $"{mode}: {verticalSize} vs {ordinarySize}");
        for (int pass = 0; pass < 2; pass++)
        {
            Css.SetStyle(text, $"writing-mode:{mode};white-space:pre;"); Layout(text);
            var vertical = Pixels(context, text.Render);
            Assert.False(horizontal.SequenceEqual(vertical));
            var first = Assert.Single(text.DocumentTextBounds(0, 1));
            var second = Assert.Single(text.DocumentTextBounds(1, 1));
            Assert.Equal(first.Left, second.Left, 3);
            if (mode == "sideways-lr") Assert.True(second.Top < first.Top);
            else Assert.True(second.Top > first.Top);
            for (int index = 0; index < text.Text.Length; index++)
            {
                var cell = Assert.Single(text.DocumentTextBounds(index, 1));
                var point = new Point(cell.Left + cell.Width / 2, cell.Top + cell.Height / 3);
                Assert.Contains(text.DocumentCharacterFromPoint(point), new[] { index, index + 1 });
                Assert.NotNull(text.GetPositionFromPoint(point, snapToText: false));
            }
            text.Select(1, 2); Assert.Equal("地玄", text.SelectedText);
            Assert.False(vertical.SequenceEqual(Pixels(context, text.Render)));
            text.Select(0, 0);
            Save(mode + ".png", vertical);
            Css.SetStyle(text, "writing-mode:horizontal-tb;white-space:pre;"); Layout(text);
            Assert.Equal(horizontal, Pixels(context, text.Render));
        }

        Size Measure(string value)
        {
            var sample = Text("天地玄黄");
            Css.SetStyle(sample, $"writing-mode:{value};white-space:pre;");
            sample.Measure(new Size(180, 180)); return sample.DesiredSize;
        }
    }

    [Theory]
    [InlineData("vertical-rl", false)]
    [InlineData("vertical-lr", true)]
    [InlineData("sideways-rl", false)]
    [InlineData("sideways-lr", true)]
    public void ColumnsFollowTheBlockAxisAndKeepPhysicalPadding(string mode, bool rightwards)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text("天地玄黄宇宙洪荒"); text.Padding = new Thickness(5, 7, 11, 13);
        Css.SetStyle(text, $"writing-mode:{mode};white-space:pre-wrap;");
        text.Measure(new Size(200, 84)); text.Arrange(new Rect(0, 0, 200, 84));
        var first = Assert.Single(text.DocumentTextBounds(0, 1));
        var next = Assert.Single(text.DocumentTextBounds(2, 1));
        if (rightwards) Assert.True(next.Left > first.Left, $"{mode}: {first}, {next}");
        else Assert.True(next.Left < first.Left, $"{mode}: {first}, {next}");
        if (mode == "sideways-lr") Assert.Equal(71, first.Bottom, 3);
        else Assert.Equal(7, first.Top, 3);
        if (rightwards) Assert.Equal(5, first.Left, 3);
        else Assert.Equal(189, first.Right, 3);
        var hit = new Point(next.Left + next.Width / 2, next.Top + next.Height / 3);
        Assert.Contains(text.DocumentCharacterFromPoint(hit), new[] { 2, 3 });
        Assert.NotNull(text.GetPositionFromPoint(hit, snapToText: false));
        Assert.Null(text.GetPositionFromPoint(new Point(201, 20), snapToText: false));
        Assert.Contains(Pixels(context, text.Render), value => value != 0);
    }

    [Theory]
    [InlineData("vertical-rl", true)]
    [InlineData("vertical-lr", true)]
    [InlineData("sideways-rl", false)]
    [InlineData("sideways-lr", false)]
    [InlineData("vertical-rl", false)]
    [InlineData("vertical-lr", false)]
    public void ControlPixelsMatchIndependentlyPlacedNativeGlyphs(string mode, bool verticalForms)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source = "漢（A）";
        var text = Text(source);
        Css.SetStyle(text, $"writing-mode:{mode};white-space:pre;line-height:40px;text-orientation:{(verticalForms ? "mixed" : "sideways")};"); Layout(text);
        NativeTextParagraph.Span[] spans = [new(source, "Hiragino Sans", 32, 400, 0, Colors.Black)];
        using var paragraph = verticalForms
            ? NativeTextParagraph.TryCreateVertical(spans, "Hiragino Sans", 32, 1000, 40,
                TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true)
            : NativeTextParagraph.TryCreate(spans, "Hiragino Sans", 32, 1000, 40,
                TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
        Assert.NotNull(paragraph);
        var row = Assert.Single(paragraph.Lines);
        var font = TextMeasurement.GetFontMetrics("Hiragino Sans", 32, 400, 0);
        var baseline = verticalForms ? 20 : font.Ascent + (40 - font.Ascent - font.Descent) / 2;
        // These placements follow the physical axes directly. No TextBlock
        // geometry or writing-mode conversion helper supplies the reference.
        var matrix = mode switch
        {
            "vertical-lr" => new Matrix(0, 1, -1, 0, 40, 0),
            "sideways-lr" => new Matrix(0, -1, 1, 0, 0, 180),
            _ => new Matrix(0, 1, -1, 0, 180, 0),
        };
        var reference = Pixels(context, dc =>
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, 180, 180)));
            dc.PushTransform(new MatrixTransform(matrix));
            dc.DrawText(new FormattedText(source, "Hiragino Sans", 32)
            {
                Foreground = Brushes.Black,
                PlatformTextLine = row.ForSource(0, source.Length, Colors.Black, 1),
            }, new Point(0, baseline - row.Metrics.Baseline));
            dc.Pop(); dc.Pop();
        });
        Assert.Equal(reference, Pixels(context, text.Render));
        Save(mode + (verticalForms ? "-mixed" : "-sideways") + "-native-reference.png", reference);
    }

    [Theory]
    [InlineData("A\u2067אבג\u2069漢")]
    [InlineData("Ae\u0301👩‍💻中")]
    [InlineData("אבג漢字")]
    public void MixedVerticalTextKeepsClusterSourceRangesAndBidiHits(string source)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text(source);
        Css.SetStyle(text, "writing-mode:vertical-lr;white-space:pre;");
        text.Measure(new Size(180, 360)); text.Arrange(new Rect(0, 0, 180, 360));
        var boundaries = StringInfo.ParseCombiningCharacters(source).Append(source.Length).ToArray();
        for (int i = 0; i + 1 < boundaries.Length; i++)
        {
            int first = boundaries[i], last = boundaries[i + 1];
            if (char.GetUnicodeCategory(source, first) == UnicodeCategory.Format) continue;
            var bounds = text.DocumentTextBounds(first, last - first);
            Assert.NotEmpty(bounds);
            foreach (var rect in bounds)
            {
                Assert.True(rect.Width > 0 && rect.Height > 0);
                var point = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 4);
                Assert.Contains(text.DocumentCharacterFromPoint(point), new[] { first, last });
                Assert.NotNull(text.GetPositionFromPoint(point, snapToText: false));
            }
        }
        text.SelectAll(); Assert.Equal(source, text.SelectedText);
        text.Select(0, 0); Save("clusters-" + source.Length + ".png", Pixels(context, text.Render));
    }

    [Fact]
    public void PlainStyledRunsRetainTheirColorsInVerticalColumns()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text("");
        text.Inlines.Add(new Run("天地") { Foreground = Brushes.Red });
        text.Inlines.Add(new Run("玄黄") { Foreground = Brushes.Blue });
        Css.SetStyle(text, "writing-mode:vertical-rl;white-space:pre;line-height:40px;"); Layout(text);
        var pixels = Pixels(context, text.Render);
        var first = Assert.Single(text.DocumentTextBounds(0, 1));
        var second = Assert.Single(text.DocumentTextBounds(2, 1));
        Assert.Equal(first.Left, second.Left, 3); Assert.True(second.Top > first.Top);
        Assert.True(CountColor(pixels, first, red: true) > 20);
        Assert.True(CountColor(pixels, second, red: false) > 20);
        Save("styled-runs.png", pixels);
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    [InlineData("sideways-rl")]
    [InlineData("sideways-lr")]
    public void SelectionForegroundStaysInsideItsPhysicalSourceCells(string mode)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text("天地玄黄");
        var root = new Border { Child = text };
        Css.SetStyle(text, $"writing-mode:{mode};white-space:pre;line-height:40px;");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("TextBlock::selection{color:red;background-color:transparent;}"));
        LayoutRoot(root);
        Assert.NotNull(CssSelection.For(text));
        text.Select(1, 2);
        var pixels = Pixels(context, root.Render);
        Save(mode + "-selection.png", pixels);
        foreach (int i in new[] { 1, 2 })
            Assert.True(CountColor(pixels, Assert.Single(text.DocumentTextBounds(i, 1)), red: true) > 20,
                $"{mode}: selected source {i} has no red glyph ink");
        foreach (int i in new[] { 0, 3 })
            Assert.Equal(0, CountColor(pixels, Assert.Single(text.DocumentTextBounds(i, 1)), red: true));
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    [InlineData("sideways-rl")]
    [InlineData("sideways-lr")]
    public void SelectionUnderlineFollowsTheSelectedVerticalSpan(string mode)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text("天地玄黄");
        text.Foreground = Brushes.Transparent;
        var root = new Border { Child = text };
        Css.SetStyle(text, $"writing-mode:{mode};white-space:pre;line-height:40px;");
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
            TextBlock::selection {color:transparent;background-color:transparent;
                text-decoration-line:underline;text-decoration-color:red;
                text-decoration-thickness:2px;text-underline-offset:4px;text-decoration-skip-ink:none;}
            """));
        LayoutRoot(root);
        Assert.NotNull(CssSelection.For(text));
        text.Select(1, 2);
        var pixels = Pixels(context, root.Render);
        Save(mode + "-selection-underline.png", pixels);
        var selected = Rect.Union(Assert.Single(text.DocumentTextBounds(1, 1)), Assert.Single(text.DocumentTextBounds(2, 1)));
        Assert.True(CountColor(pixels, selected, red: true) > 30);
        var stroke = RedInkBounds(pixels);
        Assert.InRange(stroke.Width, 1, 3);
        Assert.InRange(stroke.Height, selected.Height - 2, selected.Height + 2);
        Assert.InRange(stroke.Top, selected.Top - 1, selected.Top + 1);
        foreach (int i in new[] { 0, 3 })
            Assert.Equal(0, CountColor(pixels, Assert.Single(text.DocumentTextBounds(i, 1)), red: true));
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    [InlineData("sideways-rl")]
    [InlineData("sideways-lr")]
    public void SelectedForegroundFollowsItsSourceAcrossWrappedColumns(string mode)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text("天地玄黄宇宙洪荒");
        Css.SetStyle(text, $"writing-mode:{mode};white-space:pre-wrap;line-height:40px;");
        var root = new Border { Child = text, Height = 84 };
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("TextBlock::selection{color:red;background:transparent;}"));
        LayoutRoot(root);
        text.Select(1, 4);
        var pixels = Pixels(context, root.Render);
        for (int i = 0; i < text.Text.Length; i++)
        {
            var ink = CountColor(pixels, Assert.Single(text.DocumentTextBounds(i, 1)), red: true);
            if (i is >= 1 and < 5) Assert.True(ink > 20, $"{mode}: source {i} lost selected ink at a column boundary");
            else Assert.Equal(0, ink);
        }
        Save(mode + "-wrapped-selection.png", pixels);
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    [InlineData("sideways-rl")]
    [InlineData("sideways-lr")]
    public void LongColumnsRespectThePhysicalViewportClip(string mode)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text("天地玄黄宇宙洪荒");
        Css.SetStyle(text, $"writing-mode:{mode};white-space:pre;line-height:40px;");
        text.Measure(new Size(140, 75)); text.Arrange(new Rect(0, 0, 140, 75));
        var pixels = Pixels(context, text.Render);
        Assert.Contains(pixels.Where((_, i) => i % 4 == 3), alpha => alpha != 0);
        for (int y = 0; y < 220; y++)
        for (int x = 0; x < 220; x++)
            if (x >= 140 || y >= 75) Assert.Equal(0, pixels[(y * 220 + x) * 4 + 3]);
    }

    [Fact]
    public void InheritedWritingModeInvalidatesPreviouslyMeasuredText()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text("天地玄黄");
        Css.SetStyle(text, "white-space:pre;line-height:40px;");
        var root = new Border { Child = text };
        Arrange(); var horizontal = Pixels(context, root.Render);
        Assert.Contains(horizontal, value => value != 0);
        for (int pass = 0; pass < 2; pass++)
        {
            Css.SetStyle(root, "writing-mode:vertical-lr;"); Arrange();
            var first = Assert.Single(text.DocumentTextBounds(0, 1));
            var second = Assert.Single(text.DocumentTextBounds(1, 1));
            Assert.Equal(first.Left, second.Left, 3); Assert.True(second.Top > first.Top);
            Assert.False(horizontal.SequenceEqual(Pixels(context, root.Render)));
            Css.SetStyle(root, "writing-mode:horizontal-tb;"); Arrange();
            Assert.Equal(horizontal, Pixels(context, root.Render));
        }
        void Arrange() => LayoutRoot(root);
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    [InlineData("sideways-rl")]
    [InlineData("sideways-lr")]
    public void AccessKeyUnderlineUsesThePhysicalVerticalBaseline(string mode)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var access = new AccessText
        {
            Text = "_天地", FontFamily = new FontFamily("Hiragino Sans"), FontSize = 32,
            Foreground = Brushes.Black,
        };
        Css.SetStyle(access, $"writing-mode:{mode};white-space:pre;line-height:40px;");
        access.Measure(new Size(180, 180)); access.Arrange(new Rect(0, 0, 180, 180));
        var capture = new Lines(); access.Render(capture);
        var line = Assert.Single(capture.Values);
        Assert.Equal(line.Start.X, line.End.X, 3);
        Assert.True(Math.Abs(line.Start.Y - line.End.Y) > 20);
        Assert.Contains(Pixels(context, access.Render), value => value != 0);
    }

    private sealed class Lines : DrawingContextAdapter
    {
        internal readonly List<(Point Start, Point End)> Values = [];
        public override void DrawLine(Pen pen, Point start, Point end) => Values.Add((start, end));
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawText(FormattedText formattedText, Point origin) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }

    [Fact]
    public void VerticalSpacingAndLengthChangingTransformsKeepTheOriginalSource()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text("aßb");
        Css.SetStyle(text, "writing-mode:vertical-rl;white-space:pre;text-transform:uppercase;"); Layout(text);
        var first = Assert.Single(text.DocumentTextBounds(0, 1));
        var sharp = Assert.Single(text.DocumentTextBounds(1, 1));
        Assert.True(sharp.Height > first.Height);
        var ordinary = Pixels(context, text.Render);
        Css.SetStyle(text, "writing-mode:vertical-rl;white-space:pre;text-transform:uppercase;letter-spacing:6px;"); Layout(text);
        var spaced = Assert.Single(text.DocumentTextBounds(1, 1));
        Assert.True(spaced.Top > sharp.Top);
        Assert.False(ordinary.SequenceEqual(Pixels(context, text.Render)));
        text.SelectAll(); Assert.Equal("aßb", text.SelectedText); Assert.Equal("aßb", text.Text);
    }

    [Fact]
    public void VerticalTextRecordingRetainsThePriorModeAndNativeParagraph()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text("漢（字）AB");
        Css.SetStyle(text, "writing-mode:vertical-lr;white-space:pre;"); Layout(text);
        var original = Pixels(context, text.Render);
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); text.Render(recorder);
        var frame = recorder.Commit(); Assert.True(frame.IsFullyRecordable);
        Css.SetStyle(text, "writing-mode:horizontal-tb;white-space:pre;"); Layout(text);
        Assert.False(original.SequenceEqual(Pixels(context, text.Render)));
        Assert.Equal(original, Pixels(context, dc => DrawingReplayer.Replay(frame, dc)));
    }

    private static TextBlock Text(string value) => new()
    {
        Text = value, FontFamily = new FontFamily("Hiragino Sans"), FontSize = 32,
        Foreground = Brushes.Black, IsTextSelectionEnabled = true,
    };

    private static void Layout(TextBlock text)
    {
        text.Measure(new Size(180, 180)); text.Arrange(new Rect(0, 0, 180, 180));
    }

    private static void LayoutRoot(FrameworkElement root)
    {
        for (int pass = 0; pass < 4; pass++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new Size(180, 180)); root.Arrange(new Rect(0, 0, 180, 180)); root.UpdateLayout();
        }
    }

    private static byte[] Pixels(RenderContext context, Action<DrawingContext> draw)
    {
        using var target = context.CreateOffscreenRenderTarget(220, 220);
        target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
        using (var dc = new RenderTargetDrawingContext(target, context)) { draw(dc); dc.Close(); }
        target.EndDraw(); var pixels = new byte[220 * 220 * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 220 * 4, out _, out _));
        return pixels;
    }

    private static int CountColor(byte[] pixels, Rect bounds, bool red)
    {
        int result = 0;
        for (int y = Math.Max(0, (int)Math.Ceiling(bounds.Top)); y < Math.Min(220, (int)Math.Floor(bounds.Bottom)); y++)
        for (int x = Math.Max(0, (int)Math.Ceiling(bounds.Left)); x < Math.Min(220, (int)Math.Floor(bounds.Right)); x++)
        {
            int p = (y * 220 + x) * 4;
            if (pixels[p + 3] > 100 && (red ? pixels[p + 2] > pixels[p] + 80 : pixels[p] > pixels[p + 2] + 80)) result++;
        }
        return result;
    }

    private static Rect RedInkBounds(byte[] pixels)
    {
        int left = 220, top = 220, right = -1, bottom = -1;
        for (int y = 0; y < 220; y++)
        for (int x = 0; x < 220; x++)
        {
            int p = (y * 220 + x) * 4;
            if (pixels[p + 3] <= 100 || pixels[p + 2] <= pixels[p] + 80) continue;
            left = Math.Min(left, x); right = Math.Max(right, x);
            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        Assert.True(right >= left && bottom >= top, "Expected visible red ink");
        return new Rect(left, top, right - left + 1, bottom - top + 1);
    }

    private static void Save(string name, byte[] pixels)
    {
        var folder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        var bitmap = BitmapSource.Create(220, 220, 96, 96, PixelFormats.Bgra32, null, pixels, 220 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name)); encoder.Save(stream);
    }
}
