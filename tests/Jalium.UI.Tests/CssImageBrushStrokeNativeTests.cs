using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssImageBrushStrokeNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_ImageBrushStrokeKeepsItsPixels() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_ImageBrushStrokeKeepsItsPixels() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_ImageBrushStrokeKeepsItsPixels() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 96, height = 96;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);
        Assert.Equal(backend, context.Backend);

        var source = new WriteableBitmap(2, 1, 96, 96, PixelFormat.Bgra32, null);
        source.WritePixels(new Int32Rect(0, 0, 2, 1),
            [0, 0, 255, 255, 255, 0, 0, 255], 8, 0);
        var brush = new ImageBrush(source);
        var pen = new Pen(brush, 8);
        var dashedPen = new Pen(brush, 8)
        {
            DashStyle = new DashStyle([1.0, 2.0], 0),
        };
        var flatDashPen = new Pen(brush, 8)
        {
            DashStyle = new DashStyle([1.0, 2.0], 0),
            DashCap = PenLineCap.Flat,
        };
        var roundDotPen = new Pen(brush, 8)
        {
            DashStyle = new DashStyle([0.0, 2.0], 0),
            DashCap = PenLineCap.Round,
        };
        var squareDotPen = new Pen(brush, 8)
        {
            DashStyle = new DashStyle([0.0, 2.0], 0),
            DashCap = PenLineCap.Square,
        };
        var joinSource = new WriteableBitmap(1, 1, 96, 96, PixelFormat.Bgra32, null);
        joinSource.WritePixels(new Int32Rect(0, 0, 1, 1),
            [0, 0, 255, 255], 4, 0);
        var joinBrush = new ImageBrush(joinSource);
        Pen JoinPen(PenLineJoin join, double miterLimit = 10) => new(joinBrush, 8)
        {
            DashStyle = new DashStyle([20.0, 1.0], 0),
            DashCap = PenLineCap.Flat,
            LineJoin = join,
            MiterLimit = miterLimit,
        };
        var cornerPath = Geometry.Parse("M8,24 L48,24 L48,64");
        var acuteCornerPath = Geometry.Parse("M8,40 L48,40 L8,16");
        var partlyStroked = (PathGeometry)Geometry.Parse("M8,80 L48,80 L88,80");
        ((LineSegment)partlyStroked.Figures[0].Segments[1]).IsStroked = false;
        var fullyUnstroked = (PathGeometry)Geometry.Parse("M8,80 L88,80");
        ((LineSegment)fullyUnstroked.Figures[0].Segments[0]).IsStroked = false;
        var closedWithHiddenSide = (PathGeometry)Geometry.Parse("M8,8 L88,8 L88,88 L8,88 Z");
        ((LineSegment)closedWithHiddenSide.Figures[0].Segments[1]).IsStroked = false;
        var zeroDashPen = new Pen(brush, 8)
        {
            DashStyle = new DashStyle([0.0, 0.0], 0),
        };

        byte[] Paint(Action<DrawingContext> paint, bool recorded)
        {
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw())
            {
                Assert.True(Environment.TickCount64 < deadline);
                Thread.Sleep(1);
            }
            target.Clear(0, 0, 0, 1);
            if (recorded)
            {
                var recorder = new DrawingRecorder();
                recorder.BindWholeFrame();
                paint(recorder);
                DrawingReplayer.Replay(recorder.Commit(), drawing);
            }
            else paint(drawing);
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static byte Channel(byte[] pixels, int x, int y, int channel)
            => pixels[(y * width + x) * 4 + channel];

        void AssertPattern(byte[] pixels, int leftX, int rightX, int y, string kind, bool recorded)
        {
            Assert.True(Channel(pixels, leftX, y, 2) > 220 && Channel(pixels, leftX, y, 0) < 30,
                $"{backend} {kind} recorded={recorded}: left stroke lost red pixels " +
                $"(R={Channel(pixels, leftX, y, 2)}, B={Channel(pixels, leftX, y, 0)})");
            Assert.True(Channel(pixels, rightX, y, 0) > 220 && Channel(pixels, rightX, y, 2) < 30,
                $"{backend} {kind} recorded={recorded}: right stroke lost blue pixels");
        }

        foreach (var recorded in new[] { false, true })
        {
            var rectangle = Paint(dc => dc.DrawRectangle(null, pen, new Rect(8, 8, 80, 40)), recorded);
            AssertPattern(rectangle, 20, 76, 8, "rectangle", recorded);
            Assert.Equal((byte)0, Channel(rectangle, 48, 28, 2));

            var closedDashes = Paint(dc => dc.DrawRectangle(null, dashedPen,
                new Rect(8, 8, 80, 80)), recorded);
            Assert.True(Channel(closedDashes, 5, 5, 2) > 220,
                $"{backend} recorded={recorded}: closed dashes lost their miter seam");

            var rounded = Paint(dc => dc.DrawRoundedRectangle(null, pen,
                new Rect(8, 8, 80, 40), 8, 8), recorded);
            AssertPattern(rounded, 20, 76, 8, "rounded rectangle", recorded);

            var perCorner = Paint(dc => dc.DrawRoundedRectangle(null, pen,
                new Rect(8, 8, 80, 40), new CornerRadius(6, 0, 0, 6)), recorded);
            AssertPattern(perCorner, 20, 76, 8, "per-corner rectangle", recorded);

            var ellipse = Paint(dc => dc.DrawEllipse(null, pen,
                new Point(48, 28), 40, 20), recorded);
            AssertPattern(ellipse, 8, 88, 28, "ellipse", recorded);

            var line = Paint(dc => dc.DrawLine(pen, new Point(8, 60), new Point(88, 60)), recorded);
            AssertPattern(line, 20, 76, 60, "line", recorded);

            var path = Paint(dc => dc.DrawGeometry(null, pen,
                Geometry.Parse("M8,80 L88,80")), recorded);
            AssertPattern(path, 20, 76, 80, "path", recorded);

            var dashedLine = Paint(dc => dc.DrawLine(dashedPen,
                new Point(8, 60), new Point(88, 60)), recorded);
            AssertPattern(dashedLine, 12, 84, 60, "dashed line", recorded);
            Assert.True(Channel(dashedLine, 18, 60, 2) > 220,
                $"{backend} recorded={recorded}: square dash cap was lost");
            Assert.Equal((byte)0, Channel(dashedLine, 24, 60, 2));

            var flatDashes = Paint(dc => dc.DrawLine(flatDashPen,
                new Point(8, 60), new Point(88, 60)), recorded);
            AssertPattern(flatDashes, 12, 84, 60, "flat-cap dashed line", recorded);
            Assert.Equal((byte)0, Channel(flatDashes, 18, 60, 2));

            var roundDots = Paint(dc => dc.DrawLine(roundDotPen,
                new Point(8, 60), new Point(88, 60)), recorded);
            Assert.True(Channel(roundDots, 24, 60, 2) > 220);
            Assert.Equal((byte)0, Channel(roundDots, 16, 60, 2));

            var squareDots = Paint(dc => dc.DrawLine(squareDotPen,
                new Point(8, 60), new Point(88, 60)), recorded);
            Assert.True(Channel(squareDots, 24, 60, 2) > 220);
            Assert.Equal((byte)0, Channel(squareDots, 16, 60, 2));

            var dashedPath = Paint(dc => dc.DrawGeometry(null, dashedPen,
                Geometry.Parse("M8,80 L48,80 L88,80")), recorded);
            AssertPattern(dashedPath, 12, 84, 80, "dashed path", recorded);
            Assert.Equal((byte)0, Channel(dashedPath, 24, 80, 2));

            var miter = Paint(dc => dc.DrawGeometry(null,
                JoinPen(PenLineJoin.Miter), cornerPath), recorded);
            var bevel = Paint(dc => dc.DrawGeometry(null,
                JoinPen(PenLineJoin.Bevel), cornerPath), recorded);
            var roundJoin = Paint(dc => dc.DrawGeometry(null,
                JoinPen(PenLineJoin.Round), cornerPath), recorded);
            var limitedMiter = Paint(dc => dc.DrawGeometry(null,
                JoinPen(PenLineJoin.Miter, 1), cornerPath), recorded);
            Assert.True(Channel(miter, 51, 21, 2) > 220);
            Assert.True(Channel(bevel, 50, 21, 2) < 80);
            Assert.True(Channel(roundJoin, 50, 21, 2) > 180);
            Assert.True(Channel(roundJoin, 51, 21, 2) < 80);
            Assert.True(Channel(limitedMiter, 51, 21, 2) < 80);

            var acuteMiter = Paint(dc => dc.DrawGeometry(null,
                JoinPen(PenLineJoin.Miter), acuteCornerPath), recorded);
            Assert.True(Channel(acuteMiter, 56, 42, 2) > 220,
                $"{backend} recorded={recorded}: acute miter lost its image pixels");

            brush.ScalingMode = BitmapScalingMode.NearestNeighbor;
            var visibleSegments = Paint(dc => dc.DrawGeometry(null, pen,
                partlyStroked), recorded);
            AssertPattern(visibleSegments, 20, 40, 80, "partly stroked path", recorded);
            Assert.Equal((byte)0, Channel(visibleSegments, 76, 80, 2));
            Assert.Equal((byte)0, Channel(visibleSegments, 76, 80, 0));

            var hiddenSegments = Paint(dc => dc.DrawGeometry(null, pen,
                fullyUnstroked), recorded);
            Assert.Equal((byte)0, Channel(hiddenSegments, 20, 80, 2));
            Assert.Equal((byte)0, Channel(hiddenSegments, 76, 80, 0));

            var closedSegments = Paint(dc => dc.DrawGeometry(null, pen,
                closedWithHiddenSide), recorded);
            AssertPattern(closedSegments, 20, 76, 8, "closed path", recorded);
            Assert.Equal((byte)0, Channel(closedSegments, 88, 48, 2));
            Assert.Equal((byte)0, Channel(closedSegments, 88, 48, 0));
            Assert.True(Channel(closedSegments, 5, 5, 2) > 100,
                $"{backend} recorded={recorded}: the closed path lost its starting join");
            brush.ScalingMode = BitmapScalingMode.Unspecified;

            var zeroDashes = Paint(dc => dc.DrawLine(zeroDashPen,
                new Point(8, 60), new Point(88, 60)), recorded);
            AssertPattern(zeroDashes, 20, 76, 60, "all-zero dashes", recorded);
        }

        source.WritePixels(new Int32Rect(0, 0, 2, 1),
            [255, 0, 0, 255, 0, 0, 255, 255], 8, 0);
        var updated = Paint(dc => dc.DrawRectangle(null, pen, new Rect(8, 8, 80, 40)), false);
        Assert.True(Channel(updated, 20, 8, 0) > 220 && Channel(updated, 20, 8, 2) < 30,
            $"{backend}: stroke retained stale source pixels");
        Assert.True(Channel(updated, 76, 8, 2) > 220 && Channel(updated, 76, 8, 0) < 30,
            $"{backend}: stroke retained stale source pixels");

        updated = Paint(dc => dc.DrawLine(dashedPen,
            new Point(8, 60), new Point(88, 60)), false);
        Assert.True(Channel(updated, 12, 60, 0) > 220 && Channel(updated, 12, 60, 2) < 30,
            $"{backend}: dashed stroke retained stale source pixels");
        Assert.True(Channel(updated, 84, 60, 2) > 220 && Channel(updated, 84, 60, 0) < 30,
            $"{backend}: dashed stroke retained stale source pixels");
    }
}
