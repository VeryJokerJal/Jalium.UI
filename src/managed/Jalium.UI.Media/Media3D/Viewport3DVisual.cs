using AnimationHandoffBehavior = Jalium.UI.Media.Animation.HandoffBehavior;
using Jalium.UI.Markup;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Effects;
using System.ComponentModel;

namespace Jalium.UI.Media.Media3D;

/// <summary>Renders 3-D content within a two-dimensional visual.</summary>
public sealed partial class Viewport3DVisual : Visual
{
    /// <summary>Returns the closest 3-D object under a viewport point.</summary>
    public HitTestResult? HitTest(Point point)
    {
        List<RayMeshGeometry3DHitTestResult> results = HitTestMeshes(point, null);
        return results.Count == 0 ? null : results[0];
    }

    /// <summary>Enumerates hit-test results using WPF-compatible callbacks.</summary>
    public void HitTest(
        HitTestFilterCallback? filterCallback,
        HitTestResultCallback resultCallback,
        HitTestParameters hitTestParameters)
    {
        ArgumentNullException.ThrowIfNull(resultCallback);
        ArgumentNullException.ThrowIfNull(hitTestParameters);

        if (hitTestParameters is PointHitTestParameters pointParameters)
        {
            foreach (RayMeshGeometry3DHitTestResult result in HitTestMeshes(pointParameters.HitPoint, filterCallback))
            {
                if (resultCallback(result) == HitTestResultBehavior.Stop)
                {
                    break;
                }
            }

            return;
        }

        if (hitTestParameters is GeometryHitTestParameters geometryParameters)
        {
            GeometryHitTestResult? result = HitTestCore(geometryParameters);
            if (result is not null)
            {
                resultCallback(result);
            }
        }
    }

    protected override GeometryHitTestResult? HitTestCore(GeometryHitTestParameters hitTestParameters)
    {
        ArgumentNullException.ThrowIfNull(hitTestParameters);
        Rect visualBounds = DescendantBounds;
        if (visualBounds.IsEmpty)
        {
            visualBounds = Viewport;
        }

        Rect hitBounds = hitTestParameters.HitGeometry.Bounds;
        Rect intersection = Rect.Intersect(visualBounds, hitBounds);
        if (intersection.IsEmpty)
        {
            return null;
        }

        IntersectionDetail detail;
        if (hitBounds.Contains(visualBounds))
        {
            detail = IntersectionDetail.FullyContains;
        }
        else if (visualBounds.Contains(hitBounds))
        {
            detail = IntersectionDetail.FullyInside;
        }
        else
        {
            detail = IntersectionDetail.Intersects;
        }

        return new GeometryHitTestResult(this, detail);
    }

    private Rect CalculateProjectedBounds(bool includeDescendants)
    {
        if (Children.Count == 0 || Camera is null || Viewport.IsEmpty)
        {
            return Rect.Empty;
        }

        Matrix3D projection = GetProjectionToViewportMatrix();
        Rect result = Rect.Empty;
        foreach (Visual3D child in Children)
        {
            AccumulateVisualBounds(child, Matrix3D.Identity, projection, ref result, includeDescendants);
        }

        if (_clip is not null && !result.IsEmpty)
        {
            result.Intersect(_clip.Bounds);
        }

        return result;
    }

    private static void AccumulateVisualBounds(
        Visual3D visual,
        Matrix3D parentToWorld,
        Matrix3D projection,
        ref Rect result,
        bool includeDescendants)
    {
        Matrix3D localToWorld = (visual.Transform?.Value ?? Matrix3D.Identity) * parentToWorld;
        if (visual.SceneModel is Model3D model)
        {
            Rect3D worldBounds = SceneMath.TransformBounds(model.Bounds, localToWorld);
            Rect projectedBounds = SceneMath.ProjectBounds(worldBounds, projection);
            if (!projectedBounds.IsEmpty &&
                double.IsFinite(projectedBounds.X) &&
                double.IsFinite(projectedBounds.Y) &&
                double.IsFinite(projectedBounds.Width) &&
                double.IsFinite(projectedBounds.Height))
            {
                result.Union(projectedBounds);
            }
        }

        // Viewport3DVisual's content consists of its Visual3D subtree. ContentBounds
        // therefore includes direct children; DescendantBounds additionally follows
        // nested Visual3D containers. A direct child still contributes its own model.
        if (!includeDescendants)
        {
            return;
        }

        foreach (Visual3D child in visual.InternalChildren)
        {
            AccumulateVisualBounds(child, localToWorld, projection, ref result, includeDescendants: true);
        }
    }

    private List<RayMeshGeometry3DHitTestResult> HitTestMeshes(
        Point point,
        HitTestFilterCallback? filterCallback)
    {
        var results = new List<RayMeshGeometry3DHitTestResult>();
        if (!TryCreateWorldRay(point, out Point3D origin, out Vector3D direction))
        {
            return results;
        }

        bool stop = false;
        for (int index = Children.Count - 1; index >= 0 && !stop; index--)
        {
            stop = VisitVisualForHitTest(
                Children[index],
                Matrix3D.Identity,
                origin,
                direction,
                filterCallback,
                results);
        }

        results.Sort(static (left, right) =>
            left.DistanceToRayOrigin.CompareTo(right.DistanceToRayOrigin));
        return results;
    }

    internal IReadOnlyList<RayMeshGeometry3DHitTestResult> HitTestRay(
        Visual3D reference,
        RayHitTestParameters parameters,
        HitTestFilterCallback? filterCallback)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(parameters);

        Vector3D direction = parameters.Direction;
        if (direction.LengthSquared == 0d ||
            !double.IsFinite(direction.X) ||
            !double.IsFinite(direction.Y) ||
            !double.IsFinite(direction.Z))
        {
            return Array.Empty<RayMeshGeometry3DHitTestResult>();
        }

        direction.Normalize();
        var results = new List<RayMeshGeometry3DHitTestResult>();
        VisitVisualForHitTest(
            reference,
            Matrix3D.Identity,
            parameters.Origin,
            direction,
            filterCallback,
            results);
        results.Sort(static (left, right) =>
            left.DistanceToRayOrigin.CompareTo(right.DistanceToRayOrigin));
        return results;
    }

    private bool TryCreateWorldRay(Point point, out Point3D origin, out Vector3D direction)
    {
        origin = default;
        direction = default;
        Rect viewport = Viewport;
        if (Camera is null || viewport.IsEmpty || viewport.Width == 0d || viewport.Height == 0d ||
            !viewport.Contains(point))
        {
            return false;
        }

        if (_clip is not null && !_clip.Bounds.Contains(point))
        {
            return false;
        }

        Matrix3D inverse = GetProjectionToViewportMatrix();
        if (!inverse.HasInverse)
        {
            return false;
        }

        inverse.Invert();
        Point3D nearPoint = inverse.Transform(new Point3D(point.X, point.Y, 0d));
        Point3D farPoint = inverse.Transform(new Point3D(point.X, point.Y, 0.999999d));
        direction = farPoint - nearPoint;
        if (direction.LengthSquared == 0d ||
            !double.IsFinite(direction.X) ||
            !double.IsFinite(direction.Y) ||
            !double.IsFinite(direction.Z))
        {
            return false;
        }

        direction.Normalize();
        origin = nearPoint;
        return true;
    }

    private bool VisitVisualForHitTest(
        Visual3D visual,
        Matrix3D parentToWorld,
        Point3D worldOrigin,
        Vector3D worldDirection,
        HitTestFilterCallback? filterCallback,
        List<RayMeshGeometry3DHitTestResult> results)
    {
        bool skipSelf = false;
        bool skipChildren = false;
        if (filterCallback is not null)
        {
            switch (filterCallback(visual))
            {
                case HitTestFilterBehavior.Stop:
                    return true;
                case HitTestFilterBehavior.ContinueSkipSelfAndChildren:
                    return false;
                case HitTestFilterBehavior.ContinueSkipSelf:
                    skipSelf = true;
                    break;
                case HitTestFilterBehavior.ContinueSkipChildren:
                    skipChildren = true;
                    break;
            }
        }

        Matrix3D localToWorld = (visual.Transform?.Value ?? Matrix3D.Identity) * parentToWorld;
        if (!skipSelf && visual.SceneModel is Model3D model)
        {
            VisitModelForHitTest(
                visual,
                model,
                localToWorld,
                worldOrigin,
                worldDirection,
                results);
        }

        if (!skipChildren)
        {
            for (int index = visual.InternalChildren.Count - 1; index >= 0; index--)
            {
                if (VisitVisualForHitTest(
                    visual.InternalChildren[index],
                    localToWorld,
                    worldOrigin,
                    worldDirection,
                    filterCallback,
                    results))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void VisitModelForHitTest(
        Visual3D visual,
        Model3D model,
        Matrix3D parentToWorld,
        Point3D worldOrigin,
        Vector3D worldDirection,
        List<RayMeshGeometry3DHitTestResult> results)
    {
        Matrix3D localToWorld = (model.Transform?.Value ?? Matrix3D.Identity) * parentToWorld;
        switch (model)
        {
            case GeometryModel3D geometryModel when geometryModel.Geometry is MeshGeometry3D mesh:
                HitTestMesh(
                    visual,
                    geometryModel,
                    mesh,
                    localToWorld,
                    worldOrigin,
                    worldDirection,
                    results);
                break;

            case Model3DGroup group when group.Children is not null:
                foreach (Model3D child in group.Children)
                {
                    VisitModelForHitTest(
                        visual,
                        child,
                        localToWorld,
                        worldOrigin,
                        worldDirection,
                        results);
                }
                break;
        }
    }

    private void HitTestMesh(
        Visual3D visual,
        GeometryModel3D model,
        MeshGeometry3D mesh,
        Matrix3D localToWorld,
        Point3D worldOrigin,
        Vector3D worldDirection,
        List<RayMeshGeometry3DHitTestResult> results)
    {
        if (mesh.Positions is null || mesh.Positions.Count < 3 || !localToWorld.HasInverse)
        {
            return;
        }

        Matrix3D worldToLocal = localToWorld;
        worldToLocal.Invert();
        Point3D localOrigin = worldToLocal.Transform(worldOrigin);
        Vector3D localDirection = worldToLocal.Transform(worldDirection);
        if (localDirection.LengthSquared == 0d)
        {
            return;
        }

        int triangleCount = mesh.TriangleIndices is { Count: >= 3 }
            ? mesh.TriangleIndices.Count / 3
            : mesh.Positions.Count / 3;
        for (int triangle = 0; triangle < triangleCount; triangle++)
        {
            int index1;
            int index2;
            int index3;
            if (mesh.TriangleIndices is { Count: >= 3 } indices)
            {
                index1 = indices[triangle * 3];
                index2 = indices[triangle * 3 + 1];
                index3 = indices[triangle * 3 + 2];
            }
            else
            {
                index1 = triangle * 3;
                index2 = triangle * 3 + 1;
                index3 = triangle * 3 + 2;
            }

            if ((uint)index1 >= (uint)mesh.Positions.Count ||
                (uint)index2 >= (uint)mesh.Positions.Count ||
                (uint)index3 >= (uint)mesh.Positions.Count)
            {
                continue;
            }

            if (!TryIntersectTriangle(
                localOrigin,
                localDirection,
                mesh.Positions[index1],
                mesh.Positions[index2],
                mesh.Positions[index3],
                out double parameter,
                out double weight2,
                out double weight3))
            {
                continue;
            }

            Point3D localPoint = localOrigin + localDirection * parameter;
            Point3D worldPoint = localToWorld.Transform(localPoint);
            Vector3D toHit = worldPoint - worldOrigin;
            double distance = Vector3D.DotProduct(toHit, worldDirection);
            if (distance < 0d)
            {
                continue;
            }

            results.Add(new RayMeshGeometry3DHitTestResult(
                this,
                visual,
                model,
                mesh,
                worldPoint,
                distance,
                index1,
                index2,
                index3,
                new Point(weight2, weight3)));
        }
    }

    private static bool TryIntersectTriangle(
        Point3D origin,
        Vector3D direction,
        Point3D point1,
        Point3D point2,
        Point3D point3,
        out double parameter,
        out double weight2,
        out double weight3)
    {
        const double epsilon = 1e-12d;
        Vector3D edge1 = point2 - point1;
        Vector3D edge2 = point3 - point1;
        Vector3D perpendicular = Vector3D.CrossProduct(direction, edge2);
        double determinant = Vector3D.DotProduct(edge1, perpendicular);
        if (Math.Abs(determinant) < epsilon)
        {
            parameter = weight2 = weight3 = 0d;
            return false;
        }

        double inverseDeterminant = 1d / determinant;
        Vector3D fromPoint1 = origin - point1;
        weight2 = Vector3D.DotProduct(fromPoint1, perpendicular) * inverseDeterminant;
        if (weight2 < 0d || weight2 > 1d)
        {
            parameter = weight3 = 0d;
            return false;
        }

        Vector3D cross = Vector3D.CrossProduct(fromPoint1, edge1);
        weight3 = Vector3D.DotProduct(direction, cross) * inverseDeterminant;
        if (weight3 < 0d || weight2 + weight3 > 1d)
        {
            parameter = 0d;
            return false;
        }

        parameter = Vector3D.DotProduct(edge2, cross) * inverseDeterminant;
        return parameter >= 0d;
    }

    // --- from Visual3D.cs ---
    private readonly Visual3DCollection _children;
    private double _opacity = 1d;
    private Brush? _opacityMask;
    private Geometry? _clip;
    private Jalium.UI.Media.Transform? _transform;
    private Vector _offset;
#pragma warning disable CS0618 // Required for WPF's legacy Viewport3DVisual compatibility surface.
    private BitmapEffect? _bitmapEffect;
    private BitmapEffectInput? _bitmapEffectInput;
#pragma warning restore CS0618

    public static readonly DependencyProperty CameraProperty =
        DependencyProperty.Register(
            nameof(Camera),
            typeof(Camera),
            typeof(Viewport3DVisual),
            new PropertyMetadata(CreateDefaultCamera()));

    public static readonly DependencyProperty ViewportProperty =
        DependencyProperty.Register(
            nameof(Viewport),
            typeof(Rect),
            typeof(Viewport3DVisual),
            new PropertyMetadata(Rect.Empty));

    public Viewport3DVisual()
    {
        _children = new Visual3DCollection(this);
    }

    public Camera? Camera
    {
        get => (Camera?)GetValue(CameraProperty);
        set => SetValue(CameraProperty, value);
    }

    public Rect Viewport
    {
        get => (Rect)(GetValue(ViewportProperty) ?? Rect.Empty);
        set => SetValue(ViewportProperty, value);
    }

    public Visual3DCollection Children => _children;

    public DependencyObject? Parent => VisualParent;

    public double Opacity
    {
        get => _opacity;
        set => _opacity = value;
    }

    public Brush? OpacityMask
    {
        get => _opacityMask;
        set => _opacityMask = value;
    }

    public Geometry? Clip
    {
        get => _clip;
        set => _clip = value;
    }

    public Jalium.UI.Media.Transform? Transform
    {
        get => _transform;
        set => _transform = value;
    }

    public Vector Offset
    {
        get => _offset;
        set => _offset = value;
    }

    [Obsolete("BitmapEffect is deprecated. Use Effect instead.")]
    public BitmapEffect? BitmapEffect
    {
        get => _bitmapEffect;
        set => _bitmapEffect = value;
    }

    [Obsolete("BitmapEffectInput is deprecated. Use Effect instead.")]
    public BitmapEffectInput? BitmapEffectInput
    {
        get => _bitmapEffectInput;
        set => _bitmapEffectInput = value;
    }

    public Rect ContentBounds => CalculateProjectedBounds(includeDescendants: false);

    public Rect DescendantBounds => CalculateProjectedBounds(includeDescendants: true);

    internal Matrix3D GetProjectionToViewportMatrix()
    {
        Rect viewport = Viewport;
        Camera? camera = Camera;
        if (viewport.IsEmpty || viewport.Width == 0d || viewport.Height == 0d || camera is null)
        {
            return Matrix3D.Identity;
        }

        Matrix3D view = camera.GetCombinedViewMatrix();
        Matrix3D projection = camera.GetProjectionMatrix(viewport.Width / viewport.Height);
        Matrix3D viewportMatrix = new(
            viewport.Width / 2d, 0d, 0d, 0d,
            0d, -viewport.Height / 2d, 0d, 0d,
            0d, 0d, 1d, 0d,
            viewport.X + viewport.Width / 2d,
            viewport.Y + viewport.Height / 2d,
            0d,
            1d);
        return view * projection * viewportMatrix;
    }

    private static Camera CreateDefaultCamera()
    {
        var camera = new PerspectiveCamera();
        camera.Freeze();
        return camera;
    }
}