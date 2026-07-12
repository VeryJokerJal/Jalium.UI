using System.Diagnostics.CodeAnalysis;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media;

/// <summary>
/// Defines a potentially non-affine mapping between two-dimensional coordinate spaces.
/// </summary>
public abstract partial class GeneralTransform : Animatable, IFormattable
{
    /// <summary>Gets the inverse transform, or <see langword="null"/> when no inverse exists.</summary>
    public abstract GeneralTransform? Inverse { get; }

    /// <summary>Transforms a point or throws when the point cannot be mapped.</summary>
    public virtual Point Transform(Point inPoint)
    {
        if (!TryTransform(inPoint, out Point result))
        {
            throw new InvalidOperationException("The point could not be transformed.");
        }

        return result;
    }

    /// <summary>Attempts to transform a point.</summary>
    public abstract bool TryTransform(Point inPoint, out Point result);

    /// <summary>Returns an axis-aligned bound enclosing the transformed rectangle.</summary>
    public virtual Rect TransformBounds(Rect rect)
    {
        if (rect.IsEmpty)
        {
            return Rect.Empty;
        }

        Point topLeft = Transform(new Point(rect.X, rect.Y));
        Point topRight = Transform(new Point(rect.X + rect.Width, rect.Y));
        Point bottomLeft = Transform(new Point(rect.X, rect.Y + rect.Height));
        Point bottomRight = Transform(new Point(rect.X + rect.Width, rect.Y + rect.Height));

        double left = Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomLeft.X, bottomRight.X));
        double top = Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomLeft.Y, bottomRight.Y));
        double right = Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomLeft.X, bottomRight.X));
        double bottom = Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomLeft.Y, bottomRight.Y));
        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>Creates a modifiable clone of this transform.</summary>
    public new GeneralTransform Clone() => (GeneralTransform)base.Clone();

    /// <summary>Creates a modifiable clone using current values.</summary>
    public new GeneralTransform CloneCurrentValue() => (GeneralTransform)base.CloneCurrentValue();

    /// <summary>Formats this transform using the current culture.</summary>
    public override string ToString() => ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Formats this transform using the supplied culture.</summary>
    public string ToString(IFormatProvider? provider) => GetType().Name;

    string IFormattable.ToString(string? format, IFormatProvider? provider) => ToString(provider);

    /// <inheritdoc />
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Concrete GeneralTransform implementations preserve a parameterless constructor as part of the Freezable cloning contract.")]
    protected override Freezable CreateInstanceCore()
    {
        return (Freezable)(Activator.CreateInstance(GetType(), nonPublic: true) ??
            throw new InvalidOperationException(
                $"GeneralTransform type '{GetType().FullName}' must have a parameterless constructor."));
    }
}
