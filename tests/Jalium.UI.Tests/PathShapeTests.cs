using Jalium.UI;
using Jalium.UI.Controls.Shapes;
using Jalium.UI.Media;
using ShapePath = Jalium.UI.Controls.Shapes.Path;
using ShapeStretch = Jalium.UI.Controls.Shapes.Stretch;

namespace Jalium.UI.Tests;

public class PathShapeTests
{
    [Fact]
    public void Path_Data_ShouldUseSharedPathMarkupParser_ForArcCommands()
    {
        var path = CreatePath("M 0,4 A 4,4 0 0 1 8,0", 8, 8, ShapeStretch.None);
        var drawingContext = new RecordingDrawingContext();

        path.Render(drawingContext);

        var geometry = Assert.IsType<PathGeometry>(drawingContext.LastGeometry);
        var figure = Assert.Single(geometry.Figures);
        var arc = Assert.IsType<ArcSegment>(Assert.Single(figure.Segments));

        Assert.Equal(new Point(0, 4), figure.StartPoint);
        Assert.Equal(new Point(8, 0), arc.Point);
        Assert.Equal(new Size(4, 4), arc.Size);
        Assert.Null(drawingContext.LastTransform);
    }

    [Fact]
    public void Path_OnRender_ShouldScaleArcSegments_WhenStretched()
    {
        var path = CreatePath("M 0,4 A 4,4 0 0 1 8,0", 16, 8, ShapeStretch.Fill);
        var drawingContext = new RecordingDrawingContext();

        path.Render(drawingContext);

        var geometry = Assert.IsType<PathGeometry>(drawingContext.LastGeometry);
        var figure = Assert.Single(geometry.Figures);
        var arc = Assert.IsType<ArcSegment>(Assert.Single(figure.Segments));

        Assert.Equal(new Point(0, 8), figure.StartPoint);
        Assert.Equal(new Point(16, 0), arc.Point);
        Assert.Equal(new Size(8, 8), arc.Size);
    }

    [Fact]
    public void Path_RenderTransform_ShouldPreserveArcSegments()
    {
        var path = CreatePath("M 0,4 A 4,4 0 0 1 8,0", 8, 8, ShapeStretch.None);
        path.RenderTransform = new RotateTransform { Angle = 90 };

        var drawingContext = new RecordingDrawingContext();
        path.Render(drawingContext);

        var geometry = Assert.IsType<PathGeometry>(drawingContext.LastGeometry);
        var figure = Assert.Single(geometry.Figures);
        Assert.IsType<ArcSegment>(Assert.Single(figure.Segments));

        var transform = Assert.IsType<MatrixTransform>(drawingContext.LastTransform);
        AssertMatrixClose(new Matrix(0, 1, -1, 0, 8, 0), transform.Matrix);
    }

    [Fact]
    public void Path_OnRender_ShouldInsetStrokeInsideRenderBounds()
    {
        var path = CreatePath("M 0,0 L 8,8", 10, 10, ShapeStretch.Fill);
        path.Stroke = Brushes.Black;
        path.StrokeThickness = 2;

        var drawingContext = new RecordingDrawingContext();
        path.Render(drawingContext);

        var geometry = Assert.IsType<PathGeometry>(drawingContext.LastGeometry);
        var figure = Assert.Single(geometry.Figures);
        var line = Assert.IsType<LineSegment>(Assert.Single(figure.Segments));

        Assert.Equal(new Point(1, 1), figure.StartPoint);
        Assert.Equal(new Point(9, 9), line.Point);
        Assert.Null(drawingContext.LastTransform);
    }

    [Fact]
    public void Path_OnRender_ShouldCenterHorizontalStrokeOnlyGlyph_WhenStretchIsNone()
    {
        var path = CreatePath("M 0,0 L 10,0", 10, 10, ShapeStretch.None);
        path.Stroke = Brushes.Black;
        path.StrokeThickness = 1;

        var drawingContext = new RecordingDrawingContext();
        path.Render(drawingContext);

        var geometry = Assert.IsType<PathGeometry>(drawingContext.LastGeometry);
        var figure = Assert.Single(geometry.Figures);
        var line = Assert.IsType<LineSegment>(Assert.Single(figure.Segments));

        Assert.Equal(new Point(0.5, 5), figure.StartPoint);
        Assert.Equal(new Point(9.5, 5), line.Point);
    }

    private static TestPath CreatePath(string data, double width, double height, ShapeStretch stretch)
    {
        var path = new TestPath
        {
            Data = data,
            Width = width,
            Height = height,
            Stretch = stretch
        };

        path.Measure(new Size(width, height));
        path.Arrange(new Rect(0, 0, width, height));

        return path;
    }

    private sealed class TestPath : ShapePath
    {
        public void Render(DrawingContext drawingContext)
        {
            OnRender(drawingContext);
        }
    }

    private sealed class RecordingDrawingContext : DrawingContext
    {
        public Geometry? LastGeometry { get; private set; }
        public Transform? LastTransform { get; private set; }

        public override void DrawLine(Pen pen, Point point0, Point point1)
        {
        }

        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
        }

        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
        {
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
            LastTransform = transform;
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

    private static void AssertMatrixClose(Matrix expected, Matrix actual, double tolerance = 1e-9)
    {
        Assert.True(Math.Abs(expected.M11 - actual.M11) <= tolerance, $"M11 expected {expected.M11}, got {actual.M11}");
        Assert.True(Math.Abs(expected.M12 - actual.M12) <= tolerance, $"M12 expected {expected.M12}, got {actual.M12}");
        Assert.True(Math.Abs(expected.M21 - actual.M21) <= tolerance, $"M21 expected {expected.M21}, got {actual.M21}");
        Assert.True(Math.Abs(expected.M22 - actual.M22) <= tolerance, $"M22 expected {expected.M22}, got {actual.M22}");
        Assert.True(Math.Abs(expected.OffsetX - actual.OffsetX) <= tolerance, $"OffsetX expected {expected.OffsetX}, got {actual.OffsetX}");
        Assert.True(Math.Abs(expected.OffsetY - actual.OffsetY) <= tolerance, $"OffsetY expected {expected.OffsetY}, got {actual.OffsetY}");
    }
}
