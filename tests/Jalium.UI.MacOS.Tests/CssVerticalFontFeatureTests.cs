using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssVerticalFontFeatureTests
{
    public static IEnumerable<object[]> BackendsAndFonts()
    {
        foreach (var backend in new[] { RenderBackend.Software, RenderBackend.Metal })
        foreach (var name in new[] { "VerticalBoth", "VerticalOnly", "RotationOnly", "VerticalVariable" })
            yield return [backend, name];
    }

    [Theory]
    [MemberData(nameof(BackendsAndFonts))]
    public void ActualControlResolvesVerticalFeaturesAndRetainsDrawings(RenderBackend backend, string name)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var resource = Font(name);
        var text = Box(resource.Family, "A—(");
        var baseline = "writing-mode:vertical-lr;text-orientation:upright;white-space:pre;line-height:40px;text-autospace:no-autospace;" +
            (name == "VerticalVariable" ? "font-variation-settings:'wght' 400;" : "");
        Set("");
        Assert.Equal(115.2, Assert.Single(text.DocumentTextBounds(0, 3)).Height, 3);
        for (int i = 0; i < 3; ++i)
        {
            var cell = Assert.Single(text.DocumentTextBounds(i, 1));
            Assert.Equal(i * 38.4, cell.Top, 3); Assert.Equal(38.4, cell.Height, 3);
            Assert.Contains(text.DocumentCharacterFromPoint(new Point(cell.Left + cell.Width / 2, cell.Top + 2)), new[] { i, i + 1 });
        }
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); text.Render(recorder);
        var frame = recorder.Commit(); Assert.True(frame.IsFullyRecordable);
        if (backend == RenderBackend.Software)
        {
            var original = Pixels(context, text.Render);
            Set("font-feature-settings:'vert' 0;");
            Assert.Equal(name == "RotationOnly", original.SequenceEqual(Pixels(context, text.Render)));
            Set("font-feature-settings:'vert' 1,'vrt2' 0;");
            Assert.Equal(original, Pixels(context, text.Render));
            Set("font-feature-settings:'vert' 0,'vrt2' 1;");
            Assert.False(original.SequenceEqual(Pixels(context, text.Render)));
            Set("font-feature-settings:'vert' 1,'vrt2' 1;");
            Assert.Equal(name != "RotationOnly", original.SequenceEqual(Pixels(context, text.Render)));
            Assert.Equal(original, Pixels(context, dc => DrawingReplayer.Replay(frame, dc)));
            Set(""); Assert.Equal(original, Pixels(context, text.Render));
            Save(name + "-upright.png", original);
        }
        text.SelectAll(); Assert.Equal("A—(", text.SelectedText);
        void Set(string features) { Css.SetStyle(text, baseline + features); Layout(text); }
    }

    [Theory]
    [InlineData(550, 41.6)]
    [InlineData(700, 38.4)]
    public void SourceGeometryFollowsVvarAndConditionalGsub(int weight, double cell)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        using var resource = Font("VerticalVariable");
        var text = Box(resource.Family, "A—(");
        Css.SetStyle(text, $"writing-mode:vertical-rl;text-orientation:upright;white-space:pre;line-height:40px;text-autospace:no-autospace;font-variation-settings:'wght' {weight};");
        Layout(text);
        Assert.Equal(cell * 3, Assert.Single(text.DocumentTextBounds(0, 3)).Height, 3);
        Assert.Equal(cell, Assert.Single(text.DocumentTextBounds(1, 1)).Top, 3);
        text.SelectAll(); Assert.Equal("A—(", text.SelectedText);
        Save($"variable-{weight}.png", Pixels(context, text.Render));
    }

    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public void MixedCjkUsesVerticalAlternatesAndCanResetThem(string mode)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        using var resource = Font("VerticalBoth");
        var text = Box(resource.Family, "中（");
        string baseline = $"writing-mode:{mode};text-orientation:mixed;white-space:pre;line-height:40px;text-autospace:no-autospace;";
        Css.SetStyle(text, baseline); Layout(text); var original = Pixels(context, text.Render);
        Css.SetStyle(text, baseline + "font-feature-settings:'vert' 0;"); Layout(text);
        Assert.False(original.SequenceEqual(Pixels(context, text.Render)));
        Css.SetStyle(text, baseline); Layout(text); Assert.Equal(original, Pixels(context, text.Render));
        text.SelectAll(); Assert.Equal("中（", text.SelectedText);
        Save(mode + "-mixed.png", original);
    }

    private static CssNativeFontResource Font(string name) => CssNativeFontResource.Create(
        File.ReadAllBytes(MacOSTestAssets.PathTo("FontVerticalForms/" + name + ".ttf"))) ?? throw new InvalidOperationException("Font fixture registration failed.");
    private static TextBlock Box(string family, string source) => new()
    {
        Text = source, FontFamily = new FontFamily(family), FontSize = 32,
        Foreground = Brushes.Black, IsTextSelectionEnabled = true,
    };
    private static void Layout(TextBlock text)
    {
        for (int i = 0; i < 3; ++i) { text.Measure(new Size(180, 400)); text.Arrange(new Rect(0, 0, 180, 400)); text.UpdateLayout(); }
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
        var folder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder); var bitmap = BitmapSource.Create(460, 460, 96, 96, PixelFormats.Bgra32, null, pixels, 460 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, "font-features-" + name)); encoder.Save(stream);
    }
}
