using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Jalium.UI;

/// <summary>
/// Restricts an attached property to objects whose type exposes a specified
/// non-default attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AttachedPropertyBrowsableWhenAttributePresentAttribute : AttachedPropertyBrowsableAttribute
{
    /// <summary>
    /// Initializes a new instance for <paramref name="attributeType"/>.
    /// </summary>
    public AttachedPropertyBrowsableWhenAttributePresentAttribute(Type attributeType)
    {
        ArgumentNullException.ThrowIfNull(attributeType);
        AttributeType = attributeType;
    }

    /// <summary>
    /// Gets the attribute type that enables browsing.
    /// </summary>
    public Type AttributeType { get; }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is AttachedPropertyBrowsableWhenAttributePresentAttribute other
            && AttributeType == other.AttributeType;
    }

    /// <inheritdoc />
    public override int GetHashCode() => AttributeType.GetHashCode();

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Attached-property browsing is metadata-driven; referenced attribute metadata is retained with its declaring type.")]
    internal override bool IsBrowsable(DependencyObject d, DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(dp);

        foreach (Attribute attribute in TypeDescriptor.GetAttributes(d))
        {
            if (AttributeType.IsInstanceOfType(attribute))
            {
                return !attribute.IsDefaultAttribute();
            }
        }

        return false;
    }
}
