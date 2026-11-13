using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssGvarVerticalMetricsTests
{
    public static IEnumerable<object[]> Modes()
    {
        foreach (var name in new[] { "PhantomEmbedded", "PhantomShared", "PhantomAllPoints", "PhantomShortOffsets",
            "PhantomTopOnly", "PhantomBottomOnly", "PhantomUnreferenced", "PhantomIntermediate", "PhantomComposite",
            "PhantomUseMyMetrics", "PhantomLastMetrics", "PhantomAvar", "PhantomAvar2", "PhantomMultiAxis",
            "PhantomWithVvar", "ZeroAdvance", "MissingVertical" })
        foreach (var backend in new[] { RenderBackend.Software, RenderBackend.Metal })
        foreach (var mode in new[] { "vertical-rl", "vertical-lr" })
        foreach (var orientation in new[] { "mixed", "upright" })
            yield return [name, backend, mode, orientation];
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void ControlUsesPhantomsForNativeAndIsolatedGlyphsAndRestoresAxisState(
        string name, RenderBackend backend, string mode, string orientation)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var font = Font(name);
        var text = Box(font.Family, "A水");
        foreach (int weight in new[] { 400, 550, 700, 100, 400 })
        {
            Css.SetStyle(text, Style(mode, orientation) + $"font-variation-settings:'wght' {weight}" +
                (name == "PhantomMultiAxis" ? ",'wdth' 125;" : ";"));
            Layout(text);
            var (a, water) = Cells(name, weight);
            if (orientation == "mixed") a = 19.2;
            Cell(0, 0, a); Cell(1, a, water);
            Assert.Equal(a + water, text.DocumentTextBounds(0, 2).Sum(rect => rect.Height), 3);
            text.Select(1, 1); Assert.Equal("水", text.SelectedText);
            text.SelectAll(); Assert.Equal("A水", text.SelectedText);
            text.Select(0, 0);
            if (name == "PhantomMultiAxis") Assert.Equal(orientation == "upright" ? 2 * water : 19.2 + water,
                text.DocumentTextBounds(0, 2).Sum(rect => rect.Height), 3);
        }
        void Cell(int position, double top, double advance)
        {
            var rectangles = text.DocumentTextBounds(position, 1);
            if (advance == 0) { Assert.All(rectangles, rect => Assert.Equal(0, rect.Height)); return; }
            var cell = Assert.Single(rectangles);
            Assert.Equal(top, cell.Top, 3); Assert.Equal(advance, cell.Height, 3);
            Assert.Contains(text.DocumentCharacterFromPoint(new Point(cell.Left + cell.Width / 2, cell.Top + cell.Height / 4)),
                new[] { position, position + 1 });
        }
    }

    [Theory]
    [InlineData("upright")]
    [InlineData("mixed")]
    public void ZeroAdvanceKeepsInkAndOriginalSourceAcrossSpacingAndRecordedReplay(string orientation)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        using var font = Font("ZeroAdvance");
        var text = Box(font.Family, "A水");
        double natural = orientation == "upright" ? 0 : 19.2;
        Set(0);
        var original = Pixels(context, text.Render);
        Assert.True(original.Where((_, i) => i % 4 == 3).Count(alpha => alpha != 0) > 10,
            "Zero advance retains overhanging glyph ink.");
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); text.Render(recorder);
        var frame = recorder.Commit(); Assert.True(frame.IsFullyRecordable);
        Set(6); Assert.False(original.SequenceEqual(Pixels(context, text.Render)));
        Set(-4); Assert.Equal(orientation == "upright" ? 0 : natural,
            text.DocumentTextBounds(0, 2).Sum(rect => rect.Height), 3);
        Set(0); Assert.Equal(original, Pixels(context, text.Render));
        font.Dispose(); text.Text = "replacement"; Layout(text);
        Assert.Equal(original, Pixels(context, dc => DrawingReplayer.Replay(frame, dc)));
        Save("zero-" + orientation + ".png", original);
        void Set(double spacing)
        {
            Css.SetStyle(text, Style("vertical-lr", orientation) + $"letter-spacing:{spacing}px;"); Layout(text);
            var expected = natural + (spacing > 0 ? spacing : 0);
            Assert.Equal(expected, text.DocumentTextBounds(0, 2).Sum(rect => rect.Height), 3);
            text.SelectAll(); Assert.Equal("A水", text.SelectedText); text.Select(0, 0);
        }
    }

    [Theory]
    [InlineData(550, 2)]
    [InlineData(700, 3)]
    public void WrappingUsesInstanceAdvanceAndKeepsRecordedNativeGlyphs(int weight, int columns)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        using var font = Font("PhantomEmbedded");
        var text = Box(font.Family, "水水水水水水");
        Css.SetStyle(text, Style("vertical-lr", "upright", "pre-wrap") + $"font-variation-settings:'wght' {weight};");
        Layout(text, 126);
        var cells = Enumerable.Range(0, 6).Select(i => Assert.Single(text.DocumentTextBounds(i, 1))).ToArray();
        Assert.Equal(columns, cells.Select(cell => cell.Left).Distinct().Count());
        Assert.All(cells, cell => Assert.Equal(weight == 550 ? 41.6 : 44.8, cell.Height, 3));
        text.SelectAll(); Assert.Equal(text.Text, text.SelectedText); text.Select(0, 0);
        var original = Pixels(context, text.Render);
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); text.Render(recorder);
        var frame = recorder.Commit(); Assert.True(frame.IsFullyRecordable);
        Css.SetStyle(text, Style("vertical-rl", "mixed") + "font-variation-settings:'wght' 100;"); Layout(text);
        Assert.Equal(original, Pixels(context, dc => DrawingReplayer.Replay(frame, dc)));
        Save($"wrapped-{weight}.png", original);
    }

    public static IEnumerable<object[]> AdjustmentModes()
    {
        foreach (var name in new[] { "PhantomEmbedded", "PhantomAvar2", "PhantomComposite", "PhantomUseMyMetrics", "PhantomWithVvar" })
        // Metal pixel readback uses real native surfaces in the native suite.
        foreach (var backend in new[] { RenderBackend.Software })
        foreach (bool cascade in new[] { false, true }) yield return [name, backend, cascade];
    }

    [Theory]
    [MemberData(nameof(AdjustmentModes))]
    public void FontSizeAdjustUsesTheInstancedGvarAdvanceWithoutChangingTheFont(string name, RenderBackend backend, bool cascade)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var font = Font(name);
        using var missing = CssNativeFontResource.Create(File.ReadAllBytes(MacOSTestAssets.PathTo("FontSizeAdjust/Missing.ttf")))!;
        foreach (int weight in new[] { 550, 700 })
        {
            var actual = Box(font.Family, "水水");
            string basis = Style("vertical-lr", "upright") + $"font-variation-settings:'wght' {weight};" +
                (cascade ? $"font-family:'{missing.Family}','{font.Family}';" : "");
            Css.SetStyle(actual, basis + "font-size-adjust:ic-height 1.2;"); Layout(actual);
            Assert.Equal(32, actual.FontSize);
            Assert.Equal(76.8, actual.DocumentTextBounds(0, 2).Sum(rect => rect.Height), 3);
            var reference = Box(font.Family, "水水");
            reference.FontSize = 32 * 1.2 / (Cells(name, weight).Water / 32);
            Css.SetStyle(reference, basis + "font-size-adjust:none;"); Layout(reference);
            var expectedPixels = Pixels(context, reference.Render); var actualPixels = Pixels(context, actual.Render);
            Save($"adjust-{name}-{backend}-{cascade}-{weight}-reference.png", expectedPixels);
            Save($"adjust-{name}-{backend}-{cascade}-{weight}-actual.png", actualPixels);
            // FontSize enters the public native ABI as float; size adjustment
            // retains double precision. Compare their glyph silhouettes here;
            // the native suite compares exact pixels at the exact used size.
            Assert.Equal(Silhouette(expectedPixels), Silhouette(actualPixels));
            actual.SelectAll(); Assert.Equal("水水", actual.SelectedText);
        }
        static bool[] Silhouette(byte[] pixels) => pixels.Where((_, index) => index % 4 == 3).Select(alpha => alpha >= 128).ToArray();
    }

    private static (double A, double Water) Cells(string name, int weight)
    {
        if (name == "ZeroAdvance") return (0, 0);
        if (name == "MissingVertical") return (32, 32);
        int units = weight == 100 ? 1000 : weight == 550 ? 1300 : weight == 700 ? 1400 : 1200;
        double a = units, water = units;
        if (name == "PhantomUnreferenced") a = water = 1200;
        if (name == "PhantomLastMetrics") water = 1200;
        if (name == "PhantomComposite") water = weight == 100 ? 900 : weight == 550 ? 1350 : weight == 700 ? 1500 : 1200;
        if (name == "PhantomIntermediate") a = water = weight == 550 ? 1400 : 1200;
        if (name == "PhantomAvar" && weight == 550) a = water = 1250;
        if (name == "PhantomAvar2" && weight == 550) a = water = 1275;
        if (name == "PhantomMultiAxis") a = water = weight == 550 ? 1250 : weight == 700 ? 1300 : 1200;
        if (name == "PhantomWithVvar") { a = 1200; water = weight == 100 ? 1200 : units; }
        return (a * .032, water * .032);
    }
    private static string Style(string mode, string orientation, string whitespace = "pre") =>
        $"writing-mode:{mode};text-orientation:{orientation};white-space:{whitespace};line-height:40px;text-autospace:no-autospace;";
    private static CssNativeFontResource Font(string name) => CssNativeFontResource.Create(
        File.ReadAllBytes(MacOSTestAssets.PathTo("FontGvarMetrics/" + name + ".ttf"))) ?? throw new InvalidOperationException(name);
    private static TextBlock Box(string family, string source) => new()
    {
        Text = source, FontFamily = new FontFamily(family), FontSize = 32,
        Foreground = Brushes.Black, IsTextSelectionEnabled = true, FlowDirection = FlowDirection.LeftToRight,
    };
    private static void Layout(TextBlock text, double height = 400)
    {
        for (int i = 0; i < 3; i++) { text.Measure(new Size(180, height)); text.Arrange(new Rect(0, 0, 180, height)); text.UpdateLayout(); }
    }
    private static byte[] Pixels(RenderContext context, Action<DrawingContext> draw)
    {
        using var target = context.CreateOffscreenRenderTarget(460, 560);
        target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
        using (var dc = new RenderTargetDrawingContext(target, context))
        {
            dc.PushTransform(new MatrixTransform(new Matrix(1, 0, 0, 1, 40, 80)));
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
        using var stream = File.Create(Path.Combine(folder, "gvar-" + name)); encoder.Save(stream);
    }
}
