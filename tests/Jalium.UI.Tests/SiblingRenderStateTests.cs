using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class SiblingRenderStateTests
{
    [Fact]
    public void Render_ShouldRestoreClipAndOffsetBetweenSiblingElements()
    {
        var panel = new StackPanel();
        var first = new ProbeElement();
        var second = new ProbeElement();

        panel.Children.Add(first);
        panel.Children.Add(second);

        panel.Measure(new Size(200, 200));
        panel.Arrange(new Rect(0, 0, 200, 200));

        var dc = new ProbeDrawingContext();
        panel.Render(dc);

        Assert.Equal(2, dc.ProbeSnapshots.Count);

        var firstSnapshot = dc.ProbeSnapshots[0];
        var secondSnapshot = dc.ProbeSnapshots[1];

        Assert.Equal(Point.Zero, firstSnapshot.OffsetBefore);
        Assert.Null(firstSnapshot.ClipBefore);

        Assert.Equal(new Point(0, first.RenderSize.Height), secondSnapshot.OffsetBefore);
        Assert.Null(secondSnapshot.ClipBefore);
    }

    private sealed class ProbeElement : FrameworkElement
    {
        protected override Size MeasureOverride(Size availableSize) => new(100, 32);

        protected override void OnRender(object drawingContext)
        {
            if (drawingContext is not ProbeDrawingContext dc)
            {
                return;
            }

            dc.ProbeSnapshots.Add(new ProbeSnapshot(dc.Offset, dc.CurrentClipBounds));
            dc.PushClip(new RectangleGeometry(new Rect(1, 1, 98, 30)));
            dc.Pop();
        }
    }

    private sealed class ProbeDrawingContext : DrawingContext, IOffsetDrawingContext, IClipBoundsDrawingContext
    {
        private readonly Stack<Rect?> _clipStack = new();

        public List<ProbeSnapshot> ProbeSnapshots { get; } = new();

        public Point Offset { get; set; }

        public Rect? CurrentClipBounds => _clipStack.Count > 0 ? _clipStack.Peek() : null;

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
            var bounds = clipGeometry.Bounds;
            _clipStack.Push(new Rect(bounds.X + Offset.X, bounds.Y + Offset.Y, bounds.Width, bounds.Height));
        }

        public override void PushOpacity(double opacity)
        {
        }

        public override void Pop()
        {
            if (_clipStack.Count > 0)
            {
                _clipStack.Pop();
            }
        }

        public override void Close()
        {
        }
    }

    private readonly record struct ProbeSnapshot(Point OffsetBefore, Rect? ClipBefore);
}
