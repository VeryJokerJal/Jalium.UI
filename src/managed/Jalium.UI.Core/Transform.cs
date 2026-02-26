namespace Jalium.UI;

/// <summary>
/// Represents a transform that can be used to transform geometry from one coordinate space to another.
/// </summary>
public abstract class GeneralTransform
{
    /// <summary>
    /// Transforms the specified point and returns the result.
    /// </summary>
    /// <param name="inPoint">The point to transform.</param>
    /// <returns>The transformed point.</returns>
    public abstract Point Transform(Point inPoint);

    /// <summary>
    /// Attempts to transform the specified point and returns a value that indicates whether the transformation was successful.
    /// </summary>
    /// <param name="inPoint">The point to transform.</param>
    /// <param name="result">The result of transforming inPoint.</param>
    /// <returns>true if inPoint was transformed; otherwise, false.</returns>
    public abstract bool TryTransform(Point inPoint, out Point result);

    /// <summary>
    /// When overridden in a derived class, transforms the specified bounding box and returns an axis-aligned bounding box that is exactly large enough to contain it.
    /// </summary>
    /// <param name="rect">The bounding box to transform.</param>
    /// <returns>The smallest axis-aligned bounding box possible that contains the transformed rect.</returns>
    public abstract Rect TransformBounds(Rect rect);

    /// <summary>
    /// Gets the inverse transform of this instance, if possible.
    /// </summary>
    public abstract GeneralTransform? Inverse { get; }
}

/// <summary>
/// Provides a way to group multiple transforms.
/// </summary>
public sealed class GeneralTransformGroup : GeneralTransform
{
    private readonly List<GeneralTransform> _transforms = new();

    /// <summary>
    /// Gets the collection of transforms.
    /// </summary>
    public IList<GeneralTransform> Children => _transforms;

    /// <summary>
    /// Transforms the specified point.
    /// </summary>
    public override Point Transform(Point inPoint)
    {
        var result = inPoint;
        foreach (var transform in _transforms)
        {
            result = transform.Transform(result);
        }
        return result;
    }

    /// <summary>
    /// Attempts to transform the specified point.
    /// </summary>
    public override bool TryTransform(Point inPoint, out Point result)
    {
        result = inPoint;
        foreach (var transform in _transforms)
        {
            if (!transform.TryTransform(result, out result))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Transforms the specified bounding box.
    /// </summary>
    public override Rect TransformBounds(Rect rect)
    {
        foreach (var transform in _transforms)
        {
            rect = transform.TransformBounds(rect);
        }
        return rect;
    }

    /// <summary>
    /// Gets the inverse transform.
    /// </summary>
    public override GeneralTransform? Inverse
    {
        get
        {
            var group = new GeneralTransformGroup();
            // Add inverses in reverse order
            for (int i = _transforms.Count - 1; i >= 0; i--)
            {
                var inverse = _transforms[i].Inverse;
                if (inverse == null)
                    return null;
                group._transforms.Add(inverse);
            }
            return group;
        }
    }
}

/// <summary>
/// Represents a 2D translation transform.
/// </summary>
public sealed class TranslateTransform2D : GeneralTransform
{
    /// <summary>
    /// Gets the X offset.
    /// </summary>
    public double X { get; }

    /// <summary>
    /// Gets the Y offset.
    /// </summary>
    public double Y { get; }

    /// <summary>
    /// Initializes a new instance of the TranslateTransform2D class.
    /// </summary>
    public TranslateTransform2D(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    /// Transforms the specified point.
    /// </summary>
    public override Point Transform(Point inPoint)
    {
        return new Point(inPoint.X + X, inPoint.Y + Y);
    }

    /// <summary>
    /// Attempts to transform the specified point.
    /// </summary>
    public override bool TryTransform(Point inPoint, out Point result)
    {
        result = Transform(inPoint);
        return true;
    }

    /// <summary>
    /// Transforms the specified bounding box.
    /// </summary>
    public override Rect TransformBounds(Rect rect)
    {
        return new Rect(rect.X + X, rect.Y + Y, rect.Width, rect.Height);
    }

    /// <summary>
    /// Gets the inverse transform.
    /// </summary>
    public override GeneralTransform Inverse => new TranslateTransform2D(-X, -Y);
}
