using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class BorderClipTests
{
    [Fact]
    public void Border_ClipToBounds_WithRoundedCorners_UsesInnerRenderBounds()
    {
        var border = new TestBorder
        {
            ClipToBounds = true,
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(8)
        };

        border.Measure(new Size(120, 40));
        border.Arrange(new Rect(0, 0, 120, 40));

        var clip = Assert.IsType<RectangleGeometry>(border.InvokeGetLayoutClip());

        Assert.Equal(new Rect(3, 3, 114, 34), clip.Rect);
        Assert.Equal(5, clip.RadiusX);
        Assert.Equal(5, clip.RadiusY);
    }

    [Fact]
    public void Border_ClipToBounds_WithSuperEllipse_UsesLocalCornerRadiusAndExponent()
    {
        var border = new TestBorder
        {
            ClipToBounds = true,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(12),
            Shape = BorderShape.SuperEllipse,
            SuperEllipseN = 4,
        };

        border.Measure(new Size(120, 40));
        border.Arrange(new Rect(0, 0, 120, 40));

        var clip = Assert.IsType<StreamGeometry>(border.InvokeGetLayoutClip());
        var clipPath = Assert.IsType<PathGeometry>(clip.GetPathGeometry());

        Assert.True(clipPath.FillContains(new Point(30, 3)));
        Assert.False(clipPath.FillContains(new Point(2.5, 2.5)));
        Assert.True(clipPath.FillContains(new Point(60, 20)));
        Assert.True(clipPath.FillContains(new Point(100, 34)));

        var zeroRadius = new TestBorder
        {
            ClipToBounds = true,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(0),
            Shape = BorderShape.SuperEllipse,
            SuperEllipseN = 4,
        };
        zeroRadius.Measure(new Size(120, 40));
        zeroRadius.Arrange(new Rect(0, 0, 120, 40));

        var zeroRadiusClip = Assert.IsType<StreamGeometry>(
            zeroRadius.InvokeGetLayoutClip());
        var zeroRadiusPath = Assert.IsType<PathGeometry>(
            zeroRadiusClip.GetPathGeometry());
        Assert.True(zeroRadiusPath.FillContains(new Point(30, 3)));
        Assert.True(zeroRadiusPath.FillContains(new Point(2.5, 2.5)));
        Assert.True(zeroRadiusPath.FillContains(new Point(100, 34)));

        var ellipse = new TestBorder
        {
            ClipToBounds = true,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(12),
            Shape = BorderShape.SuperEllipse,
            SuperEllipseN = 2,
        };
        ellipse.Measure(new Size(120, 40));
        ellipse.Arrange(new Rect(0, 0, 120, 40));

        var ellipseClip = Assert.IsType<StreamGeometry>(
            ellipse.InvokeGetLayoutClip());
        var ellipsePath = Assert.IsType<PathGeometry>(
            ellipseClip.GetPathGeometry());
        Assert.True(clipPath.FillContains(new Point(4.5, 4.5)));
        Assert.False(ellipsePath.FillContains(new Point(4.5, 4.5)));
    }

    [Fact]
    public void Border_Child_VisualBounds_OffsetByBorderThickness()
    {
        var child = new FrameworkElement();
        var border = new Border
        {
            BorderThickness = new Thickness(4),
            Child = child,
        };

        border.Measure(new Size(100, 100));
        border.Arrange(new Rect(0, 0, 100, 100));

        Assert.Equal(new Rect(4, 4, 92, 92), child.VisualBounds);
    }

    [Fact]
    public void Border_Child_VisualBounds_OffsetByPaddingAndBorderThickness()
    {
        var child = new FrameworkElement();
        var border = new Border
        {
            BorderThickness = new Thickness(2),
            Padding = new Thickness(6),
            Child = child,
        };

        border.Measure(new Size(100, 100));
        border.Arrange(new Rect(0, 0, 100, 100));

        Assert.Equal(new Rect(8, 8, 84, 84), child.VisualBounds);
    }

    [Fact]
    public void Border_Child_VisualBounds_RespectsAsymmetricBorderThickness()
    {
        var child = new FrameworkElement();
        var border = new Border
        {
            BorderThickness = new Thickness(left: 8, top: 0, right: 0, bottom: 4),
            Child = child,
        };

        border.Measure(new Size(100, 100));
        border.Arrange(new Rect(0, 0, 100, 100));

        Assert.Equal(new Rect(8, 0, 92, 96), child.VisualBounds);
    }

    [Fact]
    public void Border_Child_VisualBounds_StaysConsistentWithFractionalBorderThickness()
    {
        // Pixel snapping is disabled, so a fractional BorderThickness like 1.5
        // passes straight through to the child's _visualBounds instead of being
        // rounded onto the physical-pixel grid. The child rect and the rect
        // OnRender paints the background/stroke into are computed from the same
        // raw BorderThickness, so they still agree — just at the fractional
        // position. The renderer handles sub-pixel placement / AA at draw time.
        var child = new FrameworkElement();
        var border = new Border
        {
            BorderThickness = new Thickness(1.5),
            Child = child,
        };

        border.Measure(new Size(100, 100));
        border.Arrange(new Rect(0, 0, 100, 100));

        // BT=1.5 passes through unchanged (no rounding): inset 1.5 on every side.
        Assert.Equal(new Rect(1.5, 1.5, 97, 97), child.VisualBounds);
    }

    [Fact]
    public void Border_SuperEllipse_AsymmetricThickness_UsesCenterlineBackgroundUnderlap()
    {
        var background = new SolidColorBrush
        {
            Color = Color.FromArgb(255, 30, 60, 90)
        };
        var borderBrush = new SolidColorBrush
        {
            Color = Color.FromArgb(255, 200, 220, 240)
        };
        var border = new TestBorder
        {
            ClipToBounds = true,
            Background = background,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(left: 8, top: 2, right: 4, bottom: 6),
            CornerRadius = new CornerRadius(20, 18, 16, 14),
            Shape = BorderShape.SuperEllipse,
            SuperEllipseN = 4,
        };

        border.Measure(new Size(100, 60));
        border.Arrange(new Rect(0, 0, 100, 60));

        var clip = Assert.IsType<StreamGeometry>(border.InvokeGetLayoutClip());
        var clipPath = Assert.IsType<PathGeometry>(clip.GetPathGeometry());

        var drawingContext = new GeometryCaptureDrawingContext();
        border.Render(drawingContext);

        Assert.Equal(new Rect(4, 1, 94, 56), drawingContext.BackgroundRect);
        var backgroundRadius = Assert.IsType<CornerRadius>(
            drawingContext.BackgroundCornerRadius);
        Assert.Equal(16, backgroundRadius.TopLeft);
        Assert.Equal(16, backgroundRadius.TopRight);
        Assert.Equal(13, backgroundRadius.BottomRight);
        Assert.Equal(10, backgroundRadius.BottomLeft);

        var ring = Assert.IsType<PathGeometry>(drawingContext.StrokeGeometry);
        Assert.Equal(FillRule.EvenOdd, ring.FillRule);

        Assert.True(ring.FillContains(new Point(1, 30)));
        Assert.False(ring.FillContains(new Point(9, 30)));
        Assert.True(ring.FillContains(new Point(50, 1)));
        Assert.False(ring.FillContains(new Point(50, 3)));
        Assert.True(ring.FillContains(new Point(98, 30)));
        Assert.False(ring.FillContains(new Point(95, 30)));
        Assert.True(ring.FillContains(new Point(50, 57)));
        Assert.False(ring.FillContains(new Point(50, 53)));
        Assert.False(ring.FillContains(new Point(1, 1)));

        Assert.True(clipPath.FillContains(new Point(9, 30)));
        Assert.False(clipPath.FillContains(new Point(7, 30)));

        var secondDrawingContext = new GeometryCaptureDrawingContext();
        border.Render(secondDrawingContext);
        Assert.Same(ring, secondDrawingContext.StrokeGeometry);
        Assert.True(ring.IsFrozen);

        border.CornerRadius = new CornerRadius(18, 16, 14, 12);
        var changedDrawingContext = new GeometryCaptureDrawingContext();
        border.Render(changedDrawingContext);
        Assert.NotSame(ring, changedDrawingContext.StrokeGeometry);
    }

    [Fact]
    public void Border_UniformRoundedBackground_ReachesStrokeCenterLine()
    {
        var background = new SolidColorBrush
        {
            Color = Color.FromArgb(255, 30, 60, 90)
        };
        var borderBrush = new SolidColorBrush
        {
            Color = Color.FromArgb(255, 200, 220, 240)
        };
        var border = new Border
        {
            Background = background,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };

        border.Measure(new Size(72, 36));
        border.Arrange(new Rect(0, 0, 72, 36));

        var drawingContext = new GeometryCaptureDrawingContext();
        border.Render(drawingContext);

        Assert.Equal(new Rect(0.5, 0.5, 71, 35), drawingContext.BackgroundRect);
        Assert.Equal(drawingContext.BackgroundRect, drawingContext.StrokeRect);
        Assert.Equal(
            new CornerRadius(7.5),
            drawingContext.BackgroundCornerRadius);
        Assert.Equal(
            drawingContext.BackgroundCornerRadius,
            drawingContext.StrokeCornerRadius);
        Assert.Equal(1, drawingContext.StrokeThickness);
    }

    [Fact]
    public void Border_ZeroRadiusAsymmetricStroke_UsesNonOverlappingRectangles()
    {
        var borderBrush = new SolidColorBrush
        {
            Color = Color.FromArgb(255, 200, 220, 240)
        };
        var border = new Border
        {
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(left: 2, top: 3, right: 4, bottom: 5),
            CornerRadius = new CornerRadius(0),
        };

        border.Measure(new Size(100, 60));
        border.Arrange(new Rect(0, 0, 100, 60));

        var drawingContext = new GeometryCaptureDrawingContext();
        border.Render(drawingContext);

        Assert.Equal(
            [
                new Rect(0, 0, 100, 3),
                new Rect(0, 55, 100, 5),
                new Rect(0, 3, 2, 52),
                new Rect(96, 3, 4, 52),
            ],
            drawingContext.StrokeRectangles);
        Assert.Null(drawingContext.StrokeGeometry);
    }

    private sealed class GeometryCaptureDrawingContext : DrawingContextAdapter
    {
        public Rect? BackgroundRect { get; private set; }

        public CornerRadius? BackgroundCornerRadius { get; private set; }

        public Rect? StrokeRect { get; private set; }

        public CornerRadius? StrokeCornerRadius { get; private set; }

        public double? StrokeThickness { get; private set; }

        public Geometry? StrokeGeometry { get; private set; }

        public List<Rect> StrokeRectangles { get; } = [];

        public override void DrawLine(Pen pen, Point p0, Point p1) { }

        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
            if (brush != null && pen == null)
            {
                StrokeRectangles.Add(rectangle);
            }
        }

        public override void DrawRoundedRectangle(
            Brush? brush,
            Pen? pen,
            Rect rectangle,
            double radiusX,
            double radiusY) { }

        public override void DrawEllipse(
            Brush? brush,
            Pen? pen,
            Point center,
            double radiusX,
            double radiusY) { }

        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }

        public override void DrawBackdropEffect(
            Rect rectangle,
            IBackdropEffect effect,
            CornerRadius cornerRadius) { }

        public override void PushTransform(Transform transform) { }

        public override void PushClip(Geometry clipGeometry) { }

        public override void PushOpacity(double opacity) { }

        public override void Pop() { }

        public override void Close() { }
        public override void DrawRoundedRectangle(
            Brush? brush,
            Pen? pen,
            Rect rectangle,
            CornerRadius cornerRadius)
        {
            if (brush != null && pen == null)
            {
                BackgroundRect = rectangle;
                BackgroundCornerRadius = cornerRadius;
            }
            else if (brush == null && pen != null)
            {
                StrokeRect = rectangle;
                StrokeCornerRadius = cornerRadius;
                StrokeThickness = pen.Thickness;
            }
        }

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
        {
            if (brush != null && pen == null)
            {
                StrokeGeometry = geometry;
            }
        }
    }

    private sealed class TestBorder : Border
    {
        public object? InvokeGetLayoutClip() => GetLayoutClip();
    }
}
