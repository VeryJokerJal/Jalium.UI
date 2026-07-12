using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class BulletDecoratorWpfParityTests
{
    [Fact]
    public void BackgroundPropertyMatchesWpfMetadataAndPaintsBounds()
    {
        var metadata = Assert.IsType<FrameworkPropertyMetadata>(
            BulletDecorator.BackgroundProperty.GetMetadata(typeof(BulletDecorator)));
        Assert.Null(metadata.DefaultValue);
        Assert.True(metadata.AffectsRender);

        var brush = new SolidColorBrush(Color.FromRgb(10, 20, 30));
        var decorator = new ProbeBulletDecorator { Background = brush };
        decorator.Measure(new Size(40, 25));
        decorator.Arrange(new Rect(0, 0, 40, 25));
        var drawingContext = new RecordingDrawingContext();

        decorator.InvokeRender(drawingContext);

        Assert.Same(brush, drawingContext.Brush);
        Assert.Equal(new Rect(0, 0, 40, 25), drawingContext.Bounds);
    }

    [Fact]
    public void BulletAndChildParticipateInVisualAndLogicalTrees()
    {
        var decorator = new BulletDecorator();
        var bullet = new Border();
        var child = new Border();

        decorator.Bullet = bullet;
        decorator.Child = child;

        Assert.Same(decorator, bullet.Parent);
        Assert.Same(decorator, child.Parent);
        Assert.Same(decorator, bullet.VisualParent);
        Assert.Same(decorator, child.VisualParent);
        Assert.Equal(2, decorator.VisualChildrenCount);
    }

    private sealed class ProbeBulletDecorator : BulletDecorator
    {
        public void InvokeRender(DrawingContext drawingContext) => OnRender(drawingContext);
    }

    private sealed class RecordingDrawingContext : DrawingContextAdapter
    {
        public Brush? Brush { get; private set; }
        public Rect Bounds { get; private set; }

        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
            Brush = brush;
            Bounds = rectangle;
        }

        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawText(FormattedText formattedText, Point origin) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
