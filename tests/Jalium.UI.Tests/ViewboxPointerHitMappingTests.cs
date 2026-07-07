using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// Pins transform-aware coordinate mapping on the INPUT/hit side, the counterpart to the render-side
/// <see cref="RenderBoundsTransformTests"/> / <see cref="ViewboxRenderBoundsTests"/>.
///
/// Two latent bugs are guarded here:
/// <list type="number">
/// <item>
/// <c>Visual.GetTransformToRoot</c> (engine of <c>Visual.TransformToVisual</c>) and
/// <c>FrameworkElement.TransformToAncestor</c> summed <c>VisualBounds</c> translation only and
/// ignored any <c>RenderTransform</c> on the chain, so mapping coordinates through a <c>Viewbox</c>
/// (or any scale/zoom subtree) dropped the scale. They now compose the same row-vector matrix as
/// <c>UIElement.GetRenderMatrix</c>.
/// </item>
/// <item>
/// <c>PointerPoint.GetPosition</c>/<c>GetIntermediatePoints</c> fed the window/root-space
/// <c>Position</c> through <c>SourceElement.TransformToVisual(relativeTo)</c>, which treats the
/// point as SourceElement-local and adds SourceElement's screen offset (and, inside a Viewbox, its
/// scale). They now invert <c>relativeTo</c>'s render matrix directly (root → relativeTo-local),
/// matching the authoritative <c>MouseEventArgs.GetPosition</c> / <c>TouchDevice.TransformPoint</c>.
/// </item>
/// </list>
/// These are pure geometry tests — no Application/theme/Dispatcher — so they need no
/// <c>[Collection("Application")]</c>.
/// </summary>
public class ViewboxPointerHitMappingTests
{
    private static readonly FieldInfo RenderSizeField =
        typeof(UIElement).GetField("_renderSize", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("UIElement._renderSize field not found");

    // Place an element without running full layout: set the RenderSize backing field via reflection
    // (the render matrix scales RenderTransformOrigin by RenderSize) and VisualBounds via the setter.
    private static void Place(FrameworkElement e, double x, double y, double w, double h)
    {
        RenderSizeField.SetValue(e, new Size(w, h));
        e.SetVisualBounds(new Rect(x, y, w, h));
    }

    private static void AssertPointEqual(Point expected, Point actual, double tol = 1e-4)
    {
        Assert.True(
            Math.Abs(expected.X - actual.X) < tol && Math.Abs(expected.Y - actual.Y) < tol,
            $"Expected {expected}, got {actual}");
    }

    private static MouseEventArgs MouseAt(Point windowPoint) => new(
        UIElement.MouseMoveEvent,
        windowPoint,
        MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
        MouseButtonState.Released, MouseButtonState.Released,
        ModifierKeys.None, 0);

    private static PointerPoint PointerAt(Point windowPoint, UIElement? source) =>
        new(1, windowPoint, PointerDeviceType.Mouse, false, new PointerPointProperties(), 0)
        {
            SourceElement = source
        };

    // ── Real Viewbox scene (2× scale), matching ViewboxRenderBoundsTests ────────────────────────

    [Fact]
    public void Mouse_GetPosition_InsideViewbox_InvertsScale()
    {
        var (child, _) = BuildViewboxScene();

        // Window centre (50,50) sits over the centre of the 50×50 child rendered at 2× → local (25,25).
        AssertPointEqual(new Point(25, 25), MouseAt(new Point(50, 50)).GetPosition(child));
        AssertPointEqual(new Point(0, 0), MouseAt(new Point(0, 0)).GetPosition(child));
        AssertPointEqual(new Point(50, 50), MouseAt(new Point(100, 100)).GetPosition(child));
    }

    [Fact]
    public void Pointer_GetPosition_InsideViewbox_InvertsScale()
    {
        var (child, _) = BuildViewboxScene();

        // relativeTo == SourceElement: the OLD code early-returned the raw root Position (50,50),
        // scale-blind. The fix maps root → child-local through the Viewbox's 2× scale → (25,25).
        var pp = PointerAt(new Point(50, 50), source: child);
        AssertPointEqual(new Point(25, 25), pp.GetPosition(child));
    }

    [Fact]
    public void Pointer_And_Mouse_Agree_InsideViewbox()
    {
        var (child, _) = BuildViewboxScene();

        var window = new Point(70, 40);
        var mouse = MouseAt(window).GetPosition(child);
        var pointer = PointerAt(window, source: child).GetPosition(child);

        AssertPointEqual(new Point(35, 20), mouse);   // (70/2, 40/2)
        AssertPointEqual(mouse, pointer);             // pointer path now converges on the mouse path
    }

    [Fact]
    public void Pointer_GetIntermediatePoints_InsideViewbox_InvertsScale()
    {
        var (child, _) = BuildViewboxScene();

        var pp = PointerAt(new Point(0, 0), source: child);
        var args = new PointerMoveEventArgs(pp, ModifierKeys.None, 0)
        {
            StylusPoints = new StylusPointCollection(new[]
            {
                new StylusPoint(20, 20, 0.5f),
                new StylusPoint(80, 60, 0.5f),
            })
        };

        var pts = args.GetIntermediatePoints(child);
        Assert.Equal(2, pts.Count);
        AssertPointEqual(new Point(10, 10), pts[0].Position);   // (20/2, 20/2)
        AssertPointEqual(new Point(40, 30), pts[1].Position);   // (80/2, 60/2)
    }

    // ── Pre-existing translation bug (no transform) ─────────────────────────────────────────────

    [Fact]
    public void Pointer_GetPosition_RootedOffsetSource_MapsToRelativeToLocal()
    {
        // Regression for the +SourceElement-offset bug: Position is root-space, so the answer must be
        // root → target-local = (100-30, 100-40) = (70,60), regardless of where SourceElement sits.
        // The OLD SourceElement.TransformToVisual(target).Transform(Position) returned
        // Position + srcOffset - targetOffset = (100+10-30, 100+20-40) = (80,80).
        var host = new TestHost();
        var src = new TestElement();
        var target = new TestElement();
        host.AddChild(src);
        host.AddChild(target);
        Place(src, 10, 20, 50, 50);
        Place(target, 30, 40, 50, 50);

        var pp = PointerAt(new Point(100, 100), source: src);
        AssertPointEqual(new Point(70, 60), pp.GetPosition(target));
    }

    // ── TransformToVisual / GetTransformToRoot ──────────────────────────────────────────────────

    [Fact]
    public void TransformToVisual_ToRoot_FollowsAncestorScale()
    {
        // parent Scale(2,2) about origin; child at parent-local (10,10). child-local (0,0) → root
        // (20,20); child-local (5,5) → root (30,30); and the inverse maps root (40,40) → (10,10).
        var host = new TestHost();
        var parent = new TestElement();
        var child = new TestElement();
        host.AddChild(parent);
        parent.AddChild(child);
        Place(parent, 0, 0, 500, 500);
        Place(child, 10, 10, 50, 50);
        parent.RenderTransformOrigin = new Point(0, 0);
        parent.RenderTransform = new ScaleTransform(2.0, 2.0);

        var toRoot = child.TransformToVisual(null);
        Assert.NotNull(toRoot);
        AssertPointEqual(new Point(20, 20), toRoot!.Transform(new Point(0, 0)));
        AssertPointEqual(new Point(30, 30), toRoot.Transform(new Point(5, 5)));

        var rootToChild = toRoot.Inverse;
        Assert.NotNull(rootToChild);
        AssertPointEqual(new Point(10, 10), rootToChild!.Transform(new Point(40, 40)));
    }

    [Fact]
    public void TransformToVisual_BetweenSiblings_UnderScale()
    {
        // Two children under a 2× scaled parent. a at (10,10), b at (40,40). A point at a-local
        // (0,0) is root (20,20) → b-local ((20-80)/2, (20-80)/2) = (-30,-30).
        var host = new TestHost();
        var parent = new TestElement();
        var a = new TestElement();
        var b = new TestElement();
        host.AddChild(parent);
        parent.AddChild(a);
        parent.AddChild(b);
        Place(parent, 0, 0, 500, 500);
        Place(a, 10, 10, 20, 20);
        Place(b, 40, 40, 20, 20);
        parent.RenderTransformOrigin = new Point(0, 0);
        parent.RenderTransform = new ScaleTransform(2.0, 2.0);

        var aToB = a.TransformToVisual(b);
        Assert.NotNull(aToB);
        AssertPointEqual(new Point(-30, -30), aToB!.Transform(new Point(0, 0)));
    }

    // ── TransformToAncestor ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void TransformToAncestor_FollowsScaleBetweenElementAndAncestor()
    {
        // A scale sits on `mid`, BETWEEN child and the `outer` ancestor, so it participates:
        // child origin → outer-local = T(10,10)*Scale(2) applied to (0,0) = (20,20).
        var host = new TestHost();
        var outer = new TestElement();
        var mid = new TestElement();
        var child = new TestElement();
        host.AddChild(outer);
        outer.AddChild(mid);
        mid.AddChild(child);
        Place(outer, 0, 0, 500, 500);
        Place(mid, 0, 0, 500, 500);
        Place(child, 10, 10, 50, 50);
        mid.RenderTransformOrigin = new Point(0, 0);
        mid.RenderTransform = new ScaleTransform(2.0, 2.0);

        AssertPointEqual(new Point(20, 20), child.TransformToAncestor(outer));
    }

    [Fact]
    public void TransformToAncestor_NoTransform_StillSumsVisualBounds()
    {
        // No RenderTransform on the chain → identical to the historical VisualBounds sum.
        var host = new TestHost();
        var a = new TestElement();
        var b = new TestElement();
        host.AddChild(a);
        a.AddChild(b);
        Place(a, 5, 5, 100, 100);
        Place(b, 10, 10, 50, 50);

        AssertPointEqual(new Point(10, 10), b.TransformToAncestor(a));      // relative to a (a excluded)
        AssertPointEqual(new Point(15, 15), b.TransformToAncestor(null));   // relative to root
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────────────────────

    private static (Border child, Viewbox viewbox) BuildViewboxScene()
    {
        // 50×50 child scaled 2× to fill a 100×100 Viewbox at the window origin.
        var host = new TestHost();
        var child = new Border { Width = 50, Height = 50 };
        var viewbox = new Viewbox { Stretch = Stretch.Fill, Child = child };
        host.AddChild(viewbox);
        viewbox.Measure(new Size(100, 100));
        viewbox.Arrange(new Rect(0, 0, 100, 100));
        return (child, viewbox);
    }

    private sealed class TestElement : FrameworkElement
    {
        public void AddChild(UIElement child) => AddVisualChild(child);
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
}
