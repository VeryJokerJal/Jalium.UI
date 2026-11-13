using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssVerticalOriginTests
{
    private static readonly string[] Names = ["OriginZero", "OriginPositive", "OriginNegative", "OriginShort",
        "OriginShifted", "OriginGvar", "OriginVvarTsb", "OriginVvarPoison", "OriginBaseWide", "OriginBaseNarrow", "OriginBaseVariable",
        "OriginMissing", "OriginCffVorg", "OriginCffNoVorg"];

    public static IEnumerable<object[]> Modes()
    {
        foreach (string name in Names)
        foreach (var backend in new[] { RenderBackend.Software, RenderBackend.Metal })
        foreach (string mode in new[] { "vertical-rl", "vertical-lr" })
        foreach (string orientation in new[] { "mixed", "upright" })
            yield return [name, backend, mode, orientation];
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void OriginsKeepOriginalCellsHitsAndSelectionWhileAxesAndDirectionsChange(
        string name, RenderBackend backend, string mode, string orientation)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var font = Font(name);
        var text = Box(font.Family, "A水");
        foreach (int weight in Variable(name) ? new[] { 400, 550, 700, 100, 400 } : new[] { 400 })
        {
            Css.SetStyle(text, Style(mode, orientation, name, weight)); Layout(text);
            double water = Advance(name, weight), a = orientation == "mixed" ? 19.2 : water;
            Cell(0, 0, a); Cell(1, a, water);
            Assert.Equal(a + water, text.DocumentTextBounds(0, 2).Sum(r => r.Height), 3);
            text.Select(1, 1); Assert.Equal("水", text.SelectedText);
            text.SelectAll(); Assert.Equal("A水", text.SelectedText); text.Select(0, 0);
        }
        void Cell(int position, double top, double advance)
        {
            var cell = Assert.Single(text.DocumentTextBounds(position, 1));
            Assert.Equal(top, cell.Top, 3); Assert.Equal(advance, cell.Height, 3);
            Assert.Contains(text.DocumentCharacterFromPoint(new Point(cell.Left + cell.Width / 2, cell.Top + cell.Height / 4)),
                new[] { position, position + 1 });
        }
    }

    [Theory]
    [InlineData("A", "vertical-lr", "OriginPositive", 8)]
    [InlineData("A", "vertical-rl", "OriginNegative", -8)]
    [InlineData("水", "vertical-lr", "OriginPositive", 8)]
    [InlineData("水", "vertical-rl", "OriginNegative", -8)]
    public void LiteralTopBearingMovesOnlyInkAndRecordedReplayRetainsIt(string source, string mode, string name, int dy)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        using var zero = Font("OriginZero"); using var shifted = Font(name);
        var reference = Box(zero.Family, source); var actual = Box(shifted.Family, source);
        Css.SetStyle(reference, Style(mode, "upright", "OriginZero", 400));
        Css.SetStyle(actual, Style(mode, "upright", name, 400)); Layout(reference); Layout(actual);
        var expected = Pixels(context, reference.Render, 0, dy); var pixels = Pixels(context, actual.Render);
        Save(name + "-" + source + "-" + mode + "-actual.png", pixels);
        Save(name + "-" + source + "-" + mode + "-reference.png", expected);
        Assert.Equal(expected, pixels);
        Assert.Equal(reference.DocumentTextBounds(0, 1), actual.DocumentTextBounds(0, 1));
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); actual.Render(recorder);
        var frame = recorder.Commit(); Assert.True(frame.IsFullyRecordable);
        actual.Text = "replacement"; Layout(actual); shifted.Dispose();
        Assert.Equal(pixels, Pixels(context, dc => DrawingReplayer.Replay(frame, dc)));
        Save(name + "-" + source + "-" + mode + ".png", pixels);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("水")]
    public void AuthoredAncestorClipStillConstrainsNegativeBearingInk(string source)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        using var font = Font("OriginNegative"); var text = Box(font.Family, source);
        Css.SetStyle(text, Style("vertical-lr", "upright", "OriginNegative", 400)); Layout(text);
        var all = Pixels(context, text.Render);
        var clipped = Pixels(context, dc =>
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, 180, 400)));
            text.Render(dc); dc.Pop();
        });
        Assert.Contains(all.Take(80 * 460 * 4).Where((_, i) => i % 4 == 3), a => a > 0);
        Assert.All(clipped.Take(80 * 460 * 4), b => Assert.Equal(0, b));
        Assert.Contains(Silhouette(clipped), a => a);
        text.SelectAll(); Assert.Equal(source, text.SelectedText);
    }

    [Theory]
    [InlineData("vertical-lr", "OriginPositive")]
    [InlineData("vertical-lr", "OriginNegative")]
    [InlineData("vertical-rl", "OriginPositive")]
    [InlineData("vertical-rl", "OriginNegative")]
    public void OverfullUnwrappedColumnsKeepTheirPhysicalViewport(string mode, string name)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        using var font = Font(name); var text = Box(font.Family, "水水水水水水");
        Css.SetStyle(text, Style(mode, "upright", name, 400)); Layout(text, 75);
        var pixels = Pixels(context, text.Render);
        Assert.Contains(Silhouette(pixels), a => a);
        for (int y = 0; y < 560; y++)
        for (int x = 0; x < 460; x++)
            if (x < 40 || x >= 220 || y < 80 || y >= 155) Assert.Equal(0, pixels[(y * 460 + x) * 4 + 3]);
        text.SelectAll(); Assert.Equal("水水水水水水", text.SelectedText);
    }

    [Theory]
    [InlineData("OriginBaseWide", 400, -3.2)]
    [InlineData("OriginBaseNarrow", 400, 3.2)]
    [InlineData("OriginBaseVariable", 550, -1.6)]
    [InlineData("OriginBaseVariable", 700, -3.2)]
    public void UprightLatinUsesTheLiteralVerticalBaseAxis(string name, int weight, double dx)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        using var zero = Font("OriginZero"); using var font = Font(name);
        var reference = Box(zero.Family, "AA"); var actual = Box(font.Family, "AA");
        Css.SetStyle(reference, Style("vertical-lr", "upright", "OriginZero", 400));
        Css.SetStyle(actual, Style("vertical-lr", "upright", name, weight)); Layout(reference); Layout(actual);
        var expected = Pixels(context, reference.Render, dx, 0); var pixels = Pixels(context, actual.Render);
        // Both public placement transforms enter the native API as float.
        // Compare at the same caller origin; the native suite additionally
        // verifies every exact byte against double-precision literal BASE.
        Assert.Equal(Silhouette(expected), Silhouette(pixels));
        Assert.Equal(reference.DocumentTextBounds(0, 2), actual.DocumentTextBounds(0, 2));
        Save(name + "-" + weight + "-base-actual.png", pixels);
        Save(name + "-" + weight + "-base-reference.png", expected);
    }

    public static IEnumerable<object[]> VariationModes()
    {
        foreach (string name in new[] { "OriginGvar", "OriginVvarTsb", "OriginVvarPoison", "OriginBaseVariable" })
        foreach (bool fallback in new[] { false, true }) yield return [name, fallback];
    }
    [Theory]
    [MemberData(nameof(VariationModes))]
    public void AxisRestorationSizeAdjustmentAndPrivateFallbackKeepOriginAndSource(string name, bool fallback)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        using var font = Font(name);
        using var missing = CssNativeFontResource.Create(File.ReadAllBytes(MacOSTestAssets.PathTo("FontSizeAdjust/Missing.ttf")))!;
        var text = Box(font.Family, "水水"); byte[]? original = null;
        foreach (int weight in new[] { 400, 550, 700, 100, 400 })
        {
            string style = Style("vertical-lr", "upright", name, weight) + "font-size-adjust:ic-height 1.2;";
            if (fallback) style += $"font-family:'{missing.Family}','{font.Family}';";
            Css.SetStyle(text, style); Layout(text);
            Assert.Equal(32, text.FontSize);
            Assert.Equal(76.8, text.DocumentTextBounds(0, 2).Sum(r => r.Height), 3);
            text.SelectAll(); Assert.Equal("水水", text.SelectedText); text.Select(0, 0);
            var pixels = Pixels(context, text.Render); Assert.Contains(Silhouette(pixels), x => x);
            if (original is null) original = pixels; else if (weight == 400) Assert.Equal(original, pixels);
            Save(name + "-" + weight + "-fallback-" + fallback + ".png", pixels);
        }
    }

    [Theory]
    [InlineData("OriginGvar", 550, 2)]
    [InlineData("OriginGvar", 700, 3)]
    [InlineData("OriginVvarTsb", 700, 3)]
    [InlineData("OriginVvarPoison", 700, 3)]
    [InlineData("OriginBaseVariable", 700, 2)]
    public void WrappedColumnsAndRecordedInkSurviveAnOriginChange(string name, int weight, int columns)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        using var font = Font(name); var text = Box(font.Family, "水水水水水水");
        Css.SetStyle(text, Style("vertical-lr", "upright", name, weight).Replace("white-space:pre;", "white-space:pre-wrap;"));
        Layout(text, 126);
        var cells = Enumerable.Range(0, 6).Select(i => Assert.Single(text.DocumentTextBounds(i, 1))).ToArray();
        Assert.Equal(columns, cells.Select(r => r.Left).Distinct().Count());
        Assert.All(cells, r => Assert.Equal(Advance(name, weight), r.Height, 3));
        var pixels = Pixels(context, text.Render); var recorder = new DrawingRecorder();
        recorder.BindWholeFrame(); text.Render(recorder); var frame = recorder.Commit(); Assert.True(frame.IsFullyRecordable);
        Css.SetStyle(text, Style("vertical-rl", "mixed", name, 100)); Layout(text);
        Assert.Equal(pixels, Pixels(context, dc => DrawingReplayer.Replay(frame, dc)));
        text.SelectAll(); Assert.Equal("水水水水水水", text.SelectedText);
        Save(name + "-wrapped-" + weight + ".png", pixels);
    }

    private static bool Variable(string name) => name is "OriginGvar" or "OriginVvarTsb" or "OriginVvarPoison" or "OriginBaseVariable";
    private static double Advance(string name, int weight) => .032 * (name == "OriginMissing" ? 1000 :
        name == "OriginGvar" ? 1200 + 200 * (weight - 400) / 300.0 :
        (name is "OriginVvarTsb" or "OriginVvarPoison") ? 1200 + 200 * Math.Max(0, (weight - 400) / 300.0) : 1200);
    private static string Style(string mode, string orientation, string name, int weight) =>
        $"writing-mode:{mode};text-orientation:{orientation};white-space:pre;line-height:40px;text-autospace:no-autospace;" +
        (Variable(name) ? $"font-variation-settings:'wght' {weight};" : "");
    private static CssNativeFontResource Font(string name) => CssNativeFontResource.Create(File.ReadAllBytes(
        MacOSTestAssets.PathTo("FontVerticalOrigins/" + name + (name.StartsWith("OriginCff") ? ".otf" : ".ttf")))) ?? throw new InvalidOperationException(name);
    private static TextBlock Box(string family, string source) => new()
    {
        Text = source, FontFamily = new FontFamily(family), FontSize = 32, Foreground = Brushes.Black,
        IsTextSelectionEnabled = true, FlowDirection = FlowDirection.LeftToRight,
    };
    private static void Layout(TextBlock text, double height = 400)
    {
        for (int i = 0; i < 3; i++) { text.Measure(new Size(180, height)); text.Arrange(new Rect(0, 0, 180, height)); text.UpdateLayout(); }
    }
    private static bool[] Silhouette(byte[] pixels) => pixels.Where((_, i) => i % 4 == 3).Select(a => a >= 128).ToArray();
    private static byte[] Pixels(RenderContext context, Action<DrawingContext> draw, double dx = 0, double dy = 0)
    {
        using var target = context.CreateOffscreenRenderTarget(460, 560);
        target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
        using (var dc = new RenderTargetDrawingContext(target, context))
        {
            dc.PushTransform(new MatrixTransform(new Matrix(1, 0, 0, 1, 40 + dx, 80 + dy)));
            draw(dc); dc.Pop(); dc.Close();
        }
        target.EndDraw(); var pixels = new byte[460 * 560 * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 460 * 4, out _, out _)); return pixels;
    }
    private static void Save(string name, byte[] pixels)
    {
        var folder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR"); if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder); var bitmap = BitmapSource.Create(460, 560, 96, 96, PixelFormats.Bgra32, null, pixels, 460 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, "origins-" + name)); encoder.Save(stream);
    }
}
