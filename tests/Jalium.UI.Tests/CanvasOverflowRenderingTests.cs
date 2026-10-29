using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CanvasOverflowRenderingTests
{
    [Theory]
    [InlineData(900, 750, 1)]
    [InlineData(900, 750, 4)]
    [InlineData(900, 750, .5)]
    [InlineData(-900, -750, 1)]
    public void PannedCanvas_RecordsVisibleChildrenOutsideItsArrangeSlot(double x, double y, double zoom)
    {
        var marker = new CountingElement { Width = 20, Height = 20 };
        var content = new Canvas { Width = 100, Height = 100 };
        Canvas.SetLeft(marker, x); Canvas.SetTop(marker, y);
        content.Children.Add(marker);
        var transforms = new TransformGroup();
        transforms.Children.Add(new ScaleTransform(zoom, zoom));
        transforms.Children.Add(new TranslateTransform(20 - x * zoom, 20 - y * zoom));
        content.RenderTransform = transforms;
        var viewport = new Border { Width = 100, Height = 100, ClipToBounds = true, Child = content };
        Layout(viewport);
        Capture(viewport);
        Assert.Equal(1, marker.RenderCount);

        // An explicit full canvas clip still hides out-of-bounds children.
        content.ClipToBounds = true;
        marker.RenderCount = 0;
        Capture(viewport);
        Assert.Equal(0, marker.RenderCount);
    }

    [Fact]
    public void OverflowCanvas_StillCullsIndividualOffscreenChildren()
    {
        var content = new Canvas { Width = 100, Height = 100 };
        var visible = new CountingElement { Width = 20, Height = 20 };
        var hidden = new CountingElement { Width = 20, Height = 20 };
        Canvas.SetLeft(visible, 320); Canvas.SetLeft(hidden, 800);
        content.Children.Add(visible); content.Children.Add(hidden);
        var viewport = new Canvas { Width = 100, Height = 100, ClipToBounds = true };
        Canvas.SetLeft(content, -300);
        viewport.Children.Add(content);
        Layout(viewport);
        Capture(viewport);
        Assert.Equal(1, visible.RenderCount);
        Assert.Equal(0, hidden.RenderCount);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void RetainedLayerCapture_RequiresBoundsThatContainTheCanvasContent(bool clip, int captures)
    {
        var content = new Canvas { Width = 100, Height = 100, ClipToBounds = clip, RenderTransform = new TranslateTransform(1, 0) };
        content.Children.Add(new CountingElement { Width = 20, Height = 20 });
        var root = new Border { Width = 100, Height = 100, Child = content };
        Layout(root);
        var drawing = new LayerCaptureProbe();
        root.Render(drawing);
        root.Render(drawing);
        Assert.Equal(captures, drawing.CaptureAttempts);
    }

    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(100, 100));
        root.Arrange(new Rect(0, 0, 100, 100));
        root.UpdateLayout();
    }

    private static void Capture(Visual root)
    {
        var host = new MediaRenderCacheHost();
        var recorder = host.CreateFrameRecorder();
        try { root.Render(recorder); }
        finally { host.FinishRecord(recorder); }
    }

    private sealed class CountingElement : FrameworkElement
    {
        public int RenderCount { get; set; }
        protected override void OnRender(DrawingContext drawing) { RenderCount++; drawing.DrawRectangle(Brushes.White, null, new Rect(RenderSize)); }
    }

    private sealed class LayerCaptureProbe : DrawingContextAdapter, IOffsetDrawingContext, ITransformDrawingContext, ILayerCompositingDrawingContext
    {
        public Point Offset { get; set; }
        public bool SupportsRetainedLayers => true;
        public int CaptureAttempts { get; private set; }
        public nint BeginLayerCapture(nint existingLayer, Rect bounds) { CaptureAttempts++; return 0; }
        public void EndLayerCapture(nint layer) { }
        public void CompositeLayer(nint layer, Rect bounds, double opacity, Transform? transform, double originX, double originY) { }
        public void PushTransform(Transform transform, double originX, double originY) { }
        public void PopTransform() { }
        public override void DrawLine(Pen pen, Point start, Point end) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource source, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius radius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry geometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
