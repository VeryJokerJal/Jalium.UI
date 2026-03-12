using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class RenderCullingTests
{
    [Fact]
    public void Render_ShouldSkipChildOutsideCurrentClipBounds()
    {
        var root = new Canvas { Width = 200, Height = 100 };
        var visible = new CountingElement { Width = 60, Height = 20 };
        var offscreen = new CountingElement { Width = 60, Height = 20 };

        Canvas.SetLeft(visible, 10);
        Canvas.SetTop(visible, 10);
        Canvas.SetLeft(offscreen, 10);
        Canvas.SetTop(offscreen, 140);

        root.Children.Add(visible);
        root.Children.Add(offscreen);
        root.Measure(new Size(200, 200));
        root.Arrange(new Rect(0, 0, 200, 100));

        var dc = new ClipAwareDrawingContext(new Rect(0, 0, 200, 100));
        root.Render(dc);

        Assert.Equal(1, visible.RenderCount);
        Assert.Equal(0, offscreen.RenderCount);
    }

    private sealed class CountingElement : FrameworkElement
    {
        public int RenderCount { get; private set; }

        protected override void OnRender(object drawingContext)
        {
            RenderCount++;
        }
    }

    private sealed class ClipAwareDrawingContext : DrawingContext, IOffsetDrawingContext, IClipBoundsDrawingContext
    {
        public ClipAwareDrawingContext(Rect clipBounds)
        {
            CurrentClipBounds = clipBounds;
        }

        public Point Offset { get; set; }

        public Rect? CurrentClipBounds { get; private set; }

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
            CurrentClipBounds = clipGeometry.Bounds;
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
