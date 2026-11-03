using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class GradientTextClarityTests
{
    private const int Width = 1200;
    private const int Height = 200;
    private const string Caption = "支持 NativeAOT 发布，启动不必等运行时预热";

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void FlatGradient_KeepsTheSameGlyphCoverageAsASolidBrush() =>
        AssertCoverageParity(sweep: false, transformed: false);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void RevealedSweepCaption_KeepsTheSameGlyphCoverageAsASolidBrush() =>
        AssertCoverageParity(sweep: true, transformed: false);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void GradientText_UnderScaleAndRotation_PreservesGlyphCoverage() =>
        AssertCoverageParity(sweep: true, transformed: true);

    private static void AssertCoverageParity(bool sweep, bool transformed)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.D3D12, GpuPreference.Auto, RenderingEngine.Vello);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var solid = context.CreateSolidBrush(.4f, .4f, .4f, .65f);
        // The real caption's sweep rests beyond the right edge. Its visible
        // range is the first grey stop even though later stops have colours.
        using var gradient = context.CreateLinearGradientBrush(
            sweep ? 10000 : 0, 0, sweep ? 11000 : 1000, 0,
            sweep
                ? [0, .4f, .4f, .4f, .65f, .5f, .4f, .4f, .4f, .65f,
                   .72f, .2f, .8f, .35f, 1, .84f, 1, 1, 1, 1,
                   .93f, 1, 1, 1, 0, 1, 1, 1, 1, 0]
                : [0, .4f, .4f, .4f, .65f, 1, .4f, .4f, .4f, .65f],
            sweep ? 6u : 2u);

        foreach (var dpi in new[] { 96f, 120f, 144f, 192f })
        foreach (var fontSize in new[] { 9f, 11.5f, 14f, 20f })
        {
            target.SetDpi(dpi, dpi);
            using var format = context.CreateTextFormat("Segoe UI Variable Text", fontSize);
            format.SetTextFormattingMode((int)TextFormattingMode.Ideal);
            format.SetTextRenderingMode((int)TextRenderingMode.Grayscale);
            format.SetSubpixelPositioning(true);
            foreach (var phase in new[] { 0f, .25f, .5f, .75f })
            {
                var matrix = transformed
                    ? new[] { 1.03f * MathF.Cos(.08f), 1.03f * MathF.Sin(.08f),
                        -1.03f * MathF.Sin(.08f), 1.03f * MathF.Cos(.08f), 0, 0 }
                    : null;
                var reference = Render(target, format, solid, phase, matrix);
                var actual = Render(target, format, gradient, phase, matrix);
                var maxDifference = 0;
                long totalDifference = 0;
                var ink = 0;
                for (var pixel = 0; pixel < reference.Length; pixel += 4)
                {
                    if (reference[pixel] > 20) ink++;
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var difference = Math.Abs(reference[pixel + channel] - actual[pixel + channel]);
                        maxDifference = Math.Max(maxDifference, difference);
                        totalDifference += difference;
                    }
                }
                Assert.True(ink > 100, "The caption must be visible in the comparison.");
                Assert.True(maxDifference <= 2 && totalDifference < ink,
                    $"DPI {dpi}, size {fontSize}, phase {phase}: changing only the brush " +
                    $"changed glyph coverage (max={maxDifference}, total={totalDifference}, ink={ink}).");
            }
        }
    }

    private static byte[] Render(RenderTarget target, NativeTextFormat format, NativeBrush brush,
        float phase, float[]? transform)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            target.SetFullInvalidation();
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (!target.TryBeginDraw())
            {
                Assert.True(wait.ElapsedMilliseconds < 3000, "Render target stayed busy.");
                Thread.Sleep(1);
            }
            target.Clear(.035f, .045f, .04f);
            if (transform != null) target.PushTransform(transform);
            target.DrawText(Caption, format, 20.25f, 24 + phase, Width - 100, 100, brush);
            if (transform != null) target.PopTransform();
            if (pass == 1) Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        }
        var pixels = new byte[Width * Height * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, Width * 4u, out _, out _));
        return pixels;
    }
}
