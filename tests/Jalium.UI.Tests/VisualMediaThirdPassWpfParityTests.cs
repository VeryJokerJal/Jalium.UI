using System.Collections;
using System.Reflection;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class VisualMediaThirdPassWpfParityTests
{
    [Fact]
    public void Visual_ExposesWpfAncestryDpiAndScreenContracts()
    {
        var parent = new ProbeVisual();
        var child = new ProbeVisual();
        parent.Attach(child);

        Assert.True(parent.IsAncestorOf(child));
        Assert.True(child.IsDescendantOf(parent));
        Assert.Same(parent, child.FindCommonVisualAncestor(parent));
        Assert.Equal(new Point(3, 4), child.PointToScreen(new Point(3, 4)));
        Assert.Equal(new Point(3, 4), child.PointFromScreen(new Point(3, 4)));

        VisualTreeHelper.SetRootDpi(parent, new DpiScale(1.5, 1.5));
        Assert.Equal(new DpiScale(1.5, 1.5), VisualTreeHelper.GetDpi(parent));
        Assert.Equal(1, parent.DpiChangeCount);
    }

    [Fact]
    public void VisualTreeHelper_ReadsCompositionStateFromVisualAndUiElement()
    {
        var visual = new ProbeVisual();
        var clip = new RectangleGeometry(new Rect(1, 2, 3, 4));
        var mask = new SolidColorBrush(Color.FromArgb(128, 10, 20, 30));
        var transform = new TranslateTransform(7, 9);
        var xGuidelines = new DoubleCollection([1d, 2d]);
        var yGuidelines = new DoubleCollection([3d, 4d]);
        visual.SetCompositionState(clip, mask, transform, xGuidelines, yGuidelines);

        Assert.Same(clip, VisualTreeHelper.GetClip(visual));
        Assert.Same(mask, VisualTreeHelper.GetOpacityMask(visual));
        Assert.Same(transform, VisualTreeHelper.GetTransform(visual));
        Assert.Equal(new Vector(5, 6), VisualTreeHelper.GetOffset(visual));
        Assert.Equal(0.4, VisualTreeHelper.GetOpacity(visual));
        Assert.Same(xGuidelines, VisualTreeHelper.GetXSnappingGuidelines(visual));
        Assert.Same(yGuidelines, VisualTreeHelper.GetYSnappingGuidelines(visual));
    }

    [Fact]
    public void ContainerAndDrawingVisuals_ExposeWpfCollectionAndDrawingShape()
    {
        var container = new ContainerVisual
        {
            Offset = new Vector(2, 3),
            Opacity = 0.75,
            XSnappingGuidelines = new DoubleCollection([1d]),
            YSnappingGuidelines = new DoubleCollection([2d]),
        };
        var drawing = new DrawingVisual();
        container.Children.Capacity = 4;
        container.Children.Add(drawing);

        var copied = new Visual[1];
        container.Children.CopyTo((Array)copied, 0);
        Assert.Same(drawing, copied[0]);
        Assert.True(container.Children.Capacity >= 4);
        Assert.False(container.Children.IsSynchronized);
        Assert.NotNull(container.Children.SyncRoot);
        Assert.Same(container, drawing.Parent);

        DrawingContext context = drawing.RenderOpen();
        context.DrawRectangle(new SolidColorBrush(Colors.Red), null, new Rect(0, 0, 12, 8));
        context.Close();
        Assert.NotNull(drawing.Drawing);
        Assert.Equal(new Rect(0, 0, 12, 8), VisualTreeHelper.GetContentBounds(drawing));
        Assert.False(typeof(DrawingVisual).IsSealed);
        Assert.Equal(typeof(DrawingGroup), typeof(DrawingVisual).GetProperty(nameof(DrawingVisual.Drawing))!.PropertyType);
    }

    [Fact]
    public void DrawingContext_ExposesRetainedAndCompositionCompatibilityOverloads()
    {
        Type type = typeof(DrawingContext);
        Assert.NotNull(type.GetMethod(nameof(DrawingContext.DrawDrawing), [typeof(Drawing)]));
        Assert.NotNull(type.GetMethod(nameof(DrawingContext.PushGuidelineSet), [typeof(GuidelineSet)]));
        Assert.NotNull(type.GetMethod(nameof(DrawingContext.PushOpacityMask), [typeof(Brush)]));

        MethodInfo disposeCore = Assert.IsAssignableFrom<MethodInfo>(
            type.GetMethod("DisposeCore", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.True(disposeCore.IsFamily);
        Assert.True(disposeCore.IsVirtual);
    }

    private sealed class ProbeVisual : Visual
    {
        public int DpiChangeCount { get; private set; }

        public void Attach(Visual child) => AddVisualChild(child);

        public void SetCompositionState(
            Geometry clip,
            Brush opacityMask,
            Transform transform,
            DoubleCollection xGuidelines,
            DoubleCollection yGuidelines)
        {
            VisualClip = clip;
            VisualOpacityMask = opacityMask;
            VisualTransform = transform;
            VisualOffset = new Vector(5, 6);
            VisualOpacity = 0.4;
            VisualXSnappingGuidelines = xGuidelines;
            VisualYSnappingGuidelines = yGuidelines;
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            DpiChangeCount++;
            base.OnDpiChanged(oldDpi, newDpi);
        }
    }
}
