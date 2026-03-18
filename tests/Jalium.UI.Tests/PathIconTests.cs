using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class PathIconTests
{
    [Fact]
    public void PathIcon_Measure_ShouldDefaultToTwentyPixels_ForLargeGeometry()
    {
        var icon = new PathIcon
        {
            Data = Geometry.Parse("M 0,0 L 100,0 L 100,50 L 0,50 Z")
        };

        icon.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(new Size(20, 20), icon.DesiredSize);
    }

    [Fact]
    public void PathIcon_Render_ShouldFitGeometryUniformly_AndCenterIt()
    {
        var icon = CreateIcon("M 0,0 L 8,0 L 8,4 L 0,4 Z", 20, 20);
        var brush = new SolidColorBrush(Color.FromRgb(12, 34, 56));
        icon.Foreground = brush;

        var drawingContext = new RecordingDrawingContext();
        icon.Render(drawingContext);

        Assert.Same(brush, drawingContext.LastBrush);
        Assert.Same(icon.Data, drawingContext.LastGeometry);

        var transform = Assert.IsType<MatrixTransform>(drawingContext.LastTransform);
        AssertMatrixClose(new Matrix(2.5, 0, 0, 2.5, 0, 5), transform.Matrix);
    }

    [Fact]
    public void PathIcon_RenderTransform_ShouldComposeWithFitTransform()
    {
        var icon = CreateIcon("M 0,0 L 8,0 L 8,4 L 0,4 Z", 20, 20);
        icon.RenderTransform = new RotateTransform { Angle = 90 };

        var drawingContext = new RecordingDrawingContext();
        icon.Render(drawingContext);

        var transform = Assert.IsType<MatrixTransform>(drawingContext.LastTransform);
        AssertMatrixClose(new Matrix(0, 2.5, -2.5, 0, 15, 0), transform.Matrix);
    }

    private static TestPathIcon CreateIcon(string data, double width, double height)
    {
        var icon = new TestPathIcon
        {
            Data = Geometry.Parse(data),
            Width = width,
            Height = height
        };

        icon.Measure(new Size(width, height));
        icon.Arrange(new Rect(0, 0, width, height));
        return icon;
    }

    private sealed class TestPathIcon : PathIcon
    {
        public void Render(DrawingContext drawingContext)
        {
            OnRender(drawingContext);
        }
    }

    private sealed class RecordingDrawingContext : DrawingContext
    {
        public Brush? LastBrush { get; private set; }
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
            LastBrush = brush;
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
