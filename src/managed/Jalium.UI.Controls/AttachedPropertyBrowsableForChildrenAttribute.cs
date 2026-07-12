namespace Jalium.UI;

/// <summary>
/// Makes an attached property browsable for logical children of the property owner type.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AttachedPropertyBrowsableForChildrenAttribute : AttachedPropertyBrowsableAttribute
{
    /// <summary>Gets or sets whether every descendant is considered.</summary>
    public bool IncludeDescendants { get; set; }

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is AttachedPropertyBrowsableForChildrenAttribute other &&
        IncludeDescendants == other.IncludeDescendants;

    /// <inheritdoc />
    public override int GetHashCode() => IncludeDescendants.GetHashCode();

    internal override bool IsBrowsable(DependencyObject d, DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(dp);

        DependencyObject? current = d;
        do
        {
            current = LogicalTreeHelper.GetParent(current);
            if (current is not null && dp.OwnerType.IsInstanceOfType(current))
            {
                return true;
            }
        }
        while (IncludeDescendants && current is not null);

        return false;
    }
}
