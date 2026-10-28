using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class StrokeContourBufferRenderingTests
{
    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_HotStrokesMatchColdPixelsDuringCacheChurn()
    {
        const int size = 128;
        using var window = new HiddenNativeWindow(size, size);
        using var context = new RenderContext(RenderBackend.D3D12);
        using var target = context.CreateRenderTarget(window.Hwnd, size, size);
        using var blue = context.CreateSolidBrush(0.1f, 0.3f, 1, 0.55f);
        using var red = context.CreateSolidBrush(1, 0.1f, 0.2f, 0.4f);
        target.SetRenderingEngine(RenderingEngine.Impeller);
        float[] points = new float[4];
        byte[] expected = DrawFrame(DrawHotStrokes);
        Assert.True(Enumerable.Range(0, size * size).Any(index =>
            expected[index * 4] != 0 || expected[index * 4 + 2] != 0));
        for (int phase = 0; phase < 2; phase++)
        {
            DrawFrame(() =>
            {
                for (int index = 0; index < 1800; index++)
                {
                    points[0] = 4.125f + (index % 16) * 7;
                    points[1] = 4.375f + ((index / 16) % 16) * 7;
                    points[2] = points[0] + 3.25f + (index + phase * 1800) * (1.0f / 8192);
                    points[3] = points[1] + 2.75f;
                    target.DrawPolygon(points, blue, 1.375f, closed: false);
                    // Promote hot entries repeatedly while enough distinct
                    // geometry arrives to replace the remaining cache entries.
                    if (index % 3 == 0) DrawHotStroke((index / 3) % 14);
                }
            });
            Assert.Equal(expected, DrawFrame(DrawHotStrokes));
        }

        void DrawHotStrokes()
        {
            for (int index = 0; index < 14; index++) DrawHotStroke(index);
        }

        void DrawHotStroke(int index)
        {
            points[0] = 8.125f + index * 7;
            points[1] = 12.375f + (index % 4) * 17;
            points[2] = points[0] + 8.25f;
            points[3] = points[1] + 20.75f;
            target.DrawPolygon(points, (index & 1) == 0 ? blue : red,
                1.25f + (index % 4) * 0.125f, closed: false);
        }

        byte[] DrawFrame(Action draw)
        {
            target.SetFullInvalidation();
            long deadline = Environment.TickCount64 + 5000;
            while (!target.TryBeginDraw())
            {
                Assert.True(Environment.TickCount64 < deadline, "Render target did not become ready.");
                Thread.Sleep(1);
            }
            target.Clear(0, 0, 0, 1);
            target.PushRoundedRectClip(6, 6, 116, 116, 9, 9);
            target.PushOpacity(0.65f);
            draw();
            target.PopOpacity();
            target.PopClip();
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[size * size * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, size * 4u, out _, out _));
            return pixels;
        }
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_DeferredStrokesSurviveCacheEvictionAndSourceReuse()
    {
        const int size = 128;
        using var window = new HiddenNativeWindow(size, size);
        using var context = new RenderContext(RenderBackend.D3D12);
        using var target = context.CreateRenderTarget(window.Hwnd, size, size);
        using var blue = context.CreateSolidBrush(0.1f, 0.3f, 1, 0.55f);
        using var red = context.CreateSolidBrush(1, 0.1f, 0.2f, 0.4f);
        using var black = context.CreateSolidBrush(0, 0, 0, 1);
        target.SetRenderingEngine(RenderingEngine.Impeller);
        foreach (bool roundedClip in new[] { false, true })
        {
            byte[] expected = DrawFrame(flushEach: true, roundedClip);
            byte[] actual = DrawFrame(flushEach: false, roundedClip);
            Assert.Equal(expected, actual);
            Assert.True(Enumerable.Range(0, size * size).Any(index =>
                actual[index * 4] != 0 || actual[index * 4 + 2] != 0));
        }

        byte[] DrawFrame(bool flushEach, bool roundedClip)
        {
            target.SetFullInvalidation();
            long deadline = Environment.TickCount64 + 5000;
            while (!target.TryBeginDraw())
            {
                Assert.True(Environment.TickCount64 < deadline, "Render target did not become ready.");
                Thread.Sleep(1);
            }
            target.Clear(0, 0, 0, 1);
            if (roundedClip) target.PushRoundedRectClip(8, 8, 108, 108, 9, 9);
            target.PushOpacity(0.65f);
            float[] points = new float[4];
            // The analytic stroke cache holds 512 entries. Keep substantially
            // more unique strokes pending, while reusing the caller's array.
            for (int index = 0; index < 2400; index++)
            {
                points[0] = 4.125f + (index % 16) * 7;
                points[1] = 4.375f + ((index / 16) % 16) * 7;
                points[2] = points[0] + 3.25f + index * (1.0f / 8192);
                points[3] = points[1] + 2.75f;
                target.DrawPolygon(points, (index & 1) == 0 ? blue : red,
                    1.25f + (index % 5) * 0.125f, closed: false);
                // A direct draw drains the pending Impeller batch. Black over
                // the unused opaque-black corner leaves the reference intact.
                if (flushEach) target.FillRectangle(120, 120, 1, 1, black);
            }
            target.PopOpacity();
            if (roundedClip) target.PopClip();
            target.FillRectangle(120, 120, 1, 1, black);
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[size * size * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, size * 4u, out _, out _));
            return pixels;
        }
    }

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_ShortStrokeAfterLargeStrokeMatchesFreshTarget() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_ShortStrokeAfterLargeStrokeMatchesFreshTarget() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_ReusedStrokeBatchesPreserveClipsOpacityAndOrder_AfterIdleReclamation()
    {
        const int size = 128;
        using var window = new HiddenNativeWindow(size, size);
        using var context = new RenderContext(RenderBackend.D3D12);
        using var target = context.CreateRenderTarget(window.Hwnd, size, size);
        using var blue = context.CreateSolidBrush(0.1f, 0.3f, 1, 0.55f);
        using var red = context.CreateSolidBrush(1, 0.1f, 0.2f, 0.4f);
        target.SetRenderingEngine(RenderingEngine.Impeller);
        byte[] expected = DrawFrame(0);
        Assert.Contains(expected, value => value != 0);
        for (int i = 1; i <= 24; i++) DrawFrame(i);
        Assert.Equal(expected, DrawFrame(0));
        Thread.Sleep(2100);
        target.ReclaimIdleResources();
        Assert.Equal(expected, DrawFrame(0));

        byte[] DrawFrame(int phase)
        {
            target.SetFullInvalidation();
            long deadline = Environment.TickCount64 + 5000;
            while (!target.TryBeginDraw())
            {
                Assert.True(Environment.TickCount64 < deadline, "Render target did not become ready.");
                Thread.Sleep(1);
            }
            target.Clear(0, 0, 0, 0);
            float dx = phase % 7;
            target.PushClip(12 + dx, 10, 64, 34);
            target.PushOpacity(0.65f);
            target.DrawPolygon([8, 18, 8, 18, 91, 39], blue, 7.25f, closed: false);
            target.PopOpacity();
            target.PopClip();
            // A direct draw drains the deferred stroke, then a different clip
            // and brush reuse its geometry storage within the same frame.
            target.FillRectangle(26, 19, 24, 29, red);
            target.PushRoundedRectClip(32, 24, 60, 48, 8, 8);
            target.DrawPolygon([23, 61, 62, 24, 104, 67], blue, 11, closed: false);
            target.PopClip();
            target.FillRectangle(64, 40, 14, 25, red);
            target.DrawPolygon([7, 87, 106, 97], blue, 2.75f, closed: false);
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[size * size * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, size * 4u, out _, out _));
            return pixels;
        }
    }

    private static void Verify(RenderBackend backend)
    {
        foreach (int count in new[] { 2, 9, 34 })
        {
            byte[] expected = Render(backend, count, warmWithLargeStroke: false);
            byte[] actual = Render(backend, count, warmWithLargeStroke: true);
            Assert.True(expected.SequenceEqual(actual), $"Stroke with {count} points retained previous geometry.");
            Assert.Contains(actual, value => value != 0);
        }
    }

    private static byte[] Render(RenderBackend backend, int points, bool warmWithLargeStroke)
    {
        const int size = 128;
        using var window = new HiddenNativeWindow(size, size);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, size, size);
        if (backend == RenderBackend.D3D12) target.SetRenderingEngine(RenderingEngine.Impeller);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var pen = new Pen(new SolidColorBrush(Colors.Blue), 2.75);
        if (warmWithLargeStroke)
        {
            Begin();
            target.Clear(0, 0, 0, 0);
            drawing.DrawGeometry(null, new Pen(new SolidColorBrush(Colors.Red), 11), MakeCurve(800, 2.25));
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        }

        Begin();
        target.Clear(0, 0, 0, 0);
        drawing.DrawGeometry(null, pen, MakeCurve(points, 17.375));
        Assert.Equal(JaliumResult.Ok, target.RequestReadback());
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        var pixels = new byte[size * size * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, size * 4u, out _, out _));
        return pixels;

        void Begin()
        {
            target.SetFullInvalidation();
            long deadline = Environment.TickCount64 + 5000;
            while (!target.TryBeginDraw())
            {
                Assert.True(Environment.TickCount64 < deadline, "Render target did not become ready.");
                Thread.Sleep(1);
            }
            if (backend == RenderBackend.D3D12) Assert.Equal(RenderingEngine.Impeller, target.RenderingEngine);
        }
    }

    private static PathGeometry MakeCurve(int count, double origin)
    {
        var points = new PointCollection();
        for (int i = 1; i < count; i++)
        {
            double t = (double)i / (count - 1);
            points.Add(new Point(origin + 85 * t, origin + 65 * t * t * (3 - 2 * t)));
        }
        var figure = new PathFigure { StartPoint = new Point(origin, origin), IsClosed = false };
        figure.Segments.Add(new PolyLineSegment(points, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }
}
