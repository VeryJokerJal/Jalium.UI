using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;
public sealed class MenuFlyoutIconRenderingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenGeometryIconIsStrokedAndFitsTheIconColumn(bool frozen)
    {
        var geometry = Geometry.Parse("M-12 -12L12 -12L12 12");
        if (frozen)
            geometry.Freeze();
        var(context, foreground) = Render(geometry);
        var draw = Assert.Single(context.Geometries);
        Assert.Same(geometry, draw.Geometry);
        Assert.Null(draw.Fill);
        Assert.Same(foreground, draw.Pen!.Brush);
        Assert.InRange(draw.Bounds.Left, 12, 39);
        Assert.InRange(draw.Bounds.Right, 12, 40);
        Assert.InRange(draw.Bounds.Top, 0, 24);
        Assert.InRange(draw.Bounds.Bottom, 0, 24);
        Assert.InRange(draw.Pen.Thickness * draw.Scale, 1, 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedGeometryIconUsesTheCurrentForegroundFill(bool frozen)
    {
        var geometry = Geometry.Parse("M0 0L24 0L24 24Z");
        if (frozen)
            geometry.Freeze();
        var(context, foreground) = Render(geometry);
        var draw = Assert.Single(context.Geometries);
        Assert.Same(geometry, draw.Geometry);
        Assert.Same(foreground, draw.Fill);
        Assert.Null(draw.Pen);
    }

    [Fact]
    public void MixedFiguresKeepClosedFillAndOpenStrokeSeparate()
    {
        var geometry = Geometry.Parse("M0 0L12 0L12 12Z M16 0L16 24");
        var(context, foreground) = Render(geometry);
        Assert.Equal(2, context.Geometries.Count);
        var fill = Assert.Single(context.Geometries, draw => draw.Fill != null);
        var stroke = Assert.Single(context.Geometries, draw => draw.Pen != null);
        Assert.Same(foreground, fill.Fill);
        Assert.All(Assert.IsType<PathGeometry>(fill.Geometry).Figures, figure => Assert.True(figure.IsClosed));
        Assert.All(Assert.IsType<PathGeometry>(stroke.Geometry).Figures, figure => Assert.False(figure.IsClosed));
        Assert.Equal(2, Assert.IsType<PathGeometry>(geometry).Figures.Count);
    }

    [Fact]
    public void AVerticalLineIconIsNotDiscardedBecauseItsWidthIsZero()
    {
        var(context, _) = Render(Geometry.Parse("M0 0L0 24"));
        var draw = Assert.Single(context.Geometries);
        Assert.True(draw.Bounds.Height > 0);
        Assert.NotNull(draw.Pen);
    }

    [Fact]
    public void EmptyGeometryDoesNotProduceAnInvalidDrawingTransform()
    {
        var(context, _) = Render(new PathGeometry());
        Assert.Empty(context.Geometries);
        Assert.All(context.Transforms, matrix => Assert.True(double.IsFinite(matrix.M11) && double.IsFinite(matrix.M22)));
    }

    [Fact]
    public void ExistingStringGlyphStillRendersAsText()
    {
        var(context, _) = Render("legacy-icon");
        Assert.Contains("legacy-icon", context.Texts);
    }

    private static (RecordingContext Context, Brush Foreground) Render(object icon)
    {
        var foreground = new SolidColorBrush(Color.FromRgb(30, 60, 90));
        var item = new MenuFlyoutItem
        {
            Icon = icon,
            Foreground = foreground,
            Background = null,
            Width = 200,
            Height = 24
        };
        item.MinHeight = 0;
        MenuFlyoutTemplateTestHelper.Apply(item);
        item.Measure(new Size(200, 24));
        item.Arrange(new Rect(0, 0, 200, 24));
        var context = new RecordingContext();
        item.Render(context);
        return (context, foreground);
    }

    private sealed record GeometryDraw(Geometry Geometry, Brush? Fill, Pen? Pen, Rect Bounds, double Scale);
    private sealed class RecordingContext : DrawingContextAdapter, IOffsetDrawingContext
    {
        public Point Offset { get; set; }

        private Matrix _matrix = Matrix.Identity;
        private readonly Stack<Matrix> _stack = new();
        internal List<GeometryDraw> Geometries { get; } = [];
        internal List<Matrix> Transforms { get; } = [];
        internal List<string> Texts { get; } = [];

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
        {
            var bounds = geometry.Bounds;
            var topLeft = _matrix.Transform(new Point(bounds.Left, bounds.Top));
            var bottomRight = _matrix.Transform(new Point(bounds.Right, bounds.Bottom));
            Geometries.Add(new(geometry, brush, pen, new Rect(new Point(topLeft.X + Offset.X, topLeft.Y + Offset.Y), new Point(bottomRight.X + Offset.X, bottomRight.Y + Offset.Y)), Math.Abs(_matrix.M11)));
        }

        public override void PushTransform(Transform transform)
        {
            _stack.Push(_matrix);
            Transforms.Add(transform.Value);
            _matrix = transform.Value * _matrix;
        }

        public override void PushClip(Geometry clip) => _stack.Push(_matrix);
        public override void PushOpacity(double opacity) => _stack.Push(_matrix);
        public override void Pop() => _matrix = _stack.Pop();
        public override void DrawText(FormattedText text, Point origin) => Texts.Add(text.Text);
        public override void DrawLine(Pen pen, Point start, Point end)
        {
        }

        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
        }

        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double rx, double ry)
        {
        }

        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double rx, double ry)
        {
        }

        public override void DrawImage(ImageSource image, Rect rectangle)
        {
        }

        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius radius)
        {
        }

        public override void Close()
        {
        }
    }
}
