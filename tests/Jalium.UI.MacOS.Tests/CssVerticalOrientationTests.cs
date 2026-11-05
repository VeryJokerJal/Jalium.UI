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
public sealed class CssVerticalOrientationTests
{
    [Fact]
    public void ScalarOrientationMatchesPinnedUnicode18ForEveryCodePoint()
    {
        var upright = new bool[0x110000];
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Assets", "VerticalOrientation.txt")))
        {
            var data = line.Split('#')[0].Trim();
            if (data.Length == 0) continue;
            var parts = data.Split(';', StringSplitOptions.TrimEntries);
            var limits = parts[0].Split("..");
            int first = int.Parse(limits[0], NumberStyles.HexNumber);
            int last = int.Parse(limits[^1], NumberStyles.HexNumber);
            Array.Fill(upright, parts[1] != "R", first, last - first + 1);
        }
        Assert.Contains(true, upright);
        for (int scalar = 0; scalar < upright.Length; scalar++)
            if (CssWritingModeProperties.IsNaturallyUprightInVerticalMixed(scalar) != upright[scalar])
                Assert.Fail($"Incorrect Unicode 18 vertical orientation at U+{scalar:X4}");
        Assert.False(CssWritingModeProperties.IsNaturallyUprightInVerticalMixed(-1));
        Assert.False(CssWritingModeProperties.IsNaturallyUprightInVerticalMixed(0x110000));
    }

    [Theory]
    [InlineData("A\u0301", false)]
    [InlineData("A\uFE0F", false)]
    [InlineData("漢\u0301", true)]
    [InlineData("1\u20E3", true)]
    [InlineData("1\uFE0F\u20E3", true)]
    [InlineData("A\u20DD", true)]
    [InlineData("👩‍💻", true)]
    [InlineData("", false)]
    public void WholeClusterOrientationPreservesMarksAndTheEnclosingException(string cluster, bool upright)
        => Assert.Equal(upright, CssWritingModeProperties.IsNaturallyUprightInVerticalMixed(cluster.AsSpan()));

    public static TheoryData<RenderBackend, string> Fonts => new()
    {
        { RenderBackend.Software, "Hiragino Sans" },
        { RenderBackend.Software, "Arial" },
        { RenderBackend.Software, "Times New Roman" },
        { RenderBackend.Software, "Georgia" },
    };

    [Theory]
    [MemberData(nameof(Fonts))]
    public void ActualTextBlockRotatesHorizontalScriptsUsingTheirHorizontalAdvance(RenderBackend backend, string family)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        const string source = "office AV e\u0301";
        using var reference = NativeTextParagraph.TryCreate([new(source, family, 32, 400, 0, Colors.Black)],
            family, 32, 1000, 40, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
        Assert.NotNull(reference);
        var text = Text(source, family);
        SetMode(text, "horizontal-tb");
        text.Measure(new Size(440, 40)); text.Arrange(new Rect(0, 0, 440, 40));
        var horizontalAdvance = text.DesiredSize.Width;
        var horizontalInk = Ink(Pixels(context, text.Render));
        foreach (var mode in new[] { "vertical-rl", "vertical-lr" })
        {
            SetMode(text, mode);
            text.Measure(new Size(40, 440)); text.Arrange(new Rect(0, 0, 40, 440));
            Assert.Equal(horizontalAdvance, text.DesiredSize.Height, 2);
            Assert.Equal(reference.Lines[0].Metrics.Line.Width, Assert.Single(text.DocumentTextBounds(0, source.Length)).Height, 2);
            var pixels = Pixels(context, text.Render);
            var verticalInk = Ink(pixels);
            Assert.InRange(Math.Abs(verticalInk.Width - horizontalInk.Height), 0, 2);
            Assert.InRange(Math.Abs(verticalInk.Height - horizontalInk.Width), 0, 2);
            Assert.True(verticalInk.Left >= 0 && verticalInk.Right <= 40, $"{family} {mode}: {verticalInk}");
            Assert.Equal(source, text.Text);
            text.SelectAll(); Assert.Equal(source, text.SelectedText); text.Select(0, 0);
            Save($"{family}-{backend}-{mode}.png", pixels);
        }
    }

    [Theory]
    [MemberData(nameof(Fonts))]
    public void MixedClustersRetainTheirSourceCellsSelectionHitsAndRecording(RenderBackend backend, string family)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        const string source = "ABe\u0301👩‍💻中（12）אב";
        var text = Text(source, family);
        SetMode(text, "vertical-lr");
        text.Measure(new Size(160, 440)); text.Arrange(new Rect(0, 0, 160, 440));
        var boundaries = StringInfo.ParseCombiningCharacters(source).Append(source.Length).ToArray();
        foreach (var (start, end) in boundaries.Zip(boundaries.Skip(1)))
        {
            var cell = Assert.Single(text.DocumentTextBounds(start, end - start));
            Assert.True(cell.Width > 0 && cell.Height > 0);
            var point = new Point(cell.Left + cell.Width / 2, cell.Top + cell.Height / 4);
            Assert.Contains(text.DocumentCharacterFromPoint(point), new[] { start, end });
            for (int inner = start + 1; inner < end; inner++)
                Assert.Equal(cell, Assert.Single(text.DocumentTextBounds(inner, 1)));
            text.Select(start, end - start); Assert.Equal(source[start..end], text.SelectedText);
        }
        text.Select(0, 0);
        var original = Pixels(context, text.Render);
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); text.Render(recorder);
        var frame = recorder.Commit(); Assert.True(frame.IsFullyRecordable);
        SetMode(text, "horizontal-tb");
        text.Measure(new Size(440, 40)); text.Arrange(new Rect(0, 0, 440, 40));
        Assert.False(original.SequenceEqual(Pixels(context, text.Render)));
        Assert.Equal(original, Pixels(context, dc => DrawingReplayer.Replay(frame, dc)));
        Save($"{family}-{backend}-clusters.png", original);
    }

    private static void SetMode(TextBlock text, string mode)
        => Css.SetStyle(text, $"writing-mode:{mode};text-orientation:mixed;white-space:pre;line-height:40px;");

    private static TextBlock Text(string text, string family) => new()
    {
        Text = text, FontFamily = new FontFamily(family), FontSize = 32,
        Foreground = Brushes.Black, IsTextSelectionEnabled = true,
    };

    private static byte[] Pixels(RenderContext context, Action<DrawingContext> draw)
    {
        using var target = context.CreateOffscreenRenderTarget(460, 460);
        target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
        using (var dc = new RenderTargetDrawingContext(target, context)) { draw(dc); dc.Close(); }
        target.EndDraw(); var pixels = new byte[460 * 460 * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 460 * 4, out _, out _));
        return pixels;
    }

    private static Rect Ink(byte[] pixels)
    {
        int left = 460, top = 460, right = -1, bottom = -1;
        for (int y = 0; y < 460; y++)
        for (int x = 0; x < 460; x++)
        {
            if (pixels[(y * 460 + x) * 4 + 3] <= 20) continue;
            left = Math.Min(left, x); right = Math.Max(right, x);
            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        Assert.True(right >= left && bottom >= top, "Actual glyph pixels are required");
        return new Rect(left, top, right - left + 1, bottom - top + 1);
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
