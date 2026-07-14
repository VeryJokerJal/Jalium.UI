using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// Regression guard for the DevTools element-picker overlay reporting the wrong
/// text/element size (e.g. "TextBlock 41 × 23" while the text is visibly bigger).
///
/// Root cause: DevToolsOverlay.GetElementBoundsInWindow summed raw VisualBounds and
/// returned the element's UNSCALED layout size (ActualWidth × ActualHeight), ignoring
/// ancestor RenderTransforms. A <see cref="Viewbox"/> scales its child via a
/// ScaleTransform on an inner wrapper, so the child keeps its natural size while
/// rendering larger — the overlay drew a too-small, mis-placed box and label.
///
/// The fix delegates the overlay's bounds to <see cref="UIElement.GetRenderBounds"/>.
/// These tests pin the invariant the fix relies on for the exact control that
/// triggered the report: a Viewbox child's on-screen render bounds reflect the
/// Viewbox scale, while its layout ActualWidth/Height stay natural.
/// </summary>
public class ViewboxRenderBoundsTests
{
    private static void AssertRectEqual(Rect expected, Rect actual, double tol = 1e-4)
    {
        Assert.True(
            Math.Abs(expected.X - actual.X) < tol &&
            Math.Abs(expected.Y - actual.Y) < tol &&
            Math.Abs(expected.Width - actual.Width) < tol &&
            Math.Abs(expected.Height - actual.Height) < tol,
            $"Expected {expected}, got {actual}");
    }

    [Fact]
    public void PublicShape_UsesCanonicalDecoratorContract()
    {
        Assert.Equal(typeof(Decorator), typeof(Viewbox).BaseType);
        Assert.Null(typeof(Viewbox).GetField(
            "ChildProperty",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));

        var childProperty = typeof(Viewbox).GetProperty(
            nameof(Viewbox.Child),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(childProperty);
        Assert.Equal(typeof(Decorator), childProperty.GetMethod?.GetBaseDefinition().DeclaringType);
        Assert.False(new Viewbox().ClipToBounds);

        var stretchMetadata = Assert.IsType<FrameworkPropertyMetadata>(
            Viewbox.StretchProperty.GetMetadata(typeof(Viewbox)));
        var directionMetadata = Assert.IsType<FrameworkPropertyMetadata>(
            Viewbox.StretchDirectionProperty.GetMetadata(typeof(Viewbox)));
        Assert.True(stretchMetadata.AffectsMeasure);
        Assert.True(directionMetadata.AffectsMeasure);
        Assert.NotNull(Viewbox.StretchProperty.ValidateValueCallback);
        Assert.NotNull(Viewbox.StretchDirectionProperty.ValidateValueCallback);
    }

    [Fact]
    public void Child_UsesPrivateVisualHostAndViewboxLogicalParent()
    {
        var first = new Border();
        var second = new Border();
        var viewbox = new ProbeViewbox { Child = first };

        Assert.Same(viewbox, first.Parent);
        Assert.NotNull(first.VisualParent);
        Assert.NotSame(viewbox, first.VisualParent);
        Assert.IsNotType<Border>(viewbox.InternalVisual);

        viewbox.Child = second;

        Assert.Null(first.Parent);
        Assert.Null(first.VisualParent);
        Assert.Same(viewbox, second.Parent);
        Assert.Same(viewbox.InternalVisual, second.VisualParent);
    }

    [Fact]
    public void Child_AcceptsNonFrameworkUiElement()
    {
        var child = new UIElement();
        var viewbox = new Viewbox { Child = child };

        Assert.Same(child, viewbox.Child);
        Assert.NotNull(child.VisualParent);

        viewbox.Child = null;

        Assert.Null(child.VisualParent);
    }

    [Fact]
    public void StretchProperties_RejectUndefinedEnumValues()
    {
        var viewbox = new Viewbox();

        Assert.Throws<ArgumentException>(() => viewbox.Stretch = (Stretch)99);
        Assert.Throws<ArgumentException>(() => viewbox.StretchDirection = (StretchDirection)99);
    }

    [Fact]
    public void ViewboxChild_RenderBounds_ReflectScale_LayoutSizeStaysNatural()
    {
        // A 50×50 child scaled 2× to fill a 100×100 Viewbox.
        var host = new TestHost();
        var child = new Border { Width = 50, Height = 50 };
        var viewbox = new Viewbox { Stretch = Stretch.Fill, Child = child };
        host.AddChild(viewbox);

        viewbox.Measure(new Size(100, 100));
        viewbox.Arrange(new Rect(0, 0, 100, 100));

        // Layout size is the UNSCALED natural size — what the OLD overlay showed.
        Assert.Equal(50, child.ActualWidth, 3);
        Assert.Equal(50, child.ActualHeight, 3);

        // On-screen render bounds carry the Viewbox's 2× scale — what the overlay
        // (and the size label) now report, matching what the user sees.
        AssertRectEqual(new Rect(0, 0, 100, 100), child.GetRenderBounds());

        // MapLocalRectToScreen (used for the margin/padding bands) follows the same
        // scale: a local 10×10 sub-rect at (10,10) maps to (20,20,20,20).
        AssertRectEqual(new Rect(20, 20, 20, 20), child.MapLocalRectToScreen(new Rect(10, 10, 10, 10)));
    }

    [Fact]
    public void ViewboxChild_UniformScale_HalfSize_RenderBoundsShrink()
    {
        // A 100×100 child scaled 0.5× (DownOnly) to fit a 50×50 Viewbox.
        var host = new TestHost();
        var child = new Border { Width = 100, Height = 100 };
        var viewbox = new Viewbox { Stretch = Stretch.Uniform, Child = child };
        host.AddChild(viewbox);

        viewbox.Measure(new Size(50, 50));
        viewbox.Arrange(new Rect(0, 0, 50, 50));

        Assert.Equal(100, child.ActualWidth, 3);
        AssertRectEqual(new Rect(0, 0, 50, 50), child.GetRenderBounds());
    }

    private sealed class TestHost : FrameworkElement, IWindowHost
    {
        public void AddChild(UIElement child) => AddVisualChild(child);
        public void InvalidateWindow() { }
        public void AddDirtyElement(UIElement element) { }
        public void RequestFullInvalidation() { }
        public void SetNativeCapture() { }
        public void ReleaseNativeCapture() { }
    }

    private sealed class ProbeViewbox : Viewbox
    {
        public Visual? InternalVisual => GetVisualChild(0);
    }
}
