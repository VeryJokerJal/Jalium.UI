using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class SoftwareRasterFastPathTests
{
    private const int Width = 640;
    private const int Height = 480;

    [Fact]
    public void ScalarAndParallelRows_AreByteIdentical()
    {
        string? previous = Environment.GetEnvironmentVariable("JALIUM_SOFTWARE_THREADS");
        string? previousSimd = Environment.GetEnvironmentVariable("JALIUM_SOFTWARE_SIMD");
        try
        {
            byte[] scalar = Render(workerCount: 0, simd: "scalar");
            byte[] sse2 = Render(workerCount: 0, simd: "sse2");
            byte[] parallel = Render(workerCount: 8, simd: null);
            Assert.Equal(scalar, sse2);
            Assert.Equal(scalar, parallel);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JALIUM_SOFTWARE_THREADS", previous);
            Environment.SetEnvironmentVariable("JALIUM_SOFTWARE_SIMD", previousSimd);
        }
    }

    [Fact]
    public void RoundedClip_RestrictsLargeSpanWithoutLeakingCornerPixels()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Software);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var red = context.CreateSolidBrush(0.95f, 0.04f, 0.03f, 1f);

        Assert.True(target.TryBeginDraw());
        target.Clear(0.02f, 0.03f, 0.05f, 1f);
        target.PushPerCornerRoundedRectClip(
            100f, 80f, 300f, 220f,
            36f, 20f, 44f, 28f);
        target.FillRectangle(0f, 0f, Width, Height, red);
        target.PopClip();

        byte[] pixels = FinishAndRead(target);
        AssertBackground(pixels, 102, 82);
        AssertRed(pixels, 150, 100);
        AssertRed(pixels, 250, 180);
        AssertBackground(pixels, 80, 180);
    }

    [Fact]
    public void PathRasterCache_ReusesExactCoverageAndKeysTheTransform()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Software);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var red = context.CreateSolidBrush(0.95f, 0.04f, 0.03f, 1f);
        float[] commands =
        [
            0f, 120f, 60f,
            0f, 120f, 130f,
            0f, 50f, 130f,
            5f,
        ];

        byte[] Draw(float translateX, float translateY)
        {
            Assert.True(target.TryBeginDraw());
            target.Clear(0.02f, 0.03f, 0.05f, 1f);
            if (translateX != 0f || translateY != 0f)
            {
                target.PushTransform(
                    [1f, 0f, 0f, 1f, translateX, translateY]);
            }

            target.FillPath(
                50f, 60f, commands, commands.Length,
                red, fillRule: 1, edgeMode: 2);

            if (translateX != 0f || translateY != 0f)
            {
                target.PopTransform();
            }
            return FinishAndRead(target);
        }

        byte[] cold = Draw(0f, 0f);
        Assert.True(target.TryQueryGpuStats(out var coldStats));
        Assert.True(coldStats.PathEntries >= 1);

        byte[] hot = Draw(0f, 0f);
        Assert.Equal(cold, hot);
        Assert.True(target.TryQueryGpuStats(out var hotStats));
        Assert.Equal(coldStats.PathEntries, hotStats.PathEntries);

        byte[] translated = Draw(200f, 100f);
        Assert.True(target.TryQueryGpuStats(out var translatedStats));
        Assert.True(translatedStats.PathEntries > hotStats.PathEntries);
        AssertBackground(translated, 10, 10);
        AssertRed(translated, 280, 190);
        AssertBackground(translated, 80, 90);
    }

    [Fact]
    public void DirtyRect_ClearAndReplay_PreservePixelsOutsideDamage()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Software);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var red = context.CreateSolidBrush(0.95f, 0.04f, 0.03f, 1f);
        using var green = context.CreateSolidBrush(0.03f, 0.9f, 0.08f, 1f);

        target.SetFullInvalidation();
        Assert.True(target.TryBeginDraw());
        target.Clear(0.02f, 0.03f, 0.05f, 1f);
        target.FillRectangle(0f, 0f, Width, Height, red);
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());

        target.AddDirtyRect(100f, 80f, 48f, 36f);
        Assert.True(target.TryBeginDraw());
        target.Clear(0.02f, 0.03f, 0.05f, 1f);
        target.FillRectangle(0f, 0f, Width, Height, green);
        byte[] pixels = FinishAndRead(target);

        AssertRed(pixels, 80, 90);
        int inside = (90 * Width + 120) * 4;
        Assert.True(
            pixels[inside + 1] >= 210 && pixels[inside + 2] <= 30,
            "The dirty band must contain the new green replay.");
        AssertRed(pixels, 180, 90);
    }

    [Fact]
    public void UnequalRoundedRadii_ColdAndCachedFramesAreByteIdentical()
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Software);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var fill = context.CreateSolidBrush(0.18f, 0.62f, 0.94f, 0.8f);

        byte[] Draw()
        {
            target.SetFullInvalidation();
            Assert.True(target.TryBeginDraw());
            target.Clear(0.02f, 0.03f, 0.05f, 1f);
            target.FillRoundedRectangle(
                37.25f, 44.5f, 310.75f, 92.25f,
                radiusX: 28f, radiusY: 16f, fill);
            return FinishAndRead(target);
        }

        byte[] cold = Draw();
        byte[] cached = Draw();
        Assert.Equal(cold, cached);
    }

    [Fact]
    public void RepeatedResizeAndTwoWindows_RemainIndependent()
    {
        using var firstWindow = new HiddenNativeWindow(192, 128);
        using var secondWindow = new HiddenNativeWindow(192, 128);
        using var context = new RenderContext(RenderBackend.Software);
        using var first = context.CreateRenderTarget(firstWindow.Hwnd, 192, 128);
        using var second = context.CreateRenderTarget(secondWindow.Hwnd, 192, 128);
        using var red = context.CreateSolidBrush(0.92f, 0.04f, 0.03f, 1f);
        using var green = context.CreateSolidBrush(0.03f, 0.88f, 0.08f, 1f);

        int firstWidth = 0;
        int firstHeight = 0;
        int secondWidth = 0;
        int secondHeight = 0;
        for (int frame = 0; frame < 32; frame++)
        {
            firstWidth = 128 + frame % 4 * 16;
            firstHeight = 80 + frame % 3 * 12;
            secondWidth = 176 - frame % 4 * 12;
            secondHeight = 116 - frame % 3 * 10;
            Assert.Equal(JaliumResult.Ok, first.Resize(firstWidth, firstHeight));
            Assert.Equal(JaliumResult.Ok, second.Resize(secondWidth, secondHeight));

            first.SetFullInvalidation();
            Assert.True(first.TryBeginDraw());
            first.Clear(0.01f, 0.02f, 0.03f, 1f);
            first.FillRectangle(0, 0, firstWidth, firstHeight, red);
            if (frame == 31)
                Assert.Equal(JaliumResult.Ok, first.RequestReadback());
            Assert.Equal(JaliumResult.Ok, first.TryEndDraw());

            second.SetFullInvalidation();
            Assert.True(second.TryBeginDraw());
            second.Clear(0.01f, 0.02f, 0.03f, 1f);
            second.FillRectangle(0, 0, secondWidth, secondHeight, green);
            if (frame == 31)
                Assert.Equal(JaliumResult.Ok, second.RequestReadback());
            Assert.Equal(JaliumResult.Ok, second.TryEndDraw());
        }

        var firstPixels = new byte[firstWidth * firstHeight * 4];
        Assert.Equal(
            JaliumResult.Ok,
            first.FetchReadback(
                firstPixels, (uint)firstWidth * 4,
                out int capturedFirstWidth, out int capturedFirstHeight));
        Assert.Equal(firstWidth, capturedFirstWidth);
        Assert.Equal(firstHeight, capturedFirstHeight);
        int firstCenter =
            ((firstHeight / 2) * firstWidth + firstWidth / 2) * 4;
        Assert.True(firstPixels[firstCenter + 2] >= 220);
        Assert.True(firstPixels[firstCenter + 1] <= 30);

        var secondPixels = new byte[secondWidth * secondHeight * 4];
        Assert.Equal(
            JaliumResult.Ok,
            second.FetchReadback(
                secondPixels, (uint)secondWidth * 4,
                out int capturedSecondWidth, out int capturedSecondHeight));
        Assert.Equal(secondWidth, capturedSecondWidth);
        Assert.Equal(secondHeight, capturedSecondHeight);
        int secondCenter =
            ((secondHeight / 2) * secondWidth + secondWidth / 2) * 4;
        Assert.True(secondPixels[secondCenter + 1] >= 210);
        Assert.True(secondPixels[secondCenter + 2] <= 30);
    }

    private static byte[] Render(int workerCount, string? simd = null)
    {
        Environment.SetEnvironmentVariable(
            "JALIUM_SOFTWARE_THREADS",
            workerCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable("JALIUM_SOFTWARE_SIMD", simd);

        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(RenderBackend.Software);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var background = context.CreateSolidBrush(0.04f, 0.06f, 0.09f, 1f);
        using var card = context.CreateSolidBrush(0.15f, 0.24f, 0.38f, 0.82f);
        using var outline = context.CreateSolidBrush(0.7f, 0.82f, 0.95f, 0.65f);

        Assert.True(target.TryBeginDraw());
        target.Clear(0.01f, 0.02f, 0.03f, 1f);
        target.FillRectangle(0f, 0f, Width, Height, background);
        target.FillRectangle(17.25f, 21.5f, 390.75f, 246.25f, card);
        target.FillPerCornerRoundedRectangle(
            53.25f, 64.5f, 260.5f, 132.25f,
            18f, 11f, 24f, 8f, card);
        target.DrawPerCornerRoundedRectangle(
            53.25f, 64.5f, 260.5f, 132.25f,
            18f, 11f, 24f, 8f, outline, 1.5f);
        target.FillEllipse(450.5f, 144.25f, 54f, 37f, card);
        target.DrawEllipse(450.5f, 144.25f, 58f, 41f, outline, 1.25f);

        nint layer = target.RealizeLayerBegin(0, 470f, 300f, 120f, 90f);
        Assert.NotEqual(nint.Zero, layer);
        target.FillRectangle(470f, 300f, 120f, 90f, outline);
        target.RealizeLayerEnd(layer);
        target.CompositeLayer(layer, 466.25f, 306.5f, 120f, 90f, 0.63f);

        byte[] pixels = FinishAndRead(target);
        target.DestroyRetainedLayer(layer);
        return pixels;
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
        Assert.True(
            pixels[offset + 2] < 30 && pixels[offset + 1] < 30,
            $"Expected background at ({x},{y}), BGRA=({pixels[offset]},{pixels[offset + 1]},{pixels[offset + 2]},{pixels[offset + 3]})." );
    }

    private static void AssertRed(byte[] pixels, int x, int y)
    {
        int offset = (y * Width + x) * 4;
        Assert.True(
            pixels[offset + 2] >= 220 && pixels[offset + 1] <= 30 && pixels[offset] <= 25,
            $"Expected red at ({x},{y}), BGRA=({pixels[offset]},{pixels[offset + 1]},{pixels[offset + 2]},{pixels[offset + 3]})." );
    }
}
