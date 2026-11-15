using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class D3D12TextSamplingTests
{
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void Vello_SmallTextDoesNotSampleNeighbouringAtlasRows() => Verify(RenderingEngine.Vello);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void Impeller_SmallTextDoesNotSampleNeighbouringAtlasRows() => Verify(RenderingEngine.Impeller);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void Vello_MixedFixedAndIdealTextKeepsItsOwnSamplingMode() => VerifyMixedSampling(RenderingEngine.Vello);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void Impeller_MixedFixedAndIdealTextKeepsItsOwnSamplingMode() => VerifyMixedSampling(RenderingEngine.Impeller);

    private static void VerifyMixedSampling(RenderingEngine engine)
    {
        const int width = 512, height = 256;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(RenderBackend.D3D12, GpuPreference.Auto, engine);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var white = context.CreateSolidBrush(1, 1, 1);
        using var ideal = context.CreateTextFormat("Consolas", 14);
        ideal.SetTextRenderingMode((int)TextRenderingMode.Grayscale);
        ideal.SetTextFormattingMode((int)TextFormattingMode.Ideal);
        ideal.SetSubpixelPositioning(true);
        using var fixedFormat = context.CreateTextFormat("Consolas", 14);
        fixedFormat.SetTextRenderingMode((int)TextRenderingMode.Grayscale);
        fixedFormat.SetTextFormattingMode((int)TextFormattingMode.Display);
        fixedFormat.SetTextHintingMode((int)TextHintingMode.Fixed);
        target.SetDpi(96, 96);

        foreach (var (first, second) in new[] { (fixedFormat, ideal), (ideal, fixedFormat) })
        {
            var expected = Render(null, second);
            var actual = Render(first, second);
            Assert.True(Enumerable.Range(0, width * 120).Count(pixel => expected[pixel * 4 + 3] > 20) > 100,
                "The reference code text must be visible in the pixel comparison.");
            var mode = ReferenceEquals(second, ideal) ? "ideal-after-fixed" : "fixed-after-ideal";
            Save(expected, width, height, engine + "-" + mode + "-reference.png");
            Save(actual, width, height, engine + "-" + mode + "-actual.png");
            var changed = 0;
            for (var offset = 0; offset < width * 120 * 4; offset++)
                if (expected[offset] != actual[offset]) changed++;
            Assert.True(changed == 0,
                $"Drawing another text sampling mode first changed {changed} channels in PropertyName/nameof.");
        }

        byte[] Render(NativeTextFormat? preceding, NativeTextFormat sample)
        {
            for (var pass = 0; pass < 2; pass++)
            {
                target.SetFullInvalidation();
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (!target.TryBeginDraw())
                {
                    Assert.True(wait.ElapsedMilliseconds < 5000);
                    Thread.Sleep(1);
                }
                target.Clear(0, 0, 0, 0);
                if (preceding != null)
                    target.DrawText("Fixed and Ideal mixed", preceding, 16, 180, 470, 60, white);
                target.DrawText("e.PropertyName == nameof(ViewModel.IsMonitorPlaying)",
                    sample, 16.25f, 24.25f, 480, 60, white);
                if (pass == 1) Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            }
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }
    }

    private static void Verify(RenderingEngine engine)
    {
        const int width = 720, height = 256;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(RenderBackend.D3D12, GpuPreference.Auto, engine);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var white = context.CreateSolidBrush(1, 1, 1);
        using var format = context.CreateTextFormat("Consolas", 14);
        format.SetTextRenderingMode((int)TextRenderingMode.Grayscale);
        format.SetTextFormattingMode((int)TextFormattingMode.Ideal);
        format.SetSubpixelPositioning(true);
        target.SetDpi(96, 96);

        var reference = Render(crowded: false);
        var crowded = Render(crowded: true);
        Assert.True(Enumerable.Range(0, width * 140).Count(pixel => reference[pixel * 4 + 3] > 20) > 100,
            "The reference code text must be visible in the atlas comparison.");
        Save(reference, width, height, engine + "-empty-atlas.png");
        Save(crowded, width, height, engine + "-crowded-atlas.png");
        var changed = 0;
        for (var y = 0; y < 140; y++)
        for (var x = 0; x < width; x++)
        for (var channel = 0; channel < 4; channel++)
        {
            var offset = (y * width + x) * 4 + channel;
            if (reference[offset] != crowded[offset]) changed++;
        }
        Assert.True(changed == 0, $"Unrelated atlas allocations changed {changed} channels in existing code text.");

        byte[] Render(bool crowded)
        {
            for (var pass = 0; pass < 6; pass++)
            {
                target.SetFullInvalidation();
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (!target.TryBeginDraw())
                {
                    Assert.True(wait.ElapsedMilliseconds < 5000);
                    Thread.Sleep(1);
                }
                target.Clear(0, 0, 0, 0);
                target.DrawText("e.PropertyName == nameof(WanmuStudioViewModel.IsMonitorPlaying)",
                    format, 16.25f, 24.25f, width - 32, 60, white);
                target.DrawText("private void OnViewModelPropertyChanged(object? sender)",
                    format, 16.75f, 64.75f, width - 32, 60, white);
                if (crowded)
                {
                    for (var item = 0; item < 28; item++)
                        target.DrawText(item + " " + new string('H', 90) + " MWWWqyg0123456789",
                            format, 4, 180, width - 8, 60, white);
                }
                if (pass == 5) Assert.Equal(JaliumResult.Ok, target.RequestReadback());
                Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            }
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }
    }

    private static void Save(byte[] pixels, int width, int height, string filename)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_GLYPH_GUTTER_EVIDENCE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, filename));
        encoder.Save(output);
    }
}
