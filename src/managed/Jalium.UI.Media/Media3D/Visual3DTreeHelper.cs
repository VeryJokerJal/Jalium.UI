namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Shared implementation for the WPF VisualTreeHelper operations over three-dimensional visuals.
/// This source is compiled into Core together with the Media3D contract types.
/// </summary>
public static class Visual3DTreeHelper
{
    /// <summary>Gets the local bounds of the model directly owned by a 3-D visual.</summary>
    public static Rect3D GetContentBounds(Visual3D reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference.SceneModel?.Bounds ?? Rect3D.Empty;
    }

    /// <summary>Gets local bounds containing the visual's model and complete child subtree.</summary>
    public static Rect3D GetDescendantBounds(Visual3D reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return GetDescendantBoundsCore(reference);
    }

    /// <summary>Performs a callback-based ray hit test against a connected 3-D subtree.</summary>
    public static void HitTest(
        Visual3D reference,
        HitTestFilterCallback? filterCallback,
        HitTestResultCallback resultCallback,
        HitTestParameters3D hitTestParameters)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(resultCallback);
        ArgumentNullException.ThrowIfNull(hitTestParameters);

        if (hitTestParameters is not RayHitTestParameters ray)
        {
            throw new NotSupportedException(
                $"Hit-test parameter type '{hitTestParameters.GetType().FullName}' is not supported.");
        }

        Viewport3DVisual? host = FindHost(reference);
        if (host is null)
        {
            throw new InvalidOperationException(
                "The Visual3D must be connected to a Viewport3DVisual before it can be hit tested.");
        }

        foreach (RayMeshGeometry3DHitTestResult result in host.HitTestRay(reference, ray, filterCallback))
        {
            if (resultCallback(result) == HitTestResultBehavior.Stop)
            {
                break;
            }
        }
    }

    private static Rect3D GetDescendantBoundsCore(Visual3D visual)
    {
        Rect3D bounds = GetContentBounds(visual);
        foreach (Visual3D child in visual.InternalChildren)
        {
            Rect3D childBounds = GetDescendantBoundsCore(child);
            if (childBounds.IsEmpty)
            {
                continue;
            }

            childBounds = SceneMath.TransformBounds(
                childBounds,
                child.Transform?.Value ?? Matrix3D.Identity);
            bounds.Union(childBounds);
        }

        return bounds;
    }

    private static Viewport3DVisual? FindHost(Visual3D reference)
    {
        for (DependencyObject? current = reference.Visual3DParent;
             current is not null;
             current = current is Visual3D visual3D ? visual3D.Visual3DParent : null)
        {
            if (current is Viewport3DVisual host)
            {
                return host;
            }
        }

        return null;
    }
}
