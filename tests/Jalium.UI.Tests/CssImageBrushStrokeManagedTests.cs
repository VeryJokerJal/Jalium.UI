using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests;

public sealed class CssImageBrushStrokeManagedTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DashedLinesAndPathsShareTextureAndRespectCapsAndOffset(bool recorded)
    {
        using var source = BitmapImage.FromPixels(
            [0, 0, 255, 255, 255, 0, 0, 255], 2, 1);
        var brush = new ImageBrush(source)
        {
            ScalingMode = BitmapScalingMode.NearestNeighbor,
        };
        var flat = new Pen(brush, 8)
        {
            DashStyle = new DashStyle([1.0, 2.0], 0),
            DashCap = PenLineCap.Flat,
        };
        var square = new Pen(brush, 8)
        {
            DashStyle = new DashStyle([1.0, 2.0], 0),
            DashCap = PenLineCap.Square,
        };
        var offset = new Pen(brush, 8)
        {
            DashStyle = new DashStyle([1.0, 2.0], 1),
            DashCap = PenLineCap.Flat,
        };

        byte[] Paint(Action<DrawingContext> draw)
        {
            var target = new RenderTargetBitmap(96, 96, 96, 96, PixelFormat.Bgra32);
            var drawing = new SoftwareDrawingContext(target);
            if (recorded)
            {
                var recorder = new DrawingRecorder();
                recorder.BindWholeFrame();
                draw(recorder);
                DrawingReplayer.Replay(recorder.Commit(), drawing);
            }
            else draw(drawing);
            drawing.Close();
            return target.GetPixelBuffer();
        }

        static byte Channel(byte[] pixels, int x, int y, int channel)
            => pixels[(y * 96 + x) * 4 + channel];

        var line = Paint(dc => dc.DrawLine(flat, new Point(8, 20), new Point(88, 20)));
        Assert.True(Channel(line, 12, 20, 2) > 220);
        Assert.Equal((byte)0, Channel(line, 24, 20, 3));
        Assert.True(Channel(line, 84, 20, 0) > 220);

        var squareLine = Paint(dc => dc.DrawLine(square,
            new Point(8, 20), new Point(88, 20)));
        Assert.True(Channel(squareLine, 18, 20, 2) > 220);
        Assert.Equal((byte)0, Channel(squareLine, 24, 20, 3));

        var offsetLine = Paint(dc => dc.DrawLine(offset,
            new Point(8, 20), new Point(88, 20)));
        Assert.Equal((byte)0, Channel(offsetLine, 12, 20, 3));
        Assert.True(Channel(offsetLine, 28, 20, 2) > 220);

        var path = Paint(dc => dc.DrawGeometry(null, flat,
            Geometry.Parse("M8,40 L48,40 L88,40")));
        Assert.True(Channel(path, 36, 40, 2) > 220);
        Assert.Equal((byte)0, Channel(path, 52, 40, 3));
        Assert.True(Channel(path, 84, 40, 0) > 220);

        var rectangle = Paint(dc => dc.DrawRectangle(null, flat,
            new Rect(8, 8, 80, 80)));
        Assert.True(Channel(rectangle, 12, 8, 2) > 220);
        Assert.Equal((byte)0, Channel(rectangle, 24, 8, 3));
        Assert.True(Channel(rectangle, 84, 8, 0) > 220);
        Assert.True(Channel(rectangle, 5, 5, 2) > 100,
            "The first and last dash on the closed contour should share a join.");

        var solidEllipsePen = new Pen(brush, 8);
        var solidEllipse = Paint(dc => dc.DrawEllipse(null, solidEllipsePen,
            new Point(48, 48), 40, 20));
        var dashedEllipse = Paint(dc => dc.DrawEllipse(null, flat,
            new Point(48, 48), 40, 20));
        static int PaintedPixels(byte[] pixels)
        {
            var count = 0;
            for (var i = 3; i < pixels.Length; i += 4)
                if (pixels[i] > 127) count++;
            return count;
        }
        Assert.InRange(PaintedPixels(dashedEllipse), 100,
            PaintedPixels(solidEllipse) * 3 / 4);

        var partial = (PathGeometry)Geometry.Parse("M8,60 L48,60 L88,60");
        ((LineSegment)partial.Figures[0].Segments[1]).IsStroked = false;
        var partialPixels = Paint(dc => dc.DrawGeometry(null, flat, partial));
        Assert.True(Channel(partialPixels, 12, 60, 2) > 220);
        Assert.Equal((byte)0, Channel(partialPixels, 60, 60, 3));
    }

    [Fact]
    public void ZeroLengthOnDashesFollowTheirCapAndOddPatternsRepeat()
    {
        var brush = new SolidColorBrush(Colors.Red);

        byte[] Paint(double[] dashes, PenLineCap cap, double offset = 0)
        {
            var pen = new Pen(brush, 8)
            {
                DashStyle = new DashStyle(dashes, offset),
                DashCap = cap,
            };
            var target = new RenderTargetBitmap(96, 40, 96, 96, PixelFormat.Bgra32);
            var drawing = new SoftwareDrawingContext(target);
            drawing.DrawLine(pen, new Point(8, 20), new Point(88, 20));
            drawing.Close();
            return target.GetPixelBuffer();
        }

        static byte Alpha(byte[] pixels, int x)
            => pixels[(20 * 96 + x) * 4 + 3];

        var roundDots = Paint([0, 2], PenLineCap.Round);
        Assert.True(Alpha(roundDots, 8) > 220);
        Assert.Equal((byte)0, Alpha(roundDots, 16));
        Assert.True(Alpha(roundDots, 24) > 220);

        var flatDots = Paint([0, 2], PenLineCap.Flat);
        Assert.Equal((byte)0, Alpha(flatDots, 8));
        var squareDots = Paint([0, 2], PenLineCap.Square);
        Assert.True(Alpha(squareDots, 8) > 220);

        var oddPattern = Paint([1, 2, 3], PenLineCap.Flat);
        Assert.True(Alpha(oddPattern, 40) > 220);
        Assert.Equal((byte)0, Alpha(oddPattern, 60));
        Assert.True(Alpha(oddPattern, 68) > 220);
        Assert.Equal((byte)0, Alpha(oddPattern, 84));

        var negativeOffset = Paint([1, 2], PenLineCap.Flat, -1);
        Assert.Equal((byte)0, Alpha(negativeOffset, 12));
        Assert.True(Alpha(negativeOffset, 20) > 220);
    }

    [Fact]
    public void TranslucentDashJoinCompositesOnlyOnce()
    {
        var pen = new Pen(new SolidColorBrush(Colors.Red) { Opacity = 0.5 }, 8)
        {
            DashStyle = new DashStyle([20.0, 1.0], 0),
            DashCap = PenLineCap.Flat,
        };
        var target = new RenderTargetBitmap(64, 64, 96, 96, PixelFormat.Bgra32);
        var drawing = new SoftwareDrawingContext(target);
        drawing.DrawGeometry(null, pen, Geometry.Parse("M8,8 L48,8 L48,48"));
        drawing.Close();
        var pixels = target.GetPixelBuffer();

        static byte Alpha(byte[] data, int x, int y)
            => data[(y * 64 + x) * 4 + 3];

        Assert.InRange(Alpha(pixels, 20, 8), (byte)126, (byte)129);
        Assert.InRange(Alpha(pixels, 46, 10), (byte)126, (byte)129);
    }

    [Fact]
    public void OpenPathReturningToItsStartKeepsDashEndCaps()
    {
        var pen = new Pen(new SolidColorBrush(Colors.Red), 8)
        {
            DashStyle = new DashStyle([20.0, 1.0], 0),
            DashCap = PenLineCap.Square,
        };
        var target = new RenderTargetBitmap(64, 32, 96, 96, PixelFormat.Bgra32);
        var drawing = new SoftwareDrawingContext(target);
        drawing.DrawGeometry(null, pen, Geometry.Parse("M8,8 L48,8 L8,8"));
        drawing.Close();

        Assert.True(target.GetPixelBuffer()[(5 * 64 + 5) * 4 + 3] > 220);
    }

    [Fact]
    public void DashedCornerUsesTheSelectedJoinAndMiterLimit()
    {
        byte[] Paint(PenLineJoin join, double miterLimit = 10)
        {
            var pen = new Pen(new SolidColorBrush(Colors.Red), 8)
            {
                DashStyle = new DashStyle([20.0, 1.0], 0),
                DashCap = PenLineCap.Flat,
                LineJoin = join,
                MiterLimit = miterLimit,
            };
            var target = new RenderTargetBitmap(64, 80, 96, 96, PixelFormat.Bgra32);
            var drawing = new SoftwareDrawingContext(target);
            drawing.DrawGeometry(null, pen, Geometry.Parse("M8,24 L48,24 L48,64"));
            drawing.Close();
            return target.GetPixelBuffer();
        }

        static byte Alpha(byte[] pixels, int x, int y)
            => pixels[(y * 64 + x) * 4 + 3];

        var miter = Paint(PenLineJoin.Miter);
        var bevel = Paint(PenLineJoin.Bevel);
        var round = Paint(PenLineJoin.Round);
        var clippedMiter = Paint(PenLineJoin.Miter, 1);
        Assert.True(Alpha(miter, 51, 21) > 220);
        Assert.True(Alpha(bevel, 50, 21) < 80);
        Assert.True(Alpha(round, 50, 21) > 180);
        Assert.True(Alpha(round, 51, 21) < 80);
        Assert.True(Alpha(clippedMiter, 51, 21) < 80);

        using var source = BitmapImage.FromPixels([0, 0, 255, 255], 1, 1);
        var acutePen = new Pen(new ImageBrush(source), 8)
        {
            DashStyle = new DashStyle([20.0, 1.0], 0),
            DashCap = PenLineCap.Flat,
            LineJoin = PenLineJoin.Miter,
            MiterLimit = 10,
        };
        var target = new RenderTargetBitmap(96, 80, 96, 96, PixelFormat.Bgra32);
        var drawing = new SoftwareDrawingContext(target);
        drawing.DrawGeometry(null, acutePen, Geometry.Parse("M8,40 L48,40 L8,16"));
        drawing.Close();
        Assert.True(target.GetPixelBuffer()[(42 * 96 + 56) * 4 + 3] > 220,
            "An acute miter must keep its image texture beyond the centerline box.");
    }

    [Fact]
    public void SolidPathJoinsAndLineCapsKeepTheirPenGeometry()
    {
        byte[] Paint(Action<DrawingContext> draw)
        {
            var target = new RenderTargetBitmap(64, 80, 96, 96, PixelFormat.Bgra32);
            var drawing = new SoftwareDrawingContext(target);
            draw(drawing);
            drawing.Close();
            return target.GetPixelBuffer();
        }

        static byte Alpha(byte[] pixels, int x, int y)
            => pixels[(y * 64 + x) * 4 + 3];

        var path = Geometry.Parse("M8,24 L48,24 L48,64");
        Pen Pen(PenLineJoin join) => new(new SolidColorBrush(Colors.Red), 8)
        {
            LineJoin = join,
        };
        var miter = Paint(dc => dc.DrawGeometry(null, Pen(PenLineJoin.Miter), path));
        var bevel = Paint(dc => dc.DrawGeometry(null, Pen(PenLineJoin.Bevel), path));
        var round = Paint(dc => dc.DrawGeometry(null, Pen(PenLineJoin.Round), path));
        Assert.True(Alpha(miter, 51, 21) > 220);
        Assert.True(Alpha(bevel, 50, 21) < 80);
        Assert.True(Alpha(round, 50, 21) > 180);

        var flat = new Pen(new SolidColorBrush(Colors.Red), 8);
        var square = new Pen(new SolidColorBrush(Colors.Red), 8)
        {
            StartLineCap = PenLineCap.Square,
            EndLineCap = PenLineCap.Square,
        };
        var flatLine = Paint(dc => dc.DrawLine(flat,
            new Point(8, 40), new Point(48, 40)));
        var squareLine = Paint(dc => dc.DrawLine(square,
            new Point(8, 40), new Point(48, 40)));
        Assert.Equal((byte)0, Alpha(flatLine, 5, 40));
        Assert.True(Alpha(squareLine, 5, 40) > 220);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartlyAndFullyUnstrokedPathsKeepTheirVisibleTexture(bool recorded)
    {
        using var source = BitmapImage.FromPixels(
            [0, 0, 255, 255, 255, 0, 0, 255], 2, 1);
        var brush = new ImageBrush(source)
        {
            ScalingMode = BitmapScalingMode.NearestNeighbor,
        };
        var pen = new Pen(brush, 8);

        byte[] Paint(PathGeometry path)
        {
            var target = new RenderTargetBitmap(96, 96, 96, 96, PixelFormat.Bgra32);
            var drawing = new SoftwareDrawingContext(target);
            if (recorded)
            {
                var recorder = new DrawingRecorder();
                recorder.BindWholeFrame();
                recorder.DrawGeometry(null, pen, path);
                DrawingReplayer.Replay(recorder.Commit(), drawing);
            }
            else drawing.DrawGeometry(null, pen, path);
            drawing.Close();
            return target.GetPixelBuffer();
        }

        static byte Channel(byte[] pixels, int x, int y, int channel)
            => pixels[(y * 96 + x) * 4 + channel];

        var open = (PathGeometry)Geometry.Parse("M8,20 L28,20 L48,20 L88,20");
        ((LineSegment)open.Figures[0].Segments[1]).IsStroked = false;
        var openPixels = Paint(open);
        Assert.True(Channel(openPixels, 20, 20, 2) > 220);
        Assert.True(Channel(openPixels, 76, 20, 0) > 220);
        Assert.Equal((byte)0, Channel(openPixels, 40, 20, 3));

        var closed = (PathGeometry)Geometry.Parse("M8,8 L88,8 L88,88 L8,88 Z");
        ((LineSegment)closed.Figures[0].Segments[1]).IsStroked = false;
        var closedPixels = Paint(closed);
        Assert.True(Channel(closedPixels, 20, 8, 2) > 220);
        Assert.True(Channel(closedPixels, 76, 8, 0) > 220);
        Assert.Equal((byte)0, Channel(closedPixels, 88, 48, 3));

        var hidden = (PathGeometry)Geometry.Parse("M8,70 L88,70");
        ((LineSegment)hidden.Figures[0].Segments[0]).IsStroked = false;
        Assert.Equal((byte)0, Channel(Paint(hidden), 20, 70, 3));
    }
}
