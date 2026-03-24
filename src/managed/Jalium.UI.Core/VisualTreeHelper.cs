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
            return visual.VisualChildrenCount;

        return 0;
    }

    /// <summary>
    /// Returns the child visual object at the specified index within the parent.
    /// </summary>
    public static DependencyObject? GetChild(DependencyObject reference, int childIndex)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (reference is Visual visual)
            return visual.GetVisualChild(childIndex);

        throw new ArgumentException("Reference must be a Visual.", nameof(reference));
    }

    /// <summary>
    /// Returns the parent of the visual object.
    /// </summary>
    public static DependencyObject? GetParent(DependencyObject reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (reference is Visual visual)
            return visual.VisualParent;

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

        while (visual.VisualParent != null)
        {
            visual = visual.VisualParent;
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

    private static void GetDescendantBoundsCore(Visual parent, double offsetX, double offsetY, ref Rect bounds)
    {
        for (int i = 0; i < parent.VisualChildrenCount; i++)
        {
            var child = parent.GetVisualChild(i);
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
        for (int i = reference.VisualChildrenCount - 1; i >= 0; i--)
        {
            var child = reference.GetVisualChild(i);
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
    }

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
        for (int i = visual.VisualChildrenCount - 1; i >= 0; i--)
        {
            var child = visual.GetVisualChild(i);
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
}

/// <summary>
/// Provides the return value for a hit test filter callback.
/// </summary>
public enum HitTestFilterBehavior
{
    /// <summary>Hit test against the current visual and its children.</summary>
    Continue,
    /// <summary>Do not hit test against the current visual or its children.</summary>
    ContinueSkipSelfAndChildren,
    /// <summary>Hit test against the current visual but not its children.</summary>
    ContinueSkipChildren,
    /// <summary>Do not hit test against the current visual but hit test its children.</summary>
    ContinueSkipSelf,
    /// <summary>Stop hit testing.</summary>
    Stop
}

/// <summary>
/// Provides the return value for a hit test result callback.
/// </summary>
public enum HitTestResultBehavior
{
    /// <summary>Stop any further hit testing and return.</summary>
    Stop,
    /// <summary>Continue hit testing against the next visual in the tree.</summary>
    Continue
}

// HitTestResult is defined in Visual.cs

/// <summary>
/// Base class for hit test parameters.
/// </summary>
public abstract class HitTestParameters
{
}

/// <summary>
/// Specifies a point as the parameter to use for hit testing of a visual object.
/// </summary>
public sealed class PointHitTestParameters : HitTestParameters
{
    /// <summary>
    /// Gets the point to use for hit testing.
    /// </summary>
    public Point HitPoint { get; }

    /// <summary>
    /// Initializes a new instance with the specified point.
    /// </summary>
    public PointHitTestParameters(Point hitPoint)
    {
        HitPoint = hitPoint;
    }
}

/// <summary>
/// Specifies a Geometry as the parameter to use for hit testing of a visual object.
/// </summary>
public sealed class GeometryHitTestParameters : HitTestParameters
{
    /// <summary>
    /// Gets the Geometry to use for hit testing.
    /// </summary>
    public object HitGeometry { get; }

    /// <summary>
    /// Initializes a new instance with the specified geometry.
    /// </summary>
    public GeometryHitTestParameters(object hitGeometry)
    {
        HitGeometry = hitGeometry;
    }
}

/// <summary>
/// Delegate for hit test filter callbacks.
/// </summary>
public delegate HitTestFilterBehavior HitTestFilterCallback(DependencyObject potentialHitTestTarget);

/// <summary>
/// Delegate for hit test result callbacks.
/// </summary>
public delegate HitTestResultBehavior HitTestResultCallback(HitTestResult result);
