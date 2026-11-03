using System.Linq;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssDashShapeNativeTests
{
    private const int Width = 128;
    private const int Height = 112;
    private static readonly Rect ShapeRect = new(12, 12, 96, 72);

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_closed_shape_dashes_match_path_strokes() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_closed_shape_dashes_match_path_strokes() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_closed_shape_dashes_match_path_strokes() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        using var window = new HiddenNativeWindow(Width, Height);
        using var context = new RenderContext(backend, GpuPreference.Auto, RenderingEngine.Impeller);
        using var target = context.CreateRenderTarget(window.Hwnd, Width, Height);
        using var drawing = new RenderTargetDrawingContext(target, context);

        byte[] Paint(int shape, bool direct, double offset,
            bool solid = false, bool zeroDashes = false)
        {
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw())
            {
                Assert.True(Environment.TickCount64 < deadline);
                Thread.Sleep(1);
            }
            target.Clear(0, 0, 0, 1);
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(255, 0, 0)), 4)
            {
                DashCap = PenLineCap.Round,
                DashStyle = solid ? DashStyles.Solid
                    : zeroDashes ? new DashStyle([0, 0], offset)
                    : new DashStyle([2, 2, 1], offset),
            };
            if (direct)
            {
                switch (shape)
                {
                    case 0: drawing.DrawRectangle(null, pen, ShapeRect); break;
                    case 1: drawing.DrawRoundedRectangle(null, pen, ShapeRect, 18, 12); break;
                    case 2: drawing.DrawRoundedRectangle(null, pen, ShapeRect,
                        new CornerRadius(0, 18, 6, 12)); break;
                    default: drawing.DrawEllipse(null, pen, new Point(60, 48), 42, 28); break;
                }
            }
            else
            {
                var path = new PathGeometry();
                path.Figures.Add(shape switch
                {
                    0 => CssRoundedRectangleGeometry.Figure(ShapeRect, default),
                    1 => CssRoundedRectangleGeometry.Figure(ShapeRect,
                        new CssUsedBorderRadii(new Size(18, 12), new Size(18, 12),
                            new Size(18, 12), new Size(18, 12))),
                    2 => CssRoundedRectangleGeometry.Figure(ShapeRect,
                        new CssUsedBorderRadii(new Size(0, 0), new Size(18, 18),
                            new Size(6, 6), new Size(12, 12))),
                    _ => EllipseFigure(),
                });
                drawing.DrawGeometry(null, pen, path);
            }
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[Width * Height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, Width * 4u, out _, out _));
            return pixels;
        }

        for (int shape = 0; shape < 4; shape++)
        {
            foreach (var offset in new[] { 0.0, -1000.0, 1000.0 })
                Assert.Equal(Paint(shape, false, offset), Paint(shape, true, offset));
            Assert.False(Paint(shape, true, 0, solid: true)
                .SequenceEqual(Paint(shape, true, 0)));
            Assert.Equal(Paint(shape, false, 0, zeroDashes: true),
                Paint(shape, true, 0, zeroDashes: true));
        }
    }

    private static PathFigure EllipseFigure()
    {
        var figure = new PathFigure
        {
            StartPoint = new Point(102, 48),
            IsClosed = true,
            IsFilled = true,
        };
        var radius = new Size(42, 28);
        foreach (var point in new[]
                 {
                     new Point(60, 76), new Point(18, 48),
                     new Point(60, 20), new Point(102, 48),
                 })
            figure.Segments.Add(new ArcSegment(point, radius, 0, false,
                SweepDirection.Clockwise, true));
        return figure;
    }
}
