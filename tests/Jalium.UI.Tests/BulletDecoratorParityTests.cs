using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class BulletDecoratorParityTests
{
    [Fact]
    public void PublicShape_UsesCanonicalDecoratorContracts()
    {
        Assert.Equal(typeof(Decorator), typeof(BulletDecorator).BaseType);
        Assert.Same(Panel.BackgroundProperty, BulletDecorator.BackgroundProperty);
        Assert.Null(typeof(Decorator).GetField(
            "ChildProperty",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Null(typeof(BulletDecorator).GetField(
            "BulletProperty",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Null(typeof(BulletDecorator).GetField(
            "ChildProperty",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Null(typeof(BulletDecorator).GetField(
            "BulletAlignmentProperty",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Null(typeof(BulletDecorator).GetProperty(
            nameof(Decorator.Child),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        var contentProperty = Assert.IsType<ContentPropertyAttribute>(
            Attribute.GetCustomAttribute(typeof(Decorator), typeof(ContentPropertyAttribute)));
        Assert.Equal(nameof(Decorator.Child), contentProperty.Name);
        Assert.NotNull(typeof(Decorator).GetProperty(nameof(Decorator.Child))?
            .GetCustomAttribute<DefaultValueAttribute>());
    }

    [Fact]
    public void DecoratorChildReplacement_UpdatesBothTrees()
    {
        var decorator = new Decorator();
        var first = new Border();
        var second = new Border();

        decorator.Child = first;
        decorator.Child = second;

        Assert.Null(first.Parent);
        Assert.Null(first.VisualParent);
        Assert.Same(decorator, second.Parent);
        Assert.Same(decorator, second.VisualParent);

        decorator.Child = null;

        Assert.Null(second.Parent);
        Assert.Null(second.VisualParent);
        Assert.Equal(0, decorator.VisualChildrenCount);
    }

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

    [Fact]
    public void ReplacingBullet_DetachesOldElementAndPreservesVisualOrder()
    {
        var first = new Border();
        var second = new Border();
        var child = new Border();
        var decorator = new ProbeBulletDecorator { Bullet = first, Child = child };

        decorator.Bullet = second;

        Assert.Null(first.Parent);
        Assert.Null(first.VisualParent);
        Assert.Same(decorator, second.Parent);
        Assert.Same(decorator, second.VisualParent);
        Assert.Same(second, decorator.GetChild(0));
        Assert.Same(child, decorator.GetChild(1));
    }

    [Fact]
    public void Measure_DoesNotInsertNonWpfBulletMargin()
    {
        var decorator = new BulletDecorator
        {
            Bullet = new Border { Width = 10, Height = 6 },
            Child = new Border { Width = 20, Height = 12 },
        };

        decorator.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(30, decorator.DesiredSize.Width, precision: 3);
        Assert.Equal(12, decorator.DesiredSize.Height, precision: 3);
    }

    private sealed class ProbeBulletDecorator : BulletDecorator
    {
        public void InvokeRender(DrawingContext drawingContext) => OnRender(drawingContext);
        public Visual? GetChild(int index) => GetVisualChild(index);
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
