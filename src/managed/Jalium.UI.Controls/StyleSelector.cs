namespace Jalium.UI.Controls;

/// <summary>
/// Provides a way to apply styles based on custom logic.
/// </summary>
public class StyleSelector
{
    /// <summary>
    /// When overridden in a derived class, returns a Style based on custom logic.
    /// </summary>
    /// <param name="item">The content.</param>
    /// <param name="container">The element to which the style will be applied.</param>
    /// <returns>An application-specific style to apply, or null.</returns>
    public virtual Style? SelectStyle(object item, DependencyObject container)
    {
        return null;
    }
}
