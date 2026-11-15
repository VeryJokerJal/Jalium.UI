using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class SoftwareRetainedLayerTests
{
    private const int Width = 128;
    private const int Height = 96;

    [Fact]
    public void Capture_IsIsolatedAndCompositeReplaysAtDestination()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Software);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var red = context.CreateSolidBrush(0.9f, 0.08f, 0.04f, 1f);

        Assert.True(target.SupportsRetainedLayers());
        Assert.True(target.TryBeginDraw());
        target.Clear(0.03f, 0.04f, 0.06f, 1f);

        nint layer = target.RealizeLayerBegin(0, 12f, 10f, 28f, 22f);
        Assert.NotEqual(nint.Zero, layer);
        target.FillRectangle(12f, 10f, 28f, 22f, red);
        target.RealizeLayerEnd(layer);
        target.CompositeLayer(layer, 72f, 52f, 28f, 22f, 1f);

        byte[] pixels = FinishAndRead(target);
        AssertBackground(pixels, 20, 18);
        AssertRed(pixels, 84, 62);

        target.DestroyRetainedLayer(layer);
    }

    [Fact]
    public void ExistingLayer_IsReusedAndParentClipAppliesOnlyAtCompositeTime()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Software);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var red = context.CreateSolidBrush(0.9f, 0.08f, 0.04f, 1f);
        using var green = context.CreateSolidBrush(0.04f, 0.85f, 0.12f, 1f);

        Assert.True(target.TryBeginDraw());
        target.Clear(0.03f, 0.04f, 0.06f, 1f);
        nint layer = target.RealizeLayerBegin(0, 8f, 8f, 32f, 24f);
        Assert.NotEqual(nint.Zero, layer);
        target.FillRectangle(8f, 8f, 32f, 24f, red);
        target.RealizeLayerEnd(layer);
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());

        Assert.True(target.TryBeginDraw());
        target.Clear(0.03f, 0.04f, 0.06f, 1f);
        nint reused = target.RealizeLayerBegin(layer, 8f, 8f, 32f, 24f);
        Assert.Equal(layer, reused);
        target.FillRectangle(8f, 8f, 32f, 24f, green);
        target.RealizeLayerEnd(reused);

        target.PushClip(64f, 40f, 16f, 24f);
        target.CompositeLayer(reused, 64f, 40f, 32f, 24f, 1f);
        target.PopClip();

        byte[] pixels = FinishAndRead(target);
        AssertGreen(pixels, 72, 50);
        AssertBackground(pixels, 88, 50);

        target.DestroyRetainedLayer(reused);
    }

    [Fact]
    public void FractionalOpacity_ColdAndCachedCompositeAreByteIdentical()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Software);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var red = context.CreateSolidBrush(0.9f, 0.08f, 0.04f, 1f);

        target.SetFullInvalidation();
        Assert.True(target.TryBeginDraw());
        target.Clear(0.03f, 0.04f, 0.06f, 1f);
        nint layer = target.RealizeLayerBegin(0, 8f, 8f, 32f, 24f);
        Assert.NotEqual(nint.Zero, layer);
        target.FillRectangle(8f, 8f, 32f, 24f, red);
        target.RealizeLayerEnd(layer);
        target.CompositeLayer(layer, 60.25f, 42.5f, 32f, 24f, 0.5f);
        byte[] cold = FinishAndRead(target);

        target.SetFullInvalidation();
        Assert.True(target.TryBeginDraw());
        target.Clear(0.03f, 0.04f, 0.06f, 1f);
        target.CompositeLayer(layer, 60.25f, 42.5f, 32f, 24f, 0.5f);
        byte[] cached = FinishAndRead(target);

        Assert.Equal(cold, cached);
        int center = (54 * Width + 72) * 4;
        Assert.InRange(cached[center + 2], 100, 130);
        Assert.Equal(255, cached[center + 3]);
        target.DestroyRetainedLayer(layer);
    }

    private static byte[] FinishAndRead(RenderTarget target)
    {
        Assert.Equal(JaliumResult.Ok, target.RequestReadback());
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        var pixels = new byte[Width * Height * 4];
        Assert.Equal(
            JaliumResult.Ok,
            target.FetchReadback(pixels, Width * 4u, out int width, out int height));
        Assert.Equal(Width, width);
        Assert.Equal(Height, height);
        return pixels;
    }

    private static void AssertBackground(byte[] pixels, int x, int y)
    {
        int offset = (y * Width + x) * 4;
        Assert.InRange(pixels[offset], 10, 20);
        Assert.InRange(pixels[offset + 1], 7, 16);
        Assert.InRange(pixels[offset + 2], 4, 12);
        Assert.Equal(255, pixels[offset + 3]);
    }

    private static void AssertRed(byte[] pixels, int x, int y)
    {
        int offset = (y * Width + x) * 4;
        Assert.True(
            pixels[offset + 2] >= 210 && pixels[offset + 1] <= 40 && pixels[offset] <= 30,
            $"Expected red at ({x},{y}), BGRA=({pixels[offset]},{pixels[offset + 1]},{pixels[offset + 2]},{pixels[offset + 3]})." );
    }

    private static void AssertGreen(byte[] pixels, int x, int y)
    {
        int offset = (y * Width + x) * 4;
        Assert.True(
            pixels[offset + 1] >= 195 && pixels[offset + 2] <= 40 && pixels[offset] <= 50,
            $"Expected green at ({x},{y}), BGRA=({pixels[offset]},{pixels[offset + 1]},{pixels[offset + 2]},{pixels[offset + 3]})." );
    }
}
