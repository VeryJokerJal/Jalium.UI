using Jalium.UI;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class CornerRadiusNormalizationTests
{
    [Fact]
    public void CornerRadius_Normalize_UniformOversizedRadius_ProducesCapsuleRadius()
    {
        var normalized = new CornerRadius(999).Normalize(120, 24);

        Assert.Equal(new CornerRadius(12), normalized);
    }

    [Fact]
    public void CornerRadius_Normalize_ScalesAllCorners_WhenEdgesOverflow()
    {
        var normalized = new CornerRadius(80, 20, 20, 80).Normalize(100, 40);

        Assert.Equal(new CornerRadius(20, 5, 5, 20), normalized);
    }

    [Fact]
    public void DrawRoundedRectangle_UniformOversizedCornerRadius_UsesNormalizedNativeRadius()
    {
        var drawingContext = new RecordingDrawingContext();

        drawingContext.DrawRoundedRectangle(null, null, new Rect(0, 0, 120, 24), new CornerRadius(999));

        Assert.Equal(12, drawingContext.LastRadiusX);
        Assert.Equal(12, drawingContext.LastRadiusY);
        Assert.Null(drawingContext.LastGeometry);
    }

    [Fact]
    public void DrawRoundedRectangle_AsymmetricOversizedCorners_UsesNormalizedGeometry()
    {
        var drawingContext = new RecordingDrawingContext();

        drawingContext.DrawRoundedRectangle(null, null, new Rect(0, 0, 100, 40), new CornerRadius(80, 20, 20, 80));

        var geometry = Assert.IsType<PathGeometry>(drawingContext.LastGeometry);
        var figure = Assert.Single(geometry.Figures);

        Assert.Equal(new Point(20, 0), figure.StartPoint);
        Assert.Collection(
            figure.Segments,
            segment => Assert.Equal(new Point(95, 0), Assert.IsType<LineSegment>(segment).Point),
            segment =>
            {
                var arc = Assert.IsType<ArcSegment>(segment);
                Assert.Equal(new Size(5, 5), arc.Size);
                Assert.Equal(new Point(100, 5), arc.Point);
            },
            segment => Assert.Equal(new Point(100, 35), Assert.IsType<LineSegment>(segment).Point),
            segment =>
            {
                var arc = Assert.IsType<ArcSegment>(segment);
                Assert.Equal(new Size(5, 5), arc.Size);
                Assert.Equal(new Point(95, 40), arc.Point);
            },
            segment => Assert.Equal(new Point(20, 40), Assert.IsType<LineSegment>(segment).Point),
            segment =>
            {
                var arc = Assert.IsType<ArcSegment>(segment);
                Assert.Equal(new Size(20, 20), arc.Size);
                Assert.Equal(new Point(0, 20), arc.Point);
            },
            segment => Assert.Equal(new Point(0, 20), Assert.IsType<LineSegment>(segment).Point),
            segment =>
            {
                var arc = Assert.IsType<ArcSegment>(segment);
                Assert.Equal(new Size(20, 20), arc.Size);
                Assert.Equal(new Point(20, 0), arc.Point);
            });
    }

    private sealed class RecordingDrawingContext : DrawingContext
    {
        public double? LastRadiusX { get; private set; }
        public double? LastRadiusY { get; private set; }
        public Geometry? LastGeometry { get; private set; }

        public override void DrawLine(Pen pen, Point point0, Point point1)
        {
        }

        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
        }

        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
        {
            LastRadiusX = radiusX;
            LastRadiusY = radiusY;
        }

        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
        {
        }

        public override void DrawText(FormattedText formattedText, Point origin)
        {
        }

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
        {
            LastGeometry = geometry;
        }

        public override void DrawImage(ImageSource imageSource, Rect rectangle)
        {
        }

        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius)
        {
        }

        public override void PushTransform(Transform transform)
        {
        }

        public override void PushClip(Geometry clipGeometry)
        {
        }

        public override void PushOpacity(double opacity)
        {
        }

        public override void Pop()
        {
        }

        public override void Close()
        {
        }
    }
}
