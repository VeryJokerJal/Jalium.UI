using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CoverageRectBatchRenderingTests
{
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_CoverageBurstBeyondInstanceCapacity_PreservesTailAndPainterOrder()
    {
        const int size = 128;
        using var window = new HiddenNativeWindow(size, size);
        using var context = new RenderContext(RenderBackend.D3D12);
        using var target = context.CreateRenderTarget(window.Hwnd, size, size);
        using var red = context.CreateSolidBrush(1, 0, 0, 0.55f);
        using var blue = context.CreateSolidBrush(0, 0, 1, 0.7f);
        using var white = context.CreateSolidBrush(1, 1, 1);
        using var green = context.CreateSolidBrush(0, 1, 0);
        target.SetRenderingEngine(RenderingEngine.Impeller);
        float[] warmStroke = [8.375f, 8.375f, 75.625f, 113.125f];
        float[] tailStroke = [101.375f, 17.375f, 117.625f, 108.125f];
        byte[] expected = Draw(0);
        // The diagonal produces hundreds of analytic coverage boxes per call.
        // 4096 cached copies cross the 262144-instance limit inside one batch.
        Assert.Equal(expected, Draw(4096));
        Assert.Equal(expected, Draw(0));
        Assert.True(expected[(9 * size + 115) * 4 + 3] > 0, "The first coverage chunk must remain visible.");
        Assert.True(expected[(108 * size + 118) * 4 + 3] > 0, "The late blue cap must remain visible.");
        Thread.Sleep(2100);
        target.ReclaimIdleResources();
        Assert.Equal(expected, Draw(0));

        byte[] Draw(int copies)
        {
            Begin(target);
            target.PushClip(0, 0, size, size);
            target.DrawPolygon([98, 9, 122, 9], green, 2.75f, closed: false);
            for (int i = 0; i < copies; i++) target.DrawPolygon(warmStroke, red, 2.75f, closed: false);
            // This distinct tail shares the large deferred stroke batch. The
            // later white rectangle hides the warm-up but leaves the tail visible.
            target.DrawPolygon(tailStroke, blue, 7.25f, closed: false);
            target.FillEllipse(117.625f, 108.125f, 3.625f, 3.625f, blue);
            target.FillRectangle(0, 0, 92, size, white);
            target.FillRectangle(105, 42, 18, 20, green);
            target.PopClip();
            return End(target, size);
        }
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_InterleavedCoverageClipsAndOpacity_MatchSeparatedDraws()
    {
        const int size = 192;
        using var window = new HiddenNativeWindow(size, size);
        using var context = new RenderContext(RenderBackend.D3D12);
        using var target = context.CreateRenderTarget(window.Hwnd, size, size);
        using var blue = context.CreateSolidBrush(0.1f, 0.3f, 1, 0.55f);
        using var red = context.CreateSolidBrush(1, 0.1f, 0.2f, 0.4f);
        target.SetRenderingEngine(RenderingEngine.Impeller);
        foreach (float dpi in new[] { 96f, 144f, 192f })
        {
            target.SetDpi(dpi, dpi);
            Assert.Equal(Draw(separate: true), Draw(separate: false));
        }

        byte[] Draw(bool separate)
        {
            Begin(target);
            target.PushClip(5, 4, 83, 85);
            for (int i = 0; i < 12; i++)
            {
                float dx = i % 4;
                target.PushTransform([0.95f, 0.15f, -0.1f, 0.8f, dx, 1.375f]);
                target.PushRoundedRectClip(12 + dx, 8 + i, 63, 46, 7, 7);
                target.PushOpacity(0.65f);
                target.DrawPolygon([8, 18 + i, 8, 18 + i, 81, 39 + i], blue, 2.75f, closed: false);
                Barrier();
                target.FillEllipse(8, 18 + i, 1.375f, 1.375f, blue);
                Barrier();
                target.FillEllipse(81, 39 + i, 1.375f, 1.375f, blue);
                Barrier();
                target.PopOpacity();
                target.PopClip();
                target.FillRectangle(26 + dx, 19 + i, 24, 8, red);
                target.PopTransform();
                // The deferred stroke must retain its clip after the live clip
                // has changed, and must remain behind later direct primitives.
                target.PushClip(34, 43, 44, 32);
                target.DrawPolygon([23, 61, 62, 24, 84, 67], red, 0.75f, closed: false);
                Barrier();
                target.PopClip();
            }
            target.PopClip();
            return End(target, size);

            void Barrier()
            {
                // An offscreen general SDF rectangle separates compatible
                // coverage draws without changing the visible reference image.
                if (separate) target.FillRectangle(-64, -64, 1, 1, red);
            }
        }
    }

    private static void Begin(RenderTarget target)
    {
        target.SetFullInvalidation();
        long deadline = Environment.TickCount64 + 5000;
        while (!target.TryBeginDraw())
        {
            Assert.True(Environment.TickCount64 < deadline, "Render target did not become ready.");
            Thread.Sleep(1);
        }
        target.Clear(0, 0, 0, 0);
    }

    private static byte[] End(RenderTarget target, int size)
    {
        Assert.Equal(JaliumResult.Ok, target.RequestReadback());
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        var pixels = new byte[size * size * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, (uint)size * 4, out _, out _));
        return pixels;
    }
}
