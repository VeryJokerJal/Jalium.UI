using System.Globalization;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssVerticalNativeParagraphTests
{
    private const string Family = "Hiragino Sans";
    private const double Size = 32;

    [Theory]
    [InlineData(RenderBackend.Software, "漢字（１２）ABC")]
    [InlineData(RenderBackend.Metal, "漢字（１２）ABC")]
    [InlineData(RenderBackend.Software, "Ae\u0301👩‍💻中")]
    [InlineData(RenderBackend.Metal, "Ae\u0301👩‍💻中")]
    [InlineData(RenderBackend.Software, "אבג漢字")]
    [InlineData(RenderBackend.Metal, "אבג漢字")]
    [InlineData(RenderBackend.Software, "A\u2067אבג\u2069漢")]
    [InlineData(RenderBackend.Metal, "A\u2067אבג\u2069漢")]
    public void VerticalShapingRetainsSourceClustersAndLogicalCaretGeometry(RenderBackend backend, string text)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var paragraph = Vertical(text, noWrap: true);
        Assert.NotNull(paragraph);
        Assert.Equal(text, paragraph.Text);
        var row = Assert.Single(paragraph.Lines).Metrics.Line;
        Assert.Equal((uint)text.Length, row.Length);
        Assert.True(row.Width > 0);
        var boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        for (var index = 0; index + 1 < boundaries.Length; index++)
        {
            int start = boundaries[index], end = boundaries[index + 1];
            var first = paragraph.Caret(0, start, false);
            var last = paragraph.Caret(0, end, true);
            Assert.True(float.IsFinite(first.X) && float.IsFinite(last.X));
            if (char.GetUnicodeCategory(text, start) == UnicodeCategory.Format) continue;
            Assert.True(Math.Abs(first.X - last.X) > 0);
            var selected = paragraph.Selection(0, start, end - start);
            Assert.NotEmpty(selected);
            Assert.Equal(Math.Abs(first.X - last.X), selected.Sum(rect => rect.Width), 3);
            var hit = paragraph.HitTest(0, (first.X * 3 + last.X) / 4);
            Assert.Contains((int)hit.TextPosition, new[] { start, end });
            // An interior UTF-16 code unit cannot become an independent caret.
            for (int inner = start + 1; inner < end; inner++)
                Assert.Equal(first.X, paragraph.Caret(0, inner, false).X, 3);
        }
    }

    [Theory]
    [InlineData(RenderBackend.Software)]
    [InlineData(RenderBackend.Metal)]
    public void ColumnWrappingUsesTheInlineConstraintAndPreservesHardBreaks(RenderBackend backend)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        const string text = "天地玄黄宇宙洪荒";
        using var paragraph = Vertical(text, inlineSize: 65);
        Assert.NotNull(paragraph); Assert.Equal(4, paragraph.Lines.Length);
        for (int index = 0; index < paragraph.Lines.Length; index++)
        {
            var row = paragraph.Lines[index].Metrics.Line;
            Assert.Equal((uint)(index * 2), row.TextPosition);
            Assert.Equal(2u, row.Length);
            Assert.InRange(row.Width, 63.9f, 64.1f);
            Assert.True(row.Height >= Size);
            if (index > 0) Assert.True(row.Y >= paragraph.Lines[index - 1].Metrics.Line.Y + row.Height - .01);
        }
        using var hard = Vertical("天地\r\n玄黄", noWrap: true);
        Assert.NotNull(hard); Assert.Equal(2, hard.Lines.Length);
        Assert.Equal(0u, hard.Lines[0].Metrics.Line.TextPosition);
        Assert.Equal(4u, hard.Lines[1].Metrics.Line.TextPosition);
        Assert.Equal(2u, hard.Lines[1].Metrics.Line.Length);
    }

    [Fact]
    public void MixedVerticalGlyphsDifferFromRotatedHorizontalInkAndSurviveRecordedReplay()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string text = "漢（字）AB";
        var paragraph = Vertical(text, noWrap: true);
        Assert.NotNull(paragraph);
        using var horizontal = NativeTextParagraph.TryCreate(Spans(text), Family, Size, 1000, Size,
            TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
        Assert.NotNull(horizontal);
        var actual = Pixels(context, dc => Draw(dc, paragraph, text));
        var sideways = Pixels(context, dc => Draw(dc, horizontal, text));
        Assert.True(actual.Where((_, index) => index % 4 == 3).Count(alpha => alpha != 0) > 100);
        Assert.True(actual.Zip(sideways).Count(pair => pair.First != pair.Second) > 100);
        Save("mixed-vertical.png", actual); Save("rotated-horizontal.png", sideways);
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); Draw(recorder, paragraph, text);
        var frame = recorder.Commit(); Assert.True(frame.IsFullyRecordable);
        paragraph.Dispose();
        Assert.Equal(actual, Pixels(context, dc => DrawingReplayer.Replay(frame, dc)));
        // Creating a vertical paragraph must not mutate a format shared with
        // subsequent horizontal paragraphs or their retained drawings.
        using var after = NativeTextParagraph.TryCreate(Spans(text), Family, Size, 1000, Size,
            TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
        Assert.NotNull(after);
        Assert.Equal(sideways, Pixels(context, dc => Draw(dc, after, text)));
    }

    [Fact]
    public void VerticalCreationRejectsInvalidConstraintsAndKeepsAnEmptyInsertionColumn()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        Assert.Null(Vertical("text", inlineSize: double.NaN));
        Assert.Null(Vertical("text", inlineSize: 0));
        Assert.Null(Vertical("text", inlineSize: -1));
        using var empty = NativeTextParagraph.TryCreateVertical([], Family, Size, 100, Size,
            TextAlignment.Left, FlowDirection.LeftToRight);
        Assert.NotNull(empty);
        var column = Assert.Single(empty.Lines).Metrics.Line;
        Assert.Equal(0u, column.Length); Assert.Equal(0, column.Width);
        Assert.True(column.Height >= Size);
        Assert.Equal(0, empty.Caret(0, 0, false).X);
    }

    private static NativeTextParagraph.Span[] Spans(string text) => [new(text, Family, Size, 400, 0, Colors.Black)];
    private static NativeTextParagraph? Vertical(string text, double inlineSize = 1000, bool noWrap = false)
        => NativeTextParagraph.TryCreateVertical(Spans(text), Family, Size, inlineSize, Size,
            TextAlignment.Left, FlowDirection.LeftToRight, noWrap);

    private static void Draw(DrawingContext context, NativeTextParagraph paragraph, string text)
    {
        // Logical inline X advances down; block Y advances left. CoreText
        // supplies vertical alternates and per-glyph mixed orientation.
        context.PushTransform(new MatrixTransform(new Matrix(0, 1, -1, 0, 96, 12)));
        try
        {
            foreach (var row in paragraph.Lines)
                context.DrawText(new FormattedText(text, Family, Size)
                {
                    Foreground = Brushes.Black,
                    PlatformTextLine = row.ForSource((int)row.Metrics.Line.TextPosition,
                        (int)row.Metrics.Line.Length, Colors.Black, 1)
                }, new Point(0, row.Metrics.Line.Y));
        }
        finally { context.Pop(); }
    }

    private static byte[] Pixels(RenderContext context, Action<DrawingContext> draw)
    {
        using var target = context.CreateOffscreenRenderTarget(128, 320);
        target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
        using (var dc = new RenderTargetDrawingContext(target, context)) { draw(dc); dc.Close(); }
        target.EndDraw(); var pixels = new byte[128 * 320 * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 128 * 4, out _, out _));
        return pixels;
    }

    private static void Save(string name, byte[] pixels)
    {
        var folder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        var bitmap = BitmapSource.Create(128, 320, 96, 96, PixelFormats.Bgra32, null, pixels, 128 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name)); encoder.Save(stream);
    }
}
