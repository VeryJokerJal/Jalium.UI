using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class PolygonOffsetRenderingTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_OffsetMatchesTranslatedCoordinates() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_OffsetMatchesTranslatedCoordinates() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_BorrowedPolygonCoordinatesMatchOwnedArrays() => VerifyBorrowedBuffers(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_BorrowedPolygonCoordinatesMatchOwnedArrays() => VerifyBorrowedBuffers(RenderBackend.D3D12);

    private static void VerifyBorrowedBuffers(RenderBackend backend)
    {
        foreach (var engine in backend == RenderBackend.D3D12
            ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello }
            : new[] { RenderingEngine.Auto })
        {
            var expected = RenderBuffers(backend, engine, mode: 0);
            Assert.Equal(expected, RenderBuffers(backend, engine, mode: 1));
            Assert.Equal(expected, RenderBuffers(backend, engine, mode: 2));
        }
    }

    private static byte[] RenderBuffers(RenderBackend backend, RenderingEngine engine, int mode)
    {
        const int size = 128;
        using var window = new HiddenNativeWindow(size, size);
        using var context = new RenderContext(backend, GpuPreference.Auto, engine);
        using var target = context.CreateRenderTarget(window.Hwnd, size, size);
        using var drawing = new RenderTargetDrawingContext(target, context);
        using var fill = context.CreateSolidBrush(1, 1, 1);
        using var stroke = context.CreateSolidBrush(1, 0, 0);
        long deadline = Environment.TickCount64 + 5000;
        while (!target.TryBeginDraw())
        {
            Assert.True(Environment.TickCount64 < deadline);
            Thread.Sleep(1);
        }
        target.Clear(0, 0, 0, 1);
        // Alternate across the stack/pool boundary and then shrink again. A
        // deferred native scene must own its coordinates before the next draw
        // reuses the buffer, and must ignore all unused pooled capacity.
        int shape = 0;
        foreach (int count in new[] { 257, 3, 129, 17, 128, 7 })
        {
            var coordinates = new float[count * 2];
            var points = new Point[count];
            for (int i = 0; i < count; i++)
            {
                double angle = 2 * Math.PI * i / count + 0.17;
                coordinates[i * 2] = (float)(34 + shape * 10 + 20 * Math.Cos(angle));
                coordinates[i * 2 + 1] = (float)(34 + shape * 10 + 20 * Math.Sin(angle));
                points[i] = new Point(coordinates[i * 2], coordinates[i * 2 + 1]);
            }
            if (mode == 0)
            {
                target.FillPolygon(coordinates, fill, 1);
                target.DrawPolygon(coordinates, stroke, 3);
            }
            else if (mode == 1)
            {
                // Deliberately poison both the prefix and tail of a larger
                // buffer. The submitted slice is the only valid polygon.
                var storage = Enumerable.Repeat(-1000f, coordinates.Length + 64).ToArray();
                coordinates.CopyTo(storage, 16);
                var active = storage.AsSpan(16, coordinates.Length);
                target.FillPolygon(active, fill, 1);
                target.DrawPolygon(active, stroke, 3);
                storage.AsSpan().Fill(1000);
            }
            else
            {
                var figure = new PathFigure(points[0],
                    [new PolyLineSegment(points.Skip(1))], true);
                var geometry = new PathGeometry([figure], FillRule.Nonzero, null);
                geometry.Freeze();
                drawing.DrawGeometry(new SolidColorBrush(Colors.White),
                    new Pen(new SolidColorBrush(Colors.Red), 3), geometry);
            }
            shape++;
        }
        Assert.Equal(JaliumResult.Ok, target.RequestReadback());
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        var pixels = new byte[size * size * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, size * 4u, out _, out _));
        Assert.True(Enumerable.Range(0, size * size).Count(i =>
            pixels[i * 4] > 200 && pixels[i * 4 + 1] > 200 && pixels[i * 4 + 2] > 200) > 500);
        Assert.True(Enumerable.Range(0, size * size).Count(i =>
            pixels[i * 4] < 20 && pixels[i * 4 + 1] < 20 && pixels[i * 4 + 2] > 200) > 100);
        return pixels;
    }

    private static void Verify(RenderBackend backend)
    {
        foreach (var transform in new float[][]
        {
            [1, 0, 0, 1, 0, 0],
            [2, 0, 0, 1, 0, 0],
            [0, 1, -1, 0, 120, 0],
        })
        {
            var expected = Render(backend, transform, bakeOffset: true);
            var actual = Render(backend, transform, bakeOffset: false);
            Assert.True(expected.SequenceEqual(actual), $"Transform: {string.Join(",", transform)}");
        }
    }

    private static byte[] Render(RenderBackend backend, float[] transform, bool bakeOffset)
    {
        const int size = 128;
        using var window = new HiddenNativeWindow(size, size);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, size, size);
        using var drawing = new RenderTargetDrawingContext(target, context);
        var offset = new Point(10, 6);
        Point At(double x, double y) => bakeOffset ? new Point(x + offset.X, y + offset.Y) : new Point(x, y);
        var figure = new PathFigure { StartPoint = At(10, 10), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new PolyLineSegment([At(35, 15), At(30, 40), At(15, 35)], true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();

        long deadline = Environment.TickCount64 + 5000;
        while (!target.TryBeginDraw())
        {
            Assert.True(Environment.TickCount64 < deadline);
            Thread.Sleep(1);
        }
        target.Clear(0, 0, 0, 1);
        drawing.PushTransform(new MatrixTransform(new Matrix(
            transform[0], transform[1], transform[2], transform[3], transform[4], transform[5])));
        drawing.Offset = bakeOffset ? Point.Zero : offset;
        drawing.DrawGeometry(new SolidColorBrush(Colors.Red), new Pen(new SolidColorBrush(Colors.Blue), 3), geometry);
        drawing.Offset = Point.Zero;
        drawing.DrawRectangle(new SolidColorBrush(Colors.White), null, new Rect(2, 2, 4, 4));
        drawing.Pop();
        Assert.Equal(JaliumResult.Ok, target.RequestReadback());
        Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
        var pixels = new byte[size * size * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, size * 4u, out _, out _));
        return pixels;
    }
}
