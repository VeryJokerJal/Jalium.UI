namespace Jalium.UI.Documents;

/// <summary>
/// Abstract class that represents a FrameworkElement that decorates a UIElement.
/// </summary>
public abstract class Adorner : FrameworkElement
{
    private readonly UIElement _adornedElement;

    /// <summary>
    /// Initializes a new instance of the Adorner class.
    /// </summary>
    /// <param name="adornedElement">The element to which this adorner is bound.</param>
    protected Adorner(UIElement adornedElement)
    {
        _adornedElement = adornedElement ?? throw new ArgumentNullException(nameof(adornedElement));
    }

    /// <summary>
    /// Gets the UIElement that this adorner is bound to.
    /// </summary>
    public UIElement AdornedElement => _adornedElement;

    /// <summary>
    /// Gets or sets a value that indicates whether clipping of the adorner is enabled.
    /// </summary>
    public bool IsClipEnabled { get; set; }

    /// <summary>
    /// Returns a Transform for the adorner, based on the transform that is currently applied to the adorned element.
    /// </summary>
    /// <param name="transform">The transform that is currently applied to the adorned element.</param>
    /// <returns>A transform to apply to the adorner.</returns>
    public virtual GeneralTransform? GetDesiredTransform(GeneralTransform? transform)
    {
        return transform;
    }

    /// <summary>
    /// Implements any custom measuring behavior for the adorner.
    /// </summary>
    /// <param name="constraint">A size to constrain the adorner to.</param>
    /// <returns>A Size object representing the amount of layout space needed by the adorner.</returns>
    protected override Size MeasureOverride(Size constraint)
    {
        // By default, adorners size to their adorned element
        return _adornedElement.RenderSize;
    }

    /// <summary>
    /// Positions child elements and determines a size for the adorner.
    /// </summary>
    /// <param name="finalSize">The final area within the parent that the adorner should use to arrange itself and its child elements.</param>
    /// <returns>The actual size used by the adorner.</returns>
    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    /// <summary>
    /// Gets the layout clip for this adorner.
    /// </summary>
    /// <returns>The clipping geometry, or null if clipping is not enabled.</returns>
    internal override object? GetLayoutClip()
    {
        if (IsClipEnabled)
        {
            return new Rect(0, 0, RenderSize.Width, RenderSize.Height);
        }
        return null;
    }
}

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
/// Provides a way to apply a MatrixTransform by using a GeneralTransform.
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
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Transforms the specified bounding box.
    /// </summary>
    public override Rect TransformBounds(Rect rect)
    {
        var result = rect;
        foreach (var transform in _transforms)
        {
            result = transform.TransformBounds(result);
        }
        return result;
    }

    /// <summary>
    /// Gets the inverse transform.
    /// </summary>
    public override GeneralTransform? Inverse
    {
        get
        {
            var inverse = new GeneralTransformGroup();
            for (int i = _transforms.Count - 1; i >= 0; i--)
            {
                var inv = _transforms[i].Inverse;
                if (inv == null)
                    return null;
                inverse.Children.Add(inv);
            }
            return inverse;
        }
    }
}

/// <summary>
/// Represents a 2D translation transform.
/// </summary>
public sealed class TranslateTransform2D : GeneralTransform
{
    /// <summary>
    /// Gets or sets the X offset.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Gets or sets the Y offset.
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Initializes a new instance of the TranslateTransform2D class.
    /// </summary>
    public TranslateTransform2D()
    {
    }

    /// <summary>
    /// Initializes a new instance of the TranslateTransform2D class with the specified offset.
    /// </summary>
    /// <param name="x">The X offset.</param>
    /// <param name="y">The Y offset.</param>
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
