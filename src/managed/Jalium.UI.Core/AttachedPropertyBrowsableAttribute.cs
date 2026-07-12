namespace Jalium.UI;

/// <summary>
/// Provides the common contract used to decide whether an attached dependency
/// property is browsable for a particular object.
/// </summary>
public abstract class AttachedPropertyBrowsableAttribute : Attribute
{
    /// <summary>
    /// Gets whether results from multiple attributes of the same type are
    /// combined with a logical OR.
    /// </summary>
    internal virtual bool UnionResults => false;

    /// <summary>
    /// Determines whether <paramref name="dp"/> is browsable on
    /// <paramref name="d"/>.
    /// </summary>
    internal abstract bool IsBrowsable(DependencyObject d, DependencyProperty dp);
}
