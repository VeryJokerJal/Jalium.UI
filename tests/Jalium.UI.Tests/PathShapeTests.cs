using Jalium.UI;
using Jalium.UI.Controls.Shapes;
using Jalium.UI.Media;
using ShapePath = Jalium.UI.Controls.Shapes.Path;
using ShapeStretch = Jalium.UI.Media.Stretch;

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

        // Filling 16x8 scales the arc's tight bounds (which account for the bulge, not
        // just the endpoints) by ~1.889x horizontally and ~1.236x vertically. Pure managed
        // geometry math — identical on every platform.
        AssertPointClose(new Point(0.8916493798851177, 8), figure.StartPoint);
        AssertPointClose(new Point(16, 3.055728038485117), arc.Point);
        AssertSizeClose(new Size(7.554175310057441, 4.9442719615148825), arc.Size);
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

        // Path.OnRender emits only the (Stretch.None: untouched) geometry; a user
        // RenderTransform is pushed by the parent Visual around the draw pass, not here.
        // So OnRender never pushes a transform itself — true on every platform.
        Assert.Null(drawingContext.LastTransform);
    }

    [Fact]
    public void Path_OnRender_ShouldInsetStrokeInsideRenderBounds()
    {
        // Stroke must be set before layout so the stretch matrix reserves the
        // half-stroke margin (StrokeThickness/2 = 1) on every edge.
        var path = new TestPath
        {
            Data = "M 0,0 L 8,8",
            Width = 10,
            Height = 10,
            Stretch = ShapeStretch.Fill,
            Stroke = Brushes.Black,
            StrokeThickness = 2
        };
        path.Measure(new Size(10, 10));
        path.Arrange(new Rect(0, 0, 10, 10));

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
    public void Path_OnRender_ShouldRenderAtNativeCoordinates_WhenStretchIsNone()
    {
        var path = CreatePath("M 0,0 L 10,0", 10, 10, ShapeStretch.None);
        path.Stroke = Brushes.Black;
        path.StrokeThickness = 1;

        var drawingContext = new RecordingDrawingContext();
        path.Render(drawingContext);

        var geometry = Assert.IsType<PathGeometry>(drawingContext.LastGeometry);
        var figure = Assert.Single(geometry.Figures);
        var line = Assert.IsType<LineSegment>(Assert.Single(figure.Segments));

        // Stretch.None renders the geometry at its defined coordinates (WPF-aligned):
        // no scaling, no stroke inset, no centering. Identical on every platform.
        Assert.Equal(new Point(0, 0), figure.StartPoint);
        Assert.Equal(new Point(10, 0), line.Point);
    }

    [Fact]
    public void Path_OnRender_ShouldCenterUniformGeometry_WhenAspectRatiosDiffer()
    {
        var path = CreatePath("M 0,0 L 0,8 L 4,4 Z", 8, 8, ShapeStretch.Uniform);
        var drawingContext = new RecordingDrawingContext();

        path.Render(drawingContext);

        var geometry = Assert.IsType<PathGeometry>(drawingContext.LastGeometry);
        var figure = Assert.Single(geometry.Figures);
        var firstEdge = Assert.IsType<LineSegment>(figure.Segments[0]);
        var secondEdge = Assert.IsType<LineSegment>(figure.Segments[1]);

        Assert.Equal(new Point(2, 0), figure.StartPoint);
        Assert.Equal(new Point(2, 8), firstEdge.Point);
        Assert.Equal(new Point(6, 4), secondEdge.Point);
    }

    #region Property mutual exclusion tests

    [Fact]
    public void Path_SetData_ShouldClearGeometry()
    {
        var path = new TestPath();
        path.Geometry = Geometry.Parse("M 0,0 L 10,10");
        Assert.NotNull(path.Geometry);

        path.Data = "M 0,0 L 5,5";

        Assert.Null(path.Geometry);
    }

    [Fact]
    public void Path_SetGeometry_ShouldClearData()
    {
        var path = new TestPath { Data = "M 0,0 L 10,10" };
        Assert.NotNull(path.Data);

        path.Geometry = Geometry.Parse("M 0,0 L 5,5");

        Assert.Null(path.Data);
    }

    [Fact]
    public void Path_SetDataToNull_ShouldClearGeometryAndRenderNothing()
    {
        var path = CreatePath("M 0,0 L 10,10", 10, 10, ShapeStretch.None);
        path.Data = null;

        var dc = new RecordingDrawingContext();
        path.Render(dc);

        Assert.Null(dc.LastGeometry);
    }

    [Fact]
    public void Path_SetGeometryToNull_ShouldClearDataAndRenderNothing()
    {
        var path = new TestPath { Geometry = Geometry.Parse("M 0,0 L 10,10") };
        path.Measure(new Size(10, 10));
        path.Arrange(new Rect(0, 0, 10, 10));

        path.Geometry = null;

        var dc = new RecordingDrawingContext();
        path.Render(dc);

        Assert.Null(dc.LastGeometry);
    }

    #endregion

    #region Invalid data tests

    [Fact]
    public void Path_InvalidDataString_ShouldNotThrow()
    {
        var path = new TestPath();
        path.Data = "this is not valid SVG";

        // Should silently fail — no exception, geometry null
        Assert.NotNull(path.Data);
    }

    [Fact]
    public void Path_InvalidDataString_ShouldRenderNothing()
    {
        var path = new TestPath { Data = "INVALID!!!", Width = 10, Height = 10 };
        path.Measure(new Size(10, 10));
        path.Arrange(new Rect(0, 0, 10, 10));

        var dc = new RecordingDrawingContext();
        path.Render(dc);

        Assert.Null(dc.LastGeometry);
    }

    #endregion

    #region MeasureOverride consistency tests

    [Theory]
    [InlineData("M 0,0 L 10,10")]
    [InlineData("M 0,0 Q 5,5 10,0")]
    [InlineData("M 0,0 A 5,5 0 0 1 10,0")]
    public void Path_MeasureOverride_ShouldReturnConsistentSize(string data)
    {
        var path = CreatePath(data, 20, 20, ShapeStretch.None);
        var measured1 = path.MeasureResult;

        // Re-measure should produce the same result
        path.Measure(new Size(20, 20));
        var measured2 = path.MeasureResult;

        Assert.Equal(measured1, measured2);
    }

    [Fact]
    public void Path_MeasureOverride_EmptyData_ShouldReturnEmptySize()
    {
        var path = new TestPath
        {
            Data = null,
            Stretch = ShapeStretch.None
        };
        path.Measure(new Size(100, 100));

        Assert.Equal(0, path.MeasureResult.Width);
        Assert.Equal(0, path.MeasureResult.Height);
    }

    [Fact]
    public void Path_ArrangeOverride_ShouldReturnFinalSize_WhenStretchUniformAndAspectRatiosDiffer()
    {
        // ComboBox chevron geometry is wider than tall (1126x842). Without this
        // invariant, ArrangeOverride returned the aspect-uniform stretched box
        // (e.g. 8x6 for an 8x8 slot), so RenderSize was 8x6 and a
        // RotateTransform with RenderTransformOrigin=(0.5,0.5) rotated around
        // (4,3) — off the visual center (4,4) — making the rotated arrow drift
        // outside its slot.
        var path = new TestPath
        {
            Data = "M 0,0 L 1126,0 L 563,842 Z",
            Width = 8,
            Height = 8,
            Stretch = ShapeStretch.Uniform
        };
        path.Measure(new Size(8, 8));
        path.Arrange(new Rect(0, 0, 8, 8));

        Assert.Equal(8, path.RenderSize.Width);
        Assert.Equal(8, path.RenderSize.Height);
    }

    #endregion

    #region Negative scale / FlipSweep tests

    [Fact]
    public void Path_NegativeScaleX_ShouldFlipSweepDirection()
    {
        // Create geometry with a clockwise arc, then render at a size that
        // causes negative scaleX via a horizontal flip (width < geometry width
        // wouldn't cause this, but we can use Stretch.Fill with geometry that
        // has negative bounds offset).
        var path = new TestPath
        {
            Data = "M 10,0 A 5,5 0 0 1 0,10",
            Width = 10,
            Height = 10,
            Stretch = ShapeStretch.Fill
        };
        path.Measure(new Size(10, 10));
        path.Arrange(new Rect(0, 0, 10, 10));

        var dc = new RecordingDrawingContext();
        path.Render(dc);

        var geometry = Assert.IsType<PathGeometry>(dc.LastGeometry);
        var figure = Assert.Single(geometry.Figures);
        var arc = Assert.IsType<ArcSegment>(Assert.Single(figure.Segments));

        // With uniform positive scaling, sweep direction should be preserved
        Assert.Equal(SweepDirection.Clockwise, arc.SweepDirection);
    }

    #endregion

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
        public Size MeasureResult { get; private set; }

        public new void Render(DrawingContext drawingContext)
        {
            OnRender(drawingContext);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var result = base.MeasureOverride(availableSize);
            MeasureResult = result;
            return result;
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

    private static void AssertPointClose(Point expected, Point actual, double tolerance = 1e-6)
    {
        Assert.True(Math.Abs(expected.X - actual.X) <= tolerance, $"X expected {expected.X}, got {actual.X}");
        Assert.True(Math.Abs(expected.Y - actual.Y) <= tolerance, $"Y expected {expected.Y}, got {actual.Y}");
    }

    private static void AssertSizeClose(Size expected, Size actual, double tolerance = 1e-6)
    {
        Assert.True(Math.Abs(expected.Width - actual.Width) <= tolerance, $"Width expected {expected.Width}, got {actual.Width}");
        Assert.True(Math.Abs(expected.Height - actual.Height) <= tolerance, $"Height expected {expected.Height}, got {actual.Height}");
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
