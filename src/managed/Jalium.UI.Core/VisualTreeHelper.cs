using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI;

/// <summary>
/// Provides utility methods that perform common tasks involving nodes in a visual tree.
/// </summary>
public static class VisualTreeHelper
{
    /// <summary>
    /// Returns the number of children that a parent visual contains.
    /// </summary>
    public static int GetChildrenCount(DependencyObject reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (reference is Visual visual)
            return visual.InternalVisualChildrenCount;

        return 0;
    }

    /// <summary>
    /// Returns the child visual object at the specified index within the parent.
    /// </summary>
    public static DependencyObject? GetChild(DependencyObject reference, int childIndex)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (reference is Visual visual)
            return visual.InternalGetVisualChild(childIndex);

        throw new ArgumentException("Reference must be a Visual.", nameof(reference));
    }

    /// <summary>
    /// Returns the parent of the visual object.
    /// </summary>
    public static DependencyObject? GetParent(DependencyObject reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (reference is Visual visual)
            return visual.InternalVisualParent;

        return null;
    }

    /// <summary>
    /// Returns the root of the visual tree where this element is connected.
    /// </summary>
    public static DependencyObject? GetRoot(DependencyObject reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (reference is not Visual visual)
            return null;

        while (visual.InternalVisualParent != null)
        {
            visual = visual.InternalVisualParent;
        }

        return visual;
    }

    /// <summary>
    /// Returns the cached bounding box rectangle for the specified visual.
    /// </summary>
    public static Rect GetDescendantBounds(Visual reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var bounds = Rect.Empty;
        GetDescendantBoundsCore(reference, 0, 0, ref bounds);
        return bounds;
    }

    /// <summary>Returns the content bounds for a visual, excluding descendants.</summary>
    public static Rect GetContentBounds(Visual reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference.ContentBoundsCore;
    }

    /// <summary>Returns retained vector content recorded by a visual, when available.</summary>
    public static DrawingGroup? GetDrawing(Visual reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference.DrawingCore;
    }

    /// <summary>Returns the local model bounds owned directly by a 3-D visual.</summary>
    public static Rect3D GetContentBounds(Visual3D reference) =>
        Visual3DTreeHelper.GetContentBounds(reference);

    /// <summary>Returns local bounds enclosing a complete 3-D visual subtree.</summary>
    public static Rect3D GetDescendantBounds(Visual3D reference) =>
        Visual3DTreeHelper.GetDescendantBounds(reference);

    /// <summary>Returns the legacy bitmap effect associated with a visual.</summary>
    [Obsolete("BitmapEffect is deprecated. Use Effect instead.")]
    public static BitmapEffect? GetBitmapEffect(Visual reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference is UIElement element ? element.BitmapEffect : reference.VisualBitmapEffect;
    }

    /// <summary>Returns the legacy bitmap-effect input associated with a visual.</summary>
    [Obsolete("BitmapEffectInput is deprecated. Use Effect instead.")]
    public static BitmapEffectInput? GetBitmapEffectInput(Visual reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference is UIElement element ? element.BitmapEffectInput : reference.VisualBitmapEffectInput;
    }

    /// <summary>Returns the cache mode associated with a visual.</summary>
    public static CacheMode? GetCacheMode(Visual reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference is UIElement element ? element.CacheMode : reference.VisualCacheMode;
    }

    /// <summary>Returns the composition clip associated with a visual.</summary>
    public static Geometry? GetClip(Visual reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference is UIElement element ? element.Clip : reference.VisualClip;
    }

    /// <summary>Returns the DPI scale used to render a visual.</summary>
    public static DpiScale GetDpi(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        if (visual is IWindowHost host)
        {
            double scale = host.DpiScale > 0.0 ? host.DpiScale : 1.0;
            return new DpiScale(scale, scale);
        }

        return visual.DpiScale;
    }

    /// <summary>Returns the edge-rendering mode associated with a visual.</summary>
    public static EdgeMode GetEdgeMode(Visual reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference is UIElement ? RenderOptions.GetEdgeMode(reference) : reference.VisualEdgeMode;
    }

    /// <summary>Returns the shader effect associated with a visual.</summary>
    public static Effect? GetEffect(Visual reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference is UIElement element ? element.Effect : reference.VisualEffect;
    }

    /// <summary>Returns the composition offset associated with a visual.</summary>
    public static Vector GetOffset(Visual reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference is UIElement element)
        {
            return new Vector(
                element.VisualBounds.X + element.RenderOffset.X,
                element.VisualBounds.Y + element.RenderOffset.Y);
        }

        return reference.VisualOffset;
    }

    /// <summary>Returns the composition opacity associated with a visual.</summary>
    public static double GetOpacity(Visual reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference is UIElement element ? element.Opacity : reference.VisualOpacity;
    }

    /// <summary>Returns the opacity mask associated with a visual.</summary>
    public static Brush? GetOpacityMask(Visual reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference is UIElement element ? element.OpacityMask : reference.VisualOpacityMask;
    }

    /// <summary>Returns the composition transform associated with a visual.</summary>
    public static Transform? GetTransform(Visual reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference is UIElement element ? element.RenderTransform : reference.VisualTransform;
    }

    /// <summary>Returns the vertical-edge snapping guidelines associated with a visual.</summary>
    public static DoubleCollection? GetXSnappingGuidelines(Visual reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference.VisualXSnappingGuidelines;
    }

    /// <summary>Returns the horizontal-edge snapping guidelines associated with a visual.</summary>
    public static DoubleCollection? GetYSnappingGuidelines(Visual reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference.VisualYSnappingGuidelines;
    }

    /// <summary>Sets the DPI scale on a root visual and raises its DPI-change hook.</summary>
    public static void SetRootDpi(Visual visual, DpiScale dpiInfo)
    {
        ArgumentNullException.ThrowIfNull(visual);
        visual.SetRootDpi(dpiInfo);
    }

    private static void GetDescendantBoundsCore(Visual parent, double offsetX, double offsetY, ref Rect bounds)
    {
        for (int i = 0; i < parent.InternalVisualChildrenCount; i++)
        {
            var child = parent.InternalGetVisualChild(i);
            if (child == null) continue;

            double childOffsetX = offsetX, childOffsetY = offsetY;
            if (child is UIElement uiChild)
            {
                var cb = uiChild.VisualBounds;
                var ro = uiChild.RenderOffset;
                childOffsetX += cb.X + ro.X;
                childOffsetY += cb.Y + ro.Y;

                var childBounds = new Rect(childOffsetX, childOffsetY, uiChild.RenderSize.Width, uiChild.RenderSize.Height);
                bounds.Union(childBounds);
            }

            // Recursively include descendants with accumulated offset
            GetDescendantBoundsCore(child, childOffsetX, childOffsetY, ref bounds);
        }
    }

    /// <summary>
    /// Returns the topmost visual object of a hit test by specifying a point.
    /// </summary>
    public static HitTestResult? HitTest(Visual reference, Point point)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (reference is UIElement referenceElement &&
            (referenceElement.Visibility != Visibility.Visible || !referenceElement.IsHitTestVisible))
        {
            return null;
        }

        // Walk the visual tree from top to bottom (reverse child order)
        // Use incremental coordinate transform instead of TransformToVisual per child
        for (int i = reference.InternalVisualChildrenCount - 1; i >= 0; i--)
        {
            var child = reference.InternalGetVisualChild(i);
            if (child == null) continue;

            // Compute child offset directly instead of TransformToVisual + Inverse
            var childPoint = point;
            if (child is UIElement uiChild)
            {
                var bounds = uiChild.VisualBounds;
                var ro = uiChild.RenderOffset;
                childPoint = new Point(
                    point.X - bounds.X - ro.X,
                    point.Y - bounds.Y - ro.Y);
            }

            var result = HitTest(child, childPoint);
            if (result != null)
                return result;
        }

        HitTestResult? customResult = reference.HitTestPointCore(new PointHitTestParameters(point));
        if (customResult != null)
        {
            return customResult;
        }

        // Test the reference itself
        if (reference is UIElement element)
        {
            if (point.X >= 0 && point.Y >= 0 &&
                point.X <= element.RenderSize.Width &&
                point.Y <= element.RenderSize.Height)
            {
                return new HitTestResult(reference);
            }
        }

        return null;
    }

    /// <summary>
    /// Initiates a hit test on the specified visual, with caller-defined
    /// HitTestFilterCallback and HitTestResultCallback methods.
    /// </summary>
    public static void HitTest(Visual reference,
        HitTestFilterCallback? filterCallback,
        HitTestResultCallback resultCallback,
        HitTestParameters hitTestParameters)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(resultCallback);
        ArgumentNullException.ThrowIfNull(hitTestParameters);

        if (hitTestParameters is PointHitTestParameters pointParams)
        {
            HitTestWithFilter(reference, filterCallback, resultCallback, pointParams.HitPoint);
        }
        else if (hitTestParameters is GeometryHitTestParameters geometryParams)
        {
            HitTestGeometryWithFilter(reference, filterCallback, resultCallback, geometryParams);
        }
    }

    /// <summary>Performs a callback-based ray hit test against a 3-D visual subtree.</summary>
    public static void HitTest(
        Visual3D reference,
        HitTestFilterCallback? filterCallback,
        HitTestResultCallback resultCallback,
        HitTestParameters3D hitTestParameters) =>
        Visual3DTreeHelper.HitTest(
            reference,
            filterCallback,
            resultCallback,
            hitTestParameters);

    private static HitTestFilterBehavior HitTestWithFilter(Visual visual,
        HitTestFilterCallback? filterCallback,
        HitTestResultCallback resultCallback,
        Point point)
    {
        if (visual is UIElement uiElement &&
            (uiElement.Visibility != Visibility.Visible || !uiElement.IsHitTestVisible))
        {
            return HitTestFilterBehavior.Continue;
        }

        bool skipSelf = false;

        // Apply filter
        if (filterCallback != null)
        {
            var filterResult = filterCallback(visual);
            switch (filterResult)
            {
                case HitTestFilterBehavior.ContinueSkipSelfAndChildren:
                    return HitTestFilterBehavior.Continue;
                case HitTestFilterBehavior.ContinueSkipChildren:
                    // Test self only, skip children
                    if (TestSelf(visual, resultCallback, point) == HitTestResultBehavior.Stop)
                        return HitTestFilterBehavior.Stop;
                    return HitTestFilterBehavior.Continue;
                case HitTestFilterBehavior.ContinueSkipSelf:
                    // Test children only, skip self
                    skipSelf = true;
                    break;
                case HitTestFilterBehavior.Stop:
                    return HitTestFilterBehavior.Stop;
            }
        }

        // Test children (reverse order for z-order)
        // Use incremental coordinate transform instead of TransformToVisual per child
        for (int i = visual.InternalVisualChildrenCount - 1; i >= 0; i--)
        {
            var child = visual.InternalGetVisualChild(i);
            if (child == null) continue;

            // Compute child offset directly
            var childPoint = point;
            if (child is UIElement uiChild)
            {
                var bounds = uiChild.VisualBounds;
                var ro = uiChild.RenderOffset;
                childPoint = new Point(
                    point.X - bounds.X - ro.X,
                    point.Y - bounds.Y - ro.Y);
            }

            var result = HitTestWithFilter(child, filterCallback, resultCallback, childPoint);
            if (result == HitTestFilterBehavior.Stop)
                return HitTestFilterBehavior.Stop;
        }

        // Test self (unless filter said to skip)
        if (!skipSelf)
        {
            if (TestSelf(visual, resultCallback, point) == HitTestResultBehavior.Stop)
                return HitTestFilterBehavior.Stop;
        }

        return HitTestFilterBehavior.Continue;
    }

    private static HitTestResultBehavior TestSelf(Visual visual, HitTestResultCallback resultCallback, Point point)
    {
        HitTestResult? customResult = visual.HitTestPointCore(new PointHitTestParameters(point));
        if (customResult != null)
        {
            return resultCallback(customResult);
        }

        if (visual is UIElement element &&
            element.IsHitTestVisible &&
            element.Visibility == Visibility.Visible &&
            point.X >= 0 && point.Y >= 0 &&
            point.X <= element.RenderSize.Width &&
            point.Y <= element.RenderSize.Height)
        {
            var hitResult = new HitTestResult(visual);
            return resultCallback(hitResult);
        }
        return HitTestResultBehavior.Continue;
    }

    private static HitTestResultBehavior HitTestGeometryWithFilter(
        Visual visual,
        HitTestFilterCallback? filterCallback,
        HitTestResultCallback resultCallback,
        GeometryHitTestParameters parameters)
    {
        HitTestFilterBehavior filterResult = filterCallback?.Invoke(visual) ?? HitTestFilterBehavior.Continue;
        if (filterResult == HitTestFilterBehavior.Stop)
        {
            return HitTestResultBehavior.Stop;
        }

        bool skipChildren = filterResult is HitTestFilterBehavior.ContinueSkipChildren
            or HitTestFilterBehavior.ContinueSkipSelfAndChildren;
        bool skipSelf = filterResult is HitTestFilterBehavior.ContinueSkipSelf
            or HitTestFilterBehavior.ContinueSkipSelfAndChildren;

        if (!skipChildren)
        {
            for (int i = visual.InternalVisualChildrenCount - 1; i >= 0; i--)
            {
                Visual? child = visual.InternalGetVisualChild(i);
                if (child != null &&
                    HitTestGeometryWithFilter(child, filterCallback, resultCallback, parameters) == HitTestResultBehavior.Stop)
                {
                    return HitTestResultBehavior.Stop;
                }
            }
        }

        if (!skipSelf)
        {
            GeometryHitTestResult? result = visual.HitTestGeometryCore(parameters);
            if (result != null && resultCallback(result) == HitTestResultBehavior.Stop)
            {
                return HitTestResultBehavior.Stop;
            }
        }

        return HitTestResultBehavior.Continue;
    }
}
