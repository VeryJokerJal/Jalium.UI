using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Tests;

public class FocusVisualAncestorClipTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AncestorShapeIsRetainedAndTargetOwnClipIsIgnored(int shape)
    {
        Geometry geometry = shape switch
        {
            0 => new RectangleGeometry(new Rect(0, 0, 100, 100), 30, 30),
            1 => new EllipseGeometry(new Point(50, 50), 50, 50),
            _ => Geometry.Parse("M0,0 L100,0 100,100 0,100 Z M30,30 L70,30 70,70 30,70 Z")
        };
        var target = new Button { Width = 40, Height = 40, ClipToBounds = true };
        var parent = new Canvas { Width = 100, Height = 100, Clip = geometry };
        Canvas.SetLeft(target, 20);
        Canvas.SetTop(target, 20);
        parent.Children.Add(target);
        var root = new Canvas();
        root.Children.Add(parent);
        var layer = new AdornerLayer();
        root.Children.Add(layer);
        var ring = new FocusVisualAdorner(target, new Style(typeof(Control)));
        layer.Add(ring);
        root.Measure(new Size(200, 200));
        root.Arrange(new Rect(0, 0, 200, 200));
        var clip = Assert.IsType<LayoutClipStack>(ring.GetLayoutClip());
        Assert.Same(geometry, Assert.Single(clip.Clips).Geometry);
        Assert.False(clip.FillContains(parent.TranslatePoint(new Point(1, 1), ring)) && shape < 2);
        Assert.True(clip.FillContains(parent.TranslatePoint(new Point(20, 50), ring)));
        if (shape == 2) Assert.False(clip.FillContains(parent.TranslatePoint(new Point(50, 50), ring)));
        // This point is outside the adorned button but inside the ancestor mask.
        Assert.True(clip.FillContains(parent.TranslatePoint(new Point(19, 50), ring)));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void GeometryTransformAndRenderMappingAreAppliedOnce(double deviceScale)
    {
        var shape = new EllipseGeometry(new Point(50, 50), 40, 20)
        {
            Transform = new MatrixTransform(new Matrix(0, 2, -3, 0, 180, 10))
        };
        var mapping = new Matrix(deviceScale, 0, 0, deviceScale, 23, -17);
        var stack = new LayoutClipStack();
        stack.Clips.Add((shape, mapping));
        var matrix = Matrix.Multiply(shape.Transform.Value, mapping);
        Assert.True(stack.FillContains(matrix.Transform(new Point(50, 50))));
        Assert.False(stack.FillContains(matrix.Transform(new Point(89, 69))));
        Assert.True(stack.Bounds.Contains(matrix.Transform(new Point(50, 50))));
        Assert.Same(shape.Transform, stack.Clips[0].Geometry.Transform);
    }

    [Fact]
    public void ConcavePathAndHoleIntersectWithoutBooleanPolygonClipping()
    {
        var stack = new LayoutClipStack();
        stack.Clips.Add((Geometry.Parse("M0,0 L100,0 100,30 30,30 30,100 0,100 Z"), Matrix.Identity));
        stack.Clips.Add((Geometry.Parse("M0,0 L100,0 100,100 0,100 Z M10,10 L20,10 20,20 10,20 Z"), Matrix.Identity));
        Assert.True(stack.FillContains(new Point(25, 25)));
        Assert.False(stack.FillContains(new Point(15, 15)));
        Assert.False(stack.FillContains(new Point(50, 50)));
    }

    [Fact]
    public void EmptyAndSingularMasksHideEverything()
    {
        var stack = new LayoutClipStack();
        stack.Clips.Add((Geometry.Empty, Matrix.Identity));
        Assert.False(stack.FillContains(new Point(0, 0)));
        Assert.True(stack.Bounds.IsEmpty);
        stack.Clips.Clear();
        stack.Clips.Add((new RectangleGeometry(new Rect(0, 0, 10, 10)), new Matrix(0, 0, 0, 0, 0, 0)));
        Assert.False(stack.FillContains(new Point(0, 0)));
    }

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(192)]
    public void SoftwarePixelsRespectRoundedMaskAndPathHoleAtDifferentDpi(double dpi)
    {
        var scale = dpi / 96;
        var size = (int)(100 * scale);
        var bitmap = new RenderTargetBitmap(size, size, dpi, dpi, PixelFormat.Bgra32);
        var context = new SoftwareDrawingContext(bitmap);
        var stack = new LayoutClipStack();
        stack.Clips.Add((new RectangleGeometry(new Rect(0, 0, 100, 100), 30, 30), Matrix.Identity));
        stack.Clips.Add((Geometry.Parse("M0,0 L100,0 100,100 0,100 Z M30,30 L70,30 70,70 30,70 Z"), Matrix.Identity));
        var depth = LayoutClipStack.Push(context, context, stack);
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(255, 0, 0)), null, new Rect(0, 0, 100, 100));
        while (depth-- > 0) context.Pop();
        context.Close();
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        byte Alpha(int x, int y) => pixels[((int)(y * scale) * size + (int)(x * scale)) * 4 + 3];
        Assert.Equal(0, Alpha(1, 1));
        Assert.Equal(0, Alpha(50, 50));
        Assert.True(Alpha(20, 50) > 200);
    }

    [Fact]
    public void MutableClipInvalidatesCachedShape()
    {
        var geometry = new RectangleGeometry(new Rect(0, 0, 10, 10));
        var stack = new LayoutClipStack();
        stack.Clips.Add((geometry, Matrix.Identity));
        Assert.True(stack.FillContains(new Point(5, 5)));
        geometry.Rect = new Rect(20, 20, 10, 10);
        Assert.False(stack.FillContains(new Point(5, 5)));
        Assert.True(stack.FillContains(new Point(25, 25)));
    }
    [Fact]
    public void RenderPushesIndependentMasksRestoresCoordinatesAndReusesConversions()
    {
        var stack = new LayoutClipStack();
        stack.Clips.Add((new EllipseGeometry(new Point(50, 50), 40, 20),
            new Matrix(0, 2, -3, 0, 180, 10)));
        stack.Clips.Add((Geometry.Parse("M0,0 L100,0 100,100 0,100 Z M30,30 L70,30 70,70 30,70 Z"), Matrix.Identity));
        var context = new ClipRecorder();
        var depth = LayoutClipStack.Push(context, context, stack);
        Assert.Equal(6, depth);
        Assert.Equal(2, context.Masks.Count);
        var converted = context.Masks.ToArray();
        Assert.Equal(Matrix.Identity, context.Current);
        while (depth-- > 0) context.Pop();
        Assert.Empty(context.States);
        depth = LayoutClipStack.Push(context, context, stack);
        Assert.Same(converted[0], context.Masks[2]);
        Assert.Same(converted[1], context.Masks[3]);
        while (depth-- > 0) context.Pop();
        Assert.Empty(context.States);
    }


    private sealed class ClipRecorder : DrawingContextAdapter, IClipDrawingContext
    {
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawText(FormattedText formattedText, Point origin) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        internal Matrix Current = Matrix.Identity;
        internal readonly Stack<Matrix> States = new();
        internal readonly List<Geometry> Masks = new();
        public override void PushTransform(Transform transform)
        {
            States.Push(Current);
            Current = Matrix.Multiply(transform.Value, Current);
        }
        public override void PushClip(Geometry clipGeometry) { States.Push(Current); Masks.Add(clipGeometry); }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { Current = States.Pop(); }
        public override void Close() { }
    }

}
