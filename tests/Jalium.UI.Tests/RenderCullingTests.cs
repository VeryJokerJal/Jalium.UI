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

    [Fact]
    public void Render_ShouldKeepChildWhoseRenderTransformMovesIntoClipBounds()
    {
        var root = new Canvas { Width = 800, Height = 100 };
        var animated = new CountingElement
        {
            Width = 60,
            Height = 20,
            RenderTransform = new TranslateTransform(500, 0),
        };

        Canvas.SetLeft(animated, 10);
        Canvas.SetTop(animated, 10);
        root.Children.Add(animated);
        root.Measure(new Size(800, 100));
        root.Arrange(new Rect(0, 0, 800, 100));

        // The layout box is still at x=10, outside this damage clip. Its rendered
        // pixels are at x=510, so transform-aware culling must retain the subtree.
        var dc = new ClipAwareDrawingContext(new Rect(500, 0, 100, 100));
        root.Render(dc);

        Assert.Equal(1, animated.RenderCount);
    }

    [Fact]
    public void Render_ShouldSkipChildWhoseRenderTransformMovesOutsideClipBounds()
    {
        var root = new Canvas { Width = 800, Height = 100 };
        var animated = new CountingElement
        {
            Width = 60,
            Height = 20,
            RenderTransform = new TranslateTransform(500, 0),
        };

        Canvas.SetLeft(animated, 10);
        Canvas.SetTop(animated, 10);
        root.Children.Add(animated);
        root.Measure(new Size(800, 100));
        root.Arrange(new Rect(0, 0, 800, 100));

        // The untransformed layout box intersects this clip, but no rendered pixel
        // does. This pins the opposite edge of the transform-aware culling rule.
        var dc = new ClipAwareDrawingContext(new Rect(0, 0, 100, 100));
        root.Render(dc);

        Assert.Equal(0, animated.RenderCount);
    }

    private sealed class CountingElement : FrameworkElement
    {
        public int RenderCount { get; private set; }

        protected override void OnRender(DrawingContext drawingContext)
        {
            RenderCount++;
        }
    }

    private sealed class ClipAwareDrawingContext : DrawingContextAdapter, IOffsetDrawingContext, IClipBoundsDrawingContext
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
