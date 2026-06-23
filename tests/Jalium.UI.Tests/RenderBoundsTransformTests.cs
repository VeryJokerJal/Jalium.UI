using System.Reflection;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// Numerically pins the row-vector matrix composition in <see cref="UIElement.GetRenderBounds"/> /
/// <c>UIElement.GetRenderMatrix</c> — the transform-aware dirty-region bounds that fix
/// "all Path icons flicker during animation".
///
/// Root cause being guarded: the D3D12 inline partial-present path derives its dirty region
/// (and the FLIP_SEQUENTIAL Present1 dirty rects + clip) from an element's screen AABB. The old
/// <see cref="UIElement.GetScreenBounds"/> ignored <see cref="UIElement.RenderTransform"/>, so a
/// scale/rotate/translate animation rasterized pixels at the transformed position while the dirty
/// region tracked the STATIC layout box → transformed pixels fell outside the clip and the
/// alternate FLIP buffer kept stale pixels → flicker. GetRenderBounds must reproduce EXACTLY where
/// the renderer (Visual.RenderChildVisualInline + RenderTargetDrawingContext) places pixels. If the
/// matrix handedness were wrong the dirty region would merely be wrong-in-a-different-way and icons
/// would still flicker, so these numeric expectations are the gate on the fix.
/// </summary>
public class RenderBoundsTransformTests
{
    private static readonly FieldInfo RenderSizeField =
        typeof(UIElement).GetField("_renderSize", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("UIElement._renderSize field not found");

    // Deterministically place an element: VisualBounds (position) via the public setter, and the
    // RenderSize backing field via reflection (the renderer reads _renderSize for both the local
    // content rect and the RenderTransformOrigin scaling). Avoids running full layout.
    private static void Place(FrameworkElement e, double x, double y, double w, double h)
    {
        RenderSizeField.SetValue(e, new Size(w, h));
        e.SetVisualBounds(new Rect(x, y, w, h));
    }

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
    public void NoTransform_GetRenderBounds_EqualsGetScreenBounds()
    {
        // Fast path: with no RenderTransform anywhere on the chain GetRenderBounds must be
        // byte-identical to GetScreenBounds (so non-transform animations are unaffected).
        var host = new TestHost();
        var el = new TestElement();
        host.AddChild(el);
        Place(el, 50, 60, 100, 40);

        Assert.Equal(el.GetScreenBounds(), el.GetRenderBounds());
    }

    [Fact]
    public void ScaleAboutTopLeft_GrowsFromOrigin()
    {
        // 100x100 at screen (50,50), ScaleTransform(2,2) about origin (0,0):
        // the box grows toward +x/+y from its top-left → (50,50,200,200).
        var host = new TestHost();
        var el = new TestElement();
        host.AddChild(el);
        Place(el, 50, 50, 100, 100);
        el.RenderTransformOrigin = new Point(0, 0);
        el.RenderTransform = new ScaleTransform(2.0, 2.0);

        AssertRectEqual(new Rect(50, 50, 200, 200), el.GetRenderBounds());
    }

    [Fact]
    public void ScaleAboutCenter_GrowsCentered()
    {
        // 100x100 at (50,50) (center 100,100), ScaleTransform(2,2) about center (0.5,0.5):
        // grows symmetrically about the center → (0,0,200,200).
        var host = new TestHost();
        var el = new TestElement();
        host.AddChild(el);
        Place(el, 50, 50, 100, 100);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = new ScaleTransform(2.0, 2.0);

        AssertRectEqual(new Rect(0, 0, 200, 200), el.GetRenderBounds());
    }

    [Fact]
    public void AncestorScale_MovesAndScalesDescendant()
    {
        // Parent ScaleTransform(2,2) about origin (0,0) must move+scale a descendant: a 50x50
        // child at parent-local (10,10) lands at screen (20,20) sized 100x100. This is the case
        // that proves the FULL ancestor chain is composed, not just the element's own transform.
        var host = new TestHost();
        var parent = new TestElement();
        var child = new TestElement();
        host.AddChild(parent);
        parent.AddChild(child);
        Place(parent, 0, 0, 500, 500);
        Place(child, 10, 10, 50, 50);
        parent.RenderTransformOrigin = new Point(0, 0);
        parent.RenderTransform = new ScaleTransform(2.0, 2.0);

        AssertRectEqual(new Rect(20, 20, 100, 100), child.GetRenderBounds());
    }

    [Fact]
    public void Rotate90AboutCenter_SwapsExtents()
    {
        // 100x40 at (50,50) (center 100,70), RotateTransform(90) about center: the AABB swaps to
        // 40x100, still centered at (100,70) → (80,20,40,100). Exercises off-diagonal M12/M21.
        var host = new TestHost();
        var el = new TestElement();
        host.AddChild(el);
        Place(el, 50, 50, 100, 40);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = new RotateTransform(90);

        AssertRectEqual(new Rect(80, 20, 40, 100), el.GetRenderBounds());
    }

    [Fact]
    public void MapLocalRectToScreen_FollowsTransform()
    {
        // A precise local sub-rect must also follow the transform (not a plain translate by the
        // screen origin). Element 100x100 at (50,50), scale 2x about top-left; local (10,10,20,20)
        // maps to screen (50+2*10, 50+2*10, 40, 40) = (70,70,40,40).
        var host = new TestHost();
        var el = new TestElement();
        host.AddChild(el);
        Place(el, 50, 50, 100, 100);
        el.RenderTransformOrigin = new Point(0, 0);
        el.RenderTransform = new ScaleTransform(2.0, 2.0);

        AssertRectEqual(new Rect(70, 70, 40, 40), el.MapLocalRectToScreen(new Rect(10, 10, 20, 20)));
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
