using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssVerticalMetricsTests
{
    public static IEnumerable<object[]> Modes()
    {
        foreach (var backend in new[] { RenderBackend.Software, RenderBackend.Metal })
        foreach (var mode in new[] { "vertical-rl", "vertical-lr" })
        foreach (var orientation in new[] { "mixed", "upright" })
            yield return [backend, mode, orientation];
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void NonuniformVerticalCellsDriveControlGeometryAndSelection(RenderBackend backend, string mode, string orientation)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var font = Font("MetricsMixed");
        var text = Box(font.Family, "中文国。");
        Css.SetStyle(text, Style(mode, orientation)); Layout(text);
        var advances = new[] { 38.4, 19.2, 51.2, 25.6 };
        double top = 0;
        for (int i = 0; i < advances.Length; i++)
        {
            var cell = Assert.Single(text.DocumentTextBounds(i, 1));
            Assert.Equal(top, cell.Top, 3); Assert.Equal(advances[i], cell.Height, 3);
            Assert.Contains(text.DocumentCharacterFromPoint(new Point(cell.Left + cell.Width / 2, cell.Top + cell.Height / 4)), new[] { i, i + 1 });
            text.Select(i, 1); Assert.Equal(text.Text.Substring(i, 1), text.SelectedText);
            top += advances[i];
        }
        Assert.Equal(134.4, Assert.Single(text.DocumentTextBounds(0, 4)).Height, 3);
        text.SelectAll(); Assert.Equal("中文国。", text.SelectedText);
        text.Select(0, 0);
        if (backend == RenderBackend.Software) Save($"{mode}-{orientation}-mixed.png", Pixels(context, text.Render));
    }

    [Theory]
    [InlineData("MetricsKern", "中文", 54.4)]
    [InlineData("MetricsLigature", "中中", 51.2)]
    [InlineData("MetricsContext", "中文", 44.8)]
    [InlineData("MetricsMultiple", "中", 44.8)]
    [InlineData("MetricsMark", "中\u3099", 38.4)]
    public void ShapingRemainsIntactAndRecordedFramesRetainTheirMetrics(string name, string source, double advance)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        using var font = Font(name);
        var text = Box(font.Family, source);
        Css.SetStyle(text, Style("vertical-lr", "upright") + "font-feature-settings:'liga' 1,'calt' 1,'vkrn' 1;"); Layout(text);
        Assert.Equal(advance, Assert.Single(text.DocumentTextBounds(0, source.Length)).Height, 3);
        if (name == "MetricsMark")
            Assert.Equal(Assert.Single(text.DocumentTextBounds(0, 1)), Assert.Single(text.DocumentTextBounds(1, 1)));
        var original = Pixels(context, text.Render);
        Assert.True(original.Where((_, index) => index % 4 == 3).Count(alpha => alpha != 0) > 10);
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); text.Render(recorder);
        var frame = recorder.Commit(); Assert.True(frame.IsFullyRecordable);
        text.Text = "国"; Layout(text);
        Assert.Equal(original, Pixels(context, dc => DrawingReplayer.Replay(frame, dc)));
        text.Text = source; Layout(text); Assert.Equal(original, Pixels(context, text.Render));
        text.SelectAll(); Assert.Equal(source, text.SelectedText);
        Save(name + ".png", original);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void VariableNativeVerticalGlyphsUpdateAndRestoreSourceGeometry(RenderBackend backend, string mode, string orientation)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var font = CssNativeFontResource.Create(File.ReadAllBytes(MacOSTestAssets.PathTo("FontSizeAdjust/VerticalVariable.ttf")));
        Assert.NotNull(font);
        var text = Box(font.Family, "水水");
        foreach (var (weight, cell) in new[] { (400, 38.4), (550, 41.6), (700, 44.8), (400, 38.4) })
        {
            Css.SetStyle(text, Style(mode, orientation) + $"font-variation-settings:'wght' {weight};"); Layout(text);
            Assert.Equal(cell * 2, Assert.Single(text.DocumentTextBounds(0, 2)).Height, 3);
            Assert.Equal(cell, Assert.Single(text.DocumentTextBounds(1, 1)).Top, 3);
            Assert.Equal(cell, Assert.Single(text.DocumentTextBounds(1, 1)).Height, 3);
            text.SelectAll(); Assert.Equal("水水", text.SelectedText); text.Select(0, 0);
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void ShortColumnsUseTheWholeLastCellAndPreserveSourceAcrossBreaks(RenderBackend backend, string mode, string orientation)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var font = Font("MetricsMixed");
        var text = Box(font.Family, "国国");
        Css.SetStyle(text, Style(mode, orientation, "pre-wrap")); Layout(text, 85);
        var first = Assert.Single(text.DocumentTextBounds(0, 1));
        var second = Assert.Single(text.DocumentTextBounds(1, 1));
        Assert.Equal(51.2, first.Height, 3); Assert.Equal(51.2, second.Height, 3);
        Assert.Equal(0, first.Top, 3); Assert.Equal(0, second.Top, 3);
        Assert.Equal(mode == "vertical-lr", second.Left > first.Left);
        text.SelectAll(); Assert.Equal("国国", text.SelectedText);
        text.Select(0, 0); text.Text = "文文"; Layout(text, 50);
        var whole = Assert.Single(text.DocumentTextBounds(0, 2)); Assert.Equal(38.4, whole.Height, 3);
        text.Text = "中文\r\n国。"; Layout(text);
        Assert.Equal(57.6, Assert.Single(text.DocumentTextBounds(0, 2)).Height, 3);
        Assert.Equal(76.8, Assert.Single(text.DocumentTextBounds(4, 2)).Height, 3);
        text.SelectAll(); Assert.Equal(text.Text, text.SelectedText);
    }

    [Theory]
    [InlineData(6, 63.6)]
    [InlineData(-4, 53.6)]
    public void CssSpacingComposesWithTheIntrinsicMetricCell(double spacing, double advance)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        using var font = Font("MetricsMixed");
        var text = Box(font.Family, "中文");
        Css.SetStyle(text, Style("vertical-lr", "upright") + $"letter-spacing:{spacing}px;"); Layout(text);
        Assert.Equal(advance, Assert.Single(text.DocumentTextBounds(0, 2)).Height, 3);
        var a = Assert.Single(text.DocumentTextBounds(0, 1)); var b = Assert.Single(text.DocumentTextBounds(1, 1));
        Assert.Equal(a.Bottom, b.Top, 3);
        text.SelectAll(); Assert.Equal("中文", text.SelectedText);
    }

    private static string Style(string mode, string orientation, string whitespace = "pre") =>
        $"writing-mode:{mode};text-orientation:{orientation};white-space:{whitespace};line-height:40px;text-autospace:no-autospace;";
    private static CssNativeFontResource Font(string name) => CssNativeFontResource.Create(
        File.ReadAllBytes(MacOSTestAssets.PathTo("FontVerticalMetrics/" + name + ".ttf"))) ?? throw new InvalidOperationException("Metric fixture registration failed.");
    private static TextBlock Box(string family, string source) => new()
    {
        Text = source, FontFamily = new FontFamily(family), FontSize = 32,
        Foreground = Brushes.Black, IsTextSelectionEnabled = true,
    };
    private static void Layout(TextBlock text, double height = 400)
    {
        for (int i = 0; i < 3; i++) { text.Measure(new Size(180, height)); text.Arrange(new Rect(0, 0, 180, height)); text.UpdateLayout(); }
    }
    private static byte[] Pixels(RenderContext context, Action<DrawingContext> draw)
    {
        using var target = context.CreateOffscreenRenderTarget(460, 460);
        target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
        using (var dc = new RenderTargetDrawingContext(target, context)) { draw(dc); dc.Close(); }
        target.EndDraw(); var pixels = new byte[460 * 460 * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 460 * 4, out _, out _)); return pixels;
    }
    private static void Save(string name, byte[] pixels)
    {
        var folder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR"); if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder); var bitmap = BitmapSource.Create(460, 460, 96, 96, PixelFormats.Bgra32, null, pixels, 460 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, "vertical-metrics-" + name)); encoder.Save(stream);
    }
}
