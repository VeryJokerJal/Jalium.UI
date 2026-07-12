using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media.Media3D;

public abstract class GeneralTransform3D : Animatable, IFormattable
{
    public abstract GeneralTransform3D? Inverse { get; }

    public Point3D Transform(Point3D point)
    {
        if (!TryTransform(point, out Point3D result))
            throw new InvalidOperationException("The point could not be transformed.");
        return result;
    }

    public abstract bool TryTransform(Point3D inPoint, out Point3D result);
    public abstract Rect3D TransformBounds(Rect3D rect);

    public new GeneralTransform3D Clone() => (GeneralTransform3D)base.Clone();
    public new GeneralTransform3D CloneCurrentValue() => (GeneralTransform3D)base.CloneCurrentValue();
    public override string ToString() => GetType().FullName ?? GetType().Name;
    public string ToString(IFormatProvider? provider) => ToString();
    string IFormattable.ToString(string? format, IFormatProvider? provider) => ToString(provider);
}

public sealed class GeneralTransform3DGroup : GeneralTransform3D
{
    public static readonly DependencyProperty ChildrenProperty = DependencyProperty.Register(
        nameof(Children),
        typeof(GeneralTransform3DCollection),
        typeof(GeneralTransform3DGroup),
        new PropertyMetadata(null));

    public GeneralTransform3DGroup() => Children = new GeneralTransform3DCollection();

    public GeneralTransform3DCollection Children
    {
        get => (GeneralTransform3DCollection)GetValue(ChildrenProperty)!;
        set => SetValue(ChildrenProperty, value);
    }

    public override GeneralTransform3D? Inverse
    {
        get
        {
            var inverse = new GeneralTransform3DGroup();
            for (int index = Children.Count - 1; index >= 0; index--)
            {
                GeneralTransform3D? childInverse = Children[index].Inverse;
                if (childInverse is null)
                    return null;
                inverse.Children.Add(childInverse);
            }
            return inverse;
        }
    }

    public override bool TryTransform(Point3D inPoint, out Point3D result)
    {
        result = inPoint;
        foreach (GeneralTransform3D transform in Children)
        {
            if (!transform.TryTransform(result, out result))
                return false;
        }
        return true;
    }

    public override Rect3D TransformBounds(Rect3D rect)
    {
        foreach (GeneralTransform3D transform in Children)
            rect = transform.TransformBounds(rect);
        return rect;
    }

    public new GeneralTransform3DGroup Clone() => (GeneralTransform3DGroup)base.Clone();
    public new GeneralTransform3DGroup CloneCurrentValue() => (GeneralTransform3DGroup)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new GeneralTransform3DGroup();
}

public class GeneralTransform2DTo3D : Freezable
{
    private GeneralTransform? _transform2D;
    private Viewport2DVisual3D? _visual3D;
    private GeneralTransform3D? _transform3D;

    internal GeneralTransform2DTo3D() { }

    internal GeneralTransform2DTo3D(
        GeneralTransform? transform2D,
        Viewport2DVisual3D? visual3D,
        GeneralTransform3D? transform3D)
    {
        _transform2D = transform2D;
        _visual3D = visual3D;
        _transform3D = transform3D;
    }

    internal GeneralTransform2DTo3D(GeneralTransform3D transform3D)
        : this(null, null, transform3D) { }

    public bool TryTransform(Point inPoint, out Point3D result)
    {
        Point point = inPoint;
        if (_transform2D is not null && !_transform2D.TryTransform(inPoint, out point))
        {
            result = default;
            return false;
        }

        // A Viewport2DVisual3D maps its visual plane onto the model's local XY plane.
        // The scene-specific mesh interpolation can refine this projection later; the
        // local-plane mapping remains deterministic and composes with the 3-D transform.
        result = new Point3D(point.X, point.Y, 0d);
        return _transform3D is null || _transform3D.TryTransform(result, out result);
    }

    public Point3D Transform(Point point)
    {
        if (!TryTransform(point, out Point3D result))
            throw new InvalidOperationException("The point could not be transformed into three-dimensional space.");
        return result;
    }

    protected override Freezable CreateInstanceCore() => new GeneralTransform2DTo3D();

    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        CopyFrom((GeneralTransform2DTo3D)sourceFreezable, currentValue: false, frozen: false);
    }

    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        CopyFrom((GeneralTransform2DTo3D)sourceFreezable, currentValue: true, frozen: false);
    }

    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        CopyFrom((GeneralTransform2DTo3D)sourceFreezable, currentValue: false, frozen: true);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        CopyFrom((GeneralTransform2DTo3D)sourceFreezable, currentValue: true, frozen: true);
    }

    private void CopyFrom(GeneralTransform2DTo3D source, bool currentValue, bool frozen)
    {
        _transform2D = CloneTransform(source._transform2D, currentValue, frozen);
        _transform3D = CloneTransform(source._transform3D, currentValue, frozen);
        _visual3D = source._visual3D;
    }

    private static T? CloneTransform<T>(T? transform, bool currentValue, bool frozen)
        where T : Freezable
    {
        if (transform is null)
            return null;
        return (T)(frozen
            ? currentValue ? transform.GetCurrentValueAsFrozen() : transform.GetAsFrozen()
            : currentValue ? transform.CloneCurrentValue() : transform.Clone());
    }
}

public class GeneralTransform3DTo2D : Freezable
{
    private Matrix3D _matrix;
    private GeneralTransform? _transform2D;

    internal GeneralTransform3DTo2D()
    {
        _matrix = Matrix3D.Identity;
    }

    internal GeneralTransform3DTo2D(Matrix3D matrix)
        : this(matrix, null) { }

    internal GeneralTransform3DTo2D(Matrix3D matrix, GeneralTransform? transform2D)
    {
        _matrix = matrix;
        _transform2D = transform2D;
    }

    public bool TryTransform(Point3D inPoint, out Point result)
    {
        Point3D projected = _matrix.Transform(inPoint);
        result = new Point(projected.X, projected.Y);
        return _transform2D is null || _transform2D.TryTransform(result, out result);
    }

    public Point Transform(Point3D point)
    {
        if (!TryTransform(point, out Point result))
            throw new InvalidOperationException("The point could not be transformed into two-dimensional space.");
        return result;
    }

    public Rect TransformBounds(Rect3D rect3D)
    {
        if (rect3D.IsEmpty)
            return Rect.Empty;

        double x0 = rect3D.X;
        double y0 = rect3D.Y;
        double z0 = rect3D.Z;
        double x1 = x0 + rect3D.SizeX;
        double y1 = y0 + rect3D.SizeY;
        double z1 = z0 + rect3D.SizeZ;
        Point[] corners =
        [
            Transform(new Point3D(x0, y0, z0)),
            Transform(new Point3D(x1, y0, z0)),
            Transform(new Point3D(x0, y1, z0)),
            Transform(new Point3D(x1, y1, z0)),
            Transform(new Point3D(x0, y0, z1)),
            Transform(new Point3D(x1, y0, z1)),
            Transform(new Point3D(x0, y1, z1)),
            Transform(new Point3D(x1, y1, z1)),
        ];

        double left = corners.Min(point => point.X);
        double top = corners.Min(point => point.Y);
        double right = corners.Max(point => point.X);
        double bottom = corners.Max(point => point.Y);
        return new Rect(left, top, right - left, bottom - top);
    }

    protected override Freezable CreateInstanceCore() => new GeneralTransform3DTo2D();

    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        CopyFrom((GeneralTransform3DTo2D)sourceFreezable, currentValue: false, frozen: false);
    }

    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        CopyFrom((GeneralTransform3DTo2D)sourceFreezable, currentValue: true, frozen: false);
    }

    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        CopyFrom((GeneralTransform3DTo2D)sourceFreezable, currentValue: false, frozen: true);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        CopyFrom((GeneralTransform3DTo2D)sourceFreezable, currentValue: true, frozen: true);
    }

    private void CopyFrom(GeneralTransform3DTo2D source, bool currentValue, bool frozen)
    {
        _matrix = source._matrix;
        if (source._transform2D is null)
        {
            _transform2D = null;
            return;
        }

        _transform2D = (GeneralTransform)(frozen
            ? currentValue
                ? source._transform2D.GetCurrentValueAsFrozen()
                : source._transform2D.GetAsFrozen()
            : currentValue
                ? source._transform2D.CloneCurrentValue()
                : source._transform2D.Clone());
    }
}
