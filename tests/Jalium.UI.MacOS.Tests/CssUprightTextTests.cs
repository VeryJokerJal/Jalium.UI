using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssUprightTextTests
{
    public static IEnumerable<object[]> FontsAndDirections()
    {
        foreach (var backend in new[] { RenderBackend.Software, RenderBackend.Metal })
        foreach (var family in new[] { "Hiragino Sans", "Arial", "Times New Roman", "Georgia" })
        foreach (var direction in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            yield return [backend, family, direction];
    }

    [Theory]
    [MemberData(nameof(FontsAndDirections))]
    public void NativeUprightUsesOriginalClustersAndLtrCells(RenderBackend backend, string family, FlowDirection direction)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        const string source = "Ae\u0301fiאבب👩‍💻中";
        using var paragraph = NativeTextParagraph.TryCreateUpright([new(source, family, 32, 400, 0, Colors.Black)],
            family, 32, 1000, 40, TextAlignment.Left, direction, noWrap: true);
        Assert.NotNull(paragraph);
        Assert.Equal(source, paragraph.Text);
        var row = Assert.Single(paragraph.Lines);
        var boundaries = StringInfo.ParseCombiningCharacters(source).Append(source.Length).ToArray();
        Assert.Equal((boundaries.Length - 1) * 32, row.Metrics.Line.Width, 3);
        for (int i = 0; i + 1 < boundaries.Length; i++)
        {
            int first = boundaries[i], last = boundaries[i + 1];
            Assert.Equal(i * 32, paragraph.Caret(0, first, false).X, 3);
            Assert.Equal((i + 1) * 32, paragraph.Caret(0, last, true).X, 3);
            Assert.Equal(first, (int)paragraph.HitTest(0, i * 32 + 4).TextPosition);
            Assert.Equal(32, Assert.Single(paragraph.Selection(0, first, last - first)).Width, 3);
            for (int inner = first + 1; inner < last; inner++)
                Assert.Equal(i * 32, paragraph.Caret(0, inner, false).X, 3);
        }
        Assert.All(row.Fragments, fragment => Assert.True(fragment.X >= 0 && fragment.X + fragment.Width <= row.Metrics.Line.Width + .01));
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public void ActualUprightControlUsesLtrButPreservesComputedRtlAndSourceHits(string mode)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source = "Ae\u0301אבب👩‍💻中";
        var text = Text(source); text.FlowDirection = FlowDirection.RightToLeft;
        Css.SetStyle(text, $"writing-mode:{mode};text-orientation:upright;white-space:pre;line-height:40px;text-align:start;");
        Layout(text, 180, 420);
        Assert.Equal(FlowDirection.RightToLeft, text.FlowDirection);
        Assert.Equal(FlowDirection.LeftToRight, CssWritingModeProperties.GetUsedFlowDirection(text));
        var boundaries = StringInfo.ParseCombiningCharacters(source).Append(source.Length).ToArray();
        for (int i = 0; i + 1 < boundaries.Length; i++)
        {
            var rect = Assert.Single(text.DocumentTextBounds(boundaries[i], boundaries[i + 1] - boundaries[i]));
            Assert.Equal(i * 32, rect.Top, 3);
            Assert.Equal(32, rect.Height, 3);
            Assert.Contains(text.DocumentCharacterFromPoint(new Point(rect.Left + rect.Width / 2, rect.Top + 4)),
                new[] { boundaries[i], boundaries[i + 1] });
        }
        text.SelectAll(); Assert.Equal(source, text.SelectedText);
        text.Select(1, 2); Assert.Equal("e\u0301", text.SelectedText);
        Save(mode + "-rtl-upright-selection.png", Pixels(context, text.Render));
    }

    [Fact]
    public void UprightIgnoresBidiControlsAndKeepsHardBreakSourceOffsets()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        const string source = "A\u2067אב\u2069Z\r\nfi";
        using var paragraph = NativeTextParagraph.TryCreateUpright([new(source, "Arial", 32, 400, 0, Colors.Black)],
            "Arial", 32, 1000, 40, TextAlignment.Left, FlowDirection.RightToLeft, noWrap: true);
        Assert.NotNull(paragraph); Assert.Equal(source, paragraph.Text); Assert.Equal(2, paragraph.Lines.Length);
        Assert.Equal(128, paragraph.Lines[0].Metrics.Line.Width, 3);
        Assert.Equal(8u, paragraph.Lines[1].Metrics.Line.TextPosition);
        Assert.Equal(64, paragraph.Lines[1].Metrics.Line.Width, 3);
        Assert.Equal(32, paragraph.Caret(0, 2, false).X, 3);
        Assert.Equal(64, paragraph.Caret(0, 3, false).X, 3);
    }

    [Fact]
    public void UprightChangesRefreshSpacingAndRetainedDrawings()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var text = Text("Ae\u0301fiאבب👩‍💻中");
        Set("upright");
        var first = Assert.Single(text.DocumentTextBounds(0, 1));
        var accent = Assert.Single(text.DocumentTextBounds(1, 2));
        Assert.Equal(32, accent.Top - first.Top, 3);
        var original = Pixels(context, text.Render);
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); text.Render(recorder);
        var frame = recorder.Commit(); Assert.True(frame.IsFullyRecordable);
        Set("mixed"); Assert.False(original.SequenceEqual(Pixels(context, text.Render)));
        Assert.Equal(original, Pixels(context, dc => DrawingReplayer.Replay(frame, dc)));
        Set("upright", "letter-spacing:6px;");
        // Selection carets bisect the inter-character gap. The first cell
        // has one half gap; interior cells have a complete 6px gap.
        Assert.Equal(35, Assert.Single(text.DocumentTextBounds(1, 2)).Top - Assert.Single(text.DocumentTextBounds(0, 1)).Top, 3);
        Assert.Equal(38, Assert.Single(text.DocumentTextBounds(3, 1)).Top - Assert.Single(text.DocumentTextBounds(1, 2)).Top, 3);
        Assert.Equal(9 * 32 + 8 * 6, Assert.Single(text.DocumentTextBounds(0, text.Text.Length)).Height, 3);
        text.SelectAll(); Assert.Equal(text.Text, text.SelectedText);
        Save("upright-recorded.png", original);
        void Set(string orientation, string extra = "")
        {
            Css.SetStyle(text, $"writing-mode:vertical-lr;text-orientation:{orientation};white-space:pre;line-height:40px;{extra}");
            Layout(text, 180, 420);
        }
    }

    private static TextBlock Text(string source) => new()
    {
        Text = source, FontFamily = new FontFamily("Arial"), FontSize = 32,
        Foreground = Brushes.Black, IsTextSelectionEnabled = true,
    };
    private static void Layout(TextBlock text, double width, double height)
    {
        text.Measure(new Size(width, height)); text.Arrange(new Rect(0, 0, width, height));
    }
    private static byte[] Pixels(RenderContext context, Action<DrawingContext> draw)
    {
        using var target = context.CreateOffscreenRenderTarget(460, 460);
        target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
        using (var dc = new RenderTargetDrawingContext(target, context)) { draw(dc); dc.Close(); }
        target.EndDraw(); var pixels = new byte[460 * 460 * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 460 * 4, out _, out _));
        return pixels;
    }
    private static void Save(string name, byte[] pixels)
    {
        var folder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        var bitmap = BitmapSource.Create(460, 460, 96, 96, PixelFormats.Bgra32, null, pixels, 460 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name)); encoder.Save(stream);
    }
}
